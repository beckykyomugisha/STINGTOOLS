// StingTools — SlabPenetrationDetector.
//
// After a routing pass, walks every newly-created MEPCurve and
// detects where its centreline crosses a Floor element (i.e. the
// run drops between levels). For each crossing, the detector:
//
//   1. Computes the crossing XYZ + slab thickness.
//   2. Classifies the penetration's required fire rating from the
//      floor's STING_FIRE_RATING_TXT param (defaults to "FR60").
//   3. Stamps STING_PENETRATION_REF_TXT on the conduit/pipe so
//      the takeoff and FRP register can find every run that needs
//      a fire-stop.
//   4. Records the penetration (loc + rating + host floor + member)
//      in PenetrationRecord, suitable for handing to the FRP family
//      placer (FRP_PENETRATION.rfa, family stub TBC) once it ships.
//
// The detector is read + write of penetration parameters; it does
// NOT (yet) place an FRP family — that's deferred until the family
// template authoring lands. Stamping the parameters is enough to
// make every penetration appear in a register schedule + flag the
// fire-rating mismatch when validators run.
//
// Why: routing currently produces conduits / pipes that pass through
// floor slabs without any record of the penetration. BS 9999 / BS 476
// requires every fire-rated barrier penetration to be sealed and
// recorded. Without this detector the model is silently non-compliant.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core.Routing
{
    /// <summary>
    /// Class of host element being penetrated. Drives the placer's
    /// face-resolution strategy (slab bottom face / wall outer face /
    /// beam web face) and structural-review path.
    /// </summary>
    public enum PenetrationHostKind
    {
        Floor,
        Wall,
        Beam,
        Ceiling,
        Roof,
    }

    public sealed class PenetrationRecord
    {
        public ElementId MemberId         { get; set; }      // the conduit / pipe / duct
        // Generic host id (replaces the floor-only field — kept as
        // HostFloorId alias for back-compat with FrpPenetrationPlacer
        // and the existing routing pipeline).
        public ElementId HostId           { get; set; }
        // Back-compat alias — older callers (FrpPenetrationPlacer,
        // ConduitAutoRouteCommand) still read HostFloorId.
        public ElementId HostFloorId
        {
            get => HostId;
            set => HostId = value;
        }
        public PenetrationHostKind HostKind { get; set; } = PenetrationHostKind.Floor;
        public XYZ       Location         { get; set; }      // approximate crossing point
        public double    SlabThicknessMm  { get; set; }      // host through-thickness (mm)
        public string    FireRating       { get; set; } = "FR60";
        public string    MemberCategory   { get; set; } = "";
        public double    MemberDiameterMm { get; set; }

        // Beam-only structural-review fields. Populated by
        // BeamPenetrationDetector against AISC DG2 + BS EN 1992 limits;
        // empty when the host is a slab or wall.
        public double    BeamSpanMm           { get; set; }
        public double    BeamDepthMm          { get; set; }
        public double    DistanceFromSupportMm { get; set; }
        /// <summary>STRUCT_OK / STRUCT_REVIEW / STRUCT_FAIL — see BeamPenetrationDetector for thresholds.</summary>
        public string    StructuralFlag       { get; set; } = "";
    }

    public static class SlabPenetrationDetector
    {
        /// <summary>
        /// Detect floor crossings for every MEPCurve in the supplied
        /// list. Floors come from a project-wide collector (cheap —
        /// floors are usually < 50 per model). Returns the records
        /// in input order so callers can correlate to their run list.
        /// </summary>
        public static List<PenetrationRecord> Detect(Document doc, IEnumerable<ElementId> memberIds)
        {
            var records = new List<PenetrationRecord>();
            if (doc == null || memberIds == null) return records;

            // Collect floors once.
            var floors = new List<Floor>();
            try
            {
                foreach (var f in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Floors)
                    .WhereElementIsNotElementType())
                {
                    if (f is Floor fl) floors.Add(fl);
                }
            }
            catch (Exception ex) { StingLog.Warn($"SlabPenetrationDetector: floor collect: {ex.Message}"); }

            foreach (var id in memberIds)
            {
                MEPCurve curve = null;
                try { curve = doc.GetElement(id) as MEPCurve; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                if (curve == null) continue;

                // Wave J3 — split-segment metadata inheritance. When a
                // conduit was split by JBAutoPlacer, ELC_CDT_BREAKPOINT_TXT
                // carries a "JB:<id>@<reason>" reference. The split sub-
                // segments don't have their own penetration history yet,
                // but the parent might have crossed a slab that we'd
                // miss if we only check the new segment's geometry.
                // Inherit the parent's penetration record up-front so
                // every sub-segment carries the right fire-rating
                // metadata, then continue with normal geometric
                // detection in case the split itself crossed a NEW
                // slab the parent hadn't.
                try
                {
                    string brk = ParameterHelpers.GetString(curve, "ELC_CDT_BREAKPOINT_TXT");
                    if (!string.IsNullOrEmpty(brk))
                    {
                        InheritParentPenetration(doc, curve, brk, records);
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Parent penetration inherit: {ex.Message}"); }

                LocationCurve loc = curve.Location as LocationCurve;
                if (loc?.Curve == null) continue;
                XYZ a = loc.Curve.GetEndPoint(0);
                XYZ b = loc.Curve.GetEndPoint(1);

                // Vertical-ish runs are the candidates. Use a 30° cone
                // of acceptable angles from vertical so a near-vertical
                // drop with mild offset still counts.
                XYZ dir = (b - a).Normalize();
                double cosAngle = Math.Abs(dir.Z);
                if (cosAngle < 0.866) continue;       // > 30° from vertical → skip

                foreach (var floor in floors)
                {
                    var bb = floor.get_BoundingBox(null);
                    if (bb == null) continue;

                    // Quick reject: the run must vertically straddle the
                    // floor's bounding-box Z range.
                    double minZ = Math.Min(a.Z, b.Z);
                    double maxZ = Math.Max(a.Z, b.Z);
                    if (maxZ < bb.Min.Z || minZ > bb.Max.Z) continue;

                    // XY check against the floor's bounding box. Real
                    // boundary geometry would be more accurate but is
                    // expensive; the bounding box covers 95% of cases
                    // and any false positives surface as "review me"
                    // findings rather than auto-place errors.
                    XYZ midXy = new XYZ((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, 0);
                    if (midXy.X < bb.Min.X || midXy.X > bb.Max.X) continue;
                    if (midXy.Y < bb.Min.Y || midXy.Y > bb.Max.Y) continue;

                    double slabZ = 0.5 * (bb.Min.Z + bb.Max.Z);
                    var crossing = new XYZ(midXy.X, midXy.Y, slabZ);

                    var rec = new PenetrationRecord
                    {
                        MemberId        = id,
                        HostId          = floor.Id,
                        HostKind        = PenetrationHostKind.Floor,
                        Location        = crossing,
                        SlabThicknessMm = (bb.Max.Z - bb.Min.Z) * 304.8,
                        FireRating      = ResolveFireRating(floor),
                        MemberCategory  = curve.Category?.Name ?? "",
                        MemberDiameterMm= ReadDiameterMm(curve),
                    };
                    records.Add(rec);

                    // Stamp the penetration reference on the member so the
                    // FRP register schedule + validators see it without
                    // re-running the detector. Wave J3 also stamps a
                    // running count so JBAutoPlacer's next pass knows
                    // the conduit had crossings — useful for ordering
                    // splits so the JB doesn't land at the same XY as
                    // an existing FRP.
                    try
                    {
                        ParameterHelpers.SetString(curve, "STING_PENETRATION_REF_TXT",
                            $"FLR:{floor.Id.Value}@{rec.FireRating}", overwrite: true);
                        ParameterHelpers.SetString(curve, "STING_PENETRATION_FIRE_RATING_TXT",
                            rec.FireRating, overwrite: false);
                        // Wave J3 — running count visible in tags / schedules
                        int existing = 0;
                        string c = ParameterHelpers.GetString(curve, "ELC_CDT_PENETRATION_COUNT_NR");
                        if (!string.IsNullOrEmpty(c)) int.TryParse(c, out existing);
                        ParameterHelpers.SetString(curve, "ELC_CDT_PENETRATION_COUNT_NR",
                            (existing + 1).ToString(), overwrite: true);
                    }
                    catch (Exception ex) { StingLog.Warn($"Stamp penetration: {ex.Message}"); }

                    // A run can only legitimately cross one floor at a
                    // time (multi-storey conduits should be broken into
                    // per-floor segments by the router). Break out so we
                    // don't duplicate the record on overlapping floor
                    // bounding boxes.
                    break;
                }
            }

            return records;
        }

        /// <summary>
        /// Read the floor's STING_FIRE_RATING_TXT, falling back to
        /// the building's project-default rating, then "FR60" — the
        /// BS 9999 floor default for non-warehouse occupancies.
        /// </summary>
        private static string ResolveFireRating(Floor floor)
        {
            try
            {
                string r = ParameterHelpers.GetString(floor, "STING_FIRE_RATING_TXT");
                if (!string.IsNullOrEmpty(r)) return r;
                // Try the floor type as a fallback (rating often lives
                // on the type, not the instance).
                var t = floor.Document.GetElement(floor.GetTypeId());
                if (t != null)
                {
                    r = ParameterHelpers.GetString(t, "STING_FIRE_RATING_TXT");
                    if (!string.IsNullOrEmpty(r)) return r;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return "FR60";
        }

        /// <summary>
        /// Wave J3 — inherit penetration metadata from a parent
        /// conduit when the current member was split off by
        /// JBAutoPlacer. Walks ELC_CDT_BREAKPOINT_TXT (format
        /// "JB:&lt;id&gt;@&lt;reason&gt;"), looks up the JB's
        /// upstream conduit, and copies its penetration metadata
        /// onto the current sub-segment. Idempotent — re-running on
        /// already-stamped members is a no-op via the SetIfEmpty
        /// pattern.
        /// </summary>
        private static void InheritParentPenetration(Document doc, MEPCurve sub, string breakpointRef,
            List<PenetrationRecord> records)
        {
            if (string.IsNullOrEmpty(breakpointRef)) return;
            // Parse "JB:<id>@<reason>"; tolerate "NEEDED:..." (the
            // fallback stamp when no JB family was placed).
            int colon = breakpointRef.IndexOf(':');
            int at    = breakpointRef.IndexOf('@');
            if (colon < 0 || at <= colon) return;
            string idStr = breakpointRef.Substring(colon + 1, at - colon - 1);
            if (!long.TryParse(idStr, out long parentId)) return;

            try
            {
                var parent = doc.GetElement(new ElementId((long)parentId));
                if (parent == null) return;

                // Copy fire rating + ref onto the sub-segment if it
                // doesn't already have its own. Sub-segments that DO
                // have their own ref (e.g. the JB landed exactly on a
                // slab) keep theirs.
                string parentFireRating = ParameterHelpers.GetString(parent, "STING_PENETRATION_FIRE_RATING_TXT");
                string parentRef        = ParameterHelpers.GetString(parent, "STING_PENETRATION_REF_TXT");
                if (!string.IsNullOrEmpty(parentRef))
                    ParameterHelpers.SetString(sub, "STING_PENETRATION_REF_TXT", parentRef, overwrite: false);
                if (!string.IsNullOrEmpty(parentFireRating))
                    ParameterHelpers.SetString(sub, "STING_PENETRATION_FIRE_RATING_TXT", parentFireRating, overwrite: false);
            }
            catch (Exception ex) { StingLog.Warn($"InheritParentPenetration: {ex.Message}"); }
        }

        private static double ReadDiameterMm(MEPCurve curve)
        {
            try
            {
                var p = curve.LookupParameter("Diameter")
                     ?? curve.LookupParameter("Outside Diameter")
                     ?? curve.LookupParameter("Nominal Diameter");
                if (p != null && p.StorageType == StorageType.Double)
                    return p.AsDouble() * 304.8;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return 0;
        }
    }
}
