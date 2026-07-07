namespace Lingarr.Server.Exceptions;

public class ProviderPauseException : TranslationException
{
    public ProviderPauseException(
        string provider,
        string reason,
        DateTime? resumeAt = null,
        Exception? exception = null)
        : base(reason, exception)
    {
        Provider = provider;
        Reason = reason;
        ResumeAt = resumeAt;
    }

    public string Provider { get; }
    public string Reason { get; }
    public DateTime? ResumeAt { get; }
}
