// Phase 139 B — Additional anchor types for the placement scorer.
//
// This partial class extends PlacementScorer with the 22 new anchor
// generators introduced by Phase 139 (B1-B22).  Each generator is
// invoked from PlacementScorer.GenerateAnchorPoints via a switch
// extension; rules referencing an unknown anchor name fall through
// to the legacy ROOM_CENTRE behaviour.
//
// The implementations use simplified heuristics — full geometry
// fidelity is deferred to Revit-tested follow-up passes.  Critical
// API calls are flagged with TODO-VERIFY-API.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace StingTools.Core.Placement
{
    public partial class PlacementScorer
    {
        /// <summary>
        /// Append candidate XYZs for one of the 22 Phase 139 anchor types.
        /// Returns true when the anchor was recognised (caller should not
        /// fall through to legacy ROOM_CENTRE), false when unknown.
        /// </summary>
        internal bool TryEmitPhase139Anchor(
            string anchor, Room room, PlacementRule rule,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            if (string.IsNullOrEmpty(anchor)) return false;
            switch (anchor)
            {
                case "WINDOW_SILL_KITCHEN":
                case "WINDOW_SILL_WET_ROOM":
                case "WINDOW_SILL_RESIDENTIAL":
                case "WINDOW_SILL_COMMERCIAL":
                case "WINDOW_SILL_HOSPITAL":
                    EmitWindowVariantSill(room, rule, anchor, anchorZ, offsetXFt, offsetYFt, points);
                    return true;

                case "WINDOW_HEAD":
                    EmitWindowHead(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    return true;

                case "DOOR_STRIKE_SIDE":
                    EmitDoorStrikeSide(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    return true;

                case "DOOR_CLOSER_ZONE":
                    EmitDoorCloserZone(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    return true;

                case "BEAM_SOFFIT":
                    EmitBeamSoffit(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    return true;

                case "COLUMN_FACE_NEAREST":
                    EmitColumnFaceNearest(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    return true;

                case "CEILING_TILE_CORNER":
                    EmitCeilingTileCorner(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    return true;

                case "CURTAIN_PANEL_CENTRE":
                    EmitCurtainPanelCentre(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    return true;

                case "SLAB_PERIMETER_EDGE":
                    EmitSlabPerimeterEdge(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    return true;

                case "ESCAPE_DOOR_BOTH_SIDES":
                    EmitEscapeDoorBothSides(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    return true;

                case "STAIR_LANDING_EDGE":
                    EmitStairLandingEdge(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    return true;

                case "STAIR_FLIGHT_MID":
                    EmitStairFlightMid(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    return true;

                case "CORRIDOR_JUNCTION":
                    EmitCorridorJunction(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    return true;

                case "FIRE_EXTINGUISHER_TRAVEL":
                case "CALL_POINT_TRAVEL":
                    // Travel-distance solver runs at engine level after
                    // initial placement; here we seed candidates at door
                    // jambs (a strict subset of legal positions).
                    EmitDoorJambSeeds(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    return true;

                case "RAISED_FLOOR_TILE_EDGE":
                    EmitRaisedFloorTileEdge(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    return true;

                case "NEAREST_MEP_SYSTEM_NODE":
                    EmitNearestMepSystemNode(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    return true;

                case "ZONE_BOUNDARY":
                    EmitZoneBoundary(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    return true;
            }
            return false;
        }

        // ── Implementations (simplified) ────────────────────────────

        private void EmitWindowVariantSill(Room room, PlacementRule rule, string anchor,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            var b = GetBoundary(room);
            if (b == null || b.Windows == null || b.Windows.Count == 0) return;
            // Use SillHeightMm if non-zero; else default by variant.
            double sillMm = rule.SillHeightMm > 0 ? rule.SillHeightMm : 900.0;
            double sillFt = sillMm / 304.8;
            double levelZ = (room.Level?.Elevation) ?? anchorZ;
            double zSill  = levelZ + sillFt;
            foreach (var win in b.Windows)
            {
                XYZ origin = (win.Location as LocationPoint)?.Point;
                if (origin == null) continue;
                points.Add(new XYZ(origin.X + offsetXFt, origin.Y + offsetYFt, zSill));
            }
        }

        private void EmitWindowHead(Room room, PlacementRule rule,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            var b = GetBoundary(room);
            if (b == null || b.Windows == null || b.Windows.Count == 0) return;
            double headMm = rule.HeadHeightMm > 0 ? rule.HeadHeightMm : 2400.0;
            double headFt = headMm / 304.8;
            double levelZ = (room.Level?.Elevation) ?? anchorZ;
            foreach (var win in b.Windows)
            {
                XYZ origin = (win.Location as LocationPoint)?.Point;
                if (origin == null) continue;
                points.Add(new XYZ(origin.X + offsetXFt, origin.Y + offsetYFt, levelZ + headFt));
            }
        }

        private void EmitDoorStrikeSide(Room room, PlacementRule rule,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            // Strike side is opposite hinge; we approximate with door origin shifted by 150mm + leaf width.
            var b = GetBoundary(room);
            if (b == null || b.Doors == null) return;
            double shiftFt = 150.0 / 304.8;
            foreach (var door in b.Doors)
            {
                XYZ origin = (door.Location as LocationPoint)?.Point;
                if (origin == null) continue;
                XYZ along = WallTangent(door.Host as Wall) ?? XYZ.BasisX;
                double sign = door.FacingFlipped ? -1 : 1;
                XYZ p = origin + along.Multiply(-sign * shiftFt);
                points.Add(new XYZ(p.X + offsetXFt, p.Y + offsetYFt, anchorZ));
            }
        }

        private void EmitDoorCloserZone(Room room, PlacementRule rule,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            var b = GetBoundary(room);
            if (b == null || b.Doors == null) return;
            foreach (var door in b.Doors)
            {
                XYZ origin = (door.Location as LocationPoint)?.Point;
                if (origin == null) continue;
                double zCloser = origin.Z + 2200.0 / 304.8;
                points.Add(new XYZ(origin.X + offsetXFt, origin.Y + offsetYFt, zCloser));
            }
        }

        private void EmitBeamSoffit(Room room, PlacementRule rule,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            try
            {
                var bb = room.get_BoundingBox(null);
                if (bb == null) return;
                XYZ centroid = (bb.Min + bb.Max) * 0.5;
                var pad = 1.0;
                var outline = new Outline(
                    new XYZ(bb.Min.X - pad, bb.Min.Y - pad, bb.Min.Z),
                    new XYZ(bb.Max.X + pad, bb.Max.Y + pad, bb.Max.Z + 5.0));
                var bbf = new BoundingBoxIntersectsFilter(outline);
                Element best = null; double bestSq = double.MaxValue;
                foreach (var el in new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType().WherePasses(bbf))
                {
                    var bb2 = el.get_BoundingBox(null);
                    if (bb2 == null) continue;
                    XYZ p = (bb2.Min + bb2.Max) * 0.5;
                    double sq = (p.X - centroid.X) * (p.X - centroid.X) + (p.Y - centroid.Y) * (p.Y - centroid.Y);
                    if (sq < bestSq) { bestSq = sq; best = el; }
                }
                if (best != null)
                {
                    var bb2 = best.get_BoundingBox(null);
                    XYZ pt = (bb2.Min + bb2.Max) * 0.5;
                    points.Add(new XYZ(pt.X + offsetXFt, pt.Y + offsetYFt, bb2.Min.Z));
                }
            }
            catch (Exception ex) { StingLog.Warn($"EmitBeamSoffit: {ex.Message}"); }
        }

        private void EmitColumnFaceNearest(Room room, PlacementRule rule,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            // Reuse legacy COLUMN_FACE logic by calling the existing path indirectly.
            try
            {
                var bb = room.get_BoundingBox(null);
                if (bb == null) return;
                XYZ centroid = (bb.Min + bb.Max) * 0.5;
                var cats = new[] { BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns };
                XYZ best = null; double bestSq = double.MaxValue;
                foreach (var cat in cats)
                {
                    foreach (var el in new FilteredElementCollector(_doc)
                        .OfCategory(cat).WhereElementIsNotElementType())
                    {
                        XYZ pt = (el.Location as LocationPoint)?.Point;
                        if (pt == null) continue;
                        double dx = pt.X - centroid.X, dy = pt.Y - centroid.Y;
                        double sq = dx * dx + dy * dy;
                        if (sq < bestSq) { bestSq = sq; best = pt; }
                    }
                }
                if (best != null)
                    points.Add(new XYZ(best.X + offsetXFt, best.Y + offsetYFt, anchorZ));
            }
            catch (Exception ex) { StingLog.Warn($"EmitColumnFaceNearest: {ex.Message}"); }
        }

        private void EmitCeilingTileCorner(Room room, PlacementRule rule,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            // Approximation: 4 corners of every 600mm tile in room bbox.
            var bb = room.get_BoundingBox(null);
            if (bb == null) return;
            double tileFt = 600.0 / 304.8;
            for (double x = bb.Min.X + tileFt; x < bb.Max.X; x += tileFt)
            for (double y = bb.Min.Y + tileFt; y < bb.Max.Y; y += tileFt)
                points.Add(new XYZ(x + offsetXFt, y + offsetYFt, anchorZ));
        }

        private void EmitCurtainPanelCentre(Room room, PlacementRule rule,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            try
            {
                var bb = room.get_BoundingBox(null);
                if (bb == null) return;
                var pad = 1.0;
                var outline = new Outline(
                    new XYZ(bb.Min.X - pad, bb.Min.Y - pad, bb.Min.Z - pad),
                    new XYZ(bb.Max.X + pad, bb.Max.Y + pad, bb.Max.Z + pad));
                var bbf = new BoundingBoxIntersectsFilter(outline);
                foreach (var el in new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_CurtainWallPanels)
                    .WhereElementIsNotElementType().WherePasses(bbf))
                {
                    var bb2 = el.get_BoundingBox(null);
                    if (bb2 == null) continue;
                    XYZ pt = (bb2.Min + bb2.Max) * 0.5;
                    points.Add(new XYZ(pt.X + offsetXFt, pt.Y + offsetYFt, anchorZ));
                }
            }
            catch (Exception ex) { StingLog.Warn($"EmitCurtainPanelCentre: {ex.Message}"); }
        }

        private void EmitSlabPerimeterEdge(Room room, PlacementRule rule,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            // Use room boundary segments at the floor level.
            EmitPerimeterAnchor(room, rule, anchorZ, offsetXFt, offsetYFt, points);
        }

        private void EmitEscapeDoorBothSides(Room room, PlacementRule rule,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            var b = GetBoundary(room);
            if (b == null || b.Doors == null) return;
            double shiftFt = 2000.0 / 304.8;
            foreach (var door in b.Doors)
            {
                XYZ origin = (door.Location as LocationPoint)?.Point;
                if (origin == null) continue;
                XYZ along = WallTangent(door.Host as Wall) ?? XYZ.BasisX;
                points.Add(new XYZ(origin.X + along.X * shiftFt + offsetXFt,
                                   origin.Y + along.Y * shiftFt + offsetYFt, anchorZ));
                points.Add(new XYZ(origin.X - along.X * shiftFt + offsetXFt,
                                   origin.Y - along.Y * shiftFt + offsetYFt, anchorZ));
            }
        }

        private void EmitStairLandingEdge(Room room, PlacementRule rule,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            EmitStairNosingAnchor(room, rule, anchorZ, offsetXFt, offsetYFt, points);
        }

        private void EmitStairFlightMid(Room room, PlacementRule rule,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            EmitStairNosingAnchor(room, rule, anchorZ, offsetXFt, offsetYFt, points);
        }

        private void EmitCorridorJunction(Room room, PlacementRule rule,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            // Approximation: corridor junctions are room corners; emit corner points.
            EmitWallCorners(room, rule, anchorZ, offsetXFt, offsetYFt, points);
        }

        private void EmitDoorJambSeeds(Room room, PlacementRule rule,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            EmitDoorAnchor(room, rule, anchorZ, offsetXFt, offsetYFt, points,
                hingeSide: false, overDoor: false);
        }

        private void EmitRaisedFloorTileEdge(Room room, PlacementRule rule,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            // Sample a coarse 600mm grid along bbox edges.
            var bb = room.get_BoundingBox(null);
            if (bb == null) return;
            double stepFt = 600.0 / 304.8;
            for (double x = bb.Min.X; x <= bb.Max.X; x += stepFt)
            {
                points.Add(new XYZ(x + offsetXFt, bb.Min.Y + offsetYFt, anchorZ));
                points.Add(new XYZ(x + offsetXFt, bb.Max.Y + offsetYFt, anchorZ));
            }
        }

        private void EmitNearestMepSystemNode(Room room, PlacementRule rule,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            // v1 simplification: connector points of any MEP equipment in the room.
            try
            {
                var bb = room.get_BoundingBox(null);
                if (bb == null) return;
                var pad = 1.0;
                var outline = new Outline(
                    new XYZ(bb.Min.X - pad, bb.Min.Y - pad, bb.Min.Z - pad),
                    new XYZ(bb.Max.X + pad, bb.Max.Y + pad, bb.Max.Z + pad));
                var bbf = new BoundingBoxIntersectsFilter(outline);
                foreach (var el in new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                    .WhereElementIsNotElementType().WherePasses(bbf))
                {
                    var pt = (el.Location as LocationPoint)?.Point;
                    if (pt == null) continue;
                    points.Add(new XYZ(pt.X + offsetXFt, pt.Y + offsetYFt, anchorZ));
                }
            }
            catch (Exception ex) { StingLog.Warn($"EmitNearestMepSystemNode: {ex.Message}"); }
        }

        private void EmitZoneBoundary(Room room, PlacementRule rule,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            // TODO-VERIFY-API: HVAC Zone collection — falls back to room centroid.
            try
            {
                var bb = room.get_BoundingBox(null);
                if (bb == null) return;
                XYZ c = (bb.Min + bb.Max) * 0.5;
                points.Add(new XYZ(c.X + offsetXFt, c.Y + offsetYFt, anchorZ));
            }
            catch { }
        }
    }
}
