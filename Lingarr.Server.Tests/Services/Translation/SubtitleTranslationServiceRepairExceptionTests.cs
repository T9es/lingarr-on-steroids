using System;
using System.Collections.Generic;
using System.Linq;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Translation;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Subtitle;
using Lingarr.Server.Services.Translation;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class SubtitleTranslationServiceRepairExceptionTests
{
    [Fact]
    public void CreateRepairMissingTranslationException_WhenIncomingCueCarriesRejectedText_PreservesIt()
    {
        var structureEntries = BuildStructureEntries();
        var missingCues = new List<MissingTranslationCue>
        {
            new(
                10,
                "This is ordinary dialogue line ten",
                AutoApprovalEligible: true,
                RejectedText: "To jest zwykla linia dialogu dziesiec",
                RejectionReason: ProviderRejectionReason.WrongTargetLanguage)
        };

        var exception = SubtitleTranslationService.CreateRepairMissingTranslationException(
            structureEntries,
            [
                new RepairItem
                {
                    Position = 10,
                    OriginalLine = "This is ordinary dialogue line ten",
                    OriginalBatchIndex = 1
                }
            ],
            missingCues,
            new InvalidOperationException("repair failed"));

        var cue = Assert.Single(exception.MissingCues);
        Assert.Equal(10, cue.Position);
        Assert.Equal("To jest zwykla linia dialogu dziesiec", cue.RejectedText);
        Assert.Equal(ProviderRejectionReason.WrongTargetLanguage, cue.RejectionReason);
    }

    [Fact]
    public void CreateRepairMissingTranslationException_WhenRunLevelRejectedTranslationExists_AttachesIt()
    {
        var structureEntries = BuildStructureEntries();

        var exception = SubtitleTranslationService.CreateRepairMissingTranslationException(
            structureEntries,
            [
                new RepairItem
                {
                    Position = 20,
                    OriginalLine = "This is ordinary dialogue line twenty",
                    OriginalBatchIndex = 2
                }
            ],
            missingCues: null,
            new InvalidOperationException("repair failed"),
            new Dictionary<int, SubtitleTranslationService.RejectedProviderTranslation>
            {
                [20] = new(
                    "To jest zwykla linia dialogu dwadziescia",
                    ProviderRejectionReason.SourceEcho)
            });

        var cue = Assert.Single(exception.MissingCues);
        Assert.Equal(20, cue.Position);
        Assert.Equal("To jest zwykla linia dialogu dwadziescia", cue.RejectedText);
        Assert.Equal(ProviderRejectionReason.SourceEcho, cue.RejectionReason);
    }

    [Fact]
    public void CreateRepairMissingTranslationException_WhenRunLevelRejectedTranslationBelongsToDuplicate_AttachesToEveryMember()
    {
        var structureEntries = BuildStructureEntries(
            (30, "This line is repeated twice"),
            (31, "This line is repeated twice"));

        var exception = SubtitleTranslationService.CreateRepairMissingTranslationException(
            structureEntries,
            [
                new RepairItem
                {
                    Position = 30,
                    OriginalLine = "This line is repeated twice",
                    OriginalBatchIndex = 3
                }
            ],
            missingCues: null,
            new InvalidOperationException("repair failed"),
            new Dictionary<int, SubtitleTranslationService.RejectedProviderTranslation>
            {
                [30] = new(
                    "Ta linia jest powtorzona dwa razy",
                    ProviderRejectionReason.WrongTargetLanguage)
            });

        Assert.Equal([30, 31], exception.MissingCues.Select(cue => cue.Position).Order());
        Assert.All(exception.MissingCues, cue =>
        {
            Assert.Equal("Ta linia jest powtorzona dwa razy", cue.RejectedText);
            Assert.Equal(ProviderRejectionReason.WrongTargetLanguage, cue.RejectionReason);
        });
    }

    private static List<SubtitleTranslationNode> BuildStructureEntries(
        params (int Position, string Text)[]? cues)
    {
        var effectiveCues = cues is { Length: > 0 } ? cues : DefaultCues;
        var items = effectiveCues
            .Select(cue => new SubtitleItem
            {
                Position = cue.Position,
                StartTime = cue.Position * 1000,
                EndTime = cue.Position * 1000 + 900,
                Lines = [cue.Text],
                PlaintextLines = [cue.Text]
            })
            .ToList();

        return SubtitleTranslationNodePlanner
            .Plan(items, stripSubtitleFormatting: false, preserveAssFormatting: false)
            .Nodes
            .ToList();
    }

    private static readonly (int Position, string Text)[] DefaultCues =
    [
        (10, "This is ordinary dialogue line ten"),
        (20, "This is ordinary dialogue line twenty")
    ];
}
