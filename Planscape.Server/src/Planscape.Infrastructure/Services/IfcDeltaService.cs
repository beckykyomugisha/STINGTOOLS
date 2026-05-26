using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

// ── Public contract ────────────────────────────────────────────────────────

public interface IIfcDeltaService
{
    /// <summary>
    /// Compare the supplied <paramref name="elements"/> (from a fresh IFC ingest
    /// pass) against the most recent snapshot stored for <paramref name="projectModelId"/>,
    /// persist a new generation of <see cref="IfcElementSnapshot"/> rows, and
    /// return a summary of what changed.
    /// </summary>
    Task<IfcDeltaReport> ComputeAndPersistAsync(
        Guid tenantId,
        Guid projectId,
        Guid projectModelId,
        int uploadSequence,
        IReadOnlyList<IfcElementProperties> elements,
        CancellationToken ct);
}

/// <summary>
/// Immutable summary returned by <see cref="IIfcDeltaService.ComputeAndPersistAsync"/>.
/// All counts refer to the comparison against the previous upload snapshot.
/// </summary>
public sealed record IfcDeltaReport(
    int Added,
    int Modified,
    int Deleted,
    int Unchanged,
    int Total,
    TimeSpan Duration,
    IReadOnlyList<string> AddedGuids,
    IReadOnlyList<string> ModifiedGuids,
    IReadOnlyList<string> DeletedGuids);

// ── Implementation ─────────────────────────────────────────────────────────

/// <summary>
/// Gap-5 — Per-element IFC change tracking.
///
/// Algorithm (O(n) over element count):
/// 1. Load the previous snapshot generation for <c>projectModelId</c> into a
///    dictionary keyed by <c>IfcGuid</c>.
/// 2. Walk the new element list; compute the SHA-256 property hash for each
///    element and classify as Added / Modified / Unchanged vs. the previous row.
/// 3. Emit a "Deleted" snapshot for every element present in the previous
///    generation that is absent from the new list.
/// 4. Bulk-insert all new snapshot rows and return the delta report.
/// </summary>
public sealed class IfcDeltaService : IIfcDeltaService
{
    private readonly PlanscapeDbContext _db;
    private readonly ILogger<IfcDeltaService> _logger;

    public IfcDeltaService(PlanscapeDbContext db, ILogger<IfcDeltaService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── IIfcDeltaService ──────────────────────────────────────────────

    public async Task<IfcDeltaReport> ComputeAndPersistAsync(
        Guid tenantId,
        Guid projectId,
        Guid projectModelId,
        int uploadSequence,
        IReadOnlyList<IfcElementProperties> elements,
        CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        _logger.LogInformation(
            "[IfcDelta] Starting delta for projectModelId={ProjectModelId}, uploadSequence={Seq}, elementCount={Count}",
            projectModelId, uploadSequence, elements.Count);

        // ── Step 1: load previous snapshot generation ─────────────────
        // Fetch the snapshot rows with the highest UploadSequence for this
        // project model.  Using _db.Set<T>() avoids a direct DbSet reference
        // so the service compiles before the DbContext migration lands.
        var previousSnapshots = await _db.Set<IfcElementSnapshot>()
            .AsNoTracking()
            .Where(s => s.ProjectModelId == projectModelId
                     && s.TenantId       == tenantId
                     && s.UploadSequence == _db.Set<IfcElementSnapshot>()
                                               .Where(i => i.ProjectModelId == projectModelId
                                                        && i.TenantId       == tenantId)
                                               .Select(i => i.UploadSequence)
                                               .OrderByDescending(i => i)
                                               .FirstOrDefault())
            .ToListAsync(ct);

        // ── Step 2: build lookup of previous state ────────────────────
        var previousByGuid = previousSnapshots
            .ToDictionary(s => s.IfcGuid, StringComparer.Ordinal);

        _logger.LogDebug(
            "[IfcDelta] Loaded {PreviousCount} previous snapshot rows for projectModelId={ProjectModelId}",
            previousByGuid.Count, projectModelId);

        // ── Step 3: classify new elements ─────────────────────────────
        var newSnapshots    = new List<IfcElementSnapshot>(elements.Count);
        var addedGuids      = new List<string>();
        var modifiedGuids   = new List<string>();
        var unchangedCount  = 0;

        foreach (var element in elements)
        {
            var hash      = ComputeHash(element.Properties);
            var changeKind = ClassifyElement(element.GlobalId, hash, previousByGuid);

            switch (changeKind)
            {
                case "Added":
                    addedGuids.Add(element.GlobalId);
                    break;
                case "Modified":
                    modifiedGuids.Add(element.GlobalId);
                    break;
                default:
                    unchangedCount++;
                    break;
            }

            newSnapshots.Add(new IfcElementSnapshot
            {
                TenantId        = tenantId,
                ProjectId       = projectId,
                ProjectModelId  = projectModelId,
                IfcGuid         = element.GlobalId,
                IfcType         = element.IfcType,
                Name            = element.Name,
                // Storey and Discipline are not present on IfcElementProperties;
                // callers that have resolved these can post-process the rows or
                // a richer ingester can extend IfcElementProperties in a later
                // iteration (T4-27b).
                Storey          = null,
                Discipline      = null,
                PropertiesHash  = hash,
                ChangeKind      = changeKind,
                UploadSequence  = uploadSequence,
            });

            // Mark this guid as seen so we can detect deletions below.
            previousByGuid.Remove(element.GlobalId);
        }

        // ── Step 4: emit "Deleted" rows for missing elements ──────────
        // Anything remaining in previousByGuid was not present in the new
        // ingest and is therefore considered deleted in this upload pass.
        var deletedGuids = new List<string>(previousByGuid.Count);

        foreach (var (guid, prev) in previousByGuid)
        {
            deletedGuids.Add(guid);

            newSnapshots.Add(new IfcElementSnapshot
            {
                TenantId        = tenantId,
                ProjectId       = projectId,
                ProjectModelId  = projectModelId,
                IfcGuid         = guid,
                IfcType         = prev.IfcType,
                Name            = prev.Name,
                Storey          = prev.Storey,
                Discipline      = prev.Discipline,
                // Retain the last known hash so the row is still queryable by hash.
                PropertiesHash  = prev.PropertiesHash,
                ChangeKind      = "Deleted",
                UploadSequence  = uploadSequence,
            });
        }

        // ── Step 5: persist all new snapshot rows ─────────────────────
        await _db.Set<IfcElementSnapshot>().AddRangeAsync(newSnapshots, ct);
        await _db.SaveChangesAsync(ct);

        sw.Stop();

        var report = new IfcDeltaReport(
            Added:        addedGuids.Count,
            Modified:     modifiedGuids.Count,
            Deleted:      deletedGuids.Count,
            Unchanged:    unchangedCount,
            Total:        newSnapshots.Count,
            Duration:     sw.Elapsed,
            AddedGuids:   addedGuids.AsReadOnly(),
            ModifiedGuids: modifiedGuids.AsReadOnly(),
            DeletedGuids: deletedGuids.AsReadOnly());

        _logger.LogInformation(
            "[IfcDelta] Completed in {Ms}ms — Added={Added}, Modified={Modified}, Deleted={Deleted}, Unchanged={Unchanged}",
            sw.ElapsedMilliseconds, report.Added, report.Modified, report.Deleted, report.Unchanged);

        return report;
    }

    // ── Private helpers ───────────────────────────────────────────────

    /// <summary>
    /// Classify a single element against the previous snapshot dictionary.
    /// Removes the matching entry from <paramref name="previousByGuid"/> so
    /// the caller can detect deletions by inspecting what remains after the
    /// loop.
    /// </summary>
    private static string ClassifyElement(
        string ifcGuid,
        string newHash,
        Dictionary<string, IfcElementSnapshot> previousByGuid)
    {
        if (!previousByGuid.TryGetValue(ifcGuid, out var previous))
            return "Added";

        return string.Equals(previous.PropertiesHash, newHash, StringComparison.Ordinal)
            ? "Unchanged"
            : "Modified";
    }

    /// <summary>
    /// Compute a deterministic SHA-256 hex digest of the property dictionary.
    /// The dictionary is serialized with keys in their natural insertion order;
    /// callers that require stable hashing across different ordering should
    /// sort keys before calling this method or use a sorted collection type.
    /// </summary>
    private static string ComputeHash(Dictionary<string, string> props)
    {
        // Sort keys so the hash is stable regardless of property insertion order.
        // Without this, the same element with properties in different order would
        // produce a different hash on each upload and be classified as "Modified".
        var sorted = new SortedDictionary<string, string>(props, StringComparer.Ordinal);
        var json  = JsonSerializer.Serialize(sorted, new JsonSerializerOptions { WriteIndented = false });
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
