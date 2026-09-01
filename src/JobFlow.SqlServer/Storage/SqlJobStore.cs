using JobFlow.Core;
using Microsoft.Data.SqlClient;
using System.Data;

namespace JobFlow.SqlServer;

public sealed class SqlJobStore : IJobStore, IJobQuery
{
    private readonly string _connectionString;
    private readonly JobLeaseOptions _leaseOptions;
    public SqlJobStore(string connectionString, JobLeaseOptions? leaseOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _connectionString = connectionString;
        _leaseOptions = leaseOptions ?? new JobLeaseOptions();
        _leaseOptions.Validate();
    }

    public async Task<JobDetails?> GetAsync(Guid jobId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        const string sql = """
            SELECT
                j.Id,
                j.JobType,
                j.Status,
                j.CreatedAt,
                j.NextRunAt,
                j.RetryCount,
                j.MaxRetries,
                j.LockedBy,
                j.LockedAt,
                j.LeaseExpiresAt,
                a.Id,
                a.AttemptNumber,
                a.WorkerId,
                a.Status,
                a.StartedAt,
                a.FinishedAt,
                a.ErrorId,
                a.FailureType,
                a.FailureMessage
            FROM dbo.Jobs AS j
            LEFT JOIN dbo.JobAttempts AS a ON a.JobId = j.Id
            WHERE j.Id = @jobId
            ORDER BY a.AttemptNumber;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@jobId", jobId);

        await using var reader = await command.ExecuteReaderAsync(ct);

        var attempts = new List<JobAttemptDetails>();
        JobDetails? details = null;

        while (await reader.ReadAsync(ct))
        {
            details ??= new JobDetails(
                reader.GetGuid(0),
                reader.GetString(1),
                ReadJobStatus(reader.GetByte(2)),
                reader.GetDateTimeOffset(3),
                reader.GetDateTimeOffset(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetDateTimeOffset(8),
                reader.IsDBNull(9) ? null : reader.GetDateTimeOffset(9),
                attempts);

            if (reader.IsDBNull(10))
            {
                continue;
            }

            attempts.Add(new JobAttemptDetails(
                reader.GetGuid(10),
                reader.GetInt32(11),
                reader.GetString(12),
                ReadAttemptStatus(reader.GetString(13)),
                reader.GetDateTimeOffset(14),
                reader.IsDBNull(15) ? null : reader.GetDateTimeOffset(15),
                reader.IsDBNull(16) ? null : reader.GetGuid(16),
                reader.IsDBNull(17) ? null : reader.GetString(17),
                reader.IsDBNull(18) ? null : reader.GetString(18)));
        }

        return details is null
            ? null
            : details with { Attempts = attempts.AsReadOnly() };
    }

    public async Task<JobSearchPage> SearchAsync(JobSearchCriteria criteria, CancellationToken ct)
    {
        ValidateSearchCriteria(criteria);

        var position = criteria.Cursor is null
            ? (JobSearchPosition?)null
            : JobSearchCursorCodec.Decode(criteria.Cursor, criteria);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        const string sql = """
            SELECT TOP (@take)
                j.Id,
                j.JobType,
                j.Status,
                j.CreatedAt,
                j.NextRunAt,
                j.RetryCount,
                j.MaxRetries,
                latestAttempt.WorkerId,
                latestAttempt.StartedAt
            FROM dbo.Jobs AS j
            OUTER APPLY
            (
                SELECT TOP (1)
                    a.WorkerId,
                    a.StartedAt
                FROM dbo.JobAttempts AS a
                WHERE a.JobId = j.Id
                ORDER BY a.AttemptNumber DESC
            ) AS latestAttempt
            WHERE (@status IS NULL OR j.Status = @status)
                AND (@jobType IS NULL OR j.JobType = @jobType)
                AND (@workerId IS NULL OR EXISTS
                (
                    SELECT 1
                    FROM dbo.JobAttempts AS handledAttempt
                    WHERE handledAttempt.JobId = j.Id
                        AND handledAttempt.WorkerId = @workerId
                ))
                AND (@createdFrom IS NULL OR j.CreatedAt >= @createdFrom)
                AND (@createdTo IS NULL OR j.CreatedAt < @createdTo)
                AND (@cursorCreatedAt IS NULL
                    OR j.CreatedAt < @cursorCreatedAt
                    OR (j.CreatedAt = @cursorCreatedAt AND j.Id < @cursorId))
            ORDER BY j.CreatedAt DESC, j.Id DESC;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@take", SqlDbType.Int).Value = criteria.PageSize + 1;
        command.Parameters.Add("@status", SqlDbType.TinyInt).Value = criteria.Status is null
            ? DBNull.Value
            : (object)(byte)criteria.Status.Value;
        command.Parameters.Add("@jobType", SqlDbType.NVarChar, 255).Value = (object?)criteria.JobType ?? DBNull.Value;
        command.Parameters.Add("@workerId", SqlDbType.NVarChar, 200).Value = (object?)criteria.WorkerId ?? DBNull.Value;
        command.Parameters.Add("@createdFrom", SqlDbType.DateTimeOffset).Value = criteria.CreatedFrom is null
            ? DBNull.Value
            : (object)criteria.CreatedFrom.Value;
        command.Parameters.Add("@createdTo", SqlDbType.DateTimeOffset).Value = criteria.CreatedTo is null
            ? DBNull.Value
            : (object)criteria.CreatedTo.Value;
        command.Parameters.Add("@cursorCreatedAt", SqlDbType.DateTimeOffset).Value = position is null
            ? DBNull.Value
            : (object)position.Value.CreatedAt;
        command.Parameters.Add("@cursorId", SqlDbType.UniqueIdentifier).Value = position is null
            ? DBNull.Value
            : (object)position.Value.Id;

        await using var reader = await command.ExecuteReaderAsync(ct);
        var jobs = new List<JobSummary>(criteria.PageSize + 1);

        while (await reader.ReadAsync(ct))
        {
            jobs.Add(new JobSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                ReadJobStatus(reader.GetByte(2)),
                reader.GetDateTimeOffset(3),
                reader.GetDateTimeOffset(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetDateTimeOffset(8)));
        }

        var hasMore = jobs.Count > criteria.PageSize;
        if (hasMore)
        {
            jobs.RemoveAt(jobs.Count - 1);
        }

        var nextCursor = hasMore
            ? JobSearchCursorCodec.Encode(jobs[^1].CreatedAt, jobs[^1].Id, criteria)
            : null;

        return new JobSearchPage(jobs.AsReadOnly(), nextCursor);
    }

    public async Task<Guid> EnqueueAsync(string jobType, string? payload, DateTimeOffset nextRunAt, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var id = Guid.NewGuid();

        const string sql = "INSERT INTO dbo.Jobs (Id, JobType, Payload, Status, NextRunAt, CreatedAt, RetryCount, MaxRetries) VALUES (@id, @jobType, @payload, @status, @nextRunAt, @createdAt, 0, 3)";

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
            ;WITH NextJob AS
            (
                SELECT TOP (1) *
                FROM dbo.Jobs WITH (UPDLOCK, READPAST)
                WHERE (Status = @pending AND NextRunAt <= SYSDATETIMEOFFSET())
                    OR (Status = @inProgress AND LeaseExpiresAt <= SYSDATETIMEOFFSET())
                ORDER BY
                    CASE WHEN Status = @pending THEN NextRunAt ELSE LeaseExpiresAt END,
                    CreatedAt,
                    Id
            )
            UPDATE NextJob
            SET Status = @inProgress, LockedBy = @workerId, LockedAt = SYSDATETIMEOFFSET(), LeaseToken = @leaseToken, LeaseExpiresAt = @leaseExpiresAt
            OUTPUT INSERTED.Id, INSERTED.JobType, INSERTED.Payload, INSERTED.Status,
                   INSERTED.NextRunAt, INSERTED.CreatedAt, INSERTED.RetryCount,
                   INSERTED.MaxRetries, INSERTED.LockedBy, INSERTED.LockedAt,
                   INSERTED.LeaseToken, INSERTED.LeaseExpiresAt,
                   DELETED.Status, DELETED.LeaseToken
            """;

        await using var command = new SqlCommand(sql, connection, transaction);

        command.Parameters.AddWithValue("@workerId", workerId);
        command.Parameters.AddWithValue("@pending", (byte)JobStatus.Pending);
        command.Parameters.AddWithValue("@inProgress", (byte)JobStatus.InProgress);
        command.Parameters.AddWithValue("@leaseToken", leaseToken);
        command.Parameters.AddWithValue("@leaseExpiresAt", leaseExpiresAt);

        await using var reader = await command.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct)) { return null; }

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

        if (previousStatus == JobStatus.InProgress && previousLeaseToken is not null)
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

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);

        const string sql = "UPDATE dbo.Jobs SET Status = @completed, LockedBy = NULL, LockedAt = NULL, LeaseToken = NULL, LeaseExpiresAt = NULL WHERE Id = @jobId AND Status = @inProgress AND LeaseToken = @leaseToken AND LeaseExpiresAt > SYSDATETIMEOFFSET()";

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@completed", (byte)JobStatus.Completed);
        command.Parameters.AddWithValue("@inProgress", (byte)JobStatus.InProgress);
        command.Parameters.AddWithValue("@jobId", lease.Job.Id);
        command.Parameters.AddWithValue("@leaseToken", lease.Token);

        var completed = await command.ExecuteNonQueryAsync(ct) == 1;

        if (!completed)
        {
            return false;
        }

        const string completeAttemptSql = """
            UPDATE dbo.JobAttempts
            SET Status = 'Completed',
                FinishedAt = SYSDATETIMEOFFSET()
            WHERE JobId = @jobId
                AND LeaseToken = @leaseToken
                AND Status = 'Running';
        """;

        await using var attemptCommand = new SqlCommand(completeAttemptSql, connection, transaction);

        attemptCommand.Parameters.AddWithValue("@jobId", lease.Job.Id);
        attemptCommand.Parameters.AddWithValue("@leaseToken", lease.Token);

        var attemptsUpdated = await attemptCommand.ExecuteNonQueryAsync(ct);

        if (attemptsUpdated != 1)
        {
            throw new InvalidOperationException("The claimed job was completed, but its running attempt was not found");
        }

        await transaction.CommitAsync(ct);

        return true;
    }

    public async Task<bool> MarkFailedAsync(JobLease lease, JobFailure failure, int newRetryCount, DateTimeOffset? nextRunAt, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);

        string sql = nextRunAt.HasValue
        ? "UPDATE dbo.Jobs SET Status = @pending, RetryCount = @retryCount, NextRunAt = @nextRunAt, LockedBy = NULL, LockedAt = NULL, LeaseToken = NULL, LeaseExpiresAt = NULL WHERE Id = @jobId AND Status = @inProgress AND LeaseToken = @leaseToken AND LeaseExpiresAt > SYSDATETIMEOFFSET()"
        : "UPDATE dbo.Jobs SET Status = @failed, RetryCount = @retryCount, LockedBy = NULL, LockedAt = NULL, LeaseToken = NULL, LeaseExpiresAt = NULL WHERE Id = @jobId AND Status = @inProgress AND LeaseToken = @leaseToken AND LeaseExpiresAt > SYSDATETIMEOFFSET()";

        await using var command = new SqlCommand(sql, connection, transaction);

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

        var failed = await command.ExecuteNonQueryAsync(ct) == 1;

        if (!failed)
        {
            return false;
        }

        const string failAttemptSql = """
            UPDATE dbo.JobAttempts
            SET Status = 'Failed',
                FinishedAt = SYSDATETIMEOFFSET(),
                ErrorId = @errorId,
                FailureType = @failureType,
                FailureMessage = @failureMessage
            WHERE JobId = @jobId
                AND LeaseToken = @leaseToken
                AND Status = 'Running'
        """;

        await using var attemptCommand = new SqlCommand(failAttemptSql, connection, transaction);

        attemptCommand.Parameters.AddWithValue("@jobId", lease.Job.Id);
        attemptCommand.Parameters.AddWithValue("@leaseToken", lease.Token);
        attemptCommand.Parameters.AddWithValue("@errorId", failure.ErrorId);
        attemptCommand.Parameters.AddWithValue("@failureType", failure.FailureType);
        attemptCommand.Parameters.AddWithValue("@failureMessage", failure.SafeMessage);

        var attemptsUpdated = await attemptCommand.ExecuteNonQueryAsync(ct);

        if (attemptsUpdated != 1)
        {
            throw new InvalidOperationException("The claimed job was failed, but its running attempt was not found");
        }

        await transaction.CommitAsync(ct);

        return true;
    }

    public async Task<JobLease?> RenewLeaseAsync(JobLease lease, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var renewedExpiresAt = DateTimeOffset.UtcNow.Add(_leaseOptions.LeaseDuration);

        const string sql = """
        UPDATE dbo.Jobs
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
        if (result is null)
        {
            return null;
        }

        return new JobLease(lease.Job, lease.Token, (DateTimeOffset)result);
    }

    private static JobStatus ReadJobStatus(byte value)
    {
        var status = (JobStatus)value;

        return Enum.IsDefined(status)
            ? status
            : throw new InvalidOperationException($"The database contains an unsupported job status value: {value}.");
    }

    private static JobAttemptStatus ReadAttemptStatus(string value)
    {
        return Enum.TryParse<JobAttemptStatus>(value, ignoreCase: false, out var status)
            && Enum.IsDefined(status)
            ? status
            : throw new InvalidOperationException($"The database contains an unsupported attempt status value: {value}.");
    }

    private static void ValidateSearchCriteria(JobSearchCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        if (criteria.PageSize is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(criteria.PageSize),
                "Page size must be between 1 and 100.");
        }

        if (criteria.JobType is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(criteria.JobType);
        }

        if (criteria.WorkerId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(criteria.WorkerId);
        }

        if (criteria.CreatedFrom is not null
            && criteria.CreatedTo is not null
            && criteria.CreatedFrom >= criteria.CreatedTo)
        {
            throw new ArgumentException(
                "CreatedFrom must be earlier than CreatedTo.",
                nameof(criteria));
        }
    }
}
