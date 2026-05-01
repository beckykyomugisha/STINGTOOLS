using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

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

    public Task Subscribe(string docKey)
        => Groups.AddToGroupAsync(Context.ConnectionId, GroupName(docKey));

    public Task Unsubscribe(string docKey)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(docKey));

    public async Task Push(string docKey, string updateBase64)
    {
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
}
