namespace JobFlow.Core;

public sealed class JobRetryOptions
{
    public int MaxAttempts { get; set; } = 3;

    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(5);

    public double JitterFactor { get; set; } = 0.20;

    public void Validate()
    {
        if (MaxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxAttempts), "Maximum attempts must be at least one.");
        }

        if (BaseDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(BaseDelay), "Base delay must be greater than zero.");
        }

        if (MaxDelay < BaseDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDelay), "Maximum delay must not be shorter than the base delay.");
        }

        if (JitterFactor is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(JitterFactor), "Jitter factor must be between zero and one.");
        }
    }
}
