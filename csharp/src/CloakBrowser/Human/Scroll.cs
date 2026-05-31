using System;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace CloakBrowser.Human
{
    public struct ElementBounds
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        public ElementBounds(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }

    public struct ScrollResult
    {
        public ElementBounds Box { get; set; }
        public float CursorX { get; set; }
        public float CursorY { get; set; }
        public bool DidScroll { get; set; }
    }

    public static class Scroll
    {
        private static bool IsInViewport(ElementBounds bounds, int viewportHeight, HumanConfig cfg)
        {
            double topEdge = bounds.Y;
            double bottomEdge = bounds.Y + bounds.Height;
            double zoneTop = viewportHeight * cfg.ScrollTargetZone.Min;
            double zoneBottom = viewportHeight * cfg.ScrollTargetZone.Max;
            return topEdge >= zoneTop && bottomEdge <= zoneBottom;
        }

        private static async Task SmoothWheelAsync(IRawMouse raw, double delta, HumanConfig cfg)
        {
            double absD = Math.Abs(delta);
            double sign = delta > 0 ? 1 : -1;
            double sent = 0;
            while (sent < absD)
            {
                double stepSize = ConfigResolver.Rand(20, 40);
                double chunk = Math.Min(stepSize, absD - sent);
                await raw.WheelAsync(0, (float)(Math.Round(chunk) * sign));
                sent += chunk;
                await Task.Delay(TimeSpan.FromMilliseconds(ConfigResolver.Rand(8, 20)));
            }
        }

        public static async Task<ScrollResult> HumanScrollIntoViewAsync(
            IPage page,
            IRawMouse raw,
            Func<Task<ElementBounds?>> getBox,
            float cursorX,
            float cursorY,
            HumanConfig cfg)
        {
            var viewport = page.ViewportSize;
            if (viewport == null) throw new InvalidOperationException("Viewport size not available");

            var box = await getBox();
            if (box == null) throw new InvalidOperationException("Element not found while scrolling into view");

            if (IsInViewport(box.Value, viewport.Height, cfg))
            {
                return new ScrollResult { Box = box.Value, CursorX = cursorX, CursorY = cursorY, DidScroll = false };
            }

            // Move cursor into scroll area
            float scrollAreaX = (float)Math.Round(viewport.Width * ConfigResolver.Rand(0.3, 0.7));
            float scrollAreaY = (float)Math.Round(viewport.Height * ConfigResolver.Rand(0.3, 0.7));
            await Mouse.HumanMoveAsync(raw, cursorX, cursorY, scrollAreaX, scrollAreaY, cfg);
            cursorX = scrollAreaX;
            cursorY = scrollAreaY;
            await ConfigResolver.DelayAsync(cfg.ScrollPreMoveDelay);

            // Recalculate box position post-cursor move (in case layout shifted)
            box = await getBox();
            if (box == null) throw new InvalidOperationException("Element lost during scroll initialization");

            // Calculate scroll distance
            double targetY = viewport.Height * ConfigResolver.Rand(cfg.ScrollTargetZone.Min, cfg.ScrollTargetZone.Max);
            double elementCenter = box.Value.Y + box.Value.Height / 2;
            double distanceToScroll = elementCenter - targetY;

            double direction = distanceToScroll > 0 ? 1 : -1;
            double absDistance = Math.Abs(distanceToScroll);
            double avgDelta = (cfg.ScrollDeltaBase.Min + cfg.ScrollDeltaBase.Max) / 2.0;
            int totalClicks = (int)Math.Max(3, Math.Ceiling(absDistance / avgDelta));
            int accelSteps = ConfigResolver.RandIntRange(cfg.ScrollAccelSteps);
            int decelSteps = ConfigResolver.RandIntRange(cfg.ScrollDecelSteps);

            double scrolled = 0;

            for (int i = 0; i < totalClicks; i++)
            {
                double delta;
                IntRange pause;

                if (i < accelSteps)
                {
                    delta = ConfigResolver.Rand(80, 100);
                    pause = cfg.ScrollPauseSlow;
                }
                else if (i >= totalClicks - decelSteps)
                {
                    delta = ConfigResolver.Rand(60, 90);
                    pause = cfg.ScrollPauseSlow;
                }
                else
                {
                    delta = ConfigResolver.RandRange(cfg.ScrollDeltaBase);
                    pause = cfg.ScrollPauseFast;
                }

                delta *= 1 + (ConfigResolver.Rand(0, 1) - 0.5) * 2 * cfg.ScrollDeltaVariance;
                delta = Math.Round(delta) * direction;

                await SmoothWheelAsync(raw, delta, cfg);
                scrolled += Math.Abs(delta);
                await ConfigResolver.DelayAsync(pause);

                // Check visibility every 3 steps
                if (i % 3 == 2 || i == totalClicks - 1)
                {
                    box = await getBox();
                    if (box != null && IsInViewport(box.Value, viewport.Height, cfg))
                    {
                        break;
                    }
                }

                if (scrolled >= absDistance * 1.1) break;
            }

            // Optional overshoot + correction
            if (ConfigResolver.Rand(0, 1) < cfg.ScrollOvershootChance)
            {
                double overshootPx = Math.Round(ConfigResolver.RandRange(cfg.ScrollOvershootPx)) * direction;
                await SmoothWheelAsync(raw, overshootPx, cfg);
                await ConfigResolver.DelayAsync(cfg.ScrollSettleDelay);

                int corrections = ConfigResolver.RandIntRange(new IntRange(1, 2));
                for (int c = 0; c < corrections; c++)
                {
                    double corrDelta = Math.Round(ConfigResolver.Rand(40, 80)) * -direction;
                    await SmoothWheelAsync(raw, corrDelta, cfg);
                    await Task.Delay(ConfigResolver.RandInt(100, 250));
                }
            }

            // Settle
            await ConfigResolver.DelayAsync(cfg.ScrollSettleDelay);

            box = await getBox();
            if (box == null) throw new InvalidOperationException("Element lost after scrolling into view");

            return new ScrollResult { Box = box.Value, CursorX = cursorX, CursorY = cursorY, DidScroll = true };
        }

        public static async Task<ScrollResult> ScrollToElementAsync(
            IPage page,
            IRawMouse raw,
            string selector,
            float cursorX,
            float cursorY,
            HumanConfig cfg,
            float timeout = 30000)
        {
            return await HumanScrollIntoViewAsync(
                page, raw,
                async () => await GetElementBoxAsync(page, selector, timeout),
                cursorX, cursorY, cfg
            );
        }

        private static async Task<ElementBounds?> GetElementBoxAsync(IPage page, string selector, float timeout = 30000)
        {
            var el = page.Locator(selector).First;
            try
            {
                var box = await el.BoundingBoxAsync(new LocatorBoundingBoxOptions { Timeout = Math.Max(1, timeout) });
                if (box == null) return null;
                return new ElementBounds(box.X, box.Y, box.Width, box.Height);
            }
            catch
            {
                return null;
            }
        }
    }
}
