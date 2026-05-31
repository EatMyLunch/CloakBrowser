using System;
using System.Threading.Tasks;
using NUnit.Framework;
using CloakBrowser;

namespace CloakBrowser.Tests
{
    [TestFixture]
    public class LaunchTests
    {
        [Test]
        public async Task TestLaunchAndClose()
        {
            var browser = await Cloak.LaunchAsync(new CloakLaunchOptions { Headless = true });
            Assert.That(browser, Is.Not.Null);
            Assert.That(browser.IsConnected, Is.True);
            await browser.CloseAsync();
        }

        [Test]
        public async Task TestLaunchNewPageAndNavigate()
        {
            var browser = await Cloak.LaunchAsync(new CloakLaunchOptions { Headless = true });
            var page = await browser.NewPageAsync();
            Assert.That(page, Is.Not.Null);

            await page.GotoAsync("https://example.com");
            var title = await page.TitleAsync();
            Assert.That(title, Does.Contain("Example Domain"));

            await browser.CloseAsync();
        }

        [Test]
        public async Task TestStealthWebdriverPatched()
        {
            var browser = await Cloak.LaunchAsync(new CloakLaunchOptions { Headless = true });
            var page = await browser.NewPageAsync();
            await page.GotoAsync("https://example.com");

            // navigator.webdriver should be false
            var webdriver = await page.EvaluateAsync<bool>("navigator.webdriver");
            Assert.That(webdriver, Is.False, "navigator.webdriver should be spoofed to false");

            // window.chrome should exist and be of type object
            var chromeType = await page.EvaluateAsync<string>("typeof window.chrome");
            Assert.That(chromeType, Is.EqualTo("object"), "window.chrome should be defined as an object");

            await browser.CloseAsync();
        }
    }
}
