// StingTools — Dual-source (generator / UPS) load-transfer validation.
//
// Validates that emergency generator and UPS capacity is sufficient to carry
// the declared emergency loads:
//   Generator:  sum of Emergency-feed panel loads ≤ generator kVA × 0.8 (80 % loading)
//   UPS:        sum of UPS-fed circuit loads ≤ UPS kVA rating
//
// Generator elements are identified by the ELC_GENERATOR_KVA parameter or a
// family name containing "Generator" or "Genset".
// UPS elements are identified by family name containing "UPS".
// Emergency panels are identified by ELC_FEED_TYPE_TXT = "Emergency" or "Both".
//
// Results are stamped on each generator / UPS element:
//   ELC_TRANSFER_LOAD_OK = "1" (passes) or "0" (fails).

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
    /// Validates that standby-generator and UPS ratings are sufficient to carry
    /// the emergency / UPS-backed loads in the model.  Stamps
    /// ELC_TRANSFER_LOAD_OK on every generator and UPS element found.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DualSourceValidationCommand : IExternalCommand
    {
        // Maximum recommended generator loading factor (80 %).
        private const double GeneratorLoadFactor = 0.80;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // ── Collect all electrical equipment once ────────────────────────
            var allEquipment = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .ToList();

            // ── Identify generators ──────────────────────────────────────────
            var generators = allEquipment
                .Where(fi =>
                {
                    try
                    {
                        if (fi.LookupParameter("ELC_GENERATOR_KVA") != null) return true;
                    }
                    catch { /* parameter absent */ }
                    string fname = (fi.Symbol?.FamilyName ?? "").ToUpperInvariant();
                    return fname.Contains("GENERATOR") || fname.Contains("GENSET");
                })
                .ToList();

            // ── Identify UPS units ───────────────────────────────────────────
            var upsList = allEquipment
                .Where(fi =>
                {
                    string fname = (fi.Symbol?.FamilyName ?? "").ToUpperInvariant();
                    string name  = (fi.Name ?? "").ToUpperInvariant();
                    return fname.Contains("UPS") || name.Contains("UPS");
                })
                .ToList();

            if (generators.Count == 0 && upsList.Count == 0)
            {
                TaskDialog.Show("STING Dual-Source Validation",
                    "No generators or UPS units found.\n\n" +
                    "Add the ELC_GENERATOR_KVA parameter to generator families, or use " +
                    "family names containing 'Generator', 'Genset', or 'UPS'.");
                return Result.Cancelled;
            }

            // ── Collect emergency panels (by ELC_FEED_TYPE_TXT) ──────────────
            var emergencyPanels = allEquipment
                .Where(fi =>
                {
                    try
                    {
                        string feedType = fi.LookupParameter("ELC_FEED_TYPE_TXT")?.AsString()?.Trim()
                                       ?? "";
                        return feedType.Equals("Emergency", StringComparison.OrdinalIgnoreCase)
                            || feedType.Equals("Both",      StringComparison.OrdinalIgnoreCase);
                    }
                    catch { return false; }
                })
                .ToList();

            // Sum apparent load (VA) on emergency panels from their downstream circuits.
            var allCircuits = new FilteredElementCollector(doc)
                .OfClass(typeof(ElectricalSystem))
                .Cast<ElectricalSystem>()
                .ToList();

            double totalEmergencyLoadVa = SumLoadVaForPanels(emergencyPanels, allCircuits);

            // ── Collect UPS-fed circuits ──────────────────────────────────────
            // UPS-fed circuits are identified by the ELC_UPS_FEED_BOOL parameter = 1
            // OR circuits whose panel is the UPS itself.
            double totalUpsLoadVa = 0.0;
            foreach (var ups in upsList)
            {
                double upsCircuitVa = allCircuits
                    .Where(es =>
                    {
                        try { return es.BaseEquipment?.Id == ups.Id; }
                        catch { return false; }
                    })
                    .Sum(es =>
                    {
                        try { return es.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD)?.AsDouble() ?? 0.0; }
                        catch { return 0.0; }
                    });
                totalUpsLoadVa += upsCircuitVa;
            }

            // ── Validate and stamp ────────────────────────────────────────────
            var violations = new List<string>();
            var passes     = new List<string>();

            using (var tx = new Transaction(doc, "STING Dual-Source Validation"))
            {
                tx.Start();

                // ── Generator checks ─────────────────────────────────────────
                foreach (var gen in generators)
                {
                    double genKva = 0.0;
                    try
                    {
                        var kvaParam = gen.LookupParameter("ELC_GENERATOR_KVA");
                        if (kvaParam != null) genKva = kvaParam.AsDouble();
                    }
                    catch (Exception ex) { StingLog.Warn($"DualSource gen kVA: {ex.Message}"); }

                    string genName = gen.Name ?? gen.Id.ToString();

                    if (genKva <= 0.0)
                    {
                        violations.Add(
                            $"WARNING  Generator [{genName}]: " +
                            "ELC_GENERATOR_KVA not set — cannot validate capacity.");
                        StampTransferOk(gen, "0");
                        continue;
                    }

                    double maxLoadVa   = genKva * 1000.0 * GeneratorLoadFactor;
                    double emergLoadVa = totalEmergencyLoadVa;

                    if (emergLoadVa > maxLoadVa)
                    {
                        violations.Add(
                            $"FAIL  Generator [{genName}]: " +
                            $"Emergency load {emergLoadVa / 1000.0:F1} kVA > " +
                            $"{maxLoadVa / 1000.0:F1} kVA ({GeneratorLoadFactor * 100:F0}% of {genKva:F0} kVA). " +
                            $"Emergency panels: {emergencyPanels.Count}.");
                        StampTransferOk(gen, "0");
                    }
                    else
                    {
                        passes.Add(
                            $"PASS  Generator [{genName}]: " +
                            $"Emergency load {emergLoadVa / 1000.0:F1} kVA ≤ " +
                            $"{maxLoadVa / 1000.0:F1} kVA limit.");
                        StampTransferOk(gen, "1");
                    }
                }

                // ── UPS checks ───────────────────────────────────────────────
                foreach (var ups in upsList)
                {
                    double upsKva = 0.0;
                    try
                    {
                        var kvaParam = ups.LookupParameter("ELC_UPS_KVA");
                        if (kvaParam != null) upsKva = kvaParam.AsDouble();
                    }
                    catch (Exception ex) { StingLog.Warn($"DualSource UPS kVA: {ex.Message}"); }

                    string upsName = ups.Name ?? ups.Id.ToString();

                    if (upsKva <= 0.0)
                    {
                        violations.Add(
                            $"WARNING  UPS [{upsName}]: " +
                            "ELC_UPS_KVA not set — cannot validate capacity.");
                        StampTransferOk(ups, "0");
                        continue;
                    }

                    double upsCircuitVa = allCircuits
                        .Where(es =>
                        {
                            try { return es.BaseEquipment?.Id == ups.Id; }
                            catch { return false; }
                        })
                        .Sum(es =>
                        {
                            try { return es.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD)?.AsDouble() ?? 0.0; }
                            catch { return 0.0; }
                        });

                    double upsLimitVa = upsKva * 1000.0;

                    if (upsCircuitVa > upsLimitVa)
                    {
                        violations.Add(
                            $"FAIL  UPS [{upsName}]: " +
                            $"UPS load {upsCircuitVa / 1000.0:F1} kVA > {upsLimitVa / 1000.0:F1} kVA rating.");
                        StampTransferOk(ups, "0");
                    }
                    else
                    {
                        passes.Add(
                            $"PASS  UPS [{upsName}]: " +
                            $"UPS load {upsCircuitVa / 1000.0:F1} kVA ≤ {upsLimitVa / 1000.0:F1} kVA rating.");
                        StampTransferOk(ups, "1");
                    }
                }

                tx.Commit();
            }

            try { ComplianceScan.InvalidateCache(); } catch { }

            // ── Report ───────────────────────────────────────────────────────
            string report =
                $"Dual-Source Load-Transfer Validation\n" +
                $"Generators: {generators.Count}   UPS units: {upsList.Count}   " +
                $"Emergency panels: {emergencyPanels.Count}\n" +
                $"Total emergency load: {totalEmergencyLoadVa / 1000.0:F1} kVA\n" +
                $"Passes: {passes.Count}   Violations: {violations.Count}\n\n";

            if (violations.Count > 0)
                report += "VIOLATIONS:\n" + string.Join("\n", violations.Take(20));
            else
                report += "All generator and UPS capacities cover their declared loads.";

            TaskDialog.Show("STING Dual-Source Validation", report);
            return Result.Succeeded;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Sums the apparent load (VA) for all circuits served by the given panels.
        /// </summary>
        private static double SumLoadVaForPanels(
            IEnumerable<FamilyInstance> panels,
            IEnumerable<ElectricalSystem> allCircuits)
        {
            var panelIds = new HashSet<ElementId>(panels.Select(p => p.Id));
            return allCircuits
                .Where(es =>
                {
                    try { return panelIds.Contains(es.BaseEquipment?.Id ?? ElementId.InvalidElementId); }
                    catch { return false; }
                })
                .Sum(es =>
                {
                    try { return es.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD)?.AsDouble() ?? 0.0; }
                    catch { return 0.0; }
                });
        }

        private static void StampTransferOk(Element el, string value)
        {
            try { ParameterHelpers.SetString(el, "ELC_TRANSFER_LOAD_OK", value, overwrite: true); }
            catch (Exception ex) { StingLog.Warn($"DualSource stamp: {ex.Message}"); }
        }
    }
}
