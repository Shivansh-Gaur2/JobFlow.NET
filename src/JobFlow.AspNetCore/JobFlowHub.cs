using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace JobFlow.AspNetCore;

[Authorize]
public sealed class JobFlowHub : Hub
{
}
