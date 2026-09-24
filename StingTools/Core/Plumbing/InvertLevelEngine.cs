// InvertLevelEngine — Phase 179d invert-level calculator.
//
// Computes the upstream / downstream bore invert of every drainage pipe
// through PipeInvert (the same calculation the drawing annotations use),
// and optionally writes them to PLM_DRN_INV_US_M / PLM_DRN_INV_DS_M.
//
// What changed, and why (2026-09-24):
//   * Nominal/2 → internal radius. Pipe.Diameter is the NOMINAL size.
//   * Endpoint 0 was assumed upstream; upstream is now the HIGHER end.
//   * A caller-supplied "datumMaOd" was added to internal-origin Z; the datum
//     is now IlReportingOptions (survey point by default).
//   * The four output parameters were defined in no parameter file, so the
//     "write-back" wrote nothing and said "N written". They are registered,
//     and a pipe with no bound parameter is COUNTED as unwritten.
//   * Cover depth was "-Z below the internal origin" — a placeholder that
//     produced plausible numbers from nothing. With no ground level in the
//     model there is no cover depth, so it is reported as unknown and the
//     cover parameters are left blank rather than filled with an invention.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;

namespace StingTools.Core.Plumbing
{
    public class InvertLevelRow
    {
        public ElementId PipeId    { get; set; }
        public string SystemName   { get; set; } = "";
        /// <summary>Internal diameter in mm (nominal when no internal diameter is known — see Source).</summary>
        public int    DnMm         { get; set; }
        public double UsInvertM    { get; set; }
        public double DsInvertM    { get; set; }
        public string Gradient     { get; set; }
        public InvertSource Source { get; set; }
        /// <summary>Null: no ground level is modelled, so cover cannot be stated.</summary>
        public double? CoverUsM    { get; set; }
        public double? CoverDsM    { get; set; }
        public string CoverStatus  { get; set; } = "UNKNOWN (no ground level)";
        public string Notes        { get; set; } = "";
    }

    public class InvertReport
    {
        public string DatumLabel      { get; set; } = "";
        public int PipesAnalysed      { get; set; }
        public int PipesWritten       { get; set; }
        /// <summary>Pipes the write-back could not reach because the parameters are not bound to them.</summary>
        public int PipesUnbound       { get; set; }
        public int NominalFallbacks   { get; set; }
        public int CoverViolations    { get; set; }
        public List<InvertLevelRow> Rows { get; } = new List<InvertLevelRow>();
        public List<string> Warnings  { get; } = new List<string>();
    }

    public static class InvertLevelEngine
    {
        public static InvertReport Calculate(Document doc, bool writeBack, IlReportingOptions opts = null)
        {
            opts = opts ?? IlReportingOptions.Default;
            var r = new InvertReport { DatumLabel = opts.DatumLabel };
            if (doc == null) return r;
            var pipes = new FilteredElementCollector(doc).OfClass(typeof(Pipe))
                .Cast<Pipe>().Where(IsDrainage).ToList();

            foreach (var p in pipes)
            {
                try
                {
                    var inv = PipeInvert.Compute(doc, p, opts, out var why);
                    if (inv == null) { r.Warnings.Add($"Pipe {p.Id}: no invert — {why}."); continue; }
                    var row = new InvertLevelRow
                    {
                        PipeId     = p.Id,
                        SystemName = p.MEPSystem?.Name ?? "",
                        DnMm       = (int)Math.Round(inv.InnerDiameterMm),
                        UsInvertM  = inv.UpInvertM,
                        DsInvertM  = inv.DownInvertM,
                        Gradient   = inv.Gradient ?? "level",
                        Source     = inv.Source,
                        Notes      = inv.CrossCheckNote ?? "",
                    };
                    if (inv.Source == InvertSource.NominalFallback) r.NominalFallbacks++;
                    r.Rows.Add(row);
                    r.PipesAnalysed++;

                    if (writeBack)
                    {
                        bool us = TryWriteText(p, ParamRegistry.PLM_DRN_INV_US, InvertMath.ToParamText(row.UsInvertM));
                        bool ds = TryWriteText(p, ParamRegistry.PLM_DRN_INV_DS, InvertMath.ToParamText(row.DsInvertM));
                        // Cover is unknown: clear any stale value rather than leave last run's invention.
                        TryWriteText(p, ParamRegistry.PLM_DRN_COVER_US, "");
                        TryWriteText(p, ParamRegistry.PLM_DRN_COVER_DS, "");
                        if (us && ds) r.PipesWritten++; else r.PipesUnbound++;
                    }
                }
                catch (Exception ex) { r.Warnings.Add($"Pipe {p.Id}: {ex.Message}"); }
            }

            if (r.PipesUnbound > 0)
                r.Warnings.Add($"{r.PipesUnbound} pipe(s) have no PLM_DRN_INV_US_M / _DS_M parameter bound — values not written. " +
                               "Run Load Shared Params, then re-run.");
            if (r.NominalFallbacks > 0)
                r.Warnings.Add($"{r.NominalFallbacks} pipe(s) have no internal diameter; their inverts use the NOMINAL size.");
            if (r.PipesAnalysed > 0)
                r.Warnings.Add("Cover depth not calculated: no ground level is modelled. Cover columns are blank, not zero.");
            return r;
        }

        private static bool IsDrainage(Pipe p)
        {
            var s = (p.MEPSystem?.Name ?? "").ToUpperInvariant();
            return s.Contains("SAN") || s.Contains("WASTE") || s.Contains("FOUL")
                || s.Contains("DRAIN") || s.Contains("STORM") || s.Contains("SOIL")
                || s.Contains("RAINWATER");
        }

        /// <summary>True only when the value was actually written.</summary>
        private static bool TryWriteText(Element el, string name, string v)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || p.IsReadOnly) return false;
                // Registered as TEXT (metres, 3 dp). A project that bound a same-named
                // number parameter is not written: metres into a Length parameter
                // would be read back as FEET. Counted as unwritten instead.
                if (p.StorageType == StorageType.String) return p.Set(v ?? "");
            }
            catch (Exception ex) { StingLog.Warn($"InvertLevelEngine write {name} on {el?.Id}: {ex.Message}"); }
            return false;
        }
    }
}
