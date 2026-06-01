using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MaxMind.Db;

namespace Playwright.CloakBrowser.Community
{
    public struct GeoResult
    {
        public string? Timezone { get; set; }
        public string? Locale { get; set; }
        public string? ExitIp { get; set; }
    }

    public static class GeoIp
    {
        private const string GeoipDbUrl = "https://github.com/P3TERX/GeoLite.mmdb/raw/download/GeoLite2-City.mmdb";
        private const string GeoipDbFilename = "GeoLite2-City.mmdb";
        private static readonly TimeSpan GeoipUpdateInterval = TimeSpan.FromDays(30);
        private const int DefaultGeoipTimeoutMs = 5000;

        private static readonly Dictionary<string, string> CountryLocaleMap = new Dictionary<string, string>
        {
            { "US", "en-US" }, { "GB", "en-GB" }, { "AU", "en-AU" }, { "CA", "en-CA" }, { "NZ", "en-NZ" },
            { "IE", "en-IE" }, { "ZA", "en-ZA" }, { "SG", "en-SG" },
            { "DE", "de-DE" }, { "AT", "de-AT" }, { "CH", "de-CH" },
            { "FR", "fr-FR" }, { "BE", "fr-BE" },
            { "ES", "es-ES" }, { "MX", "es-MX" }, { "AR", "es-AR" }, { "CO", "es-CO" }, { "CL", "es-CL" },
            { "BR", "pt-BR" }, { "PT", "pt-PT" },
            { "IT", "it-IT" }, { "NL", "nl-NL" },
            { "JP", "ja-JP" }, { "KR", "ko-KR" }, { "CN", "zh-CN" }, { "TW", "zh-TW" }, { "HK", "zh-HK" },
            { "RU", "ru-RU" }, { "UA", "uk-UA" }, { "PL", "pl-PL" }, { "CZ", "cs-CZ" }, { "RO", "ro-RO" },
            { "IL", "he-IL" }, { "TR", "tr-TR" }, { "SA", "ar-SA" }, { "AE", "ar-AE" }, { "EG", "ar-EG" },
            { "IN", "hi-IN" }, { "ID", "id-ID" }, { "PH", "en-PH" },
            { "TH", "th-TH" }, { "VN", "vi-VN" }, { "MY", "ms-MY" },
            { "SE", "sv-SE" }, { "NO", "nb-NO" }, { "DK", "da-DK" }, { "FI", "fi-FI" },
            { "GR", "el-GR" }, { "HU", "hu-HU" }, { "BG", "bg-BG" }
        };

        private static readonly string[] IpEchoUrls = new[]
        {
            "https://api.ipify.org",
            "https://checkip.amazonaws.com",
            "https://ifconfig.me/ip"
        };

        private static string GetGeoipDir()
        {
            return Path.Combine(Config.GetCacheDir(), "geoip");
        }

        private static async Task<string?> EnsureGeoipDbAsync()
        {
            var dir = GetGeoipDir();
            var dbPath = Path.Combine(dir, GeoipDbFilename);

            if (File.Exists(dbPath))
            {
                MaybeTriggerUpdate(dbPath);
                return dbPath;
            }

            try
            {
                await DownloadGeoipDbAsync(dbPath);
                return dbPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[cloakbrowser] Failed to download GeoIP database: {ex.Message}");
                return null;
            }
        }

        private static async Task DownloadGeoipDbAsync(string dest)
        {
            var dir = Path.GetDirectoryName(dest);
            if (dir != null) Directory.CreateDirectory(dir);

            Console.WriteLine("[cloakbrowser] Downloading GeoIP database (~70 MB)…");
            var tmpPath = $"{dest}.tmp.{DateTime.UtcNow.Ticks}";

            try
            {
                using (var client = new HttpClient())
                {
                    using (var response = await client.GetAsync(GeoipDbUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        using (var fileStream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            await response.Content.CopyToAsync(fileStream);
                        }
                    }
                }
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(tmpPath, dest);
                Console.WriteLine($"[cloakbrowser] GeoIP database ready: {dest}");
            }
            catch
            {
                if (File.Exists(tmpPath)) File.Delete(tmpPath);
                throw;
            }
        }

        private static void MaybeTriggerUpdate(string dbPath)
        {
            try
            {
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(dbPath);
                if (age < GeoipUpdateInterval) return;
            }
            catch
            {
                return;
            }

            // Run in background
            Task.Run(async () =>
            {
                try
                {
                    await DownloadGeoipDbAsync(dbPath);
                }
                catch
                {
                    // Ignore background failure
                }
            });
        }

        private static string? ExtractProxyUrl(object? proxy)
        {
            if (proxy == null) return null;
            if (proxy is string s) return ProxyConfig.EnsureProxyScheme(s);
            var p = (ProxySettings)proxy;
            if (string.IsNullOrEmpty(p.Server)) return null;
            if (!string.IsNullOrEmpty(p.Username) && ProxyConfig.IsSocksProxy(p))
            {
                return ProxyConfig.ReconstructSocksUrl(p);
            }
            return ProxyConfig.EnsureProxyScheme(p.Server);
        }

        private static int GetGeoipTimeoutMs()
        {
            var raw = Environment.GetEnvironmentVariable("CLOAKBROWSER_GEOIP_TIMEOUT_SECONDS");
            if (string.IsNullOrEmpty(raw)) return DefaultGeoipTimeoutMs;
            if (double.TryParse(raw, out var sec))
            {
                return (int)(Math.Max(sec, 0) * 1000);
            }
            return DefaultGeoipTimeoutMs;
        }

        public static async Task<string?> ResolveExitIpAsync(string proxyUrl, int timeoutMs)
        {
            var proxyUri = new Uri(proxyUrl);
            var handler = new HttpClientHandler
            {
                UseProxy = true
            };

            // Set up WebProxy credentials if embedded
            var userInfo = proxyUri.UserInfo;
            if (!string.IsNullOrEmpty(userInfo))
            {
                var parts = userInfo.Split(':');
                var creds = new NetworkCredential(
                    Uri.UnescapeDataString(parts[0]),
                    parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : ""
                );
                // Strip credentials from server url
                var serverUrl = $"{proxyUri.Scheme}://{proxyUri.Host}{(proxyUri.Port == -1 ? "" : $":{proxyUri.Port}")}";
                handler.Proxy = new WebProxy(serverUrl)
                {
                    Credentials = creds
                };
            }
            else
            {
                handler.Proxy = new WebProxy(proxyUrl);
            }

            using (var client = new HttpClient(handler))
            {
                client.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
                foreach (var echoUrl in IpEchoUrls)
                {
                    try
                    {
                        var response = await client.GetStringAsync(echoUrl);
                        var ip = response.Trim();
                        if (IPAddress.TryParse(ip, out _))
                        {
                            return ip;
                        }
                    }
                    catch
                    {
                        // Try next URL
                    }
                }
            }
            return null;
        }

        public static async Task<GeoResult> ResolveProxyGeoAsync(string proxyUrl)
        {
            var dbPath = await EnsureGeoipDbAsync();
            if (dbPath == null) return new GeoResult();

            int timeoutMs = GetGeoipTimeoutMs();
            string? ip = await ResolveExitIpAsync(proxyUrl, timeoutMs);
            if (string.IsNullOrEmpty(ip))
            {
                return new GeoResult();
            }

            try
            {
                using (var reader = new Reader(dbPath))
                {
                    var ipAddress = IPAddress.Parse(ip);
                    var data = reader.Find<Dictionary<string, object>>(ipAddress);
                    if (data == null) return new GeoResult { ExitIp = ip };

                    string? timezone = null;
                    if (data.TryGetValue("location", out var locationObj) && locationObj is Dictionary<string, object> location)
                    {
                        if (location.TryGetValue("time_zone", out var tz))
                        {
                            timezone = tz as string;
                        }
                    }

                    string? countryCode = null;
                    if (data.TryGetValue("country", out var countryObj) && countryObj is Dictionary<string, object> country)
                    {
                        if (country.TryGetValue("iso_code", out var iso))
                        {
                            countryCode = iso as string;
                        }
                    }

                    string? locale = null;
                    if (countryCode != null && CountryLocaleMap.TryGetValue(countryCode, out var loc))
                    {
                        locale = loc;
                    }

                    return new GeoResult
                    {
                        Timezone = timezone,
                        Locale = locale,
                        ExitIp = ip
                    };
                }
            }
            catch
            {
                return new GeoResult { ExitIp = ip };
            }
        }

        public static async Task<(string? exitIp, string? timezone, string? locale)> MaybeResolveGeoipAsync(CloakLaunchOptions options)
        {
            if (!options.Geoip || options.Proxy == null)
            {
                return (null, options.Timezone, options.Locale);
            }

            string? proxyUrl = ExtractProxyUrl(options.Proxy);
            if (string.IsNullOrEmpty(proxyUrl))
            {
                return (null, options.Timezone, options.Locale);
            }

            if (options.Timezone != null && options.Locale != null)
            {
                string? exitIp = await ResolveExitIpAsync(proxyUrl, GetGeoipTimeoutMs());
                return (exitIp, options.Timezone, options.Locale);
            }

            var geo = await ResolveProxyGeoAsync(proxyUrl);
            return (
                geo.ExitIp,
                options.Timezone ?? geo.Timezone,
                options.Locale ?? geo.Locale
            );
        }

        public static async Task<List<string>?> ResolveWebrtcArgsAsync(CloakLaunchOptions options)
        {
            var args = options.Args;
            if (args == null) return args;

            int idx = args.FindIndex(a => a == "--fingerprint-webrtc-ip=auto");
            if (idx == -1) return args;

            string? proxyUrl = ExtractProxyUrl(options.Proxy);
            if (string.IsNullOrEmpty(proxyUrl))
            {
                Console.WriteLine("[cloakbrowser] --fingerprint-webrtc-ip=auto requires a proxy; removing flag");
                var result = new List<string>(args);
                result.RemoveAt(idx);
                return result;
            }

            try
            {
                string? ip = await ResolveExitIpAsync(proxyUrl, GetGeoipTimeoutMs());
                var result = new List<string>(args);
                if (!string.IsNullOrEmpty(ip))
                {
                    result[idx] = $"--fingerprint-webrtc-ip={ip}";
                }
                else
                {
                    Console.WriteLine("[cloakbrowser] Could not resolve proxy exit IP for WebRTC spoofing; removing --fingerprint-webrtc-ip=auto");
                    result.RemoveAt(idx);
                }
                return result;
            }
            catch
            {
                Console.WriteLine("[cloakbrowser] Failed to resolve proxy exit IP for WebRTC spoofing; removing --fingerprint-webrtc-ip=auto");
                var result = new List<string>(args);
                result.RemoveAt(idx);
                return result;
            }
        }
    }
}

