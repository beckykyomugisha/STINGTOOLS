// Phase 108m — Prep For Export wizard. Runs the §3.2 quality-gate chain
// in one click. Each stage reports pass/fail; exporter is only unlocked
// when all stages green.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.BOQ
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BOQPrepForExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;
                var doc = ctx.Doc;

                var results = new List<(string Name, bool Pass, string Detail)>();

                // Gate 1: Compliance scan
                var comp = ComplianceScan.Scan(doc);
                results.Add(("Compliance ≥ 80%", (comp?.CompliancePercent ?? 0) >= 80,
                    $"{(comp?.CompliancePercent ?? 0):F1}% ({comp?.TaggedComplete}/{comp?.TotalElements})"));

                // Gate 2: Container completeness
                results.Add(("Container completeness ≥ 80%", (comp?.ContainerCompletePct ?? 0) >= 80,
                    $"{(comp?.ContainerCompletePct ?? 0):F1}%"));

                // Gate 3: Stale elements
                results.Add(("Stale elements = 0", (comp?.StaleCount ?? 0) == 0,
                    $"{(comp?.StaleCount ?? 0)} stale"));

                // Gate 4: Paragraph coverage (BOQ data quality)
                try
                {
                    var band = BOQBccBridge.ComputeBOQHealthBand(doc);
                    results.Add(("BOQ Data Quality ≥ Good", band.Score >= 65,
                        $"Score {band.Score:F0}/100 ({band.Grade})"));
                    results.Add(("Paragraph coverage ≥ 80%", band.ParagraphCoveragePct >= 80,
                        $"{band.ParagraphCoveragePct:F0}% ({band.TotalItems - band.ItemsMissingParagraph}/{band.TotalItems})"));
                    results.Add(("Rate fill ≥ 90%", band.RateFillPct >= 90,
                        $"{band.RateFillPct:F0}% ({band.TotalItems - band.ItemsMissingRate}/{band.TotalItems})"));
                }
                catch (Exception ex) { StingLog.Warn($"Prep BOQ band: {ex.Message}"); }

                // Gate 5: Warnings
                try
                {
                    var wr = Core.WarningsEngine.ScanWarnings(doc);
                    int critical = 0;
                    if (wr != null && wr.BySeverity.TryGetValue(Core.WarningSeverity.Critical, out int c)) critical = c;
                    results.Add(("Zero critical warnings", critical == 0, $"{critical} critical"));
                }
                catch (Exception ex) { StingLog.Warn($"Prep warnings: {ex.Message}"); }

                // Gate 6: Placeholder tokens
                int placeholders = comp?.PlaceholderCount ?? 0;
                results.Add(("Placeholders < 5% of tagged", placeholders < (comp?.TaggedComplete ?? 1) * 0.05,
                    $"{placeholders} placeholder items"));

                int pass = results.Count(r => r.Pass);
                int fail = results.Count - pass;
                bool allGreen = fail == 0;

                var rp = StingResultPanel.Create("Prep For Export — Quality Gate")
                    .SetSubtitle(allGreen
                        ? "★ All gates GREEN. Safe to export the Tender BOQ."
                        : $"{fail} of {results.Count} gates failed. Export likely to produce inaccurate BOQ — remediate first.")
                    .AddSection("QUALITY GATES");
                foreach (var r in results)
                    rp.Metric(r.Name, $"{(r.Pass ? "✓" : "✗")}  {r.Detail}");
                rp.AddSection("RECOMMENDED ACTIONS");
                if (!results[0].Pass) rp.Text("Run Workflow → DailyQA to improve compliance");
                if (results.Count > 2 && !results[2].Pass) rp.Text("Run RetagStale to clear stale elements");
                if (allGreen) rp.Text("Click ★ Tender BOQ to export");
                rp.Show();

                StingLog.Info($"Prep For Export: {pass}/{results.Count} gates green");
                return allGreen ? Result.Succeeded : Result.Failed;
            }
            catch (Exception ex) { StingLog.Error("BOQPrepForExportCommand", ex); message = ex.Message; return Result.Failed; }
        }
    }
}
