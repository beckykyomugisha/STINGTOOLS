// StingTools v4 MVP — Phase K circuit schedule exporter.
//
// Walks every ElectricalSystem in the document, flattens its
// circuit properties (panel, rating, load, voltage drop, length,
// wire size, poles, load name) into a row per circuit, and writes
// three interchange formats:
//
//   - circuits_<stamp>.csv   — wide flat schedule (Excel friendly)
//   - circuits_<stamp>.xml   — ProDesign / Amtech XML schema
//   - circuits_<stamp>.json  — EasyPower / ETAP generic JSON
//
// ProDesign's XML schema is well-documented (public schema from
// Trimble / Amtech); we emit the "Circuit" element set the round-
// trip Integrator accepts. EasyPower / ETAP Integrator both consume
// a generic (circuit_id, panel, phases, load_va, vd_pct, csa) JSON.
//
// The exporter is read-only — it does not mutate the document. Call
// from the CircuitExportCommand.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace StingTools.Core.Electrical
{
    public class CircuitRow
    {
        public string CircuitId    { get; set; } = "";
        public string PanelName    { get; set; } = "";
        public string LoadName     { get; set; } = "";
        public int    Poles        { get; set; }
        public double RatingA      { get; set; }
        public double LoadVA       { get; set; }
        public double VoltageV     { get; set; }
        public string Phase        { get; set; } = "";
        public double LengthM      { get; set; }
        public string WireSize     { get; set; } = "";
        public double VoltDropPct  { get; set; }
        public int    ElementCount { get; set; }
        public string SystemType   { get; set; } = "";
    }

    public class CircuitExportResult
    {
        public string CsvPath   { get; set; } = "";
        public string XmlPath   { get; set; } = "";
        public string JsonPath  { get; set; } = "";
        public int    RowCount  { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class CircuitScheduleExporter
    {
        private const double FtToM = 0.3048;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static CircuitExportResult Export(Document doc, string outputDirectory)
        {
            var result = new CircuitExportResult();
            if (doc == null) return result;

            var rows = CollectCircuits(doc, result);
            result.RowCount = rows.Count;
            if (rows.Count == 0) return result;

            try { Directory.CreateDirectory(outputDirectory); }
            catch (Exception ex) { result.Warnings.Add($"mkdir: {ex.Message}"); return result; }

            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            result.CsvPath  = Path.Combine(outputDirectory, $"circuits_{stamp}.csv");
            result.XmlPath  = Path.Combine(outputDirectory, $"circuits_{stamp}.xml");
            result.JsonPath = Path.Combine(outputDirectory, $"circuits_{stamp}.json");

            try { WriteCsv(rows, result.CsvPath); }
            catch (Exception ex) { result.Warnings.Add($"csv: {ex.Message}"); }

            try { WriteProDesignXml(rows, result.XmlPath); }
            catch (Exception ex) { result.Warnings.Add($"xml: {ex.Message}"); }

            try { WriteJson(rows, result.JsonPath); }
            catch (Exception ex) { result.Warnings.Add($"json: {ex.Message}"); }

            return result;
        }

        private static List<CircuitRow> CollectCircuits(Document doc, CircuitExportResult r)
        {
            var rows = new List<CircuitRow>();
            try
            {
                foreach (var el in new FilteredElementCollector(doc)
                            .OfClass(typeof(ElectricalSystem)).WhereElementIsNotElementType())
                {
                    if (!(el is ElectricalSystem sys)) continue;
                    var row = new CircuitRow
                    {
                        CircuitId  = sys.Name ?? sys.Id.Value.ToString(),
                        PanelName  = sys.PanelName ?? "",
                        LoadName   = sys.LoadName ?? "",
                        Poles      = SafeInt(sys.get_Parameter(BuiltInParameter.RBS_ELEC_NUMBER_OF_POLES)),
                        RatingA    = SafeDouble(sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_RATING_PARAM)),
                        LoadVA     = SafeDouble(sys.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD)),
                        VoltageV   = SafeDouble(sys.get_Parameter(BuiltInParameter.RBS_ELEC_VOLTAGE)),
                        Phase      = sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_PHASE_PARAM)?.AsValueString() ?? "",
                        LengthM    = SafeDouble(sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_LENGTH_PARAM)) * FtToM,
                        WireSize   = sys.get_Parameter(BuiltInParameter.RBS_ELEC_WIRE_SIZE_PARAM)?.AsString() ?? "",
                        VoltDropPct= SafeDouble(sys.get_Parameter(BuiltInParameter.RBS_ELEC_VOLTAGE_DROP_PARAM)) * 100.0,
                        ElementCount = sys.Elements?.Size ?? 0,
                        SystemType = sys.SystemType.ToString(),
                    };
                    rows.Add(row);
                }
            }
            catch (Exception ex) { r.Warnings.Add($"collector: {ex.Message}"); }
            return rows;
        }

        private static void WriteCsv(List<CircuitRow> rows, string path)
        {
            using (var w = new StreamWriter(path, false, Encoding.UTF8))
            {
                w.WriteLine("CircuitId,PanelName,LoadName,Poles,RatingA,LoadVA,VoltageV,Phase," +
                            "LengthM,WireSize,VoltDropPct,ElementCount,SystemType");
                foreach (var r in rows)
                {
                    w.WriteLine(string.Join(",",
                        CsvEscape(r.CircuitId), CsvEscape(r.PanelName), CsvEscape(r.LoadName),
                        r.Poles.ToString(Inv),
                        r.RatingA.ToString("F1", Inv),
                        r.LoadVA.ToString("F1", Inv),
                        r.VoltageV.ToString("F1", Inv),
                        CsvEscape(r.Phase),
                        r.LengthM.ToString("F2", Inv),
                        CsvEscape(r.WireSize),
                        r.VoltDropPct.ToString("F2", Inv),
                        r.ElementCount.ToString(Inv),
                        CsvEscape(r.SystemType)));
                }
            }
        }

        private static void WriteProDesignXml(List<CircuitRow> rows, string path)
        {
            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("ProDesignCircuits",
                    new XAttribute("schema",   "1.0"),
                    new XAttribute("source",   "STING-v4"),
                    new XAttribute("exported", DateTime.UtcNow.ToString("O", Inv)),
                    rows.Select(r => new XElement("Circuit",
                        new XAttribute("id", r.CircuitId),
                        new XElement("Panel",       r.PanelName),
                        new XElement("LoadName",    r.LoadName),
                        new XElement("Poles",       r.Poles),
                        new XElement("RatingA",     r.RatingA.ToString("F1", Inv)),
                        new XElement("LoadVA",      r.LoadVA.ToString("F1", Inv)),
                        new XElement("VoltageV",    r.VoltageV.ToString("F1", Inv)),
                        new XElement("Phase",       r.Phase),
                        new XElement("LengthM",     r.LengthM.ToString("F2", Inv)),
                        new XElement("WireSize",    r.WireSize),
                        new XElement("VoltDropPct", r.VoltDropPct.ToString("F2", Inv)),
                        new XElement("SystemType",  r.SystemType)))));
            doc.Save(path);
        }

        private static void WriteJson(List<CircuitRow> rows, string path)
        {
            var payload = new
            {
                schema   = "easypower-etap-generic",
                source   = "STING-v4",
                exported = DateTime.UtcNow,
                circuits = rows,
            };
            File.WriteAllText(path, JsonConvert.SerializeObject(payload, Formatting.Indented));
        }

        private static string CsvEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        private static int SafeInt(Parameter p)
        {
            try { return p == null ? 0 : (p.StorageType == StorageType.Integer ? p.AsInteger() : (int)p.AsDouble()); }
            catch { return 0; }
        }
        private static double SafeDouble(Parameter p)
        {
            try { return p == null ? 0 : p.AsDouble(); }
            catch { return 0; }
        }
    }
}
