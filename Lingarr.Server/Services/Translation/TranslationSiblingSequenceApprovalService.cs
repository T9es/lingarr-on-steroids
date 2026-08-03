using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Translation;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services.Translation;

public class TranslationSiblingSequenceApprovalService : ITranslationSiblingSequenceApprovalService
{
    private const int RequiredRequestCount = 2;
    private const int MinimumRunLength = 3;

    private readonly LingarrDbContext _dbContext;
    private readonly ITranslationCheckpointService _checkpointService;
    private readonly IFailedTranslationCompletionService _completionService;
    private readonly ILogger<TranslationSiblingSequenceApprovalService> _logger;

    public TranslationSiblingSequenceApprovalService(
        LingarrDbContext dbContext,
        ITranslationCheckpointService checkpointService,
        IFailedTranslationCompletionService completionService,
        ILogger<TranslationSiblingSequenceApprovalService> logger)
    {
        _dbContext = dbContext;
        _checkpointService = checkpointService;
        _completionService = completionService;
        _logger = logger;
    }

    public async Task<SiblingSequenceApprovalResult> ProcessMissingTranslationAsync(
        TranslationRequest request,
        MissingTranslationException exception,
        CancellationToken cancellationToken)
    {
        var sourceFingerprint = await GetCheckpointFingerprintAsync(request, cancellationToken);
        await UpsertMissingCuesAsync(
            request.Id,
            exception.MissingCues,
            sourceFingerprint,
            request.JobId,
            cancellationToken);

        if (!IsLibraryEpisodeRequest(request) ||
            exception.MissingCues.All(cue => !cue.AutoApprovalEligible))
        {
            return NoApproval(exception);
        }

        var currentEpisode = await _dbContext.Episodes
            .Include(item => item.Season)
            .ThenInclude(item => item.Show)
            .FirstOrDefaultAsync(item => item.Id == request.MediaId!.Value, cancellationToken);
        if (currentEpisode?.Season == null)
        {
            return NoApproval(exception);
        }

        var sameSeasonEpisodeIds = await _dbContext.Episodes
            .Where(item => item.SeasonId == currentEpisode.SeasonId)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        if (sameSeasonEpisodeIds.Count < RequiredRequestCount)
        {
            return NoApproval(exception);
        }

        var candidateRequests = await LoadCandidateRequestsAsync(
            request,
            sameSeasonEpisodeIds,
            cancellationToken);
        if (candidateRequests.Count < RequiredRequestCount)
        {
            return NoApproval(exception);
        }

        var candidateRequestIds = candidateRequests
            .Select(item => item.Id)
            .ToList();
        var allCues = await _dbContext.TranslationFailedCues
            .Where(item => candidateRequestIds.Contains(item.TranslationRequestId))
            .OrderBy(item => item.Position)
            .ToListAsync(cancellationToken);

        var cuesByRequest = allCues
            .GroupBy(item => item.TranslationRequestId)
            .ToDictionary(
                item => item.Key,
                item => item.OrderBy(cue => cue.Position).ToList());
        if (!cuesByRequest.TryGetValue(request.Id, out var currentCues))
        {
            return NoApproval(exception);
        }

        var currentEligibleCues = currentCues
            .Where(cue => cue.AutoApprovalEligible)
            .OrderBy(cue => cue.Position)
            .ToList();
        if (currentEligibleCues.Count < MinimumRunLength)
        {
            return NoApproval(exception);
        }

        var approvedPositionsByRequest = FindApprovals(
            request.Id,
            currentEligibleCues,
            candidateRequests,
            cuesByRequest);
        if (approvedPositionsByRequest.Count == 0 ||
            !approvedPositionsByRequest.TryGetValue(request.Id, out var currentApprovedPositions))
        {
            return NoApproval(exception);
        }

        var now = DateTime.UtcNow;
        foreach (var (requestId, approvedPositions) in approvedPositionsByRequest)
        {
            if (!cuesByRequest.TryGetValue(requestId, out var requestCues))
            {
                continue;
            }

            foreach (var run in BuildApprovedRuns(requestCues, approvedPositions))
            {
                var sequenceHash = Hash(string.Join("|", run.Select(cue => cue.TextHash)));
                foreach (var cue in run)
                {
                    cue.AutoApprovedAt ??= now;
                    cue.ApprovalSequenceHash = sequenceHash;
                }
            }

            var matchedSiblingIds = approvedPositionsByRequest.Keys
                .Where(id => id != requestId)
                .Order()
                .ToList();
            _dbContext.TranslationRequestLogs.Add(new TranslationRequestLog
            {
                TranslationRequestId = requestId,
                Level = "Information",
                Message =
                    $"Auto-approved {approvedPositions.Count} repeated source cue(s) from sibling request(s): {string.Join(", ", matchedSiblingIds)}."
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await SaveApprovedSourceTextAsync(
            candidateRequests,
            cuesByRequest,
            approvedPositionsByRequest,
            cancellationToken);

        var completedRequestIds = await CompleteFullyApprovedRequestsAsync(
            candidateRequests,
            cuesByRequest,
            approvedPositionsByRequest,
            cancellationToken);
        var currentCompleted = completedRequestIds.Contains(request.Id);

        var remainingException = BuildRemainingException(
            exception,
            currentApprovedPositions,
            currentCompleted);

        _logger.LogInformation(
            "Sibling cue approval processed request {RequestId}: approved {ApprovedCount} cue(s), completed {CompletedCount} request(s)",
            request.Id,
            currentApprovedPositions.Count,
            completedRequestIds.Count);

        return new SiblingSequenceApprovalResult
        {
            CurrentRequestCompleted = currentCompleted,
            ApprovedPositions = currentApprovedPositions,
            CompletedRequestIds = completedRequestIds,
            RemainingException = remainingException
        };
    }

    private async Task<List<TranslationRequest>> LoadCandidateRequestsAsync(
        TranslationRequest request,
        IReadOnlyCollection<int> sameSeasonEpisodeIds,
        CancellationToken cancellationToken)
    {
        var sourceSubtitleType = request.SourceSubtitleType;
        var sourceDedupeKey = request.SourceDedupeKey;

        return await _dbContext.TranslationRequests
            .Where(item =>
                item.WorkloadKind == TranslationWorkloadKind.Library &&
                item.MediaType == MediaType.Episode &&
                item.MediaId.HasValue &&
                sameSeasonEpisodeIds.Contains(item.MediaId.Value) &&
                item.SourceLanguage == request.SourceLanguage &&
                item.TargetLanguage == request.TargetLanguage &&
                item.SourceDedupeKey == sourceDedupeKey &&
                item.SourceSubtitleType == sourceSubtitleType &&
                item.Status != TranslationStatus.Pending &&
                item.Status != TranslationStatus.Cancelled &&
                item.Status != TranslationStatus.Interrupted)
            .ToListAsync(cancellationToken);
    }

    private Dictionary<int, HashSet<int>> FindApprovals(
        int currentRequestId,
        IReadOnlyList<TranslationFailedCue> currentEligibleCues,
        IReadOnlyList<TranslationRequest> candidateRequests,
        IReadOnlyDictionary<int, List<TranslationFailedCue>> cuesByRequest)
    {
        var approvedPositionsByRequest = new Dictionary<int, HashSet<int>>();
        foreach (var candidateRequest in candidateRequests.Where(item => item.Id != currentRequestId))
        {
            if (!cuesByRequest.TryGetValue(candidateRequest.Id, out var siblingCues))
            {
                continue;
            }

            var siblingEligibleCues = siblingCues
                .Where(cue => cue.AutoApprovalEligible)
                .OrderBy(cue => cue.Position)
                .ToList();
            if (siblingEligibleCues.Count < MinimumRunLength)
            {
                continue;
            }

            foreach (var match in FindMatchingRuns(currentEligibleCues, siblingEligibleCues))
            {
                AddApprovedPositions(
                    approvedPositionsByRequest,
                    currentRequestId,
                    match.CurrentRun.Select(cue => cue.Position));
                AddApprovedPositions(
                    approvedPositionsByRequest,
                    candidateRequest.Id,
                    match.SiblingRun.Select(cue => cue.Position));
            }
        }

        return approvedPositionsByRequest
            .Where(item => item.Value.Count > 0)
            .ToDictionary(item => item.Key, item => item.Value);
    }

    private static List<MatchingRun> FindMatchingRuns(
        IReadOnlyList<TranslationFailedCue> currentCues,
        IReadOnlyList<TranslationFailedCue> siblingCues)
    {
        var matches = new List<MatchingRun>();
        for (var currentIndex = 0; currentIndex <= currentCues.Count - MinimumRunLength; currentIndex++)
        {
            for (var siblingIndex = 0; siblingIndex <= siblingCues.Count - MinimumRunLength; siblingIndex++)
            {
                var length = 0;
                while (currentIndex + length < currentCues.Count &&
                       siblingIndex + length < siblingCues.Count &&
                       string.Equals(
                           currentCues[currentIndex + length].TextHash,
                           siblingCues[siblingIndex + length].TextHash,
                           StringComparison.Ordinal))
                {
                    length++;
                }

                if (length >= MinimumRunLength)
                {
                    matches.Add(new MatchingRun(
                        currentCues.Skip(currentIndex).Take(length).ToList(),
                        siblingCues.Skip(siblingIndex).Take(length).ToList()));
                }
            }
        }

        return matches;
    }

    private static void AddApprovedPositions(
        Dictionary<int, HashSet<int>> approvedPositionsByRequest,
        int requestId,
        IEnumerable<int> positions)
    {
        if (!approvedPositionsByRequest.TryGetValue(requestId, out var approvedPositions))
        {
            approvedPositions = [];
            approvedPositionsByRequest[requestId] = approvedPositions;
        }

        foreach (var position in positions)
        {
            approvedPositions.Add(position);
        }
    }

    private static List<List<TranslationFailedCue>> BuildApprovedRuns(
        IReadOnlyList<TranslationFailedCue> requestCues,
        IReadOnlySet<int> approvedPositions)
    {
        var runs = new List<List<TranslationFailedCue>>();
        var currentRun = new List<TranslationFailedCue>();
        foreach (var cue in requestCues.OrderBy(item => item.Position))
        {
            if (approvedPositions.Contains(cue.Position))
            {
                currentRun.Add(cue);
                continue;
            }

            if (currentRun.Count > 0)
            {
                runs.Add(currentRun);
                currentRun = [];
            }
        }

        if (currentRun.Count > 0)
        {
            runs.Add(currentRun);
        }

        return runs;
    }

    private async Task SaveApprovedSourceTextAsync(
        IReadOnlyList<TranslationRequest> candidateRequests,
        IReadOnlyDictionary<int, List<TranslationFailedCue>> cuesByRequest,
        IReadOnlyDictionary<int, HashSet<int>> approvedPositionsByRequest,
        CancellationToken cancellationToken)
    {
        foreach (var (requestId, approvedPositions) in approvedPositionsByRequest)
        {
            var request = candidateRequests.FirstOrDefault(item => item.Id == requestId);
            if (request == null ||
                !cuesByRequest.TryGetValue(requestId, out var cues))
            {
                continue;
            }

            var sourceFingerprint = await GetCheckpointFingerprintAsync(request, cancellationToken);
            var checkpoint = await _checkpointService.LoadByRequestIdAsync(
                requestId,
                cancellationToken) ?? new TranslationCheckpoint
                {
                    TranslationRequestId = requestId,
                    SourceFingerprint = sourceFingerprint
                };

            if (!string.Equals(
                    checkpoint.SourceFingerprint,
                    sourceFingerprint,
                    StringComparison.Ordinal))
            {
                checkpoint = new TranslationCheckpoint
                {
                    TranslationRequestId = requestId,
                    SourceFingerprint = sourceFingerprint
                };
            }

            foreach (var cue in cues.Where(item => approvedPositions.Contains(item.Position)))
            {
                checkpoint.Translations[cue.Position] = cue.SourceText;
                checkpoint.SourcePreservedPositions.Add(cue.Position);
            }

            await _checkpointService.SaveCheckpointAsync(
                checkpoint,
                cancellationToken,
                request.JobId);
        }
    }

    private async Task<List<int>> CompleteFullyApprovedRequestsAsync(
        IReadOnlyList<TranslationRequest> candidateRequests,
        IReadOnlyDictionary<int, List<TranslationFailedCue>> cuesByRequest,
        IReadOnlyDictionary<int, HashSet<int>> approvedPositionsByRequest,
        CancellationToken cancellationToken)
    {
        var completedRequestIds = new List<int>();
        foreach (var request in candidateRequests.Where(item => approvedPositionsByRequest.ContainsKey(item.Id)))
        {
            if (!cuesByRequest.TryGetValue(request.Id, out var requestCues) ||
                requestCues.Count == 0 ||
                requestCues.Any(cue => cue.AutoApprovedAt == null))
            {
                continue;
            }

            var sourceTextPositions = requestCues
                .Where(cue => cue.AutoApprovedAt != null)
                .Select(cue => cue.Position)
                .ToHashSet();
            var completion = await _completionService.CompleteAsync(
                request,
                new Dictionary<int, string>(),
                sourceTextPositions,
                $"Auto-completed after approving {sourceTextPositions.Count} repeated source cue(s) from sibling sequence match.",
                cancellationToken);
            if (completion.Completed)
            {
                completedRequestIds.Add(request.Id);
            }
        }

        return completedRequestIds.Order().ToList();
    }

    private static MissingTranslationException? BuildRemainingException(
        MissingTranslationException exception,
        IReadOnlySet<int> approvedPositions,
        bool currentCompleted)
    {
        if (currentCompleted)
        {
            return null;
        }

        var remainingCues = exception.MissingCues
            .Where(cue => !approvedPositions.Contains(cue.Position))
            .ToList();
        if (remainingCues.Count == 0)
        {
            return null;
        }

        if (remainingCues.Count == exception.MissingCues.Count)
        {
            return exception;
        }

        return new MissingTranslationException(remainingCues);
    }

    private async Task UpsertMissingCuesAsync(
        int requestId,
        IReadOnlyList<MissingTranslationCue> missingCues,
        string sourceFingerprint,
        string? ownershipToken,
        CancellationToken cancellationToken)
    {
        try
        {
            await UpsertMissingCuesOnceAsync(
                requestId,
                missingCues,
                sourceFingerprint,
                ownershipToken,
                cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            await UpsertMissingCuesOnceAsync(
                requestId,
                missingCues,
                sourceFingerprint,
                ownershipToken,
                cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task UpsertMissingCuesOnceAsync(
        int requestId,
        IReadOnlyList<MissingTranslationCue> missingCues,
        string sourceFingerprint,
        string? ownershipToken,
        CancellationToken cancellationToken)
    {
        var checkpoint = await _checkpointService.LoadByRequestIdAsync(requestId, cancellationToken);
        var sourcePreservedPositionsChanged = false;
        if (checkpoint != null &&
            !string.Equals(
                checkpoint.SourceFingerprint,
                sourceFingerprint,
                StringComparison.Ordinal))
        {
            checkpoint = new TranslationCheckpoint
            {
                TranslationRequestId = requestId,
                SourceFingerprint = sourceFingerprint
            };
            sourcePreservedPositionsChanged = true;
        }

        if (checkpoint != null)
        {
            foreach (var position in missingCues.Select(cue => cue.Position))
            {
                sourcePreservedPositionsChanged |= checkpoint.SourcePreservedPositions.Remove(position);
            }
        }

        var positions = missingCues.Select(item => item.Position).ToList();
        var existingCues = await _dbContext.TranslationFailedCues
            .Where(item => item.TranslationRequestId == requestId && positions.Contains(item.Position))
            .ToDictionaryAsync(item => item.Position, cancellationToken);

        foreach (var missingCue in missingCues)
        {
            var normalizedText = Normalize(missingCue.SourceText);
            var textHash = Hash(normalizedText);
            if (existingCues.TryGetValue(missingCue.Position, out var existingCue))
            {
                existingCue.SourceText = missingCue.SourceText;
                existingCue.NormalizedText = normalizedText;
                existingCue.TextHash = textHash;
                existingCue.AutoApprovalEligible = missingCue.AutoApprovalEligible;
                existingCue.AutoApprovedAt = null;
                existingCue.ApprovalSequenceHash = null;
                continue;
            }

            _dbContext.TranslationFailedCues.Add(new TranslationFailedCue
            {
                TranslationRequestId = requestId,
                Position = missingCue.Position,
                SourceText = missingCue.SourceText,
                NormalizedText = normalizedText,
                TextHash = textHash,
                AutoApprovalEligible = missingCue.AutoApprovalEligible
            });
        }

        if (sourcePreservedPositionsChanged)
        {
            checkpoint!.UpdatedAtUtc = DateTime.UtcNow;
            await _checkpointService.SaveCheckpointAsync(
                checkpoint,
                cancellationToken,
                ownershipToken);
        }
    }

    private static bool IsLibraryEpisodeRequest(TranslationRequest request)
    {
        return request.WorkloadKind == TranslationWorkloadKind.Library &&
               request.MediaType == MediaType.Episode &&
               request.MediaId.HasValue;
    }

    private static SiblingSequenceApprovalResult NoApproval(MissingTranslationException exception)
    {
        return new SiblingSequenceApprovalResult
        {
            CurrentRequestCompleted = false,
            ApprovedPositions = new HashSet<int>(),
            CompletedRequestIds = [],
            RemainingException = exception
        };
    }

    private static string Normalize(string text)
    {
        return Regex.Replace(text.Trim().ToLowerInvariant(), "\\s+", " ");
    }

    private static string Hash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static async Task<string> GetCheckpointFingerprintAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        var sourceIdentity = TranslationCheckpointService.GetFallbackCheckpointFingerprint(request);
        if (string.IsNullOrWhiteSpace(request.SubtitleToTranslate))
        {
            return sourceIdentity;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = new FileStream(
                request.SubtitleToTranslate,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);
            var contentHash = await SHA256.HashDataAsync(stream, cancellationToken);
            return TranslationCheckpointService.BuildCheckpointFingerprint(
                request,
                Convert.ToHexString(contentHash));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return sourceIdentity;
        }
    }

    private sealed record MatchingRun(
        IReadOnlyList<TranslationFailedCue> CurrentRun,
        IReadOnlyList<TranslationFailedCue> SiblingRun);
}
