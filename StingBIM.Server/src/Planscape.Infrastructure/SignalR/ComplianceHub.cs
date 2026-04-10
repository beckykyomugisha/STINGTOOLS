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
    public async Task JoinProject(string projectId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, projectId);
        await Clients.Caller.SendAsync("Joined", projectId);
    }

    public async Task LeaveProject(string projectId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, projectId);
    }
}

/// <summary>
/// Real-time tag sync notifications.
/// Revit plugin and web dashboard both connect here for live tag updates.
/// </summary>
[Authorize]
public class TagSyncHub : Hub
{
    public async Task JoinProject(string projectId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, projectId);
    }

    public async Task LeaveProject(string projectId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, projectId);
    }

    /// <summary>
    /// Called by the Revit plugin to notify that a tagging operation completed.
    /// </summary>
    public async Task NotifyTaggingComplete(string projectId, int elementsTagged, double compliancePct)
    {
        await Clients.Group(projectId).SendAsync("TaggingComplete", new
        {
            elementsTagged,
            compliancePct,
            timestamp = DateTime.UtcNow
        });
    }
}
