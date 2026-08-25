namespace JobFlow.Core;

public sealed class JobLeaseOptions
{
    public TimeSpan LeaseDuration {get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan RenewalInterval {get; set; } = TimeSpan.FromMinutes(1);
}