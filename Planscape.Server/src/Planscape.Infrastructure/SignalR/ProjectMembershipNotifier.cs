using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Planscape.Infrastructure.SignalR;

/// <summary>
/// S4 — single entry point a controller calls when a
/// <see cref="Planscape.Core.Entities.ProjectMember"/> is deactivated or
/// removed. Evicts every active SignalR connection of that user from the
/// project group so they stop receiving project-scoped events
/// immediately.
/// <para/>
/// Also fires a <c>MemberRevoked</c> event back to the user's personal
/// group so their UI can clear local project caches.
/// </summary>
public interface IProjectMembershipNotifier
{
    Task RevokeProjectAccessAsync(Guid userId, Guid projectId, CancellationToken ct = default);
}

public sealed class ProjectMembershipNotifier : IProjectMembershipNotifier
{
    private readonly IHubContext<NotificationHub> _hub;
    private readonly HubConnectionRegistry _registry;
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<ProjectMembershipNotifier> _logger;

    // Phase 175 audit P1-13 — keep this in sync with ProjectAccessCache
    // in the API project. Duplicated rather than shared because
    // Infrastructure must not reference API.
    private const string ProjectAccessCachePrefix = "Planscape:pv:";

    public ProjectMembershipNotifier(IHubContext<NotificationHub> hub,
                                     HubConnectionRegistry registry,
                                     ILogger<ProjectMembershipNotifier> logger,
                                     IConnectionMultiplexer? redis = null)
    {
        _hub = hub;
        _registry = registry;
        _redis = redis;
        _logger = logger;
    }

    public async Task RevokeProjectAccessAsync(Guid userId, Guid projectId, CancellationToken ct = default)
    {
        // Phase 175 audit P1-13 — invalidate the cached visibility
        // decision so the 30s window doesn't leave a removed member
        // with phantom access. Uses a Redis SCAN+DEL for the (any-tenant,
        // user, project) tuple — the IDistributedCache abstraction
        // doesn't expose pattern delete, so we go to Redis directly.
        // Best-effort: a Redis blip means we wait out the 30s TTL.
        if (_redis != null)
        {
            try
            {
                foreach (var endpoint in _redis.GetEndPoints())
                {
                    var server = _redis.GetServer(endpoint);
                    if (!server.IsConnected) continue;
                    var pattern = $"{ProjectAccessCachePrefix}*:{userId:N}:{projectId:N}";
                    await foreach (var key in server.KeysAsync(pattern: pattern).WithCancellation(ct))
                    {
                        await _redis.GetDatabase().KeyDeleteAsync(key);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ProjectAccess cache invalidation failed for user={User} project={Project}", userId, projectId);
            }
        }

        var groupName = $"project-{projectId}";
        var connections = _registry.GetConnections(userId);
        foreach (var conn in connections)
        {
            try
            {
                await _hub.Groups.RemoveFromGroupAsync(conn, groupName, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to evict connection {Conn} from group {Group}", conn, groupName);
            }
        }

        // Fire a client-side event so the UI clears caches. Even if the
        // group eviction above failed (e.g. connection just dropped), this
        // ensures any future cold-start session gets the cue too because
        // the same payload flows through the user's personal group.
        try
        {
            await _hub.Clients.Group($"user_{userId}").SendAsync(
                "MemberRevoked",
                new { projectId },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast MemberRevoked to user {UserId}", userId);
        }
    }
}
