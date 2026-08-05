using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Phase 175 audit P1-15-tx — atomic per-key counter for project-scoped
/// codes (transmittals, RFIs, NCRs, …). Replaces the previous pattern
/// of "SELECT every existing code, parse the suffix, take MAX + 1"
/// which was both O(N) per insert and racy under concurrent writes.
///
/// The implementation uses Postgres UPSERT (INSERT ... ON CONFLICT DO
/// UPDATE) with RETURNING so we get the post-increment value in one
/// round-trip and the unique (ProjectId, CounterKey) index serialises
/// concurrent allocations. Optionally takes a hint that lets the
/// counter "jump" to a higher value when migrating projects whose
/// existing data already passes the counter (the very first allocation
/// for a project that has historical TX-0042 codes should return 43,
/// not 1).
/// </summary>
public interface ISequenceCounterService
{
    /// <summary>
    /// Atomically allocate the next value for (projectId, counterKey).
    /// On a brand-new key the counter starts at <paramref name="seedFloor"/>+1
    /// (default 1) — supply seedFloor when migrating to make sure the
    /// new counter doesn't collide with legacy codes.
    /// </summary>
    Task<int> AllocateAsync(Guid tenantId, Guid projectId, string counterKey, int seedFloor = 0, int count = 1, string? updatedBy = null, CancellationToken ct = default);
}

public sealed class SequenceCounterService : ISequenceCounterService
{
    private readonly PlanscapeDbContext _db;

    public SequenceCounterService(PlanscapeDbContext db) => _db = db;

    public async Task<int> AllocateAsync(
        Guid tenantId, Guid projectId, string counterKey,
        int seedFloor = 0, int count = 1, string? updatedBy = null,
        CancellationToken ct = default)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        if (string.IsNullOrWhiteSpace(counterKey)) throw new ArgumentException("counterKey required", nameof(counterKey));

        // Postgres UPSERT with GREATEST(...) so seedFloor only takes
        // effect on first insert; subsequent calls just add `count`.
        // RETURNING gives us the post-update value atomically — no
        // SELECT FOR UPDATE + UPDATE round-trips, no race window.
        var sql = @"
            INSERT INTO ""SeqCounters""
                (""Id"", ""TenantId"", ""ProjectId"", ""CounterKey"",
                 ""CurrentValue"", ""UpdatedBy"", ""UpdatedAt"")
            VALUES (gen_random_uuid(), {0}, {1}, {2}, GREATEST({3}, 0) + {4}, {5}, now())
            ON CONFLICT (""ProjectId"", ""CounterKey"") DO UPDATE
                SET ""CurrentValue"" = GREATEST(""SeqCounters"".""CurrentValue"", {3}) + {4},
                    ""UpdatedBy""    = {5},
                    ""UpdatedAt""    = now()
            RETURNING ""CurrentValue""";

        var newValue = await _db.Database
            .SqlQueryRaw<int>(sql, tenantId, projectId, counterKey, seedFloor, count, updatedBy ?? "system")
            .FirstAsync(ct);

        // The returned value is the highest allocated. Caller starts
        // its loop at (newValue - count + 1).
        return newValue;
    }
}
