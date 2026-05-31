using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;
using CloakBrowser.Human;

namespace CloakBrowser.Proxy.Interceptors
{
    public class ElementHandleInterceptor
    {
        private readonly IElementHandle _handle;
        private readonly IPage _page;
        private readonly PageState _pageState;
        private readonly IRawMouse _rawMouse;
        private readonly IRawKeyboard _rawKeyboard;
        private readonly string _selectAllKey;

        public ElementHandleInterceptor(IElementHandle handle, IPage page, PageState pageState, IRawMouse rawMouse, IRawKeyboard rawKeyboard)
        {
            _handle = handle;
            _page = page;
            _pageState = pageState;
            _rawMouse = rawMouse;
            _rawKeyboard = rawKeyboard;
            _selectAllKey = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Meta+a" : "Control+a";
        }

        public IPage Page => _page;
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

        private async Task<bool> IsInputElementAsync()
        {
            try
            {
                return await _handle.EvaluateAsync<bool>(@"(node) => {
                    const tag = node.tagName?.toLowerCase();
                    return tag === 'input' || tag === 'textarea' || node.getAttribute?.('contenteditable') === 'true';
                }");
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> IsFocusedAsync()
        {
            try
            {
                return await _handle.EvaluateAsync<bool>("(node) => node === document.activeElement");
            }
            catch
            {
                return false;
            }
        }

        public async Task ClickAsync(ElementHandleClickOptions? options = null)
        {
            await EnsureCursorInitAsync();
            var callCfg = _pageState.Config;
            float timeout = options?.Timeout ?? 30000;
            bool force = options?.Force ?? false;

            if (callCfg.IdleBetweenActions)
            {
                await Mouse.HumanIdleAsync(_rawMouse, (float)_pageState.X, (float)_pageState.Y, callCfg);
            }

            try
            {
                await _handle.ScrollIntoViewIfNeededAsync(new ElementHandleScrollIntoViewIfNeededOptions { Timeout = timeout });
            }
            catch { }

            var freshBox = await _handle.BoundingBoxAsync();
            if (freshBox == null) throw new InvalidOperationException("Element not found or not visible");

            var box = new ElementBounds(freshBox.X, freshBox.Y, freshBox.Width, freshBox.Height);
            bool isInput = await IsInputElementAsync();
            var target = Mouse.ClickTarget(new Clip { X = (float)box.X, Y = (float)box.Y, Width = (float)box.Width, Height = (float)box.Height }, isInput, callCfg);

            if (!force)
            {
                await Task.Delay(100);
                var freshBox2 = await _handle.BoundingBoxAsync();
                if (freshBox2 != null)
                {
                    box = new ElementBounds(freshBox2.X, freshBox2.Y, freshBox2.Width, freshBox2.Height);
                    target = Mouse.ClickTarget(new Clip { X = (float)box.X, Y = (float)box.Y, Width = (float)box.Width, Height = (float)box.Height }, isInput, callCfg);
                }
            }

            if (!force)
            {
                var hit = await _handle.EvaluateAsync<JsonElement>(@"expected => {
                    const rect = expected.getBoundingClientRect();
                    const target = document.elementFromPoint(rect.x + rect.width/2, rect.y + rect.height/2);
                    if (!target) return { hit: false };
                    let node = target;
                    while (node) { if (node === expected) return { hit: true }; node = node.parentNode; }
                    if (expected.contains(target)) return { hit: true };
                    return { hit: false, covering: target.tagName };
                }");
                bool isHit = false;
                if (hit.TryGetProperty("hit", out var hitProp)) isHit = hitProp.GetBoolean();
                if (!isHit)
                {
                    string cov = hit.TryGetProperty("covering", out var covProp) ? covProp.GetString() ?? "unknown" : "unknown";
                    throw new ElementNotReceivingEventsException("<ElementHandle>", cov);
                }
            }

            await Mouse.HumanMoveAsync(_rawMouse, (float)_pageState.X, (float)_pageState.Y, target.X, target.Y, callCfg);
            _pageState.X = target.X;
            _pageState.Y = target.Y;

            await Mouse.HumanClickAsync(_rawMouse, isInput, callCfg);
        }

        public async Task DblClickAsync(ElementHandleDblClickOptions? options = null)
        {
            await EnsureCursorInitAsync();
            var callCfg = _pageState.Config;
            float timeout = options?.Timeout ?? 30000;
            bool force = options?.Force ?? false;

            if (callCfg.IdleBetweenActions)
            {
                await Mouse.HumanIdleAsync(_rawMouse, (float)_pageState.X, (float)_pageState.Y, callCfg);
            }

            try
            {
                await _handle.ScrollIntoViewIfNeededAsync(new ElementHandleScrollIntoViewIfNeededOptions { Timeout = timeout });
            }
            catch { }

            var freshBox = await _handle.BoundingBoxAsync();
            if (freshBox == null) throw new InvalidOperationException("Element not found or not visible");

            var box = new ElementBounds(freshBox.X, freshBox.Y, freshBox.Width, freshBox.Height);
            bool isInput = await IsInputElementAsync();
            var target = Mouse.ClickTarget(new Clip { X = (float)box.X, Y = (float)box.Y, Width = (float)box.Width, Height = (float)box.Height }, isInput, callCfg);

            if (!force)
            {
                await Task.Delay(100);
                var freshBox2 = await _handle.BoundingBoxAsync();
                if (freshBox2 != null)
                {
                    box = new ElementBounds(freshBox2.X, freshBox2.Y, freshBox2.Width, freshBox2.Height);
                    target = Mouse.ClickTarget(new Clip { X = (float)box.X, Y = (float)box.Y, Width = (float)box.Width, Height = (float)box.Height }, isInput, callCfg);
                }
            }

            await Mouse.HumanMoveAsync(_rawMouse, (float)_pageState.X, (float)_pageState.Y, target.X, target.Y, callCfg);
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

        public async Task HoverAsync(ElementHandleHoverOptions? options = null)
        {
            await EnsureCursorInitAsync();
            var callCfg = _pageState.Config;
            float timeout = options?.Timeout ?? 30000;

            if (callCfg.IdleBetweenActions)
            {
                await Mouse.HumanIdleAsync(_rawMouse, (float)_pageState.X, (float)_pageState.Y, callCfg);
            }

            try
            {
                await _handle.ScrollIntoViewIfNeededAsync(new ElementHandleScrollIntoViewIfNeededOptions { Timeout = timeout });
            }
            catch { }

            var freshBox = await _handle.BoundingBoxAsync();
            if (freshBox == null) throw new InvalidOperationException("Element not found or not visible");

            var box = new ElementBounds(freshBox.X, freshBox.Y, freshBox.Width, freshBox.Height);
            var target = Mouse.ClickTarget(new Clip { X = (float)box.X, Y = (float)box.Y, Width = (float)box.Width, Height = (float)box.Height }, false, callCfg);

            await Mouse.HumanMoveAsync(_rawMouse, (float)_pageState.X, (float)_pageState.Y, target.X, target.Y, callCfg);
            _pageState.X = target.X;
            _pageState.Y = target.Y;
        }

        public async Task TypeAsync(string text, ElementHandleTypeOptions? options = null)
        {
            var callCfg = _pageState.Config;
            await ConfigResolver.DelayAsync(callCfg.FieldSwitchDelay);
            await ClickAsync();
            await Task.Delay(ConfigResolver.RandInt(100, 250));

            ICDPSession? cdp = _pageState.StealthEval != null ? await _pageState.StealthEval.GetCdpSessionAsync() : null;
            await Keyboard.HumanTypeAsync(_page, _rawKeyboard, text, callCfg, cdp);
        }

        public async Task FillAsync(string value, ElementHandleFillOptions? options = null)
        {
            var callCfg = _pageState.Config;
            await ConfigResolver.DelayAsync(callCfg.FieldSwitchDelay);
            await ClickAsync();
            await Task.Delay(ConfigResolver.RandInt(100, 250));

            await _page.Keyboard.PressAsync(_selectAllKey);
            await Task.Delay(ConfigResolver.RandInt(30, 80));
            await _page.Keyboard.PressAsync("Backspace");
            await Task.Delay(ConfigResolver.RandInt(50, 150));

            ICDPSession? cdp = _pageState.StealthEval != null ? await _pageState.StealthEval.GetCdpSessionAsync() : null;
            await Keyboard.HumanTypeAsync(_page, _rawKeyboard, value, callCfg, cdp);
        }


        public async Task CheckAsync(ElementHandleCheckOptions? options = null)
        {
            var callCfg = _pageState.Config;
            if (callCfg.IdleBetweenActions)
            {
                await Mouse.HumanIdleAsync(_rawMouse, (float)_pageState.X, (float)_pageState.Y, callCfg);
            }

            bool isChecked = false;
            try { isChecked = await _handle.IsCheckedAsync(); } catch { }

            if (!isChecked)
            {
                await ClickAsync();
            }
        }

        public async Task UncheckAsync(ElementHandleUncheckOptions? options = null)
        {
            var callCfg = _pageState.Config;
            if (callCfg.IdleBetweenActions)
            {
                await Mouse.HumanIdleAsync(_rawMouse, (float)_pageState.X, (float)_pageState.Y, callCfg);
            }

            bool isChecked = true;
            try { isChecked = await _handle.IsCheckedAsync(); } catch { }

            if (isChecked)
            {
                await ClickAsync();
            }
        }

        public async Task<IReadOnlyList<string>> SelectOptionAsync(string value, ElementHandleSelectOptionOptions? options = null)
        {
            await HoverAsync();
            await Task.Delay(ConfigResolver.RandInt(100, 300));
            return await _handle.SelectOptionAsync(value, options);
        }

        public async Task<IReadOnlyList<string>> SelectOptionAsync(IElementHandle value, ElementHandleSelectOptionOptions? options = null)
        {
            await HoverAsync();
            await Task.Delay(ConfigResolver.RandInt(100, 300));
            return await _handle.SelectOptionAsync(value, options);
        }

        public async Task<IReadOnlyList<string>> SelectOptionAsync(IEnumerable<string> values, ElementHandleSelectOptionOptions? options = null)
        {
            await HoverAsync();
            await Task.Delay(ConfigResolver.RandInt(100, 300));
            return await _handle.SelectOptionAsync(values, options);
        }

        public async Task<IReadOnlyList<string>> SelectOptionAsync(IEnumerable<IElementHandle> values, ElementHandleSelectOptionOptions? options = null)
        {
            await HoverAsync();
            await Task.Delay(ConfigResolver.RandInt(100, 300));
            return await _handle.SelectOptionAsync(values, options);
        }

        public async Task PressAsync(string key, ElementHandlePressOptions? options = null)
        {
            if (!await IsFocusedAsync())
            {
                await ClickAsync();
            }

            await Task.Delay(ConfigResolver.RandInt(50, 150));
            await _page.Keyboard.PressAsync(key);
        }

        public async Task TapAsync(ElementHandleTapOptions? options = null)
        {
            await ClickAsync();
        }
    }
}
