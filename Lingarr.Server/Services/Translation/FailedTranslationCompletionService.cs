using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Translation;
using Lingarr.Server.Services.Subtitle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lingarr.Server.Services.Translation;

public class FailedTranslationCompletionService : IFailedTranslationCompletionService
{
    /// <summary>
    /// Completed-request claims (JobId set on Completed rows while saving compare edits)
    /// are considered stale after this window. A crash between claim and release would
    /// otherwise wedge the row permanently, because nothing sweeps Completed claims.
    /// </summary>
    private static readonly TimeSpan CompletedClaimStaleAfter = TimeSpan.FromMinutes(30);

    private static readonly HashSet<string> MediaFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".wmv", ".flv", ".ts", ".m2ts", ".mov", ".webm", ".mpg", ".mpeg", ".m4v"
    };

    private readonly LingarrDbContext _dbContext;
    private readonly ISourceSubtitleResolver _sourceSubtitleResolver;
    private readonly ISubtitleService _subtitleService;
    private readonly ITranslationCheckpointService _checkpointService;
    private readonly ISettingService _settingService;
    private readonly ITranslationRequestService _translationRequestService;
    private readonly IProgressService _progressService;
    private readonly IMediaStateService _mediaStateService;
    private readonly IMkvEmbeddingService _mkvEmbeddingService;
    private readonly IEmbeddedSubtitleCacheService _embeddedSubtitleCacheService;
    private readonly ILogger<FailedTranslationCompletionService> _logger;
    private readonly Func<Task>? _beforeFinalCommitAsync;

    public FailedTranslationCompletionService(
        LingarrDbContext dbContext,
        ISourceSubtitleResolver sourceSubtitleResolver,
        ISubtitleService subtitleService,
        ITranslationCheckpointService checkpointService,
        ISettingService settingService,
        ITranslationRequestService translationRequestService,
        IProgressService progressService,
        IMediaStateService mediaStateService,
        ILogger<FailedTranslationCompletionService> logger,
        IMkvEmbeddingService? mkvEmbeddingService = null,
        IEmbeddedSubtitleCacheService? embeddedSubtitleCacheService = null)
        : this(
            dbContext,
            sourceSubtitleResolver,
            subtitleService,
            checkpointService,
            settingService,
            translationRequestService,
            progressService,
            mediaStateService,
            logger,
            beforeFinalCommitAsync: null,
            mkvEmbeddingService,
            embeddedSubtitleCacheService)
    {
    }

    internal FailedTranslationCompletionService(
        LingarrDbContext dbContext,
        ISourceSubtitleResolver sourceSubtitleResolver,
        ISubtitleService subtitleService,
        ITranslationCheckpointService checkpointService,
        ISettingService settingService,
        ITranslationRequestService translationRequestService,
        IProgressService progressService,
        IMediaStateService mediaStateService,
        ILogger<FailedTranslationCompletionService> logger,
        Func<Task>? beforeFinalCommitAsync,
        IMkvEmbeddingService? mkvEmbeddingService = null,
        IEmbeddedSubtitleCacheService? embeddedSubtitleCacheService = null)
    {
        _dbContext = dbContext;
        _sourceSubtitleResolver = sourceSubtitleResolver;
        _subtitleService = subtitleService;
        _checkpointService = checkpointService;
        _settingService = settingService;
        _translationRequestService = translationRequestService;
        _progressService = progressService;
        _mediaStateService = mediaStateService;
        _mkvEmbeddingService = mkvEmbeddingService ??
            new MkvEmbeddingService(NullLogger<MkvEmbeddingService>.Instance);
        _embeddedSubtitleCacheService = embeddedSubtitleCacheService ??
            new EmbeddedSubtitleCacheService(NullLogger<EmbeddedSubtitleCacheService>.Instance);
        _logger = logger;
        _beforeFinalCommitAsync = beforeFinalCommitAsync;
    }

    public async Task<FailedTranslationCompletionResult> CompleteAsync(
        TranslationRequest request,
        IReadOnlyDictionary<int, string> edits,
        IReadOnlySet<int> sourceTextPositions,
        string logMessage,
        CancellationToken cancellationToken)
    {
        var dbRequest = await _dbContext.TranslationRequests
            .FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken);
        if (dbRequest == null)
        {
            return new FailedTranslationCompletionResult(
                Completed: false,
                AlreadyCompleted: false,
                OutputPath: null,
                SkippedReason: $"Translation request {request.Id} was not found.");
        }

        if (dbRequest.Status == TranslationStatus.Completed &&
            !string.IsNullOrWhiteSpace(dbRequest.TranslatedSubtitle))
        {
            return new FailedTranslationCompletionResult(
                Completed: true,
                AlreadyCompleted: true,
                OutputPath: dbRequest.TranslatedSubtitle);
        }

        if (dbRequest.Status != TranslationStatus.Failed)
        {
            return new FailedTranslationCompletionResult(
                Completed: false,
                AlreadyCompleted: false,
                OutputPath: null,
                SkippedReason: $"Translation request {dbRequest.Id} is no longer failed (current state: {dbRequest.Status}).");
        }

        var sourcePath = await _sourceSubtitleResolver.ResolveReadableSourcePathAsync(
            dbRequest,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return new FailedTranslationCompletionResult(
                Completed: false,
                AlreadyCompleted: false,
                OutputPath: null,
                SkippedReason: "Source subtitle file could not be resolved.");
        }

        var ownershipToken = $"failed-compare-{Guid.NewGuid():N}";
        var claimRowsUpdated = await _dbContext.TranslationRequests
            .Where(item => item.Id == dbRequest.Id &&
                           item.Status == TranslationStatus.Failed &&
                           item.JobId == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, TranslationStatus.InProgress)
                .SetProperty(item => item.IsActive, (bool?)true)
                .SetProperty(item => item.JobId, ownershipToken)
                .SetProperty(item => item.UpdatedAt, DateTime.UtcNow),
                cancellationToken);

        if (claimRowsUpdated == 0)
        {
            return await BuildCurrentStateResultAsync(dbRequest, cancellationToken);
        }

        WrittenSubtitleOutput? writtenOutput = null;
        PublishedSubtitleOutputs? publishedOutputs = null;
        EmbeddedPublicationTransaction? embeddedPublication = null;
        var completionCommitted = false;
        IAsyncDisposable? publicationLease = null;

        try
        {
            var checkpoint = await _checkpointService.LoadByRequestIdAsync(
                dbRequest.Id,
                cancellationToken);

            var settings = await _settingService.GetSettings([
            SettingKeys.Translation.UseSubtitleTagging,
            SettingKeys.Translation.RemoveLanguageTag,
            SettingKeys.Translation.SubtitleTag,
            SettingKeys.Translation.SubtitleTagShort,
            SettingKeys.Translation.StripSubtitleFormatting,
            SettingKeys.Translation.SubtitleOutputMode,
            SettingKeys.Translation.EmbedInContainer,
            SettingKeys.Translation.EmbedWhenPathTooLong
            ]);

            var originalSubtitles = await _subtitleService.ReadSubtitles(sourcePath);
            var checkpointHydration = await FailedTranslationCheckpointRules.HydrateAsync(
            dbRequest,
            sourcePath,
            checkpoint,
            originalSubtitles,
                _checkpointService,
                cancellationToken,
                ownershipToken);
            var checkpointTranslations = checkpointHydration.Translations;

            var currentPositions = originalSubtitles
            .Select(subtitle => subtitle.Position)
            .ToHashSet();
            var currentEdits = edits
            .Where(edit => currentPositions.Contains(edit.Key))
            .ToDictionary(edit => edit.Key, edit => edit.Value);
            var validSourceTextPositions = checkpointHydration.SourceTextPositions
                .Union(sourceTextPositions)
                .Where(checkpointHydration.SourceTextPositions.Contains)
                .ToHashSet();
            var unresolvedRequiredPositions = checkpointHydration.RequiredMissingPositions
            .Where(position => !currentEdits.ContainsKey(position))
            .Order()
            .ToList();
            if (unresolvedRequiredPositions.Count > 0)
            {
                return new FailedTranslationCompletionResult(
                Completed: false,
                AlreadyCompleted: false,
                OutputPath: null,
                SkippedReason:
                    $"Untranslated ordinary subtitle position(s) remain retryable: " +
                    string.Join(", ", unresolvedRequiredPositions));
            }
            if (checkpointTranslations.Count == 0 &&
            currentEdits.Count == 0 &&
            validSourceTextPositions.Count == 0)
            {
                return new FailedTranslationCompletionResult(
                Completed: false,
                AlreadyCompleted: false,
                OutputPath: null,
                SkippedReason: "No valid checkpoint translations or source-preserved positions were available.");
            }

            var useSubtitleTagging =
            settings.TryGetValue(SettingKeys.Translation.UseSubtitleTagging, out var useTaggingValue) &&
            string.Equals(useTaggingValue, "true", StringComparison.OrdinalIgnoreCase);
        var removeLanguageTag =
            settings.TryGetValue(SettingKeys.Translation.RemoveLanguageTag, out var removeLanguageTagValue) &&
            string.Equals(removeLanguageTagValue, "true", StringComparison.OrdinalIgnoreCase);
        var subtitleTag = useSubtitleTagging
            ? settings.GetValueOrDefault(SettingKeys.Translation.SubtitleTag) ?? string.Empty
            : string.Empty;
        var subtitleTagShort = useSubtitleTagging
            ? settings.GetValueOrDefault(SettingKeys.Translation.SubtitleTagShort) ?? string.Empty
            : string.Empty;

        var targetLanguage = removeLanguageTag ? string.Empty : dbRequest.TargetLanguage;
        var stripFormatting =
            settings.TryGetValue(SettingKeys.Translation.StripSubtitleFormatting, out var stripValue) &&
            string.Equals(stripValue, "true", StringComparison.OrdinalIgnoreCase);
        var embedInContainer = settings.TryGetValue(SettingKeys.Translation.EmbedInContainer, out var embedValue) &&
                               string.Equals(embedValue, "true", StringComparison.OrdinalIgnoreCase);
        var embedWhenPathTooLong = settings.TryGetValue(SettingKeys.Translation.EmbedWhenPathTooLong, out var pathTooLongValue) &&
                                   string.Equals(pathTooLongValue, "true", StringComparison.OrdinalIgnoreCase);

        var sourceFormat = SubtitleOutputModeHelper.NormalizeFormat(Path.GetExtension(sourcePath));
        var subtitleOutputMode = SubtitleOutputModeHelper.Parse(
            !string.IsNullOrWhiteSpace(dbRequest.SubtitleOutputMode)
                ? dbRequest.SubtitleOutputMode
                : settings.GetValueOrDefault(SettingKeys.Translation.SubtitleOutputMode));
        var requiredOutputFormats = SubtitleOutputModeHelper.DeserializeFormats(dbRequest.RequiredOutputFormats);
        if (requiredOutputFormats.Count == 0)
        {
            requiredOutputFormats = SubtitleOutputModeHelper.GetRequiredOutputFormats(
                sourceFormat,
                subtitleOutputMode);
        }

        var writesPreservedAssOutput =
            SubtitleOutputModeHelper.IsAssFormat(sourceFormat) &&
            requiredOutputFormats.Any(SubtitleOutputModeHelper.IsAssFormat);
        var translatedSubtitles = BuildTranslatedSubtitles(
            originalSubtitles,
            checkpointTranslations,
            currentEdits,
            validSourceTextPositions,
            stripFormatting && !writesPreservedAssOutput,
            writesPreservedAssOutput);
            var normalizedOutputMode = subtitleOutputMode.ToSettingValue();
            var serializedRequiredOutputFormats = SubtitleOutputModeHelper.SerializeFormats(requiredOutputFormats);

            writtenOutput = await WriteSubtitleOutputsAsync(
                dbRequest,
                sourcePath,
                translatedSubtitles,
                requiredOutputFormats,
                targetLanguage,
                subtitleTag,
                subtitleTagShort,
                stripFormatting,
                writesPreservedAssOutput,
                embedInContainer,
                embedWhenPathTooLong,
                ownershipToken,
                cancellationToken);

            publicationLease = await MkvEmbeddingService.AcquirePublicationLeaseAsync(
                GetPublicationPaths(writtenOutput),
                cancellationToken);

            if (_beforeFinalCommitAsync != null)
            {
                await _beforeFinalCommitAsync();
            }

            var stillOwnsRequest = await ConfirmOwnershipAsync(
                dbRequest.Id,
                ownershipToken,
                cancellationToken);
            if (!stillOwnsRequest)
            {
                await _dbContext.Entry(dbRequest).ReloadAsync(cancellationToken);
                CleanupStagedOutputs(writtenOutput);
                return BuildCurrentStateResult(dbRequest);
            }

            publishedOutputs = PublishStagedOutputs(writtenOutput, ownershipToken, dbRequest.Id, _logger);
            embeddedPublication = await CreateEmbeddedPublicationTransactionAsync(
                writtenOutput,
                ownershipToken,
                dbRequest.Id,
                cancellationToken);
            await PublishStagedEmbeddedOutputsAsync(
                writtenOutput,
                dbRequest,
                embeddedPublication,
                cancellationToken);

            var generatedSubtitlePaths = JsonSerializer.Serialize(writtenOutput.FinalPaths);
            var completedAt = DateTime.UtcNow;
            var completionRowsUpdated = await _dbContext.TranslationRequests
                .Where(item => item.Id == dbRequest.Id &&
                               item.Status == TranslationStatus.InProgress &&
                               item.JobId == ownershipToken)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.SourceSubtitleFormat, sourceFormat)
                    .SetProperty(item => item.SubtitleOutputMode, normalizedOutputMode)
                    .SetProperty(item => item.RequiredOutputFormats, serializedRequiredOutputFormats)
                    .SetProperty(item => item.SubtitleToTranslate, sourcePath)
                    .SetProperty(item => item.TranslatedSubtitle, writtenOutput.PrimaryPath)
                    .SetProperty(item => item.GeneratedOutputFormats, writtenOutput.GeneratedFormats)
                    .SetProperty(item => item.GeneratedSubtitlePaths, generatedSubtitlePaths)
                    .SetProperty(item => item.Status, TranslationStatus.Completed)
                    .SetProperty(item => item.CompletedAt, completedAt)
                    .SetProperty(item => item.IsActive, (bool?)null)
                    .SetProperty(item => item.JobId, (string?)null)
                    .SetProperty(item => item.NextRetryAt, (DateTime?)null)
                    .SetProperty(item => item.PausedAt, (DateTime?)null)
                    .SetProperty(item => item.PauseReason, (string?)null)
                    .SetProperty(item => item.PausedProvider, (string?)null)
                    .SetProperty(item => item.RetryCount, 0)
                    .SetProperty(item => item.UpdatedAt, completedAt),
                    cancellationToken);

            if (completionRowsUpdated == 0)
            {
                await _dbContext.Entry(dbRequest).ReloadAsync(cancellationToken);
                var completedByOthers = dbRequest.Status == TranslationStatus.Completed;
                if (!completedByOthers)
                {
                    RollbackPublishedOutputs(publishedOutputs);
                    RollbackEmbeddedPublication(embeddedPublication);
                }

                publishedOutputs = null;
                embeddedPublication = null;
                CleanupStagedOutputs(writtenOutput);
                return BuildCurrentStateResult(dbRequest);
            }

            completionCommitted = true;
            CleanupPublicationBackups(publishedOutputs);
            CleanupEmbeddedPublicationBackups(embeddedPublication);

            await _dbContext.Entry(dbRequest).ReloadAsync(cancellationToken);
            _dbContext.TranslationRequestLogs.Add(new TranslationRequestLog
            {
                TranslationRequestId = dbRequest.Id,
                Level = "Information",
                Message = logMessage
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            await _checkpointService.DeleteAsync(
                dbRequest.Id,
                cancellationToken,
                ownershipToken);
            await NotifyCompletionAsync(dbRequest, cancellationToken);

            return new FailedTranslationCompletionResult(
                Completed: true,
                AlreadyCompleted: false,
                OutputPath: writtenOutput.PrimaryPath);
        }
        catch (Exception exception)
        {
            var completedByOthers = false;
            try
            {
                completedByOthers = await _dbContext.TranslationRequests
                    .Where(item => item.Id == dbRequest.Id)
                    .Select(item => item.Status)
                    .FirstOrDefaultAsync(CancellationToken.None) == TranslationStatus.Completed;
            }
            catch (Exception stateQueryException)
            {
                _logger.LogWarning(
                    stateQueryException,
                    "Could not verify request state for translation request {RequestId}; keeping the existing rollback behavior",
                    dbRequest.Id);
            }

            if (!completionCommitted && publishedOutputs != null && !completedByOthers)
            {
                RollbackPublishedOutputs(publishedOutputs);
            }

            if (!completionCommitted && !completedByOthers)
            {
                RollbackEmbeddedPublication(embeddedPublication);
            }

            if (writtenOutput != null)
            {
                CleanupStagedOutputs(writtenOutput);
            }

            if (!completionCommitted)
            {
                await RecordCompletionFailureAsync(dbRequest.Id, exception);

                if (exception is RequiredEmbeddingException)
                {
                    return new FailedTranslationCompletionResult(
                        Completed: false,
                        AlreadyCompleted: false,
                        OutputPath: null,
                        SkippedReason: exception.Message);
                }
            }

            throw;
        }
        finally
        {
            if (!completionCommitted)
            {
                await ReleaseOwnershipAsync(dbRequest.Id, ownershipToken, CancellationToken.None);
            }

            if (publicationLease != null)
            {
                await publicationLease.DisposeAsync();
            }
        }
    }

    public async Task<FailedTranslationCompletionResult> PublishCompletedEditsAsync(
        TranslationRequest request,
        string sourcePath,
        IReadOnlyList<SubtitleItem> translatedSubtitles,
        CancellationToken cancellationToken)
    {
        var dbRequest = await _dbContext.TranslationRequests
            .FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken);
        if (dbRequest == null)
        {
            return new FailedTranslationCompletionResult(
                Completed: false,
                AlreadyCompleted: false,
                OutputPath: null,
                SkippedReason: $"Translation request {request.Id} was not found.");
        }

        if (dbRequest.Status != TranslationStatus.Completed)
        {
            return new FailedTranslationCompletionResult(
                Completed: false,
                AlreadyCompleted: false,
                OutputPath: null,
                SkippedReason:
                    $"Translation request {dbRequest.Id} is no longer completed (current state: {dbRequest.Status}).");
        }

        var settings = await _settingService.GetSettings([
            SettingKeys.Translation.UseSubtitleTagging,
            SettingKeys.Translation.RemoveLanguageTag,
            SettingKeys.Translation.SubtitleTag,
            SettingKeys.Translation.SubtitleTagShort,
            SettingKeys.Translation.StripSubtitleFormatting,
            SettingKeys.Translation.SubtitleOutputMode
        ]);
        var sourceFormat = SubtitleOutputModeHelper.NormalizeFormat(Path.GetExtension(sourcePath));
        if (string.IsNullOrWhiteSpace(sourceFormat))
        {
            sourceFormat = SubtitleOutputModeHelper.NormalizeFormat(dbRequest.SourceSubtitleFormat);
        }

        var subtitleOutputMode = SubtitleOutputModeHelper.Parse(
            dbRequest.SubtitleOutputMode ??
            settings.GetValueOrDefault(SettingKeys.Translation.SubtitleOutputMode));
        var requiredOutputFormats = SubtitleOutputModeHelper.DeserializeFormats(
            dbRequest.RequiredOutputFormats);
        if (requiredOutputFormats.Count == 0)
        {
            requiredOutputFormats = SubtitleOutputModeHelper.DeserializeFormats(
                dbRequest.GeneratedOutputFormats);
        }
        if (requiredOutputFormats.Count == 0)
        {
            requiredOutputFormats = SubtitleOutputModeHelper.GetRequiredOutputFormats(
                sourceFormat,
                subtitleOutputMode);
        }

        var existingOutputPaths = GetPersistedOutputPaths(dbRequest);
        var hasEmbeddedOutput = existingOutputPaths.Any(IsEmbeddedOutputPath);
        var useSubtitleTagging = settings.TryGetValue(
                                      SettingKeys.Translation.UseSubtitleTagging,
                                      out var useTaggingValue) &&
                                  string.Equals(useTaggingValue, "true", StringComparison.OrdinalIgnoreCase);
        var removeLanguageTag = settings.TryGetValue(
                                    SettingKeys.Translation.RemoveLanguageTag,
                                    out var removeLanguageTagValue) &&
                                string.Equals(removeLanguageTagValue, "true", StringComparison.OrdinalIgnoreCase);
        var subtitleTag = useSubtitleTagging
            ? settings.GetValueOrDefault(SettingKeys.Translation.SubtitleTag) ?? string.Empty
            : string.Empty;
        var subtitleTagShort = useSubtitleTagging
            ? settings.GetValueOrDefault(SettingKeys.Translation.SubtitleTagShort) ?? string.Empty
            : string.Empty;
        var stripFormatting = settings.TryGetValue(
                                  SettingKeys.Translation.StripSubtitleFormatting,
                                  out var stripValue) &&
                              string.Equals(stripValue, "true", StringComparison.OrdinalIgnoreCase);
        var writesPreservedAssOutput = SubtitleOutputModeHelper.IsAssFormat(sourceFormat) &&
                                       requiredOutputFormats.Any(SubtitleOutputModeHelper.IsAssFormat);
        var ownershipToken = $"completed-compare-{Guid.NewGuid():N}";
        var staleClaimBefore = DateTime.UtcNow - CompletedClaimStaleAfter;
        var claimRowsUpdated = await _dbContext.TranslationRequests
            .Where(item => item.Id == dbRequest.Id &&
                           item.Status == TranslationStatus.Completed &&
                           (item.JobId == null || item.UpdatedAt < staleClaimBefore))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.JobId, ownershipToken)
                .SetProperty(item => item.UpdatedAt, DateTime.UtcNow),
                cancellationToken);
        if (claimRowsUpdated == 0)
        {
            return new FailedTranslationCompletionResult(
                Completed: false,
                AlreadyCompleted: false,
                OutputPath: null,
                SkippedReason:
                    "The completed translation changed while saving edits. Reload the compare view and try again.");
        }

        WrittenSubtitleOutput? writtenOutput = null;
        PublishedSubtitleOutputs? publishedOutputs = null;
        EmbeddedPublicationTransaction? embeddedPublication = null;
        IAsyncDisposable? publicationLease = null;
        var publicationCommitted = false;

        try
        {
            writtenOutput = await WriteSubtitleOutputsAsync(
                dbRequest,
                sourcePath,
                translatedSubtitles.ToList(),
                requiredOutputFormats,
                removeLanguageTag ? string.Empty : dbRequest.TargetLanguage,
                subtitleTag,
                subtitleTagShort,
                stripFormatting,
                writesPreservedAssOutput,
                hasEmbeddedOutput,
                embedWhenPathTooLong: false,
                ownershipToken,
                cancellationToken,
                BuildPreferredOutputPaths(existingOutputPaths));

            publicationLease = await MkvEmbeddingService.AcquirePublicationLeaseAsync(
                GetPublicationPaths(writtenOutput),
                cancellationToken);

            if (_beforeFinalCommitAsync != null)
            {
                await _beforeFinalCommitAsync();
            }

            if (!await ConfirmCompletedPublicationOwnershipAsync(
                    dbRequest.Id,
                    ownershipToken,
                    cancellationToken))
            {
                await _dbContext.Entry(dbRequest).ReloadAsync(cancellationToken);
                CleanupStagedOutputs(writtenOutput);
                return new FailedTranslationCompletionResult(
                    Completed: false,
                    AlreadyCompleted: dbRequest.Status == TranslationStatus.Completed,
                    OutputPath: dbRequest.TranslatedSubtitle,
                    SkippedReason:
                        $"Translation request {dbRequest.Id} changed while saving edits (current state: {dbRequest.Status}).");
            }

            publishedOutputs = PublishStagedOutputs(writtenOutput, ownershipToken, dbRequest.Id, _logger);
            embeddedPublication = await CreateEmbeddedPublicationTransactionAsync(
                writtenOutput,
                ownershipToken,
                dbRequest.Id,
                cancellationToken);
            await PublishStagedEmbeddedOutputsAsync(
                writtenOutput,
                dbRequest,
                embeddedPublication,
                cancellationToken);

            var generatedSubtitlePaths = JsonSerializer.Serialize(writtenOutput.FinalPaths);
            var rowsUpdated = await _dbContext.TranslationRequests
                .Where(item => item.Id == dbRequest.Id &&
                               item.Status == TranslationStatus.Completed &&
                               item.JobId == ownershipToken)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.TranslatedSubtitle, writtenOutput.PrimaryPath)
                    .SetProperty(item => item.GeneratedOutputFormats, writtenOutput.GeneratedFormats)
                    .SetProperty(item => item.GeneratedSubtitlePaths, generatedSubtitlePaths)
                    .SetProperty(item => item.JobId, (string?)null)
                    .SetProperty(item => item.UpdatedAt, DateTime.UtcNow),
                    cancellationToken);
            if (rowsUpdated == 0)
            {
                await _dbContext.Entry(dbRequest).ReloadAsync(cancellationToken);
                var completedByOthers = dbRequest.Status == TranslationStatus.Completed;
                if (!completedByOthers)
                {
                    RollbackPublishedOutputs(publishedOutputs);
                    RollbackEmbeddedPublication(embeddedPublication);
                }

                publishedOutputs = null;
                embeddedPublication = null;
                CleanupStagedOutputs(writtenOutput);
                return new FailedTranslationCompletionResult(
                    Completed: false,
                    AlreadyCompleted: dbRequest.Status == TranslationStatus.Completed,
                    OutputPath: dbRequest.TranslatedSubtitle,
                    SkippedReason:
                        $"Translation request {dbRequest.Id} changed while saving edits (current state: {dbRequest.Status}).");
            }

            publicationCommitted = true;
            CleanupPublicationBackups(publishedOutputs);
            CleanupEmbeddedPublicationBackups(embeddedPublication);
            return new FailedTranslationCompletionResult(
                Completed: true,
                AlreadyCompleted: false,
                OutputPath: writtenOutput.PrimaryPath);
        }
        catch (Exception exception)
        {
            var completedByOthers = false;
            try
            {
                completedByOthers = await _dbContext.TranslationRequests
                    .Where(item => item.Id == dbRequest.Id)
                    .Select(item => item.Status)
                    .FirstOrDefaultAsync(CancellationToken.None) == TranslationStatus.Completed;
            }
            catch (Exception stateQueryException)
            {
                _logger.LogWarning(
                    stateQueryException,
                    "Could not verify request state for translation request {RequestId}; keeping the existing rollback behavior",
                    dbRequest.Id);
            }

            if (!publicationCommitted && publishedOutputs != null && !completedByOthers)
            {
                RollbackPublishedOutputs(publishedOutputs);
            }

            if (!publicationCommitted && !completedByOthers)
            {
                RollbackEmbeddedPublication(embeddedPublication);
            }

            if (writtenOutput != null)
            {
                CleanupStagedOutputs(writtenOutput);
            }

            _logger.LogError(
                exception,
                "Failed to publish completed compare edits for request {RequestId}",
                dbRequest.Id);
            return new FailedTranslationCompletionResult(
                Completed: false,
                AlreadyCompleted: false,
                OutputPath: null,
                SkippedReason: exception.Message);
        }
        finally
        {
            if (!publicationCommitted)
            {
                await ReleaseCompletedPublicationOwnershipAsync(
                    dbRequest.Id,
                    ownershipToken,
                    CancellationToken.None);
            }

            if (publicationLease != null)
            {
                await publicationLease.DisposeAsync();
            }
        }
    }

    private async Task<IReadOnlyDictionary<int, string>> ValidateCheckpointAsync(
        TranslationRequest request,
        string sourcePath,
        TranslationCheckpoint? checkpoint,
        IReadOnlyList<SubtitleItem> originalSubtitles,
        CancellationToken cancellationToken)
    {
        if (checkpoint == null || checkpoint.Translations.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        var currentPositions = originalSubtitles
            .Select(subtitle => subtitle.Position)
            .ToHashSet();
        var sourceFingerprint = await GetCheckpointFingerprintAsync(
            request,
            sourcePath,
            cancellationToken);
        var checkpointMatchesRequest = CheckpointMatchesRequest(sourceFingerprint, checkpoint);
        var originalTranslations = checkpoint.Translations.ToDictionary(
            translation => translation.Key,
            translation => translation.Value);
        var validTranslations = new Dictionary<int, string>();
        var rejectedPositions = new HashSet<int>();

        foreach (var translation in originalTranslations)
        {
            if (!checkpointMatchesRequest ||
                !currentPositions.Contains(translation.Key) ||
                string.IsNullOrWhiteSpace(translation.Value))
            {
                rejectedPositions.Add(translation.Key);
                continue;
            }

            validTranslations[translation.Key] = translation.Value;
        }

        if (checkpointMatchesRequest && validTranslations.Count > 0)
        {
            var validation = ProviderTranslationValidation.Analyze(
                originalSubtitles.Select(CreateValidationItem).ToList(),
                validTranslations,
                request.SourceLanguage,
                request.TargetLanguage);

            foreach (var invalidPosition in validation.InvalidPositions)
            {
                if (validTranslations.Remove(invalidPosition))
                {
                    rejectedPositions.Add(invalidPosition);
                }
            }
        }

        var checkpointChanged = rejectedPositions.Count > 0 ||
            validTranslations.Count != originalTranslations.Count;
        if (!checkpointChanged)
        {
            return validTranslations;
        }

        checkpoint.Translations = validTranslations;
        if (!checkpointMatchesRequest)
        {
            checkpoint.SourceFingerprint = sourceFingerprint;
        }

        await _checkpointService.SaveCheckpointAsync(checkpoint, cancellationToken);

        _logger.LogWarning(
            "Rejected {RejectedCheckpointCount} stale or invalid checkpoint translation(s) for failed request {RequestId}; {AcceptedCheckpointCount} valid cached translation(s) remain.",
            rejectedPositions.Count,
            request.Id,
            validTranslations.Count);

        return validTranslations;
    }

    private static bool CheckpointMatchesRequest(
        string sourceFingerprint,
        TranslationCheckpoint checkpoint)
    {
        return string.Equals(
            sourceFingerprint,
            checkpoint.SourceFingerprint,
            StringComparison.Ordinal);
    }

    private static async Task<string> GetCheckpointFingerprintAsync(
        TranslationRequest request,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var legacyFingerprint = BuildLegacyCheckpointFingerprint(request);
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return legacyFingerprint;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var stream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);
            var contentHash = await SHA256.HashDataAsync(stream, cancellationToken);
            return $"{legacyFingerprint}|content-sha256:{Convert.ToHexString(contentHash)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return legacyFingerprint;
        }
    }

    private static string BuildLegacyCheckpointFingerprint(TranslationRequest request)
    {
        return string.Join(
            "|",
            request.SubtitleToTranslate ?? string.Empty,
            request.SourceLanguage,
            request.TargetLanguage,
            request.SourceSubtitleFormat ?? string.Empty);
    }

    private static BatchSubtitleItem CreateValidationItem(SubtitleItem subtitle)
    {
        var sourceLines = subtitle.PlaintextLines.Count > 0
            ? subtitle.PlaintextLines
            : subtitle.Lines;

        return new BatchSubtitleItem
        {
            Position = subtitle.Position,
            Line = string.Join('\n', sourceLines)
        };
    }

    private static List<SubtitleItem> BuildTranslatedSubtitles(
        IReadOnlyList<SubtitleItem> originalSubtitles,
        IReadOnlyDictionary<int, string> checkpointTranslations,
        IReadOnlyDictionary<int, string> edits,
        IReadOnlySet<int> sourceTextPositions,
        bool stripSubtitleFormatting,
        bool preserveAssFormatting)
    {
        var outputSubtitles = new List<SubtitleItem>();
        foreach (var original in originalSubtitles)
        {
            var item = new SubtitleItem
            {
                Position = original.Position,
                StartTime = original.StartTime,
                EndTime = original.EndTime,
                Lines = [.. original.Lines],
                PlaintextLines = [.. original.PlaintextLines],
                TranslatedLines = [.. original.TranslatedLines],
                SsaDialogue = original.SsaDialogue,
                SsaFormat = original.SsaFormat
            };

            if (edits.TryGetValue(original.Position, out var editText))
            {
                item.TranslatedLines = BuildManualEditLines(
                    original,
                    editText,
                    stripSubtitleFormatting,
                    preserveAssFormatting);
            }
            else if (checkpointTranslations.TryGetValue(original.Position, out var translated))
            {
                item.TranslatedLines = ReconstructCachedTranslation(
                    original,
                    translated,
                    stripSubtitleFormatting,
                    preserveAssFormatting);
            }
            else if (sourceTextPositions.Contains(original.Position))
            {
                item.TranslatedLines = [.. original.Lines];
            }

            outputSubtitles.Add(item);
        }

        return outputSubtitles;
    }

    private static List<string> BuildManualEditLines(
        SubtitleItem original,
        string editText,
        bool stripSubtitleFormatting,
        bool preserveAssFormatting)
    {
        if (!preserveAssFormatting || ContainsAssOverrideSyntax(editText))
        {
            return [editText];
        }

        return ReconstructCachedTranslation(
            original,
            editText,
            stripSubtitleFormatting,
            preserveAssFormatting);
    }

    private static bool ContainsAssOverrideSyntax(string text)
    {
        return text.Contains("{\\", StringComparison.Ordinal);
    }

    private static List<string> ReconstructCachedTranslation(
        SubtitleItem original,
        string translated,
        bool stripSubtitleFormatting,
        bool preserveAssFormatting)
    {
        var structure = SubtitleTextStructureFactory.Create(
            original,
            stripSubtitleFormatting,
            preserveAssFormatting);
        var normalizedTranslation = SubtitleTextStructure.NormalizeProviderTranslationText(translated);
        var translatedLines = structure.ApplyProviderTranslation(normalizedTranslation);
        return TranslatedLinesAreEmpty(translatedLines)
            ? structure.ApplyProviderTranslationAsSingleVisibleText(normalizedTranslation)
            : translatedLines;
    }

    private static bool TranslatedLinesAreEmpty(IReadOnlyList<string>? translatedLines)
    {
        return translatedLines == null ||
               translatedLines.Count == 0 ||
               translatedLines.All(string.IsNullOrWhiteSpace);
    }

    private async Task<WrittenSubtitleOutput> WriteSubtitleOutputsAsync(
        TranslationRequest request,
        string sourcePath,
        List<SubtitleItem> translatedSubtitles,
        IReadOnlyList<string> requiredOutputFormats,
        string targetLanguage,
        string subtitleTag,
        string subtitleTagShort,
        bool stripFormatting,
        bool writesPreservedAssOutput,
        bool embedInContainer,
        bool embedWhenPathTooLong,
        string ownershipToken,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? preferredOutputPaths = null)
    {
        var writtenOutputs = new List<StagedSubtitleOutput>();
        var stagedEmbeddedOutputs = new List<StagedEmbeddedSubtitleOutput>();
        var outputCaption = SubtitleLanguageHelper.GetSupplementalOutputCaption(request.SourceSubtitleType);
        var outputBasePath = await ResolveOutputBasePathAsync(request, sourcePath, cancellationToken);

        try
        {
            foreach (var outputFormat in requiredOutputFormats)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var renderSubtitles = RenderOutputSubtitles(
                    translatedSubtitles,
                    outputFormat,
                    writesPreservedAssOutput);
                var outputStripFormatting =
                    stripFormatting &&
                    !(writesPreservedAssOutput && SubtitleOutputModeHelper.IsAssFormat(outputFormat));
                var candidatePaths = (preferredOutputPaths != null &&
                                      preferredOutputPaths.TryGetValue(outputFormat, out var preferredOutputPath)
                    ? new[] { preferredOutputPath }
                    : Enumerable.Empty<string>())
                    .Concat(_subtitleService.CreateFallbackPaths(
                        outputBasePath,
                        targetLanguage,
                        subtitleTag,
                        subtitleTagShort,
                        outputFormat,
                        outputCaption))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(path => !IsSamePath(path, sourcePath))
                    .ToList();

                var anyPathExceedsLimit = candidatePaths.Any(_mkvEmbeddingService.WouldExceedPathLimit);
                var embeddingRequired = embedInContainer ||
                                        (embedWhenPathTooLong && anyPathExceedsLimit);
                if (embeddingRequired)
                {
                    var embeddedOutput = await StageSubtitleForMkvEmbeddingAsync(
                        request,
                        outputBasePath,
                        translatedSubtitles,
                        outputFormat,
                        outputStripFormatting,
                        targetLanguage,
                        cancellationToken);

                    if (embeddedOutput == null)
                    {
                        throw new RequiredEmbeddingException(
                            $"MKV embedding is required for failed translation request {request.Id}, " +
                            $"but no safe MKV output could be prepared for format {outputFormat}. " +
                            "The request remains failed and can be retried after the media path or embedding tools are fixed.");
                    }

                    stagedEmbeddedOutputs.Add(embeddedOutput);
                    continue;
                }

                Exception? lastException = null;
                foreach (var candidatePath in candidatePaths)
                {
                    var stagingPath = CreateStagingPath(candidatePath, ownershipToken);
                    try
                    {
                        EnsureParentDirectory(stagingPath);
                        await _subtitleService.WriteSubtitles(
                            stagingPath,
                            renderSubtitles,
                            outputStripFormatting);
                        writtenOutputs.Add(new StagedSubtitleOutput(outputFormat, candidatePath, stagingPath));
                        lastException = null;
                        break;
                    }
                    catch (Exception ex)
                    {
                        CleanupFile(stagingPath);
                        lastException = ex;
                        _logger.LogWarning(
                            ex,
                            "Failed to write completed failed-translation output {Path} for request {RequestId}. Trying fallback path.",
                            candidatePath,
                            request.Id);
                    }
                }

                if (lastException != null)
                {
                    throw lastException;
                }

                if (!writtenOutputs.Any(output =>
                        string.Equals(output.Format, outputFormat, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException(
                        $"Failed to write subtitle output for format {outputFormat}.");
                }
            }
        }
        catch
        {
            foreach (var output in writtenOutputs)
            {
                CleanupFile(output.StagingPath);
            }

            foreach (var output in stagedEmbeddedOutputs)
            {
                CleanupFile(output.SubtitlePath);
            }

            throw;
        }

        var allOutputs = writtenOutputs
            .Select(output => new OutputReference(output.Format, output.FinalPath, IsEmbedded: false))
            .Concat(stagedEmbeddedOutputs.Select(output =>
                new OutputReference(output.Format, output.OutputPath, IsEmbedded: true)))
            .ToList();
        var primaryPath = allOutputs
            .OrderByDescending(output =>
                string.Equals(
                    SubtitleOutputModeHelper.NormalizeFormat(output.Format),
                    SubtitleOutputModeHelper.NormalizeFormat(request.SourceSubtitleFormat),
                    StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(output => output.IsEmbedded)
            .Select(output => output.Path)
            .First();
        var generatedFormats = SubtitleOutputModeHelper.SerializeFormats(
            allOutputs.Select(output => output.Format));

        _logger.LogInformation(
            "Completed failed translation request {RequestId} and created subtitle outputs: {SubtitleOutputs}",
            request.Id,
            string.Join(", ", allOutputs.Select(output => output.Path)));

        return new WrittenSubtitleOutput(
            primaryPath,
            generatedFormats,
            writtenOutputs,
            stagedEmbeddedOutputs);
    }

    private async Task<StagedEmbeddedSubtitleOutput?> StageSubtitleForMkvEmbeddingAsync(
        TranslationRequest request,
        string mediaPath,
        List<SubtitleItem> renderSubtitles,
        string outputFormat,
        bool outputStripFormatting,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mediaPath) ||
            !string.Equals(Path.GetExtension(mediaPath), ".mkv", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Cannot embed failed translation request {RequestId}: resolved output path is not an MKV ({MediaPath})",
                request.Id,
                mediaPath);
            return null;
        }

        if (!File.Exists(mediaPath))
        {
            _logger.LogWarning(
                "Cannot embed failed translation request {RequestId}: managed media path does not exist ({MediaPath})",
                request.Id,
                mediaPath);
            return null;
        }

        var normalizedLanguage = SubtitleLanguageHelper.NormalizeLanguageCode(
            string.IsNullOrWhiteSpace(targetLanguage) ? request.TargetLanguage : targetLanguage);
        if (string.IsNullOrWhiteSpace(normalizedLanguage))
        {
            normalizedLanguage = request.TargetLanguage;
        }

        var subtitleExtension = SubtitleOutputModeHelper.NormalizeFormat(outputFormat);
        if (string.IsNullOrWhiteSpace(subtitleExtension))
        {
            subtitleExtension = ".srt";
        }

        var subtitlePath = Path.Combine(
            Path.GetTempPath(),
            $"lingarr_failed_embed_{request.Id}_{Guid.NewGuid():N}{subtitleExtension}");

        try
        {
            await _subtitleService.WriteSubtitles(
                subtitlePath,
                renderSubtitles,
                outputStripFormatting);

            return new StagedEmbeddedSubtitleOutput(
                outputFormat,
                $"mkv-embedded:stream0|{mediaPath}",
                mediaPath,
                subtitlePath,
                normalizedLanguage,
                $"{normalizedLanguage} (Lingarr)");
        }
        catch (OperationCanceledException)
        {
            CleanupFile(subtitlePath);
            throw;
        }
        catch (Exception ex)
        {
            CleanupFile(subtitlePath);
            _logger.LogWarning(
                ex,
                "Failed to stage failed-translation subtitle for MKV embedding for request {RequestId}",
                request.Id);
            return null;
        }
    }

    private async Task<EmbeddedPublicationTransaction?> CreateEmbeddedPublicationTransactionAsync(
        WrittenSubtitleOutput writtenOutput,
        string ownershipToken,
        int requestId,
        CancellationToken cancellationToken)
    {
        var mediaPaths = writtenOutput.EmbeddedOutputs
            .GroupBy(output => output.MediaPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key)
            .ToList();
        if (mediaPaths.Count == 0)
        {
            return null;
        }

        var transaction = new EmbeddedPublicationTransaction();
        try
        {
            foreach (var mediaPath in mediaPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(mediaPath))
                {
                    throw new FileNotFoundException(
                        $"Media container for MKV embedding was not found: {mediaPath}",
                        mediaPath);
                }

                try
                {
                    RollbackBackupRecovery.ReconcileEmbeddedBackups(mediaPath, requestId, _logger);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to reconcile stale MKV rollback backups for {MediaPath}",
                        mediaPath);
                }

                var backupPath = CreatePublicationBackupPath(mediaPath, ownershipToken);
                var originalHash = ComputeFileHash(mediaPath);
                try
                {
                    await CopyFileAsync(mediaPath, backupPath, cancellationToken);
                }
                catch
                {
                    CleanupFile(backupPath);
                    throw;
                }

                transaction.Backups.Add(new EmbeddedPublicationBackup(
                    mediaPath,
                    backupPath,
                    originalHash));
                RollbackBackupRecovery.WriteManifest(backupPath, new RollbackBackupManifest
                {
                    RequestId = requestId,
                    TargetPath = mediaPath,
                    OriginalHash = originalHash
                });
            }

            return transaction;
        }
        catch
        {
            RollbackEmbeddedPublication(transaction);
            throw;
        }
    }

    private async Task PublishStagedEmbeddedOutputsAsync(
        WrittenSubtitleOutput writtenOutput,
        TranslationRequest request,
        EmbeddedPublicationTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction == null)
        {
            return;
        }

        foreach (var group in writtenOutput.EmbeddedOutputs
                     .GroupBy(output => output.MediaPath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var publication = transaction.Backups.Single(backup =>
                string.Equals(backup.MediaPath, group.Key, StringComparison.OrdinalIgnoreCase));
            var subtitleInputs = group
                .Select(output => new MkvSubtitleInput(
                    output.SubtitlePath,
                    output.LanguageCode,
                    output.TrackName))
                .ToList();

            foreach (var embeddedOutput in group)
            {
                if (!File.Exists(embeddedOutput.SubtitlePath))
                {
                    throw new FileNotFoundException(
                        $"Staged subtitle for MKV embedding was not found for format {embeddedOutput.Format}.",
                        embeddedOutput.SubtitlePath);
                }
            }

            var result = await _mkvEmbeddingService.EmbedSubtitlesAsync(
                group.Key,
                subtitleInputs,
                cancellationToken);

            if (result == null || !result.Success)
            {
                throw new RequiredEmbeddingException(
                    $"MKV embedding failed for failed translation request {request.Id}, " +
                    $"formats {string.Join(", ", group.Select(output => output.Format))}: " +
                    (result?.Error ?? "the embedding service returned no reason"));
            }

            publication.ExpectedPublishedHash = ComputeFileHash(group.Key);
            RollbackBackupRecovery.UpdateManifest(
                publication.BackupPath,
                manifest => manifest.ExpectedPublishedHash = publication.ExpectedPublishedHash);
            foreach (var embeddedOutput in group)
            {
                CleanupFile(embeddedOutput.SubtitlePath);
            }

            _logger.LogInformation(
                "Successfully embedded {SubtitleCount} failed-translation subtitle(s) in MKV container for request {RequestId}.",
                subtitleInputs.Count,
                request.Id);
        }
    }

    private async Task<string> ResolveOutputBasePathAsync(
        TranslationRequest request,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var embeddedMediaPath = GetEmbeddedMediaPath(request);
        if (!string.IsNullOrWhiteSpace(embeddedMediaPath) &&
            File.Exists(embeddedMediaPath))
        {
            return embeddedMediaPath;
        }

        if (request.WorkloadKind != TranslationWorkloadKind.Library ||
            !request.MediaId.HasValue ||
            !_embeddedSubtitleCacheService.IsManagedCachePath(sourcePath))
        {
            return sourcePath;
        }

        if (request.MediaType == MediaType.Movie)
        {
            var movie = await _dbContext.Movies
                .AsNoTracking()
                .Where(item => item.Id == request.MediaId.Value)
                .Select(item => new { item.Path, item.FileName })
                .FirstOrDefaultAsync(cancellationToken);

            var moviePath = ResolveMediaFilePath(movie?.Path, movie?.FileName);
            if (!string.IsNullOrWhiteSpace(moviePath))
            {
                return moviePath;
            }
        }
        else if (request.MediaType == MediaType.Episode)
        {
            var episode = await _dbContext.Episodes
                .AsNoTracking()
                .Where(item => item.Id == request.MediaId.Value)
                .Select(item => new { item.Path, item.FileName })
                .FirstOrDefaultAsync(cancellationToken);

            var episodePath = ResolveMediaFilePath(episode?.Path, episode?.FileName);
            if (!string.IsNullOrWhiteSpace(episodePath))
            {
                return episodePath;
            }
        }

        _logger.LogWarning(
            "Could not resolve the managed media path for failed translation request {RequestId}; using the extraction cache path.",
            request.Id);
        return sourcePath;
    }

    private static List<string> GetPersistedOutputPaths(TranslationRequest request)
    {
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.TranslatedSubtitle))
        {
            paths.Add(request.TranslatedSubtitle);
        }

        if (!string.IsNullOrWhiteSpace(request.GeneratedSubtitlePaths))
        {
            try
            {
                paths.AddRange(
                    JsonSerializer.Deserialize<List<string>>(request.GeneratedSubtitlePaths) ?? []);
            }
            catch (JsonException)
            {
                paths.AddRange(request.GeneratedSubtitlePaths.Split(
                    '|',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
        }

        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsEmbeddedOutputPath(string path)
    {
        return path.StartsWith("mkv-embedded:", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetEmbeddedMediaPath(TranslationRequest request)
    {
        var marker = GetPersistedOutputPaths(request)
            .FirstOrDefault(IsEmbeddedOutputPath);
        if (string.IsNullOrWhiteSpace(marker))
        {
            return null;
        }

        var separatorIndex = marker.IndexOf('|', StringComparison.Ordinal);
        return separatorIndex >= 0 && separatorIndex < marker.Length - 1
            ? marker[(separatorIndex + 1)..]
            : null;
    }

    private static IReadOnlyDictionary<string, string> BuildPreferredOutputPaths(
        IReadOnlyCollection<string> persistedPaths)
    {
        return persistedPaths
            .Where(path => !IsEmbeddedOutputPath(path))
            .Select(path => new
            {
                Format = SubtitleOutputModeHelper.NormalizeFormat(Path.GetExtension(path)),
                Path = path
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Format))
            .GroupBy(item => item.Format, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Path,
                StringComparer.OrdinalIgnoreCase);
    }

    private static string? ResolveMediaFilePath(string? directoryPath, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        if (Path.IsPathRooted(fileName))
        {
            return ResolveExistingMediaFilePath(
                Path.GetDirectoryName(fileName),
                Path.GetFileName(fileName)) ?? fileName;
        }

        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return null;
        }

        return ResolveExistingMediaFilePath(directoryPath, fileName)
               ?? Path.Combine(directoryPath, fileName);
    }

    private static string? ResolveExistingMediaFilePath(string? directoryPath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return null;
        }

        var candidatePath = Path.Combine(directoryPath, fileName);
        if (File.Exists(candidatePath))
        {
            return candidatePath;
        }

        if (!Directory.Exists(directoryPath))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(directoryPath)
            .FirstOrDefault(path =>
                MediaFileExtensions.Contains(Path.GetExtension(path)) &&
                string.Equals(
                    Path.GetFileNameWithoutExtension(path),
                    Path.GetFileNameWithoutExtension(fileName),
                    StringComparison.OrdinalIgnoreCase));
    }

    private async Task<FailedTranslationCompletionResult> BuildCurrentStateResultAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        await _dbContext.Entry(request).ReloadAsync(cancellationToken);
        return BuildCurrentStateResult(request);
    }

    private static FailedTranslationCompletionResult BuildCurrentStateResult(TranslationRequest request)
    {
        if (request.Status == TranslationStatus.Completed &&
            !string.IsNullOrWhiteSpace(request.TranslatedSubtitle))
        {
            return new FailedTranslationCompletionResult(
                Completed: true,
                AlreadyCompleted: true,
                OutputPath: request.TranslatedSubtitle);
        }

        return new FailedTranslationCompletionResult(
            Completed: false,
            AlreadyCompleted: false,
            OutputPath: null,
            SkippedReason: $"Translation request {request.Id} is no longer failed (current state: {request.Status}).");
    }

    private async Task ReleaseOwnershipAsync(
        int requestId,
        string ownershipToken,
        CancellationToken cancellationToken)
    {
        await _dbContext.TranslationRequests
            .Where(item => item.Id == requestId &&
                           item.Status == TranslationStatus.InProgress &&
                           item.JobId == ownershipToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, TranslationStatus.Failed)
                .SetProperty(item => item.IsActive, (bool?)null)
                .SetProperty(item => item.JobId, (string?)null)
                .SetProperty(item => item.UpdatedAt, DateTime.UtcNow),
            cancellationToken);
    }

    private async Task<bool> ConfirmOwnershipAsync(
        int requestId,
        string ownershipToken,
        CancellationToken cancellationToken)
    {
        var rowsUpdated = await _dbContext.TranslationRequests
            .Where(item => item.Id == requestId &&
                           item.Status == TranslationStatus.InProgress &&
                           item.JobId == ownershipToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.UpdatedAt, DateTime.UtcNow),
                cancellationToken);
        return rowsUpdated > 0;
    }

    private async Task<bool> ConfirmCompletedPublicationOwnershipAsync(
        int requestId,
        string ownershipToken,
        CancellationToken cancellationToken)
    {
        var rowsUpdated = await _dbContext.TranslationRequests
            .Where(item => item.Id == requestId &&
                           item.Status == TranslationStatus.Completed &&
                           item.JobId == ownershipToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.UpdatedAt, DateTime.UtcNow),
                cancellationToken);
        return rowsUpdated > 0;
    }

    private async Task ReleaseCompletedPublicationOwnershipAsync(
        int requestId,
        string ownershipToken,
        CancellationToken cancellationToken)
    {
        await _dbContext.TranslationRequests
            .Where(item => item.Id == requestId &&
                           item.Status == TranslationStatus.Completed &&
                           item.JobId == ownershipToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.JobId, (string?)null)
                .SetProperty(item => item.UpdatedAt, DateTime.UtcNow),
                cancellationToken);
    }

    private static PublishedSubtitleOutputs PublishStagedOutputs(
        WrittenSubtitleOutput writtenOutput,
        string ownershipToken,
        int requestId,
        ILogger logger)
    {
        var publishedOutputs = new List<PublishedSubtitleOutput>();
        try
        {
            foreach (var output in writtenOutput.Outputs)
            {
                RollbackBackupRecovery.ReconcileSubtitleBackups(output.FinalPath, requestId, logger);
                EnsureParentDirectory(output.FinalPath);
                var backupPath = CreatePublicationBackupPath(output.FinalPath, ownershipToken);
                var expectedContentHash = ComputeFileHash(output.StagingPath);
                var hadExistingFile = File.Exists(output.FinalPath);
                var originalHash = hadExistingFile ? ComputeFileHash(output.FinalPath) : null;
                var publishedOutput = new PublishedSubtitleOutput(
                    output.FinalPath,
                    hadExistingFile ? backupPath : null,
                    expectedContentHash);
                publishedOutputs.Add(publishedOutput);

                if (publishedOutput.BackupPath != null)
                {
                    File.Move(output.FinalPath, publishedOutput.BackupPath);
                    // The manifest is written as soon as the backup exists and before
                    // the published file replaces the final path, so every crash window
                    // stays reconcilable.
                    RollbackBackupRecovery.WriteManifest(publishedOutput.BackupPath, new RollbackBackupManifest
                    {
                        RequestId = requestId,
                        TargetPath = output.FinalPath,
                        OriginalHash = originalHash,
                        ExpectedPublishedHash = expectedContentHash
                    });
                }

                File.Move(output.StagingPath, output.FinalPath);
            }

            return new PublishedSubtitleOutputs(publishedOutputs);
        }
        catch
        {
            RollbackPublishedOutputs(new PublishedSubtitleOutputs(publishedOutputs));
            throw;
        }
    }

    private static void RollbackPublishedOutputs(PublishedSubtitleOutputs publishedOutputs)
    {
        foreach (var output in publishedOutputs.Outputs.Reverse())
        {
            try
            {
                if (File.Exists(output.FinalPath) &&
                    (output.BackupPath == null || File.Exists(output.BackupPath)) &&
                    string.Equals(
                        ComputeFileHash(output.FinalPath),
                        output.ExpectedContentHash,
                        StringComparison.Ordinal))
                {
                    File.Delete(output.FinalPath);
                }

                if (output.BackupPath != null &&
                    File.Exists(output.BackupPath) &&
                    !File.Exists(output.FinalPath))
                {
                    File.Move(output.BackupPath, output.FinalPath);
                    RollbackBackupRecovery.DeleteManifest(output.BackupPath);
                }
                // When the final file exists but was not produced by this publication
                // (foreign writer), the backup is deliberately kept: it holds the
                // pre-publication original and must survive for recovery.
            }
            catch
            {
            }
        }
    }

    private static void CleanupPublicationBackups(PublishedSubtitleOutputs publishedOutputs)
    {
        foreach (var output in publishedOutputs.Outputs)
        {
            if (output.BackupPath != null)
            {
                RollbackBackupRecovery.DeleteBackup(output.BackupPath);
            }
        }
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        EnsureParentDirectory(destinationPath);
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    private static void RollbackEmbeddedPublication(
        EmbeddedPublicationTransaction? transaction)
    {
        if (transaction == null)
        {
            return;
        }

        foreach (var backup in transaction.Backups.AsEnumerable().Reverse())
        {
            try
            {
                var shouldRestore = !File.Exists(backup.MediaPath);
                string? currentHash = null;
                if (!shouldRestore)
                {
                    currentHash = ComputeFileHash(backup.MediaPath);
                    if (backup.ExpectedPublishedHash == null)
                    {
                        shouldRestore = !string.Equals(
                            currentHash,
                            backup.OriginalHash,
                            StringComparison.Ordinal);
                    }
                    else
                    {
                        shouldRestore = string.Equals(
                            currentHash,
                            backup.ExpectedPublishedHash,
                            StringComparison.Ordinal);
                    }
                }

                if (shouldRestore && File.Exists(backup.BackupPath))
                {
                    if (File.Exists(backup.MediaPath))
                    {
                        File.Delete(backup.MediaPath);
                    }

                    File.Move(backup.BackupPath, backup.MediaPath);
                    RollbackBackupRecovery.DeleteManifest(backup.BackupPath);
                }
                else if (currentHash != null &&
                         string.Equals(currentHash, backup.OriginalHash, StringComparison.Ordinal))
                {
                    // Media is untouched: the backup is a redundant copy.
                    RollbackBackupRecovery.DeleteBackup(backup.BackupPath);
                }
                // When the media file exists but was not produced by this publication
                // (foreign writer), the backup is deliberately kept: it holds the
                // pre-publication original and must survive for recovery.
            }
            catch
            {
            }
        }
    }

    private static void CleanupEmbeddedPublicationBackups(
        EmbeddedPublicationTransaction? transaction)
    {
        if (transaction == null)
        {
            return;
        }

        foreach (var backup in transaction.Backups)
        {
            RollbackBackupRecovery.DeleteBackup(backup.BackupPath);
        }
    }

    private static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void CleanupStagedOutputs(WrittenSubtitleOutput writtenOutput)
    {
        foreach (var output in writtenOutput.Outputs)
        {
            CleanupFile(output.StagingPath);
        }

        foreach (var output in writtenOutput.EmbeddedOutputs)
        {
            CleanupFile(output.SubtitlePath);
        }
    }

    private static IEnumerable<string> GetPublicationPaths(WrittenSubtitleOutput writtenOutput)
    {
        return writtenOutput.Outputs
            .Select(output => output.FinalPath)
            .Concat(writtenOutput.EmbeddedOutputs.Select(output => output.MediaPath));
    }

    private async Task RecordCompletionFailureAsync(int requestId, Exception exception)
    {
        try
        {
            _dbContext.TranslationRequestLogs.Add(new TranslationRequestLog
            {
                TranslationRequestId = requestId,
                Level = "Error",
                Message = "Failed to complete the accepted translation request.",
                Details = exception.Message
            });
            await _dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception logException)
        {
            _logger.LogWarning(
                logException,
                "Failed to persist the failed-panel completion error for request {RequestId}",
                requestId);
        }
    }

    private static void CleanupFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static string CreateStagingPath(string finalPath, string ownershipToken)
    {
        var extension = Path.GetExtension(finalPath);
        var pathWithoutExtension = finalPath[..^extension.Length];
        return $"{pathWithoutExtension}.{ownershipToken}.tmp{extension}";
    }

    private static string CreatePublicationBackupPath(string finalPath, string ownershipToken)
    {
        var extension = Path.GetExtension(finalPath);
        var pathWithoutExtension = finalPath[..^extension.Length];
        return $"{pathWithoutExtension}.{ownershipToken}.bak{extension}";
    }

    private static List<SubtitleItem> RenderOutputSubtitles(
        List<SubtitleItem> translatedSubtitles,
        string outputFormat,
        bool writesPreservedAssOutput)
    {
        if (writesPreservedAssOutput && SubtitleOutputModeHelper.IsAssFormat(outputFormat))
        {
            return translatedSubtitles;
        }

        var renderedSubtitles = new List<SubtitleItem>(translatedSubtitles.Count);
        foreach (var subtitle in translatedSubtitles)
        {
            var translatedText = subtitle.TranslatedLines.Count > 0
                ? string.Join("\\N", subtitle.TranslatedLines)
                : string.Join("\\N", subtitle.Lines);
            var plainTextLines = PlainTextSubtitleOutputRenderer.ConvertToPlainTextLines(translatedText);

            if (PlainTextSubtitleOutputRenderer.ShouldSkipSubtitle(plainTextLines))
            {
                continue;
            }

            renderedSubtitles.Add(new SubtitleItem
            {
                Position = subtitle.Position,
                StartTime = subtitle.StartTime,
                EndTime = subtitle.EndTime,
                Lines = [.. subtitle.Lines],
                PlaintextLines = [.. subtitle.PlaintextLines],
                TranslatedLines = plainTextLines,
                SsaDialogue = subtitle.SsaDialogue,
                SsaFormat = subtitle.SsaFormat
            });
        }

        return renderedSubtitles;
    }

    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static bool IsSamePath(string path, string? otherPath)
    {
        return !string.IsNullOrWhiteSpace(otherPath) &&
               string.Equals(
                   Path.GetFullPath(path),
                   Path.GetFullPath(otherPath),
                   StringComparison.OrdinalIgnoreCase);
    }

    private sealed record WrittenSubtitleOutput(
        string PrimaryPath,
        string GeneratedFormats,
        IReadOnlyCollection<StagedSubtitleOutput> Outputs,
        IReadOnlyCollection<StagedEmbeddedSubtitleOutput> EmbeddedOutputs)
    {
        public IReadOnlyCollection<string> FinalPaths =>
            Outputs.Select(output => output.FinalPath)
                .Concat(EmbeddedOutputs.Select(output => output.OutputPath))
                .ToList();
    }

    private sealed record StagedSubtitleOutput(
        string Format,
        string FinalPath,
        string StagingPath);

    private sealed record StagedEmbeddedSubtitleOutput(
        string Format,
        string OutputPath,
        string MediaPath,
        string SubtitlePath,
        string LanguageCode,
        string TrackName);

    private sealed record OutputReference(
        string Format,
        string Path,
        bool IsEmbedded);

    private sealed class RequiredEmbeddingException : InvalidOperationException
    {
        public RequiredEmbeddingException(string message)
            : base(message)
        {
        }
    }

    private sealed record PublishedSubtitleOutputs(
        IReadOnlyCollection<PublishedSubtitleOutput> Outputs);

    private sealed record PublishedSubtitleOutput(
        string FinalPath,
        string? BackupPath,
        string ExpectedContentHash);

    private sealed class EmbeddedPublicationTransaction
    {
        public List<EmbeddedPublicationBackup> Backups { get; } = [];
    }

    private sealed class EmbeddedPublicationBackup
    {
        public EmbeddedPublicationBackup(
            string mediaPath,
            string backupPath,
            string originalHash)
        {
            MediaPath = mediaPath;
            BackupPath = backupPath;
            OriginalHash = originalHash;
        }

        public string MediaPath { get; }

        public string BackupPath { get; }

        public string OriginalHash { get; }

        public string? ExpectedPublishedHash { get; set; }
    }

    private async Task NotifyCompletionAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _translationRequestService.UpdateActiveCount();
            await _progressService.Emit(request, 100);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to emit completion updates for request {RequestId}",
                request.Id);
        }

        if (request.WorkloadKind != TranslationWorkloadKind.Library || !request.MediaId.HasValue)
        {
            return;
        }

        try
        {
            if (request.MediaType == MediaType.Movie)
            {
                var movie = await _dbContext.Movies.FindAsync([request.MediaId.Value], cancellationToken);
                if (movie != null)
                {
                    await _mediaStateService.UpdateStateAsync(movie, MediaType.Movie);
                }

                return;
            }

            var episode = await _dbContext.Episodes
                .Include(item => item.Season)
                .ThenInclude(item => item.Show)
                .FirstOrDefaultAsync(item => item.Id == request.MediaId.Value, cancellationToken);
            if (episode != null)
            {
                await _mediaStateService.UpdateStateAsync(episode, MediaType.Episode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to update media state after completing request {RequestId}",
                request.Id);
        }
    }
}

internal sealed record FailedTranslationCheckpointHydration(
    IReadOnlyDictionary<int, string> Translations,
    IReadOnlySet<int> SourceTextPositions,
    IReadOnlySet<int> RequiredMissingPositions,
    IReadOnlySet<int> HydratedDuplicatePositions);

internal static class FailedTranslationCheckpointRules
{
    public static async Task<FailedTranslationCheckpointHydration> HydrateAsync(
        TranslationRequest request,
        string sourcePath,
        TranslationCheckpoint? checkpoint,
        IReadOnlyList<SubtitleItem> originalSubtitles,
        ITranslationCheckpointService checkpointService,
        CancellationToken cancellationToken,
        string? ownershipToken = null)
    {
        var preserveAssFormatting = SubtitleOutputModeHelper.IsAssFormat(
            SubtitleOutputModeHelper.NormalizeFormat(Path.GetExtension(sourcePath)));
        var plan = SubtitleTranslationNodePlanner.Plan(
            originalSubtitles,
            stripSubtitleFormatting: false,
            preserveAssFormatting);
        var nodesByPosition = plan.Nodes.ToDictionary(node => node.Subtitle.Position);
        var currentPositions = nodesByPosition.Keys.ToHashSet();
        var sourceTextPositions = plan.Nodes
            .Where(node => node.CanPreserveSourceWhenProviderMissing)
            .Select(node => node.Subtitle.Position)
            .ToHashSet();

        if (checkpoint == null || checkpoint.TranslationRequestId != request.Id)
        {
            var emptyCheckpointRequiredMissingPositions = plan.Nodes
                .Where(node => node.IsTranslatable)
                .Select(node => node.Subtitle.Position)
                .ToHashSet();
            return new FailedTranslationCheckpointHydration(
                new Dictionary<int, string>(),
                sourceTextPositions,
                emptyCheckpointRequiredMissingPositions,
                new HashSet<int>());
        }

        checkpoint.Translations ??= new Dictionary<int, string>();
        checkpoint.SourcePreservedPositions ??= [];
        var currentFingerprints = await GetCurrentSourceFingerprintsAsync(
            request,
            sourcePath,
            cancellationToken);
        var checkpointChanged = false;
        if (!string.Equals(checkpoint.OwnershipToken, ownershipToken, StringComparison.Ordinal))
        {
            checkpoint.OwnershipToken = ownershipToken;
            checkpointChanged = true;
        }
        var checkpointMatches = string.Equals(
                checkpoint.SourceFingerprint,
                currentFingerprints.Canonical,
                StringComparison.Ordinal) ||
            (currentFingerprints.LegacyAllowed &&
             string.Equals(
                 checkpoint.SourceFingerprint,
                 currentFingerprints.Legacy,
                 StringComparison.Ordinal));
        if (!checkpointMatches)
        {
            checkpoint.Translations.Clear();
            checkpoint.SourcePreservedPositions.Clear();
            checkpoint.SourceFingerprint = currentFingerprints.LegacyAllowed
                ? currentFingerprints.Legacy
                : currentFingerprints.Canonical;
            checkpointChanged = true;
        }

        foreach (var position in checkpoint.Translations.Keys
                     .Where(position => !currentPositions.Contains(position) ||
                                        !nodesByPosition.TryGetValue(position, out var node) ||
                                        !node.IsTranslatable)
                     .ToList())
        {
            checkpoint.Translations.Remove(position);
            checkpointChanged = true;
        }

        var deduplication = ProviderTextDeduper.Deduplicate(
            plan.Nodes
                .Where(node => node.IsTranslatable)
                .Select(node => new ProviderTextItem(
                    node.Subtitle.Position,
                    node.ProviderText,
                    node.SemanticKind))
                .ToList());
        var hydratedTranslations = new Dictionary<int, string>();
        var validSourcePreservedPositions = new HashSet<int>();
        var hydratedDuplicatePositions = new HashSet<int>();

        foreach (var representative in deduplication.Representatives)
        {
            var representativePosition = representative.Position;
            var memberPositions = deduplication.GetMemberPositions(representativePosition);
            var candidates = new List<CheckpointCandidate>();

            foreach (var memberPosition in memberPositions.OrderBy(position => position))
            {
                if (!checkpoint.Translations.TryGetValue(memberPosition, out var translation) ||
                    !nodesByPosition.TryGetValue(memberPosition, out var memberNode))
                {
                    continue;
                }

                var normalizedTranslation = SubtitleTextStructure.NormalizeProviderTranslationText(translation);
                var markerIsValid = checkpoint.SourcePreservedPositions.Contains(memberPosition) &&
                    IsValidSourcePreservedTranslation(memberNode, normalizedTranslation);
                if (checkpoint.SourcePreservedPositions.Contains(memberPosition) && !markerIsValid)
                {
                    checkpoint.SourcePreservedPositions.Remove(memberPosition);
                    checkpointChanged = true;
                }

                var safeSourceEcho = SubtitleSemanticClassifier.IsSafeSourceEcho(
                    memberNode.Subtitle,
                    memberNode.ProviderText,
                    normalizedTranslation,
                    memberNode.Subtitle.SsaDialogue?.Style);
                var isValid = !string.IsNullOrWhiteSpace(normalizedTranslation) &&
                    ((safeSourceEcho && memberNode.CanPreserveSourceWhenProviderMissing) ||
                     ProviderTranslationValidation.Analyze(
                             [new BatchSubtitleItem
                             {
                                 Position = memberPosition,
                                 Line = memberNode.ProviderText
                             }],
                             new Dictionary<int, string>
                             {
                                 [memberPosition] = normalizedTranslation
                             },
                             request.SourceLanguage,
                             request.TargetLanguage)
                         .InvalidPositions.Count == 0);
                candidates.Add(new CheckpointCandidate(
                    memberPosition,
                    normalizedTranslation,
                    isValid,
                    markerIsValid,
                    safeSourceEcho && memberNode.CanPreserveSourceWhenProviderMissing));
            }

            var chosenCandidate = candidates
                .Where(candidate => candidate.IsValid)
                .OrderByDescending(candidate => candidate.IsSourcePreserved)
                .ThenBy(candidate => candidate.Position == representativePosition ? 0 : 1)
                .ThenBy(candidate => candidate.Position)
                .FirstOrDefault();

            if (chosenCandidate == null)
            {
                foreach (var memberPosition in memberPositions)
                {
                    if (checkpoint.Translations.Remove(memberPosition))
                    {
                        checkpointChanged = true;
                    }

                    if (checkpoint.SourcePreservedPositions.Remove(memberPosition))
                    {
                        checkpointChanged = true;
                    }
                }

                continue;
            }

            foreach (var memberPosition in memberPositions)
            {
                hydratedTranslations[memberPosition] = chosenCandidate.Translation;
                if (!chosenCandidate.IsSafeSourceEcho)
                {
                    sourceTextPositions.Remove(memberPosition);
                }
            }
            if (memberPositions.Count > 1)
            {
                hydratedDuplicatePositions.UnionWith(memberPositions);
            }

            if (chosenCandidate.IsSafeSourceEcho)
            {
                validSourcePreservedPositions.UnionWith(memberPositions);
            }

            if (!checkpoint.Translations.TryGetValue(representativePosition, out var canonicalTranslation) ||
                !string.Equals(canonicalTranslation, chosenCandidate.Translation, StringComparison.Ordinal))
            {
                checkpoint.Translations[representativePosition] = chosenCandidate.Translation;
                checkpointChanged = true;
            }

            foreach (var memberPosition in memberPositions.Where(position => position != representativePosition))
            {
                if (checkpoint.Translations.Remove(memberPosition))
                {
                    checkpointChanged = true;
                }

                if (checkpoint.SourcePreservedPositions.Remove(memberPosition))
                {
                    checkpointChanged = true;
                }
            }

            if (chosenCandidate.IsSourcePreserved)
            {
                if (checkpoint.SourcePreservedPositions.Add(representativePosition))
                {
                    checkpointChanged = true;
                }
            }
            else if (checkpoint.SourcePreservedPositions.Remove(representativePosition))
            {
                checkpointChanged = true;
            }
        }

        foreach (var position in checkpoint.SourcePreservedPositions.ToList())
        {
            if (!nodesByPosition.TryGetValue(position, out var node) ||
                !node.IsTranslatable ||
                !checkpoint.Translations.TryGetValue(position, out var translation) ||
                !IsValidSourcePreservedTranslation(node, translation))
            {
                checkpoint.SourcePreservedPositions.Remove(position);
                checkpointChanged = true;
            }
        }

        sourceTextPositions.UnionWith(validSourcePreservedPositions);
        var requiredMissingPositions = plan.Nodes
            .Where(node => node.IsTranslatable)
            .Select(node => node.Subtitle.Position)
            .Where(position => !hydratedTranslations.ContainsKey(position) &&
                               !sourceTextPositions.Contains(position))
            .ToHashSet();
        if (checkpointChanged && !string.IsNullOrWhiteSpace(ownershipToken))
        {
            checkpoint.UpdatedAtUtc = DateTime.UtcNow;
            await checkpointService.SaveCheckpointAsync(
                checkpoint,
                cancellationToken,
                ownershipToken);
        }

        return new FailedTranslationCheckpointHydration(
            hydratedTranslations,
            sourceTextPositions,
            requiredMissingPositions,
            hydratedDuplicatePositions);
    }

    public static List<string> ReconstructCachedTranslation(
        SubtitleItem original,
        string translated,
        bool stripSubtitleFormatting,
        bool preserveAssFormatting)
    {
        var structure = SubtitleTextStructureFactory.Create(
            original,
            stripSubtitleFormatting,
            preserveAssFormatting);
        var normalizedTranslation = SubtitleTextStructure.NormalizeProviderTranslationText(translated);
        var translatedLines = structure.ApplyProviderTranslation(normalizedTranslation);
        return translatedLines == null ||
               translatedLines.Count == 0 ||
               translatedLines.All(string.IsNullOrWhiteSpace)
            ? structure.ApplyProviderTranslationAsSingleVisibleText(normalizedTranslation)
            : translatedLines;
    }

    public static List<int> GetMissingPositions(
        IReadOnlyList<SubtitleItem> originalSubtitles,
        IReadOnlyCollection<int> reportedPositions,
        FailedTranslationCheckpointHydration hydration)
    {
        var currentPositions = originalSubtitles
            .Select(subtitle => subtitle.Position)
            .Distinct()
            .ToHashSet();
        if (reportedPositions.Count == 0)
        {
            return hydration.RequiredMissingPositions.Order().ToList();
        }

        return reportedPositions
            .Where(currentPositions.Contains)
            .Where(position => !hydration.SourceTextPositions.Contains(position) &&
                               (!hydration.Translations.ContainsKey(position) ||
                                !hydration.HydratedDuplicatePositions.Contains(position)))
            .Order()
            .ToList();
    }

    private static bool IsValidSourcePreservedTranslation(
        SubtitleTranslationNode node,
        string translation)
    {
        return node.CanPreserveSourceWhenProviderMissing &&
               SubtitleSemanticClassifier.IsSafeSourceEcho(
                   node.Subtitle,
                   node.ProviderText,
                   translation,
                   node.Subtitle.SsaDialogue?.Style);
    }

    private static async Task<CurrentSourceFingerprints> GetCurrentSourceFingerprintsAsync(
        TranslationRequest request,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var legacyIdentity = string.Join(
            "|",
            request.SubtitleToTranslate ?? string.Empty,
            request.SourceLanguage,
            request.TargetLanguage,
            request.SourceSubtitleFormat ?? string.Empty);
        var legacyAllowed = IsLegacyCompatibilityRequest(request, legacyIdentity);
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return new CurrentSourceFingerprints(
                TranslationCheckpointService.GetFallbackCheckpointFingerprint(request),
                legacyIdentity,
                legacyAllowed);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);
            var contentHash = await SHA256.HashDataAsync(stream, cancellationToken);
            var hash = Convert.ToHexString(contentHash);
            return new CurrentSourceFingerprints(
                TranslationCheckpointService.BuildCheckpointFingerprint(request, hash),
                $"{legacyIdentity}|content-sha256:{hash}",
                legacyAllowed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new CurrentSourceFingerprints(
                TranslationCheckpointService.GetFallbackCheckpointFingerprint(request),
                legacyIdentity,
                legacyAllowed);
        }
    }

    private static bool IsLegacyCompatibilityRequest(
        TranslationRequest request,
        string legacyIdentity)
    {
        return request.SourceSnapshotVersion == 1 &&
               string.IsNullOrWhiteSpace(request.SourceSnapshotType) &&
               string.IsNullOrWhiteSpace(request.SourceSnapshotIdentity) &&
               !request.SourceSnapshotFileSizeBytes.HasValue &&
               !request.SourceSnapshotLastWriteUtc.HasValue &&
               !request.SourceSnapshotStreamIndex.HasValue &&
               (string.IsNullOrWhiteSpace(request.SourceSnapshotFingerprint) ||
                request.SourceSnapshotFingerprint.StartsWith(
                    legacyIdentity + "|content-sha256:",
                    StringComparison.Ordinal));
    }

    private sealed record CheckpointCandidate(
        int Position,
        string Translation,
        bool IsValid,
        bool IsSourcePreserved,
        bool IsSafeSourceEcho);

    private sealed record CurrentSourceFingerprints(
        string Canonical,
        string Legacy,
        bool LegacyAllowed);
}
