using JobFlow.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace JobFlow.SqlServer.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class SqlJobStoreTests : IAsyncLifetime
{
    private readonly SqlServerTestDatabase _database;

    public SqlJobStoreTests(SqlServerTestDatabase database)
    {
        _database = database;
    }

    public Task InitializeAsync() => _database.ResetAsync();

    public Task DisposeAsync()
    {
        _database.ReleaseTestLock();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ClaimNextJobAsync_claims_a_ready_job_for_the_worker()
    {
        var store = CreateStore();
        var jobId = await store.EnqueueAsync(
            "EmailJob",
            "{\"to\":\"customer@example.test\"}",
            ReadyToRun(),
            CancellationToken.None);

        var claimedJob = await store.ClaimNextJobAsync("worker-a", CancellationToken.None);

        Assert.NotNull(claimedJob);
        Assert.Equal(jobId, claimedJob.Job.Id);
        Assert.Equal(JobStatus.InProgress, claimedJob.Job.JobStatus);
        Assert.Equal("worker-a", claimedJob.Job.LockedBy);
        Assert.NotNull(claimedJob.Job.LockedAt);
        Assert.NotEqual(Guid.Empty, claimedJob.Token);
        Assert.True(claimedJob.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ClaimNextJobAsync_allows_only_one_worker_to_claim_a_job()
    {
        var store = CreateStore();
        await store.EnqueueAsync("EmailJob", null, ReadyToRun(), CancellationToken.None);

        var firstWorker = CreateStore();
        var secondWorker = CreateStore();

        var claims = await Task.WhenAll(
            firstWorker.ClaimNextJobAsync("worker-a", CancellationToken.None),
            secondWorker.ClaimNextJobAsync("worker-b", CancellationToken.None));

        Assert.Equal(1, claims.Count(job => job is not null));
    }

    [Fact]
    public async Task ClaimNextJobAsync_reclaims_an_expired_lease_with_a_new_token()
    {
        var store = CreateStore();
        var jobId = await store.EnqueueAsync("EmailJob", null, ReadyToRun(), CancellationToken.None);

        var workerALease = await store.ClaimNextJobAsync("worker-a", CancellationToken.None);
        Assert.NotNull(workerALease);

        await _database.ExpireLeaseAsync(jobId);

        var workerBLease = await store.ClaimNextJobAsync("worker-b", CancellationToken.None);

        Assert.NotNull(workerBLease);
        Assert.Equal(jobId, workerBLease.Job.Id);
        Assert.Equal("worker-b", workerBLease.Job.LockedBy);
        Assert.NotEqual(workerALease.Token, workerBLease.Token);
    }

    [Fact]
    public async Task ClaimNextJobAsync_creates_an_attempt_for_the_claiming_worker()
    {
        var store = CreateStore();
        var jobId = await store.EnqueueAsync("EmailJob", null, ReadyToRun(), CancellationToken.None);

        var lease = await store.ClaimNextJobAsync("worker-a", CancellationToken.None);
        var attemptCount = await _database.GetAttemptCountAsync(jobId, "worker-a");

        Assert.NotNull(lease);
        Assert.Equal(1, attemptCount);
    }

    [Fact]
    public async Task ClaimNextJobAsync_abandons_the_expired_attempt_and_creates_a_new_one()
    {
        var store = CreateStore();
        var jobId = await store.EnqueueAsync("EmailJob", null, ReadyToRun(), CancellationToken.None);

        var workerALease = await store.ClaimNextJobAsync("worker-a", CancellationToken.None);
        Assert.NotNull(workerALease);

        await _database.ExpireLeaseAsync(jobId);

        var workerBLease = await store.ClaimNextJobAsync("worker-b", CancellationToken.None);
        var attempts = await _database.GetAttemptsAsync(jobId);

        Assert.NotNull(workerBLease);
        Assert.Collection(
            attempts,
            attempt =>
            {
                Assert.Equal(1, attempt.AttemptNumber);
                Assert.Equal("worker-a", attempt.WorkerId);
                Assert.Equal("Abandoned", attempt.Status);
            },
            attempt =>
            {
                Assert.Equal(2, attempt.AttemptNumber);
                Assert.Equal("worker-b", attempt.WorkerId);
                Assert.Equal("Running", attempt.Status);
            });
    }

    [Fact]
    public async Task UseSqlServerJobStore_uses_the_configured_lease_duration()
    {
        var services = new ServiceCollection();
        services.UseSqlServerJobStore(
            _database.ConnectionString,
            options => options.LeaseDuration = TimeSpan.FromMinutes(10));

        using var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<IJobStore>();
        var claimStartedAt = DateTimeOffset.UtcNow;

        await store.EnqueueAsync("EmailJob", null, claimStartedAt.AddMinutes(-1), CancellationToken.None);
        var lease = await store.ClaimNextJobAsync("worker-a", CancellationToken.None);

        Assert.NotNull(lease);
        Assert.True(lease.ExpiresAt >= claimStartedAt.AddMinutes(9).AddSeconds(59));
    }

    [Fact]
    public void UseSqlServerJobStore_rejects_a_renewal_interval_that_is_not_shorter_than_the_lease()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.UseSqlServerJobStore(
                _database.ConnectionString,
                options =>
                {
                    options.LeaseDuration = TimeSpan.FromMinutes(1);
                    options.RenewalInterval = TimeSpan.FromMinutes(1);
                }));

        Assert.Equal(nameof(JobLeaseOptions.RenewalInterval), exception.ParamName);
    }

    [Fact]
    public void DefaultJobFailureClassifier_redacts_the_raw_exception_message()
    {
        var classifier = new DefaultJobFailureClassifier();
        var failure = classifier.Classify(
            new InvalidOperationException("Customer password is hunter2."),
            Guid.NewGuid());

        Assert.Equal("InvalidOperationException", failure.FailureType);
        Assert.Equal("Job execution failed. See ErrorId for details.", failure.SafeMessage);
        Assert.DoesNotContain("hunter2", failure.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void UseSqlServerJobStore_preserves_a_host_provided_failure_classifier()
    {
        var services = new ServiceCollection();
        var classifier = new TestFailureClassifier();
        services.AddSingleton<IJobFailureClassifier>(classifier);

        services.UseSqlServerJobStore(_database.ConnectionString);

        using var serviceProvider = services.BuildServiceProvider();

        Assert.Same(classifier, serviceProvider.GetRequiredService<IJobFailureClassifier>());
    }

    [Fact]
    public async Task ClaimNextJobAsync_claims_the_oldest_ready_job_first()
    {
        var store = CreateStore();
        var oldestJobId = await store.EnqueueAsync(
            "OldestJob",
            null,
            DateTimeOffset.UtcNow.AddMinutes(-2),
            CancellationToken.None);
        await store.EnqueueAsync(
            "NewerJob",
            null,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            CancellationToken.None);

        var lease = await store.ClaimNextJobAsync("worker-a", CancellationToken.None);

        Assert.NotNull(lease);
        Assert.Equal(oldestJobId, lease.Job.Id);
    }

    [Fact]
    public async Task ApplyJobFlowSqlServerMigrationsAsync_upgrades_a_legacy_schema_once()
    {
        var databaseName = $"JobFlowMigration{Guid.NewGuid():N}";
        var connectionString = await _database.CreateDatabaseAsync(databaseName);
        await SqlServerTestDatabase.ApplyLegacySchemaAsync(connectionString);

        Assert.False(await SqlServerTestDatabase.HasColumnAsync(connectionString, "dbo.JobAttempts", "ErrorId"));

        var services = new ServiceCollection();
        services.UseSqlServerJobStore(connectionString);
        using var serviceProvider = services.BuildServiceProvider();

        await serviceProvider.ApplyJobFlowSqlServerMigrationsAsync();
        await serviceProvider.ApplyJobFlowSqlServerMigrationsAsync();

        Assert.True(await SqlServerTestDatabase.HasColumnAsync(connectionString, "dbo.JobAttempts", "ErrorId"));
        Assert.True(await SqlServerTestDatabase.HasColumnAsync(connectionString, "dbo.JobAttempts", "FailureType"));
        Assert.True(await SqlServerTestDatabase.HasColumnAsync(connectionString, "dbo.JobAttempts", "FailureMessage"));
        Assert.Equal([1, 2, 3], await SqlServerTestDatabase.GetAppliedMigrationVersionsAsync(connectionString));
    }

    [Fact]
    public async Task RenewLeaseAsync_extends_the_current_workers_lease()
    {
        var store = CreateStore();
        await store.EnqueueAsync("EmailJob", null, ReadyToRun(), CancellationToken.None);

        var lease = await store.ClaimNextJobAsync("worker-a", CancellationToken.None);
        Assert.NotNull(lease);

        var renewedLease = await store.RenewLeaseAsync(lease, CancellationToken.None);

        Assert.NotNull(renewedLease);
        Assert.Equal(lease.Job.Id, renewedLease.Job.Id);
        Assert.Equal(lease.Token, renewedLease.Token);
        Assert.True(renewedLease.ExpiresAt > lease.ExpiresAt);
    }

    [Fact]
    public async Task RenewLeaseAsync_rejects_an_expired_lease()
    {
        var store = CreateStore();
        var jobId = await store.EnqueueAsync("EmailJob", null, ReadyToRun(), CancellationToken.None);
        var lease = await store.ClaimNextJobAsync("worker-a", CancellationToken.None);

        Assert.NotNull(lease);

        await _database.ExpireLeaseAsync(jobId);

        var renewedLease = await store.RenewLeaseAsync(lease, CancellationToken.None);

        Assert.Null(renewedLease);
    }

    [Fact]
    public async Task RenewLeaseAsync_keeps_a_soon_to_expire_job_owned_by_the_current_worker()
    {
        var store = CreateStore();
        var jobId = await store.EnqueueAsync("EmailJob", null, ReadyToRun(), CancellationToken.None);
        var lease = await store.ClaimNextJobAsync("worker-a", CancellationToken.None);

        Assert.NotNull(lease);

        await _database.MakeLeaseExpireSoonAsync(jobId);

        var renewedLease = await store.RenewLeaseAsync(lease, CancellationToken.None);
        Assert.NotNull(renewedLease);

        await Task.Delay(TimeSpan.FromSeconds(2));

        var workerBLease = await store.ClaimNextJobAsync("worker-b", CancellationToken.None);

        Assert.Null(workerBLease);
    }

    [Fact]
    public async Task JobDispatcher_renews_the_lease_while_a_job_is_running()
    {
        var blockingJob = new BlockingJob();
        var services = new ServiceCollection();
        services.UseSqlServerJobStore(
            _database.ConnectionString,
            options =>
            {
                options.LeaseDuration = TimeSpan.FromSeconds(1);
                options.RenewalInterval = TimeSpan.FromMilliseconds(100);
            });
        services.AddSingleton(blockingJob);

        using var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<IJobStore>();
        var dispatcher = serviceProvider.GetRequiredService<IHostedService>();

        await store.EnqueueAsync(
            typeof(BlockingJob).AssemblyQualifiedName!,
            null,
            ReadyToRun(),
            CancellationToken.None);

        await dispatcher.StartAsync(CancellationToken.None);

        try
        {
            await blockingJob.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(TimeSpan.FromMilliseconds(1_500));

            var workerBLease = await store.ClaimNextJobAsync("worker-b", CancellationToken.None);

            Assert.Null(workerBLease);
        }
        finally
        {
            blockingJob.Release.TrySetResult(true);
            await blockingJob.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await dispatcher.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task MarkCompletedAsync_marks_the_claimed_job_as_completed()
    {
        var store = CreateStore();
        var jobId = await store.EnqueueAsync("EmailJob", null, ReadyToRun(), CancellationToken.None);
        var claimedJob = await store.ClaimNextJobAsync("worker-a", CancellationToken.None);

        Assert.NotNull(claimedJob);

        var completed = await store.MarkCompletedAsync(claimedJob, CancellationToken.None);

        Assert.True(completed);

        var status = await _database.GetStatusAsync(jobId);
        Assert.Equal(JobStatus.Completed, status);
    }

    [Fact]
    public async Task MarkCompletedAsync_completes_the_current_attempt()
    {
        var store = CreateStore();
        var jobId = await store.EnqueueAsync("EmailJob", null, ReadyToRun(), CancellationToken.None);
        var lease = await store.ClaimNextJobAsync("worker-a", CancellationToken.None);

        Assert.NotNull(lease);

        var completed = await store.MarkCompletedAsync(lease, CancellationToken.None);
        var attempt = Assert.Single(await _database.GetAttemptsAsync(jobId));

        Assert.True(completed);
        Assert.Equal("Completed", attempt.Status);
        Assert.NotNull(attempt.FinishedAt);
    }

    [Fact]
    public async Task MarkCompletedAsync_releases_the_completed_jobs_lease()
    {
        var store = CreateStore();
        var jobId = await store.EnqueueAsync("EmailJob", null, ReadyToRun(), CancellationToken.None);
        var lease = await store.ClaimNextJobAsync("worker-a", CancellationToken.None);

        Assert.NotNull(lease);

        var completed = await store.MarkCompletedAsync(lease, CancellationToken.None);
        var ownership = await _database.GetOwnershipAsync(jobId);

        Assert.True(completed);
        Assert.Null(ownership.LockedBy);
        Assert.Null(ownership.LeaseToken);
    }

    [Fact]
    public async Task MarkCompletedAsync_rejects_a_worker_that_lost_its_lease()
    {
        var store = CreateStore();
        var jobId = await store.EnqueueAsync("EmailJob", null, ReadyToRun(), CancellationToken.None);

        var workerALease = await store.ClaimNextJobAsync("worker-a", CancellationToken.None);
        Assert.NotNull(workerALease);

        await _database.ExpireLeaseAsync(jobId);

        var workerBLease = await store.ClaimNextJobAsync("worker-b", CancellationToken.None);
        Assert.NotNull(workerBLease);

        var completed = await store.MarkCompletedAsync(workerALease, CancellationToken.None);

        Assert.False(completed);
        Assert.NotEqual(workerALease.Token, workerBLease.Token);

        var status = await _database.GetStatusAsync(jobId);
        Assert.Equal(JobStatus.InProgress, status);
    }

    [Fact]
    public async Task MarkFailedAsync_reschedules_the_job_for_another_worker()
    {
        var store = CreateStore();
        var jobId = await store.EnqueueAsync("EmailJob", null, ReadyToRun(), CancellationToken.None);
        var firstClaim = await store.ClaimNextJobAsync("worker-a", CancellationToken.None);

        Assert.NotNull(firstClaim);

        var rescheduled = await store.MarkFailedAsync(
            firstClaim,
            TestFailure(),
            newRetryCount: 1,
            nextRunAt: ReadyToRun(),
            CancellationToken.None);

        Assert.True(rescheduled);

        var secondClaim = await store.ClaimNextJobAsync("worker-b", CancellationToken.None);

        Assert.NotNull(secondClaim);
        Assert.Equal(jobId, secondClaim.Job.Id);
        Assert.Equal(1, secondClaim.Job.RetryCount);
        Assert.Equal("worker-b", secondClaim.Job.LockedBy);
    }

    [Fact]
    public async Task MarkFailedAsync_releases_the_lease_before_the_retry_is_due()
    {
        var store = CreateStore();
        var jobId = await store.EnqueueAsync("EmailJob", null, ReadyToRun(), CancellationToken.None);
        var lease = await store.ClaimNextJobAsync("worker-a", CancellationToken.None);

        Assert.NotNull(lease);

        var failed = await store.MarkFailedAsync(
            lease,
            TestFailure(),
            newRetryCount: 1,
            nextRunAt: DateTimeOffset.UtcNow.AddMinutes(5),
            CancellationToken.None);
        var status = await _database.GetStatusAsync(jobId);
        var ownership = await _database.GetOwnershipAsync(jobId);

        Assert.True(failed);
        Assert.Equal(JobStatus.Pending, status);
        Assert.Null(ownership.LockedBy);
        Assert.Null(ownership.LeaseToken);
    }

    [Fact]
    public async Task MarkFailedAsync_fails_the_current_attempt_before_scheduling_a_retry()
    {
        var store = CreateStore();
        var jobId = await store.EnqueueAsync("EmailJob", null, ReadyToRun(), CancellationToken.None);
        var lease = await store.ClaimNextJobAsync("worker-a", CancellationToken.None);

        Assert.NotNull(lease);

        var failure = TestFailure();
        var failed = await store.MarkFailedAsync(
            lease,
            failure,
            newRetryCount: 1,
            nextRunAt: DateTimeOffset.UtcNow.AddMinutes(5),
            CancellationToken.None);
        var attempt = Assert.Single(await _database.GetAttemptsAsync(jobId));
        var storedFailure = await _database.GetAttemptFailureAsync(jobId);

        Assert.True(failed);
        Assert.Equal("Failed", attempt.Status);
        Assert.NotNull(attempt.FinishedAt);
        Assert.Equal(failure.ErrorId, storedFailure.ErrorId);
        Assert.Equal(failure.FailureType, storedFailure.FailureType);
        Assert.Equal(failure.SafeMessage, storedFailure.FailureMessage);
    }

    [Fact]
    public async Task MarkFailedAsync_rejects_a_worker_that_lost_its_lease()
    {
        var store = CreateStore();
        var jobId = await store.EnqueueAsync("EmailJob", null, ReadyToRun(), CancellationToken.None);

        var workerALease = await store.ClaimNextJobAsync("worker-a", CancellationToken.None);
        Assert.NotNull(workerALease);

        await _database.ExpireLeaseAsync(jobId);

        var workerBLease = await store.ClaimNextJobAsync("worker-b", CancellationToken.None);
        Assert.NotNull(workerBLease);

        var failed = await store.MarkFailedAsync(
            workerALease,
            TestFailure(),
            newRetryCount: 1,
            nextRunAt: ReadyToRun(),
            CancellationToken.None);

        Assert.False(failed);

        var status = await _database.GetStatusAsync(jobId);
        var attempts = await _database.GetAttemptsAsync(jobId);
        Assert.Equal(JobStatus.InProgress, status);
        Assert.Collection(
            attempts,
            attempt =>
            {
                Assert.Equal("Abandoned", attempt.Status);
                Assert.Null(attempt.ErrorId);
                Assert.Null(attempt.FailureType);
                Assert.Null(attempt.FailureMessage);
            },
            attempt =>
            {
                Assert.Equal("Running", attempt.Status);
                Assert.Null(attempt.ErrorId);
                Assert.Null(attempt.FailureType);
                Assert.Null(attempt.FailureMessage);
            });
    }

    [Fact]
    public async Task RenewLeaseAsync_rejects_a_worker_after_another_worker_claims_the_job()
    {
        var store = CreateStore();
        var jobId = await store.EnqueueAsync("EmailJob", null, ReadyToRun(), CancellationToken.None);
        var workerALease = await store.ClaimNextJobAsync("worker-a", CancellationToken.None);

        Assert.NotNull(workerALease);

        await _database.ExpireLeaseAsync(jobId);
        var workerBLease = await store.ClaimNextJobAsync("worker-b", CancellationToken.None);
        var staleRenewal = await store.RenewLeaseAsync(workerALease, CancellationToken.None);
        var ownership = await _database.GetOwnershipAsync(jobId);

        Assert.NotNull(workerBLease);
        Assert.Null(staleRenewal);
        Assert.Equal("worker-b", ownership.LockedBy);
        Assert.Equal(workerBLease.Token, ownership.LeaseToken);
    }

    [Fact]
    public async Task MarkFailedAsync_marks_a_job_as_failed_and_releases_its_lease()
    {
        var store = CreateStore();
        var jobId = await store.EnqueueAsync("EmailJob", null, ReadyToRun(), CancellationToken.None);
        var lease = await store.ClaimNextJobAsync("worker-a", CancellationToken.None);

        Assert.NotNull(lease);

        var failure = TestFailure();
        var failed = await store.MarkFailedAsync(
            lease,
            failure,
            newRetryCount: lease.Job.MaxRetries,
            nextRunAt: null,
            CancellationToken.None);

        Assert.True(failed);

        var status = await _database.GetStatusAsync(jobId);
        var ownership = await _database.GetOwnershipAsync(jobId);

        Assert.Equal(JobStatus.Failed, status);
        Assert.Null(ownership.LockedBy);
        Assert.Null(ownership.LeaseToken);

        var storedFailure = await _database.GetAttemptFailureAsync(jobId);
        Assert.Equal(failure.ErrorId, storedFailure.ErrorId);
        Assert.Equal(failure.FailureType, storedFailure.FailureType);
        Assert.Equal(failure.SafeMessage, storedFailure.FailureMessage);
    }

    private SqlJobStore CreateStore() => new(_database.ConnectionString);

    private static DateTimeOffset ReadyToRun() => DateTimeOffset.UtcNow.AddMinutes(-1);

    private static JobFailure TestFailure() => new(
        Guid.NewGuid(),
        "TestException",
        "A safe test failure.");

    private sealed class BlockingJob : IJob
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ExecuteAsync(string? payload, CancellationToken ct)
        {
            Started.TrySetResult(true);

            try
            {
                await Release.Task.WaitAsync(ct);
            }
            finally
            {
                Finished.TrySetResult(true);
            }
        }
    }

    private sealed class TestFailureClassifier : IJobFailureClassifier
    {
        public JobFailure Classify(Exception exception, Guid errorId) => new(
            errorId,
            "TestFailure",
            "A host-defined safe message.");
    }
}
