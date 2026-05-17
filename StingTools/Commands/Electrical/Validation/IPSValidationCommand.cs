// StingTools — IPS (Isolated Power System) branch circuit load validation.
//
// NFPA 99 §7.2.2.3.1: each IPS branch circuit must not exceed 5 mA hazard
// current at the nominal system voltage.
//   120 V system: 5 mA × 120 V = 600 VA maximum load per branch
//   230 V system: 5 mA × 230 V = 1 150 VA maximum load per branch
//
// IPS panels are identified by IPS_PANEL_BOOL = 1 or a family name containing
// "IPS" or "Isolated Power".  The Line Isolation Monitor (LIM) requirement
// is enforced per NFPA 99 §7.2.2.3.3.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Validation
{
    /// <summary>
    /// Validates all IPS (Isolated Power System) branch circuits against the
    /// 5 mA hazard-current limit defined in NFPA 99 §7.2.2.3.1 and also
    /// checks for the mandatory Line Isolation Monitor per §7.2.2.3.3.
    /// Stamps IPS_BRANCH_COMPLIANT ("1"/"0") on every checked circuit and
    /// reports results in a TaskDialog.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class IPSValidationCommand : IExternalCommand
    {
        // NFPA 99 §7.2.2.3.1 — maximum hazard current per branch circuit (mA).
        private const double MaxHazardCurrentMa = 5.0;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // ── Locate IPS panels ────────────────────────────────────────────
            // An IPS panel is any electrical equipment element where either:
            //   • the custom shared parameter IPS_PANEL_BOOL is set to 1, or
            //   • the family name contains "IPS" or "Isolated Power".
            var ipsPanels = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Where(fi =>
                {
                    try
                    {
                        var p = fi.LookupParameter("IPS_PANEL_BOOL");
                        if (p != null && p.AsInteger() == 1) return true;
                    }
                    catch { /* parameter absent — proceed to name check */ }

                    string fname = (fi.Symbol?.FamilyName ?? "").ToUpperInvariant();
                    return fname.Contains("IPS") || fname.Contains("ISOLATED POWER");
                })
                .ToList();

            if (ipsPanels.Count == 0)
            {
                TaskDialog.Show("STING IPS Validation",
                    "No IPS panels found.\n\n" +
                    "To flag a panel as IPS, set parameter IPS_PANEL_BOOL = 1 " +
                    "or use a family whose name contains 'IPS' or 'Isolated Power'.");
                return Result.Cancelled;
            }

            // ── Build circuit list once for performance ─────────────────────
            var allCircuits = new FilteredElementCollector(doc)
                .OfClass(typeof(ElectricalSystem))
                .Cast<ElectricalSystem>()
                .ToList();

            var violations = new List<string>();
            var passes     = new List<string>();

            using (var tx = new Transaction(doc, "STING IPS Validation"))
            {
                tx.Start();

                foreach (var panel in ipsPanels)
                {
                    // ── Determine system voltage ─────────────────────────────
                    double systemVoltage = 230.0; // IEC default
                    try
                    {
                        var voltParam = panel.LookupParameter("RBS_ELEC_VOLTAGE_PARAM")
                            ?? panel.LookupParameter("Voltage");
                        if (voltParam != null && voltParam.AsDouble() > 0)
                            systemVoltage = voltParam.AsDouble();
                    }
                    catch (Exception ex) { StingLog.Warn($"IPSValidate voltage: {ex.Message}"); }

                    // Maximum permitted VA load per branch = 5 mA × V_system.
                    double maxVaPerBranch = MaxHazardCurrentMa / 1000.0 * systemVoltage * 1000.0;

                    // ── Check each downstream circuit ────────────────────────
                    var circuits = allCircuits
                        .Where(es =>
                        {
                            try { return es.BaseEquipment?.Id == panel.Id; }
                            catch { return false; }
                        })
                        .ToList();

                    foreach (var circuit in circuits)
                    {
                        double loadVa = 0.0;
                        try
                        {
                            var loadParam = circuit.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD);
                            if (loadParam != null) loadVa = loadParam.AsDouble();
                        }
                        catch (Exception ex) { StingLog.Warn($"IPSValidate load: {ex.Message}"); }

                        string circRef   = circuit.CircuitNumber ?? circuit.Id.ToString();
                        string panelName = panel.Name            ?? panel.Id.ToString();

                        if (loadVa > maxVaPerBranch)
                        {
                            violations.Add(
                                $"FAIL  Panel {panelName}  Circuit {circRef}: " +
                                $"{loadVa:F0} VA > {maxVaPerBranch:F0} VA limit " +
                                $"({MaxHazardCurrentMa} mA × {systemVoltage:F0} V)");
                            try { ParameterHelpers.SetString(circuit, "IPS_BRANCH_COMPLIANT", "0", overwrite: true); }
                            catch (Exception ex) { StingLog.Warn($"IPSValidate stamp fail: {ex.Message}"); }
                        }
                        else
                        {
                            passes.Add(
                                $"PASS  Panel {panelName}  Circuit {circRef}: {loadVa:F0} VA");
                            try { ParameterHelpers.SetString(circuit, "IPS_BRANCH_COMPLIANT", "1", overwrite: true); }
                            catch (Exception ex) { StingLog.Warn($"IPSValidate stamp pass: {ex.Message}"); }
                        }
                    }

                    // ── Check for Line Isolation Monitor ─────────────────────
                    // NFPA 99 §7.2.2.3.3 requires a LIM on every IPS circuit.
                    bool limFound = false;
                    try
                    {
                        var limParam = panel.LookupParameter("LIM_INSTALLED_BOOL")
                                    ?? panel.LookupParameter("IPS_LIM_BOOL");
                        limFound = limParam != null && limParam.AsInteger() != 0;
                    }
                    catch { /* parameter absent */ }

                    if (!limFound)
                        violations.Add(
                            $"WARNING  Panel {panel.Name ?? panel.Id.ToString()}: " +
                            "No Line Isolation Monitor (LIM) parameter set. " +
                            "NFPA 99 §7.2.2.3.3 requires a LIM on all IPS circuits.");
                }

                tx.Commit();
            }

            try { ComplianceScan.InvalidateCache(); } catch { }

            // ── Build report ─────────────────────────────────────────────────
            string report =
                $"IPS Validation — NFPA 99 §7.2.2.3.1\n" +
                $"Panels checked: {ipsPanels.Count}   " +
                $"Passes: {passes.Count}   Violations: {violations.Count}\n\n";

            if (violations.Count > 0)
                report += "VIOLATIONS:\n" + string.Join("\n", violations.Take(20));
            else
                report += "All IPS branch circuits comply with the 5 mA hazard-current limit.";

            TaskDialog.Show("STING IPS Validation", report);
            return Result.Succeeded;
        }
    }
}
