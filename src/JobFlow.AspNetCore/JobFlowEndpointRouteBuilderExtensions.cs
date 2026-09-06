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
        group.MapGet("/jobs", SearchJobsAsync);
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

    private static async Task<IResult> SearchJobsAsync(
        HttpContext context,
        IJobListAuthorization authorization,
        IJobQuery query,
        JobStatus? status,
        string? jobType,
        string? workerId,
        int? pageSize,
        string? cursor,
        CancellationToken ct)
    {
        if (!await authorization.CanSearchAsync(context.User, ct))
        {
            return Results.NotFound();
        }

        var page = await query.SearchAsync(
            new JobSearchCriteria(status, jobType, workerId, PageSize: pageSize ?? 50, Cursor: cursor),
            ct);
        return Results.Ok(page);
    }

    private sealed record JobDetailResponse(JobDetails Job, JobScene Scene);
}
