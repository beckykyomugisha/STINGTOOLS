using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Xbim.Common;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Gap 4 — IFC tessellation job. Uses xBIM Essentials (property + spatial
/// traversal only; no Xbim.Geometry) to build one SceneNode per
/// (storey × discipline) AABB group and upload a JSON sidecar to MinIO.
///
/// Since Xbim.Geometry (full mesh tessellation → GLB) is not yet available,
/// each sidecar stores the IfcGuid list + computed AABB so the mobile viewer
/// can render bounding-box LOD while the real mesh pipeline is added later.
///
/// Threading: not thread-safe per instance. Hangfire creates one scope per
/// job invocation so concurrent jobs get separate instances.
/// </summary>
public interface IIfcTessellationJob
{
    /// <summary>
    /// Gap 4 — Process an uploaded IFC file:
    /// 1. Open with xBIM, scan all IIfcBuildingStorey entities (storey names + elevations)
    /// 2. For each element, determine its storey (IfcRelContainedInSpatialStructure) and discipline
    /// 3. Build one SceneNode per (storey × discipline) combination with AABB bounds
    /// 4. Write a JSON sidecar with element IfcGuids per group and store it in MinIO at
    ///    "t_{tenantId}/{projectId}/scenes/{sourceModelId}/{disc}_{storey}.aabb.json"
    /// 5. Persist SceneNode rows (upsert by SourceModelId + Discipline + LevelCode)
    /// </summary>
    Task<TessellationResult> RunAsync(
        string ifcPath,
        Guid sourceModelId,
        Guid projectId,
        Guid tenantId,
        CancellationToken ct);
}

public sealed record TessellationResult(
    int StoreyCount,
    int DisciplineCount,
    int SceneNodesCreated,
    int SceneNodesUpdated,
    TimeSpan Duration,
    string? Warning);

public sealed class IfcTessellationJob : IIfcTessellationJob
{
    private readonly PlanscapeDbContext _db;
    private readonly IFileStorageService _storage;
    private readonly ILogger<IfcTessellationJob> _logger;

    // AABB accumulator: min/max in millimetres (IFC default unit may vary;
    // we store raw values and let the viewer normalise).
    private sealed class AabbAccumulator
    {
        public double MinX = double.MaxValue, MinY = double.MaxValue, MinZ = double.MaxValue;
        public double MaxX = double.MinValue, MaxY = double.MinValue, MaxZ = double.MinValue;
        public readonly List<string> Guids = new();

        public void Expand(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            if (x0 < MinX) MinX = x0;
            if (y0 < MinY) MinY = y0;
            if (z0 < MinZ) MinZ = z0;
            if (x1 > MaxX) MaxX = x1;
            if (y1 > MaxY) MaxY = y1;
            if (z1 > MaxZ) MaxZ = z1;
        }

        /// <summary>True if at least one element was added.</summary>
        public bool IsValid => MaxX >= MinX;
    }

    public IfcTessellationJob(
        PlanscapeDbContext db,
        IFileStorageService storage,
        ILogger<IfcTessellationJob> logger)
    {
        _db      = db;
        _storage = storage;
        _logger  = logger;
    }

    public async Task<TessellationResult> RunAsync(
        string ifcPath,
        Guid sourceModelId,
        Guid projectId,
        Guid tenantId,
        CancellationToken ct)
    {
        if (!File.Exists(ifcPath))
            throw new FileNotFoundException("IFC file not found for tessellation", ifcPath);

        var sw = Stopwatch.StartNew();

        _logger.LogInformation(
            "IfcTessellationJob starting: sourceModelId={ModelId} project={ProjectId} file={Path}",
            sourceModelId, projectId, ifcPath);

        using var model = IfcStore.Open(ifcPath, null, null, null, Xbim.IO.XbimDBAccess.Read);
        ct.ThrowIfCancellationRequested();

        // ── Step 1: Build storey index via IfcRelContainedInSpatialStructure ──
        // element entity label → storey name (or "Unknown" if not in any storey)
        var elementStorey = BuildStoreyIndex(model);
        ct.ThrowIfCancellationRequested();

        // ── Step 2: Accumulate AABBs per (storey, discipline) ─────────────────
        // Key: "{discipline}|{storeyName}"
        var groups = new Dictionary<string, AabbAccumulator>(StringComparer.Ordinal);

        foreach (var element in model.Instances.OfType<IIfcElement>())
        {
            ct.ThrowIfCancellationRequested();

            string disc    = MapDiscipline(element.GetType().Name);
            string storey  = elementStorey.TryGetValue(element.EntityLabel, out var s) ? s : "Unknown";
            string key     = $"{disc}|{storey}";

            if (!groups.TryGetValue(key, out var acc))
                groups[key] = acc = new AabbAccumulator();

            string guid = element.GlobalId?.ToString() ?? "";
            if (!string.IsNullOrEmpty(guid))
                acc.Guids.Add(guid);

            // ── Step 4: AABB from IfcBoundingBox or placement-derived 200 mm cube ──
            bool gotBbox = false;

            // Try to find a bounding-box representation.
            var rep = element.Representation;
            if (rep != null)
            {
                foreach (var ctx in rep.Representations)
                {
                    foreach (var item in ctx.Items)
                    {
                        if (item is IIfcBoundingBox bb)
                        {
                            // IFC stores BoundingBox as: corner + XDim / YDim / ZDim
                            double ox = (double)bb.Corner.X;
                            double oy = (double)bb.Corner.Y;
                            double oz = (double)bb.Corner.Z;
                            double dx = (double)bb.XDim;
                            double dy = (double)bb.YDim;
                            double dz = (double)bb.ZDim;
                            acc.Expand(ox, oy, oz, ox + dx, oy + dy, oz + dz);
                            gotBbox = true;
                            break;
                        }
                    }
                    if (gotBbox) break;
                }
            }

            if (!gotBbox)
            {
                // Fall back: use placement origin ± 100 mm cube.
                var placement = element.ObjectPlacement;
                double px = 0, py = 0, pz = 0;
                if (placement is IIfcLocalPlacement lp &&
                    lp.RelativePlacement is IIfcAxis2Placement3D ap3)
                {
                    px = (double)ap3.Location.X;
                    py = (double)ap3.Location.Y;
                    pz = (double)ap3.Location.Z;
                }
                acc.Expand(px - 100, py - 100, pz - 100, px + 100, py + 100, pz + 100);
            }
        }

        ct.ThrowIfCancellationRequested();

        // ── Steps 6–11: Sidecar JSON → MinIO → DB upsert ─────────────────────
        int created = 0, updated = 0;
        var storeys = new HashSet<string>(StringComparer.Ordinal);
        var discs   = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new List<string>();

        // Load existing SceneNodes for this source model once (avoids N+1).
        var existing = await _db.SceneNodes
            .Where(sn => sn.SourceModelId == sourceModelId && sn.DeletedAt == null)
            .ToListAsync(ct);

        var existingIndex = existing.ToDictionary(
            sn => $"{sn.Discipline}|{sn.LevelCode ?? "Unknown"}",
            StringComparer.Ordinal);

        foreach (var (key, acc) in groups)
        {
            if (!acc.IsValid || acc.Guids.Count == 0)
                continue;

            var parts = key.Split('|', 2);
            string disc   = parts[0];
            string storey = parts.Length > 1 ? parts[1] : "Unknown";

            storeys.Add(storey);
            discs.Add(disc);

            // ── Step 6: Build JSON sidecar ─────────────────────────────────────
            var sidecar = new
            {
                guids      = acc.Guids,
                storey,
                discipline = disc,
                aabb       = new
                {
                    minX = acc.MinX, minY = acc.MinY, minZ = acc.MinZ,
                    maxX = acc.MaxX, maxY = acc.MaxY, maxZ = acc.MaxZ,
                },
            };

            byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(sidecar,
                new JsonSerializerOptions { WriteIndented = false });

            // ── Step 7: SHA-256 of the JSON content ────────────────────────────
            string contentHash = Convert.ToHexString(SHA256.HashData(jsonBytes))
                                         .ToLowerInvariant();

            // ── Step 8: Upload to MinIO via SaveScopedAsync ────────────────────
            // Path: t_{tenantId}/{projectId}/scenes/{sourceModelId}/{disc}_{storey}.aabb.json
            // SaveScopedAsync prepends t_{tenantId}/{projectId}/ automatically.
            string safeStorey = SanitiseName(storey);
            string fileName = $"scenes/{sourceModelId}/{disc}_{safeStorey}.aabb.json";

            string storagePath;
            try
            {
                using var ms = new MemoryStream(jsonBytes);
                storagePath = await _storage.SaveScopedAsync(tenantId, projectId, fileName, ms, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "IfcTessellationJob: failed to upload sidecar for {Key} — skipping SceneNode", key);
                warnings.Add($"Upload failed for {key}: {ex.Message}");
                continue;
            }

            // ── Steps 9–10: Upsert SceneNode ──────────────────────────────────
            if (existingIndex.TryGetValue(key, out var node))
            {
                node.ContentHash   = contentHash;
                node.StoragePath   = storagePath;
                node.FileSizeBytes = jsonBytes.Length;
                node.VertexCount   = 0;
                node.Compression   = "none";
                node.MinX = acc.MinX; node.MinY = acc.MinY; node.MinZ = acc.MinZ;
                node.MaxX = acc.MaxX; node.MaxY = acc.MaxY; node.MaxZ = acc.MaxZ;
                node.DeletedAt = null;
                _db.SceneNodes.Update(node);
                updated++;
            }
            else
            {
                var newNode = new SceneNode
                {
                    TenantId      = tenantId,
                    ProjectId     = projectId,
                    SourceModelId = sourceModelId,
                    Discipline    = disc,
                    LevelCode     = storey,
                    ContentHash   = contentHash,
                    StoragePath   = storagePath,
                    FileSizeBytes = jsonBytes.Length,
                    VertexCount   = 0,
                    Compression   = "none",
                    MinX = acc.MinX, MinY = acc.MinY, MinZ = acc.MinZ,
                    MaxX = acc.MaxX, MaxY = acc.MaxY, MaxZ = acc.MaxZ,
                };
                _db.SceneNodes.Add(newNode);
                created++;
            }
        }

        // ── Step 11: Save all changes in one round-trip ───────────────────────
        await _db.SaveChangesAsync(ct);

        sw.Stop();
        _logger.LogInformation(
            "IfcTessellationJob complete: sourceModelId={ModelId} storeys={Storeys} discs={Discs} " +
            "created={Created} updated={Updated} elapsed={Ms}ms",
            sourceModelId, storeys.Count, discs.Count, created, updated, sw.ElapsedMilliseconds);

        return new TessellationResult(
            StoreyCount:        storeys.Count,
            DisciplineCount:    discs.Count,
            SceneNodesCreated:  created,
            SceneNodesUpdated:  updated,
            Duration:           sw.Elapsed,
            Warning:            warnings.Count > 0 ? string.Join("; ", warnings) : null);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a (elementEntityLabel → storeyName) lookup via
    /// IfcRelContainedInSpatialStructure + IfcRelAggregates walk-up.
    /// </summary>
    private static Dictionary<int, string> BuildStoreyIndex(IModel model)
    {
        // Aggregation parent chain: child → parent entity label
        var parentOf = new Dictionary<int, int>();
        foreach (var rel in model.Instances.OfType<IIfcRelAggregates>())
        {
            int parentLabel = rel.RelatingObject.EntityLabel;
            foreach (var child in rel.RelatedObjects)
                parentOf[child.EntityLabel] = parentLabel;
        }

        // Walk up the aggregation chain to find the first IIfcBuildingStorey
        string? FindStoreyName(int containerLabel)
        {
            int cur = containerLabel;
            for (int guard = 20; guard > 0; guard--)
            {
                var ent = model.Instances[cur];
                if (ent == null) break;
                if (ent is IIfcBuildingStorey bs)
                    return bs.Name?.Value as string ?? "Storey";
                if (!parentOf.TryGetValue(cur, out int parent)) break;
                cur = parent;
            }
            return null;
        }

        var result = new Dictionary<int, string>();
        foreach (var rel in model.Instances.OfType<IIfcRelContainedInSpatialStructure>())
        {
            string? storeyName = FindStoreyName(rel.RelatingStructure.EntityLabel);
            if (storeyName == null) continue;

            foreach (var el in rel.RelatedElements)
                result[el.EntityLabel] = storeyName;
        }
        return result;
    }

    /// <summary>
    /// Maps an IFC type name (e.g. "IfcWallStandardCase") to an ISO 19650
    /// discipline code ("A", "P", "M", "E", "S", "FP").
    /// </summary>
    private static string MapDiscipline(string ifcTypeName)
    {
        // Normalise: strip trailing subtype suffixes like "StandardCase"
        // and compare against the canonical root name.
        var t = ifcTypeName;

        // Architectural
        if (t is "IfcWall" or "IfcWallStandardCase" or "IfcSlab" or "IfcRoof"
                 or "IfcColumn" or "IfcBeam" or "IfcStair" or "IfcDoor"
                 or "IfcWindow" or "IfcCovering" or "IfcCurtainWall" or "IfcRamp")
            return "A";

        // Public Health / Plumbing
        if (t is "IfcPipeFitting" or "IfcPipeSegment" or "IfcFlowSegment"
                 or "IfcFlowFitting" or "IfcSanitaryTerminal" or "IfcPlumbingFixture")
            return "P";

        // Mechanical / HVAC
        if (t is "IfcDuctFitting" or "IfcDuctSegment" or "IfcAirTerminal"
                 or "IfcUnitaryEquipment" or "IfcBoiler" or "IfcChiller"
                 or "IfcCoil" or "IfcFan" or "IfcFilter" or "IfcFlowController")
            return "M";

        // Electrical
        if (t is "IfcCableSegment" or "IfcCableFitting" or "IfcElectricAppliance"
                 or "IfcLamp" or "IfcLightFixture" or "IfcOutlet"
                 or "IfcElectricDistributionBoard" or "IfcElectricFlowStorageDevice"
                 or "IfcProtectiveDevice" or "IfcSwitchingDevice")
            return "E";

        // Structural (plates, members, footings, piles, rebar)
        if (t is "IfcPlate" or "IfcMember" or "IfcFooting" or "IfcPile"
                 or "IfcReinforcingBar" or "IfcReinforcingMesh" or "IfcTendon")
            return "S";

        // Fire Protection
        if (t == "IfcFireSuppressionTerminal")
            return "FP";
        // IfcFlowTerminal: check via predefined type naming convention — not
        // resolvable without the instance here, so fall through to default "A".

        return "A";
    }

    /// <summary>
    /// Strips characters that are illegal in object-store keys / filenames.
    /// Replaces spaces, slashes, and control chars with underscores.
    /// </summary>
    private static string SanitiseName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '.')
                sb.Append(c);
            else
                sb.Append('_');
        }
        return sb.Length > 0 ? sb.ToString() : "Unknown";
    }
}
