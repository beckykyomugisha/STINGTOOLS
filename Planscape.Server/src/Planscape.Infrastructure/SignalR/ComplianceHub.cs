using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.Infrastructure.SignalR;

/// <summary>
/// Real-time compliance dashboard updates.
/// NEW-LOGIC-15 — JoinProject validates project membership before adding to group.
/// </summary>
[Authorize]
public class ComplianceHub : Hub
{
    private static readonly ConcurrentDictionary<string, (string DeviceId, string ProjectId)> _connections = new();
    private readonly IServiceScopeFactory _scopeFactory;

    public ComplianceHub(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    private Guid? GetCallerUserId()
    {
        var claim = Context.User?.FindFirst("user_id")?.Value
                 ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                 ?? Context.User?.FindFirst("sub")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    public async Task JoinProject(string projectId)
    {
        // Phase 175 audit P0-2 + P2-21 — visibility is author OR active
        // member OR tenant admin (matches REST). Single opaque error
        // message so callers can't distinguish "doesn't exist" from
        // "you're not a member".
        if (Context.User == null) throw new HubException("Cannot join");
        if (!Guid.TryParse(projectId, out var pid)) throw new HubException("Cannot join");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        var ok = await ProjectVisibility.CanSeeProjectAsync(db, pid, Context.User);
        if (!ok) throw new HubException("Cannot join");

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
/// NEW-LOGIC-15 — JoinProject validates project membership before adding to group.
/// </summary>
[Authorize]
public class TagSyncHub : Hub
{
    private static readonly ConcurrentDictionary<string, (string DeviceId, string ProjectId)> _connections = new();
    private readonly IServiceScopeFactory _scopeFactory;

    public TagSyncHub(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    private Guid? GetCallerUserId()
    {
        var claim = Context.User?.FindFirst("user_id")?.Value
                 ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                 ?? Context.User?.FindFirst("sub")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    public async Task JoinProject(string projectId)
    {
        // Phase 175 audit P0-2 + P2-21 — opaque error, visibility check
        // matches REST (author OR member OR tenant admin).
        if (Context.User == null) throw new HubException("Cannot join");
        if (!Guid.TryParse(projectId, out var pid)) throw new HubException("Cannot join");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        var ok = await ProjectVisibility.CanSeeProjectAsync(db, pid, Context.User);
        if (!ok) throw new HubException("Cannot join");

        await Groups.AddToGroupAsync(Context.ConnectionId, $"project-{projectId}");
    }

    public async Task LeaveProject(string projectId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"project-{projectId}");
    }

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
