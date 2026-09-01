using JobFlow.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JobFlow.SqlServer;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection UseSqlServerJobStore(this IServiceCollection services, string connectionString, Action<JobLeaseOptions>? configureLeaseOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var leaseOptions = new JobLeaseOptions();
        configureLeaseOptions?.Invoke(leaseOptions);
        leaseOptions.Validate();

        services.AddLogging();
        services.AddSingleton(_ => new SqlServerSchemaMigrator(connectionString));
        services.AddSingleton(_ => new SqlJobStore(connectionString, leaseOptions));
        services.AddSingleton<IJobStore>(serviceProvider => serviceProvider.GetRequiredService<SqlJobStore>());
        services.AddSingleton<IJobQuery>(serviceProvider => serviceProvider.GetRequiredService<SqlJobStore>());
        services.TryAddSingleton<IJobFailureClassifier, DefaultJobFailureClassifier>();
        services.AddSingleton<JobScheduler>();
        services.AddHostedService<JobDispatcher>();
        services.AddSingleton(leaseOptions);

        return services;
    }

    public static Task ApplyJobFlowSqlServerMigrationsAsync(
        this IServiceProvider services,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services
            .GetRequiredService<SqlServerSchemaMigrator>()
            .ApplyAsync(ct);
    }
}
