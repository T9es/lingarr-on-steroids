using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Services.Translation.Base;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class BaseLanguageServiceTests
{
    [Fact]
    public void BuildBatchUserContent_RequiresTranslationsInsteadOfSourcePassthrough()
    {
        var service = new TestLanguageService();

        var content = service.BuildContent(
            [
                new BatchSubtitleItem { Position = 1, Line = "LISTEN, EVERYONE." }
            ]);

        Assert.Contains("must receive target-language translated text", content);
        Assert.Contains("Do not copy source text as a fallback", content);
        Assert.Contains("OCR errors from bitmap subtitles", content);
        Assert.Contains("use JSON \\n for line breaks", content);
        Assert.Contains("Never use raw ASS/SSA \\N", content);
        Assert.DoesNotContain("If you cannot translate a line", content);
        Assert.DoesNotContain("output it exactly as-is", content);
    }

    private sealed class TestLanguageService : BaseLanguageService
    {
        public TestLanguageService()
            : base(
                Mock.Of<ISettingService>(),
                NullLogger.Instance,
                "missing-language-file.json")
        {
        }

        public string BuildContent(List<BatchSubtitleItem> items)
        {
            return BuildBatchUserContent(items, null, null);
        }

        public override Task<string> TranslateAsync(
            string text,
            string sourceLanguage,
            string targetLanguage,
            List<string>? contextLinesBefore,
            List<string>? contextLinesAfter,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(text);
        }
    }
}
