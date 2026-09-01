namespace JobFlow.Core;

public sealed record JobDetails(
    Guid Id,
    string JobType,
    JobStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset NextRunAt,
    int RetryCount,
    int MaxRetries,
    string? CurrentWorkerId,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? LeaseExpiresAt,
    IReadOnlyList<JobAttemptDetails> Attempts);