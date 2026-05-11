using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Launcher
{
    /// <summary>
    /// Информация о версии мода (хранится в mod_update.json на Google Drive и локально).
    /// </summary>
    public class ModVersionInfo
    {
        public string mod_name { get; set; } = "";
        public string version { get; set; } = "";
        public string file_name { get; set; } = "";
        public string download_url { get; set; } = "";
        public string changelog { get; set; } = "";
        public string sha256 { get; set; } = "";
    }

    /// <summary>
    /// Проверяет обновления и скачивает мод с Google Drive (или по любой прямой ссылке).
    /// 
    /// Конфигурация (update_config.json рядом с лаунчером):
    /// {
    ///   "version_url": "https://drive.google.com/uc?export=download&amp;id=FILE_ID"
    /// }
    /// 
    /// На Google Drive лежит файл mod_update.json:
    /// {
    ///   "mod_name": "Chebovka",
    ///   "version": "1.5.2",
    ///   "file_name": "Chebovka1.5.2.pak",
    ///   "download_url": "https://drive.google.com/uc?export=download&amp;id=FILE_ID",
    ///   "changelog": "Что нового",
    ///   "sha256": "abc123..."
    /// }
    /// </summary>
    public class ModUpdater
    {
        private readonly string _versionUrl;
        private readonly string _modDir;
        private readonly string _localVersionPath;
        private readonly HttpClient _httpClient;

        public ModUpdater(string versionUrl, string modDir)
        {
            _versionUrl = versionUrl;
            _modDir = modDir;
            _localVersionPath = Path.Combine(modDir, "mod_version.json");

            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10
            };
            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ModLauncher/1.0");
        }

        /// <summary>
        /// Скачивает mod_update.json с Google Drive и парсит информацию о версии.
        /// </summary>
        public async Task<ModVersionInfo?> GetRemoteVersionAsync()
        {
            try
            {
                string json = await _httpClient.GetStringAsync(_versionUrl);
                return JsonSerializer.Deserialize<ModVersionInfo>(json);
            }
            catch
            {
                return null;
            }
        }

        public string? GetLocalVersion()
        {
            try
            {
                if (!File.Exists(_localVersionPath))
                    return null;
                string json = File.ReadAllText(_localVersionPath);
                var info = JsonSerializer.Deserialize<ModVersionInfo>(json);
                return info?.version;
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> IsUpdateAvailableAsync()
        {
            var remote = await GetRemoteVersionAsync();
            if (remote == null)
                return false;

            string localVersion = GetLocalVersion() ?? "";
            return localVersion != remote.version;
        }

        /// <summary>
        /// Скачивает мод по ссылке из mod_update.json.
        /// Поддерживает Google Drive ссылки (автоматическое подтверждение для больших файлов).
        /// </summary>
        public async Task<(bool success, string message)> DownloadModAsync(
            IProgress<(long downloaded, long total)>? progress = null,
            CancellationToken ct = default)
        {
            var remote = await GetRemoteVersionAsync();
            if (remote == null)
                return (false, "Не удалось получить информацию о версии");

            if (string.IsNullOrEmpty(remote.download_url))
                return (false, "Ссылка на скачивание не указана");

            string destPath = Path.Combine(_modDir, remote.file_name);
            string tempPath = destPath + ".tmp";

            try
            {
                string downloadUrl = ConvertToDirectLink(remote.download_url);

                using var response = await _httpClient.GetAsync(downloadUrl,
                    HttpCompletionOption.ResponseHeadersRead, ct);

                // Google Drive для больших файлов показывает страницу подтверждения
                if (response.Content.Headers.ContentType?.MediaType == "text/html")
                {
                    string html = await response.Content.ReadAsStringAsync(ct);
                    string? confirmUrl = ExtractGDriveConfirmUrl(html, downloadUrl);
                    if (confirmUrl != null)
                    {
                        response.Dispose();
                        var confirmResponse = await _httpClient.GetAsync(confirmUrl,
                            HttpCompletionOption.ResponseHeadersRead, ct);
                        confirmResponse.EnsureSuccessStatusCode();
                        await DownloadStreamToFile(confirmResponse, tempPath, remote.file_size, progress, ct);
                    }
                    else
                    {
                        return (false, "Не удалось подтвердить скачивание с Google Drive");
                    }
                }
                else
                {
                    response.EnsureSuccessStatusCode();
                    await DownloadStreamToFile(response, tempPath, remote.file_size, progress, ct);
                }

                // Проверяем SHA256
                if (!string.IsNullOrEmpty(remote.sha256))
                {
                    string downloadedHash = ComputeSha256(tempPath);
                    if (downloadedHash != remote.sha256)
                    {
                        File.Delete(tempPath);
                        return (false, "Ошибка проверки SHA256 — файл повреждён");
                    }
                }

                // Заменяем файл
                if (File.Exists(destPath))
                    File.Delete(destPath);
                File.Move(tempPath, destPath);

                SaveLocalVersion(remote);

                return (true, $"Мод обновлён до v{remote.version}");
            }
            catch (Exception ex)
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                return (false, "Ошибка скачивания: " + ex.Message);
            }
        }

        private async Task DownloadStreamToFile(
            HttpResponseMessage response, string tempPath, long fallbackSize,
            IProgress<(long downloaded, long total)>? progress, CancellationToken ct)
        {
            long totalBytes = response.Content.Headers.ContentLength ?? fallbackSize;

            using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            using var fileStream = new FileStream(tempPath, FileMode.Create,
                FileAccess.Write, FileShare.None, 65536, true);

            var buffer = new byte[65536];
            long downloaded = 0;

            while (true)
            {
                int bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct);
                if (bytesRead == 0)
                    break;

                await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
                downloaded += bytesRead;
                progress?.Report((downloaded, totalBytes));
            }
        }

        /// <summary>
        /// Конвертирует разные форматы Google Drive ссылок в прямую ссылку на скачивание.
        /// </summary>
        private static string ConvertToDirectLink(string url)
        {
            // https://drive.google.com/file/d/FILE_ID/view → прямая ссылка
            if (url.Contains("drive.google.com/file/d/"))
            {
                int start = url.IndexOf("/d/") + 3;
                int end = url.IndexOf("/", start);
                if (end == -1) end = url.Length;
                string fileId = url.Substring(start, end - start);
                return $"https://drive.google.com/uc?export=download&id={fileId}";
            }

            // https://drive.google.com/open?id=FILE_ID → прямая ссылка
            if (url.Contains("drive.google.com/open?id="))
            {
                int start = url.IndexOf("id=") + 3;
                int end = url.IndexOf("&", start);
                if (end == -1) end = url.Length;
                string fileId = url.Substring(start, end - start);
                return $"https://drive.google.com/uc?export=download&id={fileId}";
            }

            return url;
        }

        /// <summary>
        /// Извлекает ссылку подтверждения для больших файлов Google Drive.
        /// </summary>
        private static string? ExtractGDriveConfirmUrl(string html, string originalUrl)
        {
            // Ищем confirm-токен в HTML
            string searchFor = "confirm=";
            int idx = html.IndexOf(searchFor);
            if (idx == -1)
            {
                searchFor = "id=\"downloadForm\"";
                idx = html.IndexOf(searchFor);
                if (idx == -1)
                    return null;

                // Пробуем найти action URL
                int actionIdx = html.IndexOf("action=\"", idx);
                if (actionIdx != -1)
                {
                    int start = actionIdx + 8;
                    int end = html.IndexOf("\"", start);
                    if (end != -1)
                    {
                        string actionUrl = html.Substring(start, end - start)
                            .Replace("&amp;", "&");
                        return actionUrl;
                    }
                }
                return null;
            }

            // Извлекаем токен
            int tokenStart = idx + searchFor.Length;
            int tokenEnd = html.IndexOfAny(new[] { '&', '"', '\'' }, tokenStart);
            if (tokenEnd == -1) tokenEnd = tokenStart + 10;
            string token = html.Substring(tokenStart, tokenEnd - tokenStart);

            if (originalUrl.Contains("?"))
                return originalUrl + "&confirm=" + token;
            else
                return originalUrl + "?confirm=" + token;
        }

        private void SaveLocalVersion(ModVersionInfo info)
        {
            try
            {
                string json = JsonSerializer.Serialize(info, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_localVersionPath, json);
            }
            catch { }
        }

        private static string ComputeSha256(string filePath)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        public long file_size { get; set; }
    }
}
