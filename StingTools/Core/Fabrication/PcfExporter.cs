using StingTools.Core;
// StingTools v4 MVP — PCF (Pipe Component File) exporter.
//
// Writes an Alias PCF that external ISO-6412 axonometric generators
// (Alias Isogen, Ez-ISO, SmartDraft, I-CONFIG) consume to produce
// production-ready isometric drawings with auto-ballooning, BOM,
// weld symbols, and cut lengths.
//
// PCF is a plain-text, whitespace-delimited format with a small, well-
// documented vocabulary. We only emit the subset that Isogen treats
// as "required": the ISOGEN-OPTIONS block, the PIPELINE-REFERENCE,
// and a sequence of component blocks (PIPE / ELBOW / TEE / FLANGE /
// VALVE / INSTRUMENT / WELD) with END-POSITION, CENTRE-POSITION and
// ITEM-CODE tags.
//
// Reference: Alias Systems Corp. "PCF Reference Manual" (public draft,
// ISOGEN Options book) — stable since 1998.
//
// Phase F ships the exporter; upgrading to a round-trip Isogen call
// (spawn the CLI with the generated PCF + run-directory) is trivial
// once Planscape hosts the Isogen license, and is NOT covered here.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
// Alias: the StingTools.Core.Fabrication.Pipe sub-namespace shadows
// Autodesk.Revit.DB.Plumbing.Pipe inside this file. Use the alias so
// WritePipe's parameter binds to the Revit class.
using RevitPipe = Autodesk.Revit.DB.Plumbing.Pipe;

namespace StingTools.Core.Fabrication
{
    public class PcfExportResult
    {
        public string OutputPath       { get; set; } = "";
        public int    ComponentCount   { get; set; }
        public int    PipeCount        { get; set; }
        public int    FittingCount     { get; set; }
        public int    ValveCount       { get; set; }
        public int    FlangeCount      { get; set; }
        public int    WeldCount        { get; set; }
        public List<string> Warnings   { get; } = new List<string>();
        public bool   Success          { get; set; }
    }

    public static class PcfExporter
    {
        private const double FtToMm = 304.8;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>
        /// Emit one PCF file per connected pipe network rooted in the
        /// supplied element ids. For now we write a single PCF whose
        /// PIPELINE-REFERENCE is derived from the first element's
        /// MEPSystem.Name — Phase F doesn't split multi-system
        /// selections; callers who need that should slice by system
        /// before calling.
        /// </summary>
        public static PcfExportResult Export(
            Document doc,
            IEnumerable<ElementId> elementIds,
            string outputDirectory,
            string pipelineName = null)
        {
            var result = new PcfExportResult();
            if (doc == null || elementIds == null) return result;

            var ids = elementIds.Where(x => x != null && x != ElementId.InvalidElementId).ToList();
            if (ids.Count == 0)
            {
                result.Warnings.Add("PcfExporter: empty id list");
                return result;
            }

            if (string.IsNullOrEmpty(outputDirectory))
                outputDirectory = Path.Combine(Path.GetTempPath(), "STING", "pcf");
            try { Directory.CreateDirectory(outputDirectory); }
            catch (Exception ex) { result.Warnings.Add($"mkdir: {ex.Message}"); return result; }

            // Resolve pipeline name.
            if (string.IsNullOrEmpty(pipelineName))
            {
                try
                {
                    var firstEl = doc.GetElement(ids[0]);
                    if (firstEl is RevitPipe p) pipelineName = p.MEPSystem?.Name ?? "UNKNOWN";
                    else pipelineName = "UNKNOWN";
                }
                catch { pipelineName = "UNKNOWN"; }
            }
            string sanitised = SanitisePipeline(pipelineName);
            string path = Path.Combine(outputDirectory,
                $"{sanitised}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.pcf");

            try
            {
                using (var w = new StreamWriter(path, false, Encoding.ASCII))
                {
                    WriteHeader(w, pipelineName);
                    foreach (var id in ids)
                    {
                        var el = doc.GetElement(id);
                        if (el == null) continue;
                        if (el is RevitPipe pipe)
                        {
                            WritePipe(w, pipe, result);
                        }
                        else if (el is FamilyInstance fi)
                        {
                            WriteFitting(w, fi, result);
                        }
                    }
                    WriteFooter(w);
                }
                result.OutputPath = path;
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"PcfExporter: write failed: {ex.Message}");
                StingLog.Error("PcfExporter", ex);
            }
            return result;
        }

        private static void WriteHeader(StreamWriter w, string pipelineName)
        {
            w.WriteLine("ISOGEN-FILES         ISOGEN.FLS");
            w.WriteLine("UNITS-BORE           MM");
            w.WriteLine("UNITS-CO-ORDS        MM");
            w.WriteLine("UNITS-WEIGHT         KGS");
            w.WriteLine("UNITS-BOLT-DIA       MM");
            w.WriteLine("UNITS-BOLT-LENGTH    MM");
            w.WriteLine();
            w.WriteLine($"PIPELINE-REFERENCE   {pipelineName}");
            w.WriteLine("    Pipeline-ReferenceClass    PIPELINE");
            w.WriteLine("    PROJECT-IDENTIFIER         STING_v4");
            w.WriteLine($"    DATE-DMY                   {DateTime.UtcNow:dd/MM/yyyy}");
            w.WriteLine();
        }

        private static void WriteFooter(StreamWriter w)
        {
            w.WriteLine();
            w.WriteLine("MATERIALS");
            w.WriteLine("    ITEM-CODE  CS-150");
            w.WriteLine("        DESCRIPTION  CARBON STEEL, CLASS 150");
            w.WriteLine();
        }

        private static void WritePipe(StreamWriter w, RevitPipe pipe, PcfExportResult result)
        {
            try
            {
                var curve = (pipe.Location as LocationCurve)?.Curve;
                if (curve == null) return;

                var p0 = curve.GetEndPoint(0);
                var p1 = curve.GetEndPoint(1);
                double boreMm = pipe.Diameter * FtToMm;
                double weightKg = 0;
                try { weightKg = SpoolWeightCalculator.WeightKg(pipe.Document, new[] { pipe.Id }); }
                catch (Exception wEx) { StingTools.Core.StingLog.Warn($"PcfExporter.WeightKg pipe {pipe?.Id}: {wEx.Message}"); }

                w.WriteLine("PIPE");
                w.WriteLine($"    END-POSITION       {FmtXYZ(p0)} {boreMm.ToString("F1", Inv)}");
                w.WriteLine($"    END-POSITION       {FmtXYZ(p1)} {boreMm.ToString("F1", Inv)}");
                w.WriteLine($"    ITEM-CODE          {ResolveItemCode(pipe, boreMm)}");
                w.WriteLine($"    UCI                {pipe.UniqueId}");
                if (weightKg > 0)
                    w.WriteLine($"    WEIGHT             {weightKg.ToString("F2", Inv)}");
                w.WriteLine();

                result.ComponentCount++;
                result.PipeCount++;
            }
            catch (Exception ex)
            { result.Warnings.Add($"PIPE {pipe?.Id}: {ex.Message}"); }
        }

        private static void WriteFitting(StreamWriter w, FamilyInstance fi, PcfExportResult result)
        {
            try
            {
                var lp = fi.Location as LocationPoint;
                if (lp == null) return;
                var p = lp.Point;

                // Classify the fitting by family-name keyword.
                string up = (fi.Symbol?.Family?.Name ?? fi.Name ?? "").ToUpperInvariant();
                string kind =
                      up.Contains("ELBOW") || up.Contains("BEND") ? "ELBOW"
                    : up.Contains("TEE")     ? "TEE"
                    : up.Contains("REDUCER") ? "REDUCER"
                    : up.Contains("COUPL")   ? "COUPLING"
                    : up.Contains("UNION")   ? "UNION"
                    : up.Contains("FLANGE")  ? "FLANGE"
                    : up.Contains("VALVE") || up.Contains("VLV") ? "VALVE"
                    : up.Contains("CAP")     ? "CAP"
                    : up.Contains("GAUGE") || up.Contains("INSTR") ? "INSTRUMENT"
                    : "MISC-COMPONENT";

                double boreMm = 0;
                try
                {
                    var conns = fi.MEPModel?.ConnectorManager?.Connectors;
                    if (conns != null)
                    {
                        foreach (Connector c in conns)
                        {
                            if (c == null) continue;
                            try
                            {
                                double d = (c.Shape == ConnectorProfileType.Round)
                                    ? c.Radius * 2 * FtToMm
                                    : Math.Max(c.Width, c.Height) * FtToMm;
                                if (d > boreMm) boreMm = d;
                            }
                            catch { }
                        }
                    }
                }
                catch { }
                if (boreMm <= 0) boreMm = 25.0;

                w.WriteLine(kind);
                w.WriteLine($"    CENTRE-POSITION    {FmtXYZ(p)}");
                // Add two END-POSITIONs derived from connectors if we
                // have them; Isogen needs these to draw the fitting in
                // axonometric orientation.
                try
                {
                    var conns = fi.MEPModel?.ConnectorManager?.Connectors;
                    if (conns != null)
                    {
                        int endCount = 0;
                        foreach (Connector c in conns)
                        {
                            if (endCount >= 2) break;
                            try
                            {
                                w.WriteLine($"    END-POSITION       {FmtXYZ(c.Origin)} {boreMm.ToString("F1", Inv)}");
                                endCount++;
                            }
                            catch { }
                        }
                    }
                }
                catch { }
                w.WriteLine($"    ITEM-CODE          {ResolveItemCode(fi, boreMm)}");
                w.WriteLine($"    UCI                {fi.UniqueId}");
                w.WriteLine();

                result.ComponentCount++;
                switch (kind)
                {
                    case "VALVE":  result.ValveCount++;   break;
                    case "FLANGE": result.FlangeCount++;  break;
                    default:       result.FittingCount++; break;
                }
            }
            catch (Exception ex)
            { result.Warnings.Add($"FITTING {fi?.Id}: {ex.Message}"); }
        }

        /// <summary>
        /// Derive an Isogen ITEM-CODE from the element. Prefer a
        /// parameter (ASS_ITEM_CODE_TXT) when set by the design team;
        /// fall back to "{CATEGORY}-{BORE_MM}".
        /// </summary>
        private static string ResolveItemCode(Element el, double boreMm)
        {
            try
            {
                var p = el.LookupParameter("ASS_ITEM_CODE_TXT");
                var val = p?.AsString();
                if (!string.IsNullOrEmpty(val)) return val;
            }
            catch { }
            string cat = el.Category?.Name?.Replace(' ', '-') ?? "UNKNOWN";
            return $"{cat.ToUpperInvariant()}-{boreMm:F0}";
        }

        private static string FmtXYZ(XYZ p)
        {
            double x = p.X * FtToMm;
            double y = p.Y * FtToMm;
            double z = p.Z * FtToMm;
            return $"{x.ToString("F2", Inv)} {y.ToString("F2", Inv)} {z.ToString("F2", Inv)}";
        }

        private static string SanitisePipeline(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "UNKNOWN";
            var sb = new StringBuilder();
            foreach (var c in raw.ToUpperInvariant())
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else if (char.IsWhiteSpace(c)) sb.Append('_');
            }
            return sb.Length > 0 ? sb.ToString() : "UNKNOWN";
        }
    }
}
