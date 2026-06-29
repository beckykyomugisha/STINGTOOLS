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

        /// <summary>Per-metric "was this computed from real model data?" flag.
        /// Absent => assumed computed (true). A metric that is a hardcoded default,
        /// a zero-design artefact, or a delegated (EDGE-app-owned) number is marked
        /// false so the dashboard never renders it as an earned pass.</summary>
        public Dictionary<string, bool> ComputedFlags { get; } = new Dictionary<string, bool>();

        /// <summary>Per-metric note explaining a not-computed / indicative status.</summary>
        public Dictionary<string, string> Notes { get; } = new Dictionary<string, string>();

        public bool HasNumber(string metric) => Numbers.ContainsKey(metric);
        public bool HasBool(string metric)   => Bools.ContainsKey(metric);

        public double GetNumber(string metric, double fallback = 0)
            => Numbers.TryGetValue(metric, out var v) ? v : fallback;
        public bool GetBool(string metric, bool fallback = false)
            => Bools.TryGetValue(metric, out var v) ? v : fallback;

        public void SetComputed(string metric, bool computed, string note = null)
        {
            ComputedFlags[metric] = computed;
            if (!string.IsNullOrEmpty(note)) Notes[metric] = note;
        }
        public bool IsComputed(string metric)
            => !ComputedFlags.TryGetValue(metric, out var v) || v;   // default true
        public string GetNote(string metric)
            => Notes.TryGetValue(metric, out var n) ? n : "";
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
        /// <summary>False when the metric was NOT computed from real model data
        /// (zero-design artefact, hardcoded indicative default, or a delegated
        /// EDGE-app number). The dashboard renders these as "Not computed" and
        /// never as a pass — a certification tool must not imply an earned pass.</summary>
        public bool   Computed         { get; set; } = true;
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

        /// <summary>WS I2 — true ONLY when all three axes (country + climate zone +
        /// building use) were real (non-wildcard) AND matched exactly. A match on a
        /// "*" axis is a fallback/default proxy, never an exact match.</summary>
        public bool ExactMatch { get; set; }
        /// <summary>WS I2 — the axes that fell back to a wildcard/default proxy
        /// (e.g. "country (default proxy)", "climate zone 4A (default, not derived)").</summary>
        public List<string> FallbackAxes { get; } = new List<string>();

        /// <summary>Human-readable one-line summary of the resolution path, e.g.
        /// "no CAF baseline -> fell back to climate-zone 0A office, source ASHRAE 90.1 — indicative".</summary>
        public string Summary { get; set; } = "";

        public GreenBaseline Baseline { get; set; }
    }
}
