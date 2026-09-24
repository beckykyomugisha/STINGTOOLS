// StingTools — Dual-source (generator / UPS) load-transfer validation.
//
// Validates that emergency generator and UPS capacity is sufficient to carry
// the declared emergency loads:
//   Generator:  sum of Emergency-feed panel loads ≤ generator kVA × 0.8 (80 % loading)
//   UPS:        sum of UPS-fed circuit loads ≤ UPS kVA rating
//
// Generator elements are identified by a kVA parameter or a family name
// containing "Generator" or "Genset". UPS elements by "UPS" in the name.
// Emergency panels: ELC_FEED_TYPE_TXT = "Emergency"/"Both", or a panel / family
// name matched by the shared emergency keyword list (EmergencyNameMatcher).
//
// NO STING SHARED PARAMETER CARRIES A GENERATOR OR UPS RATING: MR_PARAMETERS.txt
// has no *_KVA for either (ELC_GENERATOR_KVA / ELC_UPS_KVA were read but never
// defined, so every unit reported "not set"). The rating is looked up under the
// family-level names in GeneratorKvaNames / UpsKvaNames, on the instance then
// the type; when none exists the report says PARAMETER MISSING — it is never
// assumed.
//
// Results are stamped on each generator / UPS element:
//   ELC_TRANSFER_LOAD_OK = "1" (passes), "0" (fails), "N/A" (could not validate).

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

        // Family-level rating parameters, in priority order. A Double with an
        // ApparentPower spec is converted from internal units (ElecUnits); any other
        // value is taken as kVA only because its NAME says kVA.
        private static readonly string[] GeneratorKvaNames =
            { "ELC_GENERATOR_KVA", "Generator Rating (kVA)", "Rated Output (kVA)", "kVA Rating", "Rating kVA" };
        private static readonly string[] UpsKvaNames =
            { "ELC_UPS_KVA", "UPS Rating (kVA)", "Rated Output (kVA)", "kVA Rating", "Rating kVA" };

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
                        if (GeneratorKvaNames.Any(n => fi.LookupParameter(n) != null
                                                        && n.StartsWith("ELC_GENERATOR", StringComparison.Ordinal))) return true;
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
                    "Use family names containing 'Generator', 'Genset', or 'UPS'.");
                return Result.Cancelled;
            }

            // ── Collect emergency panels (by ELC_FEED_TYPE_TXT) ──────────────
            var emergencyKw = StingTools.Core.Electrical.EmergencyKeywordRegistry.ForDocument(doc);
            var emergencyPanels = allEquipment
                .Where(fi => !generators.Contains(fi) && !upsList.Contains(fi))
                .Where(fi =>
                {
                    try
                    {
                        // ELC_FEED_TYPE_TXT is a family-level parameter (not in MR_PARAMETERS).
                        string feedType = fi.LookupParameter("ELC_FEED_TYPE_TXT")?.AsString()?.Trim()
                                       ?? "";
                        if (feedType.Equals("Emergency", StringComparison.OrdinalIgnoreCase)
                            || feedType.Equals("Both",      StringComparison.OrdinalIgnoreCase)) return true;
                        return StingTools.Core.Electrical.EmergencyNameMatcher.IsEmergencyName(fi.Name, emergencyKw)
                            || StingTools.Core.Electrical.EmergencyNameMatcher.IsEmergencyName(fi.Symbol?.FamilyName, emergencyKw);
                    }
                    catch (Exception ex) { StingLog.Warn($"DualSource emergency panel {fi.Id}: {ex.Message}"); return false; }
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
                        try { return StingTools.Core.Electrical.ElecUnits.Read(es, BuiltInParameter.RBS_ELEC_APPARENT_LOAD); }
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
                    string genName = gen.Name ?? gen.Id.ToString();
                    double genKva = ReadKva(gen, GeneratorKvaNames, out string genSrc, out bool genParamExists);

                    if (genKva <= 0.0)
                    {
                        violations.Add(
                            $"NOT VALIDATED  Generator [{genName}]: " +
                            (genParamExists
                                ? $"rating parameter '{genSrc}' exists but is empty/0."
                                : "PARAMETER MISSING — no kVA rating parameter on instance or type " +
                                  $"(looked for: {string.Join(", ", GeneratorKvaNames)}). No STING shared parameter defines it."));
                        StampTransferOk(gen, "N/A");
                        continue;
                    }
                    if (emergencyPanels.Count == 0)
                    {
                        violations.Add(
                            $"NOT VALIDATED  Generator [{genName}] ({genKva:F0} kVA): no emergency panels identified " +
                            "(ELC_FEED_TYPE_TXT or an emergency keyword in the panel name) — a 0 kVA load is not a pass.");
                        StampTransferOk(gen, "N/A");
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
                    string upsName = ups.Name ?? ups.Id.ToString();
                    double upsKva = ReadKva(ups, UpsKvaNames, out string upsSrc, out bool upsParamExists);

                    if (upsKva <= 0.0)
                    {
                        violations.Add(
                            $"NOT VALIDATED  UPS [{upsName}]: " +
                            (upsParamExists
                                ? $"rating parameter '{upsSrc}' exists but is empty/0."
                                : "PARAMETER MISSING — no kVA rating parameter on instance or type " +
                                  $"(looked for: {string.Join(", ", UpsKvaNames)}). No STING shared parameter defines it."));
                        StampTransferOk(ups, "N/A");
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
                            try { return StingTools.Core.Electrical.ElecUnits.Read(es, BuiltInParameter.RBS_ELEC_APPARENT_LOAD); }
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
                $"Emergency panels: {emergencyPanels.Count} (ELC_FEED_TYPE_TXT or emergency keyword in name)\n" +
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
                    try { return StingTools.Core.Electrical.ElecUnits.Read(es, BuiltInParameter.RBS_ELEC_APPARENT_LOAD); }
                    catch { return 0.0; }
                });
        }

        /// <summary>
        /// Rating in kVA from the first of <paramref name="names"/> present on the
        /// instance, then the type. 0 when absent or empty — never assumed.
        /// </summary>
        private static double ReadKva(FamilyInstance fi, string[] names, out string source, out bool exists)
        {
            source = ""; exists = false;
            Element type = null;
            try { type = fi.Document.GetElement(fi.GetTypeId()); }
            catch (Exception ex) { StingLog.Warn($"DualSource type {fi.Id}: {ex.Message}"); }
            foreach (var host in new Element[] { fi, type })
            {
                if (host == null) continue;
                foreach (var n in names)
                {
                    Parameter p;
                    try { p = host.LookupParameter(n); } catch { continue; }
                    if (p == null) continue;
                    exists = true; source = n;
                    if (!p.HasValue) continue;
                    try
                    {
                        bool nameSaysKva = n.IndexOf("KVA", StringComparison.OrdinalIgnoreCase) >= 0;
                        switch (p.StorageType)
                        {
                            case StorageType.Double:
                                if (StingTools.Core.Electrical.ElecUnits.SiUnitFor(p) == UnitTypeId.VoltAmperes)
                                    return StingTools.Core.Electrical.ElecUnits.ToSi(p) / 1000.0;
                                if (nameSaysKva && p.AsDouble() > 0) return p.AsDouble();
                                break;
                            case StorageType.Integer:
                                if (nameSaysKva && p.AsInteger() > 0) return p.AsInteger();
                                break;
                            case StorageType.String:
                                if (nameSaysKva && double.TryParse((p.AsString() ?? "").Replace("kVA", "").Replace("KVA", "").Trim(),
                                        System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out double v) && v > 0)
                                    return v;
                                break;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"DualSource kVA '{n}' on {host.Id}: {ex.Message}"); }
                }
            }
            return 0;
        }

        private static void StampTransferOk(Element el, string value)
        {
            try { ParameterHelpers.SetString(el, "ELC_TRANSFER_LOAD_OK", value, overwrite: true); }
            catch (Exception ex) { StingLog.Warn($"DualSource stamp: {ex.Message}"); }
        }
    }
}
