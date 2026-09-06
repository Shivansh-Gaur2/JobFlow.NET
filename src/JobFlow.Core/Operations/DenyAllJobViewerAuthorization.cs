using System.Security.Claims;

namespace JobFlow.Core;

/// <summary>
/// Safe default for web hosts that have not supplied an application-specific policy.
/// </summary>
public sealed class DenyAllJobViewerAuthorization : IJobViewerAuthorization
{
    public Task<bool> CanViewAsync(ClaimsPrincipal viewer, Guid jobId, CancellationToken ct)
        => Task.FromResult(false);
}
