using Hangfire;
using Lingarr.Core.Enum;
using Lingarr.Server.Jobs;

namespace Lingarr.Server.Services.Subtitle;

internal static class SubtitleOcrJobActivity
{
    private static readonly string[] Queues = ["system", "default", "movies", "shows", "webhook"];

    public static bool HasActiveJob(int mediaId, MediaType mediaType, int streamIndex)
    {
        try
        {
            var storage = JobStorage.Current;
            if (storage == null)
            {
                return false;
            }

            var monitoringApi = storage.GetMonitoringApi();
            if (monitoringApi.ProcessingJobs(0, 1000)
                .Any(job => IsMatchingOcrJob(job.Value?.Job, mediaId, mediaType, streamIndex)))
            {
                return true;
            }

            foreach (var queue in Queues)
            {
                if (monitoringApi.EnqueuedJobs(queue, 0, 1000)
                    .Any(job => IsMatchingOcrJob(job.Value?.Job, mediaId, mediaType, streamIndex)))
                {
                    return true;
                }
            }

            return monitoringApi.ScheduledJobs(0, 1000)
                .Any(job => IsMatchingOcrJob(job.Value?.Job, mediaId, mediaType, streamIndex));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsMatchingOcrJob(
        Hangfire.Common.Job? job,
        int mediaId,
        MediaType mediaType,
        int streamIndex)
    {
        if (job?.Type != typeof(SubtitleOcrJob) ||
            job.Method.Name != nameof(SubtitleOcrJob.Execute) ||
            job.Args.Count < 3)
        {
            return false;
        }

        return TryConvertToInt(job.Args[0], out var jobMediaId) &&
               TryConvertToInt(job.Args[1], out var jobMediaType) &&
               TryConvertToInt(job.Args[2], out var jobStreamIndex) &&
               jobMediaId == mediaId &&
               jobMediaType == (int)mediaType &&
               jobStreamIndex == streamIndex;
    }

    private static bool TryConvertToInt(object? value, out int result)
    {
        try
        {
            result = Convert.ToInt32(value);
            return true;
        }
        catch
        {
            result = 0;
            return false;
        }
    }
}
