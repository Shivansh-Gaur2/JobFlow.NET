namespace JobFlow.Core;

public sealed class JobScheduler
{
    private readonly IJobStore _store;
    public JobScheduler(IJobStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<Guid> EnqueueAsync<TJob>(string? payload = null, CancellationToken ct = default) where TJob : IJob
    {
        return _store.EnqueueAsync(typeof(TJob).AssemblyQualifiedName!, payload, DateTimeOffset.UtcNow, ct);
    }

    public Task<Guid> ScheduleAsync<TJob>(TimeSpan delay, string? payload = null, CancellationToken ct = default) where TJob : IJob
    {
        return _store.EnqueueAsync(typeof(TJob).AssemblyQualifiedName!, payload, DateTimeOffset.UtcNow.Add(delay), ct);
    }
}
