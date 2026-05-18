using StingTools.Core;
// Phase 139 D1 — Wall/ceiling/floor follower router.
//
// Routes MEP containment (Conduit, CableTray, Pipe, Duct) along
// host faces at an exact offset from the face plane.  Called when a
// PlacementRule has RoutingMode != "NONE".  Created segments are
// stamped with STING_ROUTE_RULE_ID_TXT for downstream auditing.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;

namespace StingTools.Core.Placement
{
    public class WallFollowerRouter
    {
        private const double MmToFt = 1.0 / 304.8;

        public class RouteResult
        {
            public List<ElementId> CreatedSegments { get; set; } = new List<ElementId>();
            public List<string>    Warnings        { get; set; } = new List<string>();
        }

        private readonly Document _doc;
        public WallFollowerRouter(Document doc) { _doc = doc; }

        /// <summary>
        /// Route through all endpoint XYZs.  Caller guarantees endpoints
        /// are in the same room and that <paramref name="rule.RoutingMode"/>
        /// is non-NONE.  Empty endpoints produce an empty result.
        /// </summary>
        public RouteResult Route(IList<XYZ> endpoints, PlacementRule rule, Room room)
        {
            var result = new RouteResult();
            if (endpoints == null || endpoints.Count < 2 || rule == null)
                return result;

            string mode = (rule.RoutingMode ?? "NONE").ToUpperInvariant();
            if (mode == "NONE") return result;

            try
            {
                // 1. Sort endpoints in spatial order so segments don't crisscross.
                List<XYZ> ordered = OrderEndpoints(endpoints, room, mode);

                // 2. Apply face offset per endpoint.  WALL_FOLLOW finds nearest
                //    boundary segment per endpoint and offsets along its inward
                //    normal.  CEILING_FOLLOW / FLOOR_FOLLOW just preserve the
                //    XY and apply offsetZ.
                List<XYZ> offsetPoints = ApplyFaceOffset(ordered, rule, room, mode);

                // 3. Find the right MEPSystem-aware factory for the chosen
                //    RouteSegmentCategory and create straight segments
                //    between consecutive offsetPoints.
                ElementId levelId = room?.LevelId ?? ElementId.InvalidElementId;
                // Phase 139.29 — fallback when the host room has no level (rare:
                // detached / view-specific elements). Without a valid level every
                // Conduit/Pipe/Duct.Create call below throws ArgumentException and
                // the user just sees "segment N create failed" with no clue why.
                if (levelId == ElementId.InvalidElementId)
                {
                    levelId = FallbackLevelId(_doc, endpoints);
                    if (levelId == ElementId.InvalidElementId)
                    {
                        result.Warnings.Add(
                            $"WallFollowerRouter: no resolvable level for rule '{rule.RuleId}' " +
                            "(room has no level and no project levels found).");
                        return result;
                    }
                }
                ElementId typeId  = ResolveDefaultType(rule.RouteSegmentCategory);
                ElementId systemTypeId = ResolveSystemType(rule.RouteSegmentCategory);
                if (typeId == ElementId.InvalidElementId)
                {
                    result.Warnings.Add(
                        $"WallFollowerRouter: no default type for category '{rule.RouteSegmentCategory}'");
                    return result;
                }

                for (int i = 0; i < offsetPoints.Count - 1; i++)
                {
                    XYZ a = offsetPoints[i];
                    XYZ b = offsetPoints[i + 1];
                    if (a == null || b == null || a.IsAlmostEqualTo(b)) continue;

                    ElementId newId = ElementId.InvalidElementId;
                    try
                    {
                        // API-NOTE (Revit 2025+):
                        //   Conduit.Create(Document, ElementId conduitTypeId, XYZ start, XYZ end, ElementId levelId)
                        //   CableTray.Create(Document, ElementId trayTypeId, XYZ start, XYZ end, ElementId levelId)
                        //   Pipe.Create(Document, ElementId systemTypeId, ElementId pipeTypeId, ElementId levelId, XYZ start, XYZ end)
                        //   Duct.Create(Document, ElementId systemTypeId, ElementId ductTypeId, ElementId levelId, XYZ start, XYZ end)
                        // Signatures verified against Revit 2025 API docs. Pipe/Duct require a valid
                        // systemTypeId — early-warn here rather than failing inside the API.
                        switch ((rule.RouteSegmentCategory ?? "").ToUpperInvariant())
                        {
                            case "CONDUIT":
                                newId = Conduit.Create(_doc, typeId, a, b, levelId)?.Id ?? ElementId.InvalidElementId;
                                break;
                            case "CABLETRAY":
                                newId = CableTray.Create(_doc, typeId, a, b, levelId)?.Id ?? ElementId.InvalidElementId;
                                break;
                            case "PIPE":
                                if (systemTypeId == ElementId.InvalidElementId)
                                {
                                    result.Warnings.Add(
                                        $"WallFollowerRouter: PIPE segment {i} skipped — no PipingSystemType loaded for rule '{rule.RuleId}'");
                                    continue;
                                }
                                newId = Pipe.Create(_doc, systemTypeId, typeId, levelId, a, b)?.Id ?? ElementId.InvalidElementId;
                                break;
                            case "DUCT":
                                if (systemTypeId == ElementId.InvalidElementId)
                                {
                                    result.Warnings.Add(
                                        $"WallFollowerRouter: DUCT segment {i} skipped — no MechanicalSystemType loaded for rule '{rule.RuleId}'");
                                    continue;
                                }
                                newId = Duct.Create(_doc, systemTypeId, typeId, levelId, a, b)?.Id ?? ElementId.InvalidElementId;
                                break;
                            default:
                                result.Warnings.Add(
                                    $"WallFollowerRouter: unsupported RouteSegmentCategory '{rule.RouteSegmentCategory}'");
                                continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Include API call name in the warning so a signature mismatch in a future
                        // Revit version is immediately diagnosable from the log.
                        result.Warnings.Add(
                            $"WallFollowerRouter: {rule.RouteSegmentCategory}.Create(seg={i}) failed: " +
                            $"{ex.GetType().Name} — {ex.Message}");
                    }

                    if (newId != ElementId.InvalidElementId)
                    {
                        result.CreatedSegments.Add(newId);
                        TryStampRouteRuleId(newId, rule.RuleId);
                    }
                }

                // Phase 139.28 — auto-emit physical clip / hanger family
                // instances at the BS 5572 / BS EN 12056-2 / MSS SP-58
                // spacing. Only fires for SURFACE / SUSPENDED contexts;
                // CHASED routes are cast-in and don't need surface clips.
                string ctx = (rule.MountingContext ?? "").ToUpperInvariant();
                bool wantsSupports = (ctx == "SURFACE" || ctx == "SUSPENDED")
                    && rule.EmitSupports
                    && result.CreatedSegments.Count > 0;
                if (wantsSupports)
                {
                    try
                    {
                        var supportRes = StingTools.Core.Calc.RoutingSupportPlacer.PlaceForRoute(
                            _doc, rule, result.CreatedSegments);
                        if (supportRes.SupportsPlaced > 0)
                            result.Warnings.Add(
                                $"WallFollowerRouter: emitted {supportRes.SupportsPlaced} support(s) " +
                                $"({ctx}) per BS 5572 / MSS SP-58 spacing.");
                        if (supportRes.FamilyMissCount > 0)
                            result.Warnings.Add(
                                $"WallFollowerRouter: {supportRes.FamilyMissCount} support(s) planned but no " +
                                $"hanger / clip family loaded — load STING_HANGER_GENERIC.rfa or run Place_Hangers manually.");
                        foreach (var w in supportRes.Warnings)
                            if (!result.Warnings.Contains(w)) result.Warnings.Add(w);
                    }
                    catch (Exception sex) { result.Warnings.Add($"WallFollowerRouter support emit: {sex.Message}"); }
                }

                // Phase 139.28 — slope validation for gravity-drainage runs.
                if (rule.MinSlopePercent > 0 && result.CreatedSegments.Count > 0)
                {
                    try
                    {
                        var slope = StingTools.Core.Calc.SlopeValidator.CheckSegments(
                            _doc, result.CreatedSegments, rule.MinSlopePercent, rule.RuleId);
                        foreach (var w in slope.Warnings)
                            if (!result.Warnings.Contains(w)) result.Warnings.Add(w);
                    }
                    catch (Exception slx) { result.Warnings.Add($"WallFollowerRouter slope check: {slx.Message}"); }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"WallFollowerRouter.Route: {ex.Message}");
                result.Warnings.Add($"WallFollowerRouter exception: {ex.Message}");
            }
            return result;
        }

        // ── Helpers ────────────────────────────────────────────────

        private List<XYZ> OrderEndpoints(IList<XYZ> endpoints, Room room, string mode)
        {
            var list = new List<XYZ>(endpoints.Where(p => p != null));
            if (list.Count <= 1) return list;
            try
            {
                var bb = room?.get_BoundingBox(null);
                XYZ centroid = (bb != null) ? (bb.Min + bb.Max) * 0.5 : list[0];
                if (mode == "WALL_FOLLOW")
                {
                    // Sort by atan2 angle around centroid
                    list.Sort((a, b) =>
                    {
                        double aa = Math.Atan2(a.Y - centroid.Y, a.X - centroid.X);
                        double bb2 = Math.Atan2(b.Y - centroid.Y, b.X - centroid.X);
                        return aa.CompareTo(bb2);
                    });
                }
                else
                {
                    // CEILING_FOLLOW / FLOOR_FOLLOW: sort by X then Y
                    list.Sort((a, b) =>
                    {
                        int cmp = a.X.CompareTo(b.X);
                        return cmp != 0 ? cmp : a.Y.CompareTo(b.Y);
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"OrderEndpoints: {ex.Message}"); }
            return list;
        }

        private List<XYZ> ApplyFaceOffset(List<XYZ> ordered, PlacementRule rule, Room room, string mode)
        {
            var offset = new List<XYZ>();
            // Phase 139.28 — diameter-aware standoff when MountingContext=SURFACE.
            // BS 5572 / BS EN 12056-2 standoff scales with pipe DN:
            //   ≤ 25 mm  → 35 mm clip standoff
            //   ≤ 50 mm  → 50 mm
            //   ≤ 100 mm → 65 mm
            //   ≤ 150 mm → 85 mm
            //   > 150 mm → 100 mm
            // Plus insulation thickness on top.
            double offsetMm = rule.RouteOffsetMm;
            string ctx = (rule.MountingContext ?? "").ToUpperInvariant();
            if (ctx == "SURFACE" && rule.NominalDiameterMm > 0)
            {
                double dn = rule.NominalDiameterMm;
                double standoffMm =
                      dn <=  25.0 ? 35.0
                    : dn <=  50.0 ? 50.0
                    : dn <= 100.0 ? 65.0
                    : dn <= 150.0 ? 85.0
                    : 100.0;
                offsetMm = standoffMm + rule.InsulationThicknessMm;
            }
            string face = (rule.RouteFace ?? "INTERIOR").ToUpperInvariant();
            double sign = (face == "INTERIOR" || face == "BOTTOM") ? -1.0 : 1.0;
            double offFt = offsetMm * MmToFt * sign;

            foreach (var p in ordered)
            {
                XYZ shifted = p;
                if (mode == "CEILING_FOLLOW" || mode == "FLOOR_FOLLOW")
                {
                    shifted = new XYZ(p.X, p.Y, p.Z + offFt);
                }
                else if (mode == "WALL_FOLLOW")
                {
                    // Inward-normal offset: caller has already placed at face,
                    // so we shift along (X+Y) inward bias.  v1 simplification:
                    // shift toward room centroid by offFt magnitude.
                    var bb = room?.get_BoundingBox(null);
                    XYZ centroid = (bb != null) ? (bb.Min + bb.Max) * 0.5 : p;
                    XYZ dir = (centroid - p);
                    if (dir.GetLength() > 1e-6) dir = dir.Normalize();
                    else dir = XYZ.BasisX;
                    shifted = p + dir.Multiply(Math.Abs(offFt)) * sign;
                }
                offset.Add(shifted);
            }
            return offset;
        }

        private ElementId ResolveDefaultType(string segmentCategory)
        {
            try
            {
                Type t = null;
                switch ((segmentCategory ?? "").ToUpperInvariant())
                {
                    case "CONDUIT":   t = typeof(ConduitType); break;
                    case "CABLETRAY": t = typeof(CableTrayType); break;
                    case "PIPE":      t = typeof(PipeType); break;
                    case "DUCT":      t = typeof(DuctType); break;
                }
                if (t == null) return ElementId.InvalidElementId;
                var first = new FilteredElementCollector(_doc).OfClass(t).FirstElementId();
                return first ?? ElementId.InvalidElementId;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ElementId.InvalidElementId; }
        }

        private ElementId ResolveSystemType(string segmentCategory)
        {
            try
            {
                Type t = null;
                switch ((segmentCategory ?? "").ToUpperInvariant())
                {
                    case "PIPE": t = typeof(PipingSystemType); break;
                    case "DUCT": t = typeof(MechanicalSystemType); break;
                    default: return ElementId.InvalidElementId;
                }
                var first = new FilteredElementCollector(_doc).OfClass(t).FirstElementId();
                return first ?? ElementId.InvalidElementId;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ElementId.InvalidElementId; }
        }

        private void TryStampRouteRuleId(ElementId id, string ruleId)
        {
            if (id == ElementId.InvalidElementId || string.IsNullOrEmpty(ruleId)) return;
            try
            {
                var el = _doc.GetElement(id);
                var p  = el?.LookupParameter("STING_ROUTE_RULE_ID_TXT");
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                    p.Set(ruleId);
            }
            catch (Exception ex) { StingLog.Warn($"TryStampRouteRuleId {id?.Value}: {ex.Message}"); }
        }

        /// <summary>
        /// Pick a sensible level when the host room has none. Strategy:
        ///   1. The level whose elevation is closest to the median Z of the endpoints.
        ///   2. Falls back to the first level by elevation if endpoints are null.
        /// Returns InvalidElementId if the project has no levels at all (unusual).
        /// </summary>
        private static ElementId FallbackLevelId(Document doc, IList<XYZ> endpoints)
        {
            if (doc == null) return ElementId.InvalidElementId;
            try
            {
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .ToList();
                if (levels.Count == 0) return ElementId.InvalidElementId;
                if (endpoints == null || endpoints.Count == 0) return levels[0].Id;

                double medianZ = endpoints
                    .Where(p => p != null)
                    .Select(p => p.Z)
                    .OrderBy(z => z)
                    .Skip(endpoints.Count / 2)
                    .FirstOrDefault();
                return levels
                    .OrderBy(l => Math.Abs(l.Elevation - medianZ))
                    .First()
                    .Id;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"WallFollowerRouter.FallbackLevelId: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }
    }
}
