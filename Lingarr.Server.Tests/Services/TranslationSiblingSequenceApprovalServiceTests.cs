using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Translation;
using Lingarr.Server.Services.Translation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class TranslationSiblingSequenceApprovalServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LingarrDbContext _dbContext;
    private readonly RecordingCheckpointService _checkpointService = new();
    private readonly RecordingCompletionService _completionService = new();

    public TranslationSiblingSequenceApprovalServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new LingarrDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    [Fact]
    public async Task ProcessMissingTranslationAsync_WhenSameSeasonSiblingHasSameThreeCueRun_CompletesBothRequests()
    {
        var scenario = await CreateScenarioAsync();
        await AddFailedCuesAsync(
            scenario.SiblingRequest,
            (42, "Opening line one"),
            (43, "Opening line two"),
            (44, "Opening line three"));

        var service = CreateService();

        var result = await service.ProcessMissingTranslationAsync(
            scenario.CurrentRequest,
            Missing(
                (10, "Opening line one", true),
                (11, "Opening line two", true),
                (12, "Opening line three", true)),
            CancellationToken.None);

        Assert.True(result.CurrentRequestCompleted);
        Assert.Null(result.RemainingException);
        Assert.Equal(
            new[] { scenario.SiblingRequest.Id, scenario.CurrentRequest.Id }.Order(),
            result.CompletedRequestIds.Order());
        Assert.Equal([10, 11, 12], result.ApprovedPositions.Order());
        Assert.Equal(
            new[] { scenario.SiblingRequest.Id, scenario.CurrentRequest.Id }.Order(),
            _completionService.CompletedRequestIds.Order());

        var approvedCount = await _dbContext.TranslationFailedCues
            .CountAsync(cue => cue.AutoApprovedAt != null);
        Assert.Equal(6, approvedCount);
    }

    [Fact]
    public async Task ProcessMissingTranslationAsync_WhenSiblingIsDifferentSeason_DoesNotApprove()
    {
        var scenario = await CreateScenarioAsync(siblingSeasonNumber: 2);
        await AddFailedCuesAsync(
            scenario.SiblingRequest,
            (42, "Opening line one"),
            (43, "Opening line two"),
            (44, "Opening line three"));

        var service = CreateService();

        var result = await service.ProcessMissingTranslationAsync(
            scenario.CurrentRequest,
            Missing(
                (10, "Opening line one", true),
                (11, "Opening line two", true),
                (12, "Opening line three", true)),
            CancellationToken.None);

        Assert.False(result.CurrentRequestCompleted);
        Assert.NotNull(result.RemainingException);
        Assert.Empty(result.ApprovedPositions);
        Assert.Empty(_completionService.CompletedRequestIds);
    }

    [Fact]
    public async Task ProcessMissingTranslationAsync_WhenRunHasOnlyTwoCues_DoesNotApprove()
    {
        var scenario = await CreateScenarioAsync();
        await AddFailedCuesAsync(
            scenario.SiblingRequest,
            (42, "Opening line one"),
            (43, "Opening line two"));

        var service = CreateService();

        var result = await service.ProcessMissingTranslationAsync(
            scenario.CurrentRequest,
            Missing(
                (10, "Opening line one", true),
                (11, "Opening line two", true)),
            CancellationToken.None);

        Assert.False(result.CurrentRequestCompleted);
        Assert.NotNull(result.RemainingException);
        Assert.Empty(result.ApprovedPositions);
        Assert.Empty(_completionService.CompletedRequestIds);
    }

    [Fact]
    public async Task ProcessMissingTranslationAsync_WhenCueOrderDiffers_DoesNotApprove()
    {
        var scenario = await CreateScenarioAsync();
        await AddFailedCuesAsync(
            scenario.SiblingRequest,
            (42, "Opening line one"),
            (43, "Opening line three"),
            (44, "Opening line two"));

        var service = CreateService();

        var result = await service.ProcessMissingTranslationAsync(
            scenario.CurrentRequest,
            Missing(
                (10, "Opening line one", true),
                (11, "Opening line two", true),
                (12, "Opening line three", true)),
            CancellationToken.None);

        Assert.False(result.CurrentRequestCompleted);
        Assert.NotNull(result.RemainingException);
        Assert.Empty(result.ApprovedPositions);
        Assert.Empty(_completionService.CompletedRequestIds);
    }

    [Fact]
    public async Task ProcessMissingTranslationAsync_WhenOnlyPartOfMissingCuesMatches_CheckpointsApprovedSourceText()
    {
        var scenario = await CreateScenarioAsync();
        await AddFailedCuesAsync(
            scenario.SiblingRequest,
            (42, "Opening line one"),
            (43, "Opening line two"),
            (44, "Opening line three"));

        var service = CreateService();

        var result = await service.ProcessMissingTranslationAsync(
            scenario.CurrentRequest,
            Missing(
                (10, "Opening line one", true),
                (11, "Opening line two", true),
                (12, "Opening line three", true),
                (13, "Unmatched dialogue", true)),
            CancellationToken.None);

        Assert.False(result.CurrentRequestCompleted);
        Assert.NotNull(result.RemainingException);
        Assert.Equal([13], result.RemainingException.MissingCues.Select(cue => cue.Position));
        Assert.Equal([10, 11, 12], result.ApprovedPositions.Order());
        Assert.DoesNotContain(scenario.CurrentRequest.Id, _completionService.CompletedRequestIds);
        Assert.Contains(scenario.SiblingRequest.Id, _completionService.CompletedRequestIds);
        Assert.Equal(
            [10, 11, 12],
            _checkpointService.SavedTranslations
                .Where(save => save.TranslationRequestId == scenario.CurrentRequest.Id)
                .Select(save => save.Position)
                .Order());
    }

    [Fact]
    public async Task ProcessMissingTranslationAsync_WhenMissingCueIsIneligible_DoesNotApprove()
    {
        var scenario = await CreateScenarioAsync();
        await AddFailedCuesAsync(
            scenario.SiblingRequest,
            (42, "Opening line one"),
            (43, "Opening line two"),
            (44, "Opening line three"));

        var service = CreateService();

        var result = await service.ProcessMissingTranslationAsync(
            scenario.CurrentRequest,
            Missing(
                (10, "Opening line one", false),
                (11, "Opening line two", false),
                (12, "Opening line three", false)),
            CancellationToken.None);

        Assert.False(result.CurrentRequestCompleted);
        Assert.NotNull(result.RemainingException);
        Assert.Empty(result.ApprovedPositions);
        Assert.Empty(_completionService.CompletedRequestIds);

        var storedCurrentCues = await _dbContext.TranslationFailedCues
            .Where(cue => cue.TranslationRequestId == scenario.CurrentRequest.Id)
            .ToListAsync();
        Assert.All(storedCurrentCues, cue => Assert.False(cue.AutoApprovalEligible));
    }

    [Fact]
    public async Task ProcessMissingTranslationAsync_WhenSiblingTargetLanguageDiffers_DoesNotApprove()
    {
        var scenario = await CreateScenarioAsync(siblingTargetLanguage: "de");
        await AddFailedCuesAsync(
            scenario.SiblingRequest,
            (42, "Opening line one"),
            (43, "Opening line two"),
            (44, "Opening line three"));

        var service = CreateService();

        var result = await service.ProcessMissingTranslationAsync(
            scenario.CurrentRequest,
            Missing(
                (10, "Opening line one", true),
                (11, "Opening line two", true),
                (12, "Opening line three", true)),
            CancellationToken.None);

        Assert.False(result.CurrentRequestCompleted);
        Assert.NotNull(result.RemainingException);
        Assert.Empty(result.ApprovedPositions);
        Assert.Empty(_completionService.CompletedRequestIds);
    }

    private TranslationSiblingSequenceApprovalService CreateService()
    {
        return new TranslationSiblingSequenceApprovalService(
            _dbContext,
            _checkpointService,
            _completionService,
            NullLogger<TranslationSiblingSequenceApprovalService>.Instance);
    }

    private async Task<TestScenario> CreateScenarioAsync(
        int siblingSeasonNumber = 1,
        string siblingTargetLanguage = "pl")
    {
        var show = new Show
        {
            SonarrId = 100,
            Title = "Repeated Opening Show",
            Path = "/media/show",
            DateAdded = DateTime.UtcNow
        };
        var currentSeason = new Season
        {
            SeasonNumber = 1,
            Path = "/media/show/Season 01",
            Show = show
        };
        var siblingSeason = siblingSeasonNumber == 1
            ? currentSeason
            : new Season
            {
                SeasonNumber = siblingSeasonNumber,
                Path = "/media/show/Season 02",
                Show = show
            };
        var currentEpisode = new Episode
        {
            SonarrId = 1001,
            EpisodeNumber = 1,
            Title = "Current",
            Season = currentSeason,
            Path = "/media/show/Season 01/current.mkv"
        };
        var siblingEpisode = new Episode
        {
            SonarrId = 1002,
            EpisodeNumber = 2,
            Title = "Sibling",
            Season = siblingSeason,
            Path = "/media/show/Season 01/sibling.mkv"
        };

        _dbContext.Episodes.AddRange(currentEpisode, siblingEpisode);
        await _dbContext.SaveChangesAsync();

        var currentRequest = CreateRequest(currentEpisode, TranslationStatus.InProgress, "pl");
        var siblingRequest = CreateRequest(siblingEpisode, TranslationStatus.Failed, siblingTargetLanguage);

        _dbContext.TranslationRequests.AddRange(currentRequest, siblingRequest);
        await _dbContext.SaveChangesAsync();

        return new TestScenario(currentRequest, siblingRequest);
    }

    private static TranslationRequest CreateRequest(
        Episode episode,
        TranslationStatus status,
        string targetLanguage)
    {
        return new TranslationRequest
        {
            MediaId = episode.Id,
            Title = episode.Title,
            SourceLanguage = "en",
            TargetLanguage = targetLanguage,
            SubtitleToTranslate = $"{episode.Title}.en.srt",
            MediaType = MediaType.Episode,
            WorkloadKind = TranslationWorkloadKind.Library,
            WorkloadItemKey = $"episode:{episode.Id}",
            SourceDedupeKey = "primary",
            SourceSubtitleType = "Full",
            SourceSnapshotFingerprint = $"fingerprint-{episode.Id}",
            Status = status,
            IsActive = status == TranslationStatus.InProgress ? true : null
        };
    }

    private async Task AddFailedCuesAsync(
        TranslationRequest request,
        params (int Position, string SourceText)[] cues)
    {
        foreach (var cue in cues)
        {
            var normalized = Normalize(cue.SourceText);
            _dbContext.TranslationFailedCues.Add(new TranslationFailedCue
            {
                TranslationRequestId = request.Id,
                Position = cue.Position,
                SourceText = cue.SourceText,
                NormalizedText = normalized,
                TextHash = Hash(normalized),
                AutoApprovalEligible = true
            });
        }

        await _dbContext.SaveChangesAsync();
    }

    private static MissingTranslationException Missing(
        params (int Position, string SourceText, bool Eligible)[] cues)
    {
        return new MissingTranslationException(cues
            .Select(cue => new MissingTranslationCue(
                cue.Position,
                cue.SourceText,
                cue.Eligible))
            .ToList());
    }

    private static string Normalize(string text)
    {
        return Regex.Replace(text.Trim().ToLowerInvariant(), "\\s+", " ");
    }

    private static string Hash(string normalizedText)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedText));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private sealed record TestScenario(
        TranslationRequest CurrentRequest,
        TranslationRequest SiblingRequest);

    private sealed class RecordingCheckpointService : ITranslationCheckpointService
    {
        public List<(int TranslationRequestId, int Position, string Text)> SavedTranslations { get; } = [];

        public Task<TranslationCheckpoint?> LoadAsync(
            int translationRequestId,
            string sourceFingerprint,
            CancellationToken cancellationToken)
            => Task.FromResult<TranslationCheckpoint?>(null);

        public Task<TranslationCheckpoint?> LoadByRequestIdAsync(
            int translationRequestId,
            CancellationToken cancellationToken)
            => Task.FromResult<TranslationCheckpoint?>(null);

        public Task SaveCheckpointAsync(
            TranslationCheckpoint checkpoint,
            CancellationToken cancellationToken)
        {
            foreach (var (position, text) in checkpoint.Translations)
            {
                SavedTranslations.Add((checkpoint.TranslationRequestId, position, text));
            }

            return Task.CompletedTask;
        }

        public Task SaveTranslationAsync(
            int translationRequestId,
            string sourceFingerprint,
            int position,
            string translatedText,
            CancellationToken cancellationToken)
        {
            SavedTranslations.Add((translationRequestId, position, translatedText));
            return Task.CompletedTask;
        }

        public Task DeleteAsync(int translationRequestId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class RecordingCompletionService : IFailedTranslationCompletionService
    {
        public List<int> CompletedRequestIds { get; } = [];

        public Task<FailedTranslationCompletionResult> CompleteAsync(
            TranslationRequest request,
            IReadOnlyDictionary<int, string> edits,
            IReadOnlySet<int> sourceTextPositions,
            string logMessage,
            CancellationToken cancellationToken)
        {
            CompletedRequestIds.Add(request.Id);
            request.Status = TranslationStatus.Completed;
            request.IsActive = null;

            return Task.FromResult(new FailedTranslationCompletionResult(
                Completed: true,
                AlreadyCompleted: false,
                OutputPath: $"completed-{request.Id}.srt"));
        }
    }
}
