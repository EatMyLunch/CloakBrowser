using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Playwright.CloakBrowser.Community.Proxy;
using Playwright.CloakBrowser.Community.Proxy.Interceptors;

namespace Playwright.CloakBrowser.Community
{
    public static class Cloak
    {
        /// <summary>
        /// Launches a new instance of stealth Chromium with CloakBrowser patches.
        /// </summary>
        /// <param name="options">Options for configuring the stealth launch.</param>
        /// <returns>A running <see cref="IBrowser"/> instance with stealth capabilities.</returns>
        public static async Task<IBrowser> LaunchAsync(CloakLaunchOptions? options = null)
        {
            options ??= new CloakLaunchOptions();
            var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
            try
            {
                var launchOptions = await BuildLaunchOptionsAsync(options);
                var browser = await playwright.Chromium.LaunchAsync(launchOptions);
                var browserInterceptor = new BrowserInterceptor(browser, playwright, options);
                return PlaywrightProxy<IBrowser>.Create(browser, browserInterceptor);
            }
            catch
            {
                playwright.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Launches a new browser instance and automatically creates a new stealth browser context in one step.
        /// Re-uses GeoIP resolution internally to avoid redundant lookups.
        /// </summary>
        /// <param name="options">Launch and context configuration options.</param>
        /// <returns>A running <see cref="IBrowserContext"/> with active stealth and proxy routing.</returns>
        public static async Task<IBrowserContext> LaunchContextAsync(CloakLaunchContextOptions? options = null)
        {
            options ??= new CloakLaunchContextOptions();

            // Resolve geoip BEFORE launching to avoid double-resolution
            var (exitIp, resolvedTimezone, resolvedLocale) = await GeoIp.MaybeResolveGeoipAsync(options);

            var webrtcArgs = await GeoIp.ResolveWebrtcArgsAsync(options) ?? new List<string>();
            if (!string.IsNullOrEmpty(exitIp) && !webrtcArgs.Exists(a => a.StartsWith("--fingerprint-webrtc-ip")))
            {
                webrtcArgs.Add($"--fingerprint-webrtc-ip={exitIp}");
            }

            var launchOptions = new CloakLaunchOptions
            {
                Headless = options.Headless,
                Proxy = options.Proxy,
                Args = new List<string>(options.Args ?? new List<string>()),
                ExtensionPaths = options.ExtensionPaths,
                StealthArgs = options.StealthArgs,
                Timezone = resolvedTimezone,
                Locale = resolvedLocale,
                Geoip = false,
                Humanize = options.Humanize,
                HumanPreset = options.HumanPreset,
                HumanConfig = options.HumanConfig,
                LaunchOptions = options.LaunchOptions
            };
            launchOptions.Args.AddRange(webrtcArgs);

            var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
            IBrowser browser;
            try
            {
                var builtLaunchOpts = await BuildLaunchOptionsAsync(launchOptions);
                browser = await playwright.Chromium.LaunchAsync(builtLaunchOpts);
            }
            catch
            {
                playwright.Dispose();
                throw;
            }

            IBrowserContext context;
            try
            {
                var contextOptions = ContextOptionsBuilder.BuildContextOptions(options);
                context = await browser.NewContextAsync(contextOptions);
            }
            catch
            {
                await browser.CloseAsync();
                playwright.Dispose();
                throw;
            }

            var contextInterceptor = new ContextInterceptor(context, options, async () =>
            {
                try
                {
                    await browser.CloseAsync();
                }
                finally
                {
                    playwright.Dispose();
                }
            });

            return PlaywrightProxy<IBrowserContext>.Create(context, contextInterceptor);
        }

        /// <summary>
        /// Launches a persistent stealth browser context using a specific user data directory (profile).
        /// Useful for session and state retention across script runs.
        /// </summary>
        /// <param name="userDataDir">Path to the directory where profile/session data is stored.</param>
        /// <param name="options">Launch and persistent context configuration options.</param>
        /// <returns>A running persistent <see cref="IBrowserContext"/>.</returns>
        public static async Task<IBrowserContext> LaunchPersistentContextAsync(string userDataDir, CloakLaunchPersistentContextOptions? options = null)
        {
            options ??= new CloakLaunchPersistentContextOptions { UserDataDir = userDataDir };
            if (string.IsNullOrEmpty(options.UserDataDir))
            {
                options.UserDataDir = userDataDir;
            }

            var binaryPath = Environment.GetEnvironmentVariable("CLOAKBROWSER_BINARY_PATH") ?? await Download.EnsureBinaryAsync();

            var (exitIp, resolvedTimezone, resolvedLocale) = await GeoIp.MaybeResolveGeoipAsync(options);

            var webrtcArgs = await GeoIp.ResolveWebrtcArgsAsync(options) ?? new List<string>();
            if (!string.IsNullOrEmpty(exitIp) && !webrtcArgs.Exists(a => a.StartsWith("--fingerprint-webrtc-ip")))
            {
                webrtcArgs.Add($"--fingerprint-webrtc-ip={exitIp}");
            }

            var proxyResult = ProxyConfig.ResolveProxyConfig(options.Proxy);

            var mergedOptions = new CloakLaunchOptions
            {
                Headless = options.Headless,
                Proxy = options.Proxy,
                Args = new List<string>(options.Args ?? new List<string>()),
                ExtensionPaths = options.ExtensionPaths,
                StealthArgs = options.StealthArgs,
                Timezone = resolvedTimezone,
                Locale = resolvedLocale,
                Geoip = false,
                Humanize = options.Humanize,
                HumanPreset = options.HumanPreset,
                HumanConfig = options.HumanConfig,
                LaunchOptions = options.LaunchOptions
            };

            if (mergedOptions.Args == null) mergedOptions.Args = new List<string>();
            mergedOptions.Args.AddRange(webrtcArgs);
            mergedOptions.Args.AddRange(proxyResult.ProxyArgs);

            var chromeArgs = ArgsBuilder.Build(mergedOptions, resolvedTimezone, resolvedLocale, exitIp);

            var playwright = await Microsoft.Playwright.Playwright.CreateAsync();

            var persistentOptions = new BrowserTypeLaunchPersistentContextOptions
            {
                ExecutablePath = binaryPath,
                Headless = options.Headless,
                Args = chromeArgs,
                IgnoreDefaultArgs = new[] { "--enable-automation", "--enable-unsafe-swiftshader" }
            };

            if (proxyResult.ProxyOption != null)
            {
                persistentOptions.Proxy = proxyResult.ProxyOption;
            }

            var contextOpts = ContextOptionsBuilder.BuildContextOptions(options);
            if (contextOpts.UserAgent != null) persistentOptions.UserAgent = contextOpts.UserAgent;
            if (contextOpts.ViewportSize != null) persistentOptions.ViewportSize = contextOpts.ViewportSize;
            if (contextOpts.ColorScheme != null) persistentOptions.ColorScheme = contextOpts.ColorScheme;
            if (contextOpts.AcceptDownloads != null) persistentOptions.AcceptDownloads = contextOpts.AcceptDownloads;
            if (contextOpts.BypassCSP != null) persistentOptions.BypassCSP = contextOpts.BypassCSP;

            if (contextOpts.DeviceScaleFactor != null) persistentOptions.DeviceScaleFactor = contextOpts.DeviceScaleFactor;
            if (contextOpts.ExtraHTTPHeaders != null) persistentOptions.ExtraHTTPHeaders = contextOpts.ExtraHTTPHeaders;
            if (contextOpts.ForcedColors != null) persistentOptions.ForcedColors = contextOpts.ForcedColors;
            if (contextOpts.Geolocation != null) persistentOptions.Geolocation = contextOpts.Geolocation;
            if (contextOpts.HasTouch != null) persistentOptions.HasTouch = contextOpts.HasTouch;
            if (contextOpts.HttpCredentials != null) persistentOptions.HttpCredentials = contextOpts.HttpCredentials;
            if (contextOpts.IgnoreHTTPSErrors != null) persistentOptions.IgnoreHTTPSErrors = contextOpts.IgnoreHTTPSErrors;
            if (contextOpts.IsMobile != null) persistentOptions.IsMobile = contextOpts.IsMobile;
            if (contextOpts.JavaScriptEnabled != null) persistentOptions.JavaScriptEnabled = contextOpts.JavaScriptEnabled;
            if (contextOpts.Offline != null) persistentOptions.Offline = contextOpts.Offline;
            if (contextOpts.Permissions != null) persistentOptions.Permissions = contextOpts.Permissions;
            if (contextOpts.ReducedMotion != null) persistentOptions.ReducedMotion = contextOpts.ReducedMotion;
            if (contextOpts.ScreenSize != null) persistentOptions.ScreenSize = contextOpts.ScreenSize;
            if (contextOpts.ServiceWorkers != null) persistentOptions.ServiceWorkers = contextOpts.ServiceWorkers;
            if (contextOpts.StrictSelectors != null) persistentOptions.StrictSelectors = contextOpts.StrictSelectors;

            if (options.LaunchOptions != null)
            {
                if (options.LaunchOptions.Env != null) persistentOptions.Env = options.LaunchOptions.Env;
                if (options.LaunchOptions.Timeout != null) persistentOptions.Timeout = options.LaunchOptions.Timeout;
                if (options.LaunchOptions.SlowMo != null) persistentOptions.SlowMo = options.LaunchOptions.SlowMo;
                if (options.LaunchOptions.DownloadsPath != null) persistentOptions.DownloadsPath = options.LaunchOptions.DownloadsPath;
                if (options.LaunchOptions.Devtools != null) persistentOptions.Devtools = options.LaunchOptions.Devtools;
                if (options.LaunchOptions.TracesDir != null) persistentOptions.TracesDir = options.LaunchOptions.TracesDir;
            }

            IBrowserContext context;
            try
            {
                context = await playwright.Chromium.LaunchPersistentContextAsync(userDataDir, persistentOptions);
            }
            catch
            {
                playwright.Dispose();
                throw;
            }

            var contextInterceptor = new ContextInterceptor(context, options, async () =>
            {
                playwright.Dispose();
                await Task.CompletedTask;
            });

            return PlaywrightProxy<IBrowserContext>.Create(context, contextInterceptor);
        }

        /// <summary>
        /// Prepares and builds Playwright's native <see cref="BrowserTypeLaunchOptions"/> based on CloakBrowser config,
        /// including downloading/extracting the stealth binary, proxy credentials, and fingerprint arguments.
        /// </summary>
        /// <param name="options">Stealth configuration options.</param>
        /// <returns>A fully populated <see cref="BrowserTypeLaunchOptions"/> instance ready for Playwright Chromium Launch.</returns>
        public static async Task<BrowserTypeLaunchOptions> BuildLaunchOptionsAsync(CloakLaunchOptions? options = null)
        {
            options ??= new CloakLaunchOptions();
            var binaryPath = Environment.GetEnvironmentVariable("CLOAKBROWSER_BINARY_PATH") ?? await Download.EnsureBinaryAsync();

            var (exitIp, resolvedTimezone, resolvedLocale) = await GeoIp.MaybeResolveGeoipAsync(options);

            var webrtcArgs = await GeoIp.ResolveWebrtcArgsAsync(options) ?? new List<string>();
            if (!string.IsNullOrEmpty(exitIp) && !webrtcArgs.Exists(a => a.StartsWith("--fingerprint-webrtc-ip")))
            {
                webrtcArgs.Add($"--fingerprint-webrtc-ip={exitIp}");
            }

            var proxyResult = ProxyConfig.ResolveProxyConfig(options.Proxy);

            var mergedOptions = new CloakLaunchOptions
            {
                Headless = options.Headless,
                Proxy = options.Proxy,
                Args = new List<string>(options.Args ?? new List<string>()),
                ExtensionPaths = options.ExtensionPaths,
                StealthArgs = options.StealthArgs,
                Timezone = resolvedTimezone,
                Locale = resolvedLocale,
                Geoip = false,
                Humanize = options.Humanize,
                HumanPreset = options.HumanPreset,
                HumanConfig = options.HumanConfig,
                LaunchOptions = options.LaunchOptions
            };

            if (mergedOptions.Args == null) mergedOptions.Args = new List<string>();
            mergedOptions.Args.AddRange(webrtcArgs);
            mergedOptions.Args.AddRange(proxyResult.ProxyArgs);

            var chromeArgs = ArgsBuilder.Build(mergedOptions, resolvedTimezone, resolvedLocale, exitIp);

            var launchOptions = new BrowserTypeLaunchOptions
            {
                ExecutablePath = binaryPath,
                Headless = options.Headless,
                Args = chromeArgs,
                IgnoreDefaultArgs = new[] { "--enable-automation", "--enable-unsafe-swiftshader" }
            };

            if (proxyResult.ProxyOption != null)
            {
                launchOptions.Proxy = proxyResult.ProxyOption;
            }

            if (options.LaunchOptions != null)
            {
                if (options.LaunchOptions.Env != null) launchOptions.Env = options.LaunchOptions.Env;
                if (options.LaunchOptions.Timeout != null) launchOptions.Timeout = options.LaunchOptions.Timeout;
                if (options.LaunchOptions.SlowMo != null) launchOptions.SlowMo = options.LaunchOptions.SlowMo;
                if (options.LaunchOptions.FirefoxUserPrefs != null) launchOptions.FirefoxUserPrefs = options.LaunchOptions.FirefoxUserPrefs;
                if (options.LaunchOptions.DownloadsPath != null) launchOptions.DownloadsPath = options.LaunchOptions.DownloadsPath;
                if (options.LaunchOptions.ChromiumSandbox != null) launchOptions.ChromiumSandbox = options.LaunchOptions.ChromiumSandbox;
                if (options.LaunchOptions.Devtools != null) launchOptions.Devtools = options.LaunchOptions.Devtools;
                if (options.LaunchOptions.TracesDir != null) launchOptions.TracesDir = options.LaunchOptions.TracesDir;
            }

            return launchOptions;
        }

        /// <summary>
        /// Converts Cloak context options to Playwright's native <see cref="BrowserNewContextOptions"/>.
        /// Timezone and locale parameters are excluded here to prevent browser-emulation leakage.
        /// </summary>
        /// <param name="options">Stealth context options.</param>
        /// <returns>A native <see cref="BrowserNewContextOptions"/> instance.</returns>
        public static BrowserNewContextOptions BuildContextOptions(CloakLaunchContextOptions? options = null)
        {
            return ContextOptionsBuilder.BuildContextOptions(options ?? new CloakLaunchContextOptions());
        }
    }
}

