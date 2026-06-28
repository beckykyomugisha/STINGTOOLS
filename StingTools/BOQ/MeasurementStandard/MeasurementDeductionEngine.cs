// ══════════════════════════════════════════════════════════════════════════
//  MeasurementDeductionEngine.cs — Phase 2A. The geometry side of NRM2/CESMM4
//  rules-based measurement: turns a gross modelled quantity into the net
//  measured quantity by deducting openings (walls) and voids (floors/roofs/
//  ceilings) over the rule's de-minimis threshold, and resolving the girth /
//  centre-line length for linear items.
//
//  Slice 1 (this commit): wall opening deductions only. Floors/voids land in
//  slice 2, linear girth in slice 3 — the dispatch is in place; the unhandled
//  branches return the gross quantity unchanged so nothing regresses.
//
//  Pure deduction maths — wastage is applied separately + visibly by the cost
//  manager so the gross→net derivation stays auditable. All reads; no
//  transaction. Every failure path returns the gross quantity (never crashes a
//  take-off).
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.BOQ.MeasurementStandard
{
    internal static class MeasurementDeductionEngine
    {
        // Per-document index of host element id → list of by-face/by-host
        // Opening face areas (m²). Built lazily on the first floor/roof/ceiling
        // query so we don't re-collect every Opening for every element (O(n²)),
        // and reset at the start of each BuildBOQDocument so opening edits are
        // picked up. Keyed by document PathName (host + each link are distinct).
        private static readonly ConcurrentDictionary<string, Dictionary<long, List<double>>> _voidIndex
            = new ConcurrentDictionary<string, Dictionary<long, List<double>>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Drop the per-document void index — called at the start of each
        /// BuildBOQDocument so a fresh take-off re-reads current openings.</summary>
        public static void ResetCaches() => _voidIndex.Clear();

        /// <summary>
        /// Returns the NET-of-deductions quantity for an element measured under
        /// <paramref name="rule"/>. <paramref name="thresholdOverrideM2"/> lets a
        /// standard pin its own de-minimis (e.g. CESMM4 Class U §3 = 0.5 m²);
        /// when null the rule/library default is used.
        /// </summary>
        public static double ApplyDeductions(Element el, string unit, double gross,
            MeasurementRule rule, MeasurementDefaults defaults, double? thresholdOverrideM2 = null)
        {
            if (el == null || rule == null) return gross;
            try
            {
                string nu = NormaliseUnit(unit);

                // ── Wall openings (doors / windows / wall openings) — area only.
                if (rule.DeductOpenings && nu == "m2" && el is Wall wall)
                {
                    double deMin = thresholdOverrideM2 ?? rule.ResolveOpeningDeMinimis(defaults);
                    double openings = WallOpeningAreaM2(wall, deMin);
                    return Math.Max(0, gross - openings);
                }

                // ── Floor / roof / ceiling voids — area only. The modelled area
                //    (HOST_AREA_COMPUTED) already excludes holes drawn in the
                //    element's sketch; by-face / by-host Opening elements (shafts
                //    landed on the host, vertical penetrations) are NOT in the
                //    sketch area, so we deduct those over the de-minimis here.
                if (rule.DeductVoids && nu == "m2")
                {
                    double deMin = thresholdOverrideM2 ?? rule.ResolveVoidDeMinimis(defaults);
                    double voids = HostVoidAreaM2(el, deMin);
                    return Math.Max(0, gross - voids);
                }

                // ── Linear girth / centre-line — slice 3 (return gross for now).
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("MeasDeduct", $"ApplyDeductions({rule?.Id}): {ex.Message}");
            }
            return gross;
        }

        // ── Wall openings ───────────────────────────────────────────────────

        /// <summary>
        /// Σ of door/window/wall-opening face areas (m²) hosted in the wall that
        /// exceed the de-minimis. Uses Wall.FindInserts for the host→insert
        /// relationship; each insert's area resolves from its width × height
        /// params, falling back to the two largest bounding-box extents.
        /// </summary>
        private static double WallOpeningAreaM2(Wall wall, double deMinimisM2)
        {
            double total = 0;
            Document doc = wall.Document;
            if (doc == null) return 0;

            ICollection<ElementId> inserts;
            try
            {
                // addRectOpenings, includeShadows, includeWallOpenings, includeBoundaryOpenings
                inserts = wall.FindInserts(true, false, true, true);
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("FindInserts", $"FindInserts({wall.Id}): {ex.Message}");
                return 0;
            }
            if (inserts == null) return 0;

            foreach (var id in inserts)
            {
                Element ins;
                try { ins = doc.GetElement(id); } catch { continue; }
                if (ins == null) continue;
                double a = OpeningAreaM2(ins);
                if (a > deMinimisM2) total += a;
            }
            return total;
        }

        // ── Floor / roof / ceiling voids ────────────────────────────────────

        /// <summary>
        /// Σ of by-face / by-host Opening face areas (m²) cutting this host that
        /// exceed the de-minimis. Uses the cached per-document host→openings
        /// index so we collect Opening elements once per take-off, not per host.
        /// Sketch-drawn holes are already excluded from HOST_AREA_COMPUTED, so
        /// only the separate Opening elements (Host == this) are deducted here.
        /// Shaft openings (Host == null) are not attributed to a single host and
        /// are left for a later slice.
        /// </summary>
        private static double HostVoidAreaM2(Element host, double deMinimisM2)
        {
            if (host?.Document == null || host.Id == null) return 0;
            try
            {
                var idx = VoidIndex(host.Document);
                if (!idx.TryGetValue(host.Id.Value, out var areas) || areas == null) return 0;
                double total = 0;
                foreach (var a in areas) if (a > deMinimisM2) total += a;
                return total;
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("HostVoid", $"HostVoidAreaM2({host?.Id}): {ex.Message}");
                return 0;
            }
        }

        private static Dictionary<long, List<double>> VoidIndex(Document doc)
        {
            string key = doc?.PathName ?? "default";
            return _voidIndex.GetOrAdd(key, _ => BuildVoidIndex(doc));
        }

        private static Dictionary<long, List<double>> BuildVoidIndex(Document doc)
        {
            var map = new Dictionary<long, List<double>>();
            if (doc == null) return map;
            try
            {
                foreach (var op in new FilteredElementCollector(doc).OfClass(typeof(Opening)).Cast<Opening>())
                {
                    Element h = null;
                    try { h = op.Host; } catch { }
                    if (h?.Id == null) continue;   // shaft / host-less openings — skip for now
                    double a = OpeningPlanAreaM2(op);
                    if (a <= 0) continue;
                    long hid = h.Id.Value;
                    if (!map.TryGetValue(hid, out var list)) { list = new List<double>(); map[hid] = list; }
                    list.Add(a);
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildVoidIndex: {ex.Message}"); }
            return map;
        }

        /// <summary>Plan (XY-projected) area of an Opening (m²).</summary>
        private static double OpeningPlanAreaM2(Opening op)
        {
            // Rectangular openings expose a 2-corner BoundaryRect.
            try
            {
                var rect = op.BoundaryRect;
                if (rect != null && rect.Count >= 2)
                {
                    double dx = Math.Abs(rect[1].X - rect[0].X);
                    double dy = Math.Abs(rect[1].Y - rect[0].Y);
                    if (dx > 0 && dy > 0) return dx * dy * 0.092903; // ft² → m²
                }
            }
            catch { }
            // Non-rectangular — shoelace over the boundary curve endpoints (XY).
            try
            {
                var curves = op.BoundaryCurves;
                if (curves != null && curves.Size > 0)
                {
                    double areaFt2 = PolygonPlanAreaFt2(curves);
                    if (areaFt2 > 0) return areaFt2 * 0.092903;
                }
            }
            catch { }
            return 0;
        }

        private static double PolygonPlanAreaFt2(CurveArray curves)
        {
            var pts = new List<XYZ>();
            foreach (Curve c in curves)
            {
                if (c == null) continue;
                try { pts.Add(c.GetEndPoint(0)); } catch { }
            }
            if (pts.Count < 3) return 0;
            double area2 = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                XYZ a = pts[i], b = pts[(i + 1) % pts.Count];
                area2 += a.X * b.Y - b.X * a.Y;
            }
            return Math.Abs(area2) / 2.0;
        }

        /// <summary>Face area (m²) of a single door / window / opening insert.</summary>
        private static double OpeningAreaM2(Element ins)
        {
            try
            {
                double w = DimFt(ins,
                    new[] { BuiltInParameter.DOOR_WIDTH, BuiltInParameter.FAMILY_WIDTH_PARAM,
                            BuiltInParameter.GENERIC_WIDTH, BuiltInParameter.FAMILY_ROUGH_WIDTH_PARAM },
                    new[] { "Width", "Rough Width" });
                double h = DimFt(ins,
                    new[] { BuiltInParameter.FAMILY_HEIGHT_PARAM, BuiltInParameter.GENERIC_HEIGHT,
                            BuiltInParameter.FAMILY_ROUGH_HEIGHT_PARAM },
                    new[] { "Height", "Rough Height" });
                if (w > 0 && h > 0) return w * h * 0.092903; // ft² → m²

                // Fallback: the two largest bounding-box extents approximate the
                // opening face (height × in-plane width).
                BoundingBoxXYZ bb = null;
                try { bb = ins.get_BoundingBox(null); } catch { }
                if (bb != null)
                {
                    double[] e =
                    {
                        Math.Abs(bb.Max.X - bb.Min.X),
                        Math.Abs(bb.Max.Y - bb.Min.Y),
                        Math.Abs(bb.Max.Z - bb.Min.Z)
                    };
                    Array.Sort(e);
                    return e[1] * e[2] * 0.092903; // two largest extents, ft² → m²
                }
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("OpeningArea", $"OpeningArea({ins?.Id}): {ex.Message}");
            }
            return 0;
        }

        /// <summary>
        /// Resolve a length dimension (internal feet) from a prioritised list of
        /// BuiltInParameters then named parameters, checking the instance then
        /// the family symbol. Returns 0 when nothing resolves.
        /// </summary>
        private static double DimFt(Element ins, BuiltInParameter[] bips, string[] names)
        {
            FamilyInstance fi = ins as FamilyInstance;
            FamilySymbol sym = fi?.Symbol;

            foreach (var bip in bips)
            {
                double v = ReadDouble(ins.get_Parameter(bip));
                if (v > 0) return v;
            }
            if (sym != null)
                foreach (var bip in bips)
                {
                    double v = ReadDouble(sym.get_Parameter(bip));
                    if (v > 0) return v;
                }
            if (names != null)
                foreach (var n in names)
                {
                    double v = ReadDouble(ins.LookupParameter(n));
                    if (v > 0) return v;
                    if (sym != null)
                    {
                        v = ReadDouble(sym.LookupParameter(n));
                        if (v > 0) return v;
                    }
                }
            return 0;
        }

        private static double ReadDouble(Parameter p)
        {
            if (p == null || !p.HasValue || p.StorageType != StorageType.Double) return 0;
            try { return p.AsDouble(); } catch { return 0; }
        }

        // Local unit normaliser (kept independent of BOQCostManager to avoid a
        // cross-namespace dependency from the measurement layer).
        private static string NormaliseUnit(string u)
        {
            if (string.IsNullOrEmpty(u)) return "";
            switch (u.Trim().ToLowerInvariant())
            {
                case "m²": case "sqm": case "m2": return "m2";
                case "m³": case "cum": case "m3": return "m3";
                case "lm": case "lin-m": case "linear-m": case "m": return "m";
                case "tonne": case "tonnes": case "t": case "kg": return "kg";
                case "no": case "nr": case "item": case "each": case "ea": return "each";
                default: return u.Trim().ToLowerInvariant();
            }
        }
    }
}
