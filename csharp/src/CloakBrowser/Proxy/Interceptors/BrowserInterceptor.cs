using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace CloakBrowser.Proxy.Interceptors
{
    public class BrowserInterceptor
    {
        private readonly IBrowser _browser;
        private readonly IPlaywright _playwright;
        private readonly CloakLaunchOptions _options;

        public BrowserInterceptor(IBrowser browser, IPlaywright playwright, CloakLaunchOptions options)
        {
            _browser = browser;
            _playwright = playwright;
            _options = options;
        }
        
        public CloakLaunchOptions Options => _options;

        public async Task CloseAsync()
        {
            try
            {
                await _browser.CloseAsync();
            }
            finally
            {
                _playwright.Dispose();
            }
        }

        // Intercept context creation to apply wrapping
        public async Task<IBrowserContext> NewContextAsync(BrowserNewContextOptions? options = null)
        {
            var contextOptions = ContextOptionsBuilder.BuildContextOptions(new CloakLaunchContextOptions
            {
                ContextOptions = options,
                UserAgent = _options is CloakLaunchContextOptions clc ? clc.UserAgent : null,
                Viewport = _options is CloakLaunchContextOptions clc2 ? clc2.Viewport : null,
                ColorScheme = _options is CloakLaunchContextOptions clc3 ? clc3.ColorScheme : null
            });

            var context = await _browser.NewContextAsync(contextOptions);
            var contextInterceptor = new ContextInterceptor(context, _options);
            return PlaywrightProxy<IBrowserContext>.Create(context, contextInterceptor);
        }

        public async Task<IPage> NewPageAsync(BrowserNewPageOptions? options = null)
        {
            // First create a context, then get its default page
            var contextOptions = ContextOptionsBuilder.BuildContextOptions(new CloakLaunchContextOptions
            {
                UserAgent = _options is CloakLaunchContextOptions clc ? clc.UserAgent : null,
                Viewport = _options is CloakLaunchContextOptions clc2 ? clc2.Viewport : null,
                ColorScheme = _options is CloakLaunchContextOptions clc3 ? clc3.ColorScheme : null
            });

            var context = await _browser.NewContextAsync(contextOptions);
            var contextInterceptor = new ContextInterceptor(context, _options);
            var wrappedContext = PlaywrightProxy<IBrowserContext>.Create(context, contextInterceptor);
            return await wrappedContext.NewPageAsync();
        }
    }
}
