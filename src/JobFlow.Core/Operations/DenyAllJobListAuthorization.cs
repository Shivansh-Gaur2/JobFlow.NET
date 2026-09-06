using System.Security.Claims;

namespace JobFlow.Core;

public sealed class DenyAllJobListAuthorization : IJobListAuthorization
{
    public Task<bool> CanSearchAsync(ClaimsPrincipal viewer, CancellationToken ct)
        => Task.FromResult(false);
}
