using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace CloakBrowser
{
    public static class ArgsBuilder
    {
        public static List<string> Build(
            CloakLaunchOptions options,
            string? timezone = null,
            string? locale = null,
            string? exitIp = null)
        {
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 1. Base default stealth arguments
            if (options.StealthArgs)
            {
                foreach (var arg in Config.GetDefaultStealthArgs())
                {
                    string key = GetArgKey(arg);
                    seen[key] = arg;
                }
            }

            // 2. GPU blocklist bypass
            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            if (options.Headless == false || isWindows)
            {
                seen["--ignore-gpu-blocklist"] = "--ignore-gpu-blocklist";
            }

            // 3. User-defined arguments
            if (options.Args != null)
            {
                foreach (var arg in options.Args)
                {
                    string key = GetArgKey(arg);
                    seen[key] = arg;
                }
            }

            // 4. Timezone override
            timezone = timezone ?? options.Timezone;
            if (!string.IsNullOrEmpty(timezone))
            {
                string key = "--fingerprint-timezone";
                seen[key] = $"{key}={timezone}";
            }

            // 5. Locale override
            locale = locale ?? options.Locale;
            if (!string.IsNullOrEmpty(locale))
            {
                foreach (var k in new[] { "--lang", "--fingerprint-locale" })
                {
                    seen[k] = $"{k}={locale}";
                }
            }

            // 6. SOCKS/HTTP WebRTC IP override (from GeoIP)
            if (!string.IsNullOrEmpty(exitIp))
            {
                string key = "--fingerprint-webrtc-ip";
                if (!seen.ContainsKey(key))
                {
                    seen[key] = $"{key}={exitIp}";
                }
            }

            // 7. Chrome extensions
            if (options.ExtensionPaths != null && options.ExtensionPaths.Count > 0)
            {
                var absPaths = new List<string>();
                foreach (var p in options.ExtensionPaths)
                {
                    absPaths.Add(Path.GetFullPath(p));
                }
                string joined = string.Join(",", absPaths);

                seen["--load-extension"] = $"--load-extension={joined}";
                seen["--disable-extensions-except"] = $"--disable-extensions-except={joined}";
            }

            return new List<string>(seen.Values);
        }

        private static string GetArgKey(string arg)
        {
            int idx = arg.IndexOf('=');
            return idx == -1 ? arg : arg.Substring(0, idx);
        }
    }
}
