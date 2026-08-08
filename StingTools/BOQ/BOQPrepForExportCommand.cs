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

                // A-3 — Confirmable marks a gate that reports a RISK rather than a defect:
                // something a human must look at and sign off, not something the tool can
                // call wrong on its own. A confirmable failure does not block the export;
                // it forces an explicit acknowledgement.
                var results = new List<(string Name, bool Pass, string Detail, bool Confirmable)>();

                // Gate 1: Compliance scan
                var comp = ComplianceScan.Scan(doc);
                results.Add(("Compliance ≥ 80%", (comp?.CompliancePercent ?? 0) >= 80,
                    $"{(comp?.CompliancePercent ?? 0):F1}% ({comp?.TaggedComplete}/{comp?.TotalElements})", false));

                // Gate 2: Container completeness
                results.Add(("Container completeness ≥ 80%", (comp?.ContainerCompletePct ?? 0) >= 80,
                    $"{(comp?.ContainerCompletePct ?? 0):F1}%", false));

                // Gate 3: Stale elements
                results.Add(("Stale elements = 0", (comp?.StaleCount ?? 0) == 0,
                    $"{(comp?.StaleCount ?? 0)} stale", false));

                // Gate 4: Paragraph coverage (BOQ data quality)
                try
                {
                    var band = BOQBccBridge.ComputeBOQHealthBand(doc);
                    results.Add(("BOQ Data Quality ≥ Good", band.Score >= 65,
                        $"Score {band.Score:F0}/100 ({band.Grade})", false));
                    results.Add(("Paragraph coverage ≥ 80%", band.ParagraphCoveragePct >= 80,
                        $"{band.ParagraphCoveragePct:F0}% ({band.TotalItems - band.ItemsMissingParagraph}/{band.TotalItems})", false));
                    results.Add(("Rate fill ≥ 90%", band.RateFillPct >= 90,
                        $"{band.RateFillPct:F0}% ({band.TotalItems - band.ItemsMissingRate}/{band.TotalItems})", false));
                }
                catch (Exception ex) { StingLog.Warn($"Prep BOQ band: {ex.Message}"); }

                // Gate 5: Warnings
                try
                {
                    var wr = Core.WarningsEngine.ScanWarnings(doc);
                    int critical = 0;
                    if (wr != null && wr.BySeverity.TryGetValue(Core.WarningSeverity.Critical, out int c)) critical = c;
                    results.Add(("Zero critical warnings", critical == 0, $"{critical} critical", false));
                }
                catch (Exception ex) { StingLog.Warn($"Prep warnings: {ex.Message}"); }

                // Gate 6: Placeholder tokens
                int placeholders = comp?.PlaceholderCount ?? 0;
                results.Add(("Placeholders < 5% of tagged", placeholders < (comp?.TaggedComplete ?? 1) * 0.05,
                    $"{placeholders} placeholder items", false));

                // Gate 7: A-1 — measured lines with no resolvable quantity.
                // These are the most dangerous rows a bill can carry: they have a
                // description, a classification, a rate and an NRM2 section, and a
                // quantity of zero — so they read as a genuine cheap item and price
                // into the Contract Sum at nil. Hard gate, zero tolerance. The
                // uncosted rollup already knew about them (BlocksExport) but nothing
                // on this pre-flight consulted it.
                int unresolvedQty = -1;
                var underCounts = new List<LinkUnderCount>();
                try
                {
                    var boqDoc = BOQCostManager.BuildBOQDocument(doc);
                    double floor = TagConfig.GetConfigDouble("COST_MIN_RATE_CONFIDENCE_EXPORT", 60.0);
                    var risk = BOQCostManager.ComputeUncostedRollup(boqDoc, floor);
                    unresolvedQty = risk.QuantityUnresolvedCount;
                    results.Add(("Measured lines with no resolvable quantity = 0", unresolvedQty == 0,
                        unresolvedQty == 0
                            ? "0 — every measured line has a quantity"
                            : $"{unresolvedQty} measured line(s) have no resolvable quantity", false));

                    // Gate 8: A-3 — links placed N>1 but taken off ×1. CONFIRMABLE, not
                    // a hard block: placing one shared reference model twice is normal
                    // and correct. The plugin cannot tell that from seven cottages, so
                    // it names the link, the count and the money and makes a human say.
                    underCounts = boqDoc.LinkUnderCounts ?? new List<LinkUnderCount>();
                    if (underCounts.Count == 0)
                        results.Add(("Linked models taken off at their placed count", true,
                            "no included link is placed more than once", false));
                    else
                    {
                        string names = string.Join(", ",
                            underCounts.Select(u => $"'{u.LinkName}' ×{u.InstanceCount}"));
                        double shortfall = underCounts.Sum(u => u.ShortfallUGX);
                        results.Add(("Linked models taken off at their placed count", false,
                            $"{underCounts.Count} link(s) taken off ×1 — {names} — "
                            + $"UGX {shortfall:N0} not in the bill if unintended", true));
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Prep unresolved-quantity gate: {ex.Message}");
                    // Fail closed: an un-runnable gate is not a passed gate.
                    results.Add(("Measured lines with no resolvable quantity = 0", false,
                        $"could not evaluate — {ex.Message}", false));
                }

                int pass = results.Count(r => r.Pass);
                int fail = results.Count - pass;
                int hardFail    = results.Count(r => !r.Pass && !r.Confirmable);
                int confirmFail = results.Count(r => !r.Pass &&  r.Confirmable);
                bool allGreen = fail == 0;

                var rp = StingResultPanel.Create("Prep For Export — Quality Gate")
                    .SetSubtitle(allGreen
                        ? "★ All gates GREEN. Safe to export the Tender BOQ."
                        : hardFail == 0
                            ? $"{confirmFail} gate(s) need CONFIRMATION — nothing is provably wrong, but a "
                              + "human has to agree the bill is intended to read this way."
                            : $"{hardFail} of {results.Count} gates failed. Export likely to produce inaccurate BOQ — remediate first."
                              + (confirmFail > 0 ? $" A further {confirmFail} need confirmation." : ""))
                    .AddSection("QUALITY GATES");
                foreach (var r in results)
                    rp.Metric(r.Name, $"{(r.Pass ? "✓" : r.Confirmable ? "?" : "✗")}  {r.Detail}"
                                      + (!r.Pass && r.Confirmable ? "  [CONFIRM]" : ""));
                rp.AddSection("RECOMMENDED ACTIONS");
                if (!results[0].Pass) rp.Text("Run Workflow → DailyQA to improve compliance");
                if (results.Count > 2 && !results[2].Pass) rp.Text("Run RetagStale to clear stale elements");
                if (unresolvedQty > 0)
                    rp.Text($"Run Validate → Cost (COST.QTY.UNRESOLVED) to list the {unresolvedQty} line(s); " +
                            "each is a take-off rule whose quantity source is missing or unbound. " +
                            "They would bill at zero.");
                foreach (var u in underCounts)
                    rp.Text($"Link '{u.LinkName}' is placed ×{u.InstanceCount} but is taken off ×1 "
                          + $"({u.RowCount} row(s), UGX {u.BilledUGX:N0} billed, UGX {u.WouldBeUGX:N0} if multiplied). "
                          + "Enable the per-link ×N multiplier if these are distinct buildings; ignore it if "
                          + "this is one reference model placed more than once.");
                if (allGreen) rp.Text("Click ★ Tender BOQ to export");
                rp.Show();

                StingLog.Info($"Prep For Export: {pass}/{results.Count} gates green "
                            + $"({hardFail} hard fail, {confirmFail} awaiting confirmation)");

                // A-3 — a confirmable gate must not block on its own. When the only
                // failures are confirmable, ask once and honour the answer: placing a
                // shared reference model twice is legitimate and must not be an
                // un-passable gate, while seven cottages billed as one must not slip
                // through in silence. Hard failures still block regardless.
                if (hardFail == 0 && confirmFail > 0)
                {
                    var td = new TaskDialog("Prep For Export — confirmation required")
                    {
                        MainInstruction = $"{confirmFail} gate(s) need your confirmation",
                        MainContent =
                            string.Join("\n\n", underCounts.Select(u =>
                                $"Link '{u.LinkName}' is placed ×{u.InstanceCount} but taken off ×1.\n"
                                + $"UGX {u.ShortfallUGX:N0} is not in the bill. Intended?")),
                        CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                        DefaultButton = TaskDialogResult.No,
                    };
                    bool confirmed = td.Show() == TaskDialogResult.Yes;
                    StingLog.Info($"Prep For Export: link under-count {(confirmed ? "CONFIRMED as intended" : "NOT confirmed")} by user.");
                    return confirmed ? Result.Succeeded : Result.Failed;
                }

                return allGreen ? Result.Succeeded : Result.Failed;
            }
            catch (Exception ex) { StingLog.Error("BOQPrepForExportCommand", ex); message = ex.Message; return Result.Failed; }
        }
    }
}
