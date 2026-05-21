// WaterSupplySizer — Phase 179b cold + hot water sizing engine.
//
// Walks each pipe in the active project that belongs to a recognised
// supply system (DCW / DHW / mains / chilled drinking water), accumulates
// loading units upstream from each pipe, looks up the design flow on the
// active standard (BS EN 806-3 or Hunter), then computes velocity and
// Hazen-Williams friction loss on the pipe at its current DN. Reports
// recommended DN if velocity exceeds the configured limit.
//
// Closes the calc → model loop: when writeBack=true the engine stamps
// PLM_SUP_* shared params *and* sets the native RBS_PIPE_DIAMETER_PARAM
// to the recommended bore so schedules, exports and downstream commands
// see the new size. Matches the MepAutoSizePipeCommand precedent.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;

namespace StingTools.Core.Plumbing
{
    public class SupplyPipeResult
    {
        public ElementId PipeId      { get; set; }
        public string SystemName     { get; set; } = "";
        public string ServiceClass   { get; set; } = ""; // DCW / DHW / Recirc / Mains
        public double LuAccumulated  { get; set; }
        public double WsfuAccumulated { get; set; }
        public double QdLps          { get; set; }
        public double VelMps         { get; set; }
        public double DpPaPerM       { get; set; }
        public int    CurrentDnMm    { get; set; }
        public int    RecommendedDnMm { get; set; }
        public bool   VelocityOk     { get; set; }
        public bool   PressureDropOk { get; set; }
        public string Notes          { get; set; } = "";
    }

    public class SupplySizingReport
    {
        public string Standard      { get; set; } = "BS-EN-806";
        public string MaterialDcw   { get; set; } = "";
        public string MaterialDhw   { get; set; } = "";
        public int    PipesScanned  { get; set; }
        public int    PipesUpsized  { get; set; }
        public int    PipesVelocityFailed { get; set; }
        public int    PipesDpFailed { get; set; }
        public int    PipesWritten  { get; set; }
        public int    PipesResized  { get; set; }  // native RBS_PIPE_DIAMETER_PARAM updated
        public List<SupplyPipeResult> Results { get; } = new List<SupplyPipeResult>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class WaterSupplySizer
    {
        private const double FtToM = 0.3048;
        private static readonly int[] DnSeries = { 15, 20, 22, 25, 28, 32, 35, 40, 50, 65, 80, 100, 125, 150 };

        public static SupplySizingReport Analyse(Document doc, bool writeBack, PlumbingSystemConfig cfg = null)
        {
            var r = new SupplySizingReport();
            if (doc == null) return r;
            cfg = cfg ?? PlumbingSystemConfig.Load(doc);
            r.Standard    = cfg.SupplyStandard ?? "BS-EN-806";
            r.MaterialDcw = cfg.MaterialFor("DCW");
            r.MaterialDhw = cfg.MaterialFor("DHW");

            double velMaxDcw = cfg.VelocityMaxFor("DCW_Max");
            double velMaxDhw = cfg.VelocityMaxFor("DHW_Max");
            double dpMax     = cfg.MaxPressureDropPaPerM;
            bool flushValve  = cfg.FlushValveMajority;

            var pipes = new FilteredElementCollector(doc).OfClass(typeof(Pipe)).Cast<Pipe>()
                .Where(p => IsSupply(p)).ToList();
            r.PipesScanned = pipes.Count;

            foreach (var p in pipes)
            {
                var res = SizePipe(doc, p, cfg, flushValve);
                r.Results.Add(res);
                bool isDhw = res.ServiceClass == "DHW" || res.ServiceClass == "Recirc";
                double velMax = isDhw ? velMaxDhw : velMaxDcw;
                res.VelocityOk     = res.VelMps <= velMax + 1e-3;
                res.PressureDropOk = res.DpPaPerM <= dpMax + 1e-3;
                if (!res.VelocityOk)     r.PipesVelocityFailed++;
                if (!res.PressureDropOk) r.PipesDpFailed++;
                if (res.RecommendedDnMm > res.CurrentDnMm) r.PipesUpsized++;

                if (writeBack)
                {
                    try
                    {
                        TryWriteDouble(p, ParamRegistry.PLM_SUP_QD,     res.QdLps);
                        TryWriteDouble(p, ParamRegistry.PLM_SUP_VEL,    res.VelMps);
                        TryWriteDouble(p, ParamRegistry.PLM_SUP_DP,     res.DpPaPerM);
                        TryWriteInt   (p, ParamRegistry.PLM_SUP_DN_REQ, res.RecommendedDnMm);
                        r.PipesWritten++;

                        // Close the calc → model loop: set the native pipe diameter
                        // so geometry, schedules, exports and downstream commands
                        // reflect the new bore. Only writes when the recommendation
                        // differs from the current modelled diameter.
                        if (res.RecommendedDnMm > 0
                            && res.RecommendedDnMm != res.CurrentDnMm)
                        {
                            if (TryWriteNativeDiameterMm(p, res.RecommendedDnMm))
                                r.PipesResized++;
                            else
                                r.Warnings.Add($"native diameter write skipped on pipe {p.Id} (read-only / constrained)");
                        }
                    }
                    catch (Exception ex) { r.Warnings.Add($"writeBack pipe {p.Id}: {ex.Message}"); }
                }
            }
            return r;
        }

        public static SupplyPipeResult SizePipe(Document doc, Pipe pipe, PlumbingSystemConfig cfg, bool flushValveMajority)
        {
            var res = new SupplyPipeResult { PipeId = pipe.Id };
            try
            {
                res.SystemName   = pipe.MEPSystem?.Name ?? "";
                res.ServiceClass = ClassifyService(res.SystemName);
                int currentDn = (int)Math.Round(pipe.Diameter * FtToM * 1000.0);
                res.CurrentDnMm = currentDn;

                // Estimate accumulated loading units by walking upstream connectors. Simple BFS:
                // count any plumbing fixture seen with PLM_SUP_LU_CW + PLM_SUP_LU_HW values.
                var counts = AccumulateLoadingUnits(doc, pipe, res.ServiceClass);
                res.LuAccumulated   = counts.lu;
                res.WsfuAccumulated = counts.wsfu;

                double qd;
                if ((cfg.SupplyStandard ?? "").StartsWith("HUNTER", StringComparison.OrdinalIgnoreCase))
                {
                    double gpm = PlumbingTables.WsfuToGpm(counts.wsfu, flushValveMajority);
                    qd = gpm * 0.0631;   // gpm → l/s
                }
                else
                {
                    qd = PlumbingTables.LuToQdLps(counts.lu);
                }
                res.QdLps = qd;

                if (qd > 0 && currentDn > 0)
                {
                    double diaM = currentDn / 1000.0;
                    double area = Math.PI * diaM * diaM / 4.0;
                    double qm3s = qd / 1000.0;
                    res.VelMps = qm3s / area;

                    var matKey = res.ServiceClass == "DHW" ? cfg.MaterialFor("DHW") : cfg.MaterialFor("DCW");
                    var mat    = PlumbingTables.GetMaterial(matKey);
                    double C   = mat?.HwC ?? 130;
                    res.DpPaPerM = HazenWilliamsPaPerM(qm3s, diaM, C);

                    res.RecommendedDnMm = ResolveDnForVelocity(qm3s, ResolveVelMax(cfg, res.ServiceClass));
                    if (res.RecommendedDnMm < currentDn) res.RecommendedDnMm = currentDn;
                }
                else
                {
                    res.RecommendedDnMm = currentDn;
                }
            }
            catch (Exception ex) { res.Notes = ex.Message; }
            return res;
        }

        private static (double lu, double wsfu) AccumulateLoadingUnits(Document doc, Pipe seed, string service)
        {
            // Direction-aware BFS for supply: fixtures sit ABOVE the main, so
            // from a given supply pipe we only sum fixtures at Z >= pipe.Z (minus
            // a small slack). Without this filter the BFS reaches every fixture
            // in the whole connected supply network and every pipe gets the same
            // loading-unit total. Fixture nodes themselves are exempt from the
            // Z-cutoff so a low-set utility sink off a horizontal branch still
            // counts.
            double seedZFt = SeedMidZ(seed);
            const double zTolFt = 0.05; // ~15 mm slack for nearly-flat runs

            var visited = new HashSet<long>();
            var queue   = new Queue<Element>();
            visited.Add(seed.Id.Value);
            queue.Enqueue(seed);

            double lu = 0, wsfu = 0;
            int budget = 4000;
            while (queue.Count > 0 && budget-- > 0)
            {
                var el = queue.Dequeue();
                try
                {
                    ConnectorManager cm = (el as MEPCurve)?.ConnectorManager
                                       ?? (el as FamilyInstance)?.MEPModel?.ConnectorManager;
                    if (cm == null) continue;
                    foreach (Connector c in cm.Connectors)
                    {
                        if (!c.IsConnected) continue;
                        foreach (Connector other in c.AllRefs)
                        {
                            var owner = other.Owner;
                            if (owner == null || visited.Contains(owner.Id.Value)) continue;

                            double ownerZ = OwnerMidZ(owner);
                            var bic = (BuiltInCategory)(owner.Category?.Id?.Value ?? 0);
                            bool isFixture = bic == BuiltInCategory.OST_PlumbingFixtures
                                          || bic == BuiltInCategory.OST_MechanicalEquipment;
                            if (!isFixture && ownerZ + zTolFt < seedZFt) continue;

                            visited.Add(owner.Id.Value);
                            if (isFixture)
                            {
                                lu   += ReadDouble(owner, service == "DHW"
                                    ? ParamRegistry.PLM_SUP_LU_HW
                                    : ParamRegistry.PLM_SUP_LU_CW);
                                wsfu += ReadDouble(owner, ParamRegistry.PLM_SUP_WSFU);
                                continue;
                            }
                            queue.Enqueue(owner);
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            }
            return (lu, wsfu);
        }

        private static double SeedMidZ(Pipe pipe)
        {
            try
            {
                var lc = pipe.Location as LocationCurve;
                if (lc?.Curve == null) return 0;
                var s = lc.Curve.GetEndPoint(0);
                var e = lc.Curve.GetEndPoint(1);
                return (s.Z + e.Z) / 2.0;
            }
            catch { return 0; }
        }

        private static double OwnerMidZ(Element el)
        {
            try
            {
                if (el is MEPCurve mc && mc.Location is LocationCurve lc && lc.Curve != null)
                {
                    var s = lc.Curve.GetEndPoint(0);
                    var e = lc.Curve.GetEndPoint(1);
                    return (s.Z + e.Z) / 2.0;
                }
                if (el.Location is LocationPoint lp) return lp.Point.Z;
                var bb = el.get_BoundingBox(null);
                if (bb != null) return (bb.Min.Z + bb.Max.Z) / 2.0;
            }
            catch { }
            return 0;
        }

        private static double ResolveVelMax(PlumbingSystemConfig cfg, string service)
        {
            if (service == "DHW" || service == "Recirc") return cfg.VelocityMaxFor("DHW_Max");
            return cfg.VelocityMaxFor("DCW_Max");
        }

        public static int ResolveDnForVelocity(double qM3s, double velMaxMps)
        {
            if (qM3s <= 0) return DnSeries[0];
            foreach (var dn in DnSeries)
            {
                double diaM = dn / 1000.0;
                double area = Math.PI * diaM * diaM / 4.0;
                double v    = qM3s / area;
                if (v <= velMaxMps) return dn;
            }
            return DnSeries[DnSeries.Length - 1];
        }

        public static double HazenWilliamsPaPerM(double qM3s, double diaM, double C)
        {
            if (qM3s <= 0 || diaM <= 0) return 0;
            // Hazen–Williams head loss (m / m): h = 10.67 · Q^1.852 / (C^1.852 · D^4.87).
            double hPerM = 10.67 * Math.Pow(qM3s, 1.852) / (Math.Pow(C, 1.852) * Math.Pow(diaM, 4.87));
            return hPerM * 9810.0; // ρg = 9810 Pa per m head.
        }

        public static string ClassifyService(string systemName)
        {
            if (string.IsNullOrEmpty(systemName)) return "DCW";
            var s = systemName.ToUpperInvariant();
            if (s.Contains("RECIRC") || s.Contains("RETURN")) return "Recirc";
            if (s.Contains("HOT WATER") || s.Contains("DHW") || s.Contains("HWS"))   return "DHW";
            if (s.Contains("CHILLED DRINKING") || s.Contains("CHW DRINK"))           return "Drinking";
            if (s.Contains("MAIN"))                                                  return "Mains";
            return "DCW";
        }

        public static bool IsSupply(Pipe p)
        {
            try
            {
                var s = (p.MEPSystem?.Name ?? "").ToUpperInvariant();
                if (string.IsNullOrEmpty(s)) return false;
                if (s.Contains("SANITARY") || s.Contains("WASTE") || s.Contains("FOUL")
                    || s.Contains("STORM") || s.Contains("RAIN")  || s.Contains("DRAIN")
                    || s.Contains("VENT")  || s.Contains("SOIL")) return false;
                if (s.Contains("DOMESTIC") || s.Contains("WATER") || s.Contains("DCW")
                    || s.Contains("DHW")     || s.Contains("HWS")  || s.Contains("CWS")
                    || s.Contains("MAINS")   || s.Contains("SUPPLY")) return true;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return false;
        }

        private static double ReadDouble(Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || !p.HasValue) return 0;
                if (p.StorageType == StorageType.Double)  return p.AsDouble();
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                if (p.StorageType == StorageType.String)
                {
                    if (double.TryParse(p.AsString(), out var d)) return d;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return 0;
        }

        /// <summary>
        /// Set the native Revit pipe diameter (RBS_PIPE_DIAMETER_PARAM) to
        /// the recommended bore in mm. Returns false if the parameter is
        /// missing, read-only, or rejected by Revit (e.g. fitting constraint).
        /// </summary>
        private static bool TryWriteNativeDiameterMm(Pipe pipe, int dnMm)
        {
            if (pipe == null || dnMm <= 0) return false;
            try
            {
                var p = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.Double) return false;
                double valueFt = dnMm * (1.0 / (FtToM * 1000.0)); // mm → m → ft
                p.Set(valueFt);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TryWriteNativeDiameterMm pipe={pipe?.Id} dn={dnMm}: {ex.Message}");
                return false;
            }
        }

        private static void TryWriteDouble(Element el, string name, double v)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || p.IsReadOnly) return;
                if (p.StorageType == StorageType.Double) p.Set(v);
                else if (p.StorageType == StorageType.Integer) p.Set((int)Math.Round(v));
                else if (p.StorageType == StorageType.String)  p.Set(v.ToString("F3"));
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }

        private static void TryWriteInt(Element el, string name, int v)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || p.IsReadOnly) return;
                if (p.StorageType == StorageType.Integer) p.Set(v);
                else if (p.StorageType == StorageType.Double)  p.Set((double)v);
                else if (p.StorageType == StorageType.String)  p.Set(v.ToString());
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }
    }
}
