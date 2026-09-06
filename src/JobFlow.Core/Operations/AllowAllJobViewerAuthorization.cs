using System.Security.Claims;

namespace JobFlow.Core;

/// <summary>
/// Explicit opt-in policy for trusted internal operations screens.
/// Do not use this policy for customer-facing routes.
/// </summary>
public sealed class AllowAllJobViewerAuthorization : IJobViewerAuthorization
{
    public Task<bool> CanViewAsync(ClaimsPrincipal viewer, Guid jobId, CancellationToken ct)
        => Task.FromResult(true);
}
