using JobFlow.Core;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace JobFlow.SqlServer.Tests;

public sealed class SqlServerTestDatabase : IAsyncLifetime
{
    private const string DatabaseName = "JobFlowTests";
    private readonly MsSqlContainer _container = new MsSqlBuilder(
        "mcr.microsoft.com/mssql/server:2022-latest").Build();
    private readonly SemaphoreSlim _testLock = new(1, 1);

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var masterConnectionString = _container.GetConnectionString();
        await using (var connection = new SqlConnection(masterConnectionString))
        {
            await connection.OpenAsync();

            await using var command = new SqlCommand(
                "IF DB_ID(N'JobFlowTests') IS NULL CREATE DATABASE [JobFlowTests];",
                connection);
            await command.ExecuteNonQueryAsync();
        }

        ConnectionString = new SqlConnectionStringBuilder(masterConnectionString)
        {
            InitialCatalog = DatabaseName
        }.ConnectionString;

        var services = new ServiceCollection();
        services.UseSqlServerJobStore(ConnectionString);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public async Task ResetAsync()
    {
        await _testLock.WaitAsync();

        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();

            await using var command = new SqlCommand("DELETE FROM dbo.JobAttempts; DELETE FROM dbo.Jobs;", connection);
            await command.ExecuteNonQueryAsync();
        }
        catch
        {
            _testLock.Release();
            throw;
        }
    }

    public void ReleaseTestLock()
    {
        _testLock.Release();
    }

    public async Task<JobStatus> GetStatusAsync(Guid jobId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        const string sql = "SELECT Status FROM dbo.Jobs WHERE Id = @id;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", jobId);

        var result = await command.ExecuteScalarAsync();
        return (JobStatus)Convert.ToByte(result);
    }

    public async Task ExpireLeaseAsync(Guid jobId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        const string sql = """
            UPDATE dbo.Jobs
            SET LeaseExpiresAt = DATEADD(second, -1, SYSDATETIMEOFFSET())
            WHERE Id = @jobId;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@jobId", jobId);

        await command.ExecuteNonQueryAsync();
    }

    public async Task MakeLeaseExpireSoonAsync(Guid jobId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        const string sql = """
            UPDATE dbo.Jobs
            SET LeaseExpiresAt = DATEADD(second, 1, SYSDATETIMEOFFSET())
            WHERE Id = @jobId;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@jobId", jobId);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<(string? LockedBy, Guid? LeaseToken)> GetOwnershipAsync(Guid jobId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        const string sql = "SELECT LockedBy, LeaseToken FROM dbo.Jobs WHERE Id = @jobId;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@jobId", jobId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException($"Job '{jobId}' was not found.");
        }

        return (
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1));
    }

    public async Task<int> GetAttemptCountAsync(Guid jobId, string workerId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        const string sql = "SELECT COUNT(*) FROM dbo.JobAttempts WHERE JobId = @jobId AND WorkerId = @workerId;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@jobId", jobId);
        command.Parameters.AddWithValue("@workerId", workerId);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task<IReadOnlyList<(int AttemptNumber, string WorkerId, string Status, DateTimeOffset? FinishedAt)>> GetAttemptsAsync(Guid jobId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        const string sql = """
            SELECT AttemptNumber, WorkerId, Status, FinishedAt
            FROM dbo.JobAttempts
            WHERE JobId = @jobId
            ORDER BY AttemptNumber;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@jobId", jobId);

        await using var reader = await command.ExecuteReaderAsync();
        var attempts = new List<(int AttemptNumber, string WorkerId, string Status, DateTimeOffset? FinishedAt)>();

        while (await reader.ReadAsync())
        {
            attempts.Add((
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetDateTimeOffset(3)));
        }

        return attempts;
    }
}
