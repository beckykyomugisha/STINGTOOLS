// ══════════════════════════════════════════════════════════════════════════
//  BOQSupportCommands.cs — Phases 8-10 + UI dispatch targets.
//  Houses the small command classes dispatched from the BOQ panel toolbar
//  and the snapshot-comparison + reconciliation + import flows.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.BOQ
{
    // ══════════════════════════════════════════════════════════════════════
    //  BOQRefreshCommand — dispatched by the panel's "↻ Refresh" button.
    //  Rebuilds the BOQ and writes cost parameters. The WPF panel reloads
    //  its own view-model after the ExternalEvent completes.
    // ══════════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BOQRefreshCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;
                var boq = BOQCostManager.BuildBOQDocument(ctx.Doc);
                using (var tx = new Transaction(ctx.Doc, "STING BOQ — refresh parameters"))
                {
                    tx.Start();
                    BOQCostManager.WriteElementParameters(ctx.Doc, boq.AllItems);
                    BOQCostManager.WriteProjectParameters(ctx.Doc, boq);
                    tx.Commit();
                }
                StingLog.Info($"BOQ refreshed: {boq.AllItems.Count} items, grand UGX {boq.GrandTotalUGX:N0}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("BOQRefreshCommand", ex);
                message = ex.Message; return Result.Failed;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  BOQSetBudgetCommand — writes the budget passed via ExtraParam back
    //  onto ProjectInformation AND into project_config.json so the panel
    //  and the Revit DB stay in sync across sessions.
    // ══════════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BOQSetBudgetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;
                string raw = StingCommandHandler.GetExtraParam("ProjectBudgetUgx") ?? "";
                if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double budget))
                {
                    TaskDialog.Show("STING BOQ", "Invalid budget value."); return Result.Cancelled;
                }
                using (var tx = new Transaction(ctx.Doc, "STING BOQ — set project budget"))
                {
                    tx.Start();
                    Element pi = ctx.Doc.ProjectInformation;
                    if (pi != null)
                    {
                        Parameter p = pi.LookupParameter("PROJECT_BUDGET_UGX");
                        if (p != null && !p.IsReadOnly)
                        {
                            if (p.StorageType == StorageType.Double) p.Set(budget);
                            else if (p.StorageType == StorageType.String) p.Set(budget.ToString("F0", CultureInfo.InvariantCulture));
                        }
                    }
                    tx.Commit();
                }
                TagConfig.SetConfigValue("PROJECT_BUDGET_UGX", budget.ToString("F0", CultureInfo.InvariantCulture));
                StingLog.Info($"Project budget set: UGX {budget:N0}");
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("BOQSetBudget", ex); return Result.Failed; }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  BOQSnapshotSaveCommand — persists the current BOQ as a named snapshot.
    // ══════════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BOQSnapshotSaveCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;
                string label = StingCommandHandler.GetExtraParam("SnapshotLabel") ?? $"Snapshot {DateTime.Now:yyyy-MM-dd HH:mm}";
                string type = StingCommandHandler.GetExtraParam("SnapshotType") ?? "Manual";
                var boq = BOQCostManager.BuildBOQDocument(ctx.Doc);
                using (var tx = new Transaction(ctx.Doc, "STING BOQ — write pre-snapshot parameters"))
                {
                    tx.Start();
                    BOQCostManager.WriteElementParameters(ctx.Doc, boq.AllItems);
                    BOQCostManager.WriteProjectParameters(ctx.Doc, boq);
                    tx.Commit();
                }
                // CST_BOQ_SNAPSHOT_REF requires its own transaction — SaveSnapshot
                // mutates the element parameters.
                string path;
                using (var tx = new Transaction(ctx.Doc, "STING BOQ — stamp snapshot ref"))
                {
                    tx.Start();
                    path = BOQCostManager.SaveSnapshot(ctx.Doc, boq, label, type);
                    foreach (var it in boq.AllItems.Where(i => i.RevitElementId > 0))
                    {
                        Element el;
                        try { el = ctx.Doc.GetElement(new ElementId(it.RevitElementId)); }
                        catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); continue; }
                        ParameterHelpers.SetString(el, "CST_BOQ_SNAPSHOT_REF", label, overwrite: true);
                    }
                    tx.Commit();
                }
                TaskDialog.Show("STING BOQ", $"Snapshot saved as '{label}' ({type}).\n\n{Path.GetFileName(path)}");
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("BOQSnapshotSave", ex); return Result.Failed; }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  BOQAddManualRowCommand — writes a user-authored manual/PS row into
    //  project_boq_manual.json. Called from the panel add-row flow.
    // ══════════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    public class BOQAddManualRowCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;
                var store = BOQCostManager.LoadManualStore(ctx.Doc);
                double.TryParse(StingCommandHandler.GetExtraParam("ManualRowQty"), NumberStyles.Any, CultureInfo.InvariantCulture, out double qty);
                double.TryParse(StingCommandHandler.GetExtraParam("ManualRowRate"), NumberStyles.Any, CultureInfo.InvariantCulture, out double rate);
                var newRow = new BOQLineItem
                {
                    NRM2Section = StingCommandHandler.GetExtraParam("ManualRowSection") ?? "22",
                    Discipline = StingCommandHandler.GetExtraParam("ManualRowDisc") ?? "A",
                    Category = "Manual",
                    ItemName = StingCommandHandler.GetExtraParam("ManualRowName") ?? "Manual item",
                    Unit = StingCommandHandler.GetExtraParam("ManualRowUnit") ?? "each",
                    Quantity = Math.Max(qty, 0.001),
                    RateUGX = Math.Max(rate, 0),
                    RateUSD = rate > 0 ? Math.Round(rate / Math.Max(TagConfig.GetConfigDouble("UGX_PER_USD", 3700.0), 1), 2) : 0,
                    Source = BOQRowSource.Manual,
                    RateSource = "Manual",
                    RateConfidence = 70,
                    Note = "Added manually via BOQ panel"
                };
                store.ManualRows.Add(newRow);
                BOQCostManager.SaveManualRows(ctx.Doc, store.ManualRows, store.ProjectBudgetUGX);
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("BOQAddManualRow", ex); return Result.Failed; }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  SelectInRevitCommand — helper for the panel: selects the Revit element
    //  whose ElementId was passed via ExtraParam.
    // ══════════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    public class BOQSelectInRevitCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.UIDoc == null) return Result.Failed;
                string raw = StingCommandHandler.GetExtraParam("SelectElementId") ?? "";
                if (long.TryParse(raw, out long id))
                {
                    ctx.UIDoc.Selection.SetElementIds(new[] { new ElementId(id) });
                    ctx.UIDoc.ShowElements(new[] { new ElementId(id) });
                }
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("BOQSelectInRevit", ex); return Result.Failed; }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Phase 8 — BOQImportCommand: Excel roundtrip import.
    //  Reads the "Item Schedule" sheet of a previously exported workbook,
    //  matches by BOQLineRef, and writes rate/paragraph overrides back to
    //  the Revit model (and the manual store for non-model rows).
    // ══════════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BOQImportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;

                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Import BOQ from Excel", Filter = "Excel workbook (*.xlsx)|*.xlsx"
                };
                if (dlg.ShowDialog() != true) return Result.Cancelled;

                using (var wb = new XLWorkbook(dlg.FileName))
                {
                    IXLWorksheet sheet = null;
                    try { sheet = wb.Worksheet("Item Schedule"); }
                    catch (Exception ex) { StingLog.Warn($"BOQ Import — Item Schedule sheet not found: {ex.Message}"); }
                    if (sheet == null)
                    {
                        TaskDialog.Show("STING BOQ", "Selected workbook does not contain an 'Item Schedule' sheet.");
                        return Result.Failed;
                    }

                    // Locate header row — scan down up to 5 rows
                    int headerRow = 0;
                    for (int r = 1; r <= 6; r++)
                    {
                        if (string.Equals(sheet.Cell(r, 1).GetString(), "Line ref", StringComparison.OrdinalIgnoreCase))
                        { headerRow = r; break; }
                    }
                    if (headerRow == 0)
                    {
                        TaskDialog.Show("STING BOQ", "'Line ref' header not found — is this a BOQ export?"); return Result.Failed;
                    }

                    // Build column index by header text
                    var colIdx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    for (int c = 1; c <= 24; c++)
                    {
                        string h = sheet.Cell(headerRow, c).GetString();
                        if (!string.IsNullOrEmpty(h)) colIdx[h] = c;
                    }

                    int matched = 0, updated = 0, skipped = 0, manualAdded = 0;
                    var manualStore = BOQCostManager.LoadManualStore(ctx.Doc);

                    // Build a ref → element index once so we don't collect per-row.
                    var refToElement = new Dictionary<string, Element>(StringComparer.OrdinalIgnoreCase);
                    var enums = SharedParamGuids.AllCategoryEnums;
                    var collector = new FilteredElementCollector(ctx.Doc).WhereElementIsNotElementType();
                    if (enums != null && enums.Length > 0)
                        collector = collector.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(enums)));
                    foreach (Element el in collector)
                    {
                        string refVal = ParameterHelpers.GetString(el, "ASS_BOQ_LINE_REF");
                        if (!string.IsNullOrEmpty(refVal)) refToElement[refVal] = el;
                    }

                    using (var tx = new Transaction(ctx.Doc, "STING BOQ — import rate overrides"))
                    {
                        tx.Start();
                        for (int r = headerRow + 1; r <= sheet.LastRowUsed()?.RowNumber(); r++)
                        {
                            string refStr = sheet.Cell(r, colIdx.GetValueOrDefault("Line ref", 1)).GetString();
                            if (string.IsNullOrWhiteSpace(refStr)) continue;
                            double rateUgx = sheet.Cell(r, colIdx.GetValueOrDefault("Rate UGX", 9)).GetDouble();
                            double rateUsd = sheet.Cell(r, colIdx.GetValueOrDefault("Rate USD", 11)).GetDouble();
                            string note = sheet.Cell(r, colIdx.GetValueOrDefault("Note", 14)).GetString();
                            string source = sheet.Cell(r, colIdx.GetValueOrDefault("Source", 13)).GetString();

                            if (refToElement.TryGetValue(refStr, out Element el))
                            {
                                matched++;
                                if (rateUgx > 0) ParameterHelpers.SetString(el, "CST_UNIT_RATE_UGX",
                                    rateUgx.ToString("F0", CultureInfo.InvariantCulture), overwrite: true);
                                if (rateUsd > 0) ParameterHelpers.SetString(el, "CST_UNIT_RATE_USD",
                                    rateUsd.ToString("F2", CultureInfo.InvariantCulture), overwrite: true);
                                ParameterHelpers.SetString(el, "CST_RATE_SOURCE", "Override", overwrite: true);
                                if (!string.IsNullOrEmpty(note))
                                    ParameterHelpers.SetString(el, "ASS_DESCRIPTION_TXT", note, overwrite: true);
                                updated++;
                            }
                            else if (source?.Contains("Manual") == true || source?.Contains("Provisional") == true)
                            {
                                manualAdded++; // Row not in model → keep it in the manual store on next load.
                            }
                            else
                            {
                                skipped++;
                            }
                        }
                        tx.Commit();
                    }
                    BOQCostManager.SaveManualRows(ctx.Doc, manualStore.ManualRows, manualStore.ProjectBudgetUGX);
                    TaskDialog.Show("STING BOQ", $"Matched: {matched}\nUpdated: {updated}\nSkipped (no match): {skipped}\nManual rows preserved: {manualAdded}");
                }
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("BOQImportCommand", ex); return Result.Failed; }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Phase 9 — BOQSnapshotCompareCommand: picks two snapshots and shows
    //  a structured diff in a StingResultPanel.
    // ══════════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    public class BOQSnapshotCompareCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;
                var snaps = BOQCostManager.ListSnapshots(ctx.Doc);
                if (snaps.Count < 2)
                {
                    TaskDialog.Show("STING BOQ", "At least two snapshots are required to compare.\n\nSave a snapshot now from the BOQ panel to start a comparison history.");
                    return Result.Cancelled;
                }
                // Compare the two most recent snapshots — a fuller picker is future work.
                var diff = BOQCostManager.CompareSnapshots(snaps[1].Path, snaps[0].Path);

                var builder = UI.StingResultPanel.Create("BOQ Snapshot Comparison")
                    .SetSubtitle($"{snaps[1].Label} ({snaps[1].Date:dd MMM}) → {snaps[0].Label} ({snaps[0].Date:dd MMM})")
                    .AddSection("OVERVIEW")
                    .Metric("Snap A total", $"UGX {diff.TotalA:N0}")
                    .Metric("Snap B total", $"UGX {diff.TotalB:N0}")
                    .Metric("Net change", $"{(diff.NetChange >= 0 ? "+" : "")}UGX {diff.NetChange:N0}")
                    .Metric("Change %", $"{diff.NetChangePct:+0.0;-0.0;0.0}%")
                    .Metric("Modeled delta", $"UGX {(diff.ModeledB - diff.ModeledA):N0}")
                    .Metric("Provisional delta", $"UGX {(diff.ProvB - diff.ProvA):N0}")
                    .Metric("Carbon delta", $"{diff.NetCarbonChange:+#,##0;-#,##0;0} kgCO₂e");

                if (diff.CategoryDiffs.Count > 0)
                {
                    var rows = diff.CategoryDiffs.OrderByDescending(c => Math.Abs(c.Delta)).Take(40)
                        .Select(c => new[]
                        {
                            c.NRM2Section ?? "", c.Name ?? "", c.Discipline ?? "",
                            $"UGX {c.TotalA:N0}", $"UGX {c.TotalB:N0}", $"{c.Delta:+#,##0;-#,##0;0}",
                            c.ChangeType.ToString(), c.ChangeReason ?? ""
                        }).ToList();
                    builder.AddSection("TOP CATEGORIES CHANGED")
                        .Table(new[] { "§", "Category", "Disc", "Snap A", "Snap B", "Delta", "Type", "Reason" }, rows);
                }
                builder.AddSection("SUMMARY").Text(diff.PlainSummary ?? "");
                builder.Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("BOQSnapshotCompare", ex); return Result.Failed; }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Phase 10 — ReconcileProvisionalsCommand: find PS rows that now have
    //  a matching modeled element. Presents a confirmation dialog; user
    //  confirms which matches to promote to Model source.
    // ══════════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BOQReconcileProvisionalsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;
                var boq = BOQCostManager.BuildBOQDocument(ctx.Doc);
                var matches = BOQCostManager.ReconcileProvisionals(ctx.Doc, boq);
                if (matches.Count == 0)
                {
                    TaskDialog.Show("STING BOQ", "No provisional sums could be automatically matched to modeled elements.");
                    return Result.Cancelled;
                }

                var sb = new StringBuilder();
                foreach (var m in matches.Take(12))
                    sb.AppendLine($"• PS {m.PSRow.ItemName}  (UGX {m.PSRow.TotalUGX:N0})  ↔  "
                        + $"{m.ModeledRow.ItemName} (UGX {m.ModeledRow.TotalUGX:N0})  —  "
                        + $"{m.ConfidencePct:F0}% confidence ({m.Reason})");

                var td = new TaskDialog("Reconcile Provisional Sums")
                {
                    MainInstruction = $"{matches.Count} provisional sum(s) have matching modeled elements.",
                    MainContent = "Review the top matches below. Choose 'Yes' to promote every high-confidence match (≥70%) to Model source "
                        + "and remove the corresponding PS rows from project_boq_manual.json.\n\n" + sb.ToString(),
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
                };
                if (td.Show() != TaskDialogResult.Yes) return Result.Cancelled;

                // Promote — remove promoted PS rows from the manual store, clear CST_PROVISIONAL_SUM on models.
                var store = BOQCostManager.LoadManualStore(ctx.Doc);
                var promoted = matches.Where(m => m.ConfidencePct >= 70).ToList();
                var removedIds = new HashSet<string>(promoted.Select(m => m.PSRow.Id));
                store.ManualRows = store.ManualRows.Where(r => !removedIds.Contains(r.Id)).ToList();
                BOQCostManager.SaveManualRows(ctx.Doc, store.ManualRows, store.ProjectBudgetUGX);

                using (var tx = new Transaction(ctx.Doc, "STING BOQ — reconcile provisionals"))
                {
                    tx.Start();
                    foreach (var m in promoted)
                    {
                        if (m.ModeledRow.RevitElementId < 0) continue;
                        Element el;
                        try { el = ctx.Doc.GetElement(new ElementId(m.ModeledRow.RevitElementId)); }
                        catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); continue; }
                        if (el == null) continue;
                        ParameterHelpers.SetInt(el, "CST_PROVISIONAL_SUM", 0, overwrite: true);
                        ParameterHelpers.SetString(el, "CST_RATE_SOURCE", "PromotedFromPS", overwrite: true);
                    }
                    tx.Commit();
                }
                TaskDialog.Show("STING BOQ", $"Promoted {promoted.Count} provisional sum(s) to modeled rows.");
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("BOQReconcileProvisionals", ex); return Result.Failed; }
        }
    }
    // ══════════════════════════════════════════════════════════════════════
    //  BOQWriteItemParamsCommand — Phase 108b. Writes inline-edited rate,
    //  NRM2 paragraph, and note back onto a modeled element's shared params.
    //  Called from BOQCostManagerPanel.OnItemEdited when the edited row's
    //  source is Model. Manual/PS edits persist via SaveManualRows instead.
    // ══════════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BOQWriteItemParamsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;

                string idStr = StingCommandHandler.GetExtraParam("BOQEditElementId");
                if (!long.TryParse(idStr, out long eid) || eid <= 0) return Result.Succeeded;

                string rateUGXStr = StingCommandHandler.GetExtraParam("BOQEditRateUGX");
                string rateUSDStr = StingCommandHandler.GetExtraParam("BOQEditRateUSD");
                string para = StingCommandHandler.GetExtraParam("BOQEditNRM2Para");
                string note = StingCommandHandler.GetExtraParam("BOQEditNote");

                Element el;
                try { el = ctx.Doc.GetElement(new ElementId(eid)); }
                catch (Exception ex) { StingLog.Warn($"BOQWriteItemParams: GetElement({eid}) — {ex.Message}"); return Result.Failed; }
                if (el == null) return Result.Succeeded;

                using (var tx = new Transaction(ctx.Doc, "STING BOQ — update item parameters"))
                {
                    tx.Start();
                    if (double.TryParse(rateUGXStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double ugx))
                        ParameterHelpers.SetString(el, "CST_UNIT_RATE_UGX",
                            ugx.ToString("F0", CultureInfo.InvariantCulture), overwrite: true);
                    if (double.TryParse(rateUSDStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double usd))
                        ParameterHelpers.SetString(el, "CST_UNIT_RATE_USD",
                            usd.ToString("F2", CultureInfo.InvariantCulture), overwrite: true);
                    if (!string.IsNullOrEmpty(para))
                    {
                        ParameterHelpers.SetString(el, "ASS_NRM2_PARA_TXT", para, overwrite: true);
                        ParameterHelpers.SetString(el, "ASS_NRM2_PARA_DATE_TXT",
                            DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), overwrite: true);
                        ParameterHelpers.SetString(el, "ASS_NRM2_PARA_AUTHOR_TXT",
                            Environment.UserName ?? "unknown", overwrite: true);
                    }
                    ParameterHelpers.SetString(el, "CST_RATE_SOURCE", "Override", overwrite: true);
                    // Note writeback intentionally omitted — no CST_BOQ_NOTE parameter
                    // is registered; keep the edited note on the manual-store side only.
                    tx.Commit();
                }

                StingCommandHandler.ClearExtraParam("BOQEditElementId");
                StingCommandHandler.ClearExtraParam("BOQEditRateUGX");
                StingCommandHandler.ClearExtraParam("BOQEditRateUSD");
                StingCommandHandler.ClearExtraParam("BOQEditNRM2Para");
                StingCommandHandler.ClearExtraParam("BOQEditNote");

                StingLog.Info($"BOQWriteItemParams: wrote rate / NRM2 / source to element {eid}");
                _ = note; // suppress unused-variable warning until note param is registered
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("BOQWriteItemParamsCommand", ex); message = ex.Message; return Result.Failed; }
        }
    }
}
