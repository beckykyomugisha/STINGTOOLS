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
                Document doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var registry = CostPlanRegistry.Get(doc);
                if (registry.BuildingTypes.Count == 0)
                {
                    TaskDialog.Show("STING Cost Plan",
                        "No NRM1 benchmarks loaded. Verify STING_NRM1_BENCHMARKS.csv is present in the data folder.");
                    return Result.Cancelled;
                }

                // Pick building type.
                var btItems = registry.BuildingTypes
                    .Select(b => new StingListPicker.ListItem { Label = b })
                    .ToList();
                var pickedBt = StingListPicker.Show(
                    "STING — Cost plan: building type",
                    "Pick the building type whose benchmarks should drive this plan.",
                    btItems, allowMultiSelect: false);
                if (pickedBt == null || pickedBt.Count == 0) return Result.Cancelled;
                string buildingType = pickedBt[0].Label;

                // Prompt for GIFA.
                double gifa = PromptForGifa(doc);
                if (gifa <= 0) return Result.Cancelled;

                var plan = CostPlanEngine.Create(doc, buildingType, gifa, label: "Concept");
                string path = CostPlanEngine.Save(doc, plan);

                TaskDialog.Show("STING — Cost plan created",
                    $"Cost plan saved.\n\n" +
                    $"Building type:      {buildingType}\n" +
                    $"GIFA:               {gifa:N0} m²\n" +
                    $"Lines:              {plan.Lines.Count}\n" +
                    $"Subtotal (likely):  GBP {plan.SubtotalLikely:N0}\n" +
                    $"Grand total:        GBP {plan.GrandTotalLikely:N0}\n" +
                    $"Headline £/m² GIFA: {plan.CostPerSqmLikely:N0}\n\n" +
                    $"Path: {Path.GetFileName(path)}");
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
                Document doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var paths = CostPlanEngine.ListPlans(doc);
                if (paths.Count == 0)
                {
                    TaskDialog.Show("STING Cost Plan", "No saved cost plans found. Run CostPlan_Create first.");
                    return Result.Cancelled;
                }

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

                var plan = CostPlanEngine.Load(picked[0].Tag as string);
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
                ShowVarianceReport(plan, variance);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("CostPlan_Compare", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static void ShowVarianceReport(CostPlanDocument plan,
            List<CostPlanEngine.CostPlanVariance> variances)
        {
            int red = variances.Count(v => v.Status == "Red");
            int amber = variances.Count(v => v.Status == "Amber");
            int green = variances.Count(v => v.Status == "Green");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Plan: {plan.Label}  ({plan.BuildingType}, {plan.GifaM2:N0} m² GIFA)");
            sb.AppendLine($"Status:  Red {red}  /  Amber {amber}  /  Green {green}");
            sb.AppendLine();
            sb.AppendLine("Top 10 variances (by absolute %):");
            foreach (var v in variances.OrderByDescending(x => Math.Abs(x.DeltaPct)).Take(10))
            {
                sb.AppendLine(
                    $"  [{v.Status[0]}] {v.ElementCode,-5} {Trim(v.ElementName, 28),-28}  " +
                    $"plan {v.PlannedLikely,12:N0}   actual {v.Actual,12:N0}   {v.DeltaPct,+6:F1}%");
            }

            TaskDialog.Show("STING — Cost plan compare", sb.ToString());
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
                Document doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var paths = CostPlanEngine.ListPlans(doc);
                if (paths.Count == 0)
                {
                    TaskDialog.Show("STING Cost Plan", "No saved cost plans found.");
                    return Result.Cancelled;
                }

                var items = paths.Select(p => new StingListPicker.ListItem
                {
                    Label = Path.GetFileNameWithoutExtension(p),
                    Detail = File.GetLastWriteTime(p).ToString("yyyy-MM-dd HH:mm"),
                    Tag = p
                }).ToList();
                var picked = StingListPicker.Show("STING — Cost plan: export",
                    "Pick the cost plan to export.", items, allowMultiSelect: false);
                if (picked == null || picked.Count == 0) return Result.Cancelled;

                var plan = CostPlanEngine.Load(picked[0].Tag as string);
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

                TaskDialog.Show("STING — Cost plan exported", $"Saved to:\n{outPath}");
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
