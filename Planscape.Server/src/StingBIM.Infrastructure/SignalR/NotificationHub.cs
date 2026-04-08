using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace StingBIM.Infrastructure.SignalR;

/// <summary>
/// Real-time notification hub for cross-project alerts.
/// Clients join their tenant group to receive broadcast notifications,
/// or receive direct user-targeted messages.
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
    /// <summary>
    /// Join the tenant notification group to receive broadcast notifications.
    /// </summary>
    public async Task JoinTenant(string tenantId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant_{tenantId}");
        await Clients.Caller.SendAsync("JoinedTenant", tenantId);
    }

    /// <summary>
    /// Leave the tenant notification group.
    /// </summary>
    public async Task LeaveTenant(string tenantId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"tenant_{tenantId}");
    }

    /// <summary>
    /// Register for user-specific notifications by joining a personal group.
    /// </summary>
    public async Task RegisterUser(string userId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
        await Clients.Caller.SendAsync("RegisteredUser", userId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}
