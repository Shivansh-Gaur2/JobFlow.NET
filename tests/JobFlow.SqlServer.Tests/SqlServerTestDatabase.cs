using JobFlow.Core;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace JobFlow.SqlServer.Tests;

public sealed class SqlServerTestDatabase
{
    public SqlServerTestDatabase()
    {
        ConnectionString = Environment.GetEnvironmentVariable("JOBFLOW_TEST_CONNECTION")
            ?? throw new InvalidOperationException(
                "Set JOBFLOW_TEST_CONNECTION before running the SQL Server integration tests.");

        var services = new ServiceCollection();
        services.UseSqlServerJobStore(ConnectionString);
    }

    public string ConnectionString { get; }

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
}
