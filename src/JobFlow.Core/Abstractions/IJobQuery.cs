namespace JobFlow.Core;

public interface IJobQuery
{
    Task<JobDetails?> GetAsync(Guid jobId, CancellationToken ct);
    Task<JobSearchPage> SearchAsync(JobSearchCriteria criteria, CancellationToken ct);
}