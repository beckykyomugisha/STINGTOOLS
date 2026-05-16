// Phase 179 — E3: Trimble MEP / Electra Cloud calculation import
// Parses Trimble Electrical CSV or XML export files and seeds circuit
// parameters (voltage drop, fault level, cable size, load) into Revit.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;

namespace StingTools.Commands.Electrical.Import
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TrimbleImportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title  = "Select Trimble Electrical Export File",
                    Filter = "CSV/XML (*.csv;*.xml)|*.csv;*.xml|All files (*.*)|*.*"
                };
                if (dlg.ShowDialog() != true) return Result.Cancelled;

                string path = dlg.FileName;
                List<TrimbleRecord> records;
                if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    records = ParseCsv(path);
                else
                    records = ParseXml(path);

                if (records.Count == 0)
                {
                    TaskDialog.Show("Trimble Import", "No circuit data found in the selected file.");
                    return Result.Succeeded;
                }

                int stamped = 0, notFound = 0;
                var warnings = new List<string>();

                using (var tx = new Transaction(doc, "STING Trimble Import"))
                {
                    tx.Start();
                    var circuitIndex = BuildCircuitIndex(doc);
                    foreach (var rec in records)
                    {
                        string key = MakeKey(rec.PanelName, rec.CircuitNumber);
                        if (circuitIndex.TryGetValue(key, out var sys))
                        {
                            StampCircuit(sys, rec, warnings);
                            stamped++;
                        }
                        else
                        {
                            notFound++;
                            if (notFound <= 5) warnings.Add($"Circuit not found: {key}");
                        }
                    }
                    tx.Commit();
                }

                string report = $"Records: {records.Count}  Stamped: {stamped}  Unmatched: {notFound}";
                if (warnings.Count > 0)
                    report += "\n\nWarnings:\n" + string.Join("\n", warnings.Take(10));
                TaskDialog.Show("Trimble Import", report);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("TrimbleImportCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── Parsers ──────────────────────────────────────────────────────────

        private static List<TrimbleRecord> ParseCsv(string path)
        {
            var records = new List<TrimbleRecord>();
            try
            {
                var lines = File.ReadAllLines(path);
                if (lines.Length < 2) return records;

                // Detect header row indices (case-insensitive).
                var headers = SplitCsv(lines[0]).Select(h => h.Trim().ToUpperInvariant()).ToArray();
                int iPanel  = Array.IndexOf(headers, "PANEL");
                int iCirc   = Array.IndexOf(headers, "CIRCUIT");
                int iCsa    = Array.FindIndex(headers, h => h.Contains("CSA") || h.Contains("CABLE SIZE"));
                int iFault  = Array.FindIndex(headers, h => h.Contains("FAULT") || h.Contains("ISC"));
                int iVd     = Array.FindIndex(headers, h => h.Contains("VD") || h.Contains("VOLT DROP"));
                int iLoad   = Array.FindIndex(headers, h => h.Contains("LOAD") || h.Contains("KVA") || h.Contains("KW"));
                int iRating = Array.FindIndex(headers, h => h.Contains("RATING") || h.Contains("CB "));

                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var cols = SplitCsv(lines[i]);
                    if (cols.Length == 0) continue;
                    string G(int idx) => idx >= 0 && idx < cols.Length ? cols[idx].Trim() : "";

                    var rec = new TrimbleRecord
                    {
                        PanelName     = G(iPanel),
                        CircuitNumber = G(iCirc),
                        CsaMm2        = G(iCsa),
                        FaultKa       = ParseD(G(iFault)),
                        VoltageDrop   = ParseD(G(iVd)),
                        LoadKVA       = ParseD(G(iLoad)),
                        Rating        = G(iRating)
                    };
                    if (!string.IsNullOrEmpty(rec.PanelName) || !string.IsNullOrEmpty(rec.CircuitNumber))
                        records.Add(rec);
                }
            }
            catch (Exception ex) { StingLog.Warn($"Trimble CSV parse: {ex.Message}"); }
            return records;
        }

        private static List<TrimbleRecord> ParseXml(string path)
        {
            var records = new List<TrimbleRecord>();
            try
            {
                var xdoc = XDocument.Load(path);
                foreach (var el in xdoc.Descendants("Circuit").Concat(xdoc.Descendants("Way")))
                {
                    var rec = new TrimbleRecord
                    {
                        PanelName     = Attr(el, "Panel") ?? Attr(el, "Board") ?? "",
                        CircuitNumber = Attr(el, "Number") ?? Attr(el, "Ref") ?? "",
                        CsaMm2        = Attr(el, "CableSize") ?? Attr(el, "Csa") ?? "",
                        FaultKa       = ParseD(Attr(el, "FaultLevel") ?? Attr(el, "Isc")),
                        VoltageDrop   = ParseD(Attr(el, "VoltageDrop") ?? Attr(el, "Vd")),
                        LoadKVA       = ParseD(Attr(el, "Load") ?? Attr(el, "kVA")),
                        Rating        = Attr(el, "Rating") ?? ""
                    };
                    records.Add(rec);
                }
            }
            catch (Exception ex) { StingLog.Warn($"Trimble XML parse: {ex.Message}"); }
            return records;
        }

        // ── Revit helpers ─────────────────────────────────────────────────────

        private static Dictionary<string, ElectricalSystem> BuildCircuitIndex(Document doc)
        {
            var idx = new Dictionary<string, ElectricalSystem>(StringComparer.OrdinalIgnoreCase);
            foreach (var sys in new FilteredElementCollector(doc)
                .OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>())
            {
                string key = MakeKey(sys.BaseEquipment?.Name ?? "", sys.CircuitNumber ?? "");
                if (!idx.ContainsKey(key)) idx[key] = sys;
            }
            return idx;
        }

        private static void StampCircuit(ElectricalSystem sys, TrimbleRecord r, List<string> w)
        {
            if (!string.IsNullOrEmpty(r.CsaMm2))   Set(sys, "ELC_CABLE_CSA_MM2_TXT", r.CsaMm2, w);
            if (r.FaultKa.HasValue)                  Set(sys, "ELC_FAULT_LEVEL_KA",   r.FaultKa.Value.ToString("F2"), w);
            if (r.VoltageDrop.HasValue)              Set(sys, "SLD_VD_PCT",            r.VoltageDrop.Value.ToString("F1"), w);
            if (!string.IsNullOrEmpty(r.Rating))    Set(sys, "ELC_CIRCUIT_RATING_TXT", r.Rating, w);
        }

        private static void Set(Element el, string p, string v, List<string> w)
        {
            if (string.IsNullOrEmpty(v)) return;
            var param = el.LookupParameter(p);
            if (param == null || param.IsReadOnly) return;
            try { param.Set(v); }
            catch (Exception ex) { if (w.Count < 20) w.Add($"{p}: {ex.Message}"); }
        }

        private static string MakeKey(string panel, string circuit) =>
            $"{panel}|{circuit}".ToUpperInvariant();

        private static string Attr(XElement el, string n) => el.Attribute(n)?.Value ?? el.Element(n)?.Value;
        private static double? ParseD(string s) =>
            double.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : (double?)null;

        private static string[] SplitCsv(string line)
        {
            // Basic CSV split respecting quoted fields.
            var result = new List<string>();
            bool inQuote = false;
            var cur = new System.Text.StringBuilder();
            foreach (char c in line)
            {
                if (c == '"') { inQuote = !inQuote; }
                else if (c == ',' && !inQuote) { result.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(c);
            }
            result.Add(cur.ToString());
            return result.ToArray();
        }

        private class TrimbleRecord
        {
            public string  PanelName     { get; set; } = "";
            public string  CircuitNumber { get; set; } = "";
            public string  CsaMm2        { get; set; } = "";
            public double? FaultKa       { get; set; }
            public double? VoltageDrop   { get; set; }
            public double? LoadKVA       { get; set; }
            public string  Rating        { get; set; } = "";
        }
    }
}
