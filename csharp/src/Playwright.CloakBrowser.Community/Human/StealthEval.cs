using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Playwright.CloakBrowser.Community.Human
{
    public class StealthEval
    {
        private ICDPSession? _cdp;
        private int? _contextId;
        private readonly IPage _page;

        public StealthEval(IPage page)
        {
            _page = page;
            _page.FrameNavigated += (sender, frame) => { Invalidate(); };
        }

        private async Task<ICDPSession> EnsureCdpAsync()
        {
            if (_cdp == null)
            {
                _cdp = await _page.Context.NewCDPSessionAsync(_page);
            }
            return _cdp;
        }

        private async Task<int> CreateWorldAsync()
        {
            var cdp = await EnsureCdpAsync();
            var tree = await cdp.SendAsync("Page.getFrameTree");
            
            string frameId = "";
            if (tree.HasValue)
            {
                if (tree.Value.TryGetProperty("frameTree", out var frameTree) &&
                    frameTree.TryGetProperty("frame", out var frame) &&
                    frame.TryGetProperty("id", out var id))
                {
                    frameId = id.GetString() ?? "";
                }
            }

            var result = await cdp.SendAsync("Page.createIsolatedWorld", new Dictionary<string, object>
            {
                { "frameId", frameId },
                { "worldName", "" },
                { "grantUniveralAccess", true } // Chromium CDP uses 'grantUniveralAccess' typo
            });

            int ctxId = 0;
            if (result.HasValue && result.Value.TryGetProperty("executionContextId", out var ecId))
            {
                ctxId = ecId.GetInt32();
            }

            _contextId = ctxId;
            return ctxId;
        }

        public async Task<JsonElement?> EvaluateAsync(string expression)
        {
            if (_contextId == null)
            {
                await CreateWorldAsync();
            }

            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var cdp = await EnsureCdpAsync();
                    var result = await cdp.SendAsync("Runtime.evaluate", new Dictionary<string, object>
                    {
                        { "expression", expression },
                        { "contextId", _contextId!.Value },
                        { "returnByValue", true }
                    });

                    if (result.HasValue && result.Value.TryGetProperty("exceptionDetails", out _))
                    {
                        if (attempt == 0)
                        {
                            await CreateWorldAsync();
                            continue;
                        }
                        return null;
                    }

                    if (result.HasValue && result.Value.TryGetProperty("result", out var res) && res.TryGetProperty("value", out var val))
                    {
                        return val;
                    }
                    return null;
                }
                catch
                {
                    if (attempt == 0)
                    {
                        _contextId = null;
                        try
                        {
                            await CreateWorldAsync();
                        }
                        catch
                        {
                            return null;
                        }
                        continue;
                    }
                    return null;
                }
            }
            return null;
        }

        public void Invalidate()
        {
            _contextId = null;
        }

        public async Task<ICDPSession> GetCdpSessionAsync()
        {
            return await EnsureCdpAsync();
        }
    }
}

