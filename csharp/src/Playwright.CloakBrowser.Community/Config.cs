using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Playwright.CloakBrowser.Community
{
    public static class Config
    {
        public const string WRAPPER_VERSION = "0.3.31";
        public const string CHROMIUM_VERSION = "146.0.7680.177.5";

        public static readonly Dictionary<string, string> PlatformChromiumVersions = new Dictionary<string, string>
        {
            { "linux-x64", "146.0.7680.177.5" },
            { "linux-arm64", "146.0.7680.177.3" },
            { "darwin-arm64", "145.0.7632.109.2" },
            { "darwin-x64", "145.0.7632.109.2" },
            { "windows-x64", "146.0.7680.177.5" }
        };

        public static readonly string[] IgnoreDefaultArgs = new[]
        {
            "--enable-automation",
            "--enable-unsafe-swiftshader"
        };

        public static readonly ViewportSize DefaultViewport = new ViewportSize { Width = 1920, Height = 947 };

        public static string GetChromiumVersion()
        {
            var tag = GetPlatformTag();
            return PlatformChromiumVersions.TryGetValue(tag, out var version) ? version : CHROMIUM_VERSION;
        }

        public static string GetPlatformTag()
        {
            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            bool isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
            var arch = RuntimeInformation.ProcessArchitecture;

            if (isWindows && arch == Architecture.X64) return "windows-x64";
            if (isLinux && arch == Architecture.X64) return "linux-x64";
            if (isLinux && arch == Architecture.Arm64) return "linux-arm64";
            if (isMac && arch == Architecture.Arm64) return "darwin-arm64";
            if (isMac && arch == Architecture.X64) return "darwin-x64";

            // Fallback check if win32-x64 is matched under x64 windows (which is win32 in Node)
            if (isWindows) return "windows-x64";

            throw new PlatformNotSupportedException($"Unsupported platform or architecture: {RuntimeInformation.OSDescription} {arch}");
        }

        public static string GetCacheDir()
        {
            var custom = Environment.GetEnvironmentVariable("CLOAKBROWSER_CACHE_DIR");
            if (!string.IsNullOrEmpty(custom)) return custom;

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".cloakbrowser");
        }

        public static string GetBinaryDir(string? version = null)
        {
            return Path.Combine(GetCacheDir(), $"chromium-{version ?? GetChromiumVersion()}");
        }

        public static string GetBinaryPath(string? version = null)
        {
            var binaryDir = GetBinaryDir(version);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return Path.Combine(binaryDir, "Chromium.app", "Contents", "MacOS", "Chromium");
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(binaryDir, "chrome.exe");
            }
            return Path.Combine(binaryDir, "chrome");
        }

        public static void CheckPlatformAvailable()
        {
            if (GetLocalBinaryOverride() != null) return;
            var tag = GetPlatformTag();
            if (!PlatformChromiumVersions.ContainsKey(tag))
            {
                var available = string.Join(", ", PlatformChromiumVersions.Keys);
                throw new PlatformNotSupportedException(
                    $"CloakBrowser — Pre-built binaries are currently only available for: {available}.\n\n" +
                    "To use CloakBrowser now, set CLOAKBROWSER_BINARY_PATH to a local Chromium binary."
                );
            }
        }

        public static string? GetLocalBinaryOverride()
        {
            var val = Environment.GetEnvironmentVariable("CLOAKBROWSER_BINARY_PATH");
            return string.IsNullOrEmpty(val) ? null : val;
        }

        public const string DownloadBaseUrl = "https://cloakbrowser.dev";
        public const string GithubDownloadBaseUrl = "https://github.com/CloakHQ/cloakbrowser/releases/download";

        public static string GetArchiveExt()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".zip" : ".tar.gz";
        }

        public static string GetArchiveName(string? tag = null)
        {
            return $"cloakbrowser-{tag ?? GetPlatformTag()}{GetArchiveExt()}";
        }

        public static string GetDownloadUrl(string? version = null)
        {
            var v = version ?? GetChromiumVersion();
            var baseUrl = Environment.GetEnvironmentVariable("CLOAKBROWSER_DOWNLOAD_URL") ?? DownloadBaseUrl;
            return $"{baseUrl}/chromium-v{v}/{GetArchiveName()}";
        }

        public static string GetFallbackDownloadUrl(string? version = null)
        {
            var v = version ?? GetChromiumVersion();
            return $"{GithubDownloadBaseUrl}/chromium-v{v}/{GetArchiveName()}";
        }

        public static string GetEffectiveVersion()
        {
            var baseVersion = GetChromiumVersion();
            var cacheDir = GetCacheDir();
            foreach (var name in new[] { $"latest_version_{GetPlatformTag()}", "latest_version" })
            {
                var marker = Path.Combine(cacheDir, name);
                try
                {
                    if (File.Exists(marker))
                    {
                        var version = File.ReadAllText(marker).Trim();
                        if (!string.IsNullOrEmpty(version) && VersionNewer(version, baseVersion))
                        {
                            var binary = GetBinaryPath(version);
                            if (File.Exists(binary))
                            {
                                return version;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore
                }
            }
            return baseVersion;
        }

        public static int[] ParseVersion(string v)
        {
            var parts = v.Split('.');
            var result = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (int.TryParse(parts[i], out var num))
                {
                    result[i] = num;
                }
            }
            return result;
        }

        public static bool VersionNewer(string a, string b)
        {
            var va = ParseVersion(a);
            var vb = ParseVersion(b);
            int maxLen = Math.Max(va.Length, vb.Length);
            for (int i = 0; i < maxLen; i++)
            {
                int valA = i < va.Length ? va[i] : 0;
                int valB = i < vb.Length ? vb[i] : 0;
                if (valA > valB) return true;
                if (valA < valB) return false;
            }
            return false;
        }

        public static string[] GetDefaultStealthArgs()
        {
            var random = new Random();
            int seed = random.Next(10000, 100000); // 10000-99999
            bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

            var baseArgs = new List<string>
            {
                "--no-sandbox",
                $"--fingerprint={seed}"
            };

            if (isMac)
            {
                baseArgs.Add("--fingerprint-platform=macos");
            }
            else
            {
                baseArgs.Add("--fingerprint-platform=windows");
            }

            return baseArgs.ToArray();
        }
    }

    public struct ViewportSize
    {
        public int Width { get; set; }
        public int Height { get; set; }
    }
}

