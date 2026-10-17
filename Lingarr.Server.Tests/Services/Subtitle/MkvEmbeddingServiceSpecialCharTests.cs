using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        [Fact]
        public void CreateProcessStartInfo_PreservesArgumentsAndSetsChildUtf8Locale()
        {
            var parentLang = Environment.GetEnvironmentVariable("LANG");
            var parentLcAll = Environment.GetEnvironmentVariable("LC_ALL");
            var arguments = new List<string>
            {
                "--output",
                "/media/Zoë's “Film”; $(touch nope)|$x.mkv",
                "--track-name",
                "Élodie’s subtitle \"final\"; $(touch nope) | $x"
            };

            var startInfo = MkvEmbeddingService.CreateProcessStartInfo("mkvmerge", arguments, true);

            Assert.Equal("mkvmerge", startInfo.FileName);
            Assert.False(startInfo.UseShellExecute);
            Assert.True(startInfo.RedirectStandardOutput);
            Assert.True(startInfo.RedirectStandardError);
            Assert.True(startInfo.CreateNoWindow);
            Assert.Equal(arguments.Count, startInfo.ArgumentList.Count);
            for (var index = 0; index < arguments.Count; index++)
            {
                Assert.Equal(arguments[index], startInfo.ArgumentList[index]);
            }

            Assert.Equal("C.UTF-8", startInfo.Environment["LANG"]);
            Assert.Equal("C.UTF-8", startInfo.Environment["LC_ALL"]);
            Assert.Equal(parentLang, Environment.GetEnvironmentVariable("LANG"));
            Assert.Equal(parentLcAll, Environment.GetEnvironmentVariable("LC_ALL"));
        }

        [Fact]
        public void CreateProcessStartInfo_LeavesLocaleInheritedWhenUtf8LocaleIsDisabled()
        {
            var parentLang = Environment.GetEnvironmentVariable("LANG");
            var parentLcAll = Environment.GetEnvironmentVariable("LC_ALL");

            var startInfo = MkvEmbeddingService.CreateProcessStartInfo("mkvmerge", new List<string>(), false);

            Assert.Equal(parentLang, GetEnvironmentValue(startInfo, "LANG"));
            Assert.Equal(parentLcAll, GetEnvironmentValue(startInfo, "LC_ALL"));
            Assert.Equal(parentLang, Environment.GetEnvironmentVariable("LANG"));
            Assert.Equal(parentLcAll, Environment.GetEnvironmentVariable("LC_ALL"));
        }

        private static string? GetEnvironmentValue(ProcessStartInfo startInfo, string name)
        {
            return startInfo.Environment.TryGetValue(name, out var value) ? value : null;
        }
    }
}
