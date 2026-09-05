namespace JobFlow.Core;

public sealed class ExponentialBackoffRetryPolicy : IJobRetryPolicy
{
    private readonly JobRetryOptions _options;

    public ExponentialBackoffRetryPolicy(JobRetryOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public JobRetryDecision Decide(JobRetryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Failure.Disposition == JobFailureDisposition.NonRetryable
            || context.AttemptNumber >= context.MaxAttempts)
        {
            return JobRetryDecision.Stop();
        }

        var multiplier = Math.Pow(2, Math.Min(context.AttemptNumber - 1, 62));
        var cappedMilliseconds = Math.Min(
            _options.BaseDelay.TotalMilliseconds * multiplier,
            _options.MaxDelay.TotalMilliseconds);
        var jitter = 1 + ((Random.Shared.NextDouble() * 2 - 1) * _options.JitterFactor);

        return JobRetryDecision.RetryAfter(TimeSpan.FromMilliseconds(
            Math.Min(cappedMilliseconds * jitter, _options.MaxDelay.TotalMilliseconds)));
    }
}
