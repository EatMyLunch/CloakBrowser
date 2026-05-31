using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.Playwright;

namespace CloakBrowser
{
    public class ResolvedProxyConfig
    {
        public Microsoft.Playwright.Proxy? ProxyOption { get; set; }
        public List<string> ProxyArgs { get; set; } = new List<string>();
    }

    public static class ProxyConfig
    {
        private static readonly Regex SchemeRegex = new Regex(@"^([a-z][a-z0-9+\-.]*):\/\/(.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PortRegex = new Regex(@"^\d+$", RegexOptions.Compiled);

        public static bool IsSocksProxy(object? proxy)
        {
            if (proxy == null) return false;
            string serverUrl = proxy is string s ? s : ((ProxySettings)proxy).Server;
            return serverUrl.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase) ||
                   serverUrl.StartsWith("socks5h://", StringComparison.OrdinalIgnoreCase);
        }

        public static string EnsureProxyScheme(string proxyUrl)
        {
            return proxyUrl.Contains("://") ? proxyUrl : $"http://{proxyUrl}";
        }

        private static string LenientUrlDecode(string s)
        {
            // Decodes URL-encoded parts, leaves % as % if not a valid sequence.
            // C# HttpUtility.UrlDecode does exactly this.
            return HttpUtility.UrlDecode(s);
        }

        private static string LenientUrlEncode(string s)
        {
            return HttpUtility.UrlEncode(s);
        }

        private static string AssembleSocksUrl(string scheme, string encUser, string? encPass, string hostAndRest)
        {
            string userinfo = "";
            if (encPass != null)
            {
                userinfo = $"{encUser}:{encPass}@";
            }
            else if (!string.IsNullOrEmpty(encUser))
            {
                userinfo = $"{encUser}@";
            }
            return $"{scheme}://{userinfo}{hostAndRest}";
        }

        public static string NormalizeSocksStringUrl(string urlStr)
        {
            var match = SchemeRegex.Match(urlStr);
            if (!match.Success) return urlStr;

            string scheme = match.Groups[1].Value;
            string rest = match.Groups[2].Value;

            int hostStart = rest.IndexOfAny(new[] { '/', '?', '#' });
            string authority = hostStart == -1 ? rest : rest.Substring(0, hostStart);
            string suffix = hostStart == -1 ? "" : rest.Substring(hostStart);

            int atIdx = authority.LastIndexOf('@');
            if (atIdx == -1) return urlStr; // no creds

            string userinfo = authority.Substring(0, atIdx);
            string hostPart = authority.Substring(atIdx + 1);

            int bracketEnd = hostPart.LastIndexOf(']');
            int portColonIdx = hostPart.IndexOf(':', Math.Max(bracketEnd, 0));
            if (portColonIdx != -1)
            {
                string portStr = hostPart.Substring(portColonIdx + 1);
                if (!string.IsNullOrEmpty(portStr) && !PortRegex.IsMatch(portStr))
                {
                    Console.WriteLine("[cloakbrowser] Malformed SOCKS5 proxy URL, passing through unchanged: invalid port");
                    return urlStr;
                }
            }

            string hostAndRest = hostPart + suffix;
            int colonIdx = userinfo.IndexOf(':');
            string rawUserEnc = colonIdx == -1 ? userinfo : userinfo.Substring(0, colonIdx);
            bool hasPassword = colonIdx != -1;
            string rawPassEnc = hasPassword ? userinfo.Substring(colonIdx + 1) : "";

            try
            {
                string encUser = !string.IsNullOrEmpty(rawUserEnc) ? Uri.EscapeDataString(LenientUrlDecode(rawUserEnc)) : "";
                string? encPass = hasPassword
                    ? (!string.IsNullOrEmpty(rawPassEnc) ? Uri.EscapeDataString(LenientUrlDecode(rawPassEnc)) : "")
                    : null;

                string normalized = AssembleSocksUrl(scheme, encUser, encPass, hostAndRest);
                bool credsChanged = encUser != rawUserEnc || (hasPassword ? encPass != rawPassEnc : false);
                if (credsChanged)
                {
                    Console.WriteLine("[cloakbrowser] Auto URL-encoded SOCKS5 proxy credentials (special characters detected). Pre-encode the URL to suppress this notice.");
                }
                return normalized;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[cloakbrowser] Could not normalize SOCKS5 proxy URL, passing through unchanged: {ex.Message}");
                return urlStr;
            }
        }

        public static string NormalizeHttpStringUrl(string urlStr)
        {
            string normalized = urlStr.Contains("://") ? urlStr : $"http://{urlStr}";
            var match = SchemeRegex.Match(normalized);
            if (!match.Success) return normalized;

            string scheme = match.Groups[1].Value;
            string rest = match.Groups[2].Value;

            int hostStart = rest.IndexOfAny(new[] { '/', '?', '#' });
            string authority = hostStart == -1 ? rest : rest.Substring(0, hostStart);
            string suffix = hostStart == -1 ? "" : rest.Substring(hostStart);

            int atIdx = authority.LastIndexOf('@');
            if (atIdx == -1) return normalized; // no creds

            string userinfo = authority.Substring(0, atIdx);
            string hostPart = authority.Substring(atIdx + 1);

            int bracketEnd = hostPart.LastIndexOf(']');
            int portColonIdx = hostPart.IndexOf(':', Math.Max(bracketEnd, 0));
            if (portColonIdx != -1)
            {
                string portStr = hostPart.Substring(portColonIdx + 1);
                if (!string.IsNullOrEmpty(portStr) && !PortRegex.IsMatch(portStr))
                {
                    Console.WriteLine("[cloakbrowser] Malformed HTTP proxy URL, passing through unchanged: invalid port");
                    return normalized;
                }
            }

            string hostAndRest = hostPart + suffix;
            int colonIdx = userinfo.IndexOf(':');
            string rawUserEnc = colonIdx == -1 ? userinfo : userinfo.Substring(0, colonIdx);
            bool hasPassword = colonIdx != -1;
            string rawPassEnc = hasPassword ? userinfo.Substring(colonIdx + 1) : "";

            try
            {
                string encUser = !string.IsNullOrEmpty(rawUserEnc) ? Uri.EscapeDataString(LenientUrlDecode(rawUserEnc)) : "";
                string? encPass = hasPassword
                    ? (!string.IsNullOrEmpty(rawPassEnc) ? Uri.EscapeDataString(LenientUrlDecode(rawPassEnc)) : "")
                    : null;

                string userinfoPart = encPass != null ? $"{encUser}:{encPass}@" : (!string.IsNullOrEmpty(encUser) ? $"{encUser}@" : "");
                string result = $"{scheme}://{userinfoPart}{hostAndRest}";
                bool credsChanged = encUser != rawUserEnc || (hasPassword ? encPass != rawPassEnc : false);
                if (credsChanged)
                {
                    Console.WriteLine("[cloakbrowser] Auto URL-encoded HTTP proxy credentials (special characters detected). Pre-encode the URL to suppress this notice.");
                }
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[cloakbrowser] Could not normalize HTTP proxy URL, passing through unchanged: {ex.Message}");
                return normalized;
            }
        }

        public static bool SupportsHttpProxyInlineAuth()
        {
            try
            {
                var tag = Config.GetPlatformTag();
                if (tag != "linux-x64" && tag != "windows-x64") return false;

                var current = Config.ParseVersion(Config.GetChromiumVersion());
                var minimum = Config.ParseVersion("146.0.7680.177.5");
                int maxLen = Math.Max(current.Length, minimum.Length);
                for (int i = 0; i < maxLen; i++)
                {
                    int valA = i < current.Length ? current[i] : 0;
                    int valB = i < minimum.Length ? minimum[i] : 0;
                    if (valA > valB) return true;
                    if (valA < valB) return false;
                }
                return true; // equal
            }
            catch
            {
                return false;
            }
        }

        private static bool HasCredentials(object proxy)
        {
            if (proxy is string s) return s.Contains("@");
            if (proxy is ProxySettings p) return !string.IsNullOrEmpty(p.Username);
            return false;
        }

        public static string ReconstructSocksUrl(ProxySettings proxy)
        {
            var serverUrl = proxy.Server;
            try
            {
                var uri = new Uri(serverUrl);
                string userinfo = "";
                if (!string.IsNullOrEmpty(proxy.Username))
                {
                    userinfo = Uri.EscapeDataString(proxy.Username);
                    if (!string.IsNullOrEmpty(proxy.Password))
                    {
                        userinfo += $":{Uri.EscapeDataString(proxy.Password)}";
                    }
                    userinfo += "@";
                }
                string portPart = uri.Port == -1 ? "" : $":{uri.Port}";
                return $"{uri.Scheme}://{userinfo}{uri.Host}{portPart}{uri.AbsolutePath.TrimEnd('/')}";
            }
            catch
            {
                return serverUrl;
            }
        }

        public static string ReconstructHttpUrl(ProxySettings proxy)
        {
            string serverUrl = EnsureProxyScheme(proxy.Server);
            if (string.IsNullOrEmpty(proxy.Username)) return serverUrl;
            try
            {
                var uri = new Uri(serverUrl);
                string userinfo = Uri.EscapeDataString(proxy.Username);
                if (!string.IsNullOrEmpty(proxy.Password))
                {
                    userinfo += $":{Uri.EscapeDataString(proxy.Password)}";
                }
                string portPart = uri.Port == -1 ? "" : $":{uri.Port}";
                return $"{uri.Scheme}://{userinfo}{uri.Host}{portPart}{uri.AbsolutePath.TrimEnd('/')}";
            }
            catch
            {
                return serverUrl;
            }
        }

        public static ResolvedProxyConfig ResolveProxyConfig(object? proxy)
        {
            if (proxy == null) return new ResolvedProxyConfig();

            if (IsSocksProxy(proxy))
            {
                if (proxy is string s)
                {
                    return new ResolvedProxyConfig { ProxyArgs = new List<string> { $"--proxy-server={NormalizeSocksStringUrl(s)}" } };
                }
                var p = (ProxySettings)proxy;
                var socksUrl = ReconstructSocksUrl(p);
                var args = new List<string> { $"--proxy-server={socksUrl}" };
                if (!string.IsNullOrEmpty(p.Bypass)) args.Add($"--proxy-bypass-list={p.Bypass}");
                return new ResolvedProxyConfig { ProxyArgs = args };
            }

            if (HasCredentials(proxy) && SupportsHttpProxyInlineAuth())
            {
                if (proxy is string s)
                {
                    return new ResolvedProxyConfig { ProxyArgs = new List<string> { $"--proxy-server={NormalizeHttpStringUrl(s)}" } };
                }
                var p = (ProxySettings)proxy;
                var httpUrl = ReconstructHttpUrl(p);
                var args = new List<string> { $"--proxy-server={httpUrl}" };
                if (!string.IsNullOrEmpty(p.Bypass)) args.Add($"--proxy-bypass-list={p.Bypass}");
                return new ResolvedProxyConfig { ProxyArgs = args };
            }

            // Fallback: standard Playwright proxy option
            if (proxy is string str)
            {
                return new ResolvedProxyConfig { ProxyOption = ParseProxyUrl(str) };
            }
            var ps = (ProxySettings)proxy;
            return new ResolvedProxyConfig
            {
                ProxyOption = new Microsoft.Playwright.Proxy
                {
                    Server = ps.Server,
                    Bypass = ps.Bypass,
                    Username = ps.Username,
                    Password = ps.Password
                }
            };
        }

        public static Microsoft.Playwright.Proxy ParseProxyUrl(string proxy)
        {
            string normalized = proxy.Contains("@") && !proxy.Contains("://") ? $"http://{proxy}" : proxy;
            try
            {
                var uri = new Uri(normalized);
                if (string.IsNullOrEmpty(uri.UserInfo))
                {
                    return new Microsoft.Playwright.Proxy { Server = proxy };
                }

                string server = $"{uri.Scheme}://{uri.Host}{(uri.Port == -1 ? "" : $":{uri.Port}")}";
                var parts = uri.UserInfo.Split(':');
                var result = new Microsoft.Playwright.Proxy
                {
                    Server = server,
                    Username = Uri.UnescapeDataString(parts[0])
                };
                if (parts.Length > 1)
                {
                    result.Password = Uri.UnescapeDataString(parts[1]);
                }
                return result;
            }
            catch
            {
                return new Microsoft.Playwright.Proxy { Server = proxy };
            }
        }
    }
}
