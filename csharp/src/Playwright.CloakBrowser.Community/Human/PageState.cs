using System;

namespace Playwright.CloakBrowser.Community.Human
{
    public class PageState
    {
        public double X { get; set; } = 0;
        public double Y { get; set; } = 0;
        public bool Initialized { get; set; } = false;
        public StealthEval? StealthEval { get; set; }
        public HumanConfig Config { get; }

        public PageState(HumanConfig config)
        {
            Config = config;
        }
    }
}

