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

    /// <summary>کلاینت Blazor قبل از شروع پروویژن این متد را فراخوانی می‌کند تا لاگ زنده دریافت کند.</summary>
    public async Task JoinProvisionSession(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"prov-{sessionId}");
    }

    public async Task LeaveProvisionSession(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"prov-{sessionId}");
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
