// StingTools — IPS (Isolated Power System / medical IT system) validation.
//
// What this checks, and what it deliberately does NOT:
//
//  * Hazard current (NFPA 99 §7.2.2.3 / 5 mA; IEC 60364-7-710 insulation
//    monitoring) is a property of the system's LEAKAGE to earth, measured by the
//    Line Isolation Monitor. It is NOT a function of branch load VA. The previous
//    rule "5 mA × V = max VA per branch" (600 VA at 120 V) was dimensionally a
//    power but physically meaningless, so it has been removed. The model has no
//    leakage data; hazard current is reported as not model-checkable.
//  * IEC 60364-7-710.512.1.6: a medical IT transformer's rated output is between
//    0.5 kVA and 10 kVA. The connected load on one IPS panel is therefore checked
//    against 10 kVA — a documented, dimensionally sound design limit.
//  * NFPA 99 §7.2.2.3.3 / IEC 60364-7-710.531.3.1: an IPS needs a Line Isolation
//    Monitor / insulation monitoring device — checked from the LIM parameter.
//
// IPS panels: ELC_IPS_BOOL (MR_PARAMETERS) = Yes, or a family name containing
// "IPS" / "Isolated Power".

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
    /// Validates IPS panels: connected load vs the 10 kVA medical-IT transformer
    /// ceiling (IEC 60364-7-710.512.1.6) and presence of a Line Isolation Monitor.
    /// Hazard current is reported as not model-checkable. Stamps
    /// IPS_BRANCH_COMPLIANT = "N/A" on branch circuits (the old per-branch VA
    /// verdict was not a valid hazard-current test).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class IPSValidationCommand : IExternalCommand
    {
        // IEC 60364-7-710.512.1.6 — medical IT transformer rated output ceiling.
        private const double MaxItTransformerKva = 10.0;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // ── Locate IPS panels ────────────────────────────────────────────
            // An IPS panel is any electrical equipment element where either:
            //   • the shared parameter ELC_IPS_BOOL is Yes, or
            //   • the family name contains "IPS" or "Isolated Power".
            var ipsPanels = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Where(fi =>
                {
                    try
                    {
                        // ELC_IPS_BOOL ("Powered from Isolated Power System") is the defined
                        // flag. IPS_PANEL_BOOL was read as a fallback but is defined nowhere
                        // in MR_PARAMETERS.txt, so nothing ever bound or wrote it.
                        var p = fi.LookupParameter("ELC_IPS_BOOL");
                        if (p != null && p.StorageType == StorageType.Integer && p.AsInteger() == 1) return true;
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
                    "To flag a panel as IPS, set ELC_IPS_BOOL = Yes " +
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
                    string panelName = panel.Name ?? panel.Id.ToString();
                    var circuits = allCircuits
                        .Where(es =>
                        {
                            try { return es.BaseEquipment?.Id == panel.Id; }
                            catch { return false; }
                        })
                        .ToList();

                    // ── Connected load vs the 10 kVA medical-IT ceiling ─────
                    double panelVa = 0.0;
                    foreach (var circuit in circuits)
                    {
                        try { panelVa += StingTools.Core.Electrical.ElecUnits.Read(circuit, BuiltInParameter.RBS_ELEC_APPARENT_LOAD); }
                        catch (Exception ex2) { StingLog.Warn($"IPSValidate load: {ex2.Message}"); }
                        // The old per-branch PASS/FAIL was not a hazard-current test —
                        // overwrite it rather than leave a verdict nothing supports.
                        try { ParameterHelpers.SetString(circuit, "IPS_BRANCH_COMPLIANT", "N/A", overwrite: true); }
                        catch (Exception ex3) { StingLog.Warn($"IPSValidate stamp: {ex3.Message}"); }
                    }
                    double panelKva = panelVa / 1000.0;
                    if (circuits.Count == 0)
                        violations.Add($"WARNING  Panel {panelName}: no circuits — connected load not checked.");
                    else if (panelKva > MaxItTransformerKva)
                        violations.Add(
                            $"FAIL  Panel {panelName}: connected load {panelKva:F2} kVA > {MaxItTransformerKva:F0} kVA " +
                            "medical IT transformer maximum (IEC 60364-7-710.512.1.6) — split across IT systems.");
                    else
                        passes.Add($"PASS  Panel {panelName}: connected load {panelKva:F2} kVA ≤ {MaxItTransformerKva:F0} kVA " +
                                   $"({circuits.Count} circuit(s)).");

                    // ── Check for Line Isolation Monitor ─────────────────────
                    // NFPA 99 §7.2.2.3.3 requires a LIM on every IPS circuit.
                    bool limFound = false;
                    try
                    {
                        // ELC_IPS_LIM_BOOL is the STING parameter (bound by Load Params).
                        // LIM_INSTALLED_BOOL / IPS_LIM_BOOL are family-level names some
                        // vendor families carry, still honoured. Any one set to Yes is
                        // enough: the bound STING parameter exists (unticked) on every
                        // board, so it must not hide a family parameter that says Yes.
                        // ELC_IPS_BOOL says the board IS an IPS, not that it has a LIM,
                        // so it cannot stand in.
                        foreach (string limName in new[] { "ELC_IPS_LIM_BOOL", "LIM_INSTALLED_BOOL", "IPS_LIM_BOOL" })
                        {
                            var limParam = panel.LookupParameter(limName);
                            if (limParam != null && limParam.StorageType == StorageType.Integer && limParam.AsInteger() != 0)
                            { limFound = true; break; }
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"IPSValidation LIM read on {panel.Id}: {ex.Message}"); }

                    if (!limFound)
                        violations.Add(
                            $"WARNING  Panel {panel.Name ?? panel.Id.ToString()}: " +
                            "No Line Isolation Monitor recorded (ELC_IPS_LIM_BOOL not ticked; no LIM_INSTALLED_BOOL / IPS_LIM_BOOL family parameter set). " +
                            "NFPA 99 §7.2.2.3.3 requires a LIM on all IPS circuits.");
                }

                tx.Commit();
            }

            try { ComplianceScan.InvalidateCache(); } catch { }

            // ── Build report ─────────────────────────────────────────────────
            string report =
                $"IPS Validation — IEC 60364-7-710 / NFPA 99\n" +
                $"Panels checked: {ipsPanels.Count}   " +
                $"Passes: {passes.Count}   Violations: {violations.Count}\n\n" +
                "Hazard current (5 mA, NFPA 99 §7.2.2.3) is NOT checked: it depends on leakage to " +
                "earth, which the model does not hold. Verify it by LIM test at commissioning.\n\n";

            if (violations.Count > 0)
                report += "VIOLATIONS:\n" + string.Join("\n", violations.Take(20)) + "\n\n";
            if (passes.Count > 0)
                report += "PASSES:\n" + string.Join("\n", passes.Take(20));

            TaskDialog.Show("STING IPS Validation", report);
            return Result.Succeeded;
        }
    }
}
