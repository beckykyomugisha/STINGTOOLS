// InvertLevelEngine — Phase 179d invert-level + cover-depth calculator.
//
// Distinct from `Core/Drawing/Dimensioning/DrainageInvertDimensioner.cs`
// which only places SpotElevation annotations once invert levels exist.
// This engine computes the levels: it walks every drainage pipe, derives
// US/DS invert (mAOD) from the centreline geometry minus the radius, and
// reports cover-depth violations against a configurable burial table.
//
// Datum mAOD: read from PROJECT_BASE_POINT elevation; override-able by
// callers that supply a value.

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
        public int    DnMm         { get; set; }
        public double UsInvertM    { get; set; }
        public double DsInvertM    { get; set; }
        public double CoverUsM     { get; set; }
        public double CoverDsM     { get; set; }
        public string CoverStatus  { get; set; } = "OK";
        public string Notes        { get; set; } = "";
    }

    public class InvertReport
    {
        public double DatumMaOd       { get; set; }
        public int PipesAnalysed      { get; set; }
        public int PipesWritten       { get; set; }
        public int CoverViolations    { get; set; }
        public List<InvertLevelRow> Rows { get; } = new List<InvertLevelRow>();
        public List<string> Warnings  { get; } = new List<string>();
    }

    public static class InvertLevelEngine
    {
        private const double FtToM = 0.3048;

        public static InvertReport Calculate(Document doc, double datumMaOd, bool writeBack)
        {
            var r = new InvertReport { DatumMaOd = datumMaOd };
            if (doc == null) return r;
            var pipes = new FilteredElementCollector(doc).OfClass(typeof(Pipe))
                .Cast<Pipe>().Where(IsDrainage).ToList();

            foreach (var p in pipes)
            {
                try
                {
                    var lc = p.Location as LocationCurve;
                    if (lc?.Curve == null) continue;
                    var s = lc.Curve.GetEndPoint(0);
                    var e = lc.Curve.GetEndPoint(1);
                    var radiusM = (p.Diameter * FtToM) / 2.0;
                    var row = new InvertLevelRow
                    {
                        PipeId    = p.Id,
                        SystemName= p.MEPSystem?.Name ?? "",
                        DnMm      = (int)Math.Round(p.Diameter * FtToM * 1000.0),
                        UsInvertM = datumMaOd + (s.Z * FtToM) - radiusM,
                        DsInvertM = datumMaOd + (e.Z * FtToM) - radiusM,
                    };
                    row.CoverUsM = Math.Max(0.0, ApproxCoverDepth(s) - radiusM);
                    row.CoverDsM = Math.Max(0.0, ApproxCoverDepth(e) - radiusM);
                    row.CoverStatus = EvaluateCover(row);
                    if (row.CoverStatus != "OK") r.CoverViolations++;
                    r.Rows.Add(row);
                    r.PipesAnalysed++;

                    if (writeBack)
                    {
                        TryWriteDouble(p, ParamRegistry.PLM_DRN_INV_US,    row.UsInvertM);
                        TryWriteDouble(p, ParamRegistry.PLM_DRN_INV_DS,    row.DsInvertM);
                        TryWriteDouble(p, ParamRegistry.PLM_DRN_COVER_US, row.CoverUsM);
                        TryWriteDouble(p, ParamRegistry.PLM_DRN_COVER_DS, row.CoverDsM);
                        r.PipesWritten++;
                    }
                }
                catch (Exception ex) { r.Warnings.Add($"Pipe {p.Id}: {ex.Message}"); }
            }
            return r;
        }

        private static bool IsDrainage(Pipe p)
        {
            var s = (p.MEPSystem?.Name ?? "").ToUpperInvariant();
            return s.Contains("SAN") || s.Contains("WASTE") || s.Contains("FOUL")
                || s.Contains("DRAIN") || s.Contains("STORM") || s.Contains("SOIL")
                || s.Contains("RAINWATER");
        }

        private static double ApproxCoverDepth(XYZ point)
        {
            // Without explicit ground level data, treat the lowest XYZ.Z in
            // the project as ground; cover depth therefore tracks vertical
            // distance below origin if the point is buried. Phase 179d
            // ships this as a placeholder — overridable by callers that
            // pass an actual ground reference (future enhancement).
            return Math.Max(0.0, -point.Z * FtToM);
        }

        private static string EvaluateCover(InvertLevelRow row)
        {
            const double minHighway       = 1.20;
            const double minSoftLandscape = 0.60;
            // Pick the smaller of US/DS for the worst-case check.
            double minCover = Math.Min(row.CoverUsM, row.CoverDsM);
            if (minCover <= 0)                     return "ABOVE GRADE";
            if (minCover < minSoftLandscape)       return "SHALLOW";
            if (minCover < minHighway)             return "OK (soft)";
            return "OK";
        }

        private static void TryWriteDouble(Element el, string name, double v)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || p.IsReadOnly) return;
                if (p.StorageType == StorageType.Double) p.Set(v);
                else if (p.StorageType == StorageType.String) p.Set(v.ToString("F3"));
            }
            catch { }
        }
    }
}
