// PlumbingWaterSafetyCommands — Phase 179f WATER SAFETY tab.
//
// Plumb_TMVEngine         — full TMV scan via TMVEngine, writeback, CSV export.
// Plumb_LegionellaReport  — ACOP L8 Legionella risk assessment (docx or txt).
// Plumb_WaterSafetyPlan   — combined RAG dashboard: dead legs + TMV + backflow.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Plumbing;
using StingTools.UI;

namespace StingTools.Commands.Plumbing
{
    // ─────────────────────────────────────────────────────────────────────────
    // Plumb_TMVEngine
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbTMVEngineCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            TMVRegisterResult result;
            try
            {
                using (var tx = new Transaction(doc, "STING Plumbing TMV Engine"))
                {
                    tx.Start();
                    result = TMVEngine.ScanAll(doc);
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("PlumbTMVEngine", ex);
                message = "TMV scan failed: " + ex.Message;
                return Result.Failed;
            }

            // ── CSV export ────────────────────────────────────────────────────
            string csvPath = null;
            try
            {
                csvPath = OutputLocationHelper.GetOutputPath(doc, "TMV_Register.csv");
                var sb = new StringBuilder();
                sb.AppendLine("ElementId,FamilyName,Room,TMVClass,InletHot_C,InletCold_C,Outlet_C,TestDate,AnnualDueDate,Pass");
                foreach (var row in result.Rows ?? new List<TMVRow>())
                {
                    sb.AppendLine(
                        $"{row.ElementId.Value}," +
                        $"\"{row.FamilyName}\"," +
                        $"\"{row.RoomName}\"," +
                        $"{row.TmvClass}," +
                        $"{row.InletHotC:F1}," +
                        $"{row.InletColdC:F1}," +
                        $"{row.OutletC:F1}," +
                        $"{row.TestDate:yyyy-MM-dd}," +
                        $"{row.AnnualDueDate:yyyy-MM-dd}," +
                        $"{(row.PassFail ? "PASS" : "FAIL")}");
                }
                File.WriteAllText(csvPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                StingLog.Warn("PlumbTMVEngine CSV export: " + ex.Message);
                csvPath = null;
            }

            var panel = StingResultPanel.Create("TMV Engine Register");
            panel.SetSubtitle($"Scan: {DateTime.UtcNow:u}");
            panel.AddSection("SUMMARY")
                 .Metric("TMVs total",   (result.Total).ToString())
                 .Metric("Pass",         result.Pass.ToString())
                 .Metric("Fail",         result.Fail.ToString())
                 .Metric("Overdue",      result.Overdue.ToString());

            var rows = result.Rows ?? new List<TMVRow>();
            if (rows.Count > 0)
            {
                panel.AddSection("REGISTER (first 60)");
                foreach (var row in rows.Take(60))
                {
                    string status = row.PassFail ? "PASS" : "FAIL";
                    string overdue = row.AnnualDueDate < DateTime.Today ? " · OVERDUE" : "";
                    panel.Text(
                        $"{row.ElementId.Value}  {row.FamilyName,-28}  [{row.TmvClass}]  " +
                        $"Room: {row.RoomName,-18}  " +
                        $"Hot {row.InletHotC:F0}°C  Cold {row.InletColdC:F0}°C  " +
                        $"Outlet {row.OutletC:F0}°C  " +
                        $"Test {row.TestDate:yyyy-MM-dd}  {status}{overdue}");
                }
            }
            else
            {
                panel.Text("No TMV elements found. Ensure PLM_TMV_CLASS_TXT is set on relevant fittings/fixtures.");
            }

            // Show failures with reason
            var failures = rows.Where(r => !r.PassFail).ToList();
            if (failures.Count > 0)
            {
                panel.AddSection("FAILURES");
                foreach (var f in failures.Take(20))
                    panel.Text($"⚠ {f.ElementId.Value}  {f.FamilyName}  Reason: {f.FailureReason}");
            }

            if (csvPath != null)
                panel.AddSection("EXPORT").Text($"CSV saved: {csvPath}");

            panel.Show();
            return Result.Succeeded;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Plumb_LegionellaReport
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbLegionellaReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // ── gather data ───────────────────────────────────────────────────
            DeadLegResult deadLegs;
            TMVRegisterResult tmvResult;
            int fixtureCount, dhwPipeCount, tankCount;

            try
            {
                deadLegs  = DeadLegDetector.Scan(doc, writeBack: false);
                tmvResult = TMVEngine.ScanAll(doc);

                fixtureCount = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                    .GetElementCount();

                dhwPipeCount = new FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.Plumbing.Pipe))
                    .Cast<Autodesk.Revit.DB.Plumbing.Pipe>()
                    .Count(p =>
                    {
                        var s = p.MEPSystem?.Name?.ToUpperInvariant() ?? "";
                        return s.Contains("DHW") || s.Contains("HWS") || s.Contains("HOT");
                    });

                tankCount = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                    .Cast<Element>()
                    .Count(e => (e.Name ?? "").ToUpperInvariant().Contains("TANK"));
            }
            catch (Exception ex)
            {
                StingLog.Error("PlumbLegionellaReport — data gather", ex);
                message = "Data collection failed: " + ex.Message;
                return Result.Failed;
            }

            // ── identify high-risk points ────────────────────────────────────
            var highRisk  = deadLegs.Findings.Where(f => f.LegLengthM > 0.45).ToList();
            var mediumRisk = deadLegs.Findings.Where(f => f.LegLengthM > 0.30 && f.LegLengthM <= 0.45).ToList();

            // Fixtures without TMVs where required (simple check: if TMV total < DHW fixture count)
            bool tmvShortfall = tmvResult.Total < (fixtureCount / 4); // rough heuristic

            // ── try to render a docx template ────────────────────────────────
            string outputPath = null;
            bool usedTemplate = false;

            try
            {
                string templatePath = StingToolsApp.FindDataFile("legionella_risk_assessment.docx");
                if (!string.IsNullOrEmpty(templatePath) && File.Exists(templatePath))
                {
                    var tokens = BuildTokenDictionary(doc, deadLegs, tmvResult,
                        fixtureCount, dhwPipeCount, tankCount);
                    outputPath = OutputLocationHelper.GetOutputPath(doc, "Legionella_Risk_Assessment.docx");
                    MiniWordAdapter.Render(templatePath, tokens, outputPath);
                    usedTemplate = true;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn("PlumbLegionellaReport template render: " + ex.Message);
                usedTemplate = false;
            }

            // ── fallback: plain text report ──────────────────────────────────
            if (!usedTemplate)
            {
                try
                {
                    outputPath = OutputLocationHelper.GetOutputPath(doc, "Legionella_Risk_Assessment.txt");
                    var txt = BuildTextReport(doc, deadLegs, tmvResult,
                        fixtureCount, dhwPipeCount, tankCount, highRisk, mediumRisk);
                    File.WriteAllText(outputPath, txt, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    StingLog.Warn("PlumbLegionellaReport txt write: " + ex.Message);
                    outputPath = null;
                }
            }

            // ── result panel ─────────────────────────────────────────────────
            var panel = StingResultPanel.Create("Legionella Risk Assessment");
            panel.SetSubtitle($"{doc.ProjectInformation?.Name}  ·  Assessed: {DateTime.Today:yyyy-MM-dd}");

            panel.AddSection("SYSTEM OVERVIEW")
                 .Metric("Plumbing fixtures",    fixtureCount.ToString())
                 .Metric("DHW/HWS pipes",        dhwPipeCount.ToString())
                 .Metric("Tanks (mechanical eq.)",tankCount.ToString())
                 .Metric("TMVs found",           tmvResult.Total.ToString())
                 .Metric("TMVs overdue",         tmvResult.Overdue.ToString());

            panel.AddSection("DEAD-LEG REGISTER")
                 .Metric("Pipes scanned",        deadLegs.PipesScanned.ToString())
                 .Metric("Dead legs > 0.45 m",   highRisk.Count.ToString() + " (HIGH RISK)")
                 .Metric("Dead legs 0.30–0.45 m", mediumRisk.Count.ToString() + " (MEDIUM)");

            if (highRisk.Count > 0)
            {
                panel.AddSection("HIGH-RISK DEAD LEGS");
                foreach (var f in highRisk.Take(20))
                    panel.Text($"⚠ Pipe {f.TerminalPipeId.Value}  L {f.LegLengthM:F2} m  DN{f.LegPipeDiameterMm:F0}  {f.SystemName}  {f.Notes}");
            }

            panel.AddSection("TMV STATUS")
                 .Metric("Total", tmvResult.Total.ToString())
                 .Metric("Pass",  tmvResult.Pass.ToString())
                 .Metric("Fail",  tmvResult.Fail.ToString())
                 .Metric("Overdue", tmvResult.Overdue.ToString());

            if (tmvShortfall)
                panel.Text("⚠ TMV count appears low relative to fixture count. Review DHW outlets.");

            panel.AddSection("ACOP L8 CONTROL MEASURES")
                 .Text("1. Maintain DHW storage ≥ 60 °C (legionella killed within 2 min).")
                 .Text("2. Maintain DHW distribution ≥ 50 °C at outlet within 60 seconds.")
                 .Text("3. DCW stored and distributed at < 20 °C.")
                 .Text("4. Flush infrequently used outlets at least weekly.")
                 .Text("5. TMV test and calibrate annually per HTM 04-01.")
                 .Text("6. Carry out risk assessment at least every 2 years.");

            if (outputPath != null)
                panel.AddSection("OUTPUT").Text((usedTemplate ? "DOCX" : "TXT") + " report saved: " + outputPath);

            panel.Show();
            return Result.Succeeded;
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static Dictionary<string, object> BuildTokenDictionary(
            Document doc,
            DeadLegResult deadLegs, TMVRegisterResult tmv,
            int fixtureCount, int dhwPipeCount, int tankCount)
        {
            var pi = doc.ProjectInformation;
            return new Dictionary<string, object>
            {
                ["ProjectName"]       = pi?.Name ?? "",
                ["AssessmentDate"]    = DateTime.Today.ToString("yyyy-MM-dd"),
                ["Assessor"]          = pi?.Author ?? "",
                ["ClientName"]        = ParameterHelpers.GetString(pi, "Client Name") ?? pi?.ClientName ?? "",
                ["FixtureCount"]      = fixtureCount,
                ["DhwPipeCount"]      = dhwPipeCount,
                ["TankCount"]         = tankCount,
                ["DeadLegScanned"]    = deadLegs.PipesScanned,
                ["DeadLegsHigh"]      = deadLegs.Findings.Count(f => f.LegLengthM > 0.45),
                ["DeadLegsMedium"]    = deadLegs.Findings.Count(f => f.LegLengthM > 0.30 && f.LegLengthM <= 0.45),
                ["TmvTotal"]          = tmv.Total,
                ["TmvPass"]           = tmv.Pass,
                ["TmvFail"]           = tmv.Fail,
                ["TmvOverdue"]        = tmv.Overdue,
            };
        }

        private static string BuildTextReport(
            Document doc,
            DeadLegResult deadLegs, TMVRegisterResult tmv,
            int fixtureCount, int dhwPipeCount, int tankCount,
            List<DeadLegFinding> highRisk, List<DeadLegFinding> mediumRisk)
        {
            var pi = doc.ProjectInformation;
            var sb = new StringBuilder();
            sb.AppendLine("========================================================");
            sb.AppendLine("  LEGIONELLA RISK ASSESSMENT  (ACOP L8 / HSG 274)");
            sb.AppendLine("========================================================");
            sb.AppendLine($"Project  : {pi?.Name}");
            sb.AppendLine($"Date     : {DateTime.Today:yyyy-MM-dd}");
            sb.AppendLine($"Assessor : {pi?.Author}");
            sb.AppendLine();

            sb.AppendLine("── EXECUTIVE SUMMARY ─────────────────────────────────");
            sb.AppendLine($"  Plumbing fixtures : {fixtureCount}");
            sb.AppendLine($"  DHW/HWS pipes     : {dhwPipeCount}");
            sb.AppendLine($"  Tanks             : {tankCount}");
            sb.AppendLine($"  Dead legs > 0.45m : {highRisk.Count}  (HIGH RISK)");
            sb.AppendLine($"  Dead legs 0.30–0.45m: {mediumRisk.Count}  (MEDIUM RISK)");
            sb.AppendLine($"  TMVs found        : {tmv.Total}");
            sb.AppendLine($"  TMV failures      : {tmv.Fail}");
            sb.AppendLine($"  TMVs overdue      : {tmv.Overdue}");
            sb.AppendLine();

            sb.AppendLine("── SYSTEM DESCRIPTION ────────────────────────────────");
            sb.AppendLine("  Hot water system: centralised DHW storage with distribution.");
            sb.AppendLine("  Cold water system: mains-fed DCW.");
            sb.AppendLine("  Venting: open vent / AAV as designed.");
            sb.AppendLine();

            sb.AppendLine("── DEAD-LEG REGISTER ─────────────────────────────────");
            sb.AppendLine($"  Pipes scanned: {deadLegs.PipesScanned}");
            sb.AppendLine($"  Threshold: > 0.45 m (HSG 274 Part 2)");
            sb.AppendLine();
            if (highRisk.Count == 0 && mediumRisk.Count == 0)
            {
                sb.AppendLine("  No dead legs found.");
            }
            else
            {
                sb.AppendLine("  HIGH RISK (> 0.45 m):");
                foreach (var f in highRisk)
                    sb.AppendLine($"    Pipe {f.TerminalPipeId.Value}  L={f.LegLengthM:F2}m  DN{f.LegPipeDiameterMm:F0}  {f.SystemName}");
                sb.AppendLine();
                sb.AppendLine("  MEDIUM RISK (0.30–0.45 m):");
                foreach (var f in mediumRisk)
                    sb.AppendLine($"    Pipe {f.TerminalPipeId.Value}  L={f.LegLengthM:F2}m  DN{f.LegPipeDiameterMm:F0}  {f.SystemName}");
            }
            sb.AppendLine();

            sb.AppendLine("── TMV REGISTER ──────────────────────────────────────");
            sb.AppendLine($"  Total: {tmv.Total}  Pass: {tmv.Pass}  Fail: {tmv.Fail}  Overdue: {tmv.Overdue}");
            var tmvRows = tmv.Rows ?? new List<TMVRow>();
            foreach (var row in tmvRows.Take(50))
                sb.AppendLine($"  {row.ElementId.Value}  {row.FamilyName}  {row.TmvClass}  " +
                              $"Out {row.OutletC:F0}°C  Test {row.TestDate:yyyy-MM-dd}  " +
                              $"{(row.PassFail ? "PASS" : "FAIL" + " - " + row.FailureReason)}");
            sb.AppendLine();

            sb.AppendLine("── CONTROL MEASURES (ACOP L8) ────────────────────────");
            sb.AppendLine("  1. DHW storage ≥ 60 °C. Distribution ≥ 50 °C within 60 s.");
            sb.AppendLine("  2. DCW storage and distribution < 20 °C.");
            sb.AppendLine("  3. Weekly flush of infrequently used outlets.");
            sb.AppendLine("  4. TMV test and calibration annually (HTM 04-01).");
            sb.AppendLine("  5. Risk assessment review every 2 years.");
            sb.AppendLine("  6. Eliminate or insulate dead legs > 0.45 m.");
            sb.AppendLine();

            sb.AppendLine("── SIGN-OFF ───────────────────────────────────────────");
            sb.AppendLine("  Responsible person: ___________________________");
            sb.AppendLine("  Date reviewed:      ___________________________");
            sb.AppendLine("  Next review due:    ___________________________");
            sb.AppendLine("========================================================");
            return sb.ToString();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Plumb_WaterSafetyPlan
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbWaterSafetyPlanCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // ── gather ────────────────────────────────────────────────────────
            DeadLegResult deadLegs;
            TMVRegisterResult tmv;
            List<BackflowRisk> backflowRisks;
            List<CrossConnectionFinding> crossConnections;

            try
            {
                deadLegs         = DeadLegDetector.Scan(doc, writeBack: false);
                tmv              = TMVEngine.ScanAll(doc);
                backflowRisks    = BackflowClassifier.ClassifyAll(doc);
                crossConnections = CrossConnectionChecker.Scan(doc);
            }
            catch (Exception ex)
            {
                StingLog.Error("PlumbWaterSafetyPlan", ex);
                message = "Water safety data collection failed: " + ex.Message;
                return Result.Failed;
            }

            // ── classify findings ─────────────────────────────────────────────
            var redItems   = new List<string>();
            var amberItems = new List<string>();
            var greenItems = new List<string>();

            // Dead legs
            foreach (var f in deadLegs.Findings)
            {
                if (f.LegLengthM > 0.45)
                    redItems.Add($"Dead leg {f.LegLengthM:F2} m (pipe {f.TerminalPipeId.Value}) — {f.SystemName}");
                else if (f.LegLengthM > 0.30)
                    amberItems.Add($"Dead leg {f.LegLengthM:F2} m (pipe {f.TerminalPipeId.Value}) — {f.SystemName}");
            }
            if (deadLegs.LegsFlagged == 0)
                greenItems.Add("No dead legs exceeding HSG 274 threshold detected.");

            // TMV
            foreach (var row in (tmv.Rows ?? new List<TMVRow>()).Where(r => !r.PassFail))
                redItems.Add($"TMV FAIL: {row.ElementId.Value} {row.FamilyName} — {row.FailureReason}");

            var overdueRows = (tmv.Rows ?? new List<TMVRow>()).Where(r => r.AnnualDueDate < DateTime.Today).ToList();
            foreach (var row in overdueRows)
                amberItems.Add($"TMV overdue: {row.ElementId.Value} {row.FamilyName} (due {row.AnnualDueDate:yyyy-MM-dd})");

            if (tmv.Fail == 0 && tmv.Overdue == 0)
                greenItems.Add($"All {tmv.Total} TMVs passing and within test date.");

            // Backflow risks from ClassifyAll
            foreach (var risk in backflowRisks ?? new List<BackflowRisk>())
            {
                if (risk.Category >= FluidCategory.Category4)
                    redItems.Add($"Cat-{(int)risk.Category} backflow risk: {risk.ElementId.Value} {risk.SystemName} — {risk.RecommendedDevice} required");
                else if (risk.Category == FluidCategory.Category3)
                    amberItems.Add($"Cat-3 backflow risk: {risk.ElementId.Value} {risk.SystemName} — {risk.RecommendedDevice} required");
            }

            // Cross-connections
            foreach (var cc in crossConnections ?? new List<CrossConnectionFinding>())
            {
                if (cc.NonPotableCategory >= FluidCategory.Category4)
                    redItems.Add($"Cross-connection [{cc.Severity}]: potable {cc.PotableElementId.Value} ↔ non-potable {cc.NonPotableElementId.Value} Cat-{(int)cc.NonPotableCategory} — {cc.Notes}");
                else if (cc.NonPotableCategory == FluidCategory.Category3)
                    amberItems.Add($"Cross-connection Cat-3: potable {cc.PotableElementId.Value} ↔ non-potable {cc.NonPotableElementId.Value} — {cc.Notes}");
            }

            bool noBackflowHighRisk = !((backflowRisks?.Any(r => r.Category >= FluidCategory.Category3) ?? false)
                                     || (crossConnections?.Any(c => c.NonPotableCategory >= FluidCategory.Category3) ?? false));
            if (noBackflowHighRisk)
                greenItems.Add("No Category 3+ backflow risks or cross-connections found (BS EN 1717).");

            // ── panel ─────────────────────────────────────────────────────────
            int backflowCount = (backflowRisks?.Count(r => r.Category >= FluidCategory.Category3) ?? 0)
                              + (crossConnections?.Count(c => c.NonPotableCategory >= FluidCategory.Category3) ?? 0);

            var panel = StingResultPanel.Create("Water Safety Plan — RAG Dashboard");
            panel.SetSubtitle($"Dead legs: {deadLegs.LegsFlagged}  ·  TMVs: {tmv.Total}  ·  " +
                              $"Backflow findings (Cat3+): {backflowCount}");

            panel.AddSection("RAG SUMMARY")
                 .Metric("RED   (action required)", redItems.Count.ToString())
                 .Metric("AMBER (monitor / plan)",  amberItems.Count.ToString())
                 .Metric("GREEN (compliant)",       greenItems.Count.ToString());

            int maxShow = 20;
            if (redItems.Count > 0)
            {
                panel.AddSection("RED — IMMEDIATE ACTION");
                foreach (var r in redItems.Take(maxShow)) panel.Text("🔴 " + r);
            }

            if (amberItems.Count > 0)
            {
                panel.AddSection("AMBER — MONITOR / PLAN");
                foreach (var a in amberItems.Take(maxShow)) panel.Text("🟡 " + a);
            }

            if (greenItems.Count > 0)
            {
                panel.AddSection("GREEN — COMPLIANT");
                foreach (var g in greenItems.Take(maxShow)) panel.Text("🟢 " + g);
            }

            panel.AddSection("NEXT STEPS")
                 .Text("Run Plumb_LegionellaReport to generate a full ACOP L8 assessment.")
                 .Text("Run Plumb_TMVEngine to update TMV register with latest test data.")
                 .Text("Run Plumb_BackflowAudit for detailed BS EN 1717 analysis.");

            panel.Show();
            return Result.Succeeded;
        }
    }
}
