using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.SignalR;

/// <summary>
/// Real-time notification hub for cross-project alerts.
/// Clients join their tenant group to receive broadcast notifications,
/// or receive direct user-targeted messages.
/// NEW-LOGIC-15 — all Join* methods now validate the caller's identity
/// matches their claims / project membership before adding them to a group.
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
    private static readonly ConcurrentDictionary<string, (string DeviceId, string ProjectId)> _connections = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PresenceTracker _presence;
    private readonly HubConnectionRegistry _connectionRegistry;

    public NotificationHub(IServiceScopeFactory scopeFactory,
                           PresenceTracker presence,
                           HubConnectionRegistry connectionRegistry)
    {
        _scopeFactory = scopeFactory;
        _presence = presence;
        _connectionRegistry = connectionRegistry;
    }

    private Guid? GetCallerUserId()
    {
        var claim = Context.User?.FindFirst("user_id")?.Value
                 ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                 ?? Context.User?.FindFirst("sub")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    private Guid? GetCallerTenantId()
    {
        var claim = Context.User?.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    /// <summary>
    /// Join a project notification group — only if the caller is an active member of that project.
    /// </summary>
    public async Task JoinProject(string projectId)
    {
        if (!Guid.TryParse(projectId, out var pid))
            throw new HubException("Invalid project id");
        var userId = GetCallerUserId();
        if (userId == null)
            throw new HubException("Not authenticated");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        var isMember = await db.ProjectMembers.AnyAsync(m =>
            m.ProjectId == pid && m.UserId == userId.Value && m.IsActive);
        if (!isMember)
            throw new HubException("Not a member of this project");

        await Groups.AddToGroupAsync(Context.ConnectionId, $"project-{projectId}");

        // T3 — presence: record the user + broadcast the delta.
        var displayName = Context.User?.FindFirst("display_name")?.Value
                       ?? Context.User?.Identity?.Name
                       ?? "Unknown";
        _presence.Join(pid, Context.ConnectionId,
            new PresentUser(userId.Value, displayName, "web"));
        await Clients.Caller.SendAsync("JoinedProject", projectId);
        await Clients.Group($"project-{projectId}").SendAsync("PresenceChanged", new
        {
            projectId,
            users = _presence.ProjectUsers(pid),
        });
    }

    public async Task LeaveProject(string projectId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"project-{projectId}");
        // T3 — drop from the presence tracker + fire a delta.
        var affected = _presence.Leave(Context.ConnectionId);
        foreach (var pid in affected)
        {
            await Clients.Group($"project-{pid}").SendAsync("PresenceChanged", new
            {
                projectId = pid,
                users = _presence.ProjectUsers(pid),
            });
        }
    }

    /// <summary>
    /// Join the tenant notification group — only if the caller's JWT claim tenant_id matches.
    /// </summary>
    public async Task JoinTenant(string tenantId)
    {
        if (!Guid.TryParse(tenantId, out var tid))
            throw new HubException("Invalid tenant id");
        var callerTenant = GetCallerTenantId();
        if (callerTenant == null || callerTenant.Value != tid)
            throw new HubException("Tenant mismatch");

        await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant_{tenantId}");
        await Clients.Caller.SendAsync("JoinedTenant", tenantId);
    }

    public async Task LeaveTenant(string tenantId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"tenant_{tenantId}");
    }

    /// <summary>
    /// Register for user-specific notifications — only for the caller's own user id.
    /// </summary>
    public async Task RegisterUser(string userId)
    {
        if (!Guid.TryParse(userId, out var uid))
            throw new HubException("Invalid user id");
        var callerId = GetCallerUserId();
        if (callerId == null || callerId.Value != uid)
            throw new HubException("Cannot register for another user");

        await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
        await Clients.Caller.SendAsync("RegisteredUser", userId);
    }

    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        var deviceId = httpContext?.Request.Query["device_id"].ToString() ?? "";
        var projectId = httpContext?.Request.Query["project_id"].ToString() ?? "";
        _connections[Context.ConnectionId] = (deviceId, projectId);

        // Auto-register the caller in their personal + tenant groups so they always
        // get direct messages even if the client forgets to call RegisterUser().
        var userId = GetCallerUserId();
        var tenantId = GetCallerTenantId();
        if (userId.HasValue)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
            // S4 — record the connection so MemberRemoved can evict it later.
            _connectionRegistry.Track(userId.Value, Context.ConnectionId);
        }
        if (tenantId.HasValue)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant_{tenantId}");

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _connections.TryRemove(Context.ConnectionId, out _);
        // S4 — drop the connection from the registry so revoke-loops don't
        // try to kick a stale id.
        var disconnectingUserId = GetCallerUserId();
        if (disconnectingUserId.HasValue)
            _connectionRegistry.Untrack(disconnectingUserId.Value, Context.ConnectionId);
        // T3 — broadcast PresenceChanged on hard disconnect.
        var affected = _presence.Leave(Context.ConnectionId);
        foreach (var pid in affected)
        {
            try
            {
                await Clients.Group($"project-{pid}").SendAsync("PresenceChanged", new
                {
                    projectId = pid,
                    users = _presence.ProjectUsers(pid),
                });
            }
            catch { /* ignore — client already gone */ }
        }
        await base.OnDisconnectedAsync(exception);
    }
}
