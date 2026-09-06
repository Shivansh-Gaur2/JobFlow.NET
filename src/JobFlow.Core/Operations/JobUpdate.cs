namespace JobFlow.Core;

public sealed record JobUpdate(Guid Id, Guid JobId, DateTimeOffset OccurredAt);
