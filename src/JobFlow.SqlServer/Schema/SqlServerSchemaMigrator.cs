using System.Reflection;
using Microsoft.Data.SqlClient;

namespace JobFlow.SqlServer;

internal sealed class SqlServerSchemaMigrator
{
    private static readonly SchemaMigration[] Migrations =
    [
        new(1, "Initialize schema", "JobFlow.SqlServer.Schema.Migrations.001-initialize-schema.sql"),
        new(2, "Add job attempt failure diagnostics", "JobFlow.SqlServer.Schema.Migrations.002-add-job-attempt-failure-diagnostics.sql"),
        new(3, "Add job lease recovery index", "JobFlow.SqlServer.Schema.Migrations.003-add-job-lease-recovery-index.sql")
    ];

    private readonly string _connectionString;

    public SqlServerSchemaMigrator(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _connectionString = connectionString;
    }

    public async Task ApplyAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);

        await AcquireMigrationLockAsync(connection, transaction, ct);
        await EnsureMigrationHistoryAsync(connection, transaction, ct);

        foreach (var migration in Migrations)
        {
            if (await IsAppliedAsync(connection, transaction, migration.Version, ct))
            {
                continue;
            }

            await ExecuteScriptAsync(connection, transaction, migration.ResourceName, ct);
            await RecordAppliedAsync(connection, transaction, migration, ct);
        }

        await transaction.CommitAsync(ct);
    }

    private static async Task AcquireMigrationLockAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken ct)
    {
        const string sql = """
            DECLARE @result INT;

            EXEC @result = sp_getapplock
                @Resource = N'JobFlow.SqlServer.SchemaMigrations',
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 60000;

            SELECT @result;
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(ct));

        if (result < 0)
        {
            throw new InvalidOperationException("Could not acquire the JobFlow SQL Server migration lock.");
        }
    }

    private static async Task EnsureMigrationHistoryAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken ct)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.JobFlowSchemaMigrations', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.JobFlowSchemaMigrations
                (
                    Version INT NOT NULL CONSTRAINT PK_JobFlowSchemaMigrations PRIMARY KEY,
                    Name NVARCHAR(200) NOT NULL,
                    AppliedAt DATETIMEOFFSET NOT NULL
                );
            END
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> IsAppliedAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int version,
        CancellationToken ct)
    {
        const string sql = """
            SELECT 1
            FROM dbo.JobFlowSchemaMigrations
            WHERE Version = @version;
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@version", version);

        return await command.ExecuteScalarAsync(ct) is not null;
    }

    private static async Task ExecuteScriptAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string resourceName,
        CancellationToken ct)
    {
        var assembly = typeof(SqlServerSchemaMigrator).Assembly;
        await using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not find embedded migration '{resourceName}'.");
        using var reader = new StreamReader(stream);
        var script = await reader.ReadToEndAsync(ct);

        await using var command = new SqlCommand(script, connection, transaction);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task RecordAppliedAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SchemaMigration migration,
        CancellationToken ct)
    {
        const string sql = """
            INSERT INTO dbo.JobFlowSchemaMigrations (Version, Name, AppliedAt)
            VALUES (@version, @name, SYSDATETIMEOFFSET());
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@version", migration.Version);
        command.Parameters.AddWithValue("@name", migration.Name);
        await command.ExecuteNonQueryAsync(ct);
    }

    private sealed record SchemaMigration(int Version, string Name, string ResourceName);
}
