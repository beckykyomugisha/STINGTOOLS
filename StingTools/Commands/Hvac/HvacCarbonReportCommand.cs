// StingTools Phase 183 — HVAC plant embodied + refrigerant carbon.
//
// Closes gap C8: SustainabilityEngine.cs already covers building-fabric
// carbon via the ICE Database. HVAC plant is not represented separately
// — chiller / boiler / AHU / VRF embodied carbon plus refrigerant GWP
// is its own line item under EN 15978 module A1-A3 + B7.
//
// This command:
//   1. Walks OST_MechanicalEquipment in scope.
//   2. Reads capacity (kW) + manufacturer + product code + refrigerant
//      charge (kg) + refrigerant type from STING shared parameters.
//   3. Multiplies capacity by an embodied-carbon factor per equipment
//      class (chiller / boiler / AHU / FCU / VRF / heatpump).
//   4. Multiplies refrigerant charge by the GWP of the refrigerant.
//   5. Reports per-class breakdown + grand total + worst offenders.
//
// All factors are conservative defaults drawn from CIBSE TM65 (embodied
// carbon in MEP) and the IPCC AR6 GWP table; project-specific factors
// can be overlaid via Data/STING_HVAC_CARBON_FACTORS.json (auto-loaded
// if present) without touching code.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacCarbonReportCommand : IExternalCommand
    {
        // Embodied carbon factors (kgCO2e per kW capacity).
        // Source: CIBSE TM65 product-category defaults — used when no
        // manufacturer-specific EPD is available.
        private static readonly Dictionary<string, double> _embodiedKgCo2ePerKw =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "Chiller",     50.0 },
                { "Boiler",      30.0 },
                { "AHU",         25.0 },
                { "FCU",         20.0 },
                { "VRF",         60.0 },
                { "HeatPump",    55.0 },
                { "Fan",         15.0 },
                { "CoolingTower",45.0 },
                { "GENERIC",     35.0 }
            };

        // Refrigerant GWP (kgCO2e per kg refrigerant) — IPCC AR6 / Kigali.
        private static readonly Dictionary<string, double> _refrigerantGwp =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "R32",      675 },
                { "R290",       3 }, // propane — low GWP
                { "R410A",   2088 },
                { "R407C",   1774 },
                { "R134A",   1430 },
                { "R404A",   3922 },
                { "R454B",    466 },
                { "R513A",    631 },
                { "R744",       1 }, // CO2
                { "R717",       0 }, // ammonia
                { "R1234YF",    4 },
                { "R1234ZE",    7 }
            };

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;

                LoadProjectFactorOverlay();

                // Scope follows the HVAC panel radio.
                string scope = "Project";
                try { scope = StingHvacCommandHandler.CurrentScope ?? "Project"; } catch { }

                var eq = CollectEquipment(ctx, scope);
                if (eq.Count == 0)
                {
                    TaskDialog.Show("STING HVAC — Carbon report",
                        $"No mechanical equipment in scope ({scope}).");
                    return Result.Cancelled;
                }

                double totalEmbodied = 0;
                double totalRefrigerant = 0;
                var byClass = new Dictionary<string, (int count, double kw, double kgCo2)>();
                var topOffenders = new List<(string tag, double kgCo2, string detail)>();

                foreach (var e in eq)
                {
                    try
                    {
                        string klass = ClassifyEquipment(e);
                        double kw = ReadDouble(e, ParamRegistry.HVC_CAPACITY_KW);
                        if (kw <= 0) kw = ReadDouble(e, "ELC_LOAD_KW");
                        if (kw <= 0) continue;

                        double factor = _embodiedKgCo2ePerKw.TryGetValue(klass, out var f)
                            ? f : _embodiedKgCo2ePerKw["GENERIC"];
                        double embodied = kw * factor;
                        totalEmbodied += embodied;

                        double refrigKg  = ReadDouble(e, ParamRegistry.HVC_REFRIGERANT_KG_NR);
                        string refrigType = ParameterHelpers.GetString(e, ParamRegistry.HVC_REFRIGERANT_TYPE_TXT) ?? "";
                        double gwp = _refrigerantGwp.TryGetValue(refrigType, out var g) ? g : 0;
                        double refrigCo2 = refrigKg * gwp;
                        totalRefrigerant += refrigCo2;

                        if (!byClass.ContainsKey(klass)) byClass[klass] = (0, 0, 0);
                        var cur = byClass[klass];
                        byClass[klass] = (cur.count + 1, cur.kw + kw, cur.kgCo2 + embodied + refrigCo2);

                        double total = embodied + refrigCo2;
                        string tag = ParameterHelpers.GetString(e, "ASS_TAG_1");
                        if (string.IsNullOrEmpty(tag)) tag = $"#{e.Id.Value}";
                        topOffenders.Add((tag, total,
                            $"{klass} {kw:F1} kW × {factor:F0} = {embodied:F0}" +
                            (refrigKg > 0 ? $" + {refrigKg:F2} kg {refrigType} × {gwp} = {refrigCo2:F0}" : "")));
                    }
                    catch (Exception ex) { StingLog.Warn($"Carbon row {e.Id}: {ex.Message}"); }
                }

                topOffenders.Sort((a, b) => b.kgCo2.CompareTo(a.kgCo2));

                var panel = StingResultPanel.Create("HVAC — Plant Carbon Report (EN 15978 A1-A3 + B7)");
                panel.SetSubtitle($"{eq.Count} items in scope · CIBSE TM65 factors · IPCC AR6 GWP");
                panel.AddSection("TOTALS")
                     .Metric("Embodied (A1-A3)",  $"{totalEmbodied:F0} kgCO2e")
                     .Metric("Refrigerant (B7)",  $"{totalRefrigerant:F0} kgCO2e")
                     .Metric("Combined",          $"{(totalEmbodied + totalRefrigerant):F0} kgCO2e")
                     .Metric("Combined (tCO2e)",  $"{(totalEmbodied + totalRefrigerant) / 1000.0:F2}");

                if (byClass.Count > 0)
                {
                    panel.AddSection("BY CLASS");
                    foreach (var kv in byClass.OrderByDescending(k => k.Value.kgCo2))
                        panel.Metric(
                            $"{kv.Key} (×{kv.Value.count}, {kv.Value.kw:F0} kW)",
                            $"{kv.Value.kgCo2:F0} kgCO2e");
                }

                if (topOffenders.Count > 0)
                {
                    panel.AddSection("TOP OFFENDERS (first 15)");
                    foreach (var t in topOffenders.Take(15))
                        panel.Text($"{t.tag}: {t.kgCo2:F0} kgCO2e — {t.detail}");
                }

                panel.Text("Factors are CIBSE TM65 product-category defaults. Override per project " +
                           "by editing Data/STING_HVAC_CARBON_FACTORS.json (auto-loaded).");
                panel.Show();

                try
                {
                    StingHvacPanel.Instance?.PushRunRow(
                        $"Carbon report: {(totalEmbodied + totalRefrigerant) / 1000.0:F1} tCO2e",
                        "⬤");
                }
                catch (Exception ex) { StingLog.Warn($"Panel push: {ex.Message}"); }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacCarbonReportCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static void LoadProjectFactorOverlay()
        {
            try
            {
                string path = StingTools.Core.StingToolsApp.FindDataFile("STING_HVAC_CARBON_FACTORS.json");
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                var j = JObject.Parse(File.ReadAllText(path));

                var emb = j["embodiedKgCo2ePerKw"] as JObject;
                if (emb != null)
                    foreach (var kv in emb) _embodiedKgCo2ePerKw[kv.Key] = (double?)kv.Value ?? 0;

                var gwp = j["refrigerantGwp"] as JObject;
                if (gwp != null)
                    foreach (var kv in gwp) _refrigerantGwp[kv.Key] = (double?)kv.Value ?? 0;
            }
            catch (Exception ex) { StingLog.Warn($"Carbon factor overlay: {ex.Message}"); }
        }

        private static string ClassifyEquipment(Element e)
        {
            try
            {
                string family = "";
                if (e is FamilyInstance fi)
                    family = (fi.Symbol?.Family?.Name ?? "").ToUpperInvariant();
                string typeName = (e.Name ?? "").ToUpperInvariant();
                string prod = (ParameterHelpers.GetString(e, "ASS_PRODCT_COD_TXT") ?? "").ToUpperInvariant();

                string all = $"{family}|{typeName}|{prod}";
                if (all.Contains("CHILLER") || prod == "CH") return "Chiller";
                if (all.Contains("BOILER")  || prod == "BLR") return "Boiler";
                if (all.Contains("AHU")     || all.Contains("AIR HANDL")) return "AHU";
                if (all.Contains("FCU")     || all.Contains("FAN COIL")) return "FCU";
                if (all.Contains("VRF")     || all.Contains("VRV")) return "VRF";
                if (all.Contains("HEAT PUMP") || prod == "HP") return "HeatPump";
                if (all.Contains("COOLING TOWER")) return "CoolingTower";
                if (all.Contains("FAN")) return "Fan";
            }
            catch { }
            return "GENERIC";
        }

        private static List<Element> CollectEquipment(StingCommandContext ctx, string scope)
        {
            var doc = ctx.Doc;
            if (scope == "Selection")
            {
                var ids = ctx.UIDoc?.Selection?.GetElementIds();
                if (ids == null) return new List<Element>();
                return ids.Select(id => doc.GetElement(id))
                    .Where(e => e != null && e.Category != null
                             && (BuiltInCategory)e.Category.Id.Value == BuiltInCategory.OST_MechanicalEquipment)
                    .ToList();
            }
            if (scope == "ActiveView" && doc.ActiveView != null)
            {
                return new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                    .WhereElementIsNotElementType().ToList();
            }
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                .WhereElementIsNotElementType().ToList();
        }

        private static double ReadDouble(Element el, string param)
        {
            try
            {
                var p = el?.LookupParameter(param);
                if (p == null) return 0;
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                if (p.StorageType == StorageType.String &&
                    double.TryParse(p.AsString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double v)) return v;
            }
            catch { }
            return 0;
        }
    }
}
