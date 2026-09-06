using System.Security.Claims;

namespace JobFlow.Core;

/// <summary>Lets the host decide whether a viewer may enumerate operational jobs.</summary>
public interface IJobListAuthorization
{
    Task<bool> CanSearchAsync(ClaimsPrincipal viewer, CancellationToken ct);
}
