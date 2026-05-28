using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.SignalR;

/// <summary>
/// K2 keystone — live fan-out for the platform event spine. Clients (STING
/// plugin, web, mobile) join a project group and receive <c>PlatformEvent</c>
/// pushes the instant an event is appended. The plugin still polls
/// GET /events/pending as a resilience fallback when the socket drops, so no
/// event is ever lost — SignalR is the fast path, polling the floor.
/// </summary>
[Authorize]
public class PlatformEventHub : Hub
{
    private readonly PlanscapeDbContext _db;
    public PlatformEventHub(PlanscapeDbContext db) => _db = db;

    public async Task JoinProject(string projectId)
    {
        if (Guid.TryParse(projectId, out var pid)
            && await HubTenantGuard.OwnsProjectAsync(Context.User, _db, pid))
            await Groups.AddToGroupAsync(Context.ConnectionId, EventGroup(projectId));
    }

    public async Task LeaveProject(string projectId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, EventGroup(projectId));

    /// <summary>
    /// Push a freshly-appended event to every client watching the project.
    /// Called by <see cref="Planscape.Infrastructure.Services.PlatformEventService"/>
    /// after the row is persisted.
    /// </summary>
    public static async Task NotifyAppended(
        IHubContext<PlatformEventHub> hub,
        Guid projectId,
        object eventDto)
    {
        await hub.Clients
            .Group(EventGroup(projectId.ToString()))
            .SendAsync("PlatformEvent", eventDto);
    }

    private static string EventGroup(string projectId) => $"events:{projectId}";
}
