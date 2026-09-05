namespace JobFlow.Core;

public sealed class JobRetryDecision
{
    private JobRetryDecision(TimeSpan? retryDelay)
    {
        RetryDelay = retryDelay;
    }

    public TimeSpan? RetryDelay { get; }

    public bool IsTerminal => RetryDelay is null;

    public static JobRetryDecision RetryAfter(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), "Retry delay must be greater than zero.");
        }

        return new JobRetryDecision(delay);
    }

    public static JobRetryDecision Stop() => new(null);
}
