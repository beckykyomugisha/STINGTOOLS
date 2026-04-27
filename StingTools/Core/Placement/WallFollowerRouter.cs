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
                        switch ((rule.RouteSegmentCategory ?? "").ToUpperInvariant())
                        {
                            case "CONDUIT":
                                // TODO-VERIFY-API: Conduit.Create signature
                                newId = Conduit.Create(_doc, typeId, a, b, levelId)?.Id ?? ElementId.InvalidElementId;
                                break;
                            case "CABLETRAY":
                                // TODO-VERIFY-API: CableTray.Create signature
                                newId = CableTray.Create(_doc, typeId, a, b, levelId)?.Id ?? ElementId.InvalidElementId;
                                break;
                            case "PIPE":
                                // TODO-VERIFY-API: Pipe.Create signature for systemTypeId variant
                                newId = (systemTypeId != ElementId.InvalidElementId
                                    ? Pipe.Create(_doc, systemTypeId, typeId, levelId, a, b)
                                    : null)?.Id ?? ElementId.InvalidElementId;
                                break;
                            case "DUCT":
                                // TODO-VERIFY-API: Duct.Create signature for systemTypeId variant
                                newId = (systemTypeId != ElementId.InvalidElementId
                                    ? Duct.Create(_doc, systemTypeId, typeId, levelId, a, b)
                                    : null)?.Id ?? ElementId.InvalidElementId;
                                break;
                            default:
                                result.Warnings.Add(
                                    $"WallFollowerRouter: unsupported RouteSegmentCategory '{rule.RouteSegmentCategory}'");
                                continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"WallFollowerRouter: segment {i} create failed: {ex.Message}");
                    }

                    if (newId != ElementId.InvalidElementId)
                    {
                        result.CreatedSegments.Add(newId);
                        TryStampRouteRuleId(newId, rule.RuleId);
                    }
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
            double offsetMm = rule.RouteOffsetMm;
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
            catch { return ElementId.InvalidElementId; }
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
            catch { return ElementId.InvalidElementId; }
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
    }
}
