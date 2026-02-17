using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AccessControll.API.Hubs;

[Authorize]
public class DoorHub : Hub
{
    /// <summary>کلاینت‌ها برای گروه‌بندی بر اساس دسترسی می‌توانند join کنند</summary>
    public async Task JoinDoorGroup(string doorId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"door-{doorId}");
    }

    public async Task LeaveDoorGroup(string doorId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"door-{doorId}");
    }

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "all-doors");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "all-doors");
        await base.OnDisconnectedAsync(exception);
    }
}
