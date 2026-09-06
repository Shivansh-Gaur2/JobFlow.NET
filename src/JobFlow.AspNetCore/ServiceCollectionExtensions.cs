using JobFlow.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobFlow.AspNetCore;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddJobFlowWeb(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSignalR();
        services.TryAddSingleton<IJobViewerAuthorization, DenyAllJobViewerAuthorization>();
        return services;
    }
}
