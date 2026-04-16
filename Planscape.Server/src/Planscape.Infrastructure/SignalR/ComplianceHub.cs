using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Planscape.Infrastructure.SignalR;

/// <summary>
/// Real-time compliance dashboard updates.
/// Web dashboard clients join a project group and receive live compliance metrics
/// when the Revit plugin syncs tagged elements.
/// </summary>
[Authorize]
public class ComplianceHub : Hub
{
    private static readonly ConcurrentDictionary<string, (string DeviceId, string ProjectId)> _connections = new();

    public async Task JoinProject(string projectId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"project-{projectId}");
        await Clients.Caller.SendAsync("Joined", projectId);
    }

    public async Task LeaveProject(string projectId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"project-{projectId}");
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

/// <summary>
/// Real-time tag sync notifications.
/// Revit plugin and web dashboard both connect here for live tag updates.
/// </summary>
[Authorize]
public class TagSyncHub : Hub
{
    private static readonly ConcurrentDictionary<string, (string DeviceId, string ProjectId)> _connections = new();

    public async Task JoinProject(string projectId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"project-{projectId}");
    }

    public async Task LeaveProject(string projectId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"project-{projectId}");
    }

    /// <summary>
    /// Called by the Revit plugin to notify that a tagging operation completed.
    /// </summary>
    public async Task NotifyTaggingComplete(string projectId, int elementsTagged, double compliancePct)
    {
        await Clients.Group($"project-{projectId}").SendAsync("TaggingComplete", new
        {
            elementsTagged,
            compliancePct,
            timestamp = DateTime.UtcNow
        });
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
