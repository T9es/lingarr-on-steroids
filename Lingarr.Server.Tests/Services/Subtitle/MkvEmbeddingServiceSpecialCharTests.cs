using System.Collections.Generic;
using Xunit;
using Lingarr.Server.Services.Subtitle;

namespace Lingarr.Server.Tests.Services.Subtitle
{
    public class MkvEmbeddingServiceSpecialCharTests
    {
        [Fact]
        public void BuildMkvMergeArguments_PreservesRightSingleQuotationMark()
        {
            // Arrange - U+2019 RIGHT SINGLE QUOTATION MARK
            var rightQuote = new char[] { (char)0x2019 };
            var specialChar = new string(rightQuote);
            var mkvPath = "/media/test" + specialChar + ".mkv";
            var subtitlePath = "/media/test" + specialChar + ".pl.ass";
            var tempOutputPath = "/tmp/test.mkv";
            var excludeTrackIds = new List<int>();

            // Act
            var args = MkvEmbeddingService.BuildMkvMergeArguments(
                mkvPath, subtitlePath, "pl", null, true, tempOutputPath, excludeTrackIds);

            // Assert - the special character is preserved, not mangled
            var argumentsString = string.Join(" ", args);
            Assert.Contains(specialChar, argumentsString);
        }
    }
}
