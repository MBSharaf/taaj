using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace LabStage.Api.Hubs;

[Authorize]
public sealed class OrderHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
        }
        await base.OnConnectedAsync();
    }

    public Task JoinOrderGroup(string orderNumber) =>
        Groups.AddToGroupAsync(Context.ConnectionId, $"order:{orderNumber}");
}
