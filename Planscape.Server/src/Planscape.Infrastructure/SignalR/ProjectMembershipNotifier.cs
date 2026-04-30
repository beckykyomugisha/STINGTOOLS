using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<ProjectMembershipNotifier> _logger;

    public ProjectMembershipNotifier(IHubContext<NotificationHub> hub,
                                     HubConnectionRegistry registry,
                                     ILogger<ProjectMembershipNotifier> logger)
    {
        _hub = hub;
        _registry = registry;
        _logger = logger;
    }

    public async Task RevokeProjectAccessAsync(Guid userId, Guid projectId, CancellationToken ct = default)
    {
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
