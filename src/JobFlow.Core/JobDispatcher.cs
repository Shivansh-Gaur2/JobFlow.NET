using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace JobFlow.Core;

public class JobDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IJobStore _store;
    private readonly string _workerId = Guid.NewGuid().ToString();
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    public JobDispatcher(IServiceScopeFactory scopeFactory, IJobStore jobStore)
    {
        _scopeFactory = scopeFactory;
        _store = jobStore;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var job = await _store.ClaimNextJobAsync(_workerId, stoppingToken);

            if(job is not null)
            {
                await RunJobAsync(job, stoppingToken);

            }
            else
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
        }

    }
    private async Task RunJobAsync(JobRecord job, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();

        try
        {
            var jobType = Type.GetType(job.JobType) ?? throw new InvalidDataException($"Unkown job type '{job.JobType}'");

            var jobInstance = (IJob)scope.ServiceProvider.GetRequiredService(jobType);

            await jobInstance.ExecuteAsync(job.Payload, ct);
            await _store.MarkCompletedAsync(job.Id, ct);
        }
        catch(Exception)
        {
            await HandleFailureAsync(job, ct);
        }
    } 

    private async Task HandleFailureAsync(JobRecord job, CancellationToken ct)
    {
        var newRetryCount = job.RetryCount + 1;

        if(newRetryCount >= job.MaxRetries)
        {
            await _store.MarkFailedAsync(job.Id, newRetryCount, nextRunAt : null, ct);
        }
        else
        {
            var backoff = TimeSpan.FromSeconds(Math.Pow(2, newRetryCount));
            await _store.MarkFailedAsync(job.Id, newRetryCount, DateTimeOffset.UtcNow.Add(backoff), ct);
        }
    }
}