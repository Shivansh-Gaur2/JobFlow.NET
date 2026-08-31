namespace JobFlow.Core;

public class JobRecord
{
    public Guid Id { get; set; }
    public string JobType { get; set; } = string.Empty;
    public string? Payload { get; set; }
    public JobStatus JobStatus { get; set; }
    public DateTimeOffset NextRunAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int MaxRetries { get; set; } = 3;
    public int RetryCount { get; set; }
    public string? LockedBy { get; set; }
    public DateTimeOffset? LockedAt { get; set; }
}
