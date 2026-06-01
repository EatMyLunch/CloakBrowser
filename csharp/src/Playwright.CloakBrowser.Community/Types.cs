using System;
using System.Collections.Generic;
using Microsoft.Playwright;
using Playwright.CloakBrowser.Community.Human;

namespace Playwright.CloakBrowser.Community
{
    public class ProxySettings
    {
        public string Server { get; set; } = string.Empty;
        public string? Bypass { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
    }

    public class CloakLaunchOptions
    {
        /// <summary>
        /// Run in headless mode (default: true).
        /// </summary>
        public bool? Headless { get; set; }

        /// <summary>
        /// Proxy server config — can be a URL string (e.g. 'http://user:pass@proxy:8080')
        /// or a structured ProxySettings object.
        /// </summary>
        public object? Proxy { get; set; }

        /// <summary>
        /// Additional Chromium CLI arguments.
        /// </summary>
        public List<string>? Args { get; set; }

        /// <summary>
        /// Chrome extension paths to load.
        /// </summary>
        public List<string>? ExtensionPaths { get; set; }

        /// <summary>
        /// Include default stealth fingerprint args (default: true).
        /// Set to false if you want to pass your own --fingerprint flags.
        /// </summary>
        public bool StealthArgs { get; set; } = true;

        /// <summary>
        /// IANA timezone, e.g. "America/New_York". Sets --fingerprint-timezone binary flag.
        /// </summary>
        public string? Timezone { get; set; }

        /// <summary>
        /// BCP 47 locale, e.g. "en-US". Sets --lang and --fingerprint-locale binary flags.
        /// </summary>
        public string? Locale { get; set; }

        /// <summary>
        /// Auto-detect timezone/locale from proxy IP. Requires MaxMind DB.
        /// </summary>
        public bool Geoip { get; set; } = false;

        /// <summary>
        /// Enable human-like mouse, keyboard, and scroll behavior.
        /// </summary>
        public bool Humanize { get; set; } = false;

        /// <summary>
        /// Human behavior preset: Default or Careful.
        /// </summary>
        public HumanPreset HumanPreset { get; set; } = HumanPreset.Default;

        /// <summary>
        /// Override individual human behavior parameters.
        /// </summary>
        public HumanConfig? HumanConfig { get; set; }

        /// <summary>
        /// Raw options passed directly to Playwright's BrowserTypeLaunchOptions.
        /// </summary>
        public BrowserTypeLaunchOptions? LaunchOptions { get; set; }
    }

    public class CloakLaunchContextOptions : CloakLaunchOptions
    {
        /// <summary>
        /// Custom user agent string.
        /// </summary>
        public string? UserAgent { get; set; }

        /// <summary>
        /// Viewport size. Set null to disable emulation.
        /// </summary>
        public ViewportSize? Viewport { get; set; } = Config.DefaultViewport;

        /// <summary>
        /// Color scheme preference — "light", "dark", or "no-preference".
        /// </summary>
        public string? ColorScheme { get; set; }

        /// <summary>
        /// Extra options forwarded directly to Playwright's browser.NewContextAsync() —
        /// e.g. StorageState, Permissions, Geolocation, ExtraHTTPHeaders.
        /// Locale and TimezoneId are stripped here to avoid detectable CDP emulation —
        /// use top-level Locale and Timezone fields instead (they route through undetectable binary flags).
        /// </summary>
        public BrowserNewContextOptions? ContextOptions { get; set; }
    }

    public class CloakLaunchPersistentContextOptions : CloakLaunchContextOptions
    {
        /// <summary>
        /// Path to user data directory for persistent profile.
        /// </summary>
        public string UserDataDir { get; set; } = string.Empty;
    }

    public class BinaryInfo
    {
        public string Version { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string BinaryPath { get; set; } = string.Empty;
        public bool Installed { get; set; }
        public string CacheDir { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
    }
}

