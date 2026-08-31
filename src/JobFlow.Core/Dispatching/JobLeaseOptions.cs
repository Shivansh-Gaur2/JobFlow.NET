namespace JobFlow.Core;

public sealed class JobLeaseOptions
{
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan RenewalInterval { get; set; } = TimeSpan.FromMinutes(1);

    public void Validate()
    {
        if (LeaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(LeaseDuration),
                "Lease duration must be greater than zero.");
        }

        if (RenewalInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RenewalInterval),
                "Lease renewal interval must be greater than zero.");
        }

        if (RenewalInterval >= LeaseDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RenewalInterval),
                "Lease renewal interval must be shorter than the lease duration.");
        }
    }
}
