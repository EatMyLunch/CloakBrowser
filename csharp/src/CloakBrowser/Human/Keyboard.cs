using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace CloakBrowser.Human
{
    public interface IRawKeyboard
    {
        Task DownAsync(string key);
        Task UpAsync(string key);
        Task TypeAsync(string text, KeyboardTypeOptions? options = null);
        Task InsertTextAsync(string text);
    }

    public static class Keyboard
    {
        private static readonly HashSet<char> ShiftSymbols = new HashSet<char>
        {
            '@', '#', '!', '$', '%', '^', '&', '*', '(', ')',
            '_', '+', '{', '}', '|', ':', '"', '<', '>', '?', '~'
        };

        private static readonly Dictionary<char, string> NearbyKeys = new Dictionary<char, string>
        {
            { 'a', "sqwz" }, { 'b', "vghn" }, { 'c', "xdfv" }, { 'd', "sfecx" }, { 'e', "wrsdf" },
            { 'f', "dgrtcv" }, { 'g', "fhtyb" }, { 'h', "gjybn" }, { 'i', "ujko" }, { 'j', "hkunm" },
            { 'k', "jloi" }, { 'l', "kop" }, { 'm', "njk" }, { 'n', "bhjm" }, { 'o', "iklp" },
            { 'p', "ol" }, { 'q', "wa" }, { 'r', "edft" }, { 's', "awedxz" }, { 't', "rfgy" },
            { 'u', "yhji" }, { 'v', "cfgb" }, { 'w', "qase" }, { 'x', "zsdc" }, { 'y', "tghu" },
            { 'z', "asx" },
            { '1', "2q" }, { '2', "13qw" }, { '3', "24we" }, { '4', "35er" }, { '5', "46rt" },
            { '6', "57ty" }, { '7', "68yu" }, { '8', "79ui" }, { '9', "80io" }, { '0', "9p" }
        };

        private static readonly Dictionary<char, string> ShiftSymbolCodes = new Dictionary<char, string>
        {
            { '!', "Digit1" }, { '@', "Digit2" }, { '#', "Digit3" }, { '$', "Digit4" },
            { '%', "Digit5" }, { '^', "Digit6" }, { '&', "Digit7" }, { '*', "Digit8" },
            { '(', "Digit9" }, { ')', "Digit0" }, { '_', "Minus" }, { '+', "Equal" },
            { '{', "BracketLeft" }, { '}', "BracketRight" }, { '|', "Backslash" },
            { ':', "Semicolon" }, { '"', "Quote" }, { '<', "Comma" }, { '>', "Period" },
            { '?', "Slash" }, { '~', "Backquote" }
        };

        private static readonly Dictionary<char, int> ShiftSymbolKeyCodes = new Dictionary<char, int>
        {
            { '!', 49 }, { '@', 50 }, { '#', 51 }, { '$', 52 }, { '%', 53 },
            { '^', 54 }, { '&', 55 }, { '*', 56 }, { '(', 57 }, { ')', 48 },
            { '_', 189 }, { '+', 187 }, { '{', 219 }, { '}', 221 }, { '|', 220 },
            { ':', 186 }, { '"', 222 }, { '<', 188 }, { '>', 190 }, { '?', 191 },
            { '~', 192 }
        };

        private static readonly Regex AlphanumericRegex = new Regex(@"^[a-zA-Z0-9]$", RegexOptions.Compiled);

        private static bool IsAscii(char ch)
        {
            return ch < 128;
        }

        private static string GetNearbyKey(char ch)
        {
            char lower = char.ToLowerInvariant(ch);
            if (NearbyKeys.TryGetValue(lower, out var neighbors))
            {
                var random = new Random();
                char wrong = neighbors[random.Next(neighbors.Length)];
                if (char.IsUpper(ch) && char.IsLower(ch)) // wait, in JS: ch === ch.toUpperCase() && ch !== ch.toLowerCase()
                {
                    return wrong.ToString().ToUpperInvariant();
                }
                return wrong.ToString();
            }
            return ch.ToString();
        }

        private static bool IsUpperCase(char ch)
        {
            return ch >= 'A' && ch <= 'Z';
        }

        public static async Task HumanTypeAsync(
            IPage page,
            IRawKeyboard raw,
            string text,
            HumanConfig cfg,
            ICDPSession? cdpSession)
        {
            // Use StringInfo to handle surrogate pairs (emoji) correctly in C#
            var chars = new List<string>();
            var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(text);
            while (enumerator.MoveNext())
            {
                chars.Add(enumerator.GetTextElement());
            }

            for (int i = 0; i < chars.Count; i++)
            {
                string chStr = chars[i];
                if (chStr.Length > 1)
                {
                    // Non-ASCII (surrogate pair/emoji) — use insertText
                    await ConfigResolver.DelayAsync(cfg.KeyHold);
                    await raw.InsertTextAsync(chStr);
                    if (i < chars.Count - 1)
                    {
                        await InterCharDelay(cfg);
                    }
                    continue;
                }

                char ch = chStr[0];

                if (!IsAscii(ch))
                {
                    await ConfigResolver.DelayAsync(cfg.KeyHold);
                    await raw.InsertTextAsync(chStr);
                    if (i < chars.Count - 1)
                    {
                        await InterCharDelay(cfg);
                    }
                    continue;
                }

                // Mistype chance
                if (ConfigResolver.Rand(0, 1) < cfg.MistypeChance && AlphanumericRegex.IsMatch(chStr))
                {
                    string wrong = GetNearbyKey(ch);
                    await TypeNormalChar(raw, wrong, cfg);
                    await ConfigResolver.DelayAsync(cfg.MistypeDelayNotice);
                    await raw.DownAsync("Backspace");
                    await ConfigResolver.DelayAsync(cfg.KeyHold);
                    await raw.UpAsync("Backspace");
                    await ConfigResolver.DelayAsync(cfg.MistypeDelayCorrect);
                }

                if (IsUpperCase(ch))
                {
                    await TypeShiftedChar(raw, chStr, cfg);
                }
                else if (ShiftSymbols.Contains(ch))
                {
                    await TypeShiftSymbol(page, raw, ch, cfg, cdpSession);
                }
                else
                {
                    await TypeNormalChar(raw, chStr, cfg);
                }

                if (i < chars.Count - 1)
                {
                    await InterCharDelay(cfg);
                }
            }
        }

        private static async Task TypeNormalChar(IRawKeyboard raw, string ch, HumanConfig cfg)
        {
            await raw.DownAsync(ch);
            await ConfigResolver.DelayAsync(cfg.KeyHold);
            await raw.UpAsync(ch);
        }

        private static async Task TypeShiftedChar(IRawKeyboard raw, string ch, HumanConfig cfg)
        {
            await raw.DownAsync("Shift");
            await ConfigResolver.DelayAsync(cfg.ShiftDownDelay);
            await raw.DownAsync(ch);
            await ConfigResolver.DelayAsync(cfg.KeyHold);
            await raw.UpAsync(ch);
            await ConfigResolver.DelayAsync(cfg.ShiftUpDelay);
            await raw.UpAsync("Shift");
        }

        private static async Task TypeShiftSymbol(
            IPage page,
            IRawKeyboard raw,
            char ch,
            HumanConfig cfg,
            ICDPSession? cdpSession)
        {
            if (cdpSession != null)
            {
                ShiftSymbolCodes.TryGetValue(ch, out string? code);
                ShiftSymbolKeyCodes.TryGetValue(ch, out int keyCode);
                string chStr = ch.ToString();

                await raw.DownAsync("Shift");
                await ConfigResolver.DelayAsync(cfg.ShiftDownDelay);

                await cdpSession.SendAsync("Input.dispatchKeyEvent", new Dictionary<string, object>
                {
                    { "type", "keyDown" },
                    { "modifiers", 8 }, // Shift modifier flag
                    { "key", chStr },
                    { "code", code ?? "" },
                    { "windowsVirtualKeyCode", keyCode },
                    { "text", chStr },
                    { "unmodifiedText", chStr }
                });

                await ConfigResolver.DelayAsync(cfg.KeyHold);

                await cdpSession.SendAsync("Input.dispatchKeyEvent", new Dictionary<string, object>
                {
                    { "type", "keyUp" },
                    { "modifiers", 8 },
                    { "key", chStr },
                    { "code", code ?? "" },
                    { "windowsVirtualKeyCode", keyCode }
                });

                await ConfigResolver.DelayAsync(cfg.ShiftUpDelay);
                await raw.UpAsync("Shift");
            }
            else
            {
                await raw.DownAsync("Shift");
                await ConfigResolver.DelayAsync(cfg.ShiftDownDelay);
                await raw.InsertTextAsync(ch.ToString());

                await page.EvaluateAsync(@"key => {
                    const el = document.activeElement;
                    if (el) {
                        el.dispatchEvent(new KeyboardEvent('keydown', { key: key, bubbles: true }));
                        el.dispatchEvent(new KeyboardEvent('keyup', { key: key, bubbles: true }));
                    }
                }", ch.ToString());

                await ConfigResolver.DelayAsync(cfg.ShiftUpDelay);
                await raw.UpAsync("Shift");
            }
        }

        private static async Task InterCharDelay(HumanConfig cfg)
        {
            if (ConfigResolver.Rand(0, 1) < cfg.TypingPauseChance)
            {
                await ConfigResolver.DelayAsync(cfg.TypingPauseRange);
            }
            else
            {
                double delay = cfg.TypingDelay + (ConfigResolver.Rand(0, 1) - 0.5) * 2 * cfg.TypingDelaySpread;
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(10, delay)));
            }
        }
    }
}
