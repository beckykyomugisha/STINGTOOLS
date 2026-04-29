using System.Collections.Concurrent;

namespace Planscape.Infrastructure.SignalR;

/// <summary>
/// S4 — tracks active SignalR connections per user so that when a
/// <see cref="Planscape.Core.Entities.ProjectMember"/> row is deactivated
/// or deleted the running connections can be evicted from the project
/// group, not just future <c>JoinProject</c> calls denied.
/// <para/>
/// Without this, a user removed from a project keeps receiving every
/// project-scoped SignalR message until they disconnect.
/// </summary>
public sealed class HubConnectionRegistry
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> _userConnections = new();

    public void Track(Guid userId, string connectionId)
    {
        var bucket = _userConnections.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>());
        bucket[connectionId] = 0;
    }

    public void Untrack(Guid userId, string connectionId)
    {
        if (_userConnections.TryGetValue(userId, out var bucket))
        {
            bucket.TryRemove(connectionId, out _);
            if (bucket.IsEmpty)
            {
                _userConnections.TryRemove(userId, out _);
            }
        }
    }

    public IReadOnlyCollection<string> GetConnections(Guid userId)
    {
        return _userConnections.TryGetValue(userId, out var bucket)
            ? bucket.Keys.ToArray()
            : Array.Empty<string>();
    }
}
