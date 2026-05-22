using System;
using System.Text;
using HeadlessChromium.Puppeteer.Lambda.Dotnet.Tar;
using Xunit;

namespace HeadlessChromium.Puppeteer.Lambda.Dotnet.Tests.Tar
{
    public class TarHeaderTests
    {
        // ─── TarException ─────────────────────────────────────────────────────────

        [Fact]
        public void TarException_MessageConstructor_PreservesMessage()
        {
            // Arrange / Act
            var ex = new TarException("tar error");

            // Assert
            Assert.Equal("tar error", ex.Message);
        }

        [Fact]
        public void TarException_IsExceptionSubtype()
        {
            // Arrange / Act / Assert
            Assert.IsAssignableFrom<Exception>(new TarException("x"));
        }

        // ─── TarHeader: constructor defaults ─────────────────────────────────────

        [Fact]
        public void TarHeader_DefaultMode_Is511()
        {
            // Arrange / Act
            var header = new TarHeader();

            // Assert
            Assert.Equal(511, header.Mode);
        }

        [Fact]
        public void TarHeader_DefaultUserId_Is61()
        {
            // Arrange / Act
            var header = new TarHeader();

            // Assert
            Assert.Equal(61, header.UserId);
        }

        [Fact]
        public void TarHeader_DefaultGroupId_Is61()
        {
            // Arrange / Act
            var header = new TarHeader();

            // Assert
            Assert.Equal(61, header.GroupId);
        }

        // ─── TarHeader: FileName property ─────────────────────────────────────────

        [Fact]
        public void TarHeader_FileName_GetterStripsEmbeddedNullChars()
        {
            // Arrange – embed a null char to exercise the Replace logic in the getter
            var header = new TarHeader { FileName = "file\0.txt" };

            // Act
            var name = header.FileName;

            // Assert
            Assert.Equal("file.txt", name);
            Assert.False(name.Contains('\0'));
        }

        [Fact]
        public void TarHeader_FileName_ThrowsWhenLongerThan100Chars()
        {
            // Arrange
            var header = new TarHeader();
            var longName = new string('a', 101);

            // Act / Assert
            Assert.Throws<TarException>(() => header.FileName = longName);
        }

        [Fact]
        public void TarHeader_FileName_Exactly100Chars_SetterDoesNotThrow()
        {
            // Arrange
            var header = new TarHeader();
            var name100 = new string('a', 100);

            // Act / Assert – setter allows exactly 100
            header.FileName = name100;
            Assert.Equal(name100, header.FileName);
        }

        // ─── TarHeader: formatting properties ─────────────────────────────────────

        [Fact]
        public void TarHeader_ModeString_IsOctalPaddedTo7Chars()
        {
            // Arrange – 493 decimal = 755 octal (the classic rwxr-xr-x permission)
            var header = new TarHeader { Mode = 493 };

            // Act / Assert
            Assert.Equal("0000755", header.ModeString);
            Assert.Equal(7, header.ModeString.Length);
        }

        [Fact]
        public void TarHeader_UserIdString_IsOctalPaddedTo7Chars()
        {
            // Arrange – 1000 decimal = 1750 octal
            var header = new TarHeader { UserId = 1000 };

            // Act / Assert
            Assert.Equal("0001750", header.UserIdString);
        }

        [Fact]
        public void TarHeader_GroupIdString_IsOctalPaddedTo7Chars()
        {
            // Arrange – 1000 decimal = 1750 octal
            var header = new TarHeader { GroupId = 1000 };

            // Act / Assert
            Assert.Equal("0001750", header.GroupIdString);
        }

        [Fact]
        public void TarHeader_SizeString_IsOctalPaddedTo11Chars()
        {
            // Arrange – 1024 decimal = 2000 octal
            var header = new TarHeader { SizeInBytes = 1024 };

            // Act / Assert
            Assert.Equal("00000002000", header.SizeString);
            Assert.Equal(11, header.SizeString.Length);
        }

        // ─── TarHeader: structural properties ─────────────────────────────────────

        [Fact]
        public void TarHeader_HeaderSize_Is512()
        {
            // Arrange / Act / Assert
            Assert.Equal(512, new TarHeader().HeaderSize);
        }

        [Fact]
        public void TarHeader_GetBytes_Returns512ByteArray()
        {
            // Arrange / Act
            var bytes = new TarHeader().GetBytes();

            // Assert
            Assert.NotNull(bytes);
            Assert.Equal(512, bytes.Length);
        }

        // ─── TarHeader: GetHeaderValue ─────────────────────────────────────────────

        [Fact]
        public void TarHeader_GetHeaderValue_Returns512ByteArray()
        {
            // Arrange
            var header = new TarHeader
            {
                FileName = "test.txt",
                SizeInBytes = 0,
                Mode = 0644,
                LastModification = DateTime.UnixEpoch,
                EntryType = EntryType.File
            };

            // Act
            var bytes = header.GetHeaderValue();

            // Assert
            Assert.NotNull(bytes);
            Assert.Equal(512, bytes.Length);
        }

        [Fact]
        public void TarHeader_GetHeaderValue_EncodesFileNameAtOffset0()
        {
            // Arrange
            var header = new TarHeader
            {
                FileName = "hello.txt",
                SizeInBytes = 0,
                Mode = 0644,
                LastModification = DateTime.UnixEpoch
            };

            // Act
            var bytes = header.GetHeaderValue();

            // Assert – file name occupies the first 100 bytes; the name itself starts at 0
            var decodedName = Encoding.ASCII.GetString(bytes, 0, 9);
            Assert.Equal("hello.txt", decodedName);
        }

        [Fact]
        public void TarHeader_GetHeaderValue_EncodesEntryTypeAtOffset156()
        {
            // Arrange
            var header = new TarHeader
            {
                FileName = "dir/",
                SizeInBytes = 0,
                Mode = 0755,
                LastModification = DateTime.UnixEpoch,
                EntryType = EntryType.Directory
            };

            // Act
            var bytes = header.GetHeaderValue();

            // Assert – entry type byte is at offset 156
            Assert.Equal((byte)EntryType.Directory, bytes[156]);
        }

        // ─── UsTarHeader: IsPathSeparator ─────────────────────────────────────────

        [Theory]
        [InlineData('/')]
        [InlineData('\\')]
        [InlineData('|')]
        public void UsTarHeader_IsPathSeparator_ReturnsTrueForSeparators(char ch)
        {
            // Arrange / Act / Assert
            Assert.True(UsTarHeader.IsPathSeparator(ch));
        }

        [Theory]
        [InlineData('a')]
        [InlineData('.')]
        [InlineData(' ')]
        [InlineData('0')]
        public void UsTarHeader_IsPathSeparator_ReturnsFalseForNonSeparators(char ch)
        {
            // Arrange / Act / Assert
            Assert.False(UsTarHeader.IsPathSeparator(ch));
        }

        // ─── UsTarHeader: FileName splitting ──────────────────────────────────────

        [Fact]
        public void UsTarHeader_FileName_ShortName_GetterReturnsOriginalValue()
        {
            // Arrange
            var header = new UsTarHeader { FileName = "short.txt" };

            // Act / Assert
            Assert.Equal("short.txt", header.FileName);
        }

        [Fact]
        public void UsTarHeader_FileName_LongNameWithSeparator_SplitsAtSeparator()
        {
            // Arrange – 60 a's + "/" + 60 b's = 121 chars (> 100)
            var prefix = new string('a', 60);
            var suffix = "/" + new string('b', 60);
            var longName = prefix + suffix;
            var header = new UsTarHeader { FileName = longName };

            // Act – getter recombines prefix + base
            var result = header.FileName;

            // Assert
            Assert.Equal(longName, result);
        }

        [Fact]
        public void UsTarHeader_FileName_LongerThan255Chars_Throws()
        {
            // Arrange
            var header = new UsTarHeader();
            var tooLong = new string('a', 256);

            // Act / Assert
            Assert.Throws<TarException>(() => header.FileName = tooLong);
        }

        // ─── UsTarHeader: UserName / GroupName validation ─────────────────────────

        [Fact]
        public void UsTarHeader_UserName_ThrowsWhenLongerThan32Chars()
        {
            // Arrange
            var header = new UsTarHeader();

            // Act / Assert
            Assert.Throws<TarException>(() => header.UserName = new string('x', 33));
        }

        [Fact]
        public void UsTarHeader_GroupName_ThrowsWhenLongerThan32Chars()
        {
            // Arrange
            var header = new UsTarHeader();

            // Act / Assert
            Assert.Throws<TarException>(() => header.GroupName = new string('x', 33));
        }

        [Fact]
        public void UsTarHeader_UserName_Exactly32Chars_DoesNotThrow()
        {
            // Arrange
            var header = new UsTarHeader();
            var name = new string('u', 32);

            // Act / Assert
            header.UserName = name;
            Assert.Equal(name, header.UserName);
        }
    }
}
