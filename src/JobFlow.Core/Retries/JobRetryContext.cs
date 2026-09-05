namespace JobFlow.Core;

public sealed class JobRetryContext
{
    public JobRetryContext(
        Guid jobId,
        string jobType,
        int attemptNumber,
        int maxAttempts,
        JobFailure failure)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("Job ID must not be empty.", nameof(jobId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(jobType);

        if (attemptNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), "Attempt number must be at least one.");
        }

        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Maximum attempts must be at least one.");
        }

        ArgumentNullException.ThrowIfNull(failure);

        JobId = jobId;
        JobType = jobType;
        AttemptNumber = attemptNumber;
        MaxAttempts = maxAttempts;
        Failure = failure;
    }

    public Guid JobId { get; }

    public string JobType { get; }

    public int AttemptNumber { get; }

    public int MaxAttempts { get; }

    public JobFailure Failure { get; }
}
