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
        using var serviceProvider = services.BuildServiceProvider();
        await serviceProvider.ApplyJobFlowSqlServerMigrationsAsync();
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

    public async Task SetCreatedAtAsync(Guid jobId, DateTimeOffset createdAt)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        const string sql = "UPDATE dbo.Jobs SET CreatedAt = @createdAt WHERE Id = @jobId;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@createdAt", createdAt);
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

    public async Task<(Guid? ErrorId, string? FailureType, string? FailureMessage)> GetAttemptFailureAsync(Guid jobId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        const string sql = """
            SELECT ErrorId, FailureType, FailureMessage
            FROM dbo.JobAttempts
            WHERE JobId = @jobId;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@jobId", jobId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException($"No attempt was found for job '{jobId}'.");
        }

        return (
            reader.IsDBNull(0) ? null : reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    public async Task<string> CreateDatabaseAsync(string databaseName)
    {
        var masterConnectionString = _container.GetConnectionString();
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand($"CREATE DATABASE [{databaseName}];", connection);
        await command.ExecuteNonQueryAsync();

        return new SqlConnectionStringBuilder(masterConnectionString)
        {
            InitialCatalog = databaseName
        }.ConnectionString;
    }

    public static async Task ApplyLegacySchemaAsync(string connectionString)
    {
        var assembly = typeof(SqlJobStore).Assembly;
        const string resourceName = "JobFlow.SqlServer.Schema.Migrations.001-initialize-schema.sql";

        await using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not find embedded migration '{resourceName}'.");
        using var reader = new StreamReader(stream);
        var script = await reader.ReadToEndAsync();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(script, connection);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<bool> HasColumnAsync(string connectionString, string tableName, string columnName)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        const string sql = "SELECT CASE WHEN COL_LENGTH(@tableName, @columnName) IS NULL THEN 0 ELSE 1 END;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tableName", tableName);
        command.Parameters.AddWithValue("@columnName", columnName);

        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    public static async Task<bool> HasIndexAsync(string connectionString, string tableName, string indexName)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        const string sql = """
            SELECT CASE WHEN EXISTS
            (
                SELECT 1
                FROM sys.indexes
                WHERE object_id = OBJECT_ID(@tableName)
                    AND name = @indexName
            ) THEN 1 ELSE 0 END;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tableName", tableName);
        command.Parameters.AddWithValue("@indexName", indexName);

        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    public static async Task<IReadOnlyList<int>> GetAppliedMigrationVersionsAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        const string sql = "SELECT Version FROM dbo.JobFlowSchemaMigrations ORDER BY Version;";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var versions = new List<int>();
        while (await reader.ReadAsync())
        {
            versions.Add(reader.GetInt32(0));
        }

        return versions;
    }

    public async Task<IReadOnlyList<(int AttemptNumber, string WorkerId, string Status, DateTimeOffset? FinishedAt, Guid? ErrorId, string? FailureType, string? FailureMessage)>> GetAttemptsAsync(Guid jobId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        const string sql = """
            SELECT AttemptNumber, WorkerId, Status, FinishedAt,
                   ErrorId, FailureType, FailureMessage
            FROM dbo.JobAttempts
            WHERE JobId = @jobId
            ORDER BY AttemptNumber;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@jobId", jobId);

        await using var reader = await command.ExecuteReaderAsync();
        var attempts = new List<(int AttemptNumber, string WorkerId, string Status, DateTimeOffset? FinishedAt, Guid? ErrorId, string? FailureType, string? FailureMessage)>();

        while (await reader.ReadAsync())
        {
            attempts.Add((
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetDateTimeOffset(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return attempts;
    }
}
