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

                // Gate 7: A-1 — measured lines with no resolvable quantity.
                // These are the most dangerous rows a bill can carry: they have a
                // description, a classification, a rate and an NRM2 section, and a
                // quantity of zero — so they read as a genuine cheap item and price
                // into the Contract Sum at nil. Hard gate, zero tolerance. The
                // uncosted rollup already knew about them (BlocksExport) but nothing
                // on this pre-flight consulted it.
                int unresolvedQty = -1;
                try
                {
                    var boqDoc = BOQCostManager.BuildBOQDocument(doc);
                    double floor = TagConfig.GetConfigDouble("COST_MIN_RATE_CONFIDENCE_EXPORT", 60.0);
                    var risk = BOQCostManager.ComputeUncostedRollup(boqDoc, floor);
                    unresolvedQty = risk.QuantityUnresolvedCount;
                    results.Add(("Measured lines with no resolvable quantity = 0", unresolvedQty == 0,
                        unresolvedQty == 0
                            ? "0 — every measured line has a quantity"
                            : $"{unresolvedQty} measured line(s) have no resolvable quantity"));
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Prep unresolved-quantity gate: {ex.Message}");
                    // Fail closed: an un-runnable gate is not a passed gate.
                    results.Add(("Measured lines with no resolvable quantity = 0", false,
                        $"could not evaluate — {ex.Message}"));
                }

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
                if (unresolvedQty > 0)
                    rp.Text($"Run Validate → Cost (COST.QTY.UNRESOLVED) to list the {unresolvedQty} line(s); " +
                            "each is a take-off rule whose quantity source is missing or unbound. " +
                            "They would bill at zero.");
                if (allGreen) rp.Text("Click ★ Tender BOQ to export");
                rp.Show();

                StingLog.Info($"Prep For Export: {pass}/{results.Count} gates green");
                return allGreen ? Result.Succeeded : Result.Failed;
            }
            catch (Exception ex) { StingLog.Error("BOQPrepForExportCommand", ex); message = ex.Message; return Result.Failed; }
        }
    }
}
