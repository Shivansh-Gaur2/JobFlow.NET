using System.Security.Claims;

namespace JobFlow.Core;

/// <summary>
/// Lets the host application decide whether its current user may inspect a job.
/// JobFlow deliberately does not infer ownership from a job payload.
/// </summary>
public interface IJobViewerAuthorization
{
    Task<bool> CanViewAsync(ClaimsPrincipal viewer, Guid jobId, CancellationToken ct);
}
