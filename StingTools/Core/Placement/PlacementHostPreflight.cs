// StingTools v4 MVP — placement host pre-flight.
//
// Bridges PlacementRule.AnchorType to FamilySymbol.Family.FamilyPlacementType.
// Revit's NewFamilyInstance overloads are placement-type-specific:
//
//   OneLevelBased / OneLevelBasedHosted   → (point, symbol, level, st)
//   WorkPlaneBased                        → (point, symbol, level, st)
//                                          OR (reference, point, refDir, symbol)
//   WallBased / OneLevelBasedHosted+Wall  → (point, symbol, hostWall, level, st)
//   CeilingBased / *FloorBased / RoofBased→ (point, symbol, hostElement, level, st)
//   ViewBased                             → out of scope for FixturePlacementEngine
//
// Calling the wrong overload either throws or places a free-standing
// instance whose Host is null — schedules then silently miss the host
// in QTO / COBie. This pre-flight picks the right overload up-front and,
// when a host is required, locates a sensible candidate (the nearest
// ceiling for a CeilingBased family, the nearest wall for a WallBased
// family, …) at the placement point.
//
// Family-level template promotion (Wall → Ceiling) is NOT possible via
// the Revit API — Family.FamilyPlacementType is read-only and the
// family editor blocks template-lineage swaps for hosted templates.
// What this class does is the next-best thing: pick the right overload
// for the family's existing template, and surface a clear warning when
// the rule's intent (anchor) and the family's template disagree so the
// user can fix the rule, swap families, or run the family Quick-Edit
// → ChangeHost flow.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;

namespace StingTools.Core.Placement
{
    /// <summary>Result of one pre-flight call. Carries either the
    /// successfully created FamilyInstance or a structured reason for
    /// skipping (so the engine can collect them in PlacementResult.Warnings).</summary>
    public class PlacementHostPreflightResult
    {
        public FamilyInstance Placed { get; set; }
        public bool Skipped { get; set; }
        public string Reason { get; set; } = "";
    }

    public static class PlacementHostPreflight
    {
        // Phase 139.2 — cache the 3-D view used for soffit raycasts so we
        // don't run a FilteredElementCollector for every PlaceOnCeilingSoffit
        // call. Cleared whenever the active document changes.
        private static View3D _cachedView3D;
        private static int _cachedView3DDocHash;

        private static View3D ResolveView3D(Document doc)
        {
            if (doc == null) return null;
            int docHash = doc.GetHashCode();
            if (_cachedView3D != null && _cachedView3DDocHash == docHash && _cachedView3D.IsValidObject)
                return _cachedView3D;
            try
            {
                _cachedView3D = new FilteredElementCollector(doc).OfClass(typeof(View3D))
                    .Cast<View3D>().FirstOrDefault(v => v != null && !v.IsTemplate);
                _cachedView3DDocHash = docHash;
            }
            catch { _cachedView3D = null; }
            return _cachedView3D;
        }

        /// <summary>Decide the right NewFamilyInstance overload for the
        /// supplied symbol + rule + candidate position, locate the host
        /// element when one is required, and return the placed instance.
        /// All calls happen inside the caller's existing Transaction.</summary>
        public static PlacementHostPreflightResult Place(
            Document doc,
            FamilySymbol symbol,
            Room room,
            XYZ position,
            PlacementRule rule)
        {
            var r = new PlacementHostPreflightResult();
            if (doc == null || symbol == null || position == null)
            {
                r.Skipped = true;
                r.Reason = "Pre-flight: null document / symbol / position.";
                return r;
            }

            var fpt = symbol.Family?.FamilyPlacementType ?? FamilyPlacementType.Invalid;
            try
            {
                switch (fpt)
                {
                    // OneLevelBased / TwoLevelsBased / WorkPlaneBased are
                    // the un-hosted templates — lighting devices, plumbing
                    // fixtures, MEP equipment. The historic level-based
                    // overload works.
                    case FamilyPlacementType.OneLevelBased:
                    case FamilyPlacementType.TwoLevelsBased:
                        r.Placed = doc.Create.NewFamilyInstance(
                            position, symbol, room?.Level, StructuralType.NonStructural);
                        return r;

                    case FamilyPlacementType.WorkPlaneBased:
                        // Phase 139.22 — face-based families. When the rule's
                        // anchor is wall-related, place via the face-based
                        // overload NewFamilyInstance(Reference, XYZ, XYZ, sym)
                        // so the family physically attaches to the wall's
                        // interior face. Falls back to level-based when no
                        // wall is nearby (free-standing face-based).
                        return TryFaceBasedPlace(doc, symbol, room, position, rule);

                    // OneLevelBasedHosted = wall / ceiling / floor / roof
                    // hosted templates. Locate the host before calling the
                    // host-aware overload.
                    case FamilyPlacementType.OneLevelBasedHosted:
                        return TryHostedPlace(doc, symbol, room, position, rule, fpt);

                    // ViewBased / CurveBased / Adaptive / Invalid — not
                    // something a fixture-placement engine should touch.
                    default:
                        r.Skipped = true;
                        r.Reason = $"Pre-flight: '{symbol.Family?.Name}' is {fpt} — fixture engine only supports OneLevelBased / TwoLevelsBased / WorkPlaneBased / OneLevelBasedHosted. " +
                                   "Use Family Quick Edit → Change Host on an existing instance, or pick a different family for this rule.";
                        return r;
                }
            }
            catch (Exception ex)
            {
                r.Skipped = true;
                r.Reason = $"Pre-flight: NewFamilyInstance({fpt}, {symbol.Family?.Name}) threw: {ex.Message}";
                return r;
            }
        }

        // Hosted families: walls / ceilings / floors / roofs. Locate the
        // nearest candidate within a bounded radius around the placement
        // point so the host is geometrically plausible.
        private static PlacementHostPreflightResult TryHostedPlace(
            Document doc,
            FamilySymbol symbol,
            Room room,
            XYZ position,
            PlacementRule rule,
            FamilyPlacementType fpt)
        {
            var r = new PlacementHostPreflightResult();
            string famName = symbol.Family?.Name ?? "?";

            // Heuristic choice of host kind based on the rule's anchor.
            string anchor = (rule?.AnchorType ?? "").ToUpperInvariant();
            bool prefersCeiling = anchor == "CEILING_CENTRE"
                                 || anchor == "LIGHTING_GRID"
                                 || anchor == "LUX_GRID"
                                 || anchor == "EN12464";
            // Phase 139.22 — extend the wall-anchor set so OneLevelBasedHosted
            // families targeted by Phase 139.2+ rules (WALL_FACE_OFFSET,
            // DOOR_LATCH_SIDE, DOOR_HINGE_SIDE_150, DOOR_HEAD, DOOR_STRIKE_SIDE,
            // DOOR_CLOSER_ZONE, ESCAPE_DOOR_BOTH_SIDES, WINDOW_HEAD, the
            // WINDOW_SILL_* variants) all route through TryHostedPlace's
            // NearestOf<Wall> search.  Pre-139.22 those anchors fell through
            // to the fallback chain and the engine guessed Ceiling first.
            bool prefersWall    = anchor == "WALL_MIDPOINT"
                                 || anchor == "WALL_CORNER"
                                 || anchor == "WALL_FACE_OFFSET"
                                 || anchor == "DOOR_HINGE"
                                 || anchor == "DOOR_JAMB"
                                 || anchor == "DOOR_HEAD"
                                 || anchor == "DOOR_LATCH_SIDE"
                                 || anchor == "DOOR_HINGE_SIDE_150"
                                 || anchor == "DOOR_STRIKE_SIDE"
                                 || anchor == "DOOR_CLOSER_ZONE"
                                 || anchor == "ESCAPE_DOOR_BOTH_SIDES"
                                 || anchor.StartsWith("WINDOW_");

            Element host = null;
            try
            {
                if (prefersWall)
                    host = NearestOf<Wall>(doc, position, 6.0); // ~1.83m
                else if (prefersCeiling)
                    host = NearestOf<Ceiling>(doc, position, 12.0); // ~3.66m
                else
                {
                    // Generic fallback: try ceiling first (lighting/sprinkler-y),
                    // then floor, then wall. Cast each result to Element so ??
                    // can chain across the sibling subclasses.
                    host = (Element)NearestOf<Ceiling>(doc, position, 12.0)
                        ?? (Element)NearestOf<Floor>(doc, position, 12.0)
                        ?? (Element)NearestOf<Wall>(doc, position, 6.0);
                }
            }
            catch (Exception ex)
            {
                r.Skipped = true;
                r.Reason = $"Pre-flight host search: {ex.Message}";
                return r;
            }

            if (host == null)
            {
                r.Skipped = true;
                r.Reason = $"Pre-flight: '{famName}' is {fpt} but no compatible host found within search radius near {Fmt(position)}. " +
                           "Either change the rule's CategoryFilter to a non-hosted family, or use Family Quick Edit → Change Host on an existing instance to rehost.";
                return r;
            }

            try
            {
                r.Placed = doc.Create.NewFamilyInstance(
                    position, symbol, host, room?.Level, StructuralType.NonStructural);
                if (r.Placed == null)
                {
                    r.Skipped = true;
                    r.Reason = $"Pre-flight: NewFamilyInstance({fpt}, host {host.GetType().Name} id {host.Id.Value}) returned null.";
                }
                return r;
            }
            catch (Exception ex)
            {
                r.Skipped = true;
                r.Reason = $"Pre-flight: NewFamilyInstance({fpt}, host {host.GetType().Name}) threw: {ex.Message}. " +
                           "The family's template is probably incompatible with the chosen host — swap families or use Family Quick Edit → Change Host.";
                return r;
            }
        }

        /// <summary>
        /// Phase 139.2 H — place a face-hosted family on the structural
        /// soffit above ceilingSoffitPoint. ceilingVoidDepthFt is the
        /// drop applied for second-fix devices (pendants, downlights);
        /// pass 0 to leave the family on the soffit (BESA boxes).
        /// </summary>
        public static PlacementHostPreflightResult PlaceOnCeilingSoffit(
            Document doc,
            FamilySymbol symbol,
            Room room,
            XYZ ceilingSoffitPoint,
            PlacementRule rule,
            double ceilingVoidDepthFt)
        {
            var r = new PlacementHostPreflightResult();
            if (doc == null || symbol == null || ceilingSoffitPoint == null)
            {
                r.Skipped = true;
                r.Reason  = "PlaceOnCeilingSoffit: null document / symbol / point.";
                return r;
            }

            try
            {
                XYZ rayStart = ceilingSoffitPoint;
                XYZ up = XYZ.BasisZ;
                Element soffitHost = null;

                var view3d = ResolveView3D(doc);
                if (view3d != null)
                {
                    try
                    {
                        var rayFilter = new ElementClassFilter(typeof(HostObject));
                        var rio = new ReferenceIntersector(rayFilter, FindReferenceTarget.Face, view3d) { FindReferencesInRevitLinks = false };
                        var hits = rio.Find(rayStart, up);
                        if (hits != null)
                        {
                            ReferenceWithContext best = null; double bestDist = double.MaxValue;
                            foreach (var rwc in hits)
                            {
                                if (rwc == null) continue;
                                double d = rwc.Proximity;
                                if (d > 0 && d < bestDist) { bestDist = d; best = rwc; }
                            }
                            if (best != null)
                            {
                                var refHit = best.GetReference();
                                soffitHost = doc.GetElement(refHit);
                            }
                        }
                    }
                    catch (Exception rex) { StingLog.Warn($"PlaceOnCeilingSoffit raycast: {rex.Message}"); }
                }
                if (soffitHost == null) soffitHost = NearestOf<Floor>(doc, ceilingSoffitPoint, 12.0);
                if (soffitHost == null)
                {
                    r.Skipped = true;
                    r.Reason  = $"PlaceOnCeilingSoffit: no soffit found above {Fmt(ceilingSoffitPoint)}.";
                    return r;
                }

                XYZ placeAt = ceilingSoffitPoint;
                if (ceilingVoidDepthFt > 0)
                    placeAt = new XYZ(ceilingSoffitPoint.X, ceilingSoffitPoint.Y, ceilingSoffitPoint.Z - ceilingVoidDepthFt);

                r.Placed = doc.Create.NewFamilyInstance(
                    placeAt, symbol, soffitHost, room?.Level, StructuralType.NonStructural);
                if (r.Placed == null)
                {
                    r.Skipped = true;
                    r.Reason  = "PlaceOnCeilingSoffit: NewFamilyInstance returned null.";
                }
                return r;
            }
            catch (Exception ex)
            {
                r.Skipped = true;
                r.Reason  = $"PlaceOnCeilingSoffit: {ex.Message}";
                return r;
            }
        }

        // Phase 139.23 — project a world XYZ onto a host's face plane so
        // NewFamilyInstance(faceRef, point, refDir, symbol) doesn't warn
        // "Instance origin does not lie on host face".
        private static XYZ ProjectOntoFace(Element host, Reference faceRef, XYZ worldPt)
        {
            try
            {
                if (host == null || faceRef == null || worldPt == null) return null;
                var geom = host.GetGeometryObjectFromReference(faceRef);
                if (!(geom is Face face)) return null;
                var proj = face.Project(worldPt);
                return proj?.XYZPoint;
            }
            catch (Exception ex) { StingLog.Warn($"ProjectOntoFace: {ex.Message}"); return null; }
        }

        // Phase 139.22 — face-based placement for WorkPlaneBased families.
        // Uses NewFamilyInstance(Reference, XYZ, XYZ, FamilySymbol) so the
        // family physically attaches to a wall's interior face (or the
        // ceiling's bottom face) instead of plonked at a free XYZ. Falls
        // back to the level-based overload when no wall/ceiling is
        // nearby (free-standing face-based families).
        private static PlacementHostPreflightResult TryFaceBasedPlace(
            Document doc, FamilySymbol symbol, Room room, XYZ position, PlacementRule rule)
        {
            var r = new PlacementHostPreflightResult();
            try
            {
                string anchor = (rule?.AnchorType ?? "").ToUpperInvariant();
                bool wallAnchor = anchor == "WALL_MIDPOINT" || anchor == "WALL_CORNER"
                               || anchor == "WALL_FACE_OFFSET"
                               || anchor.StartsWith("DOOR_")
                               || anchor.StartsWith("WINDOW_");
                bool ceilingAnchor = anchor == "CEILING_CENTRE"
                               || anchor == "CEILING_TILE_CENTRE"
                               || anchor == "LIGHTING_GRID"
                               || anchor == "LUX_GRID";

                if (wallAnchor)
                {
                    var wall = NearestOf<Wall>(doc, position, 6.0);
                    if (wall != null)
                    {
                        IList<Reference> faceRefs = null;
                        try { faceRefs = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Interior); }
                        catch { }
                        if (faceRefs != null && faceRefs.Count > 0)
                        {
                            var faceRef = faceRefs[0];
                            XYZ refDir = wall.Orientation ?? XYZ.BasisX;
                            // Rotate the wall normal 90° about Z to get a
                            // tangent that lies in the face plane.
                            refDir = new XYZ(-refDir.Y, refDir.X, 0);
                            if (refDir.IsZeroLength()) refDir = XYZ.BasisX;
                            else refDir = refDir.Normalize();
                            // Phase 139.23 — project the calculated position
                            // onto the face plane. Without this, Revit warns
                            // "Instance origin does not lie on host face" and
                            // strips the host association on regen.
                            XYZ placeAt = ProjectOntoFace(wall, faceRef, position) ?? position;
                            try
                            {
                                r.Placed = doc.Create.NewFamilyInstance(faceRef, placeAt, refDir, symbol);
                                if (r.Placed != null) return r;
                            }
                            catch (Exception fex)
                            {
                                StingLog.Warn($"TryFaceBasedPlace wall: {fex.Message}");
                            }
                        }
                    }
                }
                else if (ceilingAnchor)
                {
                    var ceiling = NearestOf<Ceiling>(doc, position, 12.0);
                    if (ceiling != null)
                    {
                        IList<Reference> faceRefs = null;
                        try { faceRefs = HostObjectUtils.GetBottomFaces(ceiling); }
                        catch { }
                        if (faceRefs != null && faceRefs.Count > 0)
                        {
                            var faceRef = faceRefs[0];
                            XYZ placeAt = ProjectOntoFace(ceiling, faceRef, position) ?? position;
                            try
                            {
                                r.Placed = doc.Create.NewFamilyInstance(faceRef, placeAt, XYZ.BasisX, symbol);
                                if (r.Placed != null) return r;
                            }
                            catch (Exception fex)
                            {
                                StingLog.Warn($"TryFaceBasedPlace ceiling: {fex.Message}");
                            }
                        }
                    }
                }

                // Fall-back: level-based plonk.
                // Phase 139.26 — WorkPlaneBased families can throw
                // "instances cannot be created from a face-based family
                //  without a face" on this overload. Catch it explicitly
                // so the engine logs "skipped" rather than letting an
                // uncaught exception poison the per-room loop.
                try
                {
                    r.Placed = doc.Create.NewFamilyInstance(
                        position, symbol, room?.Level, StructuralType.NonStructural);
                    if (r.Placed == null)
                    {
                        r.Skipped = true;
                        r.Reason = $"TryFaceBasedPlace fallback (level-based) returned null for '{symbol.Family?.Name}'. " +
                                   "WorkPlaneBased family with no host face nearby — load a wall-hosted variant or place a Ceiling near these rooms.";
                    }
                }
                catch (Exception fbEx)
                {
                    r.Skipped = true;
                    r.Reason = $"TryFaceBasedPlace fallback (level-based) threw for '{symbol.Family?.Name}': {fbEx.Message}. " +
                               "WorkPlaneBased family rejects level-based creation — load a wall-hosted variant or place a Ceiling near these rooms.";
                }
                return r;
            }
            catch (Exception ex)
            {
                r.Skipped = true;
                r.Reason = $"TryFaceBasedPlace: {ex.Message}";
                return r;
            }
        }

        private static T NearestOf<T>(Document doc, XYZ point, double maxDistFt) where T : Element
        {
            T best = null;
            double bestSq = maxDistFt * maxDistFt;
            try
            {
                var col = new FilteredElementCollector(doc)
                    .OfClass(typeof(T))
                    .WhereElementIsNotElementType();
                foreach (var el in col)
                {
                    if (el is not T candidate) continue;
                    BoundingBoxXYZ bb = candidate.get_BoundingBox(null);
                    if (bb == null) continue;
                    XYZ centre = (bb.Min + bb.Max) * 0.5;
                    double dx = centre.X - point.X;
                    double dy = centre.Y - point.Y;
                    double dz = centre.Z - point.Z;
                    double sq = dx * dx + dy * dy + dz * dz;
                    if (sq < bestSq)
                    {
                        best = candidate;
                        bestSq = sq;
                    }
                }
            }
            catch { }
            return best;
        }

        private static string Fmt(XYZ p) =>
            p == null ? "(null)" : $"({p.X:F2},{p.Y:F2},{p.Z:F2})";
    }
}
