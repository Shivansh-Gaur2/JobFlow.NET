namespace JobFlow.Core;

public sealed record JobSearchCriteria(JobStatus? Status = null, string? JobType = null, string? WorkerId = null, DateTimeOffset? CreatedFrom = null, DateTimeOffset? CreatedTo = null, int PageSize = 50, string? Cursor = null);