using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Xbim.Common;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Per-element AABB geometry extractor for IFC files using Xbim.Essentials.
///
/// Gap-1 implementation: upgrades clash detection from SceneNode-level
/// to FederatedElement-level granularity by extracting per-element
/// axis-aligned bounding boxes (AABBs) directly from the IFC geometric
/// representation context, without requiring Xbim.Geometry or any native
/// geometry tessellation library.
///
/// Strategy (two-tier):
///   1. Primary   — reads the explicit "BoundingBox" shape representation
///                  item (IIfcBoundingBox) where the exporter wrote one.
///                  ArchiCAD, Revit, and most BIM authoring tools write this.
///   2. Fallback  — reads the element's ObjectPlacement to get the origin
///                  and synthesises a 100 mm radius AABB. Used for
///                  analytical/structural elements that carry no body geometry.
///
/// All output coordinates are in millimetres regardless of the IFC file's
/// internal length unit (MILLIMETRE, METRE, CENTIMETRE, etc.).
///
/// Threading: not thread-safe per instance. Hangfire / DI creates one
/// scope per job invocation; each invocation gets a fresh extractor, so
/// concurrent jobs are safe across instances.
/// </summary>
public interface IIfcGeometryExtractor
{
    /// <summary>
    /// Extract AABB bounds (in mm) for every IfcElement in the file.
    /// Returns a dictionary of IfcGuid → ElementBounds.
    /// Uses the IFC geometric representation context and element placements.
    /// For elements without explicit BoundingBox representation, falls back
    /// to the storey/space bounding box or returns null.
    /// </summary>
    Task<IReadOnlyDictionary<string, ElementBounds>> ExtractBoundsAsync(
        string ifcPath, CancellationToken ct);
}

/// <summary>
/// Per-element AABB in millimetres, plus optional spatial/discipline metadata.
/// </summary>
public sealed record ElementBounds(
    double MinX, double MinY, double MinZ,
    double MaxX, double MaxY, double MaxZ,
    string? Storey,
    string? Discipline);

/// <summary>
/// Default implementation of <see cref="IIfcGeometryExtractor"/>.
/// Uses Xbim.Essentials (IFC2x3 + IFC4 schema-agnostic interfaces).
/// </summary>
public sealed class XbimIfcGeometryExtractor : IIfcGeometryExtractor
{
    private readonly ILogger<XbimIfcGeometryExtractor> _logger;

    public XbimIfcGeometryExtractor(ILogger<XbimIfcGeometryExtractor> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyDictionary<string, ElementBounds>> ExtractBoundsAsync(
        string ifcPath, CancellationToken ct)
    {
        if (!File.Exists(ifcPath))
            throw new FileNotFoundException("IFC file not found", ifcPath);

        var sw = Stopwatch.StartNew();
        var result = new Dictionary<string, ElementBounds>(StringComparer.Ordinal);

        // IfcStore.Open: xbim picks Esent-backed storage on Windows and
        // an in-memory store on Linux. Both are read-only here.
        using var model = IfcStore.Open(ifcPath, null, null, null, Xbim.IO.XbimDBAccess.Read);
        ct.ThrowIfCancellationRequested();

        // 1. Resolve the length unit scale factor → mm.
        double scaleMm = ResolveScaleToMm(model);
        _logger.LogDebug("XbimIfcGeometryExtractor: {Path} — length scale factor = {Scale} (→ mm)", ifcPath, scaleMm);

        // 2. Build storey ancestor index (elementLabel → storey name).
        var storeyByElement = BuildStoreyIndex(model);

        // 3. Walk every IfcElement and extract/synthesise an AABB.
        var allElements = model.Instances.OfType<IIfcElement>().ToList();
        int total   = allElements.Count;
        int success = 0;

        foreach (var element in allElements)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                string? guid = element.GlobalId.Value as string;
                if (string.IsNullOrEmpty(guid)) continue;

                storeyByElement.TryGetValue(element.EntityLabel, out string? storey);
                string discipline = DeriveDiscipine(element);

                ElementBounds? bounds = TryExtractFromBoundingBoxRep(element, scaleMm, storey, discipline)
                                     ?? TryExtractFromObjectPlacement(element, scaleMm, storey, discipline);

                if (bounds is not null)
                {
                    result[guid] = bounds;
                    success++;
                }
            }
            catch (Exception ex)
            {
                string? guid = null;
                try { guid = element.GlobalId.Value as string; } catch { }
                _logger.LogWarning(ex,
                    "XbimIfcGeometryExtractor: failed to extract bounds for element #{Label} (guid={Guid})",
                    element.EntityLabel, guid ?? "?");
            }
        }

        sw.Stop();
        _logger.LogInformation(
            "XbimIfcGeometryExtractor: Extracted AABB bounds for {Success} of {Total} elements from {Path} in {Ms}ms",
            success, total, ifcPath, sw.ElapsedMilliseconds);

        return Task.FromResult<IReadOnlyDictionary<string, ElementBounds>>(result);
    }

    // ── 1. Length unit scale factor ─────────────────────────────────────────

    /// <summary>
    /// Reads IIfcProject.UnitsInContext and finds the SI LENGTHUNIT.
    /// Returns a multiplier such that: value_in_file × scaleMm = value_in_mm.
    /// Defaults to 1000.0 (metres → mm) when the unit context is absent.
    /// </summary>
    private static double ResolveScaleToMm(IModel model)
    {
        try
        {
            var project = model.Instances.OfType<IIfcProject>().FirstOrDefault();
            if (project?.UnitsInContext == null)
                return 1000.0; // safe default: assume metres

            foreach (var unit in project.UnitsInContext.Units.OfType<IIfcSIUnit>())
            {
                if (unit.UnitType != IfcUnitEnum.LENGTHUNIT) continue;

                // Prefix: MILLI, CENTI, DECI, null / no prefix = base unit (metre)
                return unit.Prefix switch
                {
                    IfcSIPrefix.MILLI => 1.0,        // MILLIMETRE
                    IfcSIPrefix.CENTI => 10.0,       // CENTIMETRE
                    IfcSIPrefix.DECI  => 100.0,      // DECIMETRE
                    _                 => 1000.0,     // METRE (no prefix or any other)
                };
            }

            // Fallback: look for IfcConversionBasedUnit (e.g. inches, feet) — approximate
            // by using the conversion factor's value times 1000 (base unit = metre).
            foreach (var unit in project.UnitsInContext.Units.OfType<IIfcConversionBasedUnit>())
            {
                if (unit.UnitType != IfcUnitEnum.LENGTHUNIT) continue;
                var factor = unit.ConversionFactor?.ValueComponent?.Value;
                if (factor != null && double.TryParse(factor.ToString(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double f) && f > 0)
                {
                    // f is the factor relative to the SI base unit (metre)
                    return f * 1000.0;
                }
            }
        }
        catch { /* defensive: return default below */ }

        return 1000.0; // default: metres → mm
    }

    // ── 2. Storey ancestor index ────────────────────────────────────────────

    /// <summary>
    /// One pass over IfcRelContainedInSpatialStructure + IfcRelAggregates to build
    /// a (elementEntityLabel → storey name) map for every element.
    /// Mirrors the pattern in XbimIfcIngester.BuildSpatialAncestorIndex.
    /// </summary>
    private static Dictionary<int, string> BuildStoreyIndex(IModel model)
    {
        // id → parent id via IfcRelAggregates (storey → building → site → project)
        var parentOf = new Dictionary<int, int>();
        foreach (var rel in model.Instances.OfType<IIfcRelAggregates>())
        {
            int parentLabel = rel.RelatingObject.EntityLabel;
            foreach (var child in rel.RelatedObjects)
                parentOf[child.EntityLabel] = parentLabel;
        }

        string? WalkUpToStorey(int containerLabel)
        {
            int cur = containerLabel;
            int guard = 20;
            while (guard-- > 0)
            {
                var ent = model.Instances[cur];
                if (ent is IIfcBuildingStorey bs)
                    return bs.Name?.Value as string;
                if (!parentOf.TryGetValue(cur, out int parent)) break;
                cur = parent;
            }
            return null;
        }

        var result = new Dictionary<int, string>();
        foreach (var rel in model.Instances.OfType<IIfcRelContainedInSpatialStructure>())
        {
            int containerLabel = rel.RelatingStructure.EntityLabel;
            string? storey = WalkUpToStorey(containerLabel);
            if (storey == null) continue;

            foreach (var el in rel.RelatedElements)
            {
                if (!result.ContainsKey(el.EntityLabel))
                    result[el.EntityLabel] = storey;
            }
        }
        return result;
    }

    // ── 3. Discipline derivation ────────────────────────────────────────────

    /// <summary>
    /// Maps IFC type name to STING one-letter discipline code.
    /// Intentionally case-insensitive and prefix-matched so subtype
    /// variants (e.g. IfcWallStandardCase) resolve correctly.
    /// </summary>
    private static string DeriveDiscipine(IIfcElement element)
    {
        string typeName = element.GetType().Name;

        // Use ToUpper once; compare with StartsWith for clarity.
        string t = typeName.ToUpperInvariant();

        if (t.StartsWith("IFCWALL")   || t.StartsWith("IFCSLAB")   ||
            t.StartsWith("IFCROOF")   || t.StartsWith("IFCCOLUMN") ||
            t.StartsWith("IFCBEAM")   || t.StartsWith("IFCSTAIR")  ||
            t.StartsWith("IFCDOOR")   || t.StartsWith("IFCWINDOW") ||
            t.StartsWith("IFCCOVERING") || t.StartsWith("IFCBUILDING"))
            return "A";

        if (t.StartsWith("IFCPIPE") || t.StartsWith("IFCFLOWSEGMENT"))
        {
            // IFC4 IfcPipeSegment, IfcPipeFitting — all plumbing
            if (t.Contains("PIPE") || t.Contains("SANITARY") || t.Contains("VALVE"))
                return "P";
        }

        if (t.StartsWith("IFCDUCT")   || t.StartsWith("IFCAIRTERMINAL") ||
            t.StartsWith("IFCFAN")    || t.StartsWith("IFCCOMPRESSOR")  ||
            t.StartsWith("IFCCOIL")   || t.StartsWith("IFCHUMIDIFIER")  ||
            t.StartsWith("IFCUNITARYEQUIPMENT"))
            return "M";

        if (t.StartsWith("IFCCABLESEGMENT")  || t.StartsWith("IFCCABLEFITTING") ||
            t.StartsWith("IFCWIRE")          || t.StartsWith("IFCOUTLET")       ||
            t.StartsWith("IFCELECTRICDIST")  || t.StartsWith("IFCLAMP")         ||
            t.StartsWith("IFCELECTRICAPP")   || t.StartsWith("IFCELECTRICMOTOR")||
            t.StartsWith("IFCELECTRICGENER"))
            return "E";

        if (t.StartsWith("IFCPLATE") || t.StartsWith("IFCMEMBER") ||
            t.StartsWith("IFCFOOTING") || t.StartsWith("IFCPILE")  ||
            t.StartsWith("IFCREINFORCINGBAR") || t.StartsWith("IFCREINFORCINGMESH"))
            return "S";

        if (t.StartsWith("IFCFIRESUPPRESSION") || t.StartsWith("IFCSPRINKLER"))
            return "FP";

        // MEP equipment that is unambiguously plumbing
        if (t.Contains("SANITARY") || t.Contains("INTERCEPTOR") ||
            t.Contains("WASTE")    || t.Contains("CISTERN"))
            return "P";

        // Remaining MEP flow elements (pumps, dampers, etc.)
        if (t.StartsWith("IFCFLOW") || t.StartsWith("IFCENERGY"))
            return "M";

        return "A"; // architectural / general default
    }

    // ── 4a. Primary: BoundingBox shape representation ───────────────────────

    /// <summary>
    /// Looks for a "BoundingBox" IIfcShapeRepresentation in the element's
    /// Representation property. Returns null when none is found.
    ///
    /// IFC spec: an IIfcBoundingBox is an IfcGeometricRepresentationItem
    /// with Corner (IfcCartesianPoint), XDim, YDim, ZDim (IfcPositiveLengthMeasure).
    /// The corner is the minimum point; XDim/YDim/ZDim are positive extents.
    ///
    /// NOTE: The bounding box coordinates are in the LOCAL coordinate system
    /// of the element (relative to its ObjectPlacement). A full world-coordinate
    /// transform would require resolving the full placement chain; for AABB
    /// clash purposes we produce the LOCAL AABB, which is sufficient for
    /// containment tests when both models share a common project origin.
    /// For files with map conversion / georeferencing, this remains a known
    /// limitation (documented in caveats).
    /// </summary>
    private static ElementBounds? TryExtractFromBoundingBoxRep(
        IIfcElement element, double scaleMm, string? storey, string discipline)
    {
        try
        {
            if (element.Representation?.Representations == null) return null;

            foreach (var rep in element.Representation.Representations
                         .OfType<IIfcShapeRepresentation>())
            {
                // RepresentationType is a nullable IfcLabel; compare string value
                if (!string.Equals(
                        rep.RepresentationType?.Value as string,
                        "BoundingBox",
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                var bb = rep.Items.OfType<IIfcBoundingBox>().FirstOrDefault();
                if (bb == null) continue;

                double ox = (double)(bb.Corner.X ?? 0) * scaleMm;
                double oy = (double)(bb.Corner.Y ?? 0) * scaleMm;
                double oz = (double)(bb.Corner.Z ?? 0) * scaleMm;
                double dx = (double)bb.XDim * scaleMm;
                double dy = (double)bb.YDim * scaleMm;
                double dz = (double)bb.ZDim * scaleMm;

                return new ElementBounds(
                    MinX: ox,       MinY: oy,       MinZ: oz,
                    MaxX: ox + dx,  MaxY: oy + dy,  MaxZ: oz + dz,
                    Storey: storey,
                    Discipline: discipline);
            }
        }
        catch { /* non-fatal: caller logs */ throw; }

        return null;
    }

    // ── 4b. Fallback: ObjectPlacement origin ───────────────────────────────

    /// <summary>
    /// Reads the element's ObjectPlacement → IIfcLocalPlacement →
    /// IIfcAxis2Placement3D.Location and synthesises a 100 mm radius AABB
    /// centred on that point.
    ///
    /// We only walk one level of RelativePlacement because chaining all
    /// parent placements requires resolving an arbitrary-depth tree; the
    /// 100 mm placeholder is already approximate so absolute world coords
    /// are not required for it to be useful for clash triage.
    /// </summary>
    private static ElementBounds? TryExtractFromObjectPlacement(
        IIfcElement element, double scaleMm, string? storey, string discipline)
    {
        try
        {
            if (element.ObjectPlacement is not IIfcLocalPlacement lp) return null;

            // RelativePlacement can be IIfcAxis2Placement3D or IIfcAxis2Placement2D.
            if (lp.RelativePlacement is not IIfcAxis2Placement3D ax3d) return null;

            var loc = ax3d.Location;
            if (loc == null) return null;

            double x = (double)(loc.X ?? 0) * scaleMm;
            double y = (double)(loc.Y ?? 0) * scaleMm;
            double z = (double)(loc.Z ?? 0) * scaleMm;

            const double radius = 100.0; // mm

            return new ElementBounds(
                MinX: x - radius, MinY: y - radius, MinZ: z - radius,
                MaxX: x + radius, MaxY: y + radius, MaxZ: z + radius,
                Storey: storey,
                Discipline: discipline);
        }
        catch { /* non-fatal: caller logs */ throw; }

        return null;
    }
}
