using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Playwright.CloakBrowser.Community.Human;

namespace Playwright.CloakBrowser.Community.Proxy.Interceptors
{
    public class PageInterceptor
    {
        private readonly IPage _page;
        private readonly CloakLaunchOptions _options;
        private readonly PageState _pageState;
        private readonly PlaywrightRawMouse _rawMouse;
        private readonly PlaywrightRawKeyboard _rawKeyboard;
        private readonly string _selectAllKey;

        public PageInterceptor(IPage page, CloakLaunchOptions options)
        {
            _page = page;
            _options = options;
            
            var config = ConfigResolver.ResolveConfig(options.HumanPreset, options.HumanConfig);
            _pageState = new PageState(config)
            {
                StealthEval = new StealthEval(page)
            };
            
            _rawMouse = new PlaywrightRawMouse(page.Mouse);
            _rawKeyboard = new PlaywrightRawKeyboard(page.Keyboard);
            _selectAllKey = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Meta+a" : "Control+a";
        }

        public IPage Page => _page;
        public CloakLaunchOptions Options => _options;
        public PageState PageState => _pageState;
        public IRawMouse RawMouse => _rawMouse;
        public IRawKeyboard RawKeyboard => _rawKeyboard;

        private async Task EnsureCursorInitAsync()
        {
            if (!_pageState.Initialized)
            {
                _pageState.X = ConfigResolver.RandIntRange(_pageState.Config.InitialCursorX);
                _pageState.Y = ConfigResolver.RandIntRange(_pageState.Config.InitialCursorY);
                _pageState.Initialized = true;
            }
            await Task.CompletedTask;
        }

        private async Task<ICDPSession?> EnsureCdpAsync()
        {
            if (_pageState.StealthEval != null)
            {
                return await _pageState.StealthEval.GetCdpSessionAsync();
            }
            return null;
        }

        private async Task<bool> IsInputElementAsync(string selector)
        {
            if (_pageState.StealthEval != null)
            {
                try
                {
                    string escaped = JsonEncodedText.Encode(selector).ToString();
                    var result = await _pageState.StealthEval.EvaluateAsync($@"(() => {{
                        const el = document.querySelector('{escaped}');
                        if (!el) return false;
                        const tag = el.tagName.toLowerCase();
                        return tag === 'input' || tag === 'textarea' || el.getAttribute('contenteditable') === 'true';
                    }})()");
                    if (result.HasValue) return result.Value.GetBoolean();
                }
                catch { }
            }

            try
            {
                return await _page.EvaluateAsync<bool>($@"(sel) => {{
                    const el = document.querySelector(sel);
                    if (!el) return false;
                    const tag = el.tagName.toLowerCase();
                    return tag === 'input' || tag === 'textarea' || el.getAttribute('contenteditable') === 'true';
                }}", selector);
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> IsSelectorFocusedAsync(string selector)
        {
            if (_pageState.StealthEval != null)
            {
                try
                {
                    string escaped = JsonEncodedText.Encode(selector).ToString();
                    var result = await _pageState.StealthEval.EvaluateAsync($@"(() => {{
                        const el = document.querySelector('{escaped}');
                        return el === document.activeElement;
                    }})()");
                    if (result.HasValue) return result.Value.GetBoolean();
                }
                catch { }
            }

            try
            {
                return await _page.EvaluateAsync<bool>($@"(sel) => {{
                    const el = document.querySelector(sel);
                    return el === document.activeElement;
                }}", selector);
            }
            catch
            {
                return false;
            }
        }

        // --- Patched methods ---

        public async Task<IResponse?> GotoAsync(string url, PageGotoOptions? options = null)
        {
            var response = await _page.GotoAsync(url, options);
            _pageState.StealthEval?.Invalidate();
            return response;
        }

        public async Task ClickAsync(string selector, PageClickOptions? options = null)
        {
            await EnsureCursorInitAsync();
            var callCfg = _pageState.Config; // C# does not support dynamic option merge as easily without reflection, but we use the resolved page config
            float timeout = options?.Timeout ?? 30000;
            bool force = options?.Force ?? false;

            if (!force)
            {
                await Actionability.EnsureActionableAsync(_page, selector, Actionability.ChecksClick, timeout, force);
            }

            if (callCfg.IdleBetweenActions)
            {
                await Playwright.CloakBrowser.Community.Human.Mouse.HumanIdleAsync(_rawMouse, (float)_pageState.X, (float)_pageState.Y, callCfg);
            }

            var scrollRes = await Scroll.ScrollToElementAsync(_page, _rawMouse, selector, (float)_pageState.X, (float)_pageState.Y, callCfg, timeout);
            _pageState.X = scrollRes.CursorX;
            _pageState.Y = scrollRes.CursorY;

            bool isInput = await IsInputElementAsync(selector);
            var finalBox = scrollRes.Box;

            if (!force && scrollRes.DidScroll)
            {
                await Actionability.EnsureStableAsync(_page, selector, timeout);
                var freshBox = await _page.Locator(selector).First.BoundingBoxAsync(new LocatorBoundingBoxOptions { Timeout = Math.Max(1, timeout) });
                if (freshBox != null)
                {
                    finalBox = new ElementBounds(freshBox.X, freshBox.Y, freshBox.Width, freshBox.Height);
                }
            }

            var target = Playwright.CloakBrowser.Community.Human.Mouse.ClickTarget(new Clip { X = (float)finalBox.X, Y = (float)finalBox.Y, Width = (float)finalBox.Width, Height = (float)finalBox.Height }, isInput, callCfg);
            if (!force)
            {
                await Actionability.CheckPointerEventsAsync(_page, selector, target.X, target.Y, timeout);
            }

            await Playwright.CloakBrowser.Community.Human.Mouse.HumanMoveAsync(_rawMouse, (float)_pageState.X, (float)_pageState.Y, target.X, target.Y, callCfg);
            _pageState.X = target.X;
            _pageState.Y = target.Y;

            await Playwright.CloakBrowser.Community.Human.Mouse.HumanClickAsync(_rawMouse, isInput, callCfg);
        }

        public async Task DblClickAsync(string selector, PageDblClickOptions? options = null)
        {
            await EnsureCursorInitAsync();
            var callCfg = _pageState.Config;
            float timeout = options?.Timeout ?? 30000;
            bool force = options?.Force ?? false;

            if (!force)
            {
                await Actionability.EnsureActionableAsync(_page, selector, Actionability.ChecksClick, timeout, force);
            }

            if (callCfg.IdleBetweenActions)
            {
                await Playwright.CloakBrowser.Community.Human.Mouse.HumanIdleAsync(_rawMouse, (float)_pageState.X, (float)_pageState.Y, callCfg);
            }

            var scrollRes = await Scroll.ScrollToElementAsync(_page, _rawMouse, selector, (float)_pageState.X, (float)_pageState.Y, callCfg, timeout);
            _pageState.X = scrollRes.CursorX;
            _pageState.Y = scrollRes.CursorY;

            bool isInput = await IsInputElementAsync(selector);
            var finalBox = scrollRes.Box;

            if (!force && scrollRes.DidScroll)
            {
                await Actionability.EnsureStableAsync(_page, selector, timeout);
                var freshBox = await _page.Locator(selector).First.BoundingBoxAsync(new LocatorBoundingBoxOptions { Timeout = Math.Max(1, timeout) });
                if (freshBox != null)
                {
                    finalBox = new ElementBounds(freshBox.X, freshBox.Y, freshBox.Width, freshBox.Height);
                }
            }

            var target = Playwright.CloakBrowser.Community.Human.Mouse.ClickTarget(new Clip { X = (float)finalBox.X, Y = (float)finalBox.Y, Width = (float)finalBox.Width, Height = (float)finalBox.Height }, isInput, callCfg);
            if (!force)
            {
                await Actionability.CheckPointerEventsAsync(_page, selector, target.X, target.Y, timeout);
            }

            await Playwright.CloakBrowser.Community.Human.Mouse.HumanMoveAsync(_rawMouse, (float)_pageState.X, (float)_pageState.Y, target.X, target.Y, callCfg);
            _pageState.X = target.X;
            _pageState.Y = target.Y;

            await _rawMouse.DownAsync();
            await Task.Delay(ConfigResolver.RandInt(30, 60));
            await _rawMouse.UpAsync();
            await Task.Delay(ConfigResolver.RandInt(30, 60));
            await _rawMouse.DownAsync();
            await Task.Delay(ConfigResolver.RandInt(30, 60));
            await _rawMouse.UpAsync();
        }

        public async Task HoverAsync(string selector, PageHoverOptions? options = null)
        {
            await EnsureCursorInitAsync();
            var callCfg = _pageState.Config;
            float timeout = options?.Timeout ?? 30000;
            bool force = options?.Force ?? false;

            if (!force)
            {
                await Actionability.EnsureActionableAsync(_page, selector, Actionability.ChecksHover, timeout, force);
            }

            if (callCfg.IdleBetweenActions)
            {
                await Playwright.CloakBrowser.Community.Human.Mouse.HumanIdleAsync(_rawMouse, (float)_pageState.X, (float)_pageState.Y, callCfg);
            }

            var scrollRes = await Scroll.ScrollToElementAsync(_page, _rawMouse, selector, (float)_pageState.X, (float)_pageState.Y, callCfg, timeout);
            _pageState.X = scrollRes.CursorX;
            _pageState.Y = scrollRes.CursorY;

            var finalBox = scrollRes.Box;
            if (!force && scrollRes.DidScroll)
            {
                await Actionability.EnsureStableAsync(_page, selector, timeout);
                var freshBox = await _page.Locator(selector).First.BoundingBoxAsync(new LocatorBoundingBoxOptions { Timeout = Math.Max(1, timeout) });
                if (freshBox != null)
                {
                    finalBox = new ElementBounds(freshBox.X, freshBox.Y, freshBox.Width, freshBox.Height);
                }
            }

            var target = Playwright.CloakBrowser.Community.Human.Mouse.ClickTarget(new Clip { X = (float)finalBox.X, Y = (float)finalBox.Y, Width = (float)finalBox.Width, Height = (float)finalBox.Height }, false, callCfg);
            if (!force)
            {
                await Actionability.CheckPointerEventsAsync(_page, selector, target.X, target.Y, timeout);
            }

            await Playwright.CloakBrowser.Community.Human.Mouse.HumanMoveAsync(_rawMouse, (float)_pageState.X, (float)_pageState.Y, target.X, target.Y, callCfg);
            _pageState.X = target.X;
            _pageState.Y = target.Y;
        }

        public async Task TypeAsync(string selector, string text, PageTypeOptions? options = null)
        {
            var callCfg = _pageState.Config;
            float timeout = 30000; // PageTypeOptions doesn't have Timeout
            bool force = false;

            await Actionability.EnsureActionableAsync(_page, selector, Actionability.ChecksInput, timeout, force);

            await ConfigResolver.DelayAsync(callCfg.FieldSwitchDelay);
            await ClickAsync(selector, new PageClickOptions { Timeout = timeout, Force = force });
            await Task.Delay(ConfigResolver.RandInt(100, 250));

            var cdp = await EnsureCdpAsync();
            await Playwright.CloakBrowser.Community.Human.Keyboard.HumanTypeAsync(_page, _rawKeyboard, text, callCfg, cdp);
        }

        public async Task FillAsync(string selector, string value, PageFillOptions? options = null)
        {
            var callCfg = _pageState.Config;
            float timeout = options?.Timeout ?? 30000;
            bool force = options?.Force ?? false;

            if (!force)
            {
                await Actionability.EnsureActionableAsync(_page, selector, Actionability.ChecksInput, timeout, force);
            }

            await ConfigResolver.DelayAsync(callCfg.FieldSwitchDelay);
            await ClickAsync(selector, new PageClickOptions { Timeout = timeout, Force = force });
            await Task.Delay(ConfigResolver.RandInt(100, 250));

            await _page.Keyboard.PressAsync(_selectAllKey);
            await Task.Delay(ConfigResolver.RandInt(30, 80));
            await _page.Keyboard.PressAsync("Backspace");
            await Task.Delay(ConfigResolver.RandInt(50, 150));

            var cdp = await EnsureCdpAsync();
            await Playwright.CloakBrowser.Community.Human.Keyboard.HumanTypeAsync(_page, _rawKeyboard, value, callCfg, cdp);
        }



        public async Task CheckAsync(string selector, PageCheckOptions? options = null)
        {
            var callCfg = _pageState.Config;
            float timeout = options?.Timeout ?? 30000;
            bool force = options?.Force ?? false;

            if (!force)
            {
                await Actionability.EnsureActionableAsync(_page, selector, Actionability.ChecksCheck, timeout, force);
            }

            if (callCfg.IdleBetweenActions)
            {
                await Playwright.CloakBrowser.Community.Human.Mouse.HumanIdleAsync(_rawMouse, (float)_pageState.X, (float)_pageState.Y, callCfg);
            }

            bool isChecked = false;
            try { isChecked = await _page.IsCheckedAsync(selector); } catch { }

            if (!isChecked)
            {
                await ClickAsync(selector, new PageClickOptions { Timeout = timeout, Force = force });
            }
        }

        public async Task UncheckAsync(string selector, PageUncheckOptions? options = null)
        {
            var callCfg = _pageState.Config;
            float timeout = options?.Timeout ?? 30000;
            bool force = options?.Force ?? false;

            if (!force)
            {
                await Actionability.EnsureActionableAsync(_page, selector, Actionability.ChecksCheck, timeout, force);
            }

            if (callCfg.IdleBetweenActions)
            {
                await Playwright.CloakBrowser.Community.Human.Mouse.HumanIdleAsync(_rawMouse, (float)_pageState.X, (float)_pageState.Y, callCfg);
            }

            bool isChecked = true;
            try { isChecked = await _page.IsCheckedAsync(selector); } catch { }

            if (isChecked)
            {
                await ClickAsync(selector, new PageClickOptions { Timeout = timeout, Force = force });
            }
        }

        public async Task<IReadOnlyList<string>> SelectOptionAsync(string selector, string value, PageSelectOptionOptions? options = null)
        {
            float timeout = options?.Timeout ?? 30000;
            bool force = options?.Force ?? false;

            if (!force)
            {
                await Actionability.EnsureActionableAsync(_page, selector, Actionability.ChecksFocus, timeout, force);
            }

            await HoverAsync(selector, new PageHoverOptions { Timeout = timeout, Force = force });
            await Task.Delay(ConfigResolver.RandInt(100, 300));
            return await _page.SelectOptionAsync(selector, value, options);
        }

        public async Task<IReadOnlyList<string>> SelectOptionAsync(string selector, IElementHandle value, PageSelectOptionOptions? options = null)
        {
            float timeout = options?.Timeout ?? 30000;
            bool force = options?.Force ?? false;

            if (!force)
            {
                await Actionability.EnsureActionableAsync(_page, selector, Actionability.ChecksFocus, timeout, force);
            }

            await HoverAsync(selector, new PageHoverOptions { Timeout = timeout, Force = force });
            await Task.Delay(ConfigResolver.RandInt(100, 300));
            return await _page.SelectOptionAsync(selector, value, options);
        }

        public async Task<IReadOnlyList<string>> SelectOptionAsync(string selector, IEnumerable<string> values, PageSelectOptionOptions? options = null)
        {
            float timeout = options?.Timeout ?? 30000;
            bool force = options?.Force ?? false;

            if (!force)
            {
                await Actionability.EnsureActionableAsync(_page, selector, Actionability.ChecksFocus, timeout, force);
            }

            await HoverAsync(selector, new PageHoverOptions { Timeout = timeout, Force = force });
            await Task.Delay(ConfigResolver.RandInt(100, 300));
            return await _page.SelectOptionAsync(selector, values, options);
        }

        public async Task<IReadOnlyList<string>> SelectOptionAsync(string selector, IEnumerable<IElementHandle> values, PageSelectOptionOptions? options = null)
        {
            float timeout = options?.Timeout ?? 30000;
            bool force = options?.Force ?? false;

            if (!force)
            {
                await Actionability.EnsureActionableAsync(_page, selector, Actionability.ChecksFocus, timeout, force);
            }

            await HoverAsync(selector, new PageHoverOptions { Timeout = timeout, Force = force });
            await Task.Delay(ConfigResolver.RandInt(100, 300));
            return await _page.SelectOptionAsync(selector, values, options);
        }

        public async Task PressAsync(string selector, string key, PagePressOptions? options = null)
        {
            float timeout = options?.Timeout ?? 30000;
            bool force = false; // PagePressOptions does not have Force

            await Actionability.EnsureActionableAsync(_page, selector, Actionability.ChecksFocus, timeout, force);

            if (!await IsSelectorFocusedAsync(selector))
            {
                await ClickAsync(selector, new PageClickOptions { Timeout = timeout, Force = force });
            }

            await Task.Delay(ConfigResolver.RandInt(50, 150));
            await _page.Keyboard.PressAsync(key);
        }

        public async Task TapAsync(string selector, PageTapOptions? options = null)
        {
            var clickOpts = options != null ? new PageClickOptions { Timeout = options.Timeout, Force = options.Force, NoWaitAfter = options.NoWaitAfter } : null;
            await ClickAsync(selector, clickOpts);
        }

        // --- Custom Mouse / Keyboard wraps ---

        public IMouse Mouse => PlaywrightProxy<IMouse>.Create(_page.Mouse, new MouseProxyInterceptor(_rawMouse, _pageState));
        
        public IKeyboard Keyboard => PlaywrightProxy<IKeyboard>.Create(_page.Keyboard, new KeyboardProxyInterceptor(_page, _rawKeyboard, _pageState));

        // --- Sub-wrappers for raw input interfaces ---

        private class PlaywrightRawMouse : IRawMouse
        {
            private readonly IMouse _mouse;
            public PlaywrightRawMouse(IMouse mouse) { _mouse = mouse; }
            public Task MoveAsync(float x, float y) => _mouse.MoveAsync(x, y);
            public Task DownAsync() => _mouse.DownAsync();
            public Task UpAsync() => _mouse.UpAsync();
            public Task WheelAsync(float deltaX, float deltaY) => _mouse.WheelAsync(deltaX, deltaY);
        }

        private class PlaywrightRawKeyboard : IRawKeyboard
        {
            private readonly IKeyboard _keyboard;
            public PlaywrightRawKeyboard(IKeyboard keyboard) { _keyboard = keyboard; }
            public Task DownAsync(string key) => _keyboard.DownAsync(key);
            public Task UpAsync(string key) => _keyboard.UpAsync(key);
            public Task TypeAsync(string text, KeyboardTypeOptions? options = null) => _keyboard.TypeAsync(text, options);
            public Task InsertTextAsync(string text) => _keyboard.InsertTextAsync(text);
        }

        // --- Mouse / Keyboard Proxy Interceptors ---

        private class MouseProxyInterceptor
        {
            private readonly IRawMouse _raw;
            private readonly PageState _state;

            public MouseProxyInterceptor(IRawMouse raw, PageState state)
            {
                _raw = raw;
                _state = state;
            }

            public async Task MoveAsync(float x, float y, MouseMoveOptions? options = null)
            {
                if (!_state.Initialized)
                {
                    _state.X = ConfigResolver.RandIntRange(_state.Config.InitialCursorX);
                    _state.Y = ConfigResolver.RandIntRange(_state.Config.InitialCursorY);
                    _state.Initialized = true;
                }
                await Playwright.CloakBrowser.Community.Human.Mouse.HumanMoveAsync(_raw, (float)_state.X, (float)_state.Y, x, y, _state.Config);
                _state.X = x;
                _state.Y = y;
            }

            public async Task ClickAsync(float x, float y, MouseClickOptions? options = null)
            {
                await MoveAsync(x, y);
                await Playwright.CloakBrowser.Community.Human.Mouse.HumanClickAsync(_raw, false, _state.Config);
            }
        }

        private class KeyboardProxyInterceptor
        {
            private readonly IPage _page;
            private readonly IRawKeyboard _raw;
            private readonly PageState _state;

            public KeyboardProxyInterceptor(IPage page, IRawKeyboard raw, PageState state)
            {
                _page = page;
                _raw = raw;
                _state = state;
            }

            public async Task TypeAsync(string text, KeyboardTypeOptions? options = null)
            {
                ICDPSession? cdp = null;
                if (_state.StealthEval != null)
                {
                    cdp = await _state.StealthEval.GetCdpSessionAsync();
                }
                await Playwright.CloakBrowser.Community.Human.Keyboard.HumanTypeAsync(_page, _raw, text, _state.Config, cdp);
            }
        }
    }
}

