using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Hvac;

namespace StingTools.Commands.Hvac
{
    // ─────────────────────────────────────────────────────────────────────────
    // Phase 192 (E2) — 40-year HVAC life-cycle cost comparison.
    //
    // Loads 2+ system options from STING_HVAC_LCC_DEFAULTS.json (+ project
    // overlay), resolves energy/area from the model where requested, runs the
    // pure LifeCycleCostEngine, and writes a year-by-year XLSX (per option) +
    // summary. Graphs are charted in Excel from the year columns (the dialog
    // states this).
    // ─────────────────────────────────────────────────────────────────────────

    public class LccOptionConfig : LccOption
    {
        public double EnergyKwhPerYear { get; set; }
        public bool DeriveEnergyFromModel { get; set; }
    }

    public class LccDefaults
    {
        public int HorizonYears { get; set; } = 40;
        public double EscalationPct { get; set; } = 3.0;
        public double DiscountPct { get; set; } = 5.0;
        public double TariffPerKwh { get; set; } = 0.15;
        public double EquivalentFullLoadHours { get; set; } = 2200;
        public List<LccOptionConfig> Options { get; set; } = new List<LccOptionConfig>();
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacLifeCycleCompareCommand : IExternalCommand
    {
        private const double SqFtToM2 = 0.09290304;

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var cfg = LoadDefaults(doc);
            if (cfg.Options == null || cfg.Options.Count < 2)
            {
                TaskDialog.Show("HVAC Life-Cycle Cost",
                    "Need at least two options. Ship STING_HVAC_LCC_DEFAULTS.json in data/ " +
                    "or add _BIM_COORD/hvac_lcc.json with your scenario.");
                return Result.Succeeded;
            }

            double modelAreaM2 = ModelFloorAreaM2(doc);
            double modelPeakW = SumSpacePeakSensibleW(doc);
            double modelKwhPerYear = modelPeakW * cfg.EquivalentFullLoadHours / 1000.0;

            // Resolve each option's energy + area before handing pure inputs to the engine.
            var options = new List<LccOption>();
            foreach (var o in cfg.Options)
            {
                double energy = o.AnnualEnergyCost;
                if (energy <= 0)
                {
                    if (o.DeriveEnergyFromModel && modelKwhPerYear > 0)
                        energy = modelKwhPerYear * cfg.TariffPerKwh;
                    else if (o.EnergyKwhPerYear > 0)
                        energy = o.EnergyKwhPerYear * cfg.TariffPerKwh;
                }
                double area = (o.AnnualMaintCostPerM2 > 0 && o.AreaM2 <= 0) ? modelAreaM2 : o.AreaM2;
                options.Add(new LccOption
                {
                    Name = o.Name,
                    CapitalCost = o.CapitalCost,
                    AnnualEnergyCost = energy,
                    AnnualMaintCostPerM2 = o.AnnualMaintCostPerM2,
                    AreaM2 = area,
                    AnnualMaintCostFlat = o.AnnualMaintCostFlat,
                    Replacements = o.Replacements,
                });
            }

            var result = LifeCycleCostEngine.Compute(new LccInputs
            {
                HorizonYears = cfg.HorizonYears,
                EscalationPct = cfg.EscalationPct,
                DiscountPct = cfg.DiscountPct,
                Options = options,
            });

            string xlsx = WriteXlsx(doc, cfg, result);

            var sb = new StringBuilder();
            sb.AppendLine($"Horizon {cfg.HorizonYears} yr   escalation {cfg.EscalationPct}%   discount {cfg.DiscountPct}%");
            sb.AppendLine($"Tariff {cfg.TariffPerKwh}/kWh   model area {modelAreaM2:F0} m²   model peak {modelPeakW / 1000:F1} kW");
            sb.AppendLine();
            foreach (var o in result.Options)
                sb.AppendLine($"{o.Name}\n   40-yr nominal {o.TotalNominal:N0}   NPV {o.TotalNpv:N0}");
            sb.AppendLine();
            sb.AppendLine($"Crossover (nominal): {(result.CrossoverYearNominal > 0 ? $"year {result.CrossoverYearNominal}" : "none in horizon")}");
            sb.AppendLine($"Crossover (NPV):     {(result.CrossoverYearNpv > 0 ? $"year {result.CrossoverYearNpv}" : "none in horizon")}");
            sb.AppendLine();
            sb.AppendLine("The XLSX carries the year-by-year nominal + NPV columns — chart them in Excel for the");
            sb.AppendLine("A1 \"40-year by year financial comparison including graphs\" deliverable.");
            if (xlsx != null) { sb.AppendLine(); sb.AppendLine($"XLSX: {xlsx}"); }

            new TaskDialog("HVAC Life-Cycle Cost")
            {
                MainInstruction = $"{result.Options.Count} option(s) over {cfg.HorizonYears} years",
                MainContent = sb.ToString()
            }.Show();
            StingLog.Info($"Hvac_LifeCycleCompare: {result.Options.Count} options, crossover NPV {result.CrossoverYearNpv}");
            return Result.Succeeded;
        }

        private static LccDefaults LoadDefaults(Document doc)
        {
            // Overlay replaces wholesale when present, else corporate baseline.
            try
            {
                string dir = Path.GetDirectoryName(doc?.PathName ?? "");
                if (!string.IsNullOrEmpty(dir))
                {
                    string p = Path.Combine(dir, "_BIM_COORD", "hvac_lcc.json");
                    if (File.Exists(p))
                        return JsonConvert.DeserializeObject<LccDefaults>(File.ReadAllText(p)) ?? new LccDefaults();
                }
            }
            catch (Exception ex) { StingLog.Warn($"LCC overlay load: {ex.Message}"); }
            try
            {
                string c = StingToolsApp.FindDataFile("STING_HVAC_LCC_DEFAULTS.json");
                if (!string.IsNullOrEmpty(c) && File.Exists(c))
                    return JsonConvert.DeserializeObject<LccDefaults>(File.ReadAllText(c)) ?? new LccDefaults();
            }
            catch (Exception ex) { StingLog.Warn($"LCC corporate load: {ex.Message}"); }
            return new LccDefaults();
        }

        private static double ModelFloorAreaM2(Document doc)
        {
            double sqft = 0;
            try
            {
                foreach (var e in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType())
                {
                    var areaP = e.get_Parameter(BuiltInParameter.ROOM_AREA);
                    if (areaP != null) sqft += areaP.AsDouble();
                }
            }
            catch (Exception ex) { StingLog.Warn($"LCC model area: {ex.Message}"); }
            return sqft * SqFtToM2;
        }

        private static double SumSpacePeakSensibleW(Document doc)
        {
            double w = 0;
            try
            {
                foreach (var e in new FilteredElementCollector(doc).OfClass(typeof(Space)).WhereElementIsNotElementType())
                {
                    double v = ParameterHelpers.GetInt(e, "HVC_PEAK_SENS_W", 0);
                    if (v <= 0) { double.TryParse(ParameterHelpers.GetString(e, "HVC_PEAK_SENS_W"), out v); }
                    if (v > 0) w += v;
                }
            }
            catch (Exception ex) { StingLog.Warn($"LCC peak sum: {ex.Message}"); }
            return w;
        }

        private static string WriteXlsx(Document doc, LccDefaults cfg, LccResult result)
        {
            try
            {
                using var wb = new XLWorkbook();
                var sum = wb.AddWorksheet("Summary");
                string[] sh = { "Option", "Capital", "40yr Nominal", "40yr NPV" };
                for (int c = 0; c < sh.Length; c++) { sum.Cell(1, c + 1).Value = sh[c]; sum.Cell(1, c + 1).Style.Font.Bold = true; }
                int sr = 2;
                foreach (var o in result.Options)
                {
                    var cap = o.Years.FirstOrDefault()?.CapitalNominal ?? 0;
                    sum.Cell(sr, 1).Value = o.Name;
                    sum.Cell(sr, 2).Value = Math.Round(cap, 0);
                    sum.Cell(sr, 3).Value = Math.Round(o.TotalNominal, 0);
                    sum.Cell(sr, 4).Value = Math.Round(o.TotalNpv, 0);
                    sr++;
                }
                sum.Cell(sr + 1, 1).Value = "Horizon (yr)"; sum.Cell(sr + 1, 2).Value = cfg.HorizonYears;
                sum.Cell(sr + 2, 1).Value = "Escalation %"; sum.Cell(sr + 2, 2).Value = cfg.EscalationPct;
                sum.Cell(sr + 3, 1).Value = "Discount %"; sum.Cell(sr + 3, 2).Value = cfg.DiscountPct;
                sum.Cell(sr + 4, 1).Value = "Crossover yr (nominal)"; sum.Cell(sr + 4, 2).Value = result.CrossoverYearNominal;
                sum.Cell(sr + 5, 1).Value = "Crossover yr (NPV)"; sum.Cell(sr + 5, 2).Value = result.CrossoverYearNpv;
                sum.Columns().AdjustToContents();

                foreach (var o in result.Options)
                {
                    string name = SafeSheet(o.Name);
                    var ws = wb.AddWorksheet(name);
                    string[] h = { "Year", "Energy", "Maintenance", "Replacement", "Capital", "Year Nominal", "Cum Nominal", "Discount Factor", "Year NPV", "Cum NPV" };
                    for (int c = 0; c < h.Length; c++) { ws.Cell(1, c + 1).Value = h[c]; ws.Cell(1, c + 1).Style.Font.Bold = true; }
                    int r = 2;
                    foreach (var y in o.Years)
                    {
                        ws.Cell(r, 1).Value = y.Year;
                        ws.Cell(r, 2).Value = Math.Round(y.EnergyNominal, 0);
                        ws.Cell(r, 3).Value = Math.Round(y.MaintNominal, 0);
                        ws.Cell(r, 4).Value = Math.Round(y.ReplacementNominal, 0);
                        ws.Cell(r, 5).Value = Math.Round(y.CapitalNominal, 0);
                        ws.Cell(r, 6).Value = Math.Round(y.YearNominal, 0);
                        ws.Cell(r, 7).Value = Math.Round(y.CumulativeNominal, 0);
                        ws.Cell(r, 8).Value = Math.Round(y.DiscountFactor, 4);
                        ws.Cell(r, 9).Value = Math.Round(y.YearNpv, 0);
                        ws.Cell(r, 10).Value = Math.Round(y.CumulativeNpv, 0);
                        r++;
                    }
                    ws.Columns().AdjustToContents();
                }

                string path = OutputLocationHelper.GetOutputPath(doc, $"STING_HVAC_LCC_{DateTime.Now:yyyyMMdd}.xlsx");
                wb.SaveAs(path);
                return path;
            }
            catch (Exception ex) { StingLog.Warn($"LCC XLSX: {ex.Message}"); return null; }
        }

        private static string SafeSheet(string s)
        {
            s = (s ?? "Option").Replace(":", " ").Replace("/", " ").Replace("\\", " ")
                .Replace("?", " ").Replace("*", " ").Replace("[", "(").Replace("]", ")");
            if (s.Length > 31) s = s.Substring(0, 31);
            return s;
        }
    }
}
