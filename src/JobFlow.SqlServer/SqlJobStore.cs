using JobFlow.Core;
using Microsoft.Data.SqlClient;

namespace JobFlow.SqlServer;

public class SqlJobStore : IJobStore
{
    private readonly string _connectionString;
    public SqlJobStore(string connectionString)
    {
        _connectionString = connectionString;
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

    public async Task<JobRecord?> ClaimNextJobAsync(string workerId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);

        await connection.OpenAsync(ct);

        const string sql = """
            UPDATE TOP (1) Jobs
            SET Status = 1, LockedBy = @workerId, LockedAt = SYSDATETIMEOFFSET()
            OUTPUT INSERTED.Id, INSERTED.JobType, INSERTED.Payload, INSERTED.Status,
                   INSERTED.NextRunAt, INSERTED.CreatedAt, INSERTED.RetryCount,
                   INSERTED.MaxRetries, INSERTED.LockedBy, INSERTED.LockedAt
            FROM Jobs WITH (UPDLOCK, READPAST)
            WHERE Status = 0 AND NextRunAt <= SYSDATETIMEOFFSET()
            """;

        await using var command = new SqlCommand(sql, connection);

        command.Parameters.AddWithValue("@workerId", workerId);

        await using var reader = await command.ExecuteReaderAsync(ct);

        if(!await reader.ReadAsync(ct)){ return null; }

        return new JobRecord
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
    }

    public async Task MarkCompletedAsync(Guid jobId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);

        await connection.OpenAsync(ct);

        const string sql = "UPDATE Jobs SET Status = @status WHERE Id = @id";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@status", (byte)JobStatus.Completed);
        command.Parameters.AddWithValue("@id", jobId);

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkFailedAsync(Guid jobId, int newRetryCount, DateTimeOffset? nextRunAt, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        string sql = nextRunAt.HasValue
        ? "UPDATE Jobs SET Status = @pending, RetryCount = @retryCount, NextRunAt = @nextRunAt, LockedBy = NULL, LockedAt = NULL where Id = @id"
        : "UPDATE Jobs Set Status = @failed, RetryCount = @retryCount WHERE Id = @id";

        await using var command = new SqlCommand(sql, connection);

        command.Parameters.AddWithValue("@retryCount", newRetryCount);
        command.Parameters.AddWithValue("@id", jobId);

        if (nextRunAt.HasValue)
        {
            command.Parameters.AddWithValue("@pending", (byte)JobStatus.Pending);
            command.Parameters.AddWithValue("@nextRunAt", nextRunAt.Value);
        }
        else
        {
            command.Parameters.AddWithValue("@failed", (byte)JobStatus.Failed);
        }

        await command.ExecuteNonQueryAsync(ct);
    }
}
