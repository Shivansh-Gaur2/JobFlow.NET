using JobFlow.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace JobFlow.AspNetCore;

[Authorize]
public sealed class JobFlowHub : Hub
{
    public async Task WatchJob(Guid jobId, IJobViewerAuthorization authorization)
    {
        if (!await authorization.CanViewAsync(Context.User ?? new System.Security.Claims.ClaimsPrincipal(), jobId, Context.ConnectionAborted))
        {
            throw new HubException("The requested job is unavailable.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, JobFlowGroups.ForJob(jobId), Context.ConnectionAborted);
    }
}
