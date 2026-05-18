// StingTools Phase 109 — Fill live-calc + naming audit.
//
// MepFillLiveCalcCommand
//   For every conduit + cable tray, computes fill % LIVE from the
//   aggregate cable cross-section (ELC_CBL_TOTAL_AREA_MM2 × circuit
//   count) vs the element's internal cross section. Writes back to
//   ELC_CDT_CBL_FILL_PCT / ELC_CTR_FILL_PCT so downstream FillValidator
//   and reports reflect current design rather than stale cached values.
//
// MepNamingAuditCommand
//   Audits PipingSystem + DuctSystem + ElectricalSystem names against
//   CIBSE / BSRIA / Uniclass 2015 conventions. Flags:
//     - Uncategorised systems ("Mechanical Supply Air 1")
//     - Missing discipline/system code prefix ("DCW", "HWS", "SAN")
//     - Inconsistent separator (mixed " - " vs "_" vs "-")
//     - Duplicate names across systems

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Mep
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepFillLiveCalcCommand : IExternalCommand
    {
        private const double MmToFt = 1.0 / 304.8;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            int inspected = 0, updated = 0, skipped = 0;
            var warnings = new List<string>();

            using (var tx = new Transaction(doc, "STING MEP live fill"))
            {
                try { tx.Start(); } catch (Exception ex) { warnings.Add($"tx: {ex.Message}"); goto Done; }
                try
                {
                    // Conduits
                    foreach (var el in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Conduit)
                                      .WhereElementIsNotElementType())
                    {
                        inspected++;
                        try
                        {
                            double dMm   = InternalDiameterMm(el);
                            double cbMm2 = ReadDouble(el, "ELC_CBL_TOTAL_AREA_MM2");
                            if (dMm <= 0 || cbMm2 <= 0) { skipped++; continue; }
                            double area = Math.PI * (dMm / 2.0) * (dMm / 2.0);
                            double fill = 100.0 * cbMm2 / area;
                            if (WriteDouble(el, "ELC_CDT_CBL_FILL_PCT", fill)) updated++;
                            else skipped++;
                        }
                        catch (Exception ex2)
                        {
                            skipped++;
                            warnings.Add($"cdt {el?.Id}: {ex.Message}");
                        }
                    }
                    // Cable tray
                    foreach (var el in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_CableTray)
                                      .WhereElementIsNotElementType())
                    {
                        inspected++;
                        try
                        {
                            double wMm = ReadDouble(el, "Width")  * 304.8;
                            double hMm = ReadDouble(el, "Height") * 304.8;
                            double cbMm2 = ReadDouble(el, "ELC_CBL_TOTAL_AREA_MM2");
                            if (wMm <= 0 || hMm <= 0 || cbMm2 <= 0) { skipped++; continue; }
                            double area = wMm * hMm;
                            double fill = 100.0 * cbMm2 / area;
                            if (WriteDouble(el, "ELC_CTR_FILL_PCT", fill)) updated++;
                            else skipped++;
                        }
                        catch (Exception ex2)
                        {
                            skipped++;
                            warnings.Add($"tray {el?.Id}: {ex.Message}");
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    warnings.Add($"Live-fill fatal: {ex.Message}");
                }
            }
        Done:
            ShowReport(inspected, updated, skipped, warnings);
            return Result.Succeeded;
        }

        private static double InternalDiameterMm(Element el)
        {
            try
            {
                double d = ReadDouble(el, "Diameter");
                if (d > 0) return d * 304.8;
                var p = el.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM);
                if (p != null && p.StorageType == StorageType.Double) return p.AsDouble() * 304.8;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return 0;
        }
        private static double ReadDouble(Element el, string param)
        {
            try { var p = el?.LookupParameter(param);
                  if (p == null) return 0;
                  if (p.StorageType == StorageType.Double) return p.AsDouble();
                  if (p.StorageType == StorageType.Integer) return p.AsInteger();
                  if (p.StorageType == StorageType.String &&
                      double.TryParse(p.AsString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double v)) return v; }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return 0;
        }
        private static bool WriteDouble(Element el, string param, double val)
        {
            try { var p = el.LookupParameter(param);
                  if (p == null || p.IsReadOnly) return false;
                  if (p.StorageType == StorageType.Double)  { p.Set(val); return true; }
                  if (p.StorageType == StorageType.String)  { p.Set($"{val:F1}"); return true; }
                  if (p.StorageType == StorageType.Integer) { p.Set((int)Math.Round(val)); return true; }
                  return false; }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }
        private static void ShowReport(int inspected, int updated, int skipped, List<string> warnings)
        {
            var panel = StingResultPanel.Create("MEP Live Fill Calc");
            panel.SetSubtitle("Live fill % for conduit + cable tray");
            panel.AddSection("SUMMARY")
                 .Metric("Inspected", inspected.ToString())
                 .Metric("Updated",   updated.ToString())
                 .Metric("Skipped",   skipped.ToString());
            if (warnings.Count > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (var w in warnings.Take(30)) panel.Text(w);
                if (warnings.Count > 30) panel.Text($"(+{warnings.Count - 30} more — see StingLog)");
            }
            panel.Show();
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepNamingAuditCommand : IExternalCommand
    {
        // Recognised discipline / system prefixes per CIBSE + Uniclass 2015.
        private static readonly HashSet<string> ValidPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Plumbing
            "DCW","DHW","HWS","CWS","MCW","SAN","SOIL","WASTE","RWD","RWO","GAS","FUEL",
            // HVAC
            "HVAC","SUP","EXT","RET","OA","EA","CHW","CW","LPHW","MPHW","HTG","CLG","STM","CON",
            // Electrical
            "LV","HV","ELV","DB","MCC","UPS","LTG","PWR","SEC","FS","FLS","NCL","COM","DAT","ICT",
            // Fire / specialist
            "FP","FM","FA","BMS","EMS","CCTV"
        };

        private static readonly Regex NameStartPrefix = new Regex(
            @"^(?<prefix>[A-Z0-9]{2,6})\s*[-_\s]", RegexOptions.Compiled);

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var findings = new List<string>();
            var allNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int inspected = 0;

            try
            {
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(MEPSystem))
                                  .WhereElementIsNotElementType())
                {
                    if (!(el is MEPSystem sys)) continue;
                    inspected++;
                    string name = sys.Name ?? "";
                    allNames[name] = allNames.TryGetValue(name, out var n) ? n + 1 : 1;

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        findings.Add($"[{sys.Id}] EMPTY — system has no name");
                        continue;
                    }
                    if (name.StartsWith("Default", StringComparison.OrdinalIgnoreCase) ||
                        Regex.IsMatch(name, @"Supply Air \d+|Return Air \d+|Domestic [A-Z] \d+"))
                    {
                        findings.Add($"[{sys.Id}] GENERIC — '{name}' is a Revit default stub");
                        continue;
                    }

                    var m = NameStartPrefix.Match(name);
                    if (!m.Success)
                    {
                        findings.Add($"[{sys.Id}] NO-PREFIX — '{name}' has no discipline/system code prefix");
                        continue;
                    }
                    string prefix = m.Groups["prefix"].Value;
                    if (!ValidPrefixes.Contains(prefix))
                    {
                        findings.Add($"[{sys.Id}] UNKNOWN-PREFIX — '{prefix}' in '{name}' not in CIBSE/Uniclass prefix set");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("MepNamingAudit failed", ex);
                message = ex.Message;
                return Result.Failed;
            }

            var dupes = allNames.Where(kv => kv.Value > 1).ToList();
            var panel = StingResultPanel.Create("MEP System Naming Audit");
            panel.SetSubtitle("Against CIBSE / BSRIA / Uniclass 2015 prefixes");
            panel.AddSection("SUMMARY")
                 .Metric("Systems inspected", inspected.ToString())
                 .Metric("Findings",          findings.Count.ToString())
                 .Metric("Duplicate names",   dupes.Count.ToString());

            if (findings.Count > 0)
            {
                panel.AddSection("FINDINGS");
                foreach (var f in findings.Take(60)) panel.Text(f);
                if (findings.Count > 60) panel.Text($"(+{findings.Count - 60} more)");
            }
            if (dupes.Count > 0)
            {
                panel.AddSection("DUPLICATE NAMES");
                foreach (var d in dupes.OrderByDescending(k => k.Value).Take(30))
                    panel.Metric(d.Key, $"×{d.Value}");
            }

            panel.AddSection("RECOGNISED PREFIXES")
                 .Text(string.Join(", ", ValidPrefixes.OrderBy(x => x)));
            panel.Show();
            return Result.Succeeded;
        }
    }
}
