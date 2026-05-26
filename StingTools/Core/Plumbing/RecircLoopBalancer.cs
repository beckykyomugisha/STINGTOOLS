// RecircLoopBalancer — DHW recirculation heat-loss + DRV pre-set.
// Phase 178c. For each pipe on a recirculation system, computes pipe
// surface heat loss (Q = U·L·ΔT) using the BS 5422 default insulation
// thickness × λ, then derives the pump duty that maintains ≤5°C ΔT
// across the system. DRV pre-set values are written per branch as a
// proportional share of total Q.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;

namespace StingTools.Core.Plumbing
{
    public class RecircBranchResult
    {
        public ElementId PipeId          { get; set; }
        public double LengthM            { get; set; }
        public double DiameterMm         { get; set; }
        public double HeatLossW          { get; set; }
        public double DrvPresetKv        { get; set; }
        public double FlowLpm            { get; set; }
        public string Notes              { get; set; } = "";
    }

    public class RecircLoopReport
    {
        public string SystemName        { get; set; } = "";
        public double TotalHeatLossW    { get; set; }
        public double PumpDutyLpm       { get; set; }
        public double PumpHeadKpa       { get; set; }
        public int    BranchesStamped   { get; set; }  // PLM_RECIRC_* params
        public List<RecircBranchResult> Branches { get; } = new List<RecircBranchResult>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class RecircLoopBalancer
    {
        private const double WaterCpJperKgK = 4186.0;
        private const double WaterRhoKgM3   = 985.0;  // 60 °C
        private const double DesignDeltaTC  = 5.0;
        // Approximate U-value (W/m·K) of a typical 32 mm DHW pipe with
        // 25 mm mineral wool (λ=0.04). Refined per pipe using BS 5422
        // table once a fuller engine lands.
        private const double UperLengthWmK  = 0.40;

        public static RecircLoopReport Analyse(Document doc, string systemNameFilter = null)
            => Analyse(doc, systemNameFilter, writeBack: false);

        /// <summary>
        /// Compute DHW recirculation pump duty + per-branch DRV Kv. When
        /// writeBack=true also stamps PLM_RECIRC_PUMP_DUTY_LPM (constant per
        /// branch — the loop pump duty) and PLM_RECIRC_DRV_KV per branch
        /// pipe so DRV commissioning sheets and schedules can read the
        /// pre-set without re-running the calc. Caller owns the Transaction.
        /// </summary>
        public static RecircLoopReport Analyse(Document doc, string systemNameFilter, bool writeBack)
        {
            var r = new RecircLoopReport();
            if (doc == null) return r;

            var pipes = new FilteredElementCollector(doc).OfClass(typeof(Pipe)).Cast<Pipe>()
                .Where(p => IsRecirc(p, systemNameFilter)).ToList();
            if (pipes.Count == 0)
            {
                r.Warnings.Add("No DHW recirculation pipes found.");
                return r;
            }
            r.SystemName = pipes[0].MEPSystem?.Name ?? "";

            // ΔT for heat-loss calc. DHW typically operates at 60 °C; ambient
            // varies by service space (riser, ceiling void). Use 20 °C ambient
            // (BS EN 12831 / BS 5422 default office air temp) → ΔT = 40 K.
            // Allow per-project override via PLM_RECIRC_DELTA_T_K on
            // ProjectInformation when bound.
            const double DefaultDeltaTK = 40.0;
            double deltaTK = DefaultDeltaTK;
            try
            {
                var pi = doc.ProjectInformation;
                var prm = pi?.LookupParameter("PLM_RECIRC_DELTA_T_K");
                if (prm != null && prm.HasValue)
                {
                    if (prm.StorageType == StorageType.Double && prm.AsDouble() > 0)
                        deltaTK = prm.AsDouble();
                    else if (prm.StorageType == StorageType.Integer && prm.AsInteger() > 0)
                        deltaTK = prm.AsInteger();
                }
            }
            catch (Exception ex) { StingLog.Warn($"recirc ΔT read: {ex.Message}"); }

            double totalQ = 0;
            foreach (var p in pipes)
            {
                try
                {
                    // Use the built-in CURVE_ELEM_LENGTH parameter — robust on
                    // non-English Revit installs where LookupParameter("Length")
                    // would return null.
                    var lenParam = p.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                    double lengthFt = lenParam?.AsDouble() ?? 0;
                    double lengthM  = lengthFt * 0.3048;
                    double diaMm = p.Diameter * 0.3048 * 1000.0;
                    double q = UperLengthWmK * lengthM * deltaTK;
                    totalQ += q;
                    r.Branches.Add(new RecircBranchResult
                    {
                        PipeId     = p.Id,
                        LengthM    = lengthM,
                        DiameterMm = diaMm,
                        HeatLossW  = q
                    });
                }
                catch (Exception ex) { r.Warnings.Add($"branch {p.Id}: {ex.Message}"); }
            }
            r.TotalHeatLossW = totalQ;

            double mDotKgS = totalQ / (WaterCpJperKgK * DesignDeltaTC);
            r.PumpDutyLpm  = mDotKgS / WaterRhoKgM3 * 1000.0 * 60.0;
            r.PumpHeadKpa  = 25.0;

            // DRV pre-set: kV ≈ flow in m3/h / sqrt(Δp in bar). With
            // assumed 0.1 bar per branch and proportional flow share:
            double sumQ = totalQ > 0 ? totalQ : 1;
            foreach (var b in r.Branches)
            {
                double share = b.HeatLossW / sumQ;
                double branchFlowM3H = (mDotKgS / WaterRhoKgM3) * 3600.0 * share;
                b.FlowLpm = branchFlowM3H * 1000.0 / 60.0;
                b.DrvPresetKv = branchFlowM3H / Math.Sqrt(0.1);

                if (writeBack)
                {
                    try
                    {
                        var p = doc.GetElement(b.PipeId);
                        if (p != null)
                        {
                            StingTools.Core.ParameterHelpers.SetString(p, "PLM_RECIRC_PUMP_DUTY_LPM",
                                $"{r.PumpDutyLpm:F1}", overwrite: true);
                            StingTools.Core.ParameterHelpers.SetString(p, "PLM_RECIRC_DRV_KV",
                                $"{b.DrvPresetKv:F3}", overwrite: true);
                            r.BranchesStamped++;
                        }
                    }
                    catch (Exception exW) { r.Warnings.Add($"Recirc stamp {b.PipeId}: {exW.Message}"); }
                }
            }
            return r;
        }

        private static bool IsRecirc(Pipe p, string filter)
        {
            try
            {
                var sys = (p.MEPSystem?.Name ?? "").ToUpperInvariant();
                if (!string.IsNullOrEmpty(filter)) return sys.Contains(filter.ToUpperInvariant());
                return sys.Contains("HWS") || sys.Contains("RETURN") || sys.Contains("RECIRC");
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }
    }
}
