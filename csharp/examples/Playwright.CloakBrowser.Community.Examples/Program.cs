using System;
using System.IO;
using System.Threading.Tasks;
using Playwright.CloakBrowser.Community;

namespace Playwright.CloakBrowser.Community.Examples
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=================================================");
            Console.WriteLine("         CloakBrowser C# Examples                 ");
            Console.WriteLine("=================================================");

            try
            {
                await RunBasicExampleAsync();
                await RunPersistentContextExampleAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error running examples: {ex}");
            }

            Console.WriteLine("All examples completed!");
        }

        static async Task RunBasicExampleAsync()
        {
            Console.WriteLine("\n--- Running Basic Example ---");
            Console.WriteLine("Launching stealth browser...");

            var options = new CloakLaunchOptions
            {
                Headless = true, // Set to false to see the browser UI
                Humanize = true  // Enable human-like mouse/keyboard behaviors
            };

            var browser = await Cloak.LaunchAsync(options);
            var page = await browser.NewPageAsync();

            Console.WriteLine("Navigating to https://example.com ...");
            await page.GotoAsync("https://example.com");

            var title = await page.TitleAsync();
            Console.WriteLine($"Page Title: {title}");
            Console.WriteLine($"Page URL: {page.Url}");

            Console.WriteLine("Closing browser...");
            await browser.CloseAsync();
        }

        static async Task RunPersistentContextExampleAsync()
        {
            Console.WriteLine("\n--- Running Persistent Context Example ---");
            string profileDir = Path.Combine(Directory.GetCurrentDirectory(), "cloak-test-profile");
            if (Directory.Exists(profileDir))
            {
                Directory.Delete(profileDir, true);
            }

            // Session 1 - Set cookie/localStorage
            Console.WriteLine("=== Session 1: Setting state ===");
            var options1 = new CloakLaunchPersistentContextOptions
            {
                Headless = true,
                Humanize = true
            };

            var context1 = await Cloak.LaunchPersistentContextAsync(profileDir, options1);
            var page1 = context1.Pages.Count > 0 ? context1.Pages[0] : await context1.NewPageAsync();

            Console.WriteLine("Navigating to https://example.com ...");
            await page1.GotoAsync("https://example.com");

            Console.WriteLine("Setting cookie and localStorage values...");
            await page1.EvaluateAsync("document.cookie = 'session=abc123_dotnet; path=/; max-age=3600'");
            await page1.EvaluateAsync("localStorage.setItem('user', 'returning_dotnet')");

            var cookie1 = await page1.EvaluateAsync<string>("document.cookie");
            var localVal1 = await page1.EvaluateAsync<string>("localStorage.getItem('user')");

            Console.WriteLine($"Session 1 Cookie: {cookie1}");
            Console.WriteLine($"Session 1 localStorage: {localVal1}");

            Console.WriteLine("Closing context 1...");
            await context1.CloseAsync();

            // Session 2 - Verify cookie/localStorage are restored
            Console.WriteLine("=== Session 2: Verifying state persistence ===");
            var options2 = new CloakLaunchPersistentContextOptions
            {
                Headless = true,
                Humanize = true
            };

            var context2 = await Cloak.LaunchPersistentContextAsync(profileDir, options2);
            var page2 = context2.Pages.Count > 0 ? context2.Pages[0] : await context2.NewPageAsync();

            Console.WriteLine("Navigating to https://example.com ...");
            await page2.GotoAsync("https://example.com");

            var cookie2 = await page2.EvaluateAsync<string>("document.cookie");
            var localVal2 = await page2.EvaluateAsync<string>("localStorage.getItem('user')");

            Console.WriteLine($"Session 2 Cookie: {cookie2}");
            Console.WriteLine($"Session 2 localStorage: {localVal2}");

            Console.WriteLine("Closing context 2...");
            await context2.CloseAsync();

            // Clean up profile dir
            try
            {
                if (Directory.Exists(profileDir))
                {
                    Directory.Delete(profileDir, true);
                }
            }
            catch { }
        }
    }
}

