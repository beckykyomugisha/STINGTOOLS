using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.Infrastructure.SignalR;

/// <summary>
/// S6.3 — CRDT relay hub. Clients call Subscribe(docKey) to join the
/// group, then Push(docKey, base64Update) to broadcast their changes.
/// Server persists every update before fan-out so a late-joining peer
/// can call PullSince(docKey, lastSeenAt) and replay missed updates.
///
/// CRDT format is opaque to the server — we're a transport layer, not
/// a merge engine.
/// </summary>
[Authorize]
public class CrdtHub : Hub
{
    private readonly PlanscapeDbContext _db;
    private readonly ITenantContext _tenant;

    public CrdtHub(PlanscapeDbContext db, ITenantContext tenant)
    {
        _db = db; _tenant = tenant;
    }

    public async Task Subscribe(string docKey)
    {
        // Phase 175 audit P0-4 — gate on project visibility. Until now any
        // logged-in tenant user could subscribe to any docKey by guessing.
        await EnsureCanAccessAsync(docKey);
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(docKey));
    }

    public Task Unsubscribe(string docKey)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(docKey));

    public async Task Push(string docKey, string updateBase64)
    {
        await EnsureCanAccessAsync(docKey);
        var userId = TryParseUserId();
        var row = new PinCrdtUpdate
        {
            TenantId     = _tenant.TenantId,
            DocKey       = docKey,
            UpdateBase64 = updateBase64,
            AuthorUserId = userId,
        };
        _db.PinCrdtUpdates.Add(row);
        await _db.SaveChangesAsync();

        // Fan out to every peer in the group except the sender.
        await Clients.OthersInGroup(GroupName(docKey)).SendAsync("CrdtUpdate", new
        {
            docKey,
            updateBase64,
            authorUserId = userId,
            createdAt = row.CreatedAt,
        });
    }

    public async Task<IEnumerable<object>> PullSince(string docKey, DateTime sinceUtc)
    {
        await EnsureCanAccessAsync(docKey);
        var rows = await _db.PinCrdtUpdates.AsNoTracking()
            .Where(u => u.DocKey == docKey && u.CreatedAt > sinceUtc)
            .OrderBy(u => u.CreatedAt)
            .Take(500)
            .ToListAsync();
        return rows.Select(u => new { u.UpdateBase64, u.AuthorUserId, u.CreatedAt, u.IsSnapshot });
    }

    private static string GroupName(string docKey) => "crdt:" + docKey;

    private Guid? TryParseUserId()
        => Guid.TryParse(Context.User?.FindFirst("sub")?.Value, out var id) ? id : null;

    /// <summary>
    /// Resolve a CRDT docKey to its owning project and 403 if the caller
    /// can't see it. Recognised shapes (see <see cref="PinCrdtUpdate.DocKey"/>):
    ///   - <c>project:&lt;guid&gt;:...</c>  → project id is in the key
    ///   - <c>issue:&lt;guid&gt;</c>         → look up the issue's project id
    /// Anything else throws — opaque keys would let an attacker invent
    /// docKeys outside any project's scope and leak fan-out.
    /// </summary>
    private async Task EnsureCanAccessAsync(string docKey)
    {
        if (Context.User == null)
            throw new HubException("Not authenticated");
        var projectId = await ResolveProjectIdAsync(docKey);
        if (projectId == null)
            throw new HubException("Cannot access");
        var ok = await ProjectVisibility.CanSeeProjectAsync(_db, projectId.Value, Context.User);
        if (!ok)
            throw new HubException("Cannot access");
    }

    private async Task<Guid?> ResolveProjectIdAsync(string docKey)
    {
        if (string.IsNullOrWhiteSpace(docKey)) return null;

        if (docKey.StartsWith("project:", StringComparison.Ordinal))
        {
            var afterPrefix = docKey.Substring(8);
            var sep = afterPrefix.IndexOf(':');
            var guidPart = sep >= 0 ? afterPrefix.Substring(0, sep) : afterPrefix;
            return Guid.TryParse(guidPart, out var pid) ? pid : null;
        }

        if (docKey.StartsWith("issue:", StringComparison.Ordinal))
        {
            var guidPart = docKey.Substring(6);
            if (!Guid.TryParse(guidPart, out var iid)) return null;
            return await _db.Issues.AsNoTracking()
                .Where(i => i.Id == iid)
                .Select(i => (Guid?)i.ProjectId)
                .FirstOrDefaultAsync();
        }

        return null;
    }
}
