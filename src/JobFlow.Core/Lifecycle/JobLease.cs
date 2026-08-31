namespace JobFlow.Core;
public sealed record JobLease(
    JobRecord Job,
    Guid Token,
    DateTimeOffset ExpiresAt);
