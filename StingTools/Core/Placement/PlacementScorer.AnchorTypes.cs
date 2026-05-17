using StingTools.Core;
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
using System.Text.RegularExpressions;

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

                // ── Phase 139.2 Q — new anchor types ───────────────
                case "STRUCTURAL_SOFFIT":
                    EmitStructuralSoffit(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    return true;

                case "CEILING_TILE_CENTRE":
                    EmitCeilingTileCentre(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    return true;

                case "WALL_FACE_OFFSET":
                    EmitWallFaceOffset(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    return true;

                case "DOOR_LATCH_SIDE":
                    EmitDoorLatchSide(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    return true;

                case "DOOR_HINGE_SIDE_150":
                    EmitDoorHingeSide150(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    return true;

                case "CONDUIT_BOX_MATCHED":
                    EmitConduitBoxMatched(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    return true;

                case "CEILING_VOID_ABOVE_BOX":
                    EmitCeilingVoidAboveBox(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    return true;

                case "FLOOR_SLAB_PENETRATION":
                    EmitFloorSlabPenetration(room, rule, anchorZ, offsetXFt, offsetYFt, points);
                    return true;

                // ── Phase 177 — Toilet / WC room anchor types ──────────
                // WINDOW_SIDE_WALL_RIGHT: emits a point on the right-hand side wall
                // (right when seated at the WC facing the door) relative to the window
                // wall. Used for toilet paper holder, side grab bar, sanitary bin, etc.
                case "WINDOW_SIDE_WALL_RIGHT":
                    EmitWindowSideWall(room, rule, anchorZ, offsetXFt, offsetYFt, points, isRight: true);
                    return true;

                // WINDOW_SIDE_WALL_LEFT: left-hand side wall equivalent.
                // Used for left grab bar (fold-down ADA), left-side accessories.
                case "WINDOW_SIDE_WALL_LEFT":
                    EmitWindowSideWall(room, rule, anchorZ, offsetXFt, offsetYFt, points, isRight: false);
                    return true;
            }
            return false;
        }

        // ── Phase 177 — Toilet room anchor implementations ────────────

        /// <summary>
        /// Emits a placement point on the right or left side wall relative to the
        /// window wall (which is treated as the "back wall" behind the WC).
        ///
        /// Algorithm:
        ///  1. Find the window in the room (uses existing boundary cache).
        ///  2. Compute the inward normal of the window's host wall (pointing into room).
        ///  3. Rotate 90° CW (right) or CCW (left) in the XY plane.
        ///  4. Estimate the WC position at windowOrigin + inward × 305mm rough-in.
        ///  5. Emit: WC_pos + sideDir × OffsetXMm + inward × OffsetYMm at anchorZ.
        ///
        /// OffsetXMm = distance from WC centreline toward the side wall (e.g. 200mm).
        /// OffsetYMm = forward shift from WC centreline toward door (e.g. 150mm).
        /// MountingHeightMm = vertical position from FFL (e.g. 600mm for TP holder).
        ///
        /// Falls back to ROOM_CENTRE when no window is found (e.g. internal rooms
        /// without windows, where the WC is placed by the fallback no-window rule).
        /// </summary>
        private void EmitWindowSideWall(
            Room room,
            PlacementRule rule,
            double anchorZ,
            double offsetXFt,
            double offsetYFt,
            List<XYZ> points,
            bool isRight)
        {
            try
            {
                var b = GetBoundary(room);
                if (b?.Windows == null || b.Windows.Count == 0)
                {
                    // No window — fall back to room centre so the rule still fires
                    // (the WC itself used a wall-corner fallback in this case).
                    Fallback(room, anchorZ, offsetXFt, offsetYFt, points);
                    return;
                }

                // Take the first window (toilet rooms typically have one).
                FamilyInstance win = b.Windows[0];
                XYZ winOrigin = (win.Location as LocationPoint)?.Point;
                if (winOrigin == null)
                {
                    Fallback(room, anchorZ, offsetXFt, offsetYFt, points);
                    return;
                }

                // ── resolve the window wall's inward normal ──
                XYZ inward = null;
                try
                {
                    // ElementId hostId exists on FamilyInstance via Host property (Revit 2025+).
                    // GetElement may return null if the host is a curtain panel — guard accordingly.
                    var hostEl = _doc.GetElement(win.Host?.Id);
                    if (hostEl is Wall hostWall)
                        inward = ComputeInwardFromWall(hostWall, room);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"EmitWindowSideWall: host-wall resolve failed for win {win.Id}: {ex.Message}");
                }

                if (inward == null)
                {
                    // Fall back: direction from window toward room centroid.
                    XYZ roomPt = (room.Location as LocationPoint)?.Point;
                    if (roomPt != null)
                    {
                        XYZ delta = new XYZ(roomPt.X - winOrigin.X, roomPt.Y - winOrigin.Y, 0);
                        double len = delta.GetLength();
                        inward = len > 1e-6 ? delta.Divide(len) : XYZ.BasisY;
                    }
                    else
                    {
                        Fallback(room, anchorZ, offsetXFt, offsetYFt, points);
                        return;
                    }
                }

                // ── 90° CW rotation = (x,y) → (y,−x) = RIGHT when inward faces north ──
                // ── 90° CCW rotation = (x,y) → (−y, x) = LEFT                          ──
                XYZ sideDir = isRight
                    ? new XYZ( inward.Y, -inward.X, 0)
                    : new XYZ(-inward.Y,  inward.X, 0);

                // WC centreline estimate: 305mm (12 in, standard US rough-in) from back wall.
                // This matches the OffsetYMm=305 used by toilet-wc-at-window-standard rule.
                const double RoughInFt = 305.0 / 304.8;
                XYZ wcPos = new XYZ(
                    winOrigin.X + inward.X * RoughInFt,
                    winOrigin.Y + inward.Y * RoughInFt,
                    anchorZ);

                // ── final point: WC pos shifted sideways by OffsetX and forward by OffsetY ──
                // offsetXFt already encodes rule.OffsetXMm (side distance from WC CL).
                // offsetYFt encodes rule.OffsetYMm (forward shift toward door).
                XYZ pos = new XYZ(
                    wcPos.X + sideDir.X * offsetXFt + inward.X * offsetYFt,
                    wcPos.Y + sideDir.Y * offsetXFt + inward.Y * offsetYFt,
                    anchorZ);

                points.Add(pos);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlacementScorer.EmitWindowSideWall room={room.Id} right={isRight}: {ex.Message}");
                Fallback(room, anchorZ, offsetXFt, offsetYFt, points);
            }
        }

        // ── Phase 139.2 Q — new anchor implementations ────────────────

        private void EmitStructuralSoffit(Room room, PlacementRule rule,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            try
            {
                var bb = room.get_BoundingBox(null);
                if (bb == null) return;
                double centreX = (bb.Min.X + bb.Max.X) * 0.5;
                double centreY = (bb.Min.Y + bb.Max.Y) * 0.5;
                double soffitZ = bb.Max.Z;

                // Look for a structural floor immediately above to refine the soffit Z.
                var pad = 1.0;
                var outline = new Outline(
                    new XYZ(bb.Min.X - pad, bb.Min.Y - pad, bb.Min.Z),
                    new XYZ(bb.Max.X + pad, bb.Max.Y + pad, bb.Max.Z + 6.0));
                var bbf = new BoundingBoxIntersectsFilter(outline);
                foreach (var el in new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_Floors)
                    .WhereElementIsNotElementType().WherePasses(bbf))
                {
                    var fbb = el.get_BoundingBox(null);
                    if (fbb == null) continue;
                    if (fbb.Min.Z >= bb.Max.Z) { soffitZ = fbb.Min.Z; break; }
                }
                points.Add(new XYZ(centreX + offsetXFt, centreY + offsetYFt, soffitZ));
            }
            catch (Exception ex) { StingLog.Warn($"EmitStructuralSoffit: {ex.Message}"); }
        }

        private void EmitCeilingTileCentre(Room room, PlacementRule rule,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            try
            {
                double tileXMm = rule.TileGridSpacingXMm > 0 ? rule.TileGridSpacingXMm : 600.0;
                double tileYMm = rule.TileGridSpacingYMm > 0 ? rule.TileGridSpacingYMm : 600.0;
                double stepX = tileXMm / 304.8;
                double stepY = tileYMm / 304.8;

                var bb = room.get_BoundingBox(null);
                if (bb == null) return;
                double centreX = (bb.Min.X + bb.Max.X) * 0.5;
                double centreY = (bb.Min.Y + bb.Max.Y) * 0.5;
                double snappedX = bb.Min.X + Math.Round((centreX - bb.Min.X) / stepX) * stepX + stepX * 0.5;
                double snappedY = bb.Min.Y + Math.Round((centreY - bb.Min.Y) / stepY) * stepY + stepY * 0.5;
                points.Add(new XYZ(snappedX + offsetXFt, snappedY + offsetYFt, anchorZ));
            }
            catch (Exception ex) { StingLog.Warn($"EmitCeilingTileCentre: {ex.Message}"); }
        }

        private void EmitWallFaceOffset(Room room, PlacementRule rule,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            // Mirror WALL_MIDPOINT but apply the rule's plaster offset along the inward wall normal.
            var prev = new List<XYZ>();
            EmitWallMidpoints(room, rule, anchorZ, offsetXFt, offsetYFt, prev);
            if (prev.Count == 0) return;
            try
            {
                Wall firstWall = null;
                var bnd = GetBoundary(room);
                if (bnd != null)
                {
                    foreach (var seg in bnd.Segments)
                    {
                        if (seg?.Wall != null) { firstWall = seg.Wall; break; }
                    }
                }
                double offsetFt = firstWall != null ? PlasterOffsetResolver.Resolve(firstWall, rule) : 0.0;
                if (Math.Abs(offsetFt) < 1e-9) { points.AddRange(prev); return; }
                XYZ n = (firstWall != null && firstWall.Orientation != null) ? firstWall.Orientation : XYZ.BasisX;
                foreach (var p in prev) points.Add(new XYZ(p.X + n.X * offsetFt, p.Y + n.Y * offsetFt, p.Z));
            }
            catch (Exception ex) { StingLog.Warn($"EmitWallFaceOffset: {ex.Message}"); points.AddRange(prev); }
        }

        private void EmitDoorLatchSide(Room room, PlacementRule rule,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            var b = GetBoundary(room);
            if (b == null || b.Doors == null) return;
            double shiftFt = 150.0 / 304.8;
            foreach (var door in b.Doors)
            {
                XYZ origin = (door.Location as LocationPoint)?.Point;
                if (origin == null) continue;
                XYZ along = WallTangent(door.Host as Wall) ?? XYZ.BasisX;
                double sign = door.HandFlipped ? -1 : 1;
                XYZ p = origin + along.Multiply(sign * shiftFt);
                points.Add(new XYZ(p.X + offsetXFt, p.Y + offsetYFt, anchorZ));
            }
        }

        private void EmitDoorHingeSide150(Room room, PlacementRule rule,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            var b = GetBoundary(room);
            if (b == null || b.Doors == null) return;
            double shiftFt = 150.0 / 304.8;
            foreach (var door in b.Doors)
            {
                XYZ origin = (door.Location as LocationPoint)?.Point;
                if (origin == null) continue;
                XYZ along = WallTangent(door.Host as Wall) ?? XYZ.BasisX;
                double sign = door.HandFlipped ? 1 : -1;
                XYZ p = origin + along.Multiply(sign * shiftFt);
                points.Add(new XYZ(p.X + offsetXFt, p.Y + offsetYFt, anchorZ));
            }
        }

        private struct BoxIndexEntry
        {
            public string FamilyName;
            public string TypeName;
            public string LocationId;
            public XYZ Origin;
        }

        private readonly Dictionary<ElementId, List<BoxIndexEntry>> _boxIndexCache
            = new Dictionary<ElementId, List<BoxIndexEntry>>();

        private List<BoxIndexEntry> GetBoxIndexForRoom(Room room, string paramName)
        {
            if (_boxIndexCache.TryGetValue(room.Id, out var cached)) return cached;
            var list = new List<BoxIndexEntry>();
            try
            {
                var bb = room.get_BoundingBox(null);
                if (bb != null)
                {
                    var pad = 0.5;
                    var outline = new Outline(
                        new XYZ(bb.Min.X - pad, bb.Min.Y - pad, bb.Min.Z - pad),
                        new XYZ(bb.Max.X + pad, bb.Max.Y + pad, bb.Max.Z + pad));
                    var bbf = new BoundingBoxIntersectsFilter(outline);
                    foreach (var el in new FilteredElementCollector(_doc)
                        .OfClass(typeof(FamilyInstance)).WherePasses(bbf))
                    {
                        if (!(el is FamilyInstance fi)) continue;
                        var p = fi.LookupParameter(paramName);
                        if (p == null || !p.HasValue || p.StorageType != StorageType.String) continue;
                        string id = p.AsString() ?? "";
                        if (string.IsNullOrEmpty(id)) continue;
                        XYZ origin = (fi.Location as LocationPoint)?.Point;
                        if (origin == null) continue;
                        list.Add(new BoxIndexEntry
                        {
                            FamilyName = fi.Symbol?.Family?.Name ?? "",
                            TypeName   = fi.Symbol?.Name ?? "",
                            LocationId = id,
                            Origin     = origin,
                        });
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetBoxIndexForRoom {room.Id}: {ex.Message}"); }
            _boxIndexCache[room.Id] = list;
            return list;
        }

        private void EmitConduitBoxMatched(Room room, PlacementRule rule,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            try
            {
                System.Text.RegularExpressions.Regex rx = null;
                if (!string.IsNullOrEmpty(rule.BoxFamilyTypeRegex))
                {
                    try { rx = new System.Text.RegularExpressions.Regex(rule.BoxFamilyTypeRegex,
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
                    catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); rx = null; }
                }
                string paramName = string.IsNullOrEmpty(rule.BoxLocationIdParam)
                    ? ParamRegistry.BOX_LOCATION_ID : rule.BoxLocationIdParam;
                foreach (var entry in GetBoxIndexForRoom(room, paramName))
                {
                    if (rx != null && !rx.IsMatch(entry.FamilyName) && !rx.IsMatch(entry.TypeName))
                        continue;
                    points.Add(new XYZ(entry.Origin.X + offsetXFt, entry.Origin.Y + offsetYFt, anchorZ));
                }
            }
            catch (Exception ex) { StingLog.Warn($"EmitConduitBoxMatched: {ex.Message}"); }
        }

        private readonly Dictionary<ElementId, List<XYZ>> _outletCache
            = new Dictionary<ElementId, List<XYZ>>();

        private List<XYZ> GetOutletPositions(Room room)
        {
            if (_outletCache.TryGetValue(room.Id, out var hit)) return hit;
            var outlets = new List<XYZ>();
            try
            {
                var bb = room.get_BoundingBox(null);
                if (bb != null)
                {
                    var pad = 0.5;
                    var outline = new Outline(
                        new XYZ(bb.Min.X - pad, bb.Min.Y - pad, bb.Min.Z - pad),
                        new XYZ(bb.Max.X + pad, bb.Max.Y + pad, bb.Max.Z + pad));
                    var bbf = new BoundingBoxIntersectsFilter(outline);
                    foreach (var el in new FilteredElementCollector(_doc)
                        .OfCategory(BuiltInCategory.OST_ElectricalFixtures)
                        .WhereElementIsNotElementType()
                        .WherePasses(bbf))
                    {
                        XYZ p = (el.Location as LocationPoint)?.Point;
                        if (p != null) outlets.Add(p);
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetOutletPositions {room.Id}: {ex.Message}"); }
            _outletCache[room.Id] = outlets;
            return outlets;
        }

        private void EmitCeilingVoidAboveBox(Room room, PlacementRule rule,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            try
            {
                var bb = room.get_BoundingBox(null);
                if (bb == null) return;
                var outletPositions = GetOutletPositions(room);
                if (outletPositions.Count < 2) return;
                for (int i = 0; i < outletPositions.Count - 1; i++)
                {
                    XYZ mid = (outletPositions[i] + outletPositions[i + 1]) * 0.5;
                    points.Add(new XYZ(mid.X + offsetXFt, mid.Y + offsetYFt, bb.Max.Z));
                }
            }
            catch (Exception ex) { StingLog.Warn($"EmitCeilingVoidAboveBox: {ex.Message}"); }
        }

        private void EmitFloorSlabPenetration(Room room, PlacementRule rule,
            double anchorZ, double offsetXFt, double offsetYFt, List<XYZ> points)
        {
            try
            {
                var bb = room.get_BoundingBox(null);
                if (bb == null) return;
                double cx = (bb.Min.X + bb.Max.X) * 0.5;
                double cy = (bb.Min.Y + bb.Max.Y) * 0.5;
                points.Add(new XYZ(cx + offsetXFt, cy + offsetYFt, bb.Min.Z));
            }
            catch (Exception ex) { StingLog.Warn($"EmitFloorSlabPenetration: {ex.Message}"); }
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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }
    }
}
