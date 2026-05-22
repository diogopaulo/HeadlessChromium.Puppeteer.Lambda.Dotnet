using System;
using System.IO;
using System.Text;
using HeadlessChromium.Puppeteer.Lambda.Dotnet.Tar;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeadlessChromium.Puppeteer.Lambda.Dotnet.Tests.Tar
{
    public class TarReaderTests
    {
        // ─── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds an in-memory tar stream containing the given entries followed by the
        /// standard double-zero-block end-of-archive marker.
        /// </summary>
        private static Stream BuildTar(params (string filename, byte[] content, EntryType entryType)[] entries)
        {
            var ms = new MemoryStream();

            foreach (var (filename, content, entryType) in entries)
            {
                var header = new UsTarHeader
                {
                    FileName = filename,
                    SizeInBytes = entryType == EntryType.Directory ? 0 : content.Length,
                    Mode = 0755,
                    LastModification = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    EntryType = entryType,
                    UserName = "test",
                    GroupName = "test"
                };
                ms.Write(header.GetHeaderValue(), 0, 512);

                if (content.Length > 0)
                {
                    ms.Write(content, 0, content.Length);
                    int pad = 512 - (content.Length % 512);
                    if (pad < 512)
                        ms.Write(new byte[pad], 0, pad);
                }
            }

            // Standard tar end-of-archive: two consecutive 512-byte zero blocks
            ms.Write(new byte[1024], 0, 1024);
            ms.Seek(0, SeekOrigin.Begin);
            return ms;
        }

        // ─── MoveNext ─────────────────────────────────────────────────────────────

        [Fact]
        public void TarReader_MoveNext_ReturnsTrueForSingleFileEntry()
        {
            // Arrange
            var stream = BuildTar(("file.txt", Encoding.ASCII.GetBytes("hello"), EntryType.File));
            var reader = new TarReader(stream, NullLoggerFactory.Instance);

            // Act
            var result = reader.MoveNext(false);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void TarReader_MoveNext_ReturnsFalseAfterAllEntriesRead()
        {
            // Arrange – a 0-byte entry so remainingBytesInFile is 0 after the first MoveNext,
            // which avoids the seek-without-counter-reset path in MoveNext(skipData: true).
            var stream = BuildTar(("empty.txt", Array.Empty<byte>(), EntryType.File));
            var reader = new TarReader(stream, NullLoggerFactory.Instance);
            reader.MoveNext(false); // read the single entry's header; remainingBytesInFile = 0

            // Act
            var result = reader.MoveNext(false);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void TarReader_MoveNext_ReturnsFalseForEmptyArchive()
        {
            // Arrange – archive with only the EOF double-zero blocks
            var stream = BuildTar();
            var reader = new TarReader(stream, NullLoggerFactory.Instance);

            // Act
            var result = reader.MoveNext(false);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void TarReader_MoveNext_ThrowsTarException_WhenPreviousEntryDataNotRead()
        {
            // Arrange – entry has content; we do NOT read it before calling MoveNext again
            var stream = BuildTar(("data.txt", Encoding.ASCII.GetBytes("some content"), EntryType.File));
            var reader = new TarReader(stream, NullLoggerFactory.Instance);
            reader.MoveNext(false); // advance, but don't read content

            // Act / Assert
            Assert.Throws<TarException>(() => reader.MoveNext(skipData: false));
        }

        // ─── FileInfo ─────────────────────────────────────────────────────────────

        [Fact]
        public void TarReader_FileInfo_ReflectsHeaderFieldsAfterMoveNext()
        {
            // Arrange
            var content = Encoding.ASCII.GetBytes("hello");
            var stream = BuildTar(("data.txt", content, EntryType.File));
            var reader = new TarReader(stream, NullLoggerFactory.Instance);

            // Act
            reader.MoveNext(false);

            // Assert
            Assert.Equal("data.txt", reader.FileInfo.FileName);
            Assert.Equal(EntryType.File, reader.FileInfo.EntryType);
            Assert.Equal(5, reader.FileInfo.SizeInBytes);
        }

        // ─── ReadToEnd ────────────────────────────────────────────────────────────

        [Fact]
        public void TarReader_ReadToEnd_ExtractsFileWithCorrectContent()
        {
            // Arrange
            var expected = Encoding.ASCII.GetBytes("hello world");
            var stream = BuildTar(("output.txt", expected, EntryType.File));
            var reader = new TarReader(stream, NullLoggerFactory.Instance);
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // Act
                reader.ReadToEnd(tempDir);

                // Assert
                var extractedPath = Path.Combine(tempDir, "output.txt");
                Assert.True(File.Exists(extractedPath));
                Assert.Equal(expected, File.ReadAllBytes(extractedPath));
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void TarReader_ReadToEnd_CreatesDirectoryEntry()
        {
            // Arrange – directory entry has a trailing slash
            var stream = BuildTar(("subdir/", Array.Empty<byte>(), EntryType.Directory));
            var reader = new TarReader(stream, NullLoggerFactory.Instance);
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // Act
                reader.ReadToEnd(tempDir);

                // Assert
                Assert.True(Directory.Exists(Path.Combine(tempDir, "subdir")));
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void TarReader_ReadToEnd_CreatesParentDirectoryForNestedFile()
        {
            // Arrange
            var content = Encoding.ASCII.GetBytes("nested data");
            var stream = BuildTar(("parent/child.txt", content, EntryType.File));
            var reader = new TarReader(stream, NullLoggerFactory.Instance);
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // Act
                reader.ReadToEnd(tempDir);

                // Assert
                var extractedPath = Path.Combine(tempDir, "parent", "child.txt");
                Assert.True(File.Exists(extractedPath));
                Assert.Equal(content, File.ReadAllBytes(extractedPath));
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void TarReader_ReadToEnd_ExtractsMultipleFiles()
        {
            // Arrange
            var content1 = Encoding.ASCII.GetBytes("first");
            var content2 = Encoding.ASCII.GetBytes("second");
            var stream = BuildTar(
                ("first.txt", content1, EntryType.File),
                ("second.txt", content2, EntryType.File));
            var reader = new TarReader(stream, NullLoggerFactory.Instance);
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // Act
                reader.ReadToEnd(tempDir);

                // Assert
                Assert.Equal(content1, File.ReadAllBytes(Path.Combine(tempDir, "first.txt")));
                Assert.Equal(content2, File.ReadAllBytes(Path.Combine(tempDir, "second.txt")));
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
