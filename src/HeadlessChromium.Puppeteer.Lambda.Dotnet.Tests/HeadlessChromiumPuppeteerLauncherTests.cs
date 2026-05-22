using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeadlessChromium.Puppeteer.Lambda.Dotnet.Tests
{
    public class HeadlessChromiumPuppeteerLauncherTests
    {
        // ─── DefaultChromeArgs ────────────────────────────────────────────────────

        [Fact]
        public void DefaultChromeArgs_IsNotEmpty()
        {
            // Arrange / Act / Assert
            Assert.NotEmpty(HeadlessChromiumPuppeteerLauncher.DefaultChromeArgs);
        }

        [Fact]
        public void DefaultChromeArgs_ContainsNoSandboxFlag()
        {
            // Arrange / Act / Assert
            Assert.Contains("--no-sandbox", HeadlessChromiumPuppeteerLauncher.DefaultChromeArgs);
        }

        [Fact]
        public void DefaultChromeArgs_ContainsSingleProcessFlag()
        {
            // Arrange / Act / Assert
            Assert.Contains("--single-process", HeadlessChromiumPuppeteerLauncher.DefaultChromeArgs);
        }

        [Fact]
        public void DefaultChromeArgs_ContainsNoFirstRunFlag()
        {
            // Arrange / Act / Assert
            Assert.Contains("--no-first-run", HeadlessChromiumPuppeteerLauncher.DefaultChromeArgs);
        }

        // ─── Constructor ─────────────────────────────────────────────────────────

        [Fact]
        public void Constructor_WithLoggerFactory_DoesNotThrow()
        {
            // Arrange / Act / Assert – no exception
            var _ = new HeadlessChromiumPuppeteerLauncher(NullLoggerFactory.Instance);
        }
    }
}
