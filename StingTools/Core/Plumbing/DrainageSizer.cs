// DrainageSizer — BS EN 12056-2 / IPC 2021 drainage pipe sizing.
// Phase 178c. Reads the per-pipe DFU map from FixtureUnitAggregator,
// looks up the minimum DN against the active project plumbing code
// (BS-UK default, IPC-US toggle), then evaluates self-cleansing
// velocity via Chezy-Manning. Optionally writes results back to
// PLM_CALC_DN_MM and PLM_CALC_SLOPE_PCT.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;
using StingTools.Standards.BSEN12056;
using StingTools.Standards.IPC2021;

namespace StingTools.Core.Plumbing
{
    public class DrainageSizeResult
    {
        public ElementId PipeId { get; set; }
        public double Dfu { get; set; }
        public double SlopePct { get; set; }
        public int RecommendedDnMm { get; set; }
        public int CurrentDnMm     { get; set; }
        public bool UpsizeRequired { get; set; }
        public bool SlopeAdequate  { get; set; }
        public double SelfCleansingVelocityMps { get; set; }
        public bool SelfCleansingOk { get; set; }
        public bool IsStack { get; set; }
        public string FailureReason { get; set; } = "";
    }

    public class DrainageSizingReport
    {
        public int PipesAnalysed              { get; set; }
        public int PipesUpsized               { get; set; }
        public int PipesSlopeInsufficient     { get; set; }
        public int PipesSelfCleansingFailed   { get; set; }
        public int PipesWritten               { get; set; }
        public string CodeUsed                { get; set; } = "BS-UK";
        public List<DrainageSizeResult> Results { get; } = new List<DrainageSizeResult>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class DrainageSizer
    {
        // Manning's n by pipe material — coarse default lookup. Reads
        // PLM_MAT_TXT first; falls back to type-name heuristic.
        private const double FtToM = 0.3048;
        private const double InToMm = 25.4;

        public static DrainageSizingReport AnalyseAndSize(
            Document doc,
            Dictionary<ElementId, double> dfuMap,
            bool writeBack,
            bool dryRun)
        {
            var r = new DrainageSizingReport();
            if (doc == null || dfuMap == null) return r;
            r.CodeUsed = ResolveCode(doc);

            foreach (var kv in dfuMap)
            {
                Pipe pipe = null;
                try { pipe = doc.GetElement(kv.Key) as Pipe; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                if (pipe == null) continue;
                var res = SizePipe(pipe, kv.Value, r.CodeUsed);
                r.Results.Add(res);
                r.PipesAnalysed++;
                if (res.UpsizeRequired)         r.PipesUpsized++;
                if (!res.SlopeAdequate)         r.PipesSlopeInsufficient++;
                if (!res.SelfCleansingOk)       r.PipesSelfCleansingFailed++;

                if (writeBack && !dryRun)
                {
                    try
                    {
                        TryWriteInt(pipe, ParamRegistry.PLM_CALC_DN, res.RecommendedDnMm);
                        TryWriteString(pipe, ParamRegistry.PLM_CALC_SLOPE, res.SlopePct.ToString("F2"));
                        if (res.SelfCleansingVelocityMps > 0)
                            TryWriteString(pipe, ParamRegistry.PLM_VEL,
                                res.SelfCleansingVelocityMps.ToString("F3"));
                        r.PipesWritten++;
                    }
                    catch (Exception ex)
                    {
                        r.Warnings.Add($"writeBack pipe {pipe.Id}: {ex.Message}");
                    }
                }
            }
            return r;
        }

        public static DrainageSizeResult SizePipe(Pipe pipe, double dfu, string code = "BS-UK")
        {
            var res = new DrainageSizeResult { PipeId = pipe.Id, Dfu = dfu };
            try
            {
                var lc = pipe.Location as LocationCurve;
                if (lc?.Curve == null) { res.FailureReason = "No LocationCurve"; return res; }
                var s = lc.Curve.GetEndPoint(0);
                var e = lc.Curve.GetEndPoint(1);
                double dxFt = e.X - s.X, dyFt = e.Y - s.Y, dzFt = e.Z - s.Z;
                double horizFt = Math.Sqrt(dxFt * dxFt + dyFt * dyFt);
                double totalFt = Math.Sqrt(horizFt * horizFt + dzFt * dzFt);
                res.IsStack = totalFt > 1e-6 && Math.Abs(dzFt) / totalFt > 0.8;
                res.SlopePct = horizFt > 1e-6 ? Math.Abs(dzFt) / horizFt * 100.0 : 0.0;

                int currentDn = (int)Math.Round(pipe.Diameter * FtToM * 1000.0);
                res.CurrentDnMm = currentDn;

                int recommended;
                if (code.StartsWith("IPC", StringComparison.OrdinalIgnoreCase))
                {
                    recommended = IPCSiAdapter.GetMinimumDrainPipeSizeMm(dfu, res.SlopePct, res.IsStack);
                }
                else
                {
                    recommended = ResolveBsEnDrainSizeMm(dfu, res.IsStack);
                }
                if (recommended <= 0) recommended = currentDn;
                res.RecommendedDnMm = recommended;
                res.UpsizeRequired  = recommended > currentDn;

                double minSlopePct = BSen12056Standards.GetMinimumSlopePct(currentDn, res.IsStack, isMain: false);
                res.SlopeAdequate  = res.IsStack || res.SlopePct >= minSlopePct;

                if (!res.IsStack)
                {
                    double n = MannningNFor(pipe);
                    double diaM = currentDn / 1000.0;
                    double rH   = diaM / 4.0;
                    double slope = Math.Max(res.SlopePct / 100.0, 1e-6);
                    double v = (1.0 / n) * Math.Pow(rH, 2.0 / 3.0) * Math.Sqrt(slope);
                    res.SelfCleansingVelocityMps = v;
                    res.SelfCleansingOk = v >= BSen12056Standards.SelfCleansingVelocityMps;
                }
                else
                {
                    res.SelfCleansingOk = true;
                }
            }
            catch (Exception ex)
            {
                res.FailureReason = ex.Message;
            }
            return res;
        }

        // BS EN 12056-2 minimum branch-drain sizing — coarse derivation
        // from §6 / Table 5 (System III). Returns nominal DN in mm.
        private static int ResolveBsEnDrainSizeMm(double du, bool isStack)
        {
            if (isStack)
            {
                if (du <= 1.5)  return 50;
                if (du <= 4.0)  return 75;
                if (du <= 6.0)  return 100;
                if (du <= 12.0) return 125;
                if (du <= 20.0) return 150;
                if (du <= 50.0) return 200;
                return 250;
            }
            if (du <= 0.5) return 32;
            if (du <= 1.5) return 40;
            if (du <= 4.0) return 50;
            if (du <= 6.0) return 75;
            if (du <= 20.0) return 100;
            return 125;
        }

        private static double MannningNFor(Pipe pipe)
        {
            string mat = "";
            try
            {
                var p = pipe.LookupParameter(ParamRegistry.PLM_MAT);
                if (p != null && p.StorageType == StorageType.String) mat = p.AsString() ?? "";
                if (string.IsNullOrEmpty(mat)) mat = pipe.PipeType?.Name ?? "";
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            mat = (mat ?? "").ToUpperInvariant();
            if (mat.Contains("CLAY") || mat.Contains("CONCRETE")) return 0.013;
            if (mat.Contains("CAST") || mat.Contains("CI"))       return 0.011;
            if (mat.Contains("HDPE"))                              return 0.010;
            if (mat.Contains("PVC")  || mat.Contains("ABS"))       return 0.009;
            if (mat.Contains("COPPER") || mat.Contains("CU"))      return 0.010;
            return 0.012;
        }

        private static string ResolveCode(Document doc)
        {
            try
            {
                var pi = doc?.ProjectInformation;
                var p = pi?.LookupParameter(ParamRegistry.PRJ_PLUMBING_CODE);
                if (p != null && p.HasValue && p.StorageType == StorageType.String)
                {
                    var s = p.AsString();
                    if (!string.IsNullOrWhiteSpace(s)) return s.Trim().ToUpperInvariant();
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return "BS-UK";
        }

        private static bool TryWriteInt(Element el, string name, int value)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || p.IsReadOnly) return false;
                if (p.StorageType == StorageType.Integer) { p.Set(value); return true; }
                if (p.StorageType == StorageType.Double)  { p.Set((double)value); return true; }
                if (p.StorageType == StorageType.String)  { p.Set(value.ToString()); return true; }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return false;
        }

        private static bool TryWriteString(Element el, string name, string value)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || p.IsReadOnly) return false;
                if (p.StorageType == StorageType.String) { p.Set(value); return true; }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return false;
        }
    }
}
