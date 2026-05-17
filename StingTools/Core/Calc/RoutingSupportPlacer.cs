// Phase 139.28 — RoutingSupportPlacer.
//
// Bridges the placement-side routers (WallFollowerRouter, DropEngineBase,
// InWallChaseRouter) to the existing HangerPlacementEngine + HangerFamilyResolver
// pipeline. The placement engine and resolver were authored for the
// stand-alone PlaceHangersCommand; this class makes them callable
// from the auto-routing path so a routing rule with EmitSupports=true
// produces standards-compliant clips / hangers as part of the same
// transaction that creates the route.
//
// Responsibilities:
//
//   1. Take the routing rule + the just-created MEP segment IDs.
//   2. Build a HangerSpacingQuery per run, overriding the engine's
//      auto-detection with the rule's NominalDiameterMm / Material /
//      InsulationThicknessMm fields when present (so the rule "wins"
//      over what was read off the family).
//   3. Call HangerPlacementEngine.Plan to get HangerCandidates with
//      anchor types, rod lengths and trapeze grouping.
//   4. Resolve hanger / clip families via HangerFamilyResolver and
//      auto-load STING_HANGER_GENERIC.rfa from Families/Hangers/ when
//      no candidate is loaded (Tier-4 fallback added by 139.28).
//   5. Create FamilyInstance per candidate and stamp the standard
//      provenance parameters (HOST_ID / ANCHOR / SPACING / etc.).
//
// Caller already owns the active Transaction. RoutingSupportPlacer
// never creates its own — failures are caught per candidate so a
// single bad family symbol doesn't tank the whole route.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace StingTools.Core.Calc
{
    public class RoutingSupportPlacementResult
    {
        public int SupportsPlaced { get; set; }
        public int CandidatesGenerated { get; set; }
        public int FamilyMissCount { get; set; }
        public List<ElementId> PlacedSupportIds { get; } = new List<ElementId>();
        public List<string> Warnings { get; } = new List<string>();
        public List<HangerCandidate> Candidates { get; } = new List<HangerCandidate>();
    }

    public static class RoutingSupportPlacer
    {
        /// <summary>
        /// Plan + place supports for the freshly-created routing
        /// segments. Caller owns the Transaction. When the rule has
        /// EmitSupports=false, returns an empty result with one
        /// informational warning.
        /// </summary>
        public static RoutingSupportPlacementResult PlaceForRoute(
            Document doc,
            StingTools.Core.Placement.PlacementRule rule,
            IList<ElementId> createdSegmentIds)
        {
            var r = new RoutingSupportPlacementResult();
            if (doc == null || rule == null || createdSegmentIds == null || createdSegmentIds.Count == 0) return r;
            if (!rule.EmitSupports)
            {
                r.Warnings.Add($"RoutingSupportPlacer: rule '{rule.MergeKey}' has EmitSupports=false — supports skipped.");
                return r;
            }

            // Build the run list, overriding the spacing query with the
            // rule's NominalDiameterMm / Material / InsulationThicknessMm
            // when present so rule data beats whatever the family carries.
            var runs = new List<Element>();
            foreach (var id in createdSegmentIds)
            {
                Element el = null;
                try { el = doc.GetElement(id); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                if (el == null) continue;
                runs.Add(el);
            }
            if (runs.Count == 0) return r;

            HangerPlacementResult planRes;
            try
            {
                // We'd like to let HangerPlacementEngine.Plan pick up the
                // rule overrides directly, but that engine reads off the
                // element. The cleanest hand-off without changing its
                // public surface: stamp the rule's overrides onto the
                // run elements as parameters before calling Plan, so
                // BuildQuery picks them up. PLM_PPE_MAT_TXT and
                // HVC_DCT_MAT_TXT are already the params it reads.
                if (!string.IsNullOrEmpty(rule.Material))
                {
                    foreach (var el in runs)
                    {
                        TrySetString(el, "PLM_PPE_MAT_TXT",   rule.Material);
                        TrySetString(el, "HVC_DCT_MAT_TXT",   rule.Material);
                    }
                }
                if (rule.InsulationThicknessMm > 0)
                {
                    foreach (var el in runs)
                    {
                        TrySetDoubleMm(el, "PLM_PPE_INSULATION_THK_MM", rule.InsulationThicknessMm);
                        TrySetDoubleMm(el, "HVC_DCT_INSULATION_THK_MM", rule.InsulationThicknessMm);
                    }
                }
                planRes = HangerPlacementEngine.Plan(doc, runs);
            }
            catch (Exception ex)
            {
                r.Warnings.Add($"RoutingSupportPlacer.Plan: {ex.Message}");
                return r;
            }

            r.CandidatesGenerated = planRes.CandidatesGenerated;
            r.Candidates.AddRange(planRes.Candidates);
            foreach (var w in planRes.Warnings) r.Warnings.Add(w);

            // Resolve families up-front so missing-family is one warning
            // rather than per-candidate noise.
            var binding = new Dictionary<string, HangerFamilyBinding>(StringComparer.OrdinalIgnoreCase);
            foreach (var anchor in new[] { "CONCRETE_ANCHOR", "BEAM_CLAMP", "TRAPEZE", "GENERIC" })
            {
                var b = HangerFamilyResolver.Resolve(doc, anchor);
                if (b?.Symbol != null) binding[anchor] = b;
            }
            if (binding.Count == 0)
            {
                r.FamilyMissCount = planRes.Candidates.Count;
                r.Warnings.Add(
                    "RoutingSupportPlacer: no hanger/clip family loaded in project. The engine planned " +
                    $"{planRes.Candidates.Count} support(s) per BS 5572 / MSS SP-58 / SMACNA but could not " +
                    "place them. Load STING_HANGER_GENERIC.rfa (or any Anvil/B-Line/Unistrut family with " +
                    "'hanger' in its name) and re-run, or run Place_Hangers manually after.");
                return r;
            }

            // Place one FamilyInstance per candidate.
            foreach (var c in planRes.Candidates)
            {
                if (c.Point == null) continue;
                if (!binding.TryGetValue(c.AnchorType ?? "GENERIC", out var b) || b?.Symbol == null)
                    binding.TryGetValue("GENERIC", out b);
                if (b?.Symbol == null) { r.FamilyMissCount++; continue; }
                try
                {
                    if (!b.Symbol.IsActive) b.Symbol.Activate();
                    var fi = doc.Create.NewFamilyInstance(c.Point, b.Symbol, StructuralType.NonStructural);
                    if (fi == null) { r.FamilyMissCount++; continue; }
                    StampSupportProvenance(fi, c, rule);
                    r.PlacedSupportIds.Add(fi.Id);
                    r.SupportsPlaced++;
                }
                catch (Exception ex2)
                {
                    r.Warnings.Add($"RoutingSupportPlacer NewFamilyInstance @ ({c.Point.X:F2},{c.Point.Y:F2}): {ex.Message}");
                }
            }

            if (r.SupportsPlaced > 0)
                StingLog.Info(
                    $"RoutingSupportPlacer: rule '{rule.MergeKey}' placed {r.SupportsPlaced} support(s) " +
                    $"across {runs.Count} run(s); {r.FamilyMissCount} missed for lack of family.");
            return r;
        }

        private static void StampSupportProvenance(
            FamilyInstance fi, HangerCandidate c, StingTools.Core.Placement.PlacementRule rule)
        {
            try
            {
                TrySetString(fi, "STING_HANGER_HOST_ID",      c.HostRun?.Value.ToString() ?? "");
                TrySetString(fi, "STING_HANGER_ANCHOR_TXT",   c.AnchorType ?? "GENERIC");
                TrySetDoubleRaw(fi, "STING_HANGER_STRUT_LEN_MM", c.StrutRodMm);
                TrySetDoubleRaw(fi, "STING_HANGER_SPACING_MM",   c.MaxSpanMm);
                TrySetInt   (fi, "STING_HANGER_TRAPEZE_BOOL", c.OnTrapeze ? 1 : 0);
                TrySetDoubleRaw(fi, "STING_HANGER_POINT_LOAD_KG", c.PointLoadKg);
                TrySetDoubleRaw(fi, "STING_HANGER_ROD_DIA_MM",   c.RodDiameterMm);
                TrySetString(fi, "STING_HANGER_ROD_IMPERIAL", c.RodImperial ?? "");
                TrySetInt   (fi, "STING_HANGER_COUPLER_BOOL", c.RodNeedsCoupler ? 1 : 0);
                TrySetString(fi, "STING_HANGER_BASIS_TXT",   c.SpacingBasis ?? "");
                TrySetString(fi, "STING_HANGER_RULE_ID_TXT", rule?.MergeKey ?? "");
                TrySetString(fi, "STING_HANGER_CONTEXT_TXT", rule?.MountingContext ?? "");
            }
            catch (Exception ex) { StingLog.Warn($"RoutingSupportPlacer.StampSupportProvenance: {ex.Message}"); }
        }

        private static void TrySetString(Element el, string param, string val)
        {
            try
            {
                var p = el.LookupParameter(param);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String) p.Set(val ?? "");
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }

        private static void TrySetDoubleRaw(Element el, string param, double val)
        {
            try
            {
                var p = el.LookupParameter(param);
                if (p == null || p.IsReadOnly) return;
                if (p.StorageType == StorageType.Double) p.Set(val);
                else if (p.StorageType == StorageType.String)
                    p.Set(val.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }

        // Length-as-mm setter — converts to internal feet for Length params.
        private static void TrySetDoubleMm(Element el, string param, double valMm)
        {
            try
            {
                var p = el.LookupParameter(param);
                if (p == null || p.IsReadOnly) return;
                if (p.StorageType == StorageType.Double) p.Set(valMm / 304.8);
                else if (p.StorageType == StorageType.String)
                    p.Set(valMm.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }

        private static void TrySetInt(Element el, string param, int val)
        {
            try
            {
                var p = el.LookupParameter(param);
                if (p == null || p.IsReadOnly) return;
                if (p.StorageType == StorageType.Integer) p.Set(val);
                else if (p.StorageType == StorageType.String) p.Set(val.ToString());
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }
    }
}
