using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeadlessChromium.Puppeteer.Lambda.Dotnet.Tests
{
    public class ChromiumExtractorTests : IDisposable
    {
        private readonly ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;
        private readonly string? _originalChromiumPath;
        private readonly string? _originalChromiumBinPath;

        public ChromiumExtractorTests()
        {
            _originalChromiumPath = Environment.GetEnvironmentVariable("CHROMIUM_PATH");
            _originalChromiumBinPath = Environment.GetEnvironmentVariable("CHROMIUM_BIN_PATH");
        }

        public void Dispose()
        {
            RestoreEnvVar("CHROMIUM_PATH", _originalChromiumPath);
            RestoreEnvVar("CHROMIUM_BIN_PATH", _originalChromiumBinPath);
        }

        private static void RestoreEnvVar(string name, string? value)
        {
            if (value == null)
                Environment.SetEnvironmentVariable(name, null);
            else
                Environment.SetEnvironmentVariable(name, value);
        }

        // ─── ResolveChromiumPath / ChromiumPath ────────────────────────────────

        [Fact]
        public void ChromiumPath_WhenEnvVarNotSet_ReturnsDefaultPath()
        {
            // Arrange
            Environment.SetEnvironmentVariable("CHROMIUM_PATH", null);

            // Act
            var extractor = new ChromiumExtractor(_loggerFactory);

            // Assert
            Assert.Equal(ChromiumExtractor.DefaultChromiumPath, extractor.ChromiumPath);
        }

        [Fact]
        public void ChromiumPath_WhenEnvVarIsEmpty_ReturnsDefaultPath()
        {
            // Arrange
            Environment.SetEnvironmentVariable("CHROMIUM_PATH", "");

            // Act
            var extractor = new ChromiumExtractor(_loggerFactory);

            // Assert
            Assert.Equal(ChromiumExtractor.DefaultChromiumPath, extractor.ChromiumPath);
        }

        [Fact]
        public void ChromiumPath_WhenEnvVarIsSet_ReturnsEnvVarValue()
        {
            // Arrange
            Environment.SetEnvironmentVariable("CHROMIUM_PATH", "/custom/chromium");

            // Act
            var extractor = new ChromiumExtractor(_loggerFactory);

            // Assert
            Assert.Equal("/custom/chromium", extractor.ChromiumPath);
        }

        [Fact]
        public void ChromiumPath_IsSnapshotAtConstruction_NotReflectedAfterMutation()
        {
            // Arrange — construct with one path
            Environment.SetEnvironmentVariable("CHROMIUM_PATH", "/original/chromium");
            var extractor = new ChromiumExtractor(_loggerFactory);

            // Act — mutate env var after construction
            Environment.SetEnvironmentVariable("CHROMIUM_PATH", "/mutated/chromium");

            // Assert — instance reflects the snapshot taken at construction
            Assert.Equal("/original/chromium", extractor.ChromiumPath);
        }

        [Fact]
        public void DefaultChromiumPath_IsCorrectConstant()
        {
            // Arrange / Act / Assert
            Assert.Equal("/tmp/chromium", ChromiumExtractor.DefaultChromiumPath);
        }

        // ─── ValidateBinary ────────────────────────────────────────────────────

        [Fact]
        public void ValidateBinary_WhenFileDoesNotExist_ThrowsWithNotFoundMessage()
        {
            // Arrange
            var extractor = new ChromiumExtractor(_loggerFactory);
            var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            // Act
            var ex = Assert.Throws<ChromiumExtractionException>(
                () => extractor.ValidateBinary(missingPath));

            // Assert
            Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(missingPath, ex.Message);
        }

        [Fact]
        public void ValidateBinary_WhenFileIsEmpty_ThrowsWithEmptyMessage()
        {
            // Arrange
            var extractor = new ChromiumExtractor(_loggerFactory);
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            File.WriteAllBytes(path, Array.Empty<byte>());
            try
            {
                // Act
                var ex = Assert.Throws<ChromiumExtractionException>(
                    () => extractor.ValidateBinary(path));

                // Assert
                Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(path, ex.Message);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        [Trait("Category", "Unix")]
        public void ValidateBinary_WhenFileIsNotExecutable_ThrowsWithExecutableMessage()
        {
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
                return; // Unix-only: Mono.Unix permission APIs are not meaningful on Windows

            // Arrange
            var extractor = new ChromiumExtractor(_loggerFactory);
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            File.WriteAllBytes(path, new byte[] { 0x7F, 0x45, 0x4C, 0x46 }); // ELF magic
            // Remove executable bit — leave only rw
            Mono.Unix.Native.Syscall.chmod(path, Mono.Unix.Native.FilePermissions.S_IRUSR | Mono.Unix.Native.FilePermissions.S_IWUSR);
            try
            {
                // Act
                var ex = Assert.Throws<ChromiumExtractionException>(
                    () => extractor.ValidateBinary(path));

                // Assert
                Assert.Contains("not executable", ex.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(path, ex.Message);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        [Trait("Category", "Unix")]
        public void ValidateBinary_WhenFileExistsAndIsExecutable_DoesNotThrow()
        {
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
                return;

            // Arrange
            var extractor = new ChromiumExtractor(_loggerFactory);
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            File.WriteAllBytes(path, new byte[] { 0x7F, 0x45, 0x4C, 0x46 }); // ELF magic
            var info = new Mono.Unix.UnixFileInfo(path);
            info.FileAccessPermissions = Mono.Unix.FileAccessPermissions.UserReadWriteExecute;
            try
            {
                // Act / Assert — no exception
                extractor.ValidateBinary(path);
            }
            finally
            {
                File.Delete(path);
            }
        }

        // ─── GetCandidateDirectories ───────────────────────────────────────────

        [Fact]
        public void GetCandidateDirectories_WithEnvOverrideSet_EnvDirIsFirst()
        {
            // Arrange
            var envDir = Path.Combine(Path.GetTempPath(), "chromium-env-" + Guid.NewGuid());
            Environment.SetEnvironmentVariable("CHROMIUM_BIN_PATH", envDir);
            var extractor = new ChromiumExtractor(_loggerFactory);

            // Act
            var candidates = extractor.GetCandidateDirectories(includeEnvOverride: true).ToList();

            // Assert
            Assert.Equal(envDir, candidates.First());
        }

        [Fact]
        public void GetCandidateDirectories_WithEnvOverrideDisabled_DoesNotIncludeEnvDir()
        {
            // Arrange
            Environment.SetEnvironmentVariable("CHROMIUM_BIN_PATH", "/should-not-appear");
            var extractor = new ChromiumExtractor(_loggerFactory);

            // Act
            var candidates = extractor.GetCandidateDirectories(includeEnvOverride: false).ToList();

            // Assert
            Assert.DoesNotContain("/should-not-appear", candidates);
        }

        [Fact]
        public void GetCandidateDirectories_WithEnvOverrideUnset_DoesNotIncludeEmptyEntry()
        {
            // Arrange
            Environment.SetEnvironmentVariable("CHROMIUM_BIN_PATH", null);
            var extractor = new ChromiumExtractor(_loggerFactory);

            // Act
            var candidates = extractor.GetCandidateDirectories(includeEnvOverride: true).ToList();

            // Assert
            Assert.DoesNotContain(string.Empty, candidates);
            Assert.DoesNotContain(null, candidates);
        }

        [Fact]
        public void GetCandidateDirectories_NeverContainsEmptyOrNullEntries()
        {
            // Arrange
            Environment.SetEnvironmentVariable("CHROMIUM_BIN_PATH", null);
            var extractor = new ChromiumExtractor(_loggerFactory);

            // Act
            var candidates = extractor.GetCandidateDirectories(includeEnvOverride: true).ToList();

            // Assert
            Assert.All(candidates, d => Assert.False(string.IsNullOrEmpty(d)));
        }

        // ─── FindSourceDirectory ───────────────────────────────────────────────

        [Fact]
        public void FindSourceDirectory_WhenEnvDirContainsChromiumBr_ReturnsThatDir()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), "chromium-src-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            File.WriteAllBytes(Path.Combine(tempDir, "chromium.br"), new byte[] { 1 });
            Environment.SetEnvironmentVariable("CHROMIUM_BIN_PATH", tempDir);
            var extractor = new ChromiumExtractor(_loggerFactory);

            try
            {
                // Act
                var result = extractor.FindSourceDirectory();

                // Assert
                Assert.Equal(tempDir, result);
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void FindSourceDirectory_WhenNoCandidateHasChromiumBr_ThrowsWithCandidateList()
        {
            // Arrange — set CHROMIUM_BIN_PATH to a dir that exists but has no chromium.br
            var tempDir = Path.Combine(Path.GetTempPath(), "chromium-empty-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            Environment.SetEnvironmentVariable("CHROMIUM_BIN_PATH", tempDir);
            var extractor = new ChromiumExtractor(_loggerFactory);

            try
            {
                // Act
                var ex = Assert.Throws<ChromiumExtractionException>(
                    () => extractor.FindSourceDirectory());

                // Assert — error message lists the searched paths
                Assert.Contains(tempDir, ex.Message);
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        // ─── AwsOperatingSystem ───────────────────────────────────────────────────

        [Fact]
        public void AwsOperatingSystem_WhenSetViaSetter_GetterReturnsCachedValue()
        {
            // Arrange
            var extractor = new ChromiumExtractor(_loggerFactory);

            // Act
            extractor.AwsOperatingSystem = "al2023";

            // Assert
            Assert.Equal("al2023", extractor.AwsOperatingSystem);
        }

        [Fact]
        public void AwsOperatingSystem_SetterOverridesAnyPreviousValue()
        {
            // Arrange
            var extractor = new ChromiumExtractor(_loggerFactory);
            extractor.AwsOperatingSystem = "al2";

            // Act
            extractor.AwsOperatingSystem = "al2023";

            // Assert
            Assert.Equal("al2023", extractor.AwsOperatingSystem);
        }

        [Fact]
        public void AwsOperatingSystem_WhenNotCached_AndSystemReleaseFileAbsent_ReturnsNull()
        {
            if (System.IO.File.Exists("/etc/system-release-cpe"))
                return; // Only verifiable when the AWS file is absent

            // Arrange – fresh instance has awsOperatingSystem = null
            var extractor = new ChromiumExtractor(_loggerFactory);

            // Act
            var result = extractor.AwsOperatingSystem;

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void FindSourceDirectory_DedupsCandidates()
        {
            // Arrange — CHROMIUM_BIN_PATH points to the same dir as AppDomain.BaseDirectory
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            // Ensure chromium.br does NOT exist there so we can detect "not found" with deduped list
            var chromiumBr = Path.Combine(baseDir, "chromium.br");
            var alreadyExists = File.Exists(chromiumBr);
            Environment.SetEnvironmentVariable("CHROMIUM_BIN_PATH", baseDir);
            var extractor = new ChromiumExtractor(_loggerFactory);

            try
            {
                if (alreadyExists)
                    return; // Can't test dedup-without-file if chromium.br already exists there

                var ex = Assert.Throws<ChromiumExtractionException>(
                    () => extractor.FindSourceDirectory());

                // Assert — baseDir appears only once in the error message (deduped)
                var count = ex.Message.Split(baseDir).Length - 1;
                Assert.Equal(1, count);
            }
            finally
            {
                Environment.SetEnvironmentVariable("CHROMIUM_BIN_PATH", _originalChromiumBinPath);
            }
        }
    }
}
