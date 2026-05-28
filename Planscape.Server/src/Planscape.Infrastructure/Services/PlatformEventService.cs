using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// K2 keystone implementation. Append assigns a per-project monotonic
/// sequence, chains the SHA-256 hash to the prior event, persists, then fans
/// out over SignalR. Reads are tenant-scoped by the DbContext query filter.
/// </summary>
public sealed class PlatformEventService : IPlatformEventService
{
    private readonly PlanscapeDbContext _db;
    private readonly IHubContext<PlatformEventHub> _hub;

    /// <summary>Max retryable-failure attempts before an event becomes a poison message.</summary>
    private const int MaxAttempts = 5;

    public PlatformEventService(PlanscapeDbContext db, IHubContext<PlatformEventHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    public async Task<PlatformEvent> AppendAsync(PlatformEventAppend cmd, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.Type))
            throw new ArgumentException("Type is required", nameof(cmd));
        if (string.IsNullOrWhiteSpace(cmd.Source))
            throw new ArgumentException("Source is required", nameof(cmd));

        // Per-project monotonic sequence + hash chain. The (ProjectId,Sequence)
        // unique index makes a concurrent-append collision a DbUpdateException;
        // we recompute the tail and retry rather than corrupt the chain.
        const int maxSeqRetries = 5;
        for (var attempt = 1; ; attempt++)
        {
            var tail = await _db.PlatformEvents
                .Where(e => e.ProjectId == cmd.ProjectId)
                .OrderByDescending(e => e.Sequence)
                .Select(e => new { e.Sequence, e.RowHash })
                .FirstOrDefaultAsync(ct);

            var seq = (tail?.Sequence ?? 0) + 1;
            var prevHash = tail?.RowHash;

            var row = new PlatformEvent
            {
                TenantId = _db.CurrentTenantId,
                ProjectId = cmd.ProjectId,
                Sequence = seq,
                Source = cmd.Source,
                Type = cmd.Type,
                PayloadJson = string.IsNullOrWhiteSpace(cmd.PayloadJson) ? "{}" : cmd.PayloadJson,
                TargetIfcGlobalId = cmd.TargetIfcGlobalId,
                BaseRevisionId = cmd.BaseRevisionId,
                ActorUserId = cmd.ActorUserId,
                Status = PlatformEventStatus.Pending,
                CreatedUtc = DateTime.UtcNow,
                PrevHash = prevHash,
            };
            row.RowHash = ComputeHash(prevHash, row);

            _db.PlatformEvents.Add(row);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException) when (attempt < maxSeqRetries)
            {
                // Lost the sequence race — detach and retry with a fresh tail.
                _db.Entry(row).State = EntityState.Detached;
                continue;
            }

            // Fast path — live fan-out. Polling is the floor if a client is offline.
            await PlatformEventHub.NotifyAppended(_hub, cmd.ProjectId, ToDto(row));
            return row;
        }
    }

    public async Task<IReadOnlyList<PlatformEvent>> GetPendingAsync(
        Guid projectId, long sinceSequence = 0, int max = 200, CancellationToken ct = default)
    {
        max = Math.Clamp(max, 1, 1000);
        // Pending events plus retryable Failed events (under the attempt cap) —
        // so a transient handler failure actually gets re-served, while a poison
        // message (Attempts >= cap) drops out and waits for manual attention.
        return await _db.PlatformEvents
            .Where(e => e.ProjectId == projectId
                        && e.Sequence > sinceSequence
                        && (e.Status == PlatformEventStatus.Pending
                            || (e.Status == PlatformEventStatus.Failed && e.Attempts < MaxAttempts)))
            .OrderBy(e => e.Sequence)
            .Take(max)
            .ToListAsync(ct);
    }

    public async Task<bool> AckAsync(Guid eventId, CancellationToken ct = default)
    {
        var ev = await _db.PlatformEvents.FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null) return false;
        if (ev.Status == PlatformEventStatus.Applied) return true; // idempotent
        ev.Status = PlatformEventStatus.Applied;
        ev.AppliedUtc = DateTime.UtcNow;
        ev.StatusDetail = null;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RejectAsync(Guid eventId, string reason, bool retryable = false, CancellationToken ct = default)
    {
        var ev = await _db.PlatformEvents.FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null) return false;
        if (retryable)
        {
            ev.Status = PlatformEventStatus.Failed;
            ev.Attempts += 1; // GetPending stops re-serving once this hits the cap
        }
        else
        {
            ev.Status = PlatformEventStatus.Rejected;
        }
        ev.StatusDetail = reason;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static string ComputeHash(string? prevHash, PlatformEvent e)
    {
        var canonical = string.Join('|',
            prevHash ?? "",
            e.Sequence,
            e.ProjectId,
            e.Source,
            e.Type,
            e.PayloadJson,
            e.TargetIfcGlobalId ?? "",
            e.BaseRevisionId ?? "",
            e.CreatedUtc.ToString("O"));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes);
    }

    private static object ToDto(PlatformEvent e) => new
    {
        id = e.Id,
        sequence = e.Sequence,
        source = e.Source,
        type = e.Type,
        payloadJson = e.PayloadJson,
        targetIfcGlobalId = e.TargetIfcGlobalId,
        baseRevisionId = e.BaseRevisionId,
        status = e.Status.ToString(),
        createdUtc = e.CreatedUtc,
    };
}
