// Phase 179 — E2: EasyPower XML calculation import
// Parses EasyPower XML result files and seeds fault level, voltage drop, and
// short-circuit data back into Revit panels and circuits.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Import
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class EasyPowerImportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = ParameterHelpers.GetApp(commandData).ActiveUIDocument.Document;
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title  = "Select EasyPower Export File",
                    Filter = "EasyPower XML (*.xml;*.epx)|*.xml;*.epx|All files (*.*)|*.*"
                };
                if (dlg.ShowDialog() != true) return Result.Cancelled;

                var records = ParseEasyPowerFile(dlg.FileName);
                if (records.Count == 0)
                {
                    TaskDialog.Show("EasyPower Import", "No panel data found in the selected file.");
                    return Result.Succeeded;
                }

                int stamped = 0, notFound = 0;
                var warnings = new List<string>();

                using (var tx = new Transaction(doc, "STING EasyPower Import"))
                {
                    tx.Start();
                    var panelIndex = BuildPanelIndex(doc);
                    foreach (var rec in records)
                    {
                        if (panelIndex.TryGetValue(rec.BusName, out var panel))
                        {
                            StampPanel(panel, rec, warnings);
                            stamped++;
                        }
                        else
                        {
                            notFound++;
                            if (notFound <= 5) warnings.Add($"Bus/panel not found: '{rec.BusName}'");
                        }
                    }
                    tx.Commit();
                }

                string report = $"Records: {records.Count}  Stamped: {stamped}  Unmatched: {notFound}";
                int lgCount = records.Count(r => r.FaultKaLG.HasValue);
                if (lgCount > 0)
                    report += $"\n\nLine-to-ground fault levels in the file ({lgCount}) were not stored: " +
                              "STING has no parameter for them yet (ROADMAP PARAM-3).";
                if (warnings.Count > 0)
                    report += "\n\nWarnings:\n" + string.Join("\n", warnings.Take(10));
                TaskDialog.Show("EasyPower Import", report);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("EasyPowerImportCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static List<EasyPowerRecord> ParseEasyPowerFile(string path)
        {
            var records = new List<EasyPowerRecord>();
            try
            {
                var xdoc = XDocument.Load(path);

                // EasyPower result files typically use <Bus> or <Node> elements.
                // Short-circuit results: <ShortCircuitResult BusName="..." 3PhFaultKA="..." LLFaultKA="..."/>
                // Load-flow results: <LoadFlowResult BusName="..." VoltagePU="..." VoltageDropPct="..."/>
                var scNodes = xdoc.Descendants("ShortCircuitResult")
                    .Concat(xdoc.Descendants("FaultResult"))
                    .Concat(xdoc.Descendants("Bus"))
                    .ToList();

                foreach (var el in scNodes)
                {
                    string name = Attr(el, "BusName") ?? Attr(el, "Name") ?? Attr(el, "Id") ?? "";
                    if (string.IsNullOrEmpty(name)) continue;

                    var rec = records.FirstOrDefault(r => string.Equals(r.BusName, name, StringComparison.OrdinalIgnoreCase));
                    if (rec == null) { rec = new EasyPowerRecord { BusName = name }; records.Add(rec); }

                    // Pull fault level values (3-phase symmetrical in kA).
                    if (!rec.FaultKa3Ph.HasValue) rec.FaultKa3Ph = ParseD(Attr(el, "3PhFaultKA") ?? Attr(el, "Sym3Ph_kA") ?? Attr(el, "FaultKA"));
                    if (!rec.FaultKaLG.HasValue)  rec.FaultKaLG  = ParseD(Attr(el, "LGFaultKA")  ?? Attr(el, "LG_kA"));
                    if (!rec.VoltagePU.HasValue)   rec.VoltagePU  = ParseD(Attr(el, "VoltagePU")  ?? Attr(el, "Voltage_PU"));
                    if (!rec.VdPct.HasValue)       rec.VdPct      = ParseD(Attr(el, "VoltageDropPct") ?? Attr(el, "VD_Pct"));
                }
            }
            catch (Exception ex) { StingLog.Warn($"EasyPower parse: {ex.Message}"); }
            return records;
        }

        private static Dictionary<string, FamilyInstance> BuildPanelIndex(Document doc)
        {
            var idx = new Dictionary<string, FamilyInstance>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .Cast<FamilyInstance>())
            {
                if (!idx.ContainsKey(p.Name)) idx[p.Name] = p;
                string pn = p.LookupParameter("RBS_PANEL_NAME")?.AsString() ?? "";
                if (!string.IsNullOrEmpty(pn) && !idx.ContainsKey(pn)) idx[pn] = p;
            }
            return idx;
        }

        private static void StampPanel(FamilyInstance p, EasyPowerRecord r, List<string> w)
        {
            // ELC_FAULT_LEVEL_KA / SLD_VD_PCT were never defined in MR_PARAMETERS.txt,
            // so nothing was written. The 3-phase fault at the bus goes where
            // FaultCurrent puts it and the SLD fault label reads it
            // (ELC_PNL_SHORT_CIRCUIT_RATING_KA); voltage drop to ELC_VLT_DROP_PCT.
            // The line-to-ground fault has no STING parameter and is reported, not
            // written (see Execute).
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (r.FaultKa3Ph.HasValue)
                Set(p, "ELC_PNL_SHORT_CIRCUIT_RATING_KA", r.FaultKa3Ph.Value.ToString("F2", inv), w);
            if (r.VdPct.HasValue)
                Set(p, "ELC_VLT_DROP_PCT", r.VdPct.Value.ToString("F1", inv), w);
            else if (r.VoltagePU.HasValue)
            {
                // Convert pu to % drop for the SLD label.
                double vdPct = (1.0 - r.VoltagePU.Value) * 100.0;
                Set(p, "ELC_VLT_DROP_PCT", vdPct.ToString("F1", inv), w);
            }
        }

        /// <summary>Writes through ParameterHelpers.SetString, which also writes a
        /// unitless NUMBER parameter from its text; a failure is reported, not
        /// swallowed.</summary>
        private static void Set(Element el, string p, string v, List<string> w)
        {
            if (string.IsNullOrEmpty(v)) return;
            var param = el.LookupParameter(p);
            if (param == null)
            {
                if (w.Count < 20) w.Add($"{p} is not bound on {el.Category?.Name} — run Load Params");
                return;
            }
            if (!ParameterHelpers.SetString(el, p, v, overwrite: true) && w.Count < 20)
                w.Add($"{p}@{el.Name}: '{v}' was not written");
        }

        private static string Attr(XElement el, string n) => el.Attribute(n)?.Value ?? el.Element(n)?.Value;
        private static double? ParseD(string s) =>
            double.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : (double?)null;

        private class EasyPowerRecord
        {
            public string  BusName    { get; set; } = "";
            public double? FaultKa3Ph { get; set; }
            public double? FaultKaLG  { get; set; }
            public double? VoltagePU  { get; set; }
            public double? VdPct      { get; set; }
        }
    }
}
