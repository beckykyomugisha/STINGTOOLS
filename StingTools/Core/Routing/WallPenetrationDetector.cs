// StingTools — WallPenetrationDetector.
//
// Companion to SlabPenetrationDetector. Walks every supplied MEPCurve
// and detects horizontal-ish runs that cross a fire-rated wall.
// Stamps STING_PENETRATION_REF_TXT on the member and yields a
// PenetrationRecord per crossing so FrpPenetrationPlacer can drop
// face-based fire-stops on the wall.
//
// Walls are the most common compartment-line penetration in real
// buildings (BS 9999 / Approved Document B compartmentation runs
// vertically). Without this detector the FRP register only covered
// floors and silently missed every horizontal pipe / duct / cable
// tray crossing a 60-min compartment wall.
//
// Why a separate detector rather than one polymorphic class? Floor
// and wall detection use different geometry strategies: floors are
// horizontal so a vertical-run + bbox-z-straddle is cheap; walls are
// vertical, so we need horizontal-run + curve-vs-wall-curve XY
// proximity. Splitting keeps each path tight.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core.Routing
{
    public static class WallPenetrationDetector
    {
        /// <summary>
        /// Detect wall crossings for every MEPCurve in the supplied
        /// list. Only fire-rated walls (STING_FIRE_RATING_TXT non-
        /// empty, or BuiltInParameter.FIRE_RATING non-empty on the
        /// type) are considered — non-rated partitions don't need a
        /// firestop and would flood the register with noise.
        /// </summary>
        public static List<PenetrationRecord> Detect(Document doc, IEnumerable<ElementId> memberIds)
        {
            var records = new List<PenetrationRecord>();
            if (doc == null || memberIds == null) return records;

            var walls = new List<Wall>();
            try
            {
                foreach (var w in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Walls)
                    .WhereElementIsNotElementType())
                {
                    if (!(w is Wall wall)) continue;
                    string rating = ResolveFireRating(wall);
                    if (string.IsNullOrEmpty(rating)) continue; // skip non-rated
                    walls.Add(wall);
                }
            }
            catch (Exception ex) { StingLog.Warn($"WallPenetrationDetector: wall collect: {ex.Message}"); }

            foreach (var id in memberIds)
            {
                MEPCurve curve = null;
                try { curve = doc.GetElement(id) as MEPCurve; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                if (curve == null) continue;

                LocationCurve loc = curve.Location as LocationCurve;
                if (loc?.Curve == null) continue;
                XYZ a = loc.Curve.GetEndPoint(0);
                XYZ b = loc.Curve.GetEndPoint(1);

                // Horizontal-ish runs are wall-crossing candidates.
                // 30° cone from horizontal — same tolerance shape the
                // slab detector uses, mirrored to the other axis.
                XYZ dir = (b - a).Normalize();
                double cosAngle = Math.Abs(dir.Z);
                if (cosAngle > 0.5) continue; // > 60° from horizontal → skip (treat as a slab drop)

                foreach (var wall in walls)
                {
                    var wallCurve = (wall.Location as LocationCurve)?.Curve;
                    if (wallCurve == null) continue;

                    // Cheap XY crossing test: do the two curve segments
                    // intersect when projected to XY? Use Curve.Intersect
                    // on flattened copies.
                    XYZ memA = new XYZ(a.X, a.Y, 0);
                    XYZ memB = new XYZ(b.X, b.Y, 0);
                    XYZ wlA  = new XYZ(wallCurve.GetEndPoint(0).X, wallCurve.GetEndPoint(0).Y, 0);
                    XYZ wlB  = new XYZ(wallCurve.GetEndPoint(1).X, wallCurve.GetEndPoint(1).Y, 0);
                    if (memA.DistanceTo(memB) < 1e-6 || wlA.DistanceTo(wlB) < 1e-6) continue;

                    XYZ crossXy = TrySegmentIntersect2D(memA, memB, wlA, wlB);
                    if (crossXy == null) continue;

                    // Z-band check — is the crossing within the wall's
                    // vertical extent? Use wall instance bbox.
                    var bb = wall.get_BoundingBox(null);
                    if (bb == null) continue;

                    // Use the run's Z at the crossing parameter.
                    double t = ProjectionParam(memA, memB, crossXy);
                    if (t < -0.001 || t > 1.001) continue;
                    double z = a.Z + t * (b.Z - a.Z);
                    if (z < bb.Min.Z - 0.05 || z > bb.Max.Z + 0.05) continue;

                    var crossing = new XYZ(crossXy.X, crossXy.Y, z);
                    double thicknessFt = 0;
                    try { thicknessFt = wall.Width; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); thicknessFt = 0; }

                    var rec = new PenetrationRecord
                    {
                        MemberId          = id,
                        HostId            = wall.Id,
                        HostKind          = PenetrationHostKind.Wall,
                        Location          = crossing,
                        SlabThicknessMm   = thicknessFt * 304.8,
                        FireRating        = ResolveFireRating(wall),
                        MemberCategory    = curve.Category?.Name ?? "",
                        MemberDiameterMm  = ReadDiameterMm(curve),
                    };
                    records.Add(rec);

                    try
                    {
                        ParameterHelpers.SetString(curve, "STING_PENETRATION_REF_TXT",
                            $"WAL:{wall.Id.Value}@{rec.FireRating}", overwrite: true);
                        ParameterHelpers.SetString(curve, "STING_PENETRATION_FIRE_RATING_TXT",
                            rec.FireRating, overwrite: false);
                    }
                    catch (Exception ex) { StingLog.Warn($"WallPenetrationDetector stamp: {ex.Message}"); }

                    // A run can cross multiple walls in the same pass;
                    // unlike the floor case, we keep iterating so every
                    // wall on the path is recorded.
                }
            }

            return records;
        }

        private static string ResolveFireRating(Wall wall)
        {
            try
            {
                string r = ParameterHelpers.GetString(wall, "STING_FIRE_RATING_TXT");
                if (!string.IsNullOrEmpty(r)) return r;
                var t = wall.Document.GetElement(wall.GetTypeId());
                if (t != null)
                {
                    r = ParameterHelpers.GetString(t, "STING_FIRE_RATING_TXT");
                    if (!string.IsNullOrEmpty(r)) return r;
                    var bip = t.get_Parameter(BuiltInParameter.FIRE_RATING);
                    if (bip != null && bip.StorageType == StorageType.String)
                    {
                        var s = bip.AsString();
                        if (!string.IsNullOrEmpty(s)) return s;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return "";
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
                var w = curve.LookupParameter("Width")?.AsDouble() ?? 0;
                var h = curve.LookupParameter("Height")?.AsDouble() ?? 0;
                if (w > 0 || h > 0) return Math.Max(w, h) * 304.8;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return 0;
        }

        // 2-D segment intersection. Returns the crossing point on
        // segment 1 if both segments intersect within their parameter
        // range [0,1]. Null otherwise.
        private static XYZ TrySegmentIntersect2D(XYZ p1, XYZ p2, XYZ q1, XYZ q2)
        {
            double r1x = p2.X - p1.X, r1y = p2.Y - p1.Y;
            double r2x = q2.X - q1.X, r2y = q2.Y - q1.Y;
            double denom = r1x * r2y - r1y * r2x;
            if (Math.Abs(denom) < 1e-9) return null;
            double t = ((q1.X - p1.X) * r2y - (q1.Y - p1.Y) * r2x) / denom;
            double u = ((q1.X - p1.X) * r1y - (q1.Y - p1.Y) * r1x) / denom;
            if (t < 0 || t > 1) return null;
            if (u < 0 || u > 1) return null;
            return new XYZ(p1.X + t * r1x, p1.Y + t * r1y, 0);
        }

        // Linear projection parameter of an XY point onto an XY segment.
        private static double ProjectionParam(XYZ a, XYZ b, XYZ p)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-12) return 0;
            return ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
        }
    }
}
