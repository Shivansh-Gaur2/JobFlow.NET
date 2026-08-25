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
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand("DELETE FROM dbo.Jobs;", connection);
        await command.ExecuteNonQueryAsync();
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
}
