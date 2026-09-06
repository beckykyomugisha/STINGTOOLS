using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Coordinates;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

public interface ISceneNodeAabbRefresher
{
    /// <summary>
    /// Recompute the world-space AABB of every scene chunk belonging to a model
    /// from its local AABB and the model's current transform. Returns the number
    /// of chunks updated. Idempotent — safe to call after every transform write.
    /// </summary>
    Task<int> RefreshAsync(Guid projectId, Guid projectModelId, Guid tenantId, CancellationToken ct = default);
}

/// <summary>
/// Keeps the federation manifest's chunk bounds honest.
///
/// <para><b>Why it is a service.</b> This logic lived inline in
/// <c>ModelTransformController.Upsert</c> and ran ONLY there. Both automatic
/// transform writers — IFC ingest and auto-align — moved models without it, so
/// the manifest AABBs the viewer culls against described where the chunks used
/// to be. Symptom: geometry that vanishes when the camera looks straight at it,
/// or loads when it is nowhere near the frustum. Extracting it makes "recompute
/// after every transform write" a single call rather than three copies.</para>
///
/// <para><b>Why idempotence had to come first.</b> The inline version read the
/// STORED (already-transformed) box and transformed it again, so a second write
/// compounded the transform and the bounds drifted further from the geometry
/// each time. Calling that from more places would have multiplied the bug. The
/// world box is now derived from <see cref="SceneNode.BaseMinX"/>… — the chunk's
/// own local box — so it is a pure function of (local, transform) and can be
/// recomputed any number of times with the same result.</para>
/// </summary>
public sealed class SceneNodeAabbRefresher : ISceneNodeAabbRefresher
{
    private readonly PlanscapeDbContext _db;
    private readonly ILogger<SceneNodeAabbRefresher> _logger;

    public SceneNodeAabbRefresher(PlanscapeDbContext db, ILogger<SceneNodeAabbRefresher> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<int> RefreshAsync(
        Guid projectId, Guid projectModelId, Guid tenantId, CancellationToken ct = default)
    {
        var nodes = await _db.SceneNodes
            .Where(n => n.SourceModelId == projectModelId && n.DeletedAt == null)
            .ToListAsync(ct);
        if (nodes.Count == 0) return 0;

        var xf = await _db.ProjectModelTransforms.AsNoTracking()
            .FirstOrDefaultAsync(t => t.ProjectModelId == projectModelId
                                   && t.ProjectId == projectId
                                   && t.TenantId == tenantId, ct);

        foreach (var node in nodes)
        {
            // Capture the local box the first time we see a pre-P5 row. For a
            // chunk that was never transformed this is exactly right; for one
            // that was, it is the best available until the model is re-published
            // (ingest writes both boxes).
            node.BaseMinX ??= node.MinX;
            node.BaseMinY ??= node.MinY;
            node.BaseMinZ ??= node.MinZ;
            node.BaseMaxX ??= node.MaxX;
            node.BaseMaxY ??= node.MaxY;
            node.BaseMaxZ ??= node.MaxZ;

            var (mnX, mnY, mnZ, mxX, mxY, mxZ) = TransformBox(
                xf,
                node.BaseMinX!.Value, node.BaseMinY!.Value, node.BaseMinZ!.Value,
                node.BaseMaxX!.Value, node.BaseMaxY!.Value, node.BaseMaxZ!.Value);

            node.MinX = mnX; node.MinY = mnY; node.MinZ = mnZ;
            node.MaxX = mxX; node.MaxY = mxY; node.MaxZ = mxZ;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Refreshed {Count} SceneNode AABBs for model {ModelId} (transform: {HasTransform}).",
            nodes.Count, projectModelId, xf == null ? "none" : "present");

        return nodes.Count;
    }

    /// <summary>
    /// Transform all 8 corners of a local AABB and return the enclosing
    /// axis-aligned box.
    ///
    /// <para>All eight corners, not just min and max: under a Z rotation the
    /// transformed min corner is not generally the min of the transformed box,
    /// so transforming only the two extreme corners produces a box that is both
    /// wrong and often too small — which culls visible geometry.</para>
    ///
    /// <para>A null transform is the identity, so an untransformed model's world
    /// box equals its local box.</para>
    /// </summary>
    internal static (double minX, double minY, double minZ, double maxX, double maxY, double maxZ) TransformBox(
        ProjectModelTransform? t,
        double minX, double minY, double minZ,
        double maxX, double maxY, double maxZ)
    {
        if (t == null) return (minX, minY, minZ, maxX, maxY, maxZ);

        double[] xs = [minX, maxX, minX, maxX, minX, maxX, minX, maxX];
        double[] ys = [minY, minY, maxY, maxY, minY, minY, maxY, maxY];
        double[] zs = [minZ, minZ, minZ, minZ, maxZ, maxZ, maxZ, maxZ];

        var nx = new double[8];
        var ny = new double[8];
        var nz = new double[8];

        for (int i = 0; i < 8; i++)
        {
            // The ONE canonical transform, so the manifest, the viewer and the
            // overlay proof cannot disagree about what a transform means.
            var w = ModelTransformMath.ApplyMm(
                t.TranslationX, t.TranslationY, t.TranslationZ,
                t.RotationDeg, t.ScaleFactor,
                xs[i], ys[i], zs[i]);
            nx[i] = w.X; ny[i] = w.Y; nz[i] = w.Z;
        }

        return (nx.Min(), ny.Min(), nz.Min(), nx.Max(), ny.Max(), nz.Max());
    }
}
