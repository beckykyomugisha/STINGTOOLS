using System.Collections.Concurrent;

namespace Planscape.Infrastructure.SignalR;

/// <summary>
/// Tier 3 — In-memory presence tracker. Each SignalR connection registers
/// its user + active project on <see cref="NotificationHub.OnConnectedAsync"/>
/// and de-registers on disconnect. The hub broadcasts
/// <c>PresenceChanged</c> events to the project group so the BCC + web
/// dashboard can show "N people viewing" chips.
///
/// In-memory is intentional: the data is lifecycle-scoped to the connection,
/// and SignalR already needs the Redis backplane for horizontal scale. For
/// multi-node deployments, push the presence state through Redis (keyed by
/// projectId) — the API surface stays the same.
/// </summary>
public class PresenceTracker
{
    // key: projectId (Guid) → connectionId → user info
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, PresentUser>> _byProject = new();

    // key: connectionId → (projectId, user) — lets OnDisconnected clean up
    // without re-enumerating every project.
    private readonly ConcurrentDictionary<string, (Guid projectId, PresentUser user)> _byConnection = new();

    public void Join(Guid projectId, string connectionId, PresentUser user)
    {
        var dict = _byProject.GetOrAdd(projectId, _ => new ConcurrentDictionary<string, PresentUser>());
        dict[connectionId] = user;
        _byConnection[connectionId] = (projectId, user);
    }

    /// <summary>Remove a connection from all projects. Returns projects that lost a user.</summary>
    public IEnumerable<Guid> Leave(string connectionId)
    {
        if (!_byConnection.TryRemove(connectionId, out var entry))
            return Array.Empty<Guid>();

        if (_byProject.TryGetValue(entry.projectId, out var dict) &&
            dict.TryRemove(connectionId, out _))
        {
            return new[] { entry.projectId };
        }
        return Array.Empty<Guid>();
    }

    public IReadOnlyList<PresentUser> ProjectUsers(Guid projectId)
    {
        if (!_byProject.TryGetValue(projectId, out var dict)) return Array.Empty<PresentUser>();
        // Distinct by userId so someone with 2 devices open shows as one.
        return dict.Values
            .GroupBy(u => u.UserId)
            .Select(g => g.First())
            .ToArray();
    }
}

public sealed record PresentUser(Guid UserId, string DisplayName, string? Source);
