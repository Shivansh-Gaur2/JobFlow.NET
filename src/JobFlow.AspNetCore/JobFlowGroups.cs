namespace JobFlow.AspNetCore;

internal static class JobFlowGroups
{
    public static string ForJob(Guid jobId) => $"jobflow:job:{jobId:N}";
}
