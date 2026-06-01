using System;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Microsoft.Playwright;
using Playwright.CloakBrowser.Community;
using Playwright.CloakBrowser.Community.Proxy;
using Playwright.CloakBrowser.Community.Proxy.Interceptors;

namespace Playwright.CloakBrowser.Community.Tests
{
    [TestFixture]
    public class HumanizeTests
    {
        private PageInterceptor? GetPageInterceptor(IPage page)
        {
            var field = typeof(PlaywrightProxy<IPage>).GetField("_interceptor", BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(page) as PageInterceptor;
        }

        [Test]
        public async Task TestMouseMovementUpdatesState()
        {
            var browser = await Cloak.LaunchAsync(new CloakLaunchOptions 
            { 
                Headless = true,
                Humanize = true
            });
            var page = await browser.NewPageAsync();
            var interceptor = GetPageInterceptor(page);

            Assert.That(interceptor, Is.Not.Null);
            var state = interceptor!.PageState;
            Assert.That(state, Is.Not.Null);

            // Navigate to blank page
            await page.GotoAsync("about:blank");

            // Initial coordinates are uninitialized until first mouse move/action
            // Let's trigger a movement
            await page.Mouse.MoveAsync(150, 200);

            // The PageState coordinates should now be updated to (150, 200)
            Assert.That(state.X, Is.EqualTo(150.0).Within(1.0));
            Assert.That(state.Y, Is.EqualTo(200.0).Within(1.0));

            // Move somewhere else
            await page.Mouse.MoveAsync(300, 450);
            Assert.That(state.X, Is.EqualTo(300.0).Within(1.0));
            Assert.That(state.Y, Is.EqualTo(450.0).Within(1.0));

            await browser.CloseAsync();
        }

        [Test]
        public async Task TestHumanizeClickAndHover()
        {
            var browser = await Cloak.LaunchAsync(new CloakLaunchOptions 
            { 
                Headless = true,
                Humanize = true
            });
            var page = await browser.NewPageAsync();
            var interceptor = GetPageInterceptor(page);
            var state = interceptor!.PageState;

            // Set up a simple clickable button in DOM
            await page.GotoAsync("data:text/html,<html><body><button id='test-btn' style='width: 100px; height: 50px; margin-top: 100px; margin-left: 100px;'>Click Me</button></body></html>");

            // Click the button
            await page.ClickAsync("#test-btn");

            // The cursor should have moved somewhere within the button bounds (100, 100, width: 100, height: 50)
            Assert.That(state.X, Is.GreaterThanOrEqualTo(100.0));
            Assert.That(state.X, Is.LessThanOrEqualTo(200.0));
            Assert.That(state.Y, Is.GreaterThanOrEqualTo(100.0));
            Assert.That(state.Y, Is.LessThanOrEqualTo(150.0));

            await browser.CloseAsync();
        }
    }
}

