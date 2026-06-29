// StingTools — Phase-2 Sustainability commands (Phase 195 scaffold; spec §12).
//
// Sustain_EpdAssign + Sustain_LeedScorecard are SCAFFOLDS behind a flag —
// stubs that compile, not full implementations. They switch on when the client/
// PM confirms LEED is contractual (LEED scheme is already present as data; only
// these LEED-specific commands + the EPD register are new in Phase 2).
//
// The flag is the LEED scheme's `phase2` field + a project_setup opt-in:
// having "LEED" in Schemes enables the scorecard preview; EpdAssign is always
// available to start populating SUS_EPD_REF_TXT early (additive, harmless).

using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Sustainability;
using StingTools.UI;

namespace StingTools.Commands.Sustainability
{
    internal static class SustainPhase2Flag
    {
        /// <summary>True when LEED Phase-2 features are enabled for the project
        /// (LEED selected in project setup). Until then the scorecard is a preview
        /// stub so the build stays green and the wiring is exercised.</summary>
        public static bool LeedEnabled(SustainProjectSetup setup)
            => setup?.Schemes?.Any(s => string.Equals(s, "LEED", StringComparison.OrdinalIgnoreCase)) == true;
    }

    // ── Sustain_EpdAssign (Phase 2 scaffold) ─────────────────────────────────
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SustainEpdAssignCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var doc = SustainCmdHelper.Doc(cmd);
            if (doc == null) { TaskDialog.Show("STING Sustainability", "No document open."); return Result.Failed; }

            // SCAFFOLD: the full implementation will pick a material/type and a Type III
            // EPD reference, then stamp SUS_EPD_REF_TXT so MaterialsRollup prefers the
            // product-specific factor. The param + read path already ship in Phase 1.
            TaskDialog.Show("STING Sustainability — EPD register",
                "You can start recording product-specific EPDs now: set the material's " +
                "EPD reference on a material in the Properties palette, and the materials " +
                "roll-up will prefer that product-specific factor over the generic library " +
                "value.\n\nThe full EPD register with a material picker becomes available " +
                "once LEED is confirmed for the project.");
            StingLog.Info("Sustain_EpdAssign: EPD register opened.");
            return Result.Succeeded;
        }
    }

    // ── Sustain_LeedScorecard (Phase 2 scaffold, flag-gated) ─────────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SustainLeedScorecardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var doc = SustainCmdHelper.Doc(cmd);
            if (doc == null) { TaskDialog.Show("STING Sustainability", "No document open."); return Result.Failed; }

            var setup = SustainCmdHelper.LoadSetup(doc);
            if (!SustainPhase2Flag.LeedEnabled(setup))
            {
                TaskDialog.Show("STING Sustainability — LEED scorecard",
                    "LEED is not selected in project setup. Add 'LEED' to the schemes in the " +
                    "SETUP tab to enable the LEED v5 scorecard (whole-building life-cycle " +
                    "assessment prerequisite report + materials credit scoring + bands).");
                return Result.Cancelled;
            }

            // SCAFFOLD: run the materials rollup + LEED scheme to preview the
            // pointSum band; the full WBLCA A1-A3 prerequisite report (three
            // hotspots narrative) is Phase-2 work.
            var res = SustainabilityEngine.Run(doc, setup);
            var leed = res.Schemes.FirstOrDefault(s => s.SchemeId == "LEED");

            var b = new StingResultPanel.Builder()
                .SetTitle("STING Sustainability — LEED v5 scorecard (preview)")
                .SetSubtitle("Whole-building life-cycle assessment prerequisite + Reduce-EC credit preview");
            if (leed != null)
            {
                b.AddSection("LEED v5 gates")
                 .Metric("Total points", leed.TotalPoints.ToString(), $"band {leed.Band}");
                foreach (var g in leed.Gates)
                    b.PassFail(g.Label, g.Passed, $"{g.IndicativeValue:F1} ({g.Points} pts)");
            }
            b.AddSection("Embodied carbon (whole-building LCA, A1–A3)")
             .Metric("kgCO2e/m²", $"{res.Materials?.CarbonIntensityKgM2:F1}", "indicative — full life-cycle report to follow");
            if (res.Materials?.Hotspots?.Count > 0)
                b.Table(new[] { "Hotspot", "kgCO2e", "%" },
                    res.Materials.Hotspots.Select(h => new[] { h.Material, $"{h.CarbonKg:F0}", $"{h.SharePct:F0}%" }).ToList());
            b.Show();

            StingLog.Info("Sustain_LeedScorecard: preview rendered.");
            return Result.Succeeded;
        }
    }
}
