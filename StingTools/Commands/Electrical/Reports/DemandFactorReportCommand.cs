using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using Newtonsoft.Json.Linq;
using StingTools.Commands.Electrical.CircuitWizard;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Electrical.Reports
{
    /// <summary>
    /// Per-panel demand-factor breakdown (NEC 2023 / BS 7671:2018) exported to
    /// Excel. One worksheet per panel; rows = load class / connected VA /
    /// demand factor rule / demand VA / source citation.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DemandFactorReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            string standardId = (StingElectricalCommandHandler.ActivePanel?.SelectedStandard ?? "BS7671") == "NEC2023"
                ? "NEC_2023" : "BS7671_2018";
            var rules = LoadRules(standardId);
            if (rules.Count == 0)
            {
                TaskDialog.Show("STING Demand Report", "Demand-factor rules not loaded — check STING_DEMAND_FACTORS.json.");
                return Result.Cancelled;
            }

            var byPanel = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
            foreach (var sys in new FilteredElementCollector(doc)
                .OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>())
            {
                try
                {
                    if (sys.SystemType != ElectricalSystemType.PowerCircuit) continue;
                    string panel = sys.PanelName ?? "";
                    string cls   = ClassifySystem(sys);
                    double va    = sys.ApparentLoad;
                    if (!byPanel.TryGetValue(panel, out var bucket))
                        byPanel[panel] = bucket = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    if (!bucket.ContainsKey(cls)) bucket[cls] = 0;
                    bucket[cls] += va;
                }
                catch (Exception ex) { StingLog.Warn($"Demand collect: {ex.Message}"); }
            }
            if (byPanel.Count == 0)
            {
                TaskDialog.Show("STING Demand Report", "No power circuits found.");
                return Result.Cancelled;
            }

            string outDir = OutputLocationHelper.GetOutputDirectory(doc);
            try { outDir = Path.Combine(outDir, "electrical"); Directory.CreateDirectory(outDir); } catch { }
            string filePath = Path.Combine(outDir,
                $"DemandFactorReport_{DateTime.Now:yyyyMMdd-HHmm}.xlsx");

            try
            {
                using var wb = new XLWorkbook();
                foreach (var kv in byPanel)
                {
                    string sheet = SafeSheetName(kv.Key);
                    var ws = wb.Worksheets.Add(sheet);
                    ws.Cell(1, 1).Value = "Load class";
                    ws.Cell(1, 2).Value = "Connected VA";
                    ws.Cell(1, 3).Value = "Demand factor rule";
                    ws.Cell(1, 4).Value = "Demand VA";
                    ws.Cell(1, 5).Value = "Source";
                    ws.Range(1, 1, 1, 5).Style.Font.Bold = true;
                    int row = 2;
                    foreach (var clsKv in kv.Value)
                    {
                        var rule = rules.FirstOrDefault(r =>
                            string.Equals(r.LoadClass, clsKv.Key, StringComparison.OrdinalIgnoreCase));
                        double demand = ApplyFactor(clsKv.Value, rule);
                        ws.Cell(row, 1).Value = clsKv.Key;
                        ws.Cell(row, 2).Value = clsKv.Value;
                        ws.Cell(row, 3).Value = rule?.Description ?? "100% of connected";
                        ws.Cell(row, 4).Value = demand;
                        ws.Cell(row, 5).Value = rule?.Rule ?? "—";
                        row++;
                    }
                    ws.Columns().AdjustToContents();
                }
                wb.SaveAs(filePath);
            }
            catch (Exception ex)
            {
                StingLog.Error($"Demand report Excel save: {ex.Message}", ex);
                TaskDialog.Show("STING Demand Report", $"Save failed: {ex.Message}");
                return Result.Failed;
            }
            TaskDialog.Show("STING Demand Report", $"Exported demand-factor report:\n{filePath}");
            return Result.Succeeded;
        }

        private static string SafeSheetName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Panel";
            string safe = string.Concat(s.Where(ch =>
                char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_'));
            if (safe.Length > 28) safe = safe.Substring(0, 28);
            return string.IsNullOrEmpty(safe) ? "Panel" : safe;
        }

        private static string ClassifySystem(ElectricalSystem sys)
        {
            try
            {
                var first = sys.Elements?.Cast<Element>().FirstOrDefault();
                if (first == null) return "Other";
                string fname = (first as FamilyInstance)?.Symbol?.FamilyName ?? first.Name ?? "";
                string cat   = first.Category?.Name ?? "";
                return CircuitWizardEngine.ClassifyLoad(fname, cat);
            }
            catch { return "Other"; }
        }

        private class DemandRule
        {
            public string LoadClass, Rule, Description;
            public List<(double thresholdVA, double pct)> Brackets = new();
            public bool Continuous;
        }

        private static List<DemandRule> LoadRules(string standardId)
        {
            var list = new List<DemandRule>();
            try
            {
                string path = StingToolsApp.FindDataFile("STING_DEMAND_FACTORS.json");
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return list;
                var root = JObject.Parse(File.ReadAllText(path));
                var std = root["standards"]?[standardId] as JObject;
                if (std == null) return list;
                foreach (var lc in std["loadClasses"] as JArray ?? new JArray())
                {
                    var dr = new DemandRule
                    {
                        LoadClass   = lc["class"]?.ToString() ?? "Other",
                        Rule        = lc["rule"]?.ToString() ?? "",
                        Description = lc["description"]?.ToString() ?? "",
                        Continuous  = lc["continuousLoad"]?.Value<bool>() ?? false
                    };
                    if (lc["factors"] is JArray fac)
                        foreach (var f in fac)
                            dr.Brackets.Add((f["thresholdVA"]?.Value<double>() ?? 0,
                                              f["pct"]?.Value<double>() ?? 100));
                    else if (lc["pct"] != null)
                        dr.Brackets.Add((0, lc["pct"].Value<double>()));
                    list.Add(dr);
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadRules: {ex.Message}"); }
            return list;
        }

        private static double ApplyFactor(double connectedVA, DemandRule rule)
        {
            if (rule == null || rule.Brackets.Count == 0) return connectedVA;
            double total = 0;
            for (int i = 0; i < rule.Brackets.Count; i++)
            {
                double lower = rule.Brackets[i].thresholdVA;
                double upper = i + 1 < rule.Brackets.Count ? rule.Brackets[i + 1].thresholdVA : double.MaxValue;
                if (connectedVA <= lower) break;
                double bracket = Math.Min(connectedVA, upper) - lower;
                total += bracket * (rule.Brackets[i].pct / 100.0);
            }
            return total;
        }
    }
}
