// Phase 179 — E1: Amtech ProDesign calculation import
// Parses Amtech ProDesign XML export files and seeds panel/circuit parameters
// (fault level, voltage drop, CSA, rating) back into Revit shared parameters.

using System;
using System.Collections.Generic;
using System.IO;
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
    public class AmtechImportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = ParameterHelpers.GetApp(commandData).ActiveUIDocument.Document;
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title  = "Select Amtech ProDesign Export File",
                    Filter = "Amtech XML (*.aml;*.xml)|*.aml;*.xml|All files (*.*)|*.*"
                };
                if (dlg.ShowDialog() != true) return Result.Cancelled;

                var records = ParseAmtechFile(dlg.FileName);
                if (records.Count == 0)
                {
                    TaskDialog.Show("Amtech Import", "No circuit data found in the selected file.");
                    return Result.Succeeded;
                }

                int stamped = 0, notFound = 0, nothingWritten = 0, failedWrites = 0, circuitWrites = 0;
                int stampedByType = 0;
                var warnings = new List<string>();
                var byTypeMatches = new List<string>();

                using (var tx = new Transaction(doc, "STING Amtech Import"))
                {
                    tx.Start();
                    var panelIndex = BuildPanelIndex(doc, out var typeNameKeys);
                    foreach (var rec in records)
                    {
                        if (panelIndex.TryGetValue(rec.PanelName, out var panel))
                        {
                            // A panel counts as stamped only when at least one value
                            // actually landed — every Set can fail (unbound, read-only).
                            int written = StampPanel(panel, rec, warnings, ref failedWrites);
                            if (typeNameKeys.Contains(rec.PanelName))
                            {
                                // Matched only through the family TYPE name, which every
                                // board of that type shares: the values went onto the first
                                // such board, which may not be the one the record means.
                                string pn = panel.get_Parameter(BuiltInParameter.RBS_ELEC_PANEL_NAME)?.AsString();
                                string line = $"'{rec.PanelName}' -> board {panel.Id.Value}" +
                                              (string.IsNullOrEmpty(pn) ? "" : $" (Panel Name '{pn}')") +
                                              (written > 0 ? $", {written} value(s) written" : ", nothing written");
                                byTypeMatches.Add(line);
                                StingLog.Warn($"Amtech import: matched by family type name — verify: {line}");
                                if (written > 0) stampedByType++;
                                else nothingWritten++;
                            }
                            else if (written > 0) stamped++;
                            else nothingWritten++;
                        }
                        else
                        {
                            notFound++;
                            if (notFound <= 5) warnings.Add($"Panel not found: '{rec.PanelName}'");
                        }
                        circuitWrites += StampCircuits(doc, rec, warnings, ref failedWrites);
                    }
                    tx.Commit();
                }

                string report = $"Records: {records.Count}  Stamped: {stamped}  Unmatched: {notFound}" +
                                $"\nPanels matched but nothing written: {nothingWritten}" +
                                $"\nCircuit values written: {circuitWrites}  Failed writes: {failedWrites}";
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
                TaskDialog.Show("Amtech Import", report);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("AmtechImportCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static List<AmtechRecord> ParseAmtechFile(string path)
        {
            var records = new List<AmtechRecord>();
            try
            {
                var xdoc = XDocument.Load(path);
                var nodes = xdoc.Descendants("Distribution").Concat(xdoc.Descendants("Board")).ToList();
                foreach (var el in nodes)
                {
                    var rec = new AmtechRecord
                    {
                        PanelName    = Attr(el, "Name") ?? Attr(el, "Reference") ?? "",
                        FaultKa      = ParseD(Attr(el, "FaultLevel") ?? Attr(el, "Icc")),
                        BusbarRating = ParseD(Attr(el, "BusbarRating") ?? Attr(el, "Rating")),
                        VoltageDrop  = ParseD(Attr(el, "VoltageDrop") ?? Attr(el, "Vd"))
                    };
                    foreach (var c in el.Descendants("Way").Concat(el.Descendants("Circuit")))
                    {
                        var cr = new AmtechCircuitRecord
                        {
                            Ref        = Attr(c, "Number") ?? Attr(c, "Reference") ?? "",
                            CsaMm2     = Attr(c, "CableSize") ?? Attr(c, "Csa") ?? "",
                            FaultKa    = ParseD(Attr(c, "FaultLevel") ?? Attr(c, "Icc")),
                            VoltageDrop= ParseD(Attr(c, "VoltageDrop") ?? Attr(c, "Vd"))
                        };
                        if (!string.IsNullOrEmpty(cr.Ref)) rec.Circuits.Add(cr);
                    }
                    if (!string.IsNullOrEmpty(rec.PanelName)) records.Add(rec);
                }
            }
            catch (Exception ex) { StingLog.Warn($"Amtech parse: {ex.Message}"); }
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

        // ELC_FAULT_LEVEL_KA, ELC_BUSBAR_RATING_TXT and SLD_VD_PCT were never defined
        // in MR_PARAMETERS.txt, so every import wrote nothing and still reported the
        // panel as stamped. These are the parameters the rest of STING reads:
        //   fault at the board   -> ELC_PNL_SHORT_CIRCUIT_RATING_KA (FaultCurrent
        //                           stamps it; the SLD fault label and fault
        //                           schedule read it)
        //   busbar rating (A)    -> ELC_BUSBAR_RATING_A
        //   voltage drop (%)     -> ELC_VLT_DROP_PCT (the ELC_CKT_VD_PCT alias)
        //   circuit fault level  -> ELC_CIR_FAULT_LEVEL_TXT (the SLD fault label's
        //                           first choice on a circuit)
        /// <summary>Returns how many values were written; failures are added to
        /// <paramref name="failed"/> and to the warnings.</summary>
        private static int StampPanel(FamilyInstance p, AmtechRecord r, List<string> w, ref int failed)
        {
            int n = 0;
            n += Tally(Set(p, "ELC_PNL_SHORT_CIRCUIT_RATING_KA", r.FaultKa?.ToString("F2", Inv) ?? "", w), ref failed);
            n += Tally(Set(p, "ELC_BUSBAR_RATING_A", r.BusbarRating.HasValue ? r.BusbarRating.Value.ToString("F0", Inv) : "", w), ref failed);
            n += Tally(Set(p, "ELC_VLT_DROP_PCT",    r.VoltageDrop?.ToString("F1", Inv) ?? "", w), ref failed);
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

        private static readonly System.Globalization.CultureInfo Inv =
            System.Globalization.CultureInfo.InvariantCulture;

        /// <summary>Returns how many circuit values were written.</summary>
        private static int StampCircuits(Document doc, AmtechRecord rec, List<string> w, ref int failed)
        {
            if (rec.Circuits.Count == 0) return 0;
            int n = 0;
            foreach (var sys in new FilteredElementCollector(doc)
                .OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>())
            {
                // sys.PanelName is the feeding board's Panel Name; BaseEquipment.Name
                // is that board's family TYPE name and never matched an export.
                if (!string.Equals(sys.PanelName, rec.PanelName, StringComparison.OrdinalIgnoreCase)) continue;
                var m = rec.Circuits.FirstOrDefault(c =>
                    string.Equals(c.Ref, sys.CircuitNumber, StringComparison.OrdinalIgnoreCase));
                if (m == null) continue;
                if (m.FaultKa.HasValue)    n += Tally(Set(sys, "ELC_CIR_FAULT_LEVEL_TXT", m.FaultKa.Value.ToString("F2", Inv), w), ref failed);
                if (!string.IsNullOrEmpty(m.CsaMm2)) n += Tally(Set(sys, "ELC_CABLE_CSA_MM2_TXT", m.CsaMm2, w), ref failed);
                if (m.VoltageDrop.HasValue) n += Tally(Set(sys, "ELC_VLT_DROP_PCT", m.VoltageDrop.Value.ToString("F1", Inv), w), ref failed);
            }
            return n;
        }

        /// <summary>Writes through ParameterHelpers.SetString, which also writes a
        /// unitless NUMBER parameter from its text. A parameter that is absent, read-only
        /// or refuses the value is reported, not skipped silently — Parameter.Set(string)
        /// on a NUMBER parameter just returned false, and nothing said so.
        /// Returns null when there was nothing to write, true when written, false on failure.</summary>
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

        private class AmtechRecord
        {
            public string PanelName    { get; set; } = "";
            public double? FaultKa     { get; set; }
            public double? BusbarRating{ get; set; }
            public double? VoltageDrop { get; set; }
            public List<AmtechCircuitRecord> Circuits { get; } = new List<AmtechCircuitRecord>();
        }
        private class AmtechCircuitRecord
        {
            public string Ref         { get; set; } = "";
            public string CsaMm2      { get; set; } = "";
            public double? FaultKa    { get; set; }
            public double? VoltageDrop{ get; set; }
        }
    }
}
