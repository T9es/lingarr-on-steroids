using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models.Subtitle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lingarr.Server.Services.Subtitle;

public class SourceSubtitleResolver : ISourceSubtitleResolver
{
    private static readonly string[] MediaExtensions =
        [".mkv", ".mp4", ".avi", ".m4v", ".webm", ".mov", ".wmv"];

    private readonly LingarrDbContext _dbContext;
    private readonly ISubtitleService _subtitleService;
    private readonly ISubtitleExtractionService _subtitleExtractionService;
    private readonly ISourceSubtitleSnapshotService _sourceSubtitleSnapshotService;
    private readonly IEmbeddedSubtitleCacheService _embeddedSubtitleCacheService;
    private readonly ISubtitleOcrService _subtitleOcrService;
    private readonly ISubtitleSourceSelectionService _subtitleSourceSelectionService;
    private readonly IMediaDurationProvider? _mediaDurationProvider;
    private readonly ISubtitleLanguageContradictionDetector? _languageContradictionDetector;
    private readonly ILogger<SourceSubtitleResolver> _logger;

    public SourceSubtitleResolver(
        LingarrDbContext dbContext,
        ISubtitleService subtitleService,
        ISubtitleExtractionService subtitleExtractionService,
        ISourceSubtitleSnapshotService sourceSubtitleSnapshotService,
        IEmbeddedSubtitleCacheService embeddedSubtitleCacheService,
        ISubtitleOcrService subtitleOcrService,
        ILogger<SourceSubtitleResolver> logger,
        ISubtitleSourceSelectionService? subtitleSourceSelectionService = null,
        IMediaDurationProvider? mediaDurationProvider = null,
        ISubtitleLanguageContradictionDetector? languageContradictionDetector = null)
    {
        _dbContext = dbContext;
        _subtitleService = subtitleService;
        _subtitleExtractionService = subtitleExtractionService;
        _sourceSubtitleSnapshotService = sourceSubtitleSnapshotService;
        _embeddedSubtitleCacheService = embeddedSubtitleCacheService;
        _subtitleOcrService = subtitleOcrService;
        _subtitleSourceSelectionService = subtitleSourceSelectionService ??
            new SubtitleSourceSelectionService(
                subtitleService,
                NullLogger<SubtitleSourceSelectionService>.Instance);
        _mediaDurationProvider = mediaDurationProvider;
        _languageContradictionDetector = languageContradictionDetector;
        _logger = logger;
    }

    public async Task<PreparedTranslationSource?> PrepareTranslationSourceAsync(
        TranslationRequest request,
        TranslationSourcePreparationOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (options.IsSupplementalRequest && !options.SupplementalTranslationEnabled)
        {
            throw new InvalidOperationException(
                "Supplemental subtitle translation is disabled. Enable supplemental translation or create a new primary request.");
        }

        var currentPath = request.SubtitleToTranslate;
        if (!request.MediaId.HasValue)
        {
            return await PrepareExternalSourceAsync(
                currentPath,
                options.HasManualStreamSelection,
                cancellationToken);
        }

        var mediaFile = await ResolveCurrentMediaFileAsync(request, cancellationToken);
        var mediaFileNameChanged = mediaFile.FileNameChanged;
        if (string.IsNullOrWhiteSpace(mediaFile.Path) && !options.HasManualStreamSelection)
        {
            if (string.Equals(
                    request.SourceSnapshotType,
                    SourceSubtitleSnapshot.ExternalType,
                    StringComparison.Ordinal))
            {
                return await PrepareExternalSourceAsync(currentPath, false, cancellationToken);
            }

            if (options.IsSupplementalRequest)
            {
                throw new InvalidOperationException(
                    "The embedded subtitle source for this supplemental request is unavailable. Create a new supplemental request after restoring the media file.");
            }

            return null;
        }

        if (!string.IsNullOrWhiteSpace(currentPath) &&
            !File.Exists(currentPath) &&
            _embeddedSubtitleCacheService.IsManagedCachePath(currentPath))
        {
            var requiredStreamIndex = options.HasManualStreamSelection
                ? request.SourceSnapshotStreamIndex
                : null;
            if (!options.HasManualStreamSelection || requiredStreamIndex.HasValue)
            {
                await TryRestoreMissingManagedOcrAsync(
                    request,
                    currentPath,
                    cancellationToken,
                    requiredStreamIndex);
            }

            currentPath = request.SubtitleToTranslate;
        }

        var embeddedSubtitles = await LoadCurrentEmbeddedSubtitlesAsync(
            request,
            cancellationToken,
            invalidateStaleManagedArtifacts: false,
            forceRefresh: mediaFileNameChanged);
        var sameLanguageSubtitles = embeddedSubtitles
            .Where(subtitle => SubtitleLanguageHelper.LanguageMatchesEffective(subtitle, request.SourceLanguage))
            .OrderBy(subtitle => subtitle.StreamIndex)
            .ToList();

        // Failure-driven language contradiction shortcut. A stream tagged jpn that actually
        // contains English (or any other contradiction) is never selected by the language
        // filter above. Before failing with "no suitable source", check the readable embedded
        // streams and adopt a high confidence detected language, either by picking the stream
        // whose detected language matches the request or by switching a request that was
        // created for the contradicting tag. Results are cached on the subtitle row.
        if (sameLanguageSubtitles.Count == 0 &&
            _languageContradictionDetector != null &&
            !string.IsNullOrWhiteSpace(mediaFile.Path) &&
            embeddedSubtitles.Any(subtitle => subtitle.IsReadableSource()))
        {
            var adoptedSubtitle = await _languageContradictionDetector.TryAdoptDetectedSourceLanguageAsync(
                request,
                embeddedSubtitles,
                mediaFile.Path,
                cancellationToken);
            if (adoptedSubtitle != null)
            {
                sameLanguageSubtitles = embeddedSubtitles
                    .Where(subtitle =>
                        SubtitleLanguageHelper.LanguageMatchesEffective(subtitle, request.SourceLanguage))
                    .OrderBy(subtitle => subtitle.StreamIndex)
                    .ToList();
                if (sameLanguageSubtitles.Count == 0)
                {
                    _logger.LogWarning(
                        "Detected language contradiction on stream {StreamIndex} for request {RequestId} but no candidate matches '{SourceLanguage}'",
                        adoptedSubtitle.StreamIndex,
                        request.Id,
                        request.SourceLanguage);
                }
            }
        }

        if (options.IsSupplementalRequest &&
            string.Equals(request.SourceSnapshotType, SourceSubtitleSnapshot.ExternalType, StringComparison.Ordinal))
        {
            return await PrepareExternalSourceAsync(currentPath, false, cancellationToken);
        }

        if (options.IsSupplementalRequest)
        {
            if (!string.Equals(
                    request.SourceSnapshotType,
                    SourceSubtitleSnapshot.EmbeddedType,
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(request.SourceSnapshotIdentity))
            {
                throw new InvalidOperationException(
                    "This supplemental request has no verifiable embedded source identity. Create a new supplemental request before translating it.");
            }

            var pinnedSubtitle = sameLanguageSubtitles.FirstOrDefault(subtitle =>
                string.Equals(
                    _sourceSubtitleSnapshotService.CreateEmbeddedSnapshot(subtitle, request.SourceLanguage).Identity,
                    request.SourceSnapshotIdentity,
                    StringComparison.Ordinal));
            if (pinnedSubtitle == null)
            {
                throw new InvalidOperationException(
                    "The embedded subtitle source for this supplemental request is unavailable or changed. Create a new supplemental request before translating it.");
            }

            if (options.HasManualStreamSelection &&
                request.SourceSnapshotStreamIndex != pinnedSubtitle.StreamIndex)
            {
                throw new InvalidOperationException(
                    $"Manual stream {request.SourceSnapshotStreamIndex?.ToString() ?? "unknown"} does not match this supplemental request's pinned stream {pinnedSubtitle.StreamIndex}. Create a new supplemental request to use another stream.");
            }

            sameLanguageSubtitles = [pinnedSubtitle];
        }

        if (!options.HasManualStreamSelection &&
            string.Equals(request.SourceSnapshotType, SourceSubtitleSnapshot.ExternalType, StringComparison.Ordinal))
        {
            return await PrepareExternalSourceAsync(currentPath, false, cancellationToken);
        }

        if (!options.HasManualStreamSelection &&
            !string.IsNullOrWhiteSpace(currentPath) &&
            File.Exists(currentPath) &&
            !_embeddedSubtitleCacheService.IsManagedCachePath(currentPath) &&
            !sameLanguageSubtitles.Any(subtitle =>
                PathsEqual(subtitle.ExtractedPath, currentPath) ||
                PathsEqual(subtitle.OcrExtractedPath, currentPath)))
        {
            return await PrepareExternalSourceAsync(currentPath, false, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(mediaFile.Path))
        {
            if (options.IsSupplementalRequest)
            {
                throw new InvalidOperationException(
                    "The embedded subtitle source for this supplemental request is unavailable. Create a new supplemental request after restoring the media file.");
            }

            return null;
        }

        // Probe the media duration once per embedded-source preparation so the dialogue
        // entry floor scales with runtime. A null duration keeps the legacy fixed threshold
        // everywhere below.
        var contentDurationSeconds = await ResolveContentDurationSecondsAsync(
            mediaFile.Path,
            cancellationToken);
        var minimumDialogueEntries =
            SubtitleExtractionService.GetMinimumDialogueEntries(contentDurationSeconds);

        // Failure-driven shortcut for the shape where the request language does match a track
        // but every matching track is sparse (signs/typesetting only) while a full dialogue
        // track is mislabeled with another language tag. The Initial D case is exactly this:
        // request en, a sparse en-tagged signs track, and a full English track tagged jpn.
        // Detection runs before the sparse rejections below so the mislabeled full track can
        // be adopted instead of failing with "no suitable source".
        if (!options.HasManualStreamSelection &&
            !options.IsSupplementalRequest &&
            _languageContradictionDetector != null &&
            sameLanguageSubtitles.Count > 0 &&
            AllMatchedCandidatesKnownSparse(sameLanguageSubtitles, minimumDialogueEntries))
        {
            var adoptedSubtitle = await _languageContradictionDetector.TryAdoptDetectedSourceLanguageAsync(
                request,
                embeddedSubtitles,
                mediaFile.Path,
                cancellationToken);
            if (adoptedSubtitle != null)
            {
                sameLanguageSubtitles = embeddedSubtitles
                    .Where(subtitle =>
                        SubtitleLanguageHelper.LanguageMatchesEffective(subtitle, request.SourceLanguage))
                    .OrderBy(subtitle => subtitle.StreamIndex)
                    .ToList();
                _logger.LogInformation(
                    "Sparse matched source for request {RequestId} triggered language contradiction check; " +
                    "adopted stream {StreamIndex} (requested '{SourceLanguage}')",
                    request.Id,
                    adoptedSubtitle.StreamIndex,
                    request.SourceLanguage);
            }
        }

        if (options.HasManualStreamSelection)
        {
            var manualSubtitle = sameLanguageSubtitles.FirstOrDefault(subtitle =>
                subtitle.StreamIndex == request.SourceSnapshotStreamIndex && subtitle.IsReadableSource());
            if (manualSubtitle == null)
            {
                throw new InvalidOperationException(
                    $"Manual subtitle stream {request.SourceSnapshotStreamIndex?.ToString() ?? "unknown"} is unavailable or does not match source language '{request.SourceLanguage}'.");
            }

            var manualPath = GetValidatedCandidatePath(
                manualSubtitle,
                request,
                mediaFile.Path,
                cancellationToken);
            var newlyExtractedPath = (string?)null;
            var originalManualPath = manualSubtitle.ExtractedPath;
            var originalManualExtracted = manualSubtitle.IsExtracted;
            var manualSelectionSucceeded = false;
            try
            {
                if (manualPath == null)
                {
                    var extraction = await ExtractCandidateForPreparationAsync(
                        manualSubtitle,
                        request,
                        mediaFile.Path,
                        cancellationToken);
                    manualPath = extraction?.Path;
                    if (extraction?.IsNew == true)
                    {
                        newlyExtractedPath = extraction.Path;
                    }
                }

                if (manualPath == null)
                {
                    throw new InvalidOperationException(
                        $"Manual subtitle stream {manualSubtitle.StreamIndex} could not be extracted from the current media file.");
                }

                var manualCount = await CountEntriesForCandidate(
                    manualPath,
                    manualSubtitle.StreamIndex,
                    cancellationToken);
                if (!manualCount.HasValue)
                {
                    throw new InvalidOperationException(
                        $"Manual subtitle stream {manualSubtitle.StreamIndex} could not be parsed because its subtitle data is malformed.");
                }

                if (manualCount.Value <= 0)
                {
                    throw new InvalidOperationException(
                        $"Manual subtitle stream {manualSubtitle.StreamIndex} was rejected because its extracted subtitle could not be parsed or contains no entries.");
                }

                if (IsSupplementalRequestAllowed(options) &&
                    !SubtitleLanguageHelper.IsSupplementalSubtitleType(
                        SubtitleLanguageHelper.DetermineSubtitleType(manualSubtitle, manualCount.Value)))
                {
                    throw new InvalidOperationException(
                        $"Manual stream {manualSubtitle.StreamIndex} is full-dialogue and cannot be translated by this supplemental request. Create a new primary request to translate it.");
                }

                if (manualSubtitle.IsTextBased)
                {
                    manualSubtitle.ExtractedPath = manualPath;
                    manualSubtitle.IsExtracted = true;
                }

                if (!await IsAcceptablePrimaryCandidateAsync(
                        manualSubtitle,
                        request,
                        options,
                        manualCount.Value,
                        cancellationToken,
                        contentDurationSeconds))
                {
                    var assessment = await _subtitleSourceSelectionService.SelectPrimaryAsync(
                        [manualSubtitle],
                        [request.SourceLanguage],
                        options.AllowCaptionFallback,
                        cancellationToken: cancellationToken,
                        contentDurationSeconds: contentDurationSeconds);
                    throw CreateNoSuitableSourceException(
                        [new PreparedCandidate(manualSubtitle, manualPath, manualCount.Value, null)],
                        request.SourceLanguage,
                        assessment.Assessments);
                }

                manualSelectionSucceeded = true;
                _embeddedSubtitleCacheService.Touch(manualPath);
                return new PreparedTranslationSource(
                    manualPath,
                    manualSubtitle,
                    manualCount.Value,
                    IsManualSelection: true);
            }
            finally
            {
                if (!manualSelectionSucceeded)
                {
                    manualSubtitle.ExtractedPath = originalManualPath;
                    manualSubtitle.IsExtracted = originalManualExtracted;
                    if (newlyExtractedPath != null)
                    {
                        TryDeletePreparationFile(newlyExtractedPath);
                    }
                }
            }
        }

        if (sameLanguageSubtitles.Count == 0)
        {
            if (embeddedSubtitles.Count > 0)
            {
                var languageRejections = embeddedSubtitles
                    .Select(subtitle => new PreparedCandidate(
                        subtitle,
                        null,
                        null,
                        $"Stream {subtitle.StreamIndex}: language '{subtitle.Language ?? "unknown"}' does not match source language '{request.SourceLanguage}'."))
                    .ToList();
                throw CreateNoSuitableSourceException(languageRejections, request.SourceLanguage);
            }

            return null;
        }

        var candidateOrder = OrderTranslationCandidates(
            sameLanguageSubtitles,
            request,
            currentPath);
        var originalPaths = candidateOrder.ToDictionary(
            subtitle => subtitle,
            subtitle => (subtitle.ExtractedPath, subtitle.IsExtracted));
        var preparedCandidates = new List<PreparedCandidate>();
        var newlyExtractedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? selectedPath = null;
        EmbeddedSubtitle? selectedSubtitle = null;
        var preparationSucceeded = false;

        try
        {
            var currentSubtitle = IsSupplementalRequestAllowed(options)
                ? null
                : FindCandidateForPath(candidateOrder, request, currentPath);
            if (currentSubtitle != null)
            {
                var currentCandidatePath = GetValidatedCandidatePath(
                    currentSubtitle,
                    request,
                    mediaFile.Path,
                    cancellationToken);
                if (PathsEqual(currentCandidatePath, currentPath))
                {
                    var currentEntryCount = await CountEntriesForCandidate(
                        currentCandidatePath!,
                        currentSubtitle.StreamIndex,
                        cancellationToken);
                    if (currentEntryCount >= minimumDialogueEntries)
                    {
                        var originalPath = currentSubtitle.ExtractedPath;
                        var originalExtracted = currentSubtitle.IsExtracted;
                        if (currentSubtitle.IsTextBased)
                        {
                            currentSubtitle.ExtractedPath = currentCandidatePath;
                            currentSubtitle.IsExtracted = true;
                        }

                        SubtitleSourceSelectionResult currentSelection;
                        try
                        {
                            currentSelection = await _subtitleSourceSelectionService.SelectPrimaryAsync(
                                [currentSubtitle],
                                [request.SourceLanguage],
                                options.AllowCaptionFallback,
                                cancellationToken: cancellationToken,
                                contentDurationSeconds: contentDurationSeconds);
                        }
                        finally
                        {
                            currentSubtitle.ExtractedPath = originalPath;
                            currentSubtitle.IsExtracted = originalExtracted;
                        }

                        if (currentSelection.SelectedRole == SubtitleSourceCandidateRole.PrimaryFullDialogue)
                        {
                            selectedSubtitle = currentSubtitle;
                            selectedPath = currentCandidatePath;
                            if (currentSubtitle.IsTextBased)
                            {
                                currentSubtitle.ExtractedPath = selectedPath;
                                currentSubtitle.IsExtracted = true;
                            }

                            _embeddedSubtitleCacheService.Touch(currentCandidatePath!);
                            preparationSucceeded = true;
                            return new PreparedTranslationSource(
                                selectedPath!,
                                selectedSubtitle,
                                currentEntryCount.Value,
                                IsManualSelection: false);
                        }
                    }
                }
            }

            foreach (var subtitle in candidateOrder)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidatePath = GetValidatedCandidatePath(
                    subtitle,
                    request,
                    mediaFile.Path,
                    cancellationToken);
                if (candidatePath == null)
                {
                    var extraction = await ExtractCandidateForPreparationAsync(
                        subtitle,
                        request,
                        mediaFile.Path,
                        cancellationToken);
                    candidatePath = extraction?.Path;
                    if (extraction?.IsNew == true && candidatePath != null)
                    {
                        newlyExtractedPaths.Add(candidatePath);
                    }
                }

                if (candidatePath == null)
                {
                    preparedCandidates.Add(new PreparedCandidate(
                        subtitle,
                        null,
                        null,
                        $"Stream {subtitle.StreamIndex}: extraction returned no subtitle file."));
                    continue;
                }

                var entryCount = await CountEntriesForCandidate(
                    candidatePath,
                    subtitle.StreamIndex,
                    cancellationToken);
                if (!entryCount.HasValue)
                {
                    preparedCandidates.Add(new PreparedCandidate(
                        subtitle,
                        candidatePath,
                        null,
                        $"Stream {subtitle.StreamIndex}: extracted subtitle data is malformed and could not be parsed."));
                    continue;
                }

                if (entryCount.Value <= 0)
                {
                    preparedCandidates.Add(new PreparedCandidate(
                        subtitle,
                        candidatePath,
                        entryCount.Value,
                        $"Stream {subtitle.StreamIndex}: extracted subtitle could not be parsed."));
                    continue;
                }

                if (!IsSupplementalRequestAllowed(options) &&
                    entryCount.Value < minimumDialogueEntries)
                {
                    preparedCandidates.Add(new PreparedCandidate(
                        subtitle,
                        candidatePath,
                        entryCount.Value,
                        $"Stream {subtitle.StreamIndex}: extracted source has only {entryCount.Value} dialogue entries."));
                    continue;
                }

                if (subtitle.IsTextBased)
                {
                    subtitle.ExtractedPath = candidatePath;
                    subtitle.IsExtracted = true;
                }

                preparedCandidates.Add(new PreparedCandidate(subtitle, candidatePath, entryCount.Value, null));
            }

            var viableCandidates = preparedCandidates
                .Where(candidate =>
                    candidate.Path != null &&
                    candidate.EntryCount.HasValue &&
                    candidate.RejectionReason == null)
                .ToList();
            if (viableCandidates.Count == 0)
            {
                // A request language can match a sparse signs track while the real dialogue
                // track is mislabeled. The pre-extraction shortcut above only sees cached
                // candidates, so retry the contradiction check once the actual entry counts
                // are known and a full dialogue stream may still be found.
                var recovered = await TryRecoverContradictedFullDialogueCandidateAsync(
                    request,
                    options,
                    mediaFile.Path,
                    embeddedSubtitles,
                    preparedCandidates,
                    minimumDialogueEntries,
                    contentDurationSeconds,
                    cancellationToken);
                if (recovered != null)
                {
                    if (recovered.IsNew)
                    {
                        newlyExtractedPaths.Add(recovered.Candidate.Path!);
                    }

                    if (recovered.Candidate.Subtitle.IsTextBased)
                    {
                        recovered.Candidate.Subtitle.ExtractedPath = recovered.Candidate.Path;
                        recovered.Candidate.Subtitle.IsExtracted = true;
                    }

                    _embeddedSubtitleCacheService.Touch(recovered.Candidate.Path!);
                    preparationSucceeded = true;
                    return new PreparedTranslationSource(
                        recovered.Candidate.Path!,
                        recovered.Candidate.Subtitle,
                        recovered.Candidate.EntryCount!.Value,
                        IsManualSelection: false);
                }

                throw CreateNoSuitableSourceException(preparedCandidates, request.SourceLanguage);
            }

            if (IsSupplementalRequestAllowed(options))
            {
                var supplemental = await SelectSupplementalCandidateAsync(
                    viableCandidates,
                    request,
                    cancellationToken);
                if (supplemental == null)
                {
                    throw CreateNoSuitableSourceException(preparedCandidates, request.SourceLanguage);
                }

                selectedSubtitle = supplemental.Subtitle;
                selectedPath = supplemental.Path;
            }
            else
            {
                var selection = await _subtitleSourceSelectionService.SelectPrimaryAsync(
                    viableCandidates.Select(candidate => candidate.Subtitle).ToList(),
                    [request.SourceLanguage],
                    options.AllowCaptionFallback,
                    cancellationToken: cancellationToken,
                    contentDurationSeconds: contentDurationSeconds);
                var currentCandidate = viableCandidates.FirstOrDefault(candidate =>
                    PathsEqual(candidate.Path, currentPath));
                if (currentCandidate != null)
                {
                    var currentAssessment = selection.Assessments.FirstOrDefault(assessment =>
                        ReferenceEquals(assessment.Subtitle, currentCandidate.Subtitle));
                    if (currentAssessment != null && currentAssessment.Role == selection.SelectedRole)
                    {
                        selectedSubtitle = currentCandidate.Subtitle;
                        selectedPath = currentCandidate.Path;
                    }
                }

                if (selectedSubtitle == null)
                {
                    selectedSubtitle = selection.SelectedSubtitle;
                    if (selectedSubtitle == null)
                    {
                        throw CreateNoSuitableSourceException(
                            preparedCandidates,
                            request.SourceLanguage,
                            selection.Assessments);
                    }

                    selectedPath = viableCandidates
                        .First(candidate => ReferenceEquals(candidate.Subtitle, selectedSubtitle))
                        .Path;
                }
            }

            foreach (var (subtitle, original) in originalPaths)
            {
                subtitle.ExtractedPath = original.ExtractedPath;
                subtitle.IsExtracted = original.IsExtracted;
            }

            if (selectedSubtitle!.IsTextBased)
            {
                selectedSubtitle.ExtractedPath = selectedPath;
                selectedSubtitle.IsExtracted = true;
            }

            if (selectedPath == null || selectedSubtitle == null)
            {
                throw CreateNoSuitableSourceException(preparedCandidates, request.SourceLanguage);
            }

            _embeddedSubtitleCacheService.Touch(selectedPath);
            preparationSucceeded = true;
            return new PreparedTranslationSource(
                selectedPath,
                selectedSubtitle,
                viableCandidates.First(candidate => ReferenceEquals(candidate.Subtitle, selectedSubtitle)).EntryCount!.Value,
                IsManualSelection: false);
        }
        finally
        {
            foreach (var (subtitle, original) in originalPaths)
            {
                if (!preparationSucceeded || !ReferenceEquals(subtitle, selectedSubtitle))
                {
                    subtitle.ExtractedPath = original.ExtractedPath;
                    subtitle.IsExtracted = original.IsExtracted;
                }
            }

            foreach (var extractedPath in newlyExtractedPaths)
            {
                if (!preparationSucceeded ||
                    !string.Equals(extractedPath, selectedPath, StringComparison.OrdinalIgnoreCase))
                {
                    TryDeletePreparationFile(extractedPath);
                }
            }
        }
    }

    private async Task<PreparedTranslationSource?> PrepareExternalSourceAsync(
        string? path,
        bool isManualSelection,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var entryCount = await ReadEntryCountStrictlyAsync(path, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new PreparedTranslationSource(
            path,
            SelectedSubtitle: null,
            EntryCount: entryCount,
            IsManualSelection: isManualSelection);
    }

    private async Task<double?> ResolveContentDurationSecondsAsync(
        string? mediaPath,
        CancellationToken cancellationToken)
    {
        if (_mediaDurationProvider == null || string.IsNullOrWhiteSpace(mediaPath))
        {
            return null;
        }

        try
        {
            return await _mediaDurationProvider.GetDurationSecondsAsync(mediaPath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not resolve media duration for {MediaPath}", mediaPath);
            return null;
        }
    }

    public async Task<string?> ResolveReadableSourcePathAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (await HasUsableExistingPathAsync(request, cancellationToken))
        {
            if (_embeddedSubtitleCacheService.IsManagedCachePath(request.SubtitleToTranslate))
            {
                _embeddedSubtitleCacheService.Touch(request.SubtitleToTranslate!);
            }

            return request.SubtitleToTranslate;
        }

        if (!request.MediaId.HasValue)
        {
            return null;
        }

        if (string.Equals(request.SourceSnapshotType, SourceSubtitleSnapshot.ExternalType, StringComparison.Ordinal))
        {
            return null;
        }

        var preferredStreamIndex = await ResolvePreferredStreamIndexAsync(request, cancellationToken);
        var extractedPath = await _subtitleExtractionService.TryExtractEmbeddedSubtitleForRequestAsync(
            request.MediaId.Value,
            request.MediaType,
            request.SourceLanguage,
            null,
            preferredStreamIndex);

        if (string.IsNullOrWhiteSpace(extractedPath) || !File.Exists(extractedPath))
        {
            return null;
        }

        request.SubtitleToTranslate = extractedPath;
        request.SourceSubtitleFormat = SubtitleOutputModeHelper.NormalizeFormat(Path.GetExtension(extractedPath));
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Resolved source subtitle path for request {RequestId}: {Path}",
            request.Id,
            extractedPath);

        return extractedPath;
    }

    private string? GetValidatedCandidatePath(
        EmbeddedSubtitle subtitle,
        TranslationRequest request,
        string mediaPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!subtitle.IsReadableSource())
        {
            return null;
        }

        var storedPath = subtitle.HasUsableOcr()
            ? subtitle.OcrExtractedPath
            : subtitle.ExtractedPath;
        var canonicalPath = subtitle.HasUsableOcr()
            ? _embeddedSubtitleCacheService.GetOcrCachePath(
                request.MediaId!.Value,
                request.MediaType,
                subtitle.StreamIndex,
                subtitle.Language)
            : _embeddedSubtitleCacheService.GetCachePath(
                request.MediaId!.Value,
                request.MediaType,
                subtitle.StreamIndex,
                subtitle.CodecName,
                subtitle.Language);

        if (!string.IsNullOrWhiteSpace(storedPath) &&
            File.Exists(storedPath) &&
            IsPathCurrentForCandidate(storedPath, canonicalPath, mediaPath))
        {
            return storedPath;
        }

        if (File.Exists(canonicalPath) &&
            IsPathCurrentForCandidate(canonicalPath, canonicalPath, mediaPath))
        {
            return canonicalPath;
        }

        return null;
    }

    private bool IsPathCurrentForCandidate(string path, string canonicalPath, string mediaPath)
    {
        if (!_embeddedSubtitleCacheService.IsManagedCachePath(path))
        {
            return true;
        }

        return IsCanonicalOrPreparationPath(path, canonicalPath) &&
               _embeddedSubtitleCacheService.IsCurrentForSource(path, mediaPath);
    }

    private bool IsCanonicalOrPreparationPath(string path, string canonicalPath)
    {
        if (PathsEqual(path, canonicalPath))
        {
            return true;
        }

        if (!_embeddedSubtitleCacheService.IsManagedCachePath(path) ||
            !string.Equals(
                Path.GetDirectoryName(Path.GetFullPath(path)),
                Path.GetDirectoryName(Path.GetFullPath(canonicalPath)),
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(path), Path.GetExtension(canonicalPath), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var preparationPrefix = $"{Path.GetFileNameWithoutExtension(canonicalPath)}.prepare-";
        var pathBaseName = Path.GetFileNameWithoutExtension(path);
        return pathBaseName.StartsWith(preparationPrefix, StringComparison.OrdinalIgnoreCase) &&
               Guid.TryParseExact(pathBaseName[preparationPrefix.Length..], "N", out _);
    }

    private async Task<PreparationExtraction?> ExtractCandidateForPreparationAsync(
        EmbeddedSubtitle subtitle,
        TranslationRequest request,
        string mediaPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!subtitle.IsTextBased)
        {
            return null;
        }

        var canonicalPath = _embeddedSubtitleCacheService.GetCachePath(
            request.MediaId!.Value,
            request.MediaType,
            subtitle.StreamIndex,
            subtitle.CodecName,
            subtitle.Language);
        var extension = Path.GetExtension(canonicalPath);
        string outputPath;
        do
        {
            outputPath = Path.Combine(
                Path.GetDirectoryName(canonicalPath)!,
                $"{Path.GetFileNameWithoutExtension(canonicalPath)}.prepare-{Guid.NewGuid():N}{extension}");
        }
        while (File.Exists(outputPath));

        string? extractedPath;
        try
        {
            extractedPath = await _subtitleExtractionService.ExtractSubtitleToFile(
                mediaPath,
                subtitle.StreamIndex,
                outputPath,
                subtitle.CodecName);
        }
        catch
        {
            TryDeletePreparationFile(outputPath);
            throw;
        }

        if (string.IsNullOrWhiteSpace(extractedPath) || !File.Exists(extractedPath))
        {
            TryDeletePreparationFile(outputPath);
            return null;
        }

        try
        {
            if (_embeddedSubtitleCacheService.IsManagedCachePath(extractedPath) &&
                !_embeddedSubtitleCacheService.IsCurrentForSource(extractedPath, mediaPath))
            {
                _embeddedSubtitleCacheService.RecordSourceSnapshot(extractedPath, mediaPath);
            }
        }
        catch
        {
            TryDeletePreparationFile(outputPath);
            throw;
        }

        return new PreparationExtraction(
            extractedPath,
            IsNew: PathsEqual(extractedPath, outputPath));
    }

    /// <summary>
    /// Returns true only when every readable candidate is already extracted and its entry count
    /// is below the dialogue floor. Candidates without an extracted file are treated as unknown
    /// so the contradiction check never runs just because files were not cached yet.
    /// </summary>
    private static bool AllMatchedCandidatesKnownSparse(
        IReadOnlyList<EmbeddedSubtitle> candidates,
        int minimumDialogueEntries)
    {
        var readableCandidates = candidates
            .Where(candidate => candidate.IsReadableSource())
            .ToList();
        if (readableCandidates.Count == 0)
        {
            return false;
        }

        foreach (var candidate in readableCandidates)
        {
            var readablePath = candidate.GetReadableSourcePath();
            if (string.IsNullOrWhiteSpace(readablePath) || !File.Exists(readablePath))
            {
                return false;
            }

            try
            {
                if (SubtitleExtractionService.CountSubtitleEntries(readablePath) >= minimumDialogueEntries)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Last contradiction check before a sparse-source failure is reported. The earlier
    /// shortcut only trusts cached entry counts, so a matching signs track can be extracted and
    /// rejected while a mislabeled full dialogue stream was never counted. When every candidate
    /// for the request language is now known to be below the dialogue floor, sample the other
    /// readable streams and adopt one whose detected language satisfies the request.
    /// </summary>
    private async Task<RecoveredCandidate?> TryRecoverContradictedFullDialogueCandidateAsync(
        TranslationRequest request,
        TranslationSourcePreparationOptions options,
        string? mediaPath,
        IReadOnlyList<EmbeddedSubtitle> embeddedSubtitles,
        IReadOnlyList<PreparedCandidate> preparedCandidates,
        int minimumDialogueEntries,
        double? contentDurationSeconds,
        CancellationToken cancellationToken)
    {
        if (options.HasManualStreamSelection ||
            options.IsSupplementalRequest ||
            _languageContradictionDetector == null ||
            string.IsNullOrWhiteSpace(mediaPath))
        {
            return null;
        }

        var sparseRejections = preparedCandidates
            .Where(candidate => candidate.RejectionReason != null &&
                                candidate.EntryCount.HasValue &&
                                candidate.EntryCount.Value < minimumDialogueEntries)
            .ToList();
        if (sparseRejections.Count == 0 || sparseRejections.Count != preparedCandidates.Count)
        {
            return null;
        }

        // Nothing to adopt when every readable stream already matches the request language.
        if (!embeddedSubtitles.Any(subtitle =>
                subtitle.IsReadableSource() &&
                !SubtitleLanguageHelper.LanguageMatchesEffective(subtitle, request.SourceLanguage)))
        {
            return null;
        }

        var adoptedSubtitle = await _languageContradictionDetector.TryAdoptDetectedSourceLanguageAsync(
            request,
            embeddedSubtitles,
            mediaPath,
            cancellationToken);
        if (adoptedSubtitle == null)
        {
            return null;
        }

        var candidates = embeddedSubtitles
            .Where(subtitle =>
                subtitle.IsReadableSource() &&
                SubtitleLanguageHelper.LanguageMatchesEffective(subtitle, request.SourceLanguage))
            .OrderBy(subtitle => subtitle.StreamIndex)
            .ToList();

        foreach (var subtitle in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidatePath = GetValidatedCandidatePath(
                subtitle,
                request,
                mediaPath,
                cancellationToken);
            var isNew = false;
            if (candidatePath == null)
            {
                var extraction = await ExtractCandidateForPreparationAsync(
                    subtitle,
                    request,
                    mediaPath,
                    cancellationToken);
                if (extraction == null)
                {
                    continue;
                }

                candidatePath = extraction.Path;
                isNew = extraction.IsNew;
            }

            var entryCount = await CountEntriesForCandidate(
                candidatePath,
                subtitle.StreamIndex,
                cancellationToken);
            if (!entryCount.HasValue || entryCount.Value < minimumDialogueEntries)
            {
                continue;
            }

            // Validate against the freshly extracted path so entry count, health and subtitle
            // type describe the adopted stream instead of its stale cached metadata.
            var originalPath = subtitle.ExtractedPath;
            var originalExtracted = subtitle.IsExtracted;
            var acceptable = false;
            if (subtitle.IsTextBased)
            {
                subtitle.ExtractedPath = candidatePath;
                subtitle.IsExtracted = true;
            }

            try
            {
                acceptable = await IsAcceptablePrimaryCandidateAsync(
                    subtitle,
                    request,
                    options,
                    entryCount.Value,
                    cancellationToken,
                    contentDurationSeconds);
            }
            finally
            {
                if (!acceptable)
                {
                    subtitle.ExtractedPath = originalPath;
                    subtitle.IsExtracted = originalExtracted;
                }
            }

            if (!acceptable)
            {
                continue;
            }

            return new RecoveredCandidate(
                new PreparedCandidate(subtitle, candidatePath, entryCount.Value, null),
                isNew);
        }

        return null;
    }

    private async Task<bool> IsAcceptablePrimaryCandidateAsync(
        EmbeddedSubtitle subtitle,
        TranslationRequest request,
        TranslationSourcePreparationOptions options,
        int entryCount,
        CancellationToken cancellationToken,
        double? contentDurationSeconds = null)
    {
        if (IsSupplementalRequestAllowed(options))
        {
            var subtitleType = SubtitleLanguageHelper.DetermineSubtitleType(subtitle, entryCount);
            if (!SubtitleLanguageHelper.IsSupplementalSubtitleType(subtitleType))
            {
                return false;
            }

            return await IsUsableSupplementalCandidateAsync(
                subtitle.GetReadableSourcePath()!,
                entryCount,
                cancellationToken);
        }

        if (entryCount < SubtitleExtractionService.GetMinimumDialogueEntries(contentDurationSeconds))
        {
            return false;
        }

        var selection = await _subtitleSourceSelectionService.SelectPrimaryAsync(
            [subtitle],
            [request.SourceLanguage],
            options.AllowCaptionFallback,
            cancellationToken: cancellationToken,
            contentDurationSeconds: contentDurationSeconds);
        return selection.SelectedSubtitle != null;
    }

    private async Task<PreparedCandidate?> SelectSupplementalCandidateAsync(
        IReadOnlyCollection<PreparedCandidate> candidates,
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in candidates
                     .Where(item => item.EntryCount is > 0)
                     .OrderByDescending(item => ScoreSubtitle(item.Subtitle, request)))
        {
            var subtitleType = SubtitleLanguageHelper.DetermineSubtitleType(
                candidate.Subtitle,
                candidate.EntryCount);
            if (!SubtitleLanguageHelper.IsSupplementalSubtitleType(subtitleType) ||
                candidate.Path == null)
            {
                continue;
            }

            if (await IsUsableSupplementalCandidateAsync(
                    candidate.Path,
                    candidate.EntryCount!.Value,
                    cancellationToken))
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task<bool> IsUsableSupplementalCandidateAsync(
        string path,
        int entryCount,
        CancellationToken cancellationToken)
    {
        if (entryCount <= 0 || !File.Exists(path))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var parsedSubtitles = await _subtitleService.ReadSubtitles(path);
        cancellationToken.ThrowIfCancellationRequested();
        return parsedSubtitles.Count > 0 &&
               SubtitleSourceHealthAnalyzer.Analyze(parsedSubtitles).Status !=
               SubtitleSourceHealthStatus.CorruptText;
    }

    private async Task<int?> CountEntriesForCandidate(
        string path,
        int streamIndex,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ReadEntryCountStrictlyAsync(path, cancellationToken);
        }
        catch (Exception ex) when (IsSubtitleDataFormatException(ex))
        {
            _logger.LogDebug(
                ex,
                "Could not parse source subtitle stream {StreamIndex} at {Path}",
                streamIndex,
                path);
            return null;
        }
    }

    private async Task<int> ReadEntryCountStrictlyAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parsedSubtitles = await _subtitleService.ReadSubtitles(path);
        cancellationToken.ThrowIfCancellationRequested();
        return parsedSubtitles.Count;
    }

    private static bool IsSubtitleDataFormatException(Exception exception)
    {
        return exception is FormatException or
            ArgumentException or
            IndexOutOfRangeException or
            OverflowException or
            NotSupportedException;
    }

    private void TryDeletePreparationFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                if (_embeddedSubtitleCacheService.IsManagedCachePath(path))
                {
                    _embeddedSubtitleCacheService.Invalidate(path);
                }
                else
                {
                    File.Delete(path);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not remove rejected temporary subtitle preparation file {Path}",
                path);
        }
    }

    private static bool IsSupplementalRequestAllowed(TranslationSourcePreparationOptions options)
    {
        return options.IsSupplementalRequest && options.SupplementalTranslationEnabled;
    }

    private IReadOnlyList<EmbeddedSubtitle> OrderTranslationCandidates(
        IReadOnlyList<EmbeddedSubtitle> subtitles,
        TranslationRequest request,
        string? currentPath)
    {
        var currentPathSubtitle = FindCandidateForPath(subtitles, request, currentPath);
        var snapshotIdentitySubtitle = subtitles.FirstOrDefault(subtitle =>
            !string.IsNullOrWhiteSpace(request.SourceSnapshotIdentity) &&
            string.Equals(
                _sourceSubtitleSnapshotService.CreateEmbeddedSnapshot(subtitle, request.SourceLanguage).Identity,
                request.SourceSnapshotIdentity,
                StringComparison.Ordinal));
        var snapshotIndexSubtitle = request.SourceSnapshotStreamIndex.HasValue
            ? subtitles.FirstOrDefault(subtitle => subtitle.StreamIndex == request.SourceSnapshotStreamIndex.Value)
            : null;

        return subtitles
            .OrderByDescending(subtitle => ReferenceEquals(subtitle, currentPathSubtitle))
            .ThenByDescending(subtitle => ReferenceEquals(subtitle, snapshotIdentitySubtitle))
            .ThenByDescending(subtitle => ReferenceEquals(subtitle, snapshotIndexSubtitle))
            .ThenByDescending(subtitle => ScoreSubtitle(subtitle, request))
            .ThenBy(subtitle => subtitle.StreamIndex)
            .ToList();
    }

    private EmbeddedSubtitle? FindCandidateForPath(
        IReadOnlyList<EmbeddedSubtitle> subtitles,
        TranslationRequest request,
        string? path)
    {
        return subtitles.FirstOrDefault(subtitle =>
                   IsCanonicalOrPreparationPath(path ?? string.Empty, GetCanonicalPath(subtitle, request))) ??
               subtitles.FirstOrDefault(subtitle =>
                   PathsEqual(GetStoredReadablePath(subtitle), path));
    }

    private string? GetStoredReadablePath(EmbeddedSubtitle subtitle)
    {
        return subtitle.HasUsableOcr()
            ? subtitle.OcrExtractedPath
            : subtitle.ExtractedPath;
    }

    private string GetCanonicalPath(EmbeddedSubtitle subtitle, TranslationRequest request)
    {
        return subtitle.HasUsableOcr()
            ? _embeddedSubtitleCacheService.GetOcrCachePath(
                request.MediaId!.Value,
                request.MediaType,
                subtitle.StreamIndex,
                subtitle.Language)
            : _embeddedSubtitleCacheService.GetCachePath(
                request.MediaId!.Value,
                request.MediaType,
                subtitle.StreamIndex,
                subtitle.CodecName,
                subtitle.Language);
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static InvalidOperationException CreateNoSuitableSourceException(
        IReadOnlyCollection<PreparedCandidate> candidates,
        string sourceLanguage,
        IReadOnlyCollection<SubtitleSourceCandidateAssessment>? assessments = null)
    {
        var assessmentByStream = assessments?
            .GroupBy(item => item.Subtitle.StreamIndex)
            .ToDictionary(group => group.Key, group => group.First());
        var reasons = candidates
            .OrderBy(candidate => candidate.Subtitle.StreamIndex)
            .Select(candidate =>
            {
                var count = candidate.EntryCount.HasValue
                    ? $" ({candidate.EntryCount.Value} entries)"
                    : string.Empty;
                var reason = candidate.RejectionReason;
                if (reason == null && assessmentByStream != null &&
                    assessmentByStream.TryGetValue(candidate.Subtitle.StreamIndex, out var assessment))
                {
                    reason = assessment.Reason;
                    if (!candidate.EntryCount.HasValue && assessment.EntryCount.HasValue)
                    {
                        count = $" ({assessment.EntryCount.Value} entries)";
                    }
                }

                return $"Stream {candidate.Subtitle.StreamIndex}{count}: {reason ?? "rejected by source fitness checks"}";
            })
            .ToList();
        var rejectionDetails = reasons.Count > 0
            ? string.Join("; ", reasons)
            : "No readable candidates were available.";
        return new InvalidOperationException(
            $"No suitable source subtitle matched source language '{sourceLanguage}'. {rejectionDetails}");
    }

    private sealed record PreparedCandidate(
        EmbeddedSubtitle Subtitle,
        string? Path,
        int? EntryCount,
        string? RejectionReason);

    private sealed record RecoveredCandidate(PreparedCandidate Candidate, bool IsNew);

    private sealed record PreparationExtraction(string Path, bool IsNew);

    private async Task<bool> HasUsableExistingPathAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        var path = request.SubtitleToTranslate;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (!File.Exists(path))
        {
            if (_embeddedSubtitleCacheService.IsManagedCachePath(path))
            {
                return await TryRestoreMissingManagedOcrAsync(request, path, cancellationToken);
            }

            return false;
        }

        if (!_embeddedSubtitleCacheService.IsManagedCachePath(path))
        {
            return true;
        }

        if (_embeddedSubtitleCacheService.IsExpired(path) || !request.MediaId.HasValue)
        {
            return false;
        }

        var mediaFile = await ResolveCurrentMediaFileAsync(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(mediaFile.Path))
        {
            _logger.LogDebug(
                "Cannot validate managed embedded subtitle cache {CachePath} because media {MediaId} is unavailable",
                path,
                request.MediaId);
            return false;
        }

        if (_embeddedSubtitleCacheService.IsCurrentForSource(path, mediaFile.Path))
        {
            return true;
        }

        _embeddedSubtitleCacheService.Invalidate(path);
        _logger.LogInformation(
            "Invalidated managed embedded subtitle cache {CachePath} after source media snapshot changed at {MediaPath}",
            path,
            mediaFile.Path);
        return false;
    }

    /// <summary>
    /// Recovers a missing managed OCR cache file whose source media is unchanged by
    /// re-running OCR for the stream (the same invocation the OcrPending flow uses) and
    /// returning the regenerated path. Stale OCR of a CHANGED media is never regenerated:
    /// recovery only fires when the recorded source snapshot still matches the media.
    /// Any failure falls back to the existing behavior (false) - recovery never loops,
    /// never throws, and never masks the original resolution failure.
    /// </summary>
    private async Task<bool> TryRestoreMissingManagedOcrAsync(
        TranslationRequest request,
        string cachePath,
        CancellationToken cancellationToken,
        int? requiredStreamIndex = null)
    {
        if (!request.MediaId.HasValue)
        {
            return false;
        }

        var subtitle = await _dbContext.EmbeddedSubtitles
            .FirstOrDefaultAsync(
                item =>
                    item.OcrExtractedPath == cachePath &&
                    (request.MediaType == MediaType.Movie
                        ? item.MovieId == request.MediaId.Value
                        : item.EpisodeId == request.MediaId.Value),
                cancellationToken);
        if (subtitle == null ||
            (requiredStreamIndex.HasValue && subtitle.StreamIndex != requiredStreamIndex.Value) ||
            subtitle.OcrStatus is not (SubtitleOcrStatus.Succeeded or SubtitleOcrStatus.Approved))
        {
            return false;
        }

        var mediaFile = await ResolveCurrentMediaFileAsync(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(mediaFile.Path))
        {
            _logger.LogDebug(
                "Cannot restore missing OCR cache {CachePath} because media {MediaId} is unavailable",
                cachePath,
                request.MediaId);
            return false;
        }

        if (!_embeddedSubtitleCacheService.IsSourceSnapshotCurrent(cachePath, mediaFile.Path))
        {
            return false;
        }

        _logger.LogInformation(
            "OCR cache file missing for {CachePath} but media unchanged; re-running OCR for {MediaType} {MediaId} stream {StreamIndex}",
            cachePath,
            request.MediaType,
            request.MediaId,
            subtitle.StreamIndex);

        // Capture the row state BEFORE re-running OCR: a single failed re-OCR must not be
        // allowed to permanently demote a previously successful row into a terminal
        // status, or the recovery guard would close off all future recovery attempts.
        var capturedOcrStatus = subtitle.OcrStatus;
        var capturedOcrError = subtitle.OcrError;

        try
        {
            var result = await _subtitleOcrService.RunOcrAsync(
                request.MediaId.Value,
                request.MediaType,
                subtitle.StreamIndex,
                manual: false,
                cancellationToken);
            if (!result.Success ||
                string.IsNullOrWhiteSpace(result.ExtractedPath) ||
                !File.Exists(result.ExtractedPath))
            {
                await RestoreOcrRowAfterFailedRecoveryAsync(
                    subtitle,
                    capturedOcrStatus,
                    capturedOcrError,
                    cachePath,
                    cancellationToken);
                return false;
            }

            request.SubtitleToTranslate = result.ExtractedPath;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to re-run OCR for missing cache {CachePath} of {MediaType} {MediaId}; falling back to existing resolution behavior",
                cachePath,
                request.MediaType,
                request.MediaId);
            await RestoreOcrRowAfterFailedRecoveryAsync(
                subtitle,
                capturedOcrStatus,
                capturedOcrError,
                cachePath,
                cancellationToken);
            return false;
        }
    }

    /// <summary>
    /// Reverts a failed re-OCR that demoted a previously successful OCR row into a
    /// terminal status (Failed / BlockedLowQuality). One failed attempt must not lock the
    /// row out of future recovery: the captured Succeeded/Approved state is restored so
    /// the next resolution attempt can re-run OCR again. Best-effort by design: the
    /// recovery path never throws, so a restore failure is logged and otherwise ignored.
    /// </summary>
    private async Task RestoreOcrRowAfterFailedRecoveryAsync(
        EmbeddedSubtitle subtitle,
        SubtitleOcrStatus capturedStatus,
        string? capturedError,
        string cachePath,
        CancellationToken cancellationToken)
    {
        if (capturedStatus is not (SubtitleOcrStatus.Succeeded or SubtitleOcrStatus.Approved))
        {
            return;
        }

        try
        {
            // Reload by Id: RunOcrAsync may have detached or replaced the tracked row
            // while applying its failure state.
            var current = await _dbContext.EmbeddedSubtitles
                .FirstOrDefaultAsync(item => item.Id == subtitle.Id, cancellationToken);
            if (current == null ||
                current.OcrStatus == capturedStatus ||
                current.OcrStatus is not (SubtitleOcrStatus.Failed or SubtitleOcrStatus.BlockedLowQuality))
            {
                return;
            }

            var demotedStatus = current.OcrStatus;
            current.OcrStatus = capturedStatus;
            current.OcrError = capturedError;
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "Failed re-OCR for missing cache {CachePath} demoted subtitle {SubtitleId} to {DemotedStatus}; restored to {RestoredStatus} so future recovery attempts remain possible",
                cachePath,
                current.Id,
                demotedStatus,
                capturedStatus);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not restore OCR row state after failed re-OCR for missing cache {CachePath}",
                cachePath);
        }
    }

    private async Task<ResolvedMediaFile> ResolveCurrentMediaFileAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.MediaId.HasValue)
        {
            return ResolvedMediaFile.None;
        }

        if (request.MediaType == MediaType.Movie)
        {
            var movie = await _dbContext.Movies
                .FirstOrDefaultAsync(item => item.Id == request.MediaId.Value, cancellationToken);
            if (movie == null)
            {
                return ResolvedMediaFile.None;
            }

            return await ResolveMediaFileWithAdoptionAsync(
                movie.Path,
                movie.FileName,
                adoptedFileName =>
                {
                    movie.FileName = adoptedFileName;
                    return _dbContext.SaveChangesAsync(cancellationToken);
                });
        }

        if (request.MediaType == MediaType.Episode)
        {
            var episode = await _dbContext.Episodes
                .FirstOrDefaultAsync(item => item.Id == request.MediaId.Value, cancellationToken);
            if (episode == null)
            {
                return ResolvedMediaFile.None;
            }

            return await ResolveMediaFileWithAdoptionAsync(
                episode.Path,
                episode.FileName,
                adoptedFileName =>
                {
                    episode.FileName = adoptedFileName;
                    return _dbContext.SaveChangesAsync(cancellationToken);
                });
        }

        return ResolvedMediaFile.None;
    }

    /// <summary>
    /// Resolves the media file for a directory + DB file name. When the recorded file name is
    /// stale (the release was replaced on disk) or missing, and the directory holds EXACTLY ONE
    /// video file, that file is adopted: the DB file name is updated and the resolution continues
    /// with it. With zero or multiple video files we never guess - the resolution fails exactly
    /// as it would have before.
    /// </summary>
    private static async Task<ResolvedMediaFile> ResolveMediaFileWithAdoptionAsync(
        string? directoryPath,
        string? fileName,
        Func<string, Task> persistAdoption)
    {
        var directPath = ResolveMediaFilePath(directoryPath, fileName);
        if (directPath != null)
        {
            return new ResolvedMediaFile(directPath, FileNameChanged: false);
        }

        var searchDirectory = directoryPath;
        if (string.IsNullOrWhiteSpace(searchDirectory) &&
            !string.IsNullOrWhiteSpace(fileName) &&
            Path.IsPathRooted(fileName))
        {
            searchDirectory = Path.GetDirectoryName(fileName);
        }

        var videoFiles = EnumerateVideoFiles(searchDirectory);
        if (videoFiles.Count != 1)
        {
            return ResolvedMediaFile.None;
        }

        var adoptedFileName = Path.GetFileName(videoFiles[0]);
        var fileNameChanged = !string.Equals(
            adoptedFileName,
            fileName,
            StringComparison.OrdinalIgnoreCase);
        if (fileNameChanged)
        {
            await persistAdoption(adoptedFileName);
        }

        return new ResolvedMediaFile(videoFiles[0], fileNameChanged);
    }

    private static List<string> EnumerateVideoFiles(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(directoryPath)
                .Where(path => MediaExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private sealed record ResolvedMediaFile(string? Path, bool FileNameChanged)
    {
        public static ResolvedMediaFile None { get; } = new(null, false);
    }

    private static string? ResolveMediaFilePath(string? directoryPath, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        if (Path.IsPathRooted(fileName))
        {
            return File.Exists(fileName) ? fileName : null;
        }

        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return null;
        }

        var directPath = Path.Combine(directoryPath, fileName);
        if (File.Exists(directPath))
        {
            return directPath;
        }

        if (!Directory.Exists(directoryPath))
        {
            return null;
        }

        var baseName = MediaExtensions.Contains(
            Path.GetExtension(fileName),
            StringComparer.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(fileName)
            : fileName;

        foreach (var extension in MediaExtensions)
        {
            var candidatePath = Path.Combine(directoryPath, baseName + extension);
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return Directory.EnumerateFiles(directoryPath)
            .FirstOrDefault(path =>
                MediaExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) &&
                (string.Equals(
                     Path.GetFileNameWithoutExtension(path),
                     baseName,
                     StringComparison.OrdinalIgnoreCase) ||
                 Path.GetFileName(path).StartsWith(
                     baseName + ".",
                     StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<int?> ResolvePreferredStreamIndexAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        var embeddedSubtitles = await LoadCurrentEmbeddedSubtitlesAsync(request, cancellationToken);
        if (embeddedSubtitles.Count == 0)
        {
            return request.SourceSnapshotStreamIndex;
        }

        if (request.SourceSnapshotStreamIndex.HasValue)
        {
            var exactStream = embeddedSubtitles.FirstOrDefault(subtitle =>
                subtitle.IsReadableSource() && subtitle.StreamIndex == request.SourceSnapshotStreamIndex.Value);
            if (exactStream != null)
            {
                return exactStream.StreamIndex;
            }
        }

        if (!string.IsNullOrWhiteSpace(request.SourceSnapshotIdentity))
        {
            foreach (var subtitle in embeddedSubtitles.Where(subtitle => subtitle.IsReadableSource()))
            {
                var snapshot = _sourceSubtitleSnapshotService.CreateEmbeddedSnapshot(subtitle, request.SourceLanguage);
                if (string.Equals(snapshot.Identity, request.SourceSnapshotIdentity, StringComparison.Ordinal))
                {
                    return subtitle.StreamIndex;
                }
            }
        }

        var readableSubtitles = embeddedSubtitles
            .Where(subtitle => subtitle.IsReadableSource())
            .ToList();
        if (!SubtitleLanguageHelper.IsSupplementalSubtitleType(request.SourceSubtitleType))
        {
            var mediaFile = await ResolveCurrentMediaFileAsync(request, cancellationToken);
            var contentDurationSeconds = await ResolveContentDurationSecondsAsync(
                mediaFile.Path,
                cancellationToken);
            var selection = await _subtitleSourceSelectionService.SelectPrimaryAsync(
                readableSubtitles,
                [request.SourceLanguage],
                allowCaptionFallback: true,
                cancellationToken: cancellationToken,
                contentDurationSeconds: contentDurationSeconds);
            if (selection.SelectedSubtitle != null)
            {
                return selection.SelectedSubtitle.StreamIndex;
            }
        }

        return readableSubtitles
            .OrderByDescending(subtitle => ScoreSubtitle(subtitle, request))
            .Select(subtitle => (int?)subtitle.StreamIndex)
            .FirstOrDefault();
    }

    private async Task<List<EmbeddedSubtitle>> LoadCurrentEmbeddedSubtitlesAsync(
        TranslationRequest request,
        CancellationToken cancellationToken,
        bool invalidateStaleManagedArtifacts = true,
        bool forceRefresh = false)
    {
        if (!request.MediaId.HasValue)
        {
            return [];
        }

        var mediaFile = await ResolveCurrentMediaFileAsync(request, cancellationToken);

        if (request.MediaType == MediaType.Movie)
        {
            var movie = await _dbContext.Movies
                .Include(item => item.EmbeddedSubtitles)
                .FirstOrDefaultAsync(item => item.Id == request.MediaId.Value, cancellationToken);
            if (movie == null)
            {
                return [];
            }

            // Refresh when there are no rows at all, or when the media file changed on disk
            // (the DB file name was stale and the resolver adopted the replacement file) -
            // rows probed from the old release are no longer accurate.
            if (movie.EmbeddedSubtitles.Count == 0 || mediaFile.FileNameChanged || forceRefresh)
            {
                await _subtitleExtractionService.SyncEmbeddedSubtitles(movie);
                await _dbContext.Entry(movie).Collection(item => item.EmbeddedSubtitles).LoadAsync(cancellationToken);
            }

            var movieSubtitles = movie.EmbeddedSubtitles.ToList();
            if (invalidateStaleManagedArtifacts)
            {
                await InvalidateStaleManagedArtifactsAsync(movieSubtitles, mediaFile.Path, cancellationToken);
            }

            return movieSubtitles;
        }

        var episode = await _dbContext.Episodes
            .Include(item => item.EmbeddedSubtitles)
            .FirstOrDefaultAsync(item => item.Id == request.MediaId.Value, cancellationToken);
        if (episode == null)
        {
            return [];
        }

        if (episode.EmbeddedSubtitles.Count == 0 || mediaFile.FileNameChanged || forceRefresh)
        {
            await _subtitleExtractionService.SyncEmbeddedSubtitles(episode);
            await _dbContext.Entry(episode).Collection(item => item.EmbeddedSubtitles).LoadAsync(cancellationToken);
        }

        var episodeSubtitles = episode.EmbeddedSubtitles.ToList();
        if (invalidateStaleManagedArtifacts)
        {
            await InvalidateStaleManagedArtifactsAsync(episodeSubtitles, mediaFile.Path, cancellationToken);
        }

        return episodeSubtitles;
    }

    private async Task InvalidateStaleManagedArtifactsAsync(
        IReadOnlyCollection<EmbeddedSubtitle> subtitles,
        string? mediaPath,
        CancellationToken cancellationToken)
    {
        if (subtitles.Count == 0 || string.IsNullOrWhiteSpace(mediaPath))
        {
            return;
        }

        var changed = false;
        foreach (var subtitle in subtitles)
        {
            if (IsStaleManagedArtifact(subtitle.ExtractedPath, mediaPath))
            {
                _embeddedSubtitleCacheService.Invalidate(subtitle.ExtractedPath!);
                subtitle.IsExtracted = false;
                subtitle.ExtractedPath = null;
                changed = true;
            }

            if (IsStaleManagedArtifact(subtitle.OcrExtractedPath, mediaPath))
            {
                _embeddedSubtitleCacheService.Invalidate(subtitle.OcrExtractedPath!);
                SubtitleOcrStatePolicy.Reset(subtitle);
                changed = true;
            }
        }

        if (changed)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private bool IsStaleManagedArtifact(string? path, string mediaPath)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               _embeddedSubtitleCacheService.IsManagedCachePath(path) &&
               !_embeddedSubtitleCacheService.IsCurrentForSource(path, mediaPath);
    }

    private static int ScoreSubtitle(EmbeddedSubtitle subtitle, TranslationRequest request)
    {
        var score = SubtitleLanguageHelper.ScoreSubtitleCandidate(subtitle, request.SourceLanguage);

        if (!string.IsNullOrWhiteSpace(request.SelectedStreamTitle) &&
            string.Equals(subtitle.Title?.Trim(), request.SelectedStreamTitle.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            score += 120;
        }

        if (subtitle.IsForced == request.IsForcedSubtitle)
        {
            score += 30;
        }
        else if (request.IsForcedSubtitle)
        {
            score -= 30;
        }

        var requestType = request.SourceSubtitleType ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(requestType) &&
            string.Equals(SubtitleLanguageHelper.DetermineSubtitleType(subtitle), requestType, StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }

        return score;
    }

}
