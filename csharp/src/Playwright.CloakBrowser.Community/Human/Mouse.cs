using System;
using System.Threading.Tasks;

namespace Playwright.CloakBrowser.Community.Human
{
    public interface IRawMouse
    {
        Task MoveAsync(float x, float y);
        Task DownAsync();
        Task UpAsync();
        Task WheelAsync(float deltaX, float deltaY);
    }

    public struct Point
    {
        public float X { get; set; }
        public float Y { get; set; }

        public Point(float x, float y)
        {
            X = x;
            Y = y;
        }
    }

    public static class Mouse
    {
        private static double EaseInOut(double t)
        {
            return t < 0.5
                ? 4 * t * t * t
                : 1 - Math.Pow(-2 * t + 2, 3) / 2;
        }

        private static Point Bezier(Point p0, Point p1, Point p2, Point p3, double t)
        {
            double u = 1 - t;
            double uu = u * u;
            double uuu = uu * u;
            double tt = t * t;
            double ttt = tt * t;

            float x = (float)(uuu * p0.X + 3 * uu * t * p1.X + 3 * u * tt * p2.X + ttt * p3.X);
            float y = (float)(uuu * p0.Y + 3 * uu * t * p1.Y + 3 * u * tt * p2.Y + ttt * p3.Y);

            return new Point(x, y);
        }

        private static Point[] RandomControlPoints(Point start, Point end)
        {
            float dx = end.X - start.X;
            float dy = end.Y - start.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            float px = (float)(-dy / (dist == 0 ? 1 : dist));
            float py = (float)(dx / (dist == 0 ? 1 : dist));

            float bias1 = (float)(ConfigResolver.Rand(-0.3, 0.3) * dist);
            float bias2 = (float)(ConfigResolver.Rand(-0.3, 0.3) * dist);

            return new[]
            {
                new Point(start.X + dx * 0.25f + px * bias1, start.Y + dy * 0.25f + py * bias1),
                new Point(start.X + dx * 0.75f + px * bias2, start.Y + dy * 0.75f + py * bias2)
            };
        }

        public static async Task HumanMoveAsync(
            IRawMouse raw,
            float startX,
            float startY,
            float endX,
            float endY,
            HumanConfig cfg)
        {
            float dx = endX - startX;
            float dy = endY - startY;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 1) return;

            int steps = (int)Math.Max(
                cfg.MouseMinSteps,
                Math.Min(cfg.MouseMaxSteps, Math.Round(dist / cfg.MouseStepsDivisor))
            );

            var start = new Point(startX, startY);
            var end = new Point(endX, endY);
            var cps = RandomControlPoints(start, end);
            var cp1 = cps[0];
            var cp2 = cps[1];

            int burstCounter = 0;
            int burstSize = ConfigResolver.RandIntRange(cfg.MouseBurstSize);

            for (int i = 0; i <= steps; i++)
            {
                double progress = (double)i / steps;
                double easedT = EaseInOut(progress);
                var pt = Bezier(start, cp1, cp2, end, easedT);

                double wobbleAmp = Math.Sin(Math.PI * progress) * cfg.MouseWobbleMax;
                float wx = (float)(pt.X + (ConfigResolver.Rand(-0.5, 0.5) * 2 * wobbleAmp));
                float wy = (float)(pt.Y + (ConfigResolver.Rand(-0.5, 0.5) * 2 * wobbleAmp));

                await raw.MoveAsync((float)Math.Round(wx), (float)Math.Round(wy));

                burstCounter++;
                if (burstCounter >= burstSize && i < steps)
                {
                    await ConfigResolver.DelayAsync(cfg.MouseBurstPause);
                    burstCounter = 0;
                }
            }

            if (ConfigResolver.Rand(0, 1) < cfg.MouseOvershootChance)
            {
                double overshootDist = ConfigResolver.RandRange(cfg.MouseOvershootPx);
                double angle = Math.Atan2(endY - startY, endX - startX);
                float ovX = (float)Math.Round(endX + Math.Cos(angle) * overshootDist);
                float ovY = (float)Math.Round(endY + Math.Sin(angle) * overshootDist);
                await raw.MoveAsync(ovX, ovY);
                await Task.Delay(ConfigResolver.RandInt(30, 70));
                float corrX = (float)Math.Round(endX + (ConfigResolver.Rand(-0.5, 0.5) * 4));
                float corrY = (float)Math.Round(endY + (ConfigResolver.Rand(-0.5, 0.5) * 4));
                await raw.MoveAsync(corrX, corrY);
            }
        }

        public static Point ClickTarget(
            Microsoft.Playwright.Clip box,
            bool isInput,
            HumanConfig cfg)
        {
            if (isInput)
            {
                double xFrac = ConfigResolver.RandRange(cfg.ClickInputXRange);
                double yFrac = ConfigResolver.Rand(0.30, 0.70);
                return new Point(
                    (float)Math.Round(box.X + box.Width * xFrac),
                    (float)Math.Round(box.Y + box.Height * yFrac)
                );
            }
            else
            {
                double xFrac = ConfigResolver.Rand(0.35, 0.65);
                double yFrac = ConfigResolver.Rand(0.35, 0.65);
                return new Point(
                    (float)Math.Round(box.X + box.Width * xFrac),
                    (float)Math.Round(box.Y + box.Height * yFrac)
                );
            }
        }

        public static async Task HumanClickAsync(
            IRawMouse raw,
            bool isInput,
            HumanConfig cfg)
        {
            double aimDelay = isInput
                ? ConfigResolver.RandRange(cfg.ClickAimDelayInput)
                : ConfigResolver.RandRange(cfg.ClickAimDelayButton);
            await ConfigResolver.DelayAsync(aimDelay);

            double holdTime = isInput
                ? ConfigResolver.RandRange(cfg.ClickHoldInput)
                : ConfigResolver.RandRange(cfg.ClickHoldButton);
            await raw.DownAsync();
            await ConfigResolver.DelayAsync(holdTime);
            await raw.UpAsync();
        }

        public static async Task HumanIdleAsync(
            IRawMouse raw,
            float cx,
            float cy,
            HumanConfig cfg)
        {
            double seconds = ConfigResolver.Rand(cfg.IdleBetweenDuration.Min, cfg.IdleBetweenDuration.Max);
            await HumanIdleAsync(raw, seconds, cx, cy, cfg);
        }

        public static async Task HumanIdleAsync(
            IRawMouse raw,
            double seconds,
            float cx,
            float cy,
            HumanConfig cfg)
        {
            var endTime = DateTime.UtcNow.AddSeconds(seconds);
            float x = cx;
            float y = cy;
            while (DateTime.UtcNow < endTime)
            {
                float dx = (float)((ConfigResolver.Rand(0, 1) - 0.5) * 2 * cfg.IdleDriftPx);
                float dy = (float)((ConfigResolver.Rand(0, 1) - 0.5) * 2 * cfg.IdleDriftPx);
                x += dx;
                y += dy;
                await raw.MoveAsync((float)Math.Round(x), (float)Math.Round(y));
                await ConfigResolver.DelayAsync(cfg.IdlePauseRange);
            }
        }
    }
}

