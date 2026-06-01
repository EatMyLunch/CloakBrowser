using System;
using NUnit.Framework;
using Playwright.CloakBrowser.Community;

namespace Playwright.CloakBrowser.Community.Tests
{
    [TestFixture]
    public class GeoIpTests
    {
        [Test]
        public void TestProxyConfigParsing()
        {
            // For proxy with credentials, it should map to CLI arguments when inline auth is supported
            var confCreds = ProxyConfig.ResolveProxyConfig("http://user:pass@127.0.0.1:8080");
            if (ProxyConfig.SupportsHttpProxyInlineAuth())
            {
                Assert.That(confCreds.ProxyOption, Is.Null);
                Assert.That(confCreds.ProxyArgs, Does.Contain("--proxy-server=http://user:pass@127.0.0.1:8080"));
            }
            else
            {
                Assert.That(confCreds.ProxyOption, Is.Not.Null);
                Assert.That(confCreds.ProxyOption.Server, Is.EqualTo("http://127.0.0.1:8080"));
                Assert.That(confCreds.ProxyOption.Username, Is.EqualTo("user"));
                Assert.That(confCreds.ProxyOption.Password, Is.EqualTo("pass"));
            }

            // Simple proxy without credentials should fall back to standard Playwright proxy options
            var confSimple = ProxyConfig.ResolveProxyConfig("http://127.0.0.1:8080");
            Assert.That(confSimple.ProxyOption, Is.Not.Null);
            Assert.That(confSimple.ProxyOption.Server, Is.EqualTo("http://127.0.0.1:8080"));
        }

        [Test]
        public void TestProxyConfigNormalizationSocks()
        {
            // SOCKS URLs are normalized
            string raw = "socks5://user:pass@127.0.0.1:1080";
            string normalized = ProxyConfig.NormalizeSocksStringUrl(raw);
            Assert.That(normalized, Is.EqualTo("socks5://user:pass@127.0.0.1:1080"));
        }

        [Test]
        public void TestProxyConfigNormalizationHttp()
        {
            string raw = "user:pass@127.0.0.1:8080";
            string normalized = ProxyConfig.NormalizeHttpStringUrl(raw);
            Assert.That(normalized, Is.EqualTo("http://user:pass@127.0.0.1:8080"));
        }
    }
}

