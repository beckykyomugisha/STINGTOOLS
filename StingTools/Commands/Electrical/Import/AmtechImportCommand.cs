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
            var doc = commandData.Application.ActiveUIDocument.Document;
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

                int stamped = 0, notFound = 0;
                var warnings = new List<string>();

                using (var tx = new Transaction(doc, "STING Amtech Import"))
                {
                    tx.Start();
                    var panelIndex = BuildPanelIndex(doc);
                    foreach (var rec in records)
                    {
                        if (panelIndex.TryGetValue(rec.PanelName, out var panel))
                        {
                            StampPanel(panel, rec, warnings);
                            stamped++;
                        }
                        else
                        {
                            notFound++;
                            if (notFound <= 5) warnings.Add($"Panel not found: '{rec.PanelName}'");
                        }
                        StampCircuits(doc, rec, warnings);
                    }
                    tx.Commit();
                }

                string report = $"Records: {records.Count}  Stamped: {stamped}  Unmatched: {notFound}";
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

        private static void StampPanel(FamilyInstance p, AmtechRecord r, List<string> w)
        {
            Set(p, "ELC_FAULT_LEVEL_KA",   r.FaultKa?.ToString("F2") ?? "", w);
            Set(p, "ELC_BUSBAR_RATING_TXT", r.BusbarRating.HasValue ? $"{r.BusbarRating:F0}A" : "", w);
            Set(p, "SLD_VD_PCT",            r.VoltageDrop?.ToString("F1") ?? "", w);
        }

        private static void StampCircuits(Document doc, AmtechRecord rec, List<string> w)
        {
            if (rec.Circuits.Count == 0) return;
            foreach (var sys in new FilteredElementCollector(doc)
                .OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>())
            {
                if (!string.Equals(sys.BaseEquipment?.Name, rec.PanelName, StringComparison.OrdinalIgnoreCase)) continue;
                var m = rec.Circuits.FirstOrDefault(c =>
                    string.Equals(c.Ref, sys.CircuitNumber, StringComparison.OrdinalIgnoreCase));
                if (m == null) continue;
                if (m.FaultKa.HasValue)    Set(sys, "ELC_FAULT_LEVEL_KA",    m.FaultKa.Value.ToString("F2"), w);
                if (!string.IsNullOrEmpty(m.CsaMm2)) Set(sys, "ELC_CABLE_CSA_MM2_TXT", m.CsaMm2, w);
                if (m.VoltageDrop.HasValue) Set(sys, "SLD_VD_PCT", m.VoltageDrop.Value.ToString("F1"), w);
            }
        }

        private static void Set(Element el, string p, string v, List<string> w)
        {
            if (string.IsNullOrEmpty(v)) return;
            var param = el.LookupParameter(p);
            if (param == null || param.IsReadOnly) return;
            try { param.Set(v); }
            catch (Exception ex) { if (w.Count < 20) w.Add($"{p}@{el.Name}: {ex.Message}"); }
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
