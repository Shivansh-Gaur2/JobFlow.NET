using JobFlow.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace JobFlow.AspNetCore;

public static class JobFlowEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapJobFlow(this IEndpointRouteBuilder endpoints, string pattern = "/jobflow")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        var group = endpoints.MapGroup(pattern).RequireAuthorization();
        group.MapHub<JobFlowHub>("/updates");
        group.MapGet("/jobs/{jobId:guid}", GetJobAsync);
        return endpoints;
    }

    private static async Task<IResult> GetJobAsync(
        Guid jobId,
        HttpContext context,
        IJobViewerAuthorization authorization,
        IJobQuery query,
        CancellationToken ct)
    {
        if (!await authorization.CanViewAsync(context.User, jobId, ct))
        {
            return Results.NotFound();
        }

        var job = await query.GetAsync(jobId, ct);
        return job is null ? Results.NotFound() : Results.Ok(new JobDetailResponse(job, JobSceneMapper.Map(job)));
    }

    private sealed record JobDetailResponse(JobDetails Job, JobScene Scene);
}
