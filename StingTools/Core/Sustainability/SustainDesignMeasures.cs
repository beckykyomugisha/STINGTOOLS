// StingTools — SustainDesignMeasures (SUS-1).
//
// The DESIGN MEASURES the EDGE App's Design tab asks for: envelope U-values + SHGC,
// window-to-wall ratio per orientation, lighting power, AC COP, and per-fixture
// design vs baseline flow/flush rates. The sustainability engine already computes
// all of this (ConstructionProfile, the per-zone envelope detector, the water
// estimator's fixture flows) but it never reached the export workbook, so a user
// re-keyed every value into EDGE by hand. This POCO carries it onto the run result
// (additive — no snapshot regression) so the EDGE input pack + evidence pack and any
// future report read one source.
//
// Every figure is STING-INDICATIVE: it pre-fills the EDGE App Design tab so a user
// transcribes rather than re-derives; the EDGE App owns the certified number.
//
// Pure POCO — no Revit dependency.

using System.Collections.Generic;

namespace StingTools.Core.Sustainability
{
    /// <summary>One fixture's design vs baseline flow/flush rate, for the EDGE water
    /// measures + the fixture schedule evidence sheet.</summary>
    public sealed class SustainFixtureMeasure
    {
        public string Fixture  { get; set; } = "";   // WC, Urinal, Basin tap, Shower, Kitchen tap
        public double Design   { get; set; }
        public double Baseline { get; set; }
        public string Unit     { get; set; } = "";   // L/flush | L/min
        public double SavingsPct => Baseline > 0 ? (Baseline - Design) / Baseline * 100.0 : 0;
    }

    public sealed class SustainDesignMeasures
    {
        // ── Envelope (ConstructionProfile) ─────────────────────────────────
        public double WallUvalueWm2K   { get; set; }
        public double RoofUvalueWm2K   { get; set; }
        public double FloorUvalueWm2K  { get; set; }
        public double WindowUvalueWm2K { get; set; }
        public double WindowShgc       { get; set; }
        public double WindowShadingFactor { get; set; }
        public string ConstructionProfile { get; set; } = "";   // profile id/label, or "default"

        // ── Glazing / WWR (from the per-zone envelope detector) ────────────
        public double GlazingAreaM2 { get; set; }
        public double ExtWallAreaM2 { get; set; }   // opaque exterior wall (net of glazing)
        public double RoofAreaM2    { get; set; }
        /// <summary>Window-to-wall ratio = glazing / (glazing + opaque wall), 0..1.</summary>
        public double WwrOverall    { get; set; }
        /// <summary>WWR per façade orientation bucket (N/E/S/W). Empty when no oriented glazing.</summary>
        public Dictionary<string, double> WwrByOrientation { get; } =
            new Dictionary<string, double>();

        // ── Systems ────────────────────────────────────────────────────────
        public double LightingWPerM2  { get; set; }   // installed LPD (area-weighted)
        public double CoolingCop       { get; set; }   // AC seasonal COP/SEER used
        public bool   HeatingIsElectric { get; set; }
        public double HeatingEfficiency { get; set; }
        public string AcSystemNote     { get; set; } = "";   // supply mode / PV / heating fuel

        // ── Water fixtures (design vs baseline) ────────────────────────────
        public List<SustainFixtureMeasure> Fixtures { get; } = new List<SustainFixtureMeasure>();
        /// <summary>True when the design flows were read off the model (not the 25%-below
        /// baseline indicative default) — drives the honesty label on the export.</summary>
        public bool FixtureFlowsFromModel { get; set; }

        public List<string> Notes { get; } = new List<string>();
    }
}
