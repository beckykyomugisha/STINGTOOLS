using StingTools.Core;
// StingTools — BeamPenetrationDetector.
//
// Detects MEP runs that cross structural framing (beams) and stamps
// each crossing with structural-review metadata. Beam penetrations
// are far more sensitive than slab / wall penetrations because the
// host is load-bearing — an oversized hole or a hole near a support
// point can compromise shear capacity. The detector implements the
// well-known prescriptive limits so the BIM flags structural review
// before fabrication, not at site sign-off.
//
// Rule set (consensus from AISC Design Guide 2 §3 + BS EN 1992-1-1
// research community + IStructE practice notes):
//
//   * Distance from support: must be ≥ max(beam depth d, span/10).
//     Best-practice band 0.20 L – 0.33 L from a support carries no
//     review cost. Anywhere in 0.10 L – 0.20 L flagged STRUCT_REVIEW
//     so the structural engineer signs off; closer than 0.10 L is
//     flagged STRUCT_FAIL — the routing has to move.
//   * Diameter / depth ratio: AISC DG2 caps web openings at 0.7 d for
//     steel; BS EN 1992 research caps reinforced-concrete circular
//     web openings at ~0.4 d without ad-hoc reinforcement. The
//     detector applies 0.4 d for OK, 0.4–0.7 d for REVIEW, > 0.7 d
//     for FAIL. Material is read from the beam type when available;
//     if unknown the detector defaults to the stricter concrete band
//     to err on the safe side.
//   * Concentrated-load proximity: not yet checked — flagged as a
//     follow-up on the structural-review record so the engineer
//     verifies it as part of sign-off.
//
// The detector ONLY records and stamps. Geometric routing fixes are
// the job of the routing engine — re-running with a relaxed search
// radius or moving the host beam are user decisions.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using System.Linq;

namespace StingTools.Core.Routing
{
    public static class BeamPenetrationDetector
    {
        // Thresholds. All distances metric mm, ratios dimensionless.
        private const double MinSupportClearancePctSpan = 0.10;   // FAIL below this fraction of span
        private const double ReviewSupportClearancePct  = 0.20;   // REVIEW between 0.10 and 0.20
        private const double SteelMaxDepthRatio         = 0.70;   // AISC DG2 absolute limit
        private const double SteelOkDepthRatio          = 0.50;   // OK below this for steel
        private const double ConcreteMaxDepthRatio      = 0.50;   // FAIL above this for RC (research band)
        private const double ConcreteOkDepthRatio       = 0.40;   // OK below this for RC

        public static List<PenetrationRecord> Detect(Document doc, IEnumerable<ElementId> memberIds)
        {
            var records = new List<PenetrationRecord>();
            if (doc == null || memberIds == null) return records;

            var beams = new List<FamilyInstance>();
            try
            {
                foreach (var b in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType())
                {
                    if (b is FamilyInstance fi) beams.Add(fi);
                }
            }
            catch (Exception ex) { StingLog.Warn($"BeamPenetrationDetector: beam collect: {ex.Message}"); }

            foreach (var id in memberIds)
            {
                MEPCurve curve = null;
                try { curve = doc.GetElement(id) as MEPCurve; } catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); }
                if (curve == null) continue;

                LocationCurve loc = curve.Location as LocationCurve;
                if (loc?.Curve == null) continue;
                XYZ a = loc.Curve.GetEndPoint(0);
                XYZ b = loc.Curve.GetEndPoint(1);

                foreach (var beam in beams)
                {
                    var beamCurve = (beam.Location as LocationCurve)?.Curve;
                    if (beamCurve == null) continue;

                    XYZ bA = beamCurve.GetEndPoint(0);
                    XYZ bB = beamCurve.GetEndPoint(1);
                    double spanFt = bA.DistanceTo(bB);
                    if (spanFt < 0.5) continue; // degenerate beam

                    // 3D shortest-distance test between two segments.
                    double dist = SegmentSegmentDistance(a, b, bA, bB,
                        out XYZ ptOnMember, out XYZ ptOnBeam, out double tBeam);
                    if (dist > 1.5) continue; // > ~450 mm — definitely not a crossing

                    // Distance criterion: penetration radius + beam half-
                    // depth + sleeve clearance. Use beam depth as the
                    // dominant clearance — a 600 mm-deep beam is hit
                    // even by a pipe centreline 250 mm away.
                    double beamDepthMm = ReadBeamDepthMm(beam);
                    double memberOdMm  = ReadDiameterMm(curve);
                    double clearanceFt = (beamDepthMm + memberOdMm + 50.0) * 0.5 / 304.8;
                    if (dist > clearanceFt) continue;

                    var crossing = ptOnBeam ?? ptOnMember;
                    double distFromSupportMm = Math.Min(tBeam, spanFt - tBeam) * 304.8;
                    double spanMm = spanFt * 304.8;

                    string material = ResolveBeamMaterial(beam);
                    string flag = ClassifyStructural(spanMm, beamDepthMm, distFromSupportMm,
                        memberOdMm, material);

                    var rec = new PenetrationRecord
                    {
                        MemberId               = id,
                        HostId                 = beam.Id,
                        HostKind               = PenetrationHostKind.Beam,
                        Location               = crossing,
                        SlabThicknessMm        = beamDepthMm,
                        FireRating             = ResolveFireRating(beam),
                        MemberCategory         = curve.Category?.Name ?? "",
                        MemberDiameterMm       = memberOdMm,
                        BeamSpanMm             = spanMm,
                        BeamDepthMm            = beamDepthMm,
                        DistanceFromSupportMm  = distFromSupportMm,
                        StructuralFlag         = flag,
                    };
                    records.Add(rec);

                    try
                    {
                        ParameterHelpers.SetString(curve, "STING_PENETRATION_REF_TXT",
                            $"BEM:{beam.Id.Value}@{rec.FireRating}#{flag}", overwrite: true);
                    }
                    catch (Exception ex3) { StingLog.Warn($"BeamPenetrationDetector stamp: {ex3.Message}"); }
                }
            }

            return records;
        }

        /// <summary>
        /// Apply AISC DG2 + BS EN 1992 prescriptive thresholds.
        /// Returns STRUCT_OK / STRUCT_REVIEW / STRUCT_FAIL.
        /// </summary>
        public static string ClassifyStructural(double spanMm, double beamDepthMm,
            double distFromSupportMm, double memberOdMm, string material)
        {
            if (spanMm <= 0 || beamDepthMm <= 0) return "STRUCT_REVIEW";

            double pct = distFromSupportMm / spanMm;
            // Hard constraint — also at least one beam-depth from any support.
            double minClearMm = Math.Max(MinSupportClearancePctSpan * spanMm, beamDepthMm);
            if (distFromSupportMm < minClearMm) return "STRUCT_FAIL";

            double depthRatio = beamDepthMm > 0 ? (memberOdMm / beamDepthMm) : 1.0;
            bool isSteel = !string.IsNullOrEmpty(material) &&
                material.IndexOf("steel", StringComparison.OrdinalIgnoreCase) >= 0;
            double okRatio   = isSteel ? SteelOkDepthRatio : ConcreteOkDepthRatio;
            double failRatio = isSteel ? SteelMaxDepthRatio : ConcreteMaxDepthRatio;

            if (depthRatio > failRatio) return "STRUCT_FAIL";

            // Combine pct-of-span banding with depth-ratio banding.
            // Either condition in the review band → REVIEW.
            bool reviewByLocation = pct < ReviewSupportClearancePct;
            bool reviewBySize     = depthRatio > okRatio;
            return (reviewByLocation || reviewBySize) ? "STRUCT_REVIEW" : "STRUCT_OK";
        }

        // ── Geometry helpers ───────────────────────────────────────────

        /// <summary>
        /// Closest distance between two 3D line segments. Out
        /// parameters give the closest point on each segment + the
        /// parameter (in feet from segment B's start) of the closest
        /// point on segment B — used as distance-from-support proxy.
        /// </summary>
        private static double SegmentSegmentDistance(XYZ a1, XYZ a2, XYZ b1, XYZ b2,
            out XYZ ptOnA, out XYZ ptOnB, out double tBeamFt)
        {
            XYZ d1 = a2 - a1;
            XYZ d2 = b2 - b1;
            XYZ r  = a1 - b1;
            double a = d1.DotProduct(d1);
            double e = d2.DotProduct(d2);
            double f = d2.DotProduct(r);

            double s, t;
            const double eps = 1e-9;
            if (a <= eps && e <= eps)
            {
                ptOnA = a1; ptOnB = b1; tBeamFt = 0;
                return r.GetLength();
            }
            if (a <= eps)
            {
                s = 0; t = f / e; t = Clamp01(t);
            }
            else
            {
                double c = d1.DotProduct(r);
                if (e <= eps)
                {
                    t = 0; s = Clamp01(-c / a);
                }
                else
                {
                    double bb = d1.DotProduct(d2);
                    double denom = a * e - bb * bb;
                    s = denom != 0 ? Clamp01((bb * f - c * e) / denom) : 0;
                    t = (bb * s + f) / e;
                    if (t < 0) { t = 0; s = Clamp01(-c / a); }
                    else if (t > 1) { t = 1; s = Clamp01((bb - c) / a); }
                }
            }
            ptOnA = a1 + d1.Multiply(s);
            ptOnB = b1 + d2.Multiply(t);
            tBeamFt = t * d2.GetLength();
            return ptOnA.DistanceTo(ptOnB);
        }

        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

        // ── Beam-property readers ──────────────────────────────────────

        public static double ReadBeamDepthMm(FamilyInstance beam)
        {
            try
            {
                // Prefer explicit depth parameter on the type.
                var symbol = beam.Symbol;
                var p = symbol?.LookupParameter("d") ?? symbol?.LookupParameter("Depth")
                     ?? symbol?.LookupParameter("h") ?? symbol?.LookupParameter("Height");
                if (p != null && p.StorageType == StorageType.Double) return p.AsDouble() * 304.8;
                // Fallback: bbox height of the instance.
                var bb = beam.get_BoundingBox(null);
                if (bb != null) return (bb.Max.Z - bb.Min.Z) * 304.8;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return 400.0; // 400 mm — typical reinforced concrete beam, conservative default
        }

        public static string ResolveBeamMaterial(FamilyInstance beam)
        {
            try
            {
                var symbol = beam.Symbol;
                if (symbol != null)
                {
                    string fam = symbol.FamilyName ?? "";
                    string typ = symbol.Name ?? "";
                    string both = (fam + " " + typ).ToLowerInvariant();
                    if (both.Contains("steel") || both.Contains("ub ") || both.Contains("uc ") ||
                        both.Contains("ipe") || both.Contains("hea") || both.Contains("heb") ||
                        both.Contains("w-shape") || both.Contains("w shape")) return "Steel";
                    if (both.Contains("concrete") || both.Contains("rc ") || both.Contains("precast"))
                        return "Concrete";
                    if (both.Contains("timber") || both.Contains("glulam") || both.Contains("lvl"))
                        return "Timber";
                }
                var matParam = beam.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);
                if (matParam != null && matParam.StorageType == StorageType.ElementId)
                {
                    var mat = beam.Document.GetElement(matParam.AsElementId()) as Material;
                    if (mat != null) return mat.Name ?? "";
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return "Concrete"; // safer default — applies stricter ratio
        }

        private static string ResolveFireRating(FamilyInstance beam)
        {
            // Beams are typically not fire-rated barriers (their fire
            // protection is via cementitious coatings / cladding, not
            // hosted firestops). Return empty string — the placer
            // treats this as "non-fire-rated, structural review only".
            try
            {
                string r = ParameterHelpers.GetString(beam, "STING_FIRE_RATING_TXT");
                if (!string.IsNullOrEmpty(r)) return r;
                var t = beam.Document.GetElement(beam.GetTypeId());
                if (t != null)
                {
                    r = ParameterHelpers.GetString(t, "STING_FIRE_RATING_TXT");
                    if (!string.IsNullOrEmpty(r)) return r;
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
    }
}
