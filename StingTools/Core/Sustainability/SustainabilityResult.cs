// StingTools — Sustainability result POCOs (Phase 195).
//
// Pure data types shared by the four estimators, the scheme evaluator and
// the metric-provider layer. No Revit dependency — these compile and unit-test
// in the StingTools.Sustainability.Tests project.

using System.Collections.Generic;

namespace StingTools.Core.Sustainability
{
    /// <summary>The named metric values a provider can supply for one document
    /// (a single provider may supply several — e.g. MaterialsRollup supplies
    /// embodied_energy_savings_pct, gwp_reduction_pct and wblca_completed).</summary>
    public class MetricResult
    {
        /// <summary>Numeric metric values, keyed by metric id (the scheme gate's
        /// "metric" field). e.g. {"energy_savings_pct": 42.1}.</summary>
        public Dictionary<string, double> Numbers { get; } = new Dictionary<string, double>();

        /// <summary>Boolean metric values, keyed by metric id. e.g.
        /// {"wblca_completed": true}.</summary>
        public Dictionary<string, bool> Bools { get; } = new Dictionary<string, bool>();

        public bool HasNumber(string metric) => Numbers.ContainsKey(metric);
        public bool HasBool(string metric)   => Bools.ContainsKey(metric);

        public double GetNumber(string metric, double fallback = 0)
            => Numbers.TryGetValue(metric, out var v) ? v : fallback;
        public bool GetBool(string metric, bool fallback = false)
            => Bools.TryGetValue(metric, out var v) ? v : fallback;
    }

    /// <summary>One resolved gate result inside a scheme evaluation.</summary>
    public class GateResult
    {
        public string GateId      { get; set; } = "";
        public string Label       { get; set; } = "";
        public string Metric      { get; set; } = "";
        public string Provider    { get; set; } = "";
        public bool   Required     { get; set; }
        public bool   Delegated    { get; set; }   // EDGE_APP-owned number
        public string Unit         { get; set; } = "";

        /// <summary>The STING indicative value pulled from the provider.</summary>
        public double IndicativeValue { get; set; }
        /// <summary>The threshold/target the gate compares against (for the
        /// selected scheme level), when applicable.</summary>
        public double Threshold        { get; set; }
        /// <summary>Did this gate pass? (for all_required schemes).</summary>
        public bool   Passed           { get; set; }
        /// <summary>Points awarded (for pointSum schemes); 0 otherwise.</summary>
        public int    Points           { get; set; }
        /// <summary>True when this gate has no measurable value (e.g. provider
        /// returned nothing) — surfaced distinctly from a hard fail.</summary>
        public bool   NotEvaluated     { get; set; }
        public string Note             { get; set; } = "";
    }

    /// <summary>The outcome of evaluating one scheme against a document.</summary>
    public class SchemeResult
    {
        public string SchemeId     { get; set; } = "";
        public string SchemeName   { get; set; } = "";
        public string Aggregation  { get; set; } = "";   // all_required | pointSum
        public string TargetLevel  { get; set; } = "";   // selected level
        public string AchievedLevel { get; set; } = "None";
        public bool   Passed        { get; set; }
        public int    TotalPoints   { get; set; }
        public string Band          { get; set; } = "";  // pointSum band name
        public List<GateResult> Gates { get; } = new List<GateResult>();
    }

    /// <summary>One hop in a baseline resolution path — the proxy log (rule D2).</summary>
    public class ResolutionHop
    {
        public string Key        { get; set; } = "";   // which composite key tried
        public bool   Matched     { get; set; }
        public string Detail      { get; set; } = "";
    }

    /// <summary>The resolved baseline plus its provenance trail.</summary>
    public class BaselineResolution
    {
        public bool   Found        { get; set; }
        public string Source       { get; set; } = "";
        public string Provenance   { get; set; } = "indicative";
        public string MatchedKey    { get; set; } = "";
        public List<ResolutionHop> Path { get; } = new List<ResolutionHop>();

        /// <summary>Human-readable one-line summary of the resolution path, e.g.
        /// "no CAF baseline -> fell back to climate-zone 0A office, source ASHRAE 90.1 — indicative".</summary>
        public string Summary { get; set; } = "";

        public GreenBaseline Baseline { get; set; }
    }
}
