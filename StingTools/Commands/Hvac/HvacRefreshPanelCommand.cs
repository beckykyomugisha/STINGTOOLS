// StingTools Phase 188 (Tier 2 / gap G1 + F2) — refresh + scrape commands
// for the STING HVAC dock panel.
//
//   HvacRefreshPanelCommand
//     Walks the current document and re-seeds every grid on the HVAC panel
//     (EQPT / SYS / LOADS / FAB / RPRT-drift). Read-only; idempotent.
//
//   HvacScrapeEquipmentParamsCommand
//     Closes F2 — equipment carbon params (HVC_CAPACITY_KW,
//     HVC_REFRIGERANT_TYPE_TXT, HVC_REFRIGERANT_KG_NR) were write-only
//     until now (HvacCarbonReportCommand reads them but nothing wrote
//     them). This command walks Mechanical Equipment + Air Terminals,
//     infers capacity from connected MEP system flow + built-in cooling
//     capacity, infers refrigerant type from family name regex, and
//     stamps the params so a carbon report has something to read.
//     Refrigerant CHARGE (kg) cannot be derived from the model and is
//     left to manufacturer-spec entry — flagged with a warning.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacRefreshPanelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var panel = StingHvacPanel.Instance;
                if (panel == null)
                {
                    TaskDialog.Show("STING HVAC — Refresh",
                        "HVAC panel not open. Toggle via Ribbon → ❄ HVAC → STING HVAC.");
                    return Result.Cancelled;
                }
                panel.RefreshFromDoc(ctx.Doc);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacRefreshPanelCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacScrapeEquipmentParamsCommand : IExternalCommand
    {
        // Family-name pattern → refrigerant type id from STING_HVAC_CARBON_FACTORS.json.
        // First match wins. Capture-letter case is normalised before lookup.
        private static readonly (string Pattern, string Refrigerant)[] _refrigerantPatterns =
        {
            ("R1234YF",   "R1234YF"),
            ("R1234ZE",   "R1234ZE"),
            ("R454B",     "R454B"),
            ("R513A",     "R513A"),
            ("R410A",     "R410A"),
            ("R407C",     "R407C"),
            ("R134A",     "R134A"),
            ("R404A",     "R404A"),
            ("R32",       "R32"),
            ("R290",      "R290"),
            ("R744",      "R744"),
            ("R717",      "R717"),
            ("PROPANE",   "R290"),
            ("CO2",       "R744"),
            ("AMMONIA",   "R717")
        };

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;

                int inspected = 0, capacityWrites = 0, refrigWrites = 0, skipped = 0;
                var sample = new List<string>();

                var eq = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                    .WhereElementIsNotElementType().ToList();

                using (var tx = new Transaction(doc, "STING Scrape HVAC equipment params"))
                {
                    tx.Start();
                    foreach (var e in eq)
                    {
                        inspected++;
                        try
                        {
                            // ── Capacity (kW) ────────────────────────────
                            // Already set? Skip. HVC_CAPACITY_KW is TEXT-storage
                            // (Revit shared-param convention) so we check empty
                            // string rather than parsing zero.
                            string capExisting = ParameterHelpers.GetString(e, "HVC_CAPACITY_KW");
                            if (string.IsNullOrEmpty(capExisting))
                            {
                                double kw = InferCapacityKw(e);
                                if (kw > 0)
                                {
                                    string kwStr = kw.ToString("F1",
                                        System.Globalization.CultureInfo.InvariantCulture);
                                    if (ParameterHelpers.SetString(e, "HVC_CAPACITY_KW",
                                        kwStr, overwrite: false))
                                    {
                                        capacityWrites++;
                                    }
                                }
                            }

                            // ── Refrigerant type ────────────────────────
                            string refrigExisting = ParameterHelpers.GetString(e, "HVC_REFRIGERANT_TYPE_TXT");
                            if (string.IsNullOrEmpty(refrigExisting))
                            {
                                string family = (e is FamilyInstance fi)
                                    ? (fi.Symbol?.Family?.Name ?? "")
                                    : "";
                                string typeName = e.Name ?? "";
                                string detected = DetectRefrigerant($"{family}|{typeName}");
                                if (!string.IsNullOrEmpty(detected))
                                {
                                    if (ParameterHelpers.SetString(e, "HVC_REFRIGERANT_TYPE_TXT",
                                        detected, overwrite: false))
                                    {
                                        refrigWrites++;
                                        if (sample.Count < 15)
                                            sample.Add($"#{e.Id.Value} {family} → {detected}");
                                    }
                                }
                            }
                        }
                        catch (Exception exE)
                        {
                            skipped++;
                            StingLog.Warn($"Scrape row {e?.Id}: {exE.Message}");
                        }
                    }
                    tx.Commit();
                }

                var panel = StingResultPanel.Create("HVAC — Scrape Equipment Params");
                panel.SetSubtitle($"{inspected} mechanical-equipment elements inspected");
                panel.AddSection("RESULTS")
                     .Metric("Capacity (kW) writes",  capacityWrites.ToString())
                     .Metric("Refrigerant writes",    refrigWrites.ToString())
                     .Metric("Skipped (errors)",      skipped.ToString());

                if (sample.Count > 0)
                {
                    panel.AddSection("REFRIGERANT MATCHES (first 15)");
                    foreach (var s in sample) panel.Text(s);
                }
                panel.Text("Refrigerant CHARGE mass (HVC_REFRIGERANT_KG_NR) cannot be derived " +
                           "from the model and must be entered from manufacturer datasheets " +
                           "before HvacCarbonReportCommand can produce accurate refrigerant GWP totals.");
                panel.Show();

                try
                {
                    StingHvacPanel.Instance?.PushRunRow(
                        $"Scrape equipment params (+{capacityWrites} kW, +{refrigWrites} refrig)",
                        (capacityWrites + refrigWrites) > 0 ? "⬤" : "⬡");
                    // Refresh equipment grid so the new values show up live.
                    StingHvacPanel.Instance?.RefreshFromDoc(doc);
                }
                catch (Exception ex) { StingLog.Warn($"Panel push: {ex.Message}"); }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacScrapeEquipmentParamsCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Capacity inference order:
        ///   1. Revit built-in Cooling/Heating Design Load type parameters.
        ///   2. Sum of connected supply-air flow × ΔT × ρ × Cp (sensible only).
        ///   3. Zero (skip).
        /// </summary>
        private static double InferCapacityKw(Element e)
        {
            try
            {
                // Built-in cooling design load (W) on family type, if present.
                var bipC = e.get_Parameter(BuiltInParameter.ROOM_DESIGN_COOLING_LOAD_PARAM);
                if (bipC != null && bipC.StorageType == StorageType.Double)
                {
                    double w = bipC.AsDouble();
                    if (w > 0) return w / 1000.0;
                }
                var bipH = e.get_Parameter(BuiltInParameter.ROOM_DESIGN_HEATING_LOAD_PARAM);
                if (bipH != null && bipH.StorageType == StorageType.Double)
                {
                    double w = bipH.AsDouble();
                    if (w > 0) return w / 1000.0;
                }

                // Sum the supply-side connector flows × sensible heat factor.
                if (e is FamilyInstance fi && fi.MEPModel?.ConnectorManager?.Connectors != null)
                {
                    double cfm = 0;
                    foreach (Connector c in fi.MEPModel.ConnectorManager.Connectors)
                    {
                        try
                        {
                            if (c?.Domain == Domain.DomainHvac && c.Direction == FlowDirectionType.Out)
                                cfm += c.Flow;
                        }
                        catch { }
                    }
                    if (cfm > 0)
                    {
                        // 1 cfm ≈ 1.08 BTU/h per °F. Assuming ΔT = 20 °F → 21.6 BTU/h/cfm.
                        // 21.6 BTU/h × cfm / 3412.14 ≈ kW. Conservative default.
                        return cfm * 21.6 / 3412.14;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"InferCapacityKw: {ex.Message}"); }
            return 0;
        }

        private static string DetectRefrigerant(string haystack)
        {
            if (string.IsNullOrEmpty(haystack)) return "";
            string hayU = haystack.ToUpperInvariant();
            foreach (var (pattern, refrigerant) in _refrigerantPatterns)
            {
                if (hayU.Contains(pattern)) return refrigerant;
            }
            return "";
        }
    }
}
