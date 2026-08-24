using JobFlow.Core;

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

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ClaimNextJobAsync_claims_a_ready_job_for_the_worker()
    {
        var store = CreateStore();
        var jobId = await store.EnqueueAsync(
            "EmailJob",
            "{\"to\":\"customer@example.test\"}",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        var claimedJob = await store.ClaimNextJobAsync("worker-a", CancellationToken.None);

        Assert.NotNull(claimedJob);
        Assert.Equal(jobId, claimedJob.Id);
        Assert.Equal(JobStatus.InProgress, claimedJob.JobStatus);
        Assert.Equal("worker-a", claimedJob.LockedBy);
        Assert.NotNull(claimedJob.LockedAt);
    }

    [Fact]
    public async Task ClaimNextJobAsync_allows_only_one_worker_to_claim_a_job()
    {
        var store = CreateStore();
        await store.EnqueueAsync("EmailJob", null, DateTimeOffset.UtcNow, CancellationToken.None);

        var firstWorker = CreateStore();
        var secondWorker = CreateStore();

        var claims = await Task.WhenAll(
            firstWorker.ClaimNextJobAsync("worker-a", CancellationToken.None),
            secondWorker.ClaimNextJobAsync("worker-b", CancellationToken.None));

        Assert.Equal(1, claims.Count(job => job is not null));
    }

    [Fact]
    public async Task MarkCompletedAsync_marks_the_claimed_job_as_completed()
    {
        var store = CreateStore();
        var jobId = await store.EnqueueAsync("EmailJob", null, DateTimeOffset.UtcNow, CancellationToken.None);
        var claimedJob = await store.ClaimNextJobAsync("worker-a", CancellationToken.None);

        Assert.NotNull(claimedJob);

        await store.MarkCompletedAsync(claimedJob.Id, CancellationToken.None);

        var status = await _database.GetStatusAsync(jobId);
        Assert.Equal(JobStatus.Completed, status);
    }

    [Fact]
    public async Task MarkFailedAsync_reschedules_the_job_for_another_worker()
    {
        var store = CreateStore();
        var jobId = await store.EnqueueAsync("EmailJob", null, DateTimeOffset.UtcNow, CancellationToken.None);
        var firstClaim = await store.ClaimNextJobAsync("worker-a", CancellationToken.None);

        Assert.NotNull(firstClaim);

        await store.MarkFailedAsync(
            firstClaim.Id,
            newRetryCount: 1,
            nextRunAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            CancellationToken.None);

        var secondClaim = await store.ClaimNextJobAsync("worker-b", CancellationToken.None);

        Assert.NotNull(secondClaim);
        Assert.Equal(jobId, secondClaim.Id);
        Assert.Equal(1, secondClaim.RetryCount);
        Assert.Equal("worker-b", secondClaim.LockedBy);
    }

    private SqlJobStore CreateStore() => new(_database.ConnectionString);
}
