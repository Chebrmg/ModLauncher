using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Launcher
{
    public class ModVersionInfo
    {
        public string mod_name { get; set; } = "";
        public string version { get; set; } = "";
        public string file_name { get; set; } = "";
        public string changelog { get; set; } = "";
        public bool available { get; set; }
        public string sha256 { get; set; } = "";
        public long file_size { get; set; }
    }

    public class ModUpdater
    {
        private readonly string _serverUrl;
        private readonly string _modDir;
        private readonly string _versionFilePath;
        private readonly HttpClient _httpClient;

        public ModUpdater(string serverUrl, string modDir)
        {
            _serverUrl = serverUrl.TrimEnd('/');
            _modDir = modDir;
            _versionFilePath = Path.Combine(modDir, "mod_version.json");
            _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        }

        public async Task<ModVersionInfo?> GetRemoteVersionAsync()
        {
            try
            {
                string url = _serverUrl + "/api/version";
                string json = await _httpClient.GetStringAsync(url);
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
                if (!File.Exists(_versionFilePath))
                    return null;
                string json = File.ReadAllText(_versionFilePath);
                var info = JsonSerializer.Deserialize<ModVersionInfo>(json);
                return info?.version;
            }
            catch
            {
                return null;
            }
        }

        public string? GetLocalSha256()
        {
            try
            {
                if (!File.Exists(_versionFilePath))
                    return null;
                string json = File.ReadAllText(_versionFilePath);
                var info = JsonSerializer.Deserialize<ModVersionInfo>(json);
                return info?.sha256;
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> IsUpdateAvailableAsync()
        {
            var remote = await GetRemoteVersionAsync();
            if (remote == null || !remote.available)
                return false;

            string localVersion = GetLocalVersion() ?? "";
            if (localVersion != remote.version)
                return true;

            // Та же версия — проверяем хеш файла
            string localSha = GetLocalSha256() ?? "";
            if (!string.IsNullOrEmpty(remote.sha256) && localSha != remote.sha256)
                return true;

            return false;
        }

        public async Task<bool> DownloadModAsync(
            IProgress<(long downloaded, long total)>? progress = null,
            CancellationToken ct = default)
        {
            var remote = await GetRemoteVersionAsync();
            if (remote == null || !remote.available)
                return false;

            string downloadUrl = _serverUrl + "/api/download";
            string destPath = Path.Combine(_modDir, remote.file_name);
            string tempPath = destPath + ".tmp";

            try
            {
                using var response = await _httpClient.GetAsync(downloadUrl,
                    HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? remote.file_size;

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

                fileStream.Close();

                // Проверяем хеш скачанного файла
                if (!string.IsNullOrEmpty(remote.sha256))
                {
                    string downloadedHash = ComputeSha256(tempPath);
                    if (downloadedHash != remote.sha256)
                    {
                        File.Delete(tempPath);
                        return false;
                    }
                }

                // Заменяем файл
                if (File.Exists(destPath))
                    File.Delete(destPath);
                File.Move(tempPath, destPath);

                // Сохраняем версию
                SaveLocalVersion(remote);

                return true;
            }
            catch
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                return false;
            }
        }

        private void SaveLocalVersion(ModVersionInfo info)
        {
            try
            {
                string json = JsonSerializer.Serialize(info, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_versionFilePath, json);
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
    }
}
