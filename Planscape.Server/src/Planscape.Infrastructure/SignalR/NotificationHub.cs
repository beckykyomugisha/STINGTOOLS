using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Planscape.Infrastructure.SignalR;

/// <summary>
/// Real-time notification hub for cross-project alerts.
/// Clients join their tenant group to receive broadcast notifications,
/// or receive direct user-targeted messages.
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
    private static readonly ConcurrentDictionary<string, (string DeviceId, string ProjectId)> _connections = new();

    /// <summary>
    /// Join a project notification group for project-scoped alerts.
    /// </summary>
    public async Task JoinProject(string projectId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"project-{projectId}");
        await Clients.Caller.SendAsync("JoinedProject", projectId);
    }

    /// <summary>
    /// Leave a project notification group.
    /// </summary>
    public async Task LeaveProject(string projectId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"project-{projectId}");
    }

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

    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        var deviceId = httpContext?.Request.Query["device_id"].ToString() ?? "";
        var projectId = httpContext?.Request.Query["project_id"].ToString() ?? "";
        _connections[Context.ConnectionId] = (deviceId, projectId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _connections.TryRemove(Context.ConnectionId, out _);
        await base.OnDisconnectedAsync(exception);
    }
}
