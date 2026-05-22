using System;
using Xunit;

namespace HeadlessChromium.Puppeteer.Lambda.Dotnet.Tests
{
    public class ChromiumExtractionExceptionTests
    {
        // ─── Message constructor ───────────────────────────────────────────────────

        [Fact]
        public void ChromiumExtractionException_MessageConstructor_PreservesMessage()
        {
            // Arrange / Act
            var ex = new ChromiumExtractionException("something went wrong");

            // Assert
            Assert.Equal("something went wrong", ex.Message);
        }

        [Fact]
        public void ChromiumExtractionException_IsExceptionSubtype()
        {
            // Arrange / Act
            var ex = new ChromiumExtractionException("msg");

            // Assert
            Assert.IsAssignableFrom<Exception>(ex);
        }

        // ─── Inner-exception constructor ───────────────────────────────────────────

        [Fact]
        public void ChromiumExtractionException_InnerExceptionConstructor_PreservesMessageAndInner()
        {
            // Arrange
            var inner = new InvalidOperationException("root cause");

            // Act
            var ex = new ChromiumExtractionException("outer message", inner);

            // Assert
            Assert.Equal("outer message", ex.Message);
            Assert.Same(inner, ex.InnerException);
        }
    }
}
