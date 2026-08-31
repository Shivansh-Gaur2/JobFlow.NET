namespace JobFlow.Core;

public interface IJobStore
{
    Task<Guid> EnqueueAsync(string jobType, string? payload, DateTimeOffset nextRunAt, CancellationToken ct);
    Task<JobLease?> ClaimNextJobAsync(string workerId, CancellationToken ct);
    Task<bool> MarkCompletedAsync(JobLease lease, CancellationToken ct);
    Task<bool> MarkFailedAsync(JobLease lease, JobFailure failure, int newRetryCount, DateTimeOffset? nextRunAt, CancellationToken ct);
    Task<JobLease?> RenewLeaseAsync(JobLease lease, CancellationToken ct);
}
