// StingTools — Shared perimeter-envelope detector (WS A2).
//
// Single source of envelope-from-geometry truth: intersects a Space/Room
// boundary with exterior walls + their hosted windows, derives net wall + glazing
// area + orientation, and adds a roof segment on the top level. Falls back to a
// generic envelope ratio when the geometry doesn't yield (linked architectural
// model, no boundary). Extracted from HvacBlockLoadCommand so the annual-energy
// (sustainability) path reuses the EXACT same conduction + per-façade solar input
// the HVAC block-load uses — one detector, no fork.
//
// Revit-facing (touches geometry) — NOT in the test project. The per-façade solar
// MATH that consumes these segments (AnnualEnergyEstimator.VerticalSolarFactor /
// EstimateZone) is pure and unit-tested separately.

using System;
using System.Collections.Concurrent;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Hvac.Loads
{
    public static class EnvelopeDetector
    {
        // Per-document cache of the top level's id — re-resolving the highest Level
        // on every space gets expensive on large projects.
        private static readonly ConcurrentDictionary<string, ElementId> _topLevelCache
            = new ConcurrentDictionary<string, ElementId>();

        /// <summary>Drop the cached top-level lookup for a document (document-close hook).</summary>
        public static void InvalidateTopLevelCache(Document doc)
        {
            try { _topLevelCache.TryRemove(doc?.PathName ?? "<no-doc>", out _); } catch { }
        }

        /// <summary>
        /// Best-effort envelope detection by intersecting the room boundary with
        /// exterior walls + their hosted windows. When the geometry doesn't yield
        /// (linked architectural model, etc.) fall back to a generic envelope ratio
        /// so the calc still runs. Appends segments to <paramref name="z"/>.Envelope.
        /// </summary>
        public static void AddPerimeterEnvelope(SpatialElement spatial, LoadZone z, ConstructionProfile construction)
        {
            try
            {
                var opts = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Center
                };
                var segs = spatial.GetBoundarySegments(opts);
                if (segs == null || segs.Count == 0) goto Fallback;

                double extWallAreaM2 = 0;
                double glazingAreaM2 = 0;
                double avgOrient = 0; int orientN = 0;
                foreach (var loop in segs)
                foreach (var seg in loop)
                {
                    var el = spatial.Document.GetElement(seg.ElementId);
                    if (el is not Wall w) continue;
                    if (w.WallType?.Function != WallFunction.Exterior) continue;
                    double lenM = UnitUtils.ConvertFromInternalUnits(seg.GetCurve()?.Length ?? 0, UnitTypeId.Meters);
                    double h = z.HeightM;
                    double area = lenM * h;
                    extWallAreaM2 += area;

                    // Glazing — sum hosted window areas if any
                    try
                    {
                        var hosted = w.FindInserts(true, false, false, false);
                        foreach (var ins in hosted)
                        {
                            if (w.Document.GetElement(ins) is FamilyInstance fi &&
                                fi.Category?.Id?.Value == (long)BuiltInCategory.OST_Windows)
                            {
                                var bb = fi.get_BoundingBox(null);
                                if (bb != null)
                                {
                                    double wFt = bb.Max.X - bb.Min.X;
                                    double hFt = bb.Max.Z - bb.Min.Z;
                                    double aM2 = UnitUtils.ConvertFromInternalUnits(wFt * hFt, UnitTypeId.SquareMeters);
                                    if (aM2 > 0.1) glazingAreaM2 += aM2;
                                }
                            }
                        }
                    }
                    catch { /* swallow per-window failures */ }

                    // Crude orientation: wall facing vector
                    try
                    {
                        var dir = w.Orientation;
                        double deg = Math.Atan2(dir.X, dir.Y) * 180 / Math.PI;
                        if (deg < 0) deg += 360;
                        avgOrient += deg; orientN++;
                    }
                    catch { }
                }
                double orientation = orientN > 0 ? avgOrient / orientN : 180;
                double netWall = Math.Max(0, extWallAreaM2 - glazingAreaM2);

                if (netWall > 0)
                    z.Envelope.Add(new EnvelopeSegment
                    {
                        Kind = SegmentKind.ExteriorWall, AreaM2 = netWall,
                        UvalueWm2K = construction.WallUvalue, OrientationDeg = orientation
                    });
                if (glazingAreaM2 > 0)
                    z.Envelope.Add(new EnvelopeSegment
                    {
                        Kind = SegmentKind.Window, AreaM2 = glazingAreaM2,
                        UvalueWm2K = construction.WindowUvalue,
                        SHGC = construction.WindowSHGC,
                        ShadingFactor = construction.WindowShadingFactor,
                        OrientationDeg = orientation
                    });

                // Roof segment only when the zone is on the top level.
                if (IsTopLevel(spatial))
                {
                    z.Envelope.Add(new EnvelopeSegment
                    {
                        Kind = SegmentKind.Roof, AreaM2 = z.FloorAreaM2,
                        UvalueWm2K = construction.RoofUvalue, OrientationDeg = 0
                    });
                }
                return;

                Fallback:
                z.Envelope.Add(new EnvelopeSegment
                {
                    Kind = SegmentKind.ExteriorWall, AreaM2 = Math.Max(z.FloorAreaM2 * 0.6, 8),
                    UvalueWm2K = construction.WallUvalue, OrientationDeg = 180
                });
                z.Envelope.Add(new EnvelopeSegment
                {
                    Kind = SegmentKind.Window, AreaM2 = Math.Max(z.FloorAreaM2 * 0.15, 2),
                    UvalueWm2K = construction.WindowUvalue,
                    SHGC = construction.WindowSHGC,
                    ShadingFactor = construction.WindowShadingFactor,
                    OrientationDeg = 180
                });
            }
            catch (Exception ex) { StingLog.Warn($"Envelope detect {spatial?.Id}: {ex.Message}"); }
        }

        public static bool IsTopLevel(SpatialElement spatial)
        {
            try
            {
                var doc = spatial.Document;
                // SpatialElement.LevelId is on the Element base; safer than `.Level`
                // which lives on Room/Space individually.
                var lvlId = spatial.LevelId;
                if (lvlId == ElementId.InvalidElementId) return false;
                string key = doc.PathName ?? "<no-doc>";
                var topId = _topLevelCache.GetOrAdd(key, _ =>
                {
                    var top = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .OrderByDescending(l => l.Elevation)
                        .FirstOrDefault();
                    return top?.Id ?? ElementId.InvalidElementId;
                });
                return topId != ElementId.InvalidElementId && topId == lvlId;
            }
            catch { return false; }
        }
    }
}
