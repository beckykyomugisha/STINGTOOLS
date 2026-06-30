// ══════════════════════════════════════════════════════════════════════════
//  CostPlanCommands.cs — User-facing NRM1 cost-plan commands (P4).
//
//  CostPlan_Create  — pick building type + GIFA → mint a CostPlanDocument.
//  CostPlan_Compare — pick a saved plan + compare against live BOQ totals.
//  CostPlan_Export  — render the active plan to xlsx via ClosedXML.
//  CostPlan_Open    — pick a saved plan and re-open in the dock panel
//                     (panel UI is a follow-on; the command surfaces a
//                     diagnostic summary today).
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.BIMManager;
using StingTools.BOQ;
using StingTools.Core;
using StingTools.Core.CostPlan;
using StingTools.Select;
using StingTools.UI;       // StingResultPanel

namespace StingTools.Commands.Cost
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CostPlanCreateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var registry = CostPlanRegistry.Get(doc);
                if (registry.BuildingTypes.Count == 0)
                {
                    StingResultPanel.Create("New Cost Plan")
                        .AddSection("NO BENCHMARKS")
                        .Text("No NRM1 benchmarks loaded. Verify STING_NRM1_BENCHMARKS.csv is present in the data folder.")
                        .Show();
                    return Result.Cancelled;
                }

                // P0.3 — inline-form gate. When the BOQ panel supplied the
                // CostPlanBuildingType + CostPlanGifa ExtraParams, skip the building-
                // type picker + GIFA TaskDialog (no popup). Falls back to the modal
                // picker chain for ribbon / other callers.
                string buildingType;
                double gifa;
                string fBt = UI.StingCommandHandler.GetExtraParam("CostPlanBuildingType");
                string fGifa = UI.StingCommandHandler.GetExtraParam("CostPlanGifa");
                string btMatch = string.IsNullOrEmpty(fBt) ? null
                    : registry.BuildingTypes.FirstOrDefault(b => string.Equals(b, fBt, StringComparison.OrdinalIgnoreCase));
                if (btMatch != null
                    && double.TryParse(fGifa, NumberStyles.Any, CultureInfo.InvariantCulture, out gifa) && gifa > 0)
                {
                    buildingType = btMatch;
                }
                else
                {
                    // Pick building type.
                    var btItems = registry.BuildingTypes
                        .Select(b => new StingListPicker.ListItem { Label = b })
                        .ToList();
                    var pickedBt = StingListPicker.Show(
                        "STING — Cost plan: building type",
                        "Pick the building type whose benchmarks should drive this plan.",
                        btItems, allowMultiSelect: false);
                    if (pickedBt == null || pickedBt.Count == 0) return Result.Cancelled;
                    buildingType = pickedBt[0].Label;

                    // Prompt for GIFA.
                    gifa = PromptForGifa(doc);
                    if (gifa <= 0) return Result.Cancelled;
                }

                var plan = CostPlanEngine.Create(doc, buildingType, gifa, label: "Concept");
                string path = CostPlanEngine.Save(doc, plan);

                // PM-2 — the elemental cost plan auto-seeds the project budget
                // (PROJECT_BUDGET_UGX, FX-converted), so the budget-variance and the
                // forecast read the plan's GrandTotalLikely instead of needing a
                // separate manual budget entry. ReadProjectBudget consumes this.
                double budgetUgx = plan.GrandTotalLikely;
                string pc = (plan.Currency ?? "UGX").ToUpperInvariant();
                if (pc == "USD") budgetUgx *= TagConfig.GetConfigDouble("UGX_PER_USD", 3700.0);
                else if (pc == "GBP") budgetUgx *= TagConfig.GetConfigDouble("UGX_PER_GBP", 4700.0);
                try
                {
                    TagConfig.SetConfigValue("PROJECT_BUDGET_UGX",
                        Math.Round(budgetUgx, 0).ToString("F0", CultureInfo.InvariantCulture));
                }
                catch (Exception ex) { StingLog.Warn($"CostPlan budget seed: {ex.Message}"); }

                StingResultPanel.Create("Cost plan created")
                    .SetSubtitle($"{buildingType} · {gifa:N0} m² GIFA")
                    .AddSection("PLAN")
                    .Metric("Building type", buildingType)
                    .Metric("GIFA", $"{gifa:N0} m²")
                    .Metric("Lines", plan.Lines.Count.ToString())
                    .Metric("Subtotal (likely)", $"{plan.Currency} {plan.SubtotalLikely:N0}")
                    .Metric("Grand total", $"{plan.Currency} {plan.GrandTotalLikely:N0}")
                    .Metric($"Headline {plan.Currency}/m² GIFA", $"{plan.CostPerSqmLikely:N0}")
                    .Text($"Path: {Path.GetFileName(path)}")
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("CostPlan_Create", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static double PromptForGifa(Document doc)
        {
            // Try to suggest a default from the sum of Room.Area on the
            // model. Falls back to a literal prompt if no rooms.
            double suggested = 0;
            try
            {
                var col = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType();
                foreach (Element el in col)
                {
                    if (el is Autodesk.Revit.DB.Architecture.Room room && room.Area > 0)
                        suggested += room.Area * 0.092903; // ft² → m²
                }
            }
            catch (Exception ex) { StingLog.Warn($"PromptForGifa room scan: {ex.Message}"); }

            string suggestion = suggested > 0 ? $" (model rooms total {suggested:N0} m²)" : "";
            var td = new TaskDialog("STING — Cost plan GIFA")
            {
                MainInstruction = "Enter target GIFA in m²" + suggestion,
                MainContent =
                    "The cost plan multiplies each benchmark by this number. " +
                    "Use the gross internal floor area in square metres.",
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel
            };
            if (suggested > 0)
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    $"Use {suggested:N0} m² (from model rooms)",
                    "Sum of all Room.Area in the project.");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Enter a literal value",
                "Falls back to a basic numeric prompt.");

            var result = td.Show();
            if (result == TaskDialogResult.CommandLink1 && suggested > 0)
                return suggested;
            if (result == TaskDialogResult.CommandLink2)
            {
                // Simple sequential prompts (TaskDialog has no native
                // text input). Surface a list of common GIFA bands.
                var items = new[] { "500", "1000", "2500", "5000", "8500", "10000", "15000", "20000", "30000" }
                    .Select(s => new StingListPicker.ListItem { Label = s + " m²", Tag = (object)double.Parse(s) })
                    .ToList();
                var picked = StingListPicker.Show("STING — GIFA",
                    "Pick a GIFA band (or cancel and rerun with a project-specific value).",
                    items, allowMultiSelect: false);
                if (picked != null && picked.Count > 0 && picked[0].Tag is double d) return d;
            }
            return 0;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CostPlanCompareCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var paths = CostPlanEngine.ListPlans(doc);
                if (paths.Count == 0)
                {
                    StingResultPanel.Create("Compare vs BOQ")
                        .AddSection("NO PLANS")
                        .Text("No saved cost plans found. Run CostPlan_Create first.")
                        .Show();
                    return Result.Cancelled;
                }

                // P0.3 — inline-form gate: when the panel supplied CostPlanPath, skip
                // the picker (no popup). Falls back to the modal picker otherwise.
                string chosen;
                string fPath = UI.StingCommandHandler.GetExtraParam("CostPlanPath");
                if (!string.IsNullOrEmpty(fPath) && File.Exists(fPath))
                {
                    chosen = fPath;
                }
                else
                {
                    var items = paths.Select(p => new StingListPicker.ListItem
                    {
                        Label = Path.GetFileNameWithoutExtension(p),
                        Detail = File.GetLastWriteTime(p).ToString("yyyy-MM-dd HH:mm"),
                        Tag = p
                    }).ToList();
                    var picked = StingListPicker.Show("STING — Cost plan: compare",
                        "Pick the cost plan to compare against the live BOQ.",
                        items, allowMultiSelect: false);
                    if (picked == null || picked.Count == 0) return Result.Cancelled;
                    chosen = picked[0].Tag as string;
                }

                var plan = CostPlanEngine.Load(chosen);
                if (plan == null)
                {
                    message = "Failed to load cost plan.";
                    return Result.Failed;
                }

                var boq = BOQCostManager.BuildBOQDocument(doc);
                var byNrm2 = boq.Sections
                    .GroupBy(s => s.NRM2Section ?? "99")
                    .ToDictionary(g => g.Key, g => g.Sum(s => s.TotalUGX));

                var variance = CostPlanEngine.Compare(plan, byNrm2);
                ShowVarianceReport(doc, plan, variance, boq);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("CostPlan_Compare", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static void ShowVarianceReport(Document doc, CostPlanDocument plan,
            List<CostPlanEngine.CostPlanVariance> variances, BOQDocument boq)
        {
            int red = variances.Count(v => v.Status == "Red");
            int amber = variances.Count(v => v.Status == "Amber");
            int green = variances.Count(v => v.Status == "Green");

            var rp = StingResultPanel.Create("Cost plan compare")
                .SetSubtitle($"{plan.Label}  ({plan.BuildingType}, {plan.GifaM2:N0} m² GIFA)");

            // P0-7 — total-level reconciliation: the NRM1 benchmark estimate
            // (GIFA × £/m²) and the NRM2 element take-off are TWO DELIBERATE
            // VIEWS of the same project. Tie the plan grand total to the BOQ
            // Contract Sum via the shared ContractSumResolver so the headline is
            // "one rate source, two methods" — not two accidental numbers.
            double planUgx = plan.GrandTotalLikely;
            string pc = (plan.Currency ?? "UGX").ToUpperInvariant();
            if (pc == "USD") planUgx *= TagConfig.GetConfigDouble("UGX_PER_USD", 3700.0);
            else if (pc == "GBP") planUgx *= TagConfig.GetConfigDouble("UGX_PER_GBP", 4700.0);

            double boqSum = ContractSumResolver.Resolve(doc, boq, out string sumSource);
            double recDelta = boqSum - planUgx;
            double recPct = planUgx > 0 ? recDelta / planUgx * 100.0 : 0;
            string recStatus = Math.Abs(recPct) > 20 ? "Red" : Math.Abs(recPct) > 10 ? "Amber" : "Green";

            rp.AddSection("RECONCILIATION — NRM1 plan vs BOQ Contract Sum")
                .Metric("NRM1 plan (UGX)", $"{planUgx:N0}")
                .Metric("BOQ Contract Sum (UGX)", $"{boqSum:N0}")
                .Metric("Δ", $"{recDelta:+#,##0;-#,##0}")
                .Metric("Δ%", $"{recPct:+0.0;-0.0}%")
                .Metric("Status", recStatus)
                .Text($"BOQ basis: {sumSource}. The NRM1 benchmark and the NRM2 element " +
                      "take-off are deliberate alternative views; both draw on the canonical " +
                      "rate library so a large Δ flags a real estimating gap, not a method artefact.");

            rp.AddSection("STATUS")
                .Metric("Red", red.ToString())
                .Metric("Amber", amber.ToString())
                .Metric("Green", green.ToString());

            var rows = variances.OrderByDescending(x => Math.Abs(x.DeltaPct)).Take(10)
                .Select(v => new[]
                {
                    v.Status, v.ElementCode ?? "", Trim(v.ElementName, 28),
                    $"{v.PlannedLikely:N0}", $"{v.Actual:N0}", $"{v.DeltaPct:+0.0;-0.0}%"
                }).ToList();
            rp.AddSection("TOP 10 VARIANCES (by absolute %)")
                .Table(new[] { "Status", "Code", "Element", "Plan", "Actual", "Δ%" }, rows);

            rp.Show();
        }

        private static string Trim(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max - 1) + "…");
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CostPlanExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var paths = CostPlanEngine.ListPlans(doc);
                if (paths.Count == 0)
                {
                    StingResultPanel.Create("Export Cost Plan")
                        .AddSection("NO PLANS")
                        .Text("No saved cost plans found.")
                        .Show();
                    return Result.Cancelled;
                }

                // P0.3 — inline-form gate: when the panel supplied CostPlanPath, skip
                // the picker (no popup). Falls back to the modal picker otherwise.
                string chosen;
                string fPath = UI.StingCommandHandler.GetExtraParam("CostPlanPath");
                if (!string.IsNullOrEmpty(fPath) && File.Exists(fPath))
                {
                    chosen = fPath;
                }
                else
                {
                    var items = paths.Select(p => new StingListPicker.ListItem
                    {
                        Label = Path.GetFileNameWithoutExtension(p),
                        Detail = File.GetLastWriteTime(p).ToString("yyyy-MM-dd HH:mm"),
                        Tag = p
                    }).ToList();
                    var picked = StingListPicker.Show("STING — Cost plan: export",
                        "Pick the cost plan to export.", items, allowMultiSelect: false);
                    if (picked == null || picked.Count == 0) return Result.Cancelled;
                    chosen = picked[0].Tag as string;
                }

                var plan = CostPlanEngine.Load(chosen);
                if (plan == null) { message = "Failed to load plan."; return Result.Failed; }

                string outDir = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc), "cost_plans");
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir,
                    $"cost_plan_{SafeName(plan.Label)}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

                using (var wb = new XLWorkbook())
                {
                    var ws = wb.Worksheets.Add("NRM1 Cost Plan");
                    int r = 1;
                    ws.Cell(r, 1).Value = $"STING Cost Plan — {plan.Label}";
                    ws.Cell(r, 1).Style.Font.Bold = true;
                    ws.Cell(r, 1).Style.Font.FontSize = 14;
                    r += 2;
                    ws.Cell(r, 1).Value = "Building type:"; ws.Cell(r, 2).Value = plan.BuildingType; r++;
                    ws.Cell(r, 1).Value = "GIFA (m²):";      ws.Cell(r, 2).Value = plan.GifaM2; r++;
                    ws.Cell(r, 1).Value = "Currency:";       ws.Cell(r, 2).Value = plan.Currency; r++;
                    ws.Cell(r, 1).Value = "Created:";        ws.Cell(r, 2).Value = plan.CreatedUtc; r += 2;

                    int headerRow = r;
                    string[] headers = { "NRM1", "Element", "Unit", "Low £/m²", "Likely £/m²", "High £/m²",
                                          "Qty", "Total Low", "Total Likely", "Total High", "PERT Expected", "Note" };
                    for (int i = 0; i < headers.Length; i++)
                    {
                        ws.Cell(headerRow, i + 1).Value = headers[i];
                        ws.Cell(headerRow, i + 1).Style.Font.Bold = true;
                        ws.Cell(headerRow, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
                    }
                    r = headerRow + 1;
                    foreach (var line in plan.Lines)
                    {
                        ws.Cell(r, 1).Value = line.ElementCode;
                        ws.Cell(r, 2).Value = line.ElementName;
                        ws.Cell(r, 3).Value = line.Unit;
                        ws.Cell(r, 4).Value = line.LowRate;
                        ws.Cell(r, 5).Value = line.LikelyRate;
                        ws.Cell(r, 6).Value = line.HighRate;
                        ws.Cell(r, 7).Value = line.Quantity;
                        ws.Cell(r, 8).Value = line.TotalLow;
                        ws.Cell(r, 9).Value = line.TotalLikely;
                        ws.Cell(r, 10).Value = line.TotalHigh;
                        ws.Cell(r, 11).Value = line.TotalExpected;
                        ws.Cell(r, 12).Value = line.Note;
                        r++;
                    }
                    // Totals row.
                    int totalsRow = r;
                    ws.Cell(totalsRow, 1).Value = "Subtotal";
                    ws.Cell(totalsRow, 1).Style.Font.Bold = true;
                    ws.Cell(totalsRow, 8).Value = plan.SubtotalLow;
                    ws.Cell(totalsRow, 9).Value = plan.SubtotalLikely;
                    ws.Cell(totalsRow, 10).Value = plan.SubtotalHigh;
                    ws.Cell(totalsRow, 11).Value = plan.SubtotalExpected;
                    r += 2;

                    ws.Cell(r, 1).Value = "Risk allowance %:"; ws.Cell(r, 2).Value = plan.RiskAllowancePct; r++;
                    ws.Cell(r, 1).Value = "Inflation %:";       ws.Cell(r, 2).Value = plan.InflationAllowancePct; r++;
                    ws.Cell(r, 1).Value = "Design contingency %:"; ws.Cell(r, 2).Value = plan.DesignContingencyPct; r += 2;

                    ws.Cell(r, 1).Value = "GRAND TOTAL (Likely)";
                    ws.Cell(r, 1).Style.Font.Bold = true;
                    ws.Cell(r, 9).Value = plan.GrandTotalLikely;
                    ws.Cell(r, 9).Style.Font.Bold = true;
                    r++;
                    ws.Cell(r, 1).Value = "Headline £/m² GIFA";
                    ws.Cell(r, 9).Value = plan.CostPerSqmLikely;

                    ws.Columns().AdjustToContents();
                    wb.SaveAs(outPath);
                }

                StingResultPanel.Create("Cost plan exported")
                    .SetCsvPath(outPath)
                    .AddSection("EXPORT")
                    .Metric("Plan", plan.Label)
                    .Text($"Saved to: {outPath}")
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("CostPlan_Export", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "plan";
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars().Concat(new[] { ' ', '/', '\\' }));
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s) sb.Append(invalid.Contains(c) ? '-' : c);
            return sb.ToString().Trim('-');
        }
    }
}
