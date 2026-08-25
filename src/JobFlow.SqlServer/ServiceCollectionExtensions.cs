using System.Reflection;
using JobFlow.Core;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace JobFlow.SqlServer;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection UseSqlServerJobStore(this IServiceCollection services, string connectionString, Action<JobLeaseOptions>? configureLeaseOptions = null)
    {
        var leaseOptions = new JobLeaseOptions();
        configureLeaseOptions?.Invoke(leaseOptions);

        EnsureSchemaCreated(connectionString);

        services.AddSingleton<IJobStore>(_ => new SqlJobStore(connectionString, leaseOptions));
        services.AddSingleton<JobScheduler>();
        services.AddHostedService<JobDispatcher>();
        services.AddSingleton(leaseOptions);

        return services;
    }

    private static void EnsureSchemaCreated(string connectionString)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "JobFlow.SqlServer.Scripts.CreateJobsTable.sql";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not find embedded resource '{resourceName}'");
        
        using var reader = new StreamReader(stream);
        var script = reader.ReadToEnd();

        using var connection = new SqlConnection(connectionString);
        connection.Open();

        using var command = new SqlCommand(script, connection);
        command.ExecuteNonQuery();
    }
}