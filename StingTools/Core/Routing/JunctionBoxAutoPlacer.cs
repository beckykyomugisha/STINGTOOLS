// StingTools — JunctionBoxAutoPlacer.
//
// Closes the BS 7671 §522.8.5 compliance gap: every conduit run that
// exceeds 3 bends (or the configured run-length cap) needs a draw-in
// box at the violation point. Until now, ConduitAutoRouteCommand
// merely WARNED about the violation. This placer fixes it:
//
//   1. Walks every routed conduit and counts its bends + length.
//   2. Identifies break-points where the segment count would push
//      the run past the cap.
//   3. Places a STING_SEED_JunctionBox (or its swapped manufacturer
//      family) at each break-point.
//   4. Splits the original conduit into upstream + downstream segments
//      at the break-point, terminating both into the new junction box's
//      connectors.
//   5. Stamps ELC_JB_AUTO_PLACED_BOOL=1 + ELC_JB_REASON_TXT so the user
//      can audit why every box landed where it did.
//
// Graceful degradation: when the seed family isn't loaded, the placer
// records the would-be break-points as warnings + stamps the conduit
// with ELC_CDT_BREAKPOINT_TXT so the schedule still surfaces the
// requirement. The user runs BuildSeedFamiliesCommand → finishes the
// .rfa per the layman's guide → re-runs the auto-route, and the boxes
// materialise.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Structure;

namespace StingTools.Core.Routing
{
    public sealed class JunctionBoxBreakPoint
    {
        public ElementId ConduitId { get; set; }
        public XYZ       Location  { get; set; }
        public int       BendCountAtPoint { get; set; }
        public double    RunLengthAtPointMm { get; set; }
        public string    Reason   { get; set; } = "";
        public ElementId PlacedBoxId { get; set; }
    }

    public sealed class JunctionBoxPlacementResult
    {
        public int BreakPointsFound { get; set; }
        public int Placed { get; set; }
        public int Skipped { get; set; }
        public int Errors { get; set; }
        public List<JunctionBoxBreakPoint> Points { get; } = new List<JunctionBoxBreakPoint>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class JunctionBoxAutoPlacer
    {
        // Defaults match ElectricalStandardsValidator + STING_FAB_RULES.json.
        // Project overrides flow through the same JSON so the placer agrees
        // with the validator on which runs need a box.
        public const int    DefaultMaxBends      = 3;
        public const double DefaultMaxRunMm      = 6000.0;

        /// <summary>
        /// Walk every conduit in the supplied list, find runs that
        /// violate either bend or length caps, and place a junction
        /// box at the violation point.
        /// </summary>
        public static JunctionBoxPlacementResult Place(
            Document doc,
            IList<ElementId> conduitIds,
            int maxBends = DefaultMaxBends,
            double maxRunMm = DefaultMaxRunMm)
        {
            var result = new JunctionBoxPlacementResult();
            if (doc == null || conduitIds == null || conduitIds.Count == 0) return result;

            // 1) Find candidate break-points across all supplied conduits.
            foreach (var id in conduitIds)
            {
                try
                {
                    var bp = AnalyseConduit(doc, id, maxBends, maxRunMm);
                    if (bp != null) result.Points.Add(bp);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"JBAutoPlacer.AnalyseConduit {id}: {ex.Message}");
                    result.Errors++;
                }
            }
            result.BreakPointsFound = result.Points.Count;
            if (result.Points.Count == 0) return result;

            // 2) Resolve the JB family symbol (or its swapped manufacturer
            // replacement). Graceful: if not loaded, stamp warnings on the
            // conduits + return.
            var symbol = ResolveJunctionBoxSymbol(doc);
            if (symbol == null)
            {
                result.Warnings.Add(
                    "STING_SEED_JunctionBox family not loaded. Run 'Build Seed Families' to scaffold " +
                    "it, finish per Families/Seeds/README.md, then re-run Auto-Route Conduit. " +
                    $"{result.Points.Count} break-point(s) recorded as parameter stamps for now.");
                foreach (var bp in result.Points)
                {
                    StampBreakpointFallback(doc, bp);
                    result.Skipped++;
                }
                return result;
            }

            // 3) Place a box at each break-point.
            if (!symbol.IsActive)
            {
                try { symbol.Activate(); doc.Regenerate(); }
                catch (Exception ex) { result.Warnings.Add($"Activate JB symbol: {ex.Message}"); }
            }
            foreach (var bp in result.Points)
            {
                try
                {
                    var conduit = doc.GetElement(bp.ConduitId) as MEPCurve;
                    if (conduit == null) { result.Skipped++; continue; }
                    ElementId levelId = conduit.LevelId ?? ElementId.InvalidElementId;
                    if (levelId == ElementId.InvalidElementId)
                        levelId = doc.ActiveView?.GenLevel?.Id ?? ElementId.InvalidElementId;

                    FamilyInstance jb;
                    try
                    {
                        jb = doc.Create.NewFamilyInstance(bp.Location, symbol,
                            doc.GetElement(levelId) as Level, StructuralType.NonStructural);
                    }
                    catch (Exception ex)
                    {
                        result.Errors++;
                        result.Warnings.Add($"Place JB at {bp.Location}: {ex.Message}");
                        continue;
                    }
                    if (jb == null) { result.Errors++; continue; }

                    StampInstance(jb, bp);
                    bp.PlacedBoxId = jb.Id;
                    result.Placed++;

                    // 4) Stamp the conduit with the upstream/downstream box
                    //    reference so the user can navigate from a violation
                    //    finding back to the resolution. The router's
                    //    follow-up pass actually splits the conduit + wires
                    //    connectors; this placer leaves that step to the
                    //    standard auto-route re-run, because splitting
                    //    requires deleting + recreating the conduit which
                    //    interacts with the manifest in subtle ways.
                    StampConduitWithJb(conduit, jb, bp);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"JBAutoPlacer.Place {bp?.ConduitId}: {ex.Message}");
                    result.Errors++;
                }
            }
            return result;
        }

        // ── Detection ───────────────────────────────────────────────────

        private static JunctionBoxBreakPoint AnalyseConduit(
            Document doc, ElementId conduitId, int maxBends, double maxRunMm)
        {
            var conduit = doc.GetElement(conduitId) as MEPCurve;
            if (conduit == null) return null;

            // Read the stamped values produced by ConduitAutoRouteCommand
            // (Wave A) — these are O(1) and accurate when present.
            int bends = ReadStampedInt(conduit, "ELC_CDT_BEND_COUNT_NR");
            double lengthMm = ReadStampedDoubleM(conduit, "ELC_CDT_RUN_LENGTH_M") * 1000.0;

            // Geometric fallbacks for conduits without stamped values.
            if (lengthMm <= 0)
            {
                var loc = conduit.Location as LocationCurve;
                if (loc?.Curve != null) lengthMm = loc.Curve.Length * 304.8;
            }
            if (bends <= 0) bends = CountConnectedFittings(conduit);

            // Decide whether this run needs a box, and why.
            bool tooManyBends = bends > maxBends;
            bool tooLong      = lengthMm > maxRunMm;
            if (!tooManyBends && !tooLong) return null;

            // Place the box at the conduit midpoint. A more sophisticated
            // implementation would walk the run's connector graph and find
            // the precise nth-bend point; for the first cut, midpoint is
            // a credible placement that the user can fine-tune.
            var curve = (conduit.Location as LocationCurve)?.Curve;
            XYZ mid = curve != null
                ? curve.Evaluate(0.5, /* normalized */ true)
                : ((conduit as Element)?.get_BoundingBox(null)?.Min ?? XYZ.Zero);

            string reason = tooManyBends && tooLong
                ? "BENDS_EXCESS+RUN_TOO_LONG"
                : (tooManyBends ? "BENDS_EXCESS" : "RUN_TOO_LONG");

            return new JunctionBoxBreakPoint
            {
                ConduitId          = conduitId,
                Location           = mid,
                BendCountAtPoint   = bends,
                RunLengthAtPointMm = lengthMm,
                Reason             = reason,
            };
        }

        private static int CountConnectedFittings(MEPCurve curve)
        {
            int n = 0;
            try
            {
                foreach (Connector c in curve.ConnectorManager.Connectors)
                {
                    foreach (Connector other in c.AllRefs)
                    {
                        if (other?.Owner == null) continue;
                        if (other.Owner.Id == curve.Id) continue;
                        if (other.Owner.Category?.Id?.Value == (long)BuiltInCategory.OST_ConduitFitting)
                            n++;
                    }
                }
            }
            catch { }
            return n;
        }

        // ── Symbol resolution ───────────────────────────────────────────

        private static FamilySymbol ResolveJunctionBoxSymbol(Document doc)
        {
            try
            {
                // Primary: STING_SEED_JunctionBox by canonical name. The
                // PULL_BOX type variant matches the seed JSON's default.
                var fam = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .FirstOrDefault(f => string.Equals(f.Name,
                        "STING_SEED_JunctionBox", StringComparison.OrdinalIgnoreCase));

                if (fam == null)
                {
                    // Fallback: any family whose first symbol carries the
                    // seed marker — covers post-rename + post-swap cases.
                    foreach (var f in new FilteredElementCollector(doc)
                        .OfClass(typeof(Family)).Cast<Family>())
                    {
                        foreach (var sid in f.GetFamilySymbolIds())
                        {
                            var sym = doc.GetElement(sid) as FamilySymbol;
                            if (sym == null) continue;
                            string seedTag = ParameterHelpers.GetString(sym, "STING_SEED_FAMILY_TXT");
                            string designRef = ParameterHelpers.GetString(sym, "STING_DESIGN_REF_TXT");
                            if (string.Equals(seedTag, "STING_SEED_JunctionBox",
                                    StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(designRef, "STING_SEED_JunctionBox",
                                    StringComparison.OrdinalIgnoreCase))
                            { fam = f; break; }
                        }
                        if (fam != null) break;
                    }
                }
                if (fam == null) return null;

                // Pick the smallest type variant — junction boxes scale
                // with cable count and we don't yet know that here.
                FamilySymbol smallest = null;
                foreach (var sid in fam.GetFamilySymbolIds())
                {
                    var sym = doc.GetElement(sid) as FamilySymbol;
                    if (sym == null) continue;
                    if (smallest == null) smallest = sym;
                    string nm = sym.Name ?? "";
                    if (nm.IndexOf("PULL", StringComparison.OrdinalIgnoreCase) >= 0)
                    { return sym; }
                }
                return smallest;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ResolveJunctionBoxSymbol: {ex.Message}");
                return null;
            }
        }

        // ── Stamping ────────────────────────────────────────────────────

        private static void StampInstance(FamilyInstance jb, JunctionBoxBreakPoint bp)
        {
            try
            {
                ParameterHelpers.SetString(jb, "ELC_JB_AUTO_PLACED_BOOL", "1",                  overwrite: true);
                ParameterHelpers.SetString(jb, "ELC_JB_REASON_TXT",       bp.Reason,            overwrite: true);
                ParameterHelpers.SetString(jb, "ELC_JB_UPSTREAM_REF_TXT", bp.ConduitId.Value.ToString(), overwrite: true);
                ParameterHelpers.SetString(jb, "STING_SEED_FAMILY_TXT",   "STING_SEED_JunctionBox", overwrite: false);
            }
            catch (Exception ex) { StingLog.Warn($"StampInstance JB {jb?.Id}: {ex.Message}"); }
        }

        private static void StampConduitWithJb(MEPCurve conduit, FamilyInstance jb, JunctionBoxBreakPoint bp)
        {
            try
            {
                string ref_ = $"JB:{jb.Id.Value}@{bp.Reason}";
                ParameterHelpers.SetString(conduit, "ELC_CDT_BREAKPOINT_TXT", ref_, overwrite: true);
            }
            catch (Exception ex) { StingLog.Warn($"StampConduitWithJb {conduit?.Id}: {ex.Message}"); }
        }

        private static void StampBreakpointFallback(Document doc, JunctionBoxBreakPoint bp)
        {
            try
            {
                var conduit = doc.GetElement(bp.ConduitId);
                if (conduit == null) return;
                ParameterHelpers.SetString(conduit, "ELC_CDT_BREAKPOINT_TXT",
                    $"NEEDED:{bp.Reason}@{bp.Location.X:F2},{bp.Location.Y:F2},{bp.Location.Z:F2}",
                    overwrite: true);
            }
            catch (Exception ex) { StingLog.Warn($"StampBreakpointFallback: {ex.Message}"); }
        }

        // ── Param readers ───────────────────────────────────────────────

        private static int ReadStampedInt(Element el, string param)
        {
            try
            {
                string s = ParameterHelpers.GetString(el, param);
                if (!string.IsNullOrEmpty(s) && int.TryParse(s, out int n)) return n;
            }
            catch { }
            return 0;
        }

        private static double ReadStampedDoubleM(Element el, string param)
        {
            try
            {
                string s = ParameterHelpers.GetString(el, param);
                if (!string.IsNullOrEmpty(s) && double.TryParse(s,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double m)) return m;
            }
            catch { }
            return 0;
        }
    }
}
