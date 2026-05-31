using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace CloakBrowser.Human
{
    public class ActionabilityException : Exception
    {
        public string Selector { get; }
        public string Check { get; }

        public ActionabilityException(string selector, string check, string message)
            : base($"Element \"{selector}\" failed {check} check: {message}")
        {
            Selector = selector;
            Check = check;
        }
    }

    public class ElementNotAttachedException : ActionabilityException
    {
        public ElementNotAttachedException(string selector)
            : base(selector, "attached", "element not found in DOM") { }
    }

    public class ElementNotVisibleException : ActionabilityException
    {
        public ElementNotVisibleException(string selector)
            : base(selector, "visible", "element is not visible") { }
    }

    public class ElementNotStableException : ActionabilityException
    {
        public ElementNotStableException(string selector)
            : base(selector, "stable", "element position is still changing") { }
    }

    public class ElementNotEnabledException : ActionabilityException
    {
        public ElementNotEnabledException(string selector)
            : base(selector, "enabled", "element is disabled") { }
    }

    public class ElementNotEditableException : ActionabilityException
    {
        public ElementNotEditableException(string selector)
            : base(selector, "editable", "element is not editable") { }
    }

    public class ElementNotReceivingEventsException : ActionabilityException
    {
        public string CoveringTag { get; }
        public ElementNotReceivingEventsException(string selector, string coveringTag = "unknown")
            : base(selector, "pointer_events", $"element is covered by <{coveringTag}>")
        {
            CoveringTag = coveringTag;
        }
    }

    public enum CheckName
    {
        Attached,
        Visible,
        Enabled,
        Editable,
        PointerEvents
    }

    public static class Actionability
    {
        public static readonly HashSet<CheckName> ChecksClick = new HashSet<CheckName> { CheckName.Attached, CheckName.Visible, CheckName.Enabled, CheckName.PointerEvents };
        public static readonly HashSet<CheckName> ChecksHover = new HashSet<CheckName> { CheckName.Attached, CheckName.Visible, CheckName.PointerEvents };
        public static readonly HashSet<CheckName> ChecksInput = new HashSet<CheckName> { CheckName.Attached, CheckName.Visible, CheckName.Enabled, CheckName.Editable, CheckName.PointerEvents };
        public static readonly HashSet<CheckName> ChecksFocus = new HashSet<CheckName> { CheckName.Attached, CheckName.Visible, CheckName.Enabled };
        public static readonly HashSet<CheckName> ChecksCheck = new HashSet<CheckName> { CheckName.Attached, CheckName.Visible, CheckName.Enabled, CheckName.PointerEvents };

        private static readonly int[] BackoffMs = new[] { 100, 250, 500, 1000 };

        private static async Task BackoffSleepAsync(int attempt)
        {
            int idx = Math.Min(attempt, BackoffMs.Length - 1);
            await Task.Delay(BackoffMs[idx]);
        }

        public static async Task EnsureActionableAsync(
            IPage page,
            string selector,
            HashSet<CheckName> checks,
            float timeout = 30000,
            bool force = false)
        {
            if (force) return;

            var deadline = DateTime.UtcNow.AddMilliseconds(timeout);
            int attempt = 0;
            ActionabilityException? lastError = null;

            while (true)
            {
                var remainingMs = (float)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remainingMs <= 0)
                {
                    if (lastError != null) throw lastError;
                    throw new ActionabilityException(selector, "timeout", "timeout expired before first check");
                }

                try
                {
                    var loc = page.Locator(selector).First;

                    if (checks.Contains(CheckName.Attached))
                    {
                        try
                        {
                            await loc.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = Math.Max(1, Math.Min(remainingMs, 2000)) });
                        }
                        catch
                        {
                            throw new ElementNotAttachedException(selector);
                        }
                    }

                    if (checks.Contains(CheckName.Visible))
                    {
                        if (!await loc.IsVisibleAsync()) throw new ElementNotVisibleException(selector);
                    }

                    if (checks.Contains(CheckName.Enabled))
                    {
                        if (!await loc.IsEnabledAsync()) throw new ElementNotEnabledException(selector);
                    }

                    if (checks.Contains(CheckName.Editable))
                    {
                        if (!await loc.IsEditableAsync()) throw new ElementNotEditableException(selector);
                    }

                    return;
                }
                catch (ActionabilityException e)
                {
                    lastError = e;
                    if (DateTime.UtcNow >= deadline) throw;
                    await BackoffSleepAsync(attempt);
                    attempt++;
                }
            }
        }

        public static async Task EnsureStableAsync(
            IPage page,
            string selector,
            float timeout = 5000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeout);
            int attempt = 0;

            while (true)
            {
                var remainingMs = (float)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remainingMs <= 0) throw new ElementNotStableException(selector);

                var loc = page.Locator(selector).First;
                var box1 = await loc.BoundingBoxAsync(new LocatorBoundingBoxOptions { Timeout = Math.Max(1, Math.Min(remainingMs, 1000)) });
                if (box1 == null) throw new ElementNotAttachedException(selector);

                await Task.Delay(100);

                var box2 = await loc.BoundingBoxAsync(new LocatorBoundingBoxOptions { Timeout = Math.Max(1, Math.Min(remainingMs, 1000)) });
                if (box2 == null) throw new ElementNotAttachedException(selector);

                if (Math.Abs(box1.X - box2.X) <= 1 &&
                    Math.Abs(box1.Y - box2.Y) <= 1 &&
                    Math.Abs(box1.Width - box2.Width) <= 1 &&
                    Math.Abs(box1.Height - box2.Height) <= 1)
                {
                    return;
                }

                if (DateTime.UtcNow >= deadline) throw new ElementNotStableException(selector);

                await BackoffSleepAsync(attempt);
                attempt++;
            }
        }

        private const string PointerEventsLocatorJs = @"(expected, data) => {
            const rect = expected.getBoundingClientRect();
            // data is [x, y, boxX, boxY, boxWidth, boxHeight]
            const hasBox = data[4] > 0;
            const frameOffsetX = hasBox ? data[2] - rect.x : 0;
            const frameOffsetY = hasBox ? data[3] - rect.y : 0;
            const px = data[0] - frameOffsetX;
            const py = data[1] - frameOffsetY;
            const target = document.elementFromPoint(px, py);
            if (!target) return { hit: false, reason: 'no_element_at_point', covering: 'none' };
            let node = target;
            while (node) { if (node === expected) return { hit: true }; node = node.parentNode; }
            if (expected.contains(target)) return { hit: true };
            return { hit: false, reason: 'covered', covering: target.tagName || 'unknown' };
        }";

        public static async Task CheckPointerEventsAsync(
            IPage page,
            string selector,
            double x,
            double y,
            float timeout = 5000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeout);
            int attempt = 0;

            while (true)
            {
                JsonElement? result = null;
                try
                {
                    var loc = page.Locator(selector).First;
                    var box = await loc.BoundingBoxAsync(new LocatorBoundingBoxOptions { Timeout = Math.Max(1, (float)Math.Min((deadline - DateTime.UtcNow).TotalMilliseconds, 1000)) });
                    var argsArray = box != null 
                        ? new double[] { x, y, (double)box.X, (double)box.Y, (double)box.Width, (double)box.Height } 
                        : new double[] { x, y, 0, 0, 0, 0 };
                    result = await loc.EvaluateAsync<JsonElement>(PointerEventsLocatorJs, argsArray);
                }
                catch
                {
                    result = null;
                }

                if (result.HasValue)
                {
                    bool hit = false;
                    if (result.Value.TryGetProperty("hit", out var hitProp))
                    {
                        hit = hitProp.GetBoolean();
                    }
                    if (hit) return;
                }

                string covering = "unknown";
                if (result.HasValue && result.Value.TryGetProperty("covering", out var covProp))
                {
                    covering = covProp.GetString() ?? "unknown";
                }

                if (DateTime.UtcNow >= deadline) throw new ElementNotReceivingEventsException(selector, covering);

                await BackoffSleepAsync(attempt);
                attempt++;
            }
        }

        public static async Task EnsureActionableHandleAsync(
            IElementHandle el,
            HashSet<CheckName> checks,
            float timeout = 30000,
            bool force = false)
        {
            if (force) return;

            var deadline = DateTime.UtcNow.AddMilliseconds(timeout);
            int attempt = 0;
            ActionabilityException? lastError = null;
            const string label = "<ElementHandle>";

            while (true)
            {
                var remainingMs = (float)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remainingMs <= 0)
                {
                    if (lastError != null) throw lastError;
                    throw new ActionabilityException(label, "timeout", "timeout expired before first check");
                }

                try
                {
                    if (checks.Contains(CheckName.Visible))
                    {
                        try
                        {
                            await el.WaitForElementStateAsync(ElementState.Visible, new ElementHandleWaitForElementStateOptions { Timeout = Math.Max(1, Math.Min(remainingMs, 2000)) });
                        }
                        catch
                        {
                            throw new ElementNotVisibleException(label);
                        }
                    }

                    if (checks.Contains(CheckName.Enabled))
                    {
                        try
                        {
                            await el.WaitForElementStateAsync(ElementState.Enabled, new ElementHandleWaitForElementStateOptions { Timeout = Math.Max(1, Math.Min(remainingMs, 2000)) });
                        }
                        catch
                        {
                            throw new ElementNotEnabledException(label);
                        }
                    }

                    if (checks.Contains(CheckName.Editable))
                    {
                        try
                        {
                            await el.WaitForElementStateAsync(ElementState.Editable, new ElementHandleWaitForElementStateOptions { Timeout = Math.Max(1, Math.Min(remainingMs, 2000)) });
                        }
                        catch
                        {
                            throw new ElementNotEditableException(label);
                        }
                    }

                    return;
                }
                catch (ActionabilityException e)
                {
                    lastError = e;
                    if (DateTime.UtcNow >= deadline) throw;
                    await BackoffSleepAsync(attempt);
                    attempt++;
                }
            }
        }

        public static async Task CheckPointerEventsHandleAsync(
            IElementHandle el,
            double x,
            double y,
            float timeout = 5000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeout);
            int attempt = 0;

            while (true)
            {
                JsonElement? result = null;
                try
                {
                    var box = await el.BoundingBoxAsync();
                    result = await el.EvaluateAsync<JsonElement>(PointerEventsLocatorJs, new { x, y, box });
                }
                catch
                {
                    result = null;
                }

                if (result.HasValue)
                {
                    bool hit = false;
                    if (result.Value.TryGetProperty("hit", out var hitProp))
                    {
                        hit = hitProp.GetBoolean();
                    }
                    if (hit) return;
                }

                string covering = "unknown";
                if (result.HasValue && result.Value.TryGetProperty("covering", out var covProp))
                {
                    covering = covProp.GetString() ?? "unknown";
                }

                if (DateTime.UtcNow >= deadline) throw new ElementNotReceivingEventsException("<ElementHandle>", covering);

                await BackoffSleepAsync(attempt);
                attempt++;
            }
        }
    }
}
