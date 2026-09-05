using JobFlow.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JobFlow.SqlServer;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection UseSqlServerJobStore(
        this IServiceCollection services,
        string connectionString,
        Action<JobLeaseOptions>? configureLeaseOptions = null,
        Action<JobRetryOptions>? configureRetryOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var leaseOptions = new JobLeaseOptions();
        configureLeaseOptions?.Invoke(leaseOptions);
        leaseOptions.Validate();
        var retryOptions = new JobRetryOptions();
        configureRetryOptions?.Invoke(retryOptions);
        retryOptions.Validate();

        services.AddLogging();
        services.AddSingleton(_ => new SqlServerSchemaMigrator(connectionString));
        services.AddSingleton(_ => new SqlJobStore(connectionString, leaseOptions, retryOptions));
        services.AddSingleton<IJobStore>(serviceProvider => serviceProvider.GetRequiredService<SqlJobStore>());
        services.AddSingleton<IJobQuery>(serviceProvider => serviceProvider.GetRequiredService<SqlJobStore>());
        services.TryAddSingleton<IJobFailureClassifier, DefaultJobFailureClassifier>();
        services.TryAddSingleton<IJobRetryPolicy, ExponentialBackoffRetryPolicy>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<JobScheduler>();
        services.AddHostedService<JobDispatcher>();
        services.AddSingleton(leaseOptions);
        services.AddSingleton(retryOptions);

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
