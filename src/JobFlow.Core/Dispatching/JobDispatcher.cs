using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JobFlow.Core;

public sealed class JobDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IJobStore _store;
    private readonly string _workerId = Guid.NewGuid().ToString();
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private readonly JobLeaseOptions _leaseOptions;
    private readonly IJobFailureClassifier _jobFailureClassifier;
    private readonly IJobRetryPolicy _jobRetryPolicy;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JobDispatcher> _logger;

    public JobDispatcher(
        IServiceScopeFactory scopeFactory,
        IJobStore jobStore,
        IJobFailureClassifier jobFailureClassifier,
        IJobRetryPolicy jobRetryPolicy,
        TimeProvider timeProvider,
        ILogger<JobDispatcher> logger,
        JobLeaseOptions? leaseOptions = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _store = jobStore ?? throw new ArgumentNullException(nameof(jobStore));
        _leaseOptions = leaseOptions ?? new JobLeaseOptions();
        _leaseOptions.Validate();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jobFailureClassifier = jobFailureClassifier
            ?? throw new ArgumentNullException(nameof(jobFailureClassifier));
        _jobRetryPolicy = jobRetryPolicy ?? throw new ArgumentNullException(nameof(jobRetryPolicy));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            JobLease? lease;

            try
            {
                lease = await _store.ClaimNextJobAsync(_workerId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Job worker {WorkerId} could not claim the next job. It will retry after the poll interval.",
                    _workerId);

                await Task.Delay(PollInterval, stoppingToken);
                continue;
            }

            if (lease is not null)
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

            var completed = await _store.MarkCompletedAsync(jobLease, ct);

            if (!completed)
            {
                _logger.LogWarning(
                    "Job {JobId} finished on worker {WorkerId}, but its lease was no longer valid. Another worker may own it now.",
                    jobLease.Job.Id,
                    _workerId);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The application is shutting down. Do not mark the job as failed.
        }
        catch (OperationCanceledException) when (executionCancellation.IsCancellationRequested)
        {
            // The lease was lost. This worker must not change the job anymore.
        }
        catch (Exception exception)
        {
            await HandleFailureSafelyAsync(jobLease, exception, ct);
        }
        finally
        {
            renewalCancellation.Cancel();
            await renewalTask;
        }
    }

    private async Task RenewLeaseUntilCancelledAsync(
        JobLease lease,
        CancellationTokenSource executionCancellation,
        CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_leaseOptions.RenewalInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var renewedLease = await _store.RenewLeaseAsync(lease, ct);

                if (renewedLease is null)
                {
                    executionCancellation.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Worker {WorkerId} could not renew the lease for job {JobId}. The job execution was cancelled to protect ownership.",
                _workerId,
                lease.Job.Id);
            executionCancellation.Cancel();
        }
    }

    private async Task HandleFailureSafelyAsync(
        JobLease lease,
        Exception exception,
        CancellationToken ct)
    {
        try
        {
            var job = lease.Job;
            var attemptNumber = job.RetryCount + 1;
            var failure = _jobFailureClassifier.Classify(exception, Guid.NewGuid());
            var decision = _jobRetryPolicy.Decide(new JobRetryContext(
                job.Id,
                job.JobType,
                attemptNumber,
                job.MaxAttempts,
                failure));

            _logger.LogError(
                exception,
                "Job execution failed. ErrorId: {ErrorId}; JobId: {JobId}; WorkerId: {WorkerId}",
                failure.ErrorId,
                lease.Job.Id,
                _workerId);

            var nextRunAt = decision.RetryDelay is { } retryDelay
                ? _timeProvider.GetUtcNow().Add(retryDelay)
                : (DateTimeOffset?)null;
            var markedFailed = await _store.MarkFailedAsync(
                lease,
                failure,
                attemptNumber,
                nextRunAt,
                ct);

            if (!markedFailed)
            {
                _logger.LogWarning(
                    "Job {JobId} failed on worker {WorkerId}, but its lease was no longer valid. Another worker may own it now.",
                    lease.Job.Id,
                    _workerId);
            }
        }
        catch (Exception persistenceException)
        {
            _logger.LogError(
                persistenceException,
                "Worker {WorkerId} could not record the failure for job {JobId}. The lease will expire and the job can be recovered.",
                _workerId,
                lease.Job.Id);
        }
    }
}
