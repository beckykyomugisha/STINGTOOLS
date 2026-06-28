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
using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.BOQ.MeasurementStandard
{
    internal static class MeasurementDeductionEngine
    {
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

                // ── Floor / roof / ceiling voids — slice 2 (return gross for now).
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
