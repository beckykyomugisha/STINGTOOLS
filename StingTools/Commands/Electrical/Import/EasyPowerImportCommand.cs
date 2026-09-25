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

                int stamped = 0, notFound = 0, nothingWritten = 0, failedWrites = 0;
                int stampedByType = 0;
                var warnings = new List<string>();
                var byTypeMatches = new List<string>();

                using (var tx = new Transaction(doc, "STING EasyPower Import"))
                {
                    tx.Start();
                    var panelIndex = BuildPanelIndex(doc, out var typeNameKeys);
                    foreach (var rec in records)
                    {
                        if (panelIndex.TryGetValue(rec.BusName, out var panel))
                        {
                            // Counted only when at least one value actually landed.
                            int written = StampPanel(panel, rec, warnings, ref failedWrites);
                            if (typeNameKeys.Contains(rec.BusName))
                            {
                                // Matched only through the family TYPE name, which every
                                // board of that type shares: the values went onto the first
                                // such board, which may not be the bus the record means.
                                string pn = panel.get_Parameter(BuiltInParameter.RBS_ELEC_PANEL_NAME)?.AsString();
                                string line = $"'{rec.BusName}' -> board {panel.Id.Value}" +
                                              (string.IsNullOrEmpty(pn) ? "" : $" (Panel Name '{pn}')") +
                                              (written > 0 ? $", {written} value(s) written" : ", nothing written");
                                byTypeMatches.Add(line);
                                StingLog.Warn($"EasyPower import: matched by family type name — verify: {line}");
                                if (written > 0) stampedByType++;
                                else nothingWritten++;
                            }
                            else if (written > 0) stamped++;
                            else nothingWritten++;
                        }
                        else
                        {
                            notFound++;
                            if (notFound <= 5) warnings.Add($"Bus/panel not found: '{rec.BusName}'");
                        }
                    }
                    tx.Commit();
                }

                string report = $"Records: {records.Count}  Stamped: {stamped}  Unmatched: {notFound}" +
                                $"\nPanels matched but nothing written: {nothingWritten}  Failed writes: {failedWrites}";
                if (byTypeMatches.Count > 0)
                {
                    report += $"\n\nMatched by family type name — verify: {byTypeMatches.Count} " +
                              $"(values written on {stampedByType}; not counted as Stamped)." +
                              "\nThe record named a board type, not a Panel Name; the values went " +
                              "on the first board of that type:\n" +
                              string.Join("\n", byTypeMatches.Take(10));
                    if (byTypeMatches.Count > 10)
                        report += $"\n… and {byTypeMatches.Count - 10} more (see the STING log)";
                }
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

        /// <summary>Panel Name → board. <paramref name="typeNameKeys"/> holds the keys
        /// that resolved only through the family type name fallback, so a match on one
        /// can be reported as "verify" rather than as an ordinary stamp.</summary>
        private static Dictionary<string, FamilyInstance> BuildPanelIndex(Document doc, out HashSet<string> typeNameKeys)
        {
            var idx = new Dictionary<string, FamilyInstance>(StringComparer.OrdinalIgnoreCase);
            typeNameKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var boards = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .Cast<FamilyInstance>()
                .ToList();
            // Panel Name is the built-in RBS_ELEC_PANEL_NAME. LookupParameter("RBS_PANEL_NAME")
            // passed an enum name that no parameter carries, so it always returned null and
            // the index fell back to p.Name — the family TYPE name, shared by every board of
            // that type. Panel Name first; the type name only as a last resort.
            foreach (var p in boards)
            {
                string pn = p.get_Parameter(BuiltInParameter.RBS_ELEC_PANEL_NAME)?.AsString() ?? "";
                if (!string.IsNullOrEmpty(pn) && !idx.ContainsKey(pn)) idx[pn] = p;
            }
            foreach (var p in boards)
                if (!idx.ContainsKey(p.Name)) { idx[p.Name] = p; typeNameKeys.Add(p.Name); }
            return idx;
        }

        /// <summary>Returns how many values were written; failed writes are added to
        /// <paramref name="failed"/> and the warnings.</summary>
        private static int StampPanel(FamilyInstance p, EasyPowerRecord r, List<string> w, ref int failed)
        {
            int n = 0;
            // ELC_FAULT_LEVEL_KA / SLD_VD_PCT were never defined in MR_PARAMETERS.txt,
            // so nothing was written. The 3-phase fault at the bus goes where
            // FaultCurrent puts it and the SLD fault label reads it
            // (ELC_PNL_SHORT_CIRCUIT_RATING_KA); voltage drop to ELC_VLT_DROP_PCT;
            // the line-to-ground fault to ELC_PNL_FAULT_LG_KA (NUMBER, unitless kA).
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (r.FaultKa3Ph.HasValue)
                n += Tally(Set(p, "ELC_PNL_SHORT_CIRCUIT_RATING_KA", r.FaultKa3Ph.Value.ToString("F2", inv), w), ref failed);
            if (r.FaultKaLG.HasValue)
                n += Tally(Set(p, "ELC_PNL_FAULT_LG_KA", r.FaultKaLG.Value.ToString("F2", inv), w), ref failed);
            if (r.VdPct.HasValue)
                n += Tally(Set(p, "ELC_VLT_DROP_PCT", r.VdPct.Value.ToString("F1", inv), w), ref failed);
            else if (r.VoltagePU.HasValue)
            {
                // Convert pu to % drop for the SLD label.
                double vdPct = (1.0 - r.VoltagePU.Value) * 100.0;
                n += Tally(Set(p, "ELC_VLT_DROP_PCT", vdPct.ToString("F1", inv), w), ref failed);
            }
            return n;
        }

        /// <summary>1 for a write that landed, 0 otherwise; a failed write (false)
        /// is counted. null means there was nothing to write.</summary>
        private static int Tally(bool? result, ref int failed)
        {
            if (result == true) return 1;
            if (result == false) failed++;
            return 0;
        }

        /// <summary>Writes through ParameterHelpers.SetString, which also writes a
        /// unitless NUMBER parameter from its text; a failure is reported, not
        /// swallowed. Returns null when there was nothing to write, true when
        /// written, false on failure.</summary>
        private static bool? Set(Element el, string p, string v, List<string> w)
        {
            if (string.IsNullOrEmpty(v)) return null;
            var param = el.LookupParameter(p);
            if (param == null)
            {
                if (w.Count < 20) w.Add($"{p} is not bound on {el.Category?.Name} — run Load Params");
                return false;
            }
            if (ParameterHelpers.SetString(el, p, v, overwrite: true)) return true;
            if (w.Count < 20) w.Add($"{p}@{el.Name}: '{v}' was not written");
            return false;
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
