using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Commands.Electrical.CableSizer;
using StingTools.Commands.Electrical.FaultCurrent;
using StingTools.Commands.Electrical.VoltageDrop;

namespace StingTools.Commands.Electrical.FeederSizing
{
    /// <summary>
    /// Inputs for sizing one feeder. All units SI: kW, V, m, mm². Diversity
    /// factor in the range 0..1 (1 = no diversity); derate factor in the same
    /// range (e.g. 0.8 for grouping) — applied to the cable's capacity alongside
    /// the tabulated BS 7671 factors.
    /// </summary>
    public class FeederSizeInput
    {
        public string PanelName       { get; set; }
        public double DemandKW        { get; set; }
        public double PowerFactor     { get; set; } = 0.85;
        public double SystemVoltageV  { get; set; } = 415.0;
        public int    Phases          { get; set; } = 3;
        public double DerateFactor    { get; set; } = 1.0;
        public double DiversityFactor { get; set; } = 1.0;
        public string InstallMethod   { get; set; } = "C";
        public string Material        { get; set; } = "Cu";
        /// <summary>PVC70 — BS 7671 Table 4D2A is the only Appendix 4 table shipped.
        /// XLPE / SWA feeders (Tables 4E2A / 4E4A) are refused until those tables are added.</summary>
        public string Insulation      { get; set; } = "PVC70";
        public double FeederLengthM   { get; set; } = 10.0;
        /// <summary>BS 7671 Appendix 12 "other uses" limit; was 2 %, which no standard sets.</summary>
        public double VDLimitPct      { get; set; } = 5.0;
        public string Standard        { get; set; } = "BS7671";
        public bool   ContinuousLoad  { get; set; } = false;

        /// <summary>Every value above that was a DEFAULT rather than read from the model
        /// (e.g. "length 10 m (no circuit length)"). Reported on the result so a feeder
        /// sized on an assumed length cannot pass as a measured one.</summary>
        public List<string> DefaultsUsed { get; } = new List<string>();

        /// <summary>When set, the feeder is not sized and this is reported instead.</summary>
        public string SkipReason      { get; set; }
    }

    public class FeederSizeResult
    {
        public string PanelName       { get; set; }
        public double DemandKW        { get; set; }
        public double DesignCurrentA  { get; set; }
        public double ProposedCsaMm2  { get; set; }
        public string CsaLabel        { get; set; } = "—";
        public double ActualVDPct     { get; set; }
        public bool   VDCompliant     { get; set; }
        public double ProposedRatingA { get; set; }
        public string Status          { get; set; } = "OK";
        public string Warning         { get; set; } = "";
        /// <summary>False when nothing was sized — a caller must not write the zero CSA.</summary>
        public bool   Sized           { get; set; }
        /// <summary>Tables / factors the size rests on (from the cable sizer).</summary>
        public string Basis           { get; set; } = "";
        /// <summary>Inputs that were defaults, not model data.</summary>
        public List<string> DefaultsUsed { get; } = new List<string>();
    }

    /// <summary>
    /// Pure feeder-sizing engine — no Revit API. Wraps CableSizerEngine and treats
    /// each panel as a single sized cable from the supplying bus. Diversity scales the
    /// load; the derate factor is passed to the sizer as a capacity factor (ELEC-3: it
    /// used to set only a "DERATED" status flag and never touched the size).
    /// </summary>
    public static class FeederSizerEngine
    {
        public static FeederSizeResult Calculate(FeederSizeInput input, WireTableSet wireTables)
        {
            var result = new FeederSizeResult { PanelName = input?.PanelName ?? "" };
            if (input == null) { result.Warning = "Null input"; result.Status = "ERROR"; return result; }
            result.DefaultsUsed.AddRange(input.DefaultsUsed);

            if (!string.IsNullOrEmpty(input.SkipReason))
            {
                result.Status = "SKIPPED";
                result.Warning = input.SkipReason;
                return result;
            }

            double diversifiedKW = input.DemandKW * (input.DiversityFactor <= 0 ? 1.0 : input.DiversityFactor);
            double iB = CableSizerEngine.DesignCurrent(diversifiedKW, input.SystemVoltageV,
                input.PowerFactor, input.Phases);
            result.DemandKW = diversifiedKW;
            result.DesignCurrentA = iB;
            if (iB <= 0)
            {
                result.Warning = "Invalid demand / voltage / PF — feeder not sized.";
                result.Status = "ERROR";
                return result;
            }

            double derate = input.DerateFactor > 0 && input.DerateFactor <= 1.0 ? input.DerateFactor : 1.0;
            var sizerInput = new CableSizeInput
            {
                LoadKW            = diversifiedKW,
                VoltageV          = input.SystemVoltageV,
                Phases            = input.Phases,
                PowerFactor       = input.PowerFactor,
                LengthM           = input.FeederLengthM,
                InstallMethod     = input.InstallMethod,
                Material          = input.Material,
                Insulation        = input.Insulation,
                VDLimitPct        = input.VDLimitPct,
                Standard          = input.Standard,
                ContinuousLoad    = input.ContinuousLoad,
                ExtraDerateFactor = derate,
            };
            var sized = CableSizerEngine.Calculate(sizerInput);
            result.Sized           = sized.Sized;
            result.ProposedCsaMm2  = sized.Sized ? sized.RecommendedCsaMm2 : 0;
            result.CsaLabel        = sized.Sized ? sized.CsaLabel : "—";
            result.ActualVDPct     = sized.ActualVoltDropPct;
            result.VDCompliant     = sized.Sized && sized.VDCompliant;
            result.ProposedRatingA = sized.ProposedBreakerA;
            result.Warning         = sized.Warning;
            result.Basis           = string.IsNullOrEmpty(sized.Basis) ? sized.DerivationNote : sized.Basis;
            if (derate < 1.0 && StingTools.Standards.ElectricalStandardId.Normalise(input.Standard)
                                == StingTools.Standards.ElectricalStandardId.Nec2023)
                result.Warning = (string.IsNullOrEmpty(result.Warning) ? "" : result.Warning + " ") +
                                 $"Derate {derate:0.00} NOT applied: the NEC path adjusts by 310.15 only.";

            if (!sized.Sized) result.Status = "NOT_SIZED";
            else if (!result.VDCompliant) result.Status = "VD_FAIL";
            else if (result.DefaultsUsed.Count > 0) result.Status = "OK_DEFAULTS";
            return result;
        }

        public static List<FeederSizeResult> CalculateAll(IEnumerable<FeederSizeInput> inputs,
            WireTableSet wireTables)
        {
            return (inputs ?? Enumerable.Empty<FeederSizeInput>())
                .Select(i => Calculate(i, wireTables))
                .ToList();
        }
    }
}
