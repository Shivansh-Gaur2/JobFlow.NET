using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace JobFlow.Core;

public class JobDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IJobStore _store;
    private readonly string _workerId = Guid.NewGuid().ToString();
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private readonly JobLeaseOptions _leaseOptions;

    public JobDispatcher(
        IServiceScopeFactory scopeFactory,
        IJobStore jobStore,
        JobLeaseOptions? leaseOptions = null)
    {
        _scopeFactory = scopeFactory;
        _store = jobStore;
        _leaseOptions = leaseOptions ?? new JobLeaseOptions();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var lease = await _store.ClaimNextJobAsync(_workerId, stoppingToken);

            if(lease is not null)
            {
                await RunJobAsync(lease, stoppingToken);

            }
            else
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
        }

    }
private async Task RunJobAsync(JobLease jobLease, CancellationToken ct)
{
    using var scope = _scopeFactory.CreateScope();
    using var executionCancellation =
        CancellationTokenSource.CreateLinkedTokenSource(ct);
    using var renewalCancellation =
        CancellationTokenSource.CreateLinkedTokenSource(ct);

    var renewalTask = RenewLeaseUntilCancelledAsync(
        jobLease,
        executionCancellation,
        renewalCancellation.Token);

    var job = jobLease.Job;

    try
    {
        var jobType = Type.GetType(job.JobType)
            ?? throw new InvalidDataException(
                $"Unknown job type '{job.JobType}'");

        var jobInstance = (IJob)scope.ServiceProvider
            .GetRequiredService(jobType);

        await jobInstance.ExecuteAsync(
            job.Payload,
            executionCancellation.Token);

        await _store.MarkCompletedAsync(jobLease, ct);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        // The application is shutting down. Do not mark the job as failed.
    }
    catch (OperationCanceledException) when (executionCancellation.IsCancellationRequested)
    {
        // The lease was lost. Worker A must not change this job anymore.
    }
    catch (Exception)
    {
        await HandleFailureAsync(jobLease, ct);
    }
    finally
    {
        renewalCancellation.Cancel();
        await renewalTask;
    }
}
    private async Task RenewLeaseUntilCancelledAsync(JobLease lease, CancellationTokenSource executionCancellation, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_leaseOptions.RenewalInterval);

        try
        {
            while(await timer.WaitForNextTickAsync(ct))
            {
                var renewedLease = await _store.RenewLeaseAsync(lease, ct);
                if(renewedLease is null)
                {
                    executionCancellation.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private async Task HandleFailureAsync(JobLease lease, CancellationToken ct)
    {
        var job = lease.Job;
        var newRetryCount = job.RetryCount + 1;

        if(newRetryCount >= job.MaxRetries)
        {
            await _store.MarkFailedAsync(lease, newRetryCount, nextRunAt : null, ct);
        }
        else
        {
            var backoff = TimeSpan.FromSeconds(Math.Pow(2, newRetryCount));
            await _store.MarkFailedAsync(lease, newRetryCount, DateTimeOffset.UtcNow.Add(backoff), ct);
        }
    }
}
