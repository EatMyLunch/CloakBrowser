using System;
using Microsoft.Playwright;

namespace CloakBrowser
{
    public static class ContextOptionsBuilder
    {
        public static BrowserNewContextOptions BuildContextOptions(CloakLaunchContextOptions options)
        {
            var ctx = options.ContextOptions ?? new BrowserNewContextOptions();

            if (ctx.Locale != null)
            {
                Console.WriteLine("[cloakbrowser] ContextOptions.Locale ignored — use top-level Locale option instead (routes through binary flag, avoiding detectable CDP emulation).");
                ctx.Locale = null;
            }
            if (ctx.TimezoneId != null)
            {
                Console.WriteLine("[cloakbrowser] ContextOptions.TimezoneId ignored — use top-level Timezone option instead (routes through binary flag, avoiding detectable CDP emulation).");
                ctx.TimezoneId = null;
            }

            if (!string.IsNullOrEmpty(options.UserAgent))
            {
                ctx.UserAgent = options.UserAgent;
            }

            if (options.Viewport != null)
            {
                ctx.ViewportSize = new Microsoft.Playwright.ViewportSize
                {
                    Width = options.Viewport.Value.Width,
                    Height = options.Viewport.Value.Height
                };
            }
            else
            {
                // If not specified, use realistic maximized screen size
                ctx.ViewportSize = new Microsoft.Playwright.ViewportSize
                {
                    Width = Config.DefaultViewport.Width,
                    Height = Config.DefaultViewport.Height
                };
            }

            if (!string.IsNullOrEmpty(options.ColorScheme))
            {
                if (Enum.TryParse<ColorScheme>(options.ColorScheme, true, out var scheme))
                {
                    ctx.ColorScheme = scheme;
                }
            }

            return ctx;
        }
    }
}
