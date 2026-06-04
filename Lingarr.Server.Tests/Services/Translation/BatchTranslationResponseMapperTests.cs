using System.Collections.Generic;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Models.Batch.Response;
using Lingarr.Server.Services.Translation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class BatchTranslationResponseMapperTests
{
    [Fact]
    public void MapAlignedTranslations_ReturnsItemsWithMatchingPositionAndSourceKey()
    {
        var requestedItems = new List<BatchSubtitleItem>
        {
            new() { Position = 10, Line = "Bolin lets fly a flurry of attacks." },
            new() { Position = 11, Line = "Korra ducks." }
        };
        var translatedItems = new List<StructuredBatchResponse>
        {
            new() { Position = 10, SourceKey = SourceKey(10, "Bolin lets fly a flurry of attacks."), Line = "Bolin launches a series of attacks." },
            new() { Position = 11, SourceKey = SourceKey(11, "Korra ducks."), Line = "Korra robi unik." }
        };

        var result = BatchTranslationResponseMapper.MapAlignedTranslations(
            requestedItems,
            translatedItems,
            NullLogger.Instance,
            "test");

        Assert.Equal("Bolin launches a series of attacks.", result[10]);
        Assert.Equal("Korra robi unik.", result[11]);
    }
    [Fact]
    public void MapAlignedTranslations_AcceptsNonEmptyLineWithMismatchedSourceKey()
    {
        var requestedItems = new List<BatchSubtitleItem>
        {
            new() { Position = 10, Line = "Bolin lets fly a flurry of attacks." },
            new() { Position = 11, Line = "Korra ducks." },
            new() { Position = 12, Line = "Round one goes to the Fire Ferrets." },
            new() { Position = 13, Line = "Empty response should still be rejected." }
        };
        var translatedItems = new List<StructuredBatchResponse>
        {
            new() { Position = 10, SourceKey = SourceKey(11, "Korra ducks."), Line = "Korra robi unik." },
            new() { Position = 11, SourceKey = null, Line = "Round one for the Fire Ferrets." },
            new() { Position = 12, SourceKey = SourceKey(12, "Round one goes to the Fire Ferrets."), Line = "Round one for the Fire Ferrets." },
            new() { Position = 13, SourceKey = SourceKey(10, "Bolin lets fly a flurry of attacks."), Line = "" }
        };

        var result = BatchTranslationResponseMapper.MapAlignedTranslations(
            requestedItems,
            translatedItems,
            NullLogger.Instance,
            "test");

        // Position 10: sourceKey from wrong item but non-empty line -> accepted leniently
        Assert.Equal("Korra robi unik.", result[10]);
        // Position 11: null sourceKey but non-empty line -> accepted leniently
        Assert.Equal("Round one for the Fire Ferrets.", result[11]);
        // Position 12: exact match -> accepted as before
        Assert.Equal("Round one for the Fire Ferrets.", result[12]);
        // Position 13: mismatched sourceKey and empty line -> still rejected
        Assert.False(result.ContainsKey(13));
    }

    [Fact]
    public void MapAlignedTranslationsSafe_RejectsNonEmptyLineWithMismatchedSourceKey()
    {
        var requestedItems = new List<BatchSubtitleItem>
        {
            new() { Position = 10, Line = "Bolin lets fly a flurry of attacks." },
            new() { Position = 11, Line = "Korra ducks." },
            new() { Position = 12, Line = "Round one goes to the Fire Ferrets." }
        };
        var translatedItems = new List<StructuredBatchResponse>
        {
            new() { Position = 10, SourceKey = SourceKey(11, "Korra ducks."), Line = "Korra robi unik." },
            new() { Position = 11, SourceKey = null, Line = "Runda dla Fire Ferrets." },
            new() { Position = 12, SourceKey = SourceKey(12, "Round one goes to the Fire Ferrets."), Line = "Pierwsza runda dla Fire Ferrets." }
        };

        var result = BatchTranslationResponseMapper.MapAlignedTranslationsSafe(
            requestedItems,
            translatedItems,
            NullLogger.Instance,
            "test");

        Assert.False(result.ValidTranslations.ContainsKey(10));
        Assert.False(result.ValidTranslations.ContainsKey(11));
        Assert.Equal("Pierwsza runda dla Fire Ferrets.", result.ValidTranslations[12]);
        Assert.Equal([10, 11], result.SourceKeyFailures);
    }

    private static string SourceKey(int position, string line)
    {
        return BatchTranslationResponseMapper.GetSourceKey(new BatchSubtitleItem
        {
            Position = position,
            Line = line
        });
    }
}
