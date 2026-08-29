using JobFlow.Core;
using Microsoft.Data.SqlClient;

namespace JobFlow.SqlServer;

public class SqlJobStore : IJobStore
{
    private readonly string _connectionString;
    private readonly JobLeaseOptions _leaseOptions;
    public SqlJobStore(string connectionString, JobLeaseOptions? leaseOptions = null)
    {
        _connectionString = connectionString;
        _leaseOptions = leaseOptions ?? new JobLeaseOptions();
    }

    public async Task<Guid> EnqueueAsync(string jobType, string? payload, DateTimeOffset nextRunAt, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var id = Guid.NewGuid();

        const string sql = "INSERT INTO Jobs (Id, JobType, Payload, Status, NextRunAt, CreatedAt, RetryCount, MaxRetries) VALUES (@id, @jobType, @payload, @status, @nextRunAt, @createdAt, 0, 3)";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@jobType", jobType);
        command.Parameters.AddWithValue("@payload", (object?)payload ?? DBNull.Value);
        command.Parameters.AddWithValue("@status", (byte)JobStatus.Pending);
        command.Parameters.AddWithValue("@nextRunAt", nextRunAt);
        command.Parameters.AddWithValue("@createdAt", DateTimeOffset.UtcNow);

        await command.ExecuteNonQueryAsync(ct);

        return id;
    }

    public async Task<JobLease?> ClaimNextJobAsync(string workerId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);

        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        var leaseToken = Guid.NewGuid();
        var leaseExpiresAt = DateTimeOffset.UtcNow.Add(_leaseOptions.LeaseDuration);

        const string sql = """
            UPDATE TOP (1) Jobs
            SET Status = @inProgress, LockedBy = @workerId, LockedAt = SYSDATETIMEOFFSET(), LeaseToken = @leaseToken, LeaseExpiresAt = @leaseExpiresAt
            OUTPUT INSERTED.Id, INSERTED.JobType, INSERTED.Payload, INSERTED.Status,
                   INSERTED.NextRunAt, INSERTED.CreatedAt, INSERTED.RetryCount,
                   INSERTED.MaxRetries, INSERTED.LockedBy, INSERTED.LockedAt,
                   INSERTED.LeaseToken, INSERTED.LeaseExpiresAt,
                   DELETED.Status, DELETED.LeaseToken
            FROM Jobs WITH (UPDLOCK, READPAST)
            WHERE (Status = @pending AND NextRunAt <= SYSDATETIMEOFFSET())
                OR (Status = @inProgress AND LeaseExpiresAt <= SYSDATETIMEOFFSET())
            """;

        await using var command = new SqlCommand(sql, connection, transaction);

        command.Parameters.AddWithValue("@workerId", workerId);
        command.Parameters.AddWithValue("@pending", (byte)JobStatus.Pending);
        command.Parameters.AddWithValue("@inProgress", (byte)JobStatus.InProgress);
        command.Parameters.AddWithValue("@leaseToken", leaseToken);
        command.Parameters.AddWithValue("@leaseExpiresAt", leaseExpiresAt);

        await using var reader = await command.ExecuteReaderAsync(ct);

        if(!await reader.ReadAsync(ct)){ return null; }

        var job = new JobRecord
        {
            Id = reader.GetGuid(0),
            JobType = reader.GetString(1),
            Payload = reader.IsDBNull(2) ? null : reader.GetString(2),
            JobStatus = (JobStatus)reader.GetByte(3),
            NextRunAt = reader.GetDateTimeOffset(4),
            CreatedAt = reader.GetDateTimeOffset(5),
            RetryCount = reader.GetInt32(6),
            MaxRetries = reader.GetInt32(7),
            LockedBy = reader.IsDBNull(8) ? null : reader.GetString(8),
            LockedAt = reader.IsDBNull(9) ? null : reader.GetDateTimeOffset(9)
        };

        var previousStatus = (JobStatus)reader.GetByte(12);
        var previousLeaseToken = reader.IsDBNull(13) ? (Guid?)null : reader.GetGuid(13);

        await reader.DisposeAsync();

        if(previousStatus == JobStatus.InProgress && previousLeaseToken is not null)
        {
            const string abandonAttemptSql = """
                UPDATE dbo.JobAttempts
                SET Status = 'Abandoned',
                    FinishedAt = SYSDATETIMEOFFSET()
                WHERE JobId = @jobId
                    AND LeaseToken = @leaseToken
                    AND Status = 'Running';
            """;
            await using var abandonCommand = new SqlCommand(abandonAttemptSql, connection, transaction);

            abandonCommand.Parameters.AddWithValue("@jobId", job.Id);
            abandonCommand.Parameters.AddWithValue("@leaseToken", previousLeaseToken.Value);

            await abandonCommand.ExecuteNonQueryAsync(ct);
        }

        const string insertAttempSql = """
            INSERT INTO dbo.JobAttempts
                (Id, JobId, AttemptNumber, WorkerId, LeaseToken, Status, StartedAt)
            VALUES
                (@id, @jobId, @attemptNumber, @workerId, @leaseToken, @status, SYSDATETIMEOFFSET());
        """;

        const string nextAttemptNumberSql = """
            SELECT COALESCE(MAX(AttemptNumber), 0) + 1
            FROM dbo.JobAttempts
            WHERE JobId = @jobId;
        """;

        await using var attemptCommand = new SqlCommand(insertAttempSql, connection, transaction);
        await using var attemptNumberCommand = new SqlCommand(nextAttemptNumberSql, connection, transaction);
        
        attemptNumberCommand.Parameters.AddWithValue("@jobId", job.Id);
        
        var attemptNumber = Convert.ToInt32(await attemptNumberCommand.ExecuteScalarAsync(ct));

        attemptCommand.Parameters.AddWithValue("@id", Guid.NewGuid());
        attemptCommand.Parameters.AddWithValue("@jobId", job.Id);
        attemptCommand.Parameters.AddWithValue("@attemptNumber", attemptNumber);
        attemptCommand.Parameters.AddWithValue("@workerId", workerId);
        attemptCommand.Parameters.AddWithValue("@leaseToken", leaseToken);
        attemptCommand.Parameters.AddWithValue("@status", "Running");

        await attemptCommand.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);

        return new JobLease(job, leaseToken, leaseExpiresAt);
    }

    public async Task<bool> MarkCompletedAsync(JobLease lease, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);

        await connection.OpenAsync(ct);

        const string sql = "UPDATE Jobs SET Status = @completed, LockedBy = NULL, LockedAt = NULL, LeaseToken = NULL, LeaseExpiresAt = NULL WHERE Id = @jobId AND Status = @inProgress AND LeaseToken = @leaseToken AND LeaseExpiresAt > SYSDATETIMEOFFSET()";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@completed", (byte)JobStatus.Completed);
        command.Parameters.AddWithValue("@inProgress", (byte)JobStatus.InProgress);
        command.Parameters.AddWithValue("@jobId", lease.Job.Id);
        command.Parameters.AddWithValue("@leaseToken", lease.Token);

        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<bool> MarkFailedAsync(JobLease lease, int newRetryCount, DateTimeOffset? nextRunAt, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        string sql = nextRunAt.HasValue
        ? "UPDATE Jobs SET Status = @pending, RetryCount = @retryCount, NextRunAt = @nextRunAt, LockedBy = NULL, LockedAt = NULL, LeaseToken = NULL, LeaseExpiresAt = NULL where Id = @jobId AND Status = @inProgress AND LeaseToken = @leaseToken AND LeaseExpiresAt > SYSDATETIMEOFFSET()"
        : "UPDATE Jobs Set Status = @failed, RetryCount = @retryCount, LockedBy = NULL, LockedAt = NULL, LeaseToken = NULL, LeaseExpiresAt = NULL where Id = @jobId AND Status = @inProgress AND LeaseToken = @leaseToken AND LeaseExpiresAt > SYSDATETIMEOFFSET()";

        await using var command = new SqlCommand(sql, connection);

        command.Parameters.AddWithValue("@retryCount", newRetryCount);
        command.Parameters.AddWithValue("@jobId", lease.Job.Id);
        command.Parameters.AddWithValue("@leaseToken", lease.Token);
        command.Parameters.AddWithValue("@inProgress", (byte)JobStatus.InProgress);

        if (nextRunAt.HasValue)
        {
            command.Parameters.AddWithValue("@pending", (byte)JobStatus.Pending);
            command.Parameters.AddWithValue("@nextRunAt", nextRunAt.Value);
        }
        else
        {
            command.Parameters.AddWithValue("@failed", (byte)JobStatus.Failed);
        }

        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<JobLease?> RenewLeaseAsync(JobLease lease, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var renewedExpiresAt = DateTimeOffset.UtcNow.Add(_leaseOptions.LeaseDuration);

        const string sql = """
        UPDATE Jobs
        SET LeaseExpiresAt = @renewedExpiresAt
        OUTPUT INSERTED.LeaseExpiresAt
        WHERE Id = @jobId
        AND Status = @inProgress
        AND LeaseToken = @leaseToken
        AND LeaseExpiresAt > SYSDATETIMEOFFSET()
        """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@renewedExpiresAt", renewedExpiresAt);
        command.Parameters.AddWithValue("@jobId", lease.Job.Id);
        command.Parameters.AddWithValue("@inProgress", (byte)JobStatus.InProgress);
        command.Parameters.AddWithValue("@leaseToken", lease.Token);

        var result = await command.ExecuteScalarAsync(ct);
        if(result is null)
        {
            return null;
        }

        return new JobLease(lease.Job, lease.Token, (DateTimeOffset)result);
    }
}
