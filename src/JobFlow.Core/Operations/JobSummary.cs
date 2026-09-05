namespace JobFlow.Core;

public sealed record JobSummary(
    Guid Id,
    string JobType,
    JobStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset NextRunAt,
    int RetryCount,
    int MaxAttempts,
    string? LastWorkerId,
    DateTimeOffset? LastAttemptAt);
