using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CloakBrowser
{
    public static class Download
    {
        private const int DownloadTimeoutMs = 600000; // 10 minutes
        private const int UpdateCheckIntervalMs = 3600000; // 1 hour

        private static bool _wrapperUpdateChecked = false;
        private static readonly Regex ChecksumLineRegex = new Regex(@"^([a-f0-9]{64})\s+\*?(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static async Task<string> EnsureBinaryAsync()
        {
            var localOverride = Config.GetLocalBinaryOverride();
            if (localOverride != null)
            {
                if (!File.Exists(localOverride) && !Directory.Exists(localOverride))
                {
                    throw new FileNotFoundException($"CLOAKBROWSER_BINARY_PATH set to '{localOverride}' but file does not exist.");
                }
                Console.WriteLine($"[cloakbrowser] Using local binary override: {localOverride}");
                return localOverride;
            }

            Config.CheckPlatformAvailable();

            var effective = Config.GetEffectiveVersion();
            var binaryPath = Config.GetBinaryPath(effective);

            if (File.Exists(binaryPath) && IsExecutable(binaryPath))
            {
                ShowWelcome();
                MaybeTriggerUpdateCheck();
                return binaryPath;
            }

            // Fallback to default platform version if effective version is not downloaded
            var platformVersion = Config.GetChromiumVersion();
            if (effective != platformVersion)
            {
                var fallbackPath = Config.GetBinaryPath();
                if (File.Exists(fallbackPath) && IsExecutable(fallbackPath))
                {
                    MaybeTriggerUpdateCheck();
                    return fallbackPath;
                }
            }

            Console.WriteLine($"[cloakbrowser] Stealth Chromium {platformVersion} not found. Downloading for {Config.GetPlatformTag()}...");
            await DownloadAndExtractAsync(platformVersion);

            var downloadedPath = Config.GetBinaryPath();
            if (!File.Exists(downloadedPath))
            {
                throw new FileNotFoundException(
                    $"Download completed but binary not found at expected path: {downloadedPath}. " +
                    "This may indicate a packaging issue. Please report at https://github.com/CloakHQ/cloakbrowser/issues"
                );
            }

            MaybeTriggerUpdateCheck();
            return downloadedPath;
        }

        public static void ClearCache()
        {
            var cacheDir = Config.GetCacheDir();
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, true);
                Console.WriteLine($"[cloakbrowser] Cache cleared: {cacheDir}");
            }
        }

        public static BinaryInfo GetBinaryInfo()
        {
            var effective = Config.GetEffectiveVersion();
            var binaryPath = Config.GetBinaryPath(effective);
            return new BinaryInfo
            {
                Version = effective,
                Platform = Config.GetPlatformTag(),
                BinaryPath = binaryPath,
                Installed = File.Exists(binaryPath),
                CacheDir = Config.GetBinaryDir(effective),
                DownloadUrl = Config.GetDownloadUrl(effective)
            };
        }

        public static async Task<string?> CheckForUpdateAsync()
        {
            string? latest = await GetLatestChromiumVersionAsync();
            if (latest == null || !Config.VersionNewer(latest, Config.GetChromiumVersion())) return null;

            var binaryDir = Config.GetBinaryDir(latest);
            if (Directory.Exists(binaryDir))
            {
                WriteVersionMarker(latest);
                return latest;
            }

            Console.WriteLine($"[cloakbrowser] Downloading Chromium {latest}...");
            await DownloadAndExtractAsync(latest);
            WriteVersionMarker(latest);
            return latest;
        }

        private static void ShowWelcome()
        {
            var marker = Path.Combine(Config.GetCacheDir(), ".welcome_shown");
            if (File.Exists(marker)) return;

            Console.Error.WriteLine();
            Console.Error.WriteLine("  CloakBrowser — stealth Chromium for automation");
            Console.Error.WriteLine("  https://github.com/CloakHQ/CloakBrowser");
            Console.Error.WriteLine();
            Console.Error.WriteLine("  Issues?  https://github.com/CloakHQ/CloakBrowser/issues");
            Console.Error.WriteLine("  Donate?  https://ko-fi.com/cloakhq");
            Console.Error.WriteLine("  Star us if CloakBrowser helps your project!");
            Console.Error.WriteLine();

            try
            {
                Directory.CreateDirectory(Config.GetCacheDir());
                File.WriteAllText(marker, "");
            }
            catch
            {
                // Non-fatal
            }
        }

        private static async Task DownloadAndExtractAsync(string? version = null)
        {
            string primaryUrl = Config.GetDownloadUrl(version);
            string fallbackUrl = Config.GetFallbackDownloadUrl(version);
            string binaryDir = Config.GetBinaryDir(version);
            string binaryPath = Config.GetBinaryPath(version);

            Directory.CreateDirectory(Path.GetDirectoryName(binaryDir)!);

            string tmpPath = Path.Combine(
                Path.GetDirectoryName(binaryDir)!,
                $"_download_{DateTime.UtcNow.Ticks}{Config.GetArchiveExt()}"
            );

            try
            {
                try
                {
                    await DownloadFileAsync(primaryUrl, tmpPath);
                }
                catch (Exception ex)
                {
                    if (Environment.GetEnvironmentVariable("CLOAKBROWSER_DOWNLOAD_URL") != null)
                    {
                        throw;
                    }
                    Console.WriteLine($"[cloakbrowser] Primary download failed ({ex.Message}), trying GitHub Releases...");
                    await DownloadFileAsync(fallbackUrl, tmpPath);
                }

                if (!string.Equals(Environment.GetEnvironmentVariable("CLOAKBROWSER_SKIP_CHECKSUM"), "true", StringComparison.OrdinalIgnoreCase))
                {
                    await VerifyDownloadChecksumAsync(tmpPath, version);
                }

                await ExtractArchiveAsync(tmpPath, binaryDir, binaryPath);
                ShowWelcome();
            }
            finally
            {
                if (File.Exists(tmpPath))
                {
                    try { File.Delete(tmpPath); } catch { }
                }
            }
        }

        private static async Task VerifyDownloadChecksumAsync(string filePath, string? version)
        {
            var checksums = await FetchChecksumsAsync(version);
            string tarballName = Config.GetArchiveName();

            if (checksums == null)
            {
                Console.WriteLine("[cloakbrowser] SHA256SUMS not available for this release — skipping checksum verification");
                return;
            }

            if (!checksums.TryGetValue(tarballName, out string? expected))
            {
                Console.WriteLine($"[cloakbrowser] SHA256SUMS found but no entry for {tarballName} — skipping verification");
                return;
            }

            await VerifyChecksumAsync(filePath, expected);
        }

        private static async Task<Dictionary<string, string>?> FetchChecksumsAsync(string? version)
        {
            string v = version ?? Config.GetChromiumVersion();
            bool hasCustomUrl = Environment.GetEnvironmentVariable("CLOAKBROWSER_DOWNLOAD_URL") != null;

            var urls = new List<string> { $"{Config.DownloadBaseUrl}/chromium-v{v}/SHA256SUMS" };
            if (!hasCustomUrl)
            {
                urls.Add($"{Config.GithubDownloadBaseUrl}/chromium-v{v}/SHA256SUMS");
            }

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                foreach (var url in urls)
                {
                    try
                    {
                        var resp = await client.GetAsync(url);
                        if (!resp.IsSuccessStatusCode) continue;
                        string text = await resp.Content.ReadAsStringAsync();
                        return ParseChecksums(text);
                    }
                    catch
                    {
                        continue;
                    }
                }
            }
            return null;
        }

        private static Dictionary<string, string> ParseChecksums(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var match = ChecksumLineRegex.Match(line.Trim());
                if (match.Success)
                {
                    result[match.Groups[2].Value] = match.Groups[1].Value.ToLowerInvariant();
                }
            }
            return result;
        }

        private static async Task VerifyChecksumAsync(string filePath, string expectedHash)
        {
            using (var sha256 = SHA256.Create())
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true))
                {
                    byte[] hashBytes = await Task.Run(() => sha256.ComputeHash(stream));
                    var sb = new StringBuilder();
                    foreach (byte b in hashBytes)
                    {
                        sb.Append(b.ToString("x2"));
                    }
                    string actual = sb.ToString();
                    if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new CryptographicException(
                            $"Checksum verification failed!\n" +
                            $"  Expected: {expectedHash}\n" +
                            $"  Got:      {actual}\n" +
                            "  File may be corrupted or tampered with. Please retry or report at https://github.com/CloakHQ/cloakbrowser/issues"
                        );
                    }
                }
            }
            Console.WriteLine("[cloakbrowser] Checksum verified: SHA-256 OK");
        }

        private static async Task DownloadFileAsync(string url, string dest)
        {
            Console.WriteLine($"[cloakbrowser] Downloading from {url}");
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMilliseconds(DownloadTimeoutMs);
                using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    long totalBytes = response.Content.Headers.ContentLength ?? 0;

                    using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        using (var stream = await response.Content.ReadAsStreamAsync())
                        {
                            byte[] buffer = new byte[8192];
                            long totalRead = 0;
                            int read;
                            int lastLoggedPct = -1;

                            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fs.WriteAsync(buffer, 0, read);
                                totalRead += read;

                                if (totalBytes > 0)
                                {
                                    int pct = (int)((double)totalRead / totalBytes * 100);
                                    if (pct >= lastLoggedPct + 10)
                                    {
                                        lastLoggedPct = pct;
                                        int dlMB = (int)(totalRead / (1024 * 1024));
                                        int totalMB = (int)(totalBytes / (1024 * 1024));
                                        Console.WriteLine($"[cloakbrowser] Download progress: {pct}% ({dlMB}/{totalMB} MB)");
                                    }
                                }
                            }
                        }
                    }
                }
            }
            int sizeMB = (int)(new FileInfo(dest).Length / (1024 * 1024));
            Console.WriteLine($"[cloakbrowser] Download complete: {sizeMB} MB");
        }

        private static async Task ExtractArchiveAsync(string archivePath, string destDir, string binaryPath)
        {
            Console.WriteLine($"[cloakbrowser] Extracting to {destDir}");

            if (Directory.Exists(destDir))
            {
                Directory.Delete(destDir, true);
            }
            Directory.CreateDirectory(destDir);

            if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                await ExtractZipAsync(archivePath, destDir);
            }
            else
            {
                await ExtractTarAsync(archivePath, destDir);
            }

            FlattenSingleSubdir(destDir);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && File.Exists(binaryPath))
            {
                // Set executable permission
                SetChmod(binaryPath, "755");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                RemoveQuarantine(destDir);
            }

            if (File.Exists(binaryPath))
            {
                Console.WriteLine($"[cloakbrowser] Binary ready: {binaryPath}");
            }
        }

        private static async Task ExtractZipAsync(string archivePath, string destDir)
        {
            await Task.Delay(500); // ensure file handles release
            await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, destDir));
        }

        private static async Task ExtractTarAsync(string archivePath, string destDir)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new PlatformNotSupportedException("Tar extraction is not natively supported on Windows. Use zip binaries.");
            }

            // Execute "tar -xzf archivePath -C destDir"
            var psi = new ProcessStartInfo
            {
                FileName = "tar",
                Arguments = $"-xzf \"{archivePath}\" -C \"{destDir}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = Process.Start(psi))
            {
                if (process == null) throw new InvalidOperationException("Failed to start tar process.");
                await Task.Run(() => process.WaitForExit(120000));
                if (process.ExitCode != 0)
                {
                    string err = await process.StandardError.ReadToEndAsync();
                    throw new InvalidOperationException($"Tar extraction failed: {err}");
                }
            }
        }

        private static void FlattenSingleSubdir(string destDir)
        {
            var entries = Directory.GetFileSystemEntries(destDir);
            if (entries.Length == 1)
            {
                var subdir = entries[0];
                if (subdir.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return; // Keep app bundles on Mac
                if (Directory.Exists(subdir))
                {
                    foreach (var child in Directory.GetFileSystemEntries(subdir))
                    {
                        var name = Path.GetFileName(child);
                        var target = Path.Combine(destDir, name);
                        if (Directory.Exists(child))
                        {
                            Directory.Move(child, target);
                        }
                        else
                        {
                            File.Move(child, target);
                        }
                    }
                    Directory.Delete(subdir);
                }
            }
        }

        private static void SetChmod(string filePath, string permissions)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"{permissions} \"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var process = Process.Start(psi))
                {
                    process?.WaitForExit(30000);
                }
            }
            catch { }
        }

        private static void RemoveQuarantine(string dirPath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "xattr",
                    Arguments = $"-cr \"{dirPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var process = Process.Start(psi))
                {
                    process?.WaitForExit(30000);
                }
            }
            catch { }
        }

        private static bool IsExecutable(string filePath)
        {
            // On Windows, if File.Exists is true, it is executable
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return true;

            // On Linux/Mac, check if it's executable via system command or assuming it is if it exists
            return File.Exists(filePath);
        }

        private static bool ShouldCheckForUpdate()
        {
            var raw = Environment.GetEnvironmentVariable("CLOAKBROWSER_AUTO_UPDATE");
            if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase)) return false;
            if (Config.GetLocalBinaryOverride() != null) return false;
            if (Environment.GetEnvironmentVariable("CLOAKBROWSER_DOWNLOAD_URL") != null) return false;

            var checkFile = Path.Combine(Config.GetCacheDir(), ".last_update_check");
            try
            {
                if (File.Exists(checkFile))
                {
                    var lastCheckStr = File.ReadAllText(checkFile).Trim();
                    if (long.TryParse(lastCheckStr, out var lastCheck))
                    {
                        var diff = DateTime.UtcNow - new DateTime(lastCheck);
                        if (diff.TotalMilliseconds < UpdateCheckIntervalMs) return false;
                    }
                }
            }
            catch { }
            return true;
        }

        public static async Task<string?> GetLatestChromiumVersionAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("CloakBrowser-DotNet-Wrapper");
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var resp = await client.GetAsync("https://api.github.com/repos/CloakHQ/cloakbrowser/releases?per_page=10");
                    if (!resp.IsSuccessStatusCode) return null;

                    string json = await resp.Content.ReadAsStringAsync();
                    using (var doc = JsonDocument.Parse(json))
                    {
                        string platformTarball = Config.GetArchiveName();
                        foreach (var release in doc.RootElement.EnumerateArray())
                        {
                            string tagName = release.GetProperty("tag_name").GetString() ?? "";
                            bool draft = release.GetProperty("draft").GetBoolean();
                            if (tagName.StartsWith("chromium-v") && !draft)
                            {
                                var assets = release.GetProperty("assets");
                                foreach (var asset in assets.EnumerateArray())
                                {
                                    string name = asset.GetProperty("name").GetString() ?? "";
                                    if (string.Equals(name, platformTarball, StringComparison.OrdinalIgnoreCase))
                                    {
                                        return tagName.Substring("chromium-v".Length);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static void WriteVersionMarker(string version)
        {
            var cacheDir = Config.GetCacheDir();
            Directory.CreateDirectory(cacheDir);
            var marker = Path.Combine(cacheDir, $"latest_version_{Config.GetPlatformTag()}");
            var tmp = $"{marker}.tmp";
            File.WriteAllText(tmp, version);
            if (File.Exists(marker)) File.Delete(marker);
            File.Move(tmp, marker);
        }

        private static void MaybeTriggerUpdateCheck()
        {
            if (!_wrapperUpdateChecked)
            {
                _wrapperUpdateChecked = true;
                // NuGet wrapper update check is skipped for now to avoid registry API reliance
            }

            if (!ShouldCheckForUpdate()) return;

            Task.Run(async () =>
            {
                try
                {
                    var cacheDir = Config.GetCacheDir();
                    Directory.CreateDirectory(cacheDir);
                    File.WriteAllText(Path.Combine(cacheDir, ".last_update_check"), DateTime.UtcNow.Ticks.ToString());

                    var platformVersion = Config.GetChromiumVersion();
                    string? latest = await GetLatestChromiumVersionAsync();
                    if (latest == null || !Config.VersionNewer(latest, platformVersion)) return;

                    if (Directory.Exists(Config.GetBinaryDir(latest)))
                    {
                        WriteVersionMarker(latest);
                        return;
                    }

                    Console.WriteLine($"[cloakbrowser] Newer Chromium available: {latest} (current: {platformVersion}). Downloading in background...");
                    await DownloadAndExtractAsync(latest);
                    WriteVersionMarker(latest);
                    Console.WriteLine($"[cloakbrowser] Background update complete: Chromium {latest} ready. Will use on next launch.");
                }
                catch { }
            });
        }
    }
}
