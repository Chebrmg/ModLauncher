using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Launcher
{
    public class LauncherVersionInfo
    {
        public string version { get; set; } = "";
        public string download_url { get; set; } = "";
        public string changelog { get; set; } = "";
        public long file_size { get; set; }
    }

    /// <summary>
    /// Проверяет обновления лаунчера и скачивает ZIP с новой версией.
    /// 
    /// На Google Drive лежит launcher_update.json:
    /// {
    ///   "version": "2.0.0",
    ///   "download_url": "https://drive.google.com/file/d/FILE_ID/view",
    ///   "changelog": "Что нового"
    /// }
    /// 
    /// В update_config.json рядом с лаунчером:
    /// {
    ///   "version_url": "...",
    ///   "launcher_version_url": "https://drive.google.com/uc?export=download&id=FILE_ID"
    /// }
    /// </summary>
    public class LauncherUpdater
    {
        public static readonly string CurrentVersion = "1.0.0";

        private readonly string _versionUrl;
        private readonly string _launcherDir;
        private readonly HttpClient _httpClient;

        public LauncherUpdater(string versionUrl, string launcherDir)
        {
            _versionUrl = versionUrl;
            _launcherDir = launcherDir;

            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10
            };
            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ModLauncher/1.0");
        }

        public async Task<LauncherVersionInfo?> GetRemoteVersionAsync()
        {
            try
            {
                string json = await _httpClient.GetStringAsync(_versionUrl);
                return JsonSerializer.Deserialize<LauncherVersionInfo>(json);
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> IsUpdateAvailableAsync()
        {
            var remote = await GetRemoteVersionAsync();
            if (remote == null || string.IsNullOrEmpty(remote.version))
                return false;

            return CompareVersions(remote.version, CurrentVersion) > 0;
        }

        public async Task<(bool success, string message)> DownloadUpdateAsync(
            IProgress<(long downloaded, long total)>? progress = null,
            CancellationToken ct = default)
        {
            var remote = await GetRemoteVersionAsync();
            if (remote == null)
                return (false, "Не удалось получить информацию о версии");

            if (string.IsNullOrEmpty(remote.download_url))
                return (false, "Ссылка на скачивание не указана");

            string zipPath = Path.Combine(_launcherDir, "_launcher_update.zip");

            try
            {
                string downloadUrl = ConvertToDirectLink(remote.download_url);

                using var response = await _httpClient.GetAsync(downloadUrl,
                    HttpCompletionOption.ResponseHeadersRead, ct);

                // Google Drive confirm для больших файлов
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
                        await DownloadStreamToFile(confirmResponse, zipPath, remote.file_size, progress, ct);
                    }
                    else
                    {
                        return (false, "Не удалось подтвердить скачивание с Google Drive");
                    }
                }
                else
                {
                    response.EnsureSuccessStatusCode();
                    await DownloadStreamToFile(response, zipPath, remote.file_size, progress, ct);
                }

                return (true, zipPath);
            }
            catch (Exception ex)
            {
                if (File.Exists(zipPath))
                    File.Delete(zipPath);
                return (false, "Ошибка скачивания: " + ex.Message);
            }
        }

        /// <summary>
        /// Запускает Updater.exe и закрывает лаунчер.
        /// </summary>
        public void LaunchUpdaterAndExit(string zipPath)
        {
            string updaterExe = Path.Combine(_launcherDir, "Updater.exe");
            if (!File.Exists(updaterExe))
            {
                throw new FileNotFoundException("Updater.exe не найден в папке лаунчера");
            }

            int pid = Process.GetCurrentProcess().Id;
            string launcherExe = Path.GetFileName(Process.GetCurrentProcess().MainModule?.FileName ?? "Launcher.exe");

            Process.Start(new ProcessStartInfo
            {
                FileName = updaterExe,
                Arguments = $"--pid {pid} --zip \"{zipPath}\" --target \"{_launcherDir}\" --launcher \"{launcherExe}\"",
                UseShellExecute = true
            });

            Application.Exit();
        }

        private async Task DownloadStreamToFile(
            HttpResponseMessage response, string filePath, long fallbackSize,
            IProgress<(long downloaded, long total)>? progress, CancellationToken ct)
        {
            long totalBytes = response.Content.Headers.ContentLength ?? fallbackSize;

            using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            using var fileStream = new FileStream(filePath, FileMode.Create,
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

        private static string ConvertToDirectLink(string url)
        {
            if (url.Contains("drive.google.com/file/d/"))
            {
                int start = url.IndexOf("/d/") + 3;
                int end = url.IndexOf("/", start);
                if (end == -1) end = url.Length;
                string fileId = url.Substring(start, end - start);
                return $"https://drive.google.com/uc?export=download&id={fileId}";
            }

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

        private static string? ExtractGDriveConfirmUrl(string html, string originalUrl)
        {
            string searchFor = "confirm=";
            int idx = html.IndexOf(searchFor);
            if (idx == -1)
            {
                searchFor = "id=\"downloadForm\"";
                idx = html.IndexOf(searchFor);
                if (idx == -1) return null;

                int actionIdx = html.IndexOf("action=\"", idx);
                if (actionIdx != -1)
                {
                    int start = actionIdx + 8;
                    int end = html.IndexOf("\"", start);
                    if (end != -1)
                        return html.Substring(start, end - start).Replace("&amp;", "&");
                }
                return null;
            }

            int tokenStart = idx + searchFor.Length;
            int tokenEnd = html.IndexOfAny(new[] { '&', '"', '\'' }, tokenStart);
            if (tokenEnd == -1) tokenEnd = tokenStart + 10;
            string token = html.Substring(tokenStart, tokenEnd - tokenStart);

            return originalUrl.Contains("?")
                ? originalUrl + "&confirm=" + token
                : originalUrl + "?confirm=" + token;
        }

        private static int CompareVersions(string a, string b)
        {
            var partsA = a.Split('.');
            var partsB = b.Split('.');
            int len = Math.Max(partsA.Length, partsB.Length);

            for (int i = 0; i < len; i++)
            {
                int numA = i < partsA.Length && int.TryParse(partsA[i], out int va) ? va : 0;
                int numB = i < partsB.Length && int.TryParse(partsB[i], out int vb) ? vb : 0;
                if (numA != numB)
                    return numA.CompareTo(numB);
            }
            return 0;
        }
    }
}
