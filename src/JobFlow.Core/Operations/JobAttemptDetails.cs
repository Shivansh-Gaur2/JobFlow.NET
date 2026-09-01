namespace JobFlow.Core;

public sealed record JobAttemptDetails(
    Guid Id,
    int AttemptNumber,
    string WorkerId,
    JobAttemptStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    Guid? ErrorId,
    string? FailureType,
    string? FailureMessage);