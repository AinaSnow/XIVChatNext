using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace XIVChat_Desktop {
    public static class LocalAssetCache {
        public static readonly string CacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XIVChat", "Cache");
        private static readonly HttpClient HttpClient = new HttpClient {
            Timeout = TimeSpan.FromSeconds(8)
        };

        static LocalAssetCache() {
            try {
                Directory.CreateDirectory(CacheDir);
                if (!HttpClient.DefaultRequestHeaders.Contains("User-Agent")) {
                    HttpClient.DefaultRequestHeaders.Add("User-Agent", "XIVChatDesktop/1.0");
                }
            } catch (Exception ex) {
                Debug.WriteLine($"Failed to init LocalAssetCache dir: {ex.Message}");
            }
        }

        public static async Task<string> GetCachedImageAsync(string subfolder, string filename, params string[] remoteUrls) {
            string targetFolder = Path.Combine(CacheDir, subfolder);
            string localFilePath = Path.Combine(targetFolder, filename);

            try {
                Directory.CreateDirectory(targetFolder);
                if (File.Exists(localFilePath)) {
                    var info = new FileInfo(localFilePath);
                    if (info.Length > 10) {
                        return $"https://cache.local/{subfolder}/{filename}";
                    }
                }
            } catch (Exception ex) {
                Debug.WriteLine($"Error checking local image cache: {ex.Message}");
            }

            foreach (var url in remoteUrls) {
                if (string.IsNullOrEmpty(url)) continue;
                try {
                    var bytes = await HttpClient.GetByteArrayAsync(url);
                    if (bytes != null && bytes.Length > 10) {
                        try {
                            await File.WriteAllBytesAsync(localFilePath, bytes);
                            return $"https://cache.local/{subfolder}/{filename}";
                        } catch (Exception ex) {
                            Debug.WriteLine($"Error saving image to disk cache: {ex.Message}");
                        }
                    }
                } catch {
                    // Try next fallback URL
                }
            }

            // If offline or all fetches failed, return the first remote URL if available
            return remoteUrls.FirstOrDefault() ?? "";
        }

        public static async Task<string?> GetCachedTextAsync(string subfolder, string filename, TimeSpan maxAge, params string[] remoteUrls) {
            string targetFolder = Path.Combine(CacheDir, subfolder);
            string localFilePath = Path.Combine(targetFolder, filename);

            try {
                Directory.CreateDirectory(targetFolder);
                if (File.Exists(localFilePath)) {
                    var info = new FileInfo(localFilePath);
                    if (info.Length > 0 && (DateTime.UtcNow - info.LastWriteTimeUtc) < maxAge) {
                        return await File.ReadAllTextAsync(localFilePath);
                    }
                }
            } catch (Exception ex) {
                Debug.WriteLine($"Error checking local text cache: {ex.Message}");
            }

            foreach (var url in remoteUrls) {
                if (string.IsNullOrEmpty(url)) continue;
                try {
                    string content = await HttpClient.GetStringAsync(url);
                    if (!string.IsNullOrWhiteSpace(content)) {
                        try {
                            await File.WriteAllTextAsync(localFilePath, content);
                        } catch (Exception ex) {
                            Debug.WriteLine($"Error saving text to disk cache: {ex.Message}");
                        }
                        return content;
                    }
                } catch {
                    // Try next fallback URL
                }
            }

            // If fetch failed (e.g. offline), fallback to expired local cache if it exists
            try {
                if (File.Exists(localFilePath)) {
                    return await File.ReadAllTextAsync(localFilePath);
                }
            } catch { }

            return null;
        }
    }
}
