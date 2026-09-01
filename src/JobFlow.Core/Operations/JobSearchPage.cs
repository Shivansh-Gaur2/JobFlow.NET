namespace JobFlow.Core;
public sealed record JobSearchPage(IReadOnlyList<JobSummary> Jobs, string? NextCursor);