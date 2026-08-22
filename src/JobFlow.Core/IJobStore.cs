namespace JobFlow.Core;

public interface IJobStore
{
    Task<Guid> EnqueueAsync(string jobType, string? payload, DateTimeOffset nextRunAt, CancellationToken ct);
    Task<JobRecord?> ClaimNextJobAsync(string workerId, CancellationToken ct);
    Task MarkCompletedAsync(Guid jobId, CancellationToken ct);
    Task MarkFailedAsync(Guid jobId, int nexRetryCount, DateTimeOffset? nextRunAt, CancellationToken ct);
}