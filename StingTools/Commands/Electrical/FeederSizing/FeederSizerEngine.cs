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
    /// range (e.g. 0.8 for cable-tray grouping per BS 7671 Tbl 4C).
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
        public string Insulation      { get; set; } = "XLPE90";
        public double FeederLengthM   { get; set; } = 10.0;
        public double VDLimitPct      { get; set; } = 2.0;
        public string Standard        { get; set; } = "BS7671";
        public bool   ContinuousLoad  { get; set; } = false;
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
    }

    /// <summary>
    /// Pure feeder-sizing engine — no Revit API. Wraps Phase 177
    /// CableSizerEngine and treats each panel as a single sized cable from
    /// the supplying bus, applying derate + diversity before sizing.
    /// </summary>
    public static class FeederSizerEngine
    {
        public static FeederSizeResult Calculate(FeederSizeInput input, WireTableSet wireTables)
        {
            var result = new FeederSizeResult { PanelName = input?.PanelName ?? "" };
            if (input == null) { result.Warning = "Null input"; result.Status = "ERROR"; return result; }

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

            var sizerInput = new CableSizeInput
            {
                LoadKW         = diversifiedKW,
                VoltageV       = input.SystemVoltageV,
                Phases         = input.Phases,
                PowerFactor    = input.PowerFactor,
                LengthM        = input.FeederLengthM,
                InstallMethod  = input.InstallMethod,
                Material       = input.Material,
                Insulation     = input.Insulation,
                VDLimitPct     = input.VDLimitPct,
                Standard       = input.Standard,
                ContinuousLoad = input.ContinuousLoad
            };
            var sized = CableSizerEngine.Calculate(sizerInput);
            result.ProposedCsaMm2  = sized.RecommendedCsaMm2;
            result.CsaLabel        = sized.CsaLabel;
            result.ActualVDPct     = sized.ActualVoltDropPct;
            result.VDCompliant     = sized.VDCompliant;
            result.ProposedRatingA = sized.ProposedBreakerA;
            result.Warning         = sized.Warning;
            if (!result.VDCompliant) result.Status = "VD_FAIL";
            else if (input.DerateFactor > 0 && input.DerateFactor < 0.5) result.Status = "DERATED";
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
