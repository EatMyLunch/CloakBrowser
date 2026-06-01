using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Playwright.CloakBrowser.Community.Proxy.Interceptors
{
    public class ContextInterceptor
    {
        private readonly IBrowserContext _context;
        private readonly CloakLaunchOptions _options;
        private readonly Func<Task>? _onClose;

        public ContextInterceptor(IBrowserContext context, CloakLaunchOptions options, Func<Task>? onClose = null)
        {
            _context = context;
            _options = options;
            _onClose = onClose;
        }
        
        public CloakLaunchOptions Options => _options;

        public async Task<IPage> NewPageAsync()
        {
            var page = await _context.NewPageAsync();
            var pageInterceptor = new PageInterceptor(page, _options);
            return PlaywrightProxy<IPage>.Create(page, pageInterceptor);
        }

        public IReadOnlyList<IPage> Pages => _context.Pages.Select(p =>
        {
            var pageInterceptor = new PageInterceptor(p, _options);
            return PlaywrightProxy<IPage>.Create(p, pageInterceptor);
        }).ToList().AsReadOnly();

        public async Task CloseAsync()
        {
            await _context.CloseAsync();
            if (_onClose != null)
            {
                await _onClose();
            }
        }
    }
}

