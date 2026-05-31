using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CloakBrowser.Human
{
    public enum HumanPreset
    {
        Default,
        Careful
    }

    public struct DoubleRange
    {
        public double Min { get; set; }
        public double Max { get; set; }

        public DoubleRange(double min, double max)
        {
            Min = min;
            Max = max;
        }
    }

    public struct IntRange
    {
        public int Min { get; set; }
        public int Max { get; set; }

        public IntRange(int min, int max)
        {
            Min = min;
            Max = max;
        }
    }

    public class HumanConfig
    {
        // Keyboard
        public double TypingDelay { get; set; } = 70;
        public double TypingDelaySpread { get; set; } = 40;
        public double TypingPauseChance { get; set; } = 0.1;
        public IntRange TypingPauseRange { get; set; } = new IntRange(400, 1000);
        public IntRange ShiftDownDelay { get; set; } = new IntRange(30, 70);
        public IntRange ShiftUpDelay { get; set; } = new IntRange(20, 50);
        public IntRange KeyHold { get; set; } = new IntRange(15, 35);
        public IntRange FieldSwitchDelay { get; set; } = new IntRange(800, 1500);
        public double MistypeChance { get; set; } = 0.02;
        public IntRange MistypeDelayNotice { get; set; } = new IntRange(100, 300);
        public IntRange MistypeDelayCorrect { get; set; } = new IntRange(50, 150);

        // Mouse — movement
        public double MouseStepsDivisor { get; set; } = 8;
        public int MouseMinSteps { get; set; } = 25;
        public int MouseMaxSteps { get; set; } = 80;
        public double MouseWobbleMax { get; set; } = 1.5;
        public double MouseOvershootChance { get; set; } = 0.15;
        public IntRange MouseOvershootPx { get; set; } = new IntRange(3, 6);
        public IntRange MouseBurstSize { get; set; } = new IntRange(3, 5);
        public IntRange MouseBurstPause { get; set; } = new IntRange(8, 18);

        // Mouse — clicks
        public IntRange ClickAimDelayInput { get; set; } = new IntRange(60, 140);
        public IntRange ClickAimDelayButton { get; set; } = new IntRange(80, 200);
        public IntRange ClickHoldInput { get; set; } = new IntRange(40, 100);
        public IntRange ClickHoldButton { get; set; } = new IntRange(60, 150);
        public DoubleRange ClickInputXRange { get; set; } = new DoubleRange(0.05, 0.30);

        // Mouse — idle
        public double IdleDriftPx { get; set; } = 3;
        public IntRange IdlePauseRange { get; set; } = new IntRange(300, 1000);

        // Scroll
        public IntRange ScrollDeltaBase { get; set; } = new IntRange(80, 130);
        public double ScrollDeltaVariance { get; set; } = 0.2;
        public IntRange ScrollPauseFast { get; set; } = new IntRange(30, 80);
        public IntRange ScrollPauseSlow { get; set; } = new IntRange(80, 200);
        public IntRange ScrollAccelSteps { get; set; } = new IntRange(2, 3);
        public IntRange ScrollDecelSteps { get; set; } = new IntRange(2, 3);
        public double ScrollOvershootChance { get; set; } = 0.1;
        public IntRange ScrollOvershootPx { get; set; } = new IntRange(50, 150);
        public IntRange ScrollSettleDelay { get; set; } = new IntRange(300, 600);
        public DoubleRange ScrollTargetZone { get; set; } = new DoubleRange(0.20, 0.80);
        public IntRange ScrollPreMoveDelay { get; set; } = new IntRange(100, 300);

        // Initial cursor position
        public IntRange InitialCursorX { get; set; } = new IntRange(400, 700);
        public IntRange InitialCursorY { get; set; } = new IntRange(45, 60);

        // Idle between actions
        public bool IdleBetweenActions { get; set; } = false;
        public DoubleRange IdleBetweenDuration { get; set; } = new DoubleRange(0.3, 0.8);

        public HumanConfig Clone()
        {
            return (HumanConfig)this.MemberwiseClone();
        }
    }

    public static class ConfigResolver
    {
        private static readonly Random _random = new Random();

        public static HumanConfig ResolveConfig(HumanPreset preset = HumanPreset.Default, HumanConfig? overrides = null)
        {
            var baseConfig = GetPresetConfig(preset);
            if (overrides == null) return baseConfig;

            // Apply overrides via reflection or simple property copy to keep original clean
            var config = baseConfig.Clone();
            var properties = typeof(HumanConfig).GetProperties();
            foreach (var prop in properties)
            {
                if (prop.CanWrite)
                {
                    // Check if overrides has set property differently from its default (or simply write all non-default values).
                    // For safety, we can just copy properties that are explicitly set.
                    // But since overrides is just passed by the user, they can copy the entire config or we can write property copier.
                    var userVal = prop.GetValue(overrides);
                    var defaultVal = prop.GetValue(new HumanConfig()); // Default new config

                    if (userVal != null && !userVal.Equals(defaultVal))
                    {
                        prop.SetValue(config, userVal);
                    }
                }
            }
            return config;
        }

        public static HumanConfig MergeConfig(HumanConfig cfg, HumanConfig? overrides)
        {
            if (overrides == null) return cfg;
            var config = cfg.Clone();
            var properties = typeof(HumanConfig).GetProperties();
            foreach (var prop in properties)
            {
                if (prop.CanWrite)
                {
                    var userVal = prop.GetValue(overrides);
                    var defaultVal = prop.GetValue(new HumanConfig());
                    if (userVal != null && !userVal.Equals(defaultVal))
                    {
                        prop.SetValue(config, userVal);
                    }
                }
            }
            return config;
        }

        private static HumanConfig GetPresetConfig(HumanPreset preset)
        {
            var config = new HumanConfig();
            if (preset == HumanPreset.Careful)
            {
                // Keyboard
                config.TypingDelay = 100;
                config.TypingDelaySpread = 50;
                config.TypingPauseChance = 0.15;
                config.TypingPauseRange = new IntRange(500, 1200);
                config.ShiftDownDelay = new IntRange(40, 90);
                config.ShiftUpDelay = new IntRange(30, 70);
                config.KeyHold = new IntRange(20, 45);
                config.FieldSwitchDelay = new IntRange(1000, 2000);
                config.MistypeChance = 0.03;
                config.MistypeDelayNotice = new IntRange(150, 400);
                config.MistypeDelayCorrect = new IntRange(80, 200);

                // Mouse
                config.MouseOvershootChance = 0.10;
                config.MouseBurstPause = new IntRange(12, 25);

                // Mouse clicks
                config.ClickAimDelayInput = new IntRange(80, 180);
                config.ClickAimDelayButton = new IntRange(120, 280);
                config.ClickHoldInput = new IntRange(60, 140);
                config.ClickHoldButton = new IntRange(80, 200);

                // Scroll
                config.ScrollPauseFast = new IntRange(100, 200);
                config.ScrollPauseSlow = new IntRange(250, 600);
                config.ScrollSettleDelay = new IntRange(400, 800);
                config.ScrollPreMoveDelay = new IntRange(150, 400);

                // Idle between actions
                config.IdleBetweenActions = true;
                config.IdleBetweenDuration = new DoubleRange(0.4, 1.0);
            }
            return config;
        }

        // Random helpers
        public static double Rand(double min, double max)
        {
            lock (_random)
            {
                return min + _random.NextDouble() * (max - min);
            }
        }

        public static int RandInt(int min, int max)
        {
            lock (_random)
            {
                return _random.Next(min, max + 1);
            }
        }

        public static double RandRange(DoubleRange range)
        {
            return Rand(range.Min, range.Max);
        }

        public static int RandIntRange(IntRange range)
        {
            return RandInt(range.Min, range.Max);
        }

        public static double RandRange(IntRange range)
        {
            return Rand(range.Min, range.Max);
        }

        public static Task DelayAsync(double ms)
        {
            return Task.Delay(TimeSpan.FromMilliseconds(ms));
        }

        public static Task DelayAsync(IntRange range)
        {
            return Task.Delay(TimeSpan.FromMilliseconds(RandIntRange(range)));
        }
    }
}
