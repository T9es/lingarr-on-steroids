using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;

namespace Lingarr.Server.Services.Translation;

public static class TranslationRequestQueueOrdering
{
    public const string QueueSort = "Queue";

    public static IQueryable<TranslationRequest> OrderByEffectiveQueuePriority(
        this IQueryable<TranslationRequest> query,
        LingarrDbContext dbContext)
    {
        return query
            .Select(request => new
            {
                Request = request,
                EffectivePriority =
                    (request.WorkloadKind == TranslationWorkloadKind.Library &&
                     request.MediaType == MediaType.Movie &&
                     dbContext.Movies.Any(movie =>
                         movie.Id == request.MediaId &&
                         movie.IsPriority)) ||
                    (request.WorkloadKind == TranslationWorkloadKind.Library &&
                     request.MediaType == MediaType.Episode &&
                     dbContext.Episodes.Any(episode =>
                         episode.Id == request.MediaId &&
                         episode.Season.Show.IsPriority)) ||
                    (request.WorkloadKind == TranslationWorkloadKind.CustomSource &&
                     dbContext.CustomMediaItems.Any(item =>
                         item.Id == request.CustomMediaItemId &&
                         item.IsPriority)),
                PriorityDate =
                    request.WorkloadKind == TranslationWorkloadKind.Library &&
                    request.MediaType == MediaType.Movie
                        ? dbContext.Movies
                            .Where(movie =>
                                movie.Id == request.MediaId &&
                                movie.IsPriority)
                            .Select(movie => movie.PriorityDate)
                            .FirstOrDefault()
                        : request.WorkloadKind == TranslationWorkloadKind.Library &&
                          request.MediaType == MediaType.Episode
                            ? dbContext.Episodes
                                .Where(episode =>
                                    episode.Id == request.MediaId &&
                                    episode.Season.Show.IsPriority)
                                .Select(episode => episode.Season.Show.PriorityDate)
                                .FirstOrDefault()
                            : request.WorkloadKind == TranslationWorkloadKind.CustomSource
                                ? dbContext.CustomMediaItems
                                    .Where(item =>
                                        item.Id == request.CustomMediaItemId &&
                                        item.IsPriority)
                                    .Select(item => item.PriorityDate)
                                    .FirstOrDefault()
                                : null,
                request.CreatedAt,
                request.Id
            })
            .OrderByDescending(sort => sort.EffectivePriority)
            .ThenByDescending(sort => sort.PriorityDate)
            .ThenBy(sort => sort.CreatedAt)
            .ThenBy(sort => sort.Id)
            .Select(sort => sort.Request);
    }
}
