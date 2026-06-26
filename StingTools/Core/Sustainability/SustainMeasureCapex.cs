// StingTools — Sustainability measure capex sizing (Phase 195, WS A5).
//
// Pure POCO / Revit-free + unit-tested. Sizes a green measure's capex from a
// real model-quantity context rather than the crude floor-area proxies the old
// EstimateCapex used. The Revit-facing Sustain_LccBenefit command fills the
// context from the model (PV kWp, glazing m², plumbing-fixture count, cooling
// kW) and this helper does the unit → quantity mapping, so the arithmetic stays
// Revit-free and verifiable.
//
// The resulting rows are fed into the BOQ Cost Manager's manual store as
// BOQLineItem rows (BOQCostManager.SaveManualRows), so the "sustainability
// targets in the DD Cost/Budget Estimate" are real BOQ lines, not a side CSV.

using System;

namespace StingTools.Core.Sustainability
{
    /// <summary>Real model quantities a measure can be sized against. The Revit
    /// command fills these; 0 means "not available in the model" and the helper
    /// falls back to a documented proxy.</summary>
    public class MeasureQuantityContext
    {
        public double PvKwp        { get; set; }
        public double FloorAreaM2  { get; set; }
        public double GlazingAreaM2{ get; set; }
        public int    FixtureCount { get; set; }
        public int    Occupancy    { get; set; }
        /// <summary>Design cooling capacity, kW (from the energy estimate / model);
        /// 0 ⇒ fall back to a ~80 W/m² floor-area proxy.</summary>
        public double CoolingKw    { get; set; }
    }

    public class MeasureCapexResult
    {
        public double Quantity { get; set; }
        public double Capex    { get; set; }
        /// <summary>Human-readable sizing basis, e.g. "glazing m² (model)" /
        /// "cooling kW (≈80 W/m² proxy)".</summary>
        public string BasisLabel { get; set; } = "";
        /// <summary>True when the quantity came from a real model measurement
        /// (not a proxy) — surfaced so the report flags proxy-sized rows.</summary>
        public bool UsedModelQuantity { get; set; }
    }

    public static class SustainMeasureCapex
    {
        private static readonly string[] GlazingKeywords =
            { "glaz", "window", "facade", "façade", "curtain wall", "shad", "solar control" };
        private static readonly string[] FixtureKeywords =
            { "fixture", "fitting", "wc", "toilet", "tap", "urinal", "shower", "low-flow",
              "lowflow", "low flow", "dual-flush", "dual flush", "aerator", "tmv" };

        /// <summary>Size a measure's capex from real model quantities, falling back
        /// to documented proxies when a quantity is absent.</summary>
        public static MeasureCapexResult Compute(GreenMeasure measure, MeasureQuantityContext ctx)
        {
            var r = new MeasureCapexResult();
            if (measure?.Cost == null) return r;
            ctx = ctx ?? new MeasureQuantityContext();

            double rate = measure.Cost.DefaultRate;
            string unit = (measure.Cost.Unit ?? "").Trim().ToLowerInvariant();
            string name = (measure.Name ?? "") + " " + (measure.Description ?? "");

            switch (unit)
            {
                case "kwp":
                    r.Quantity = ctx.PvKwp;
                    r.UsedModelQuantity = ctx.PvKwp > 0;
                    r.BasisLabel = ctx.PvKwp > 0 ? "PV kWp (model)" : "PV kWp (unset — 0)";
                    break;

                case "kw":
                    if (ctx.CoolingKw > 0) { r.Quantity = ctx.CoolingKw; r.UsedModelQuantity = true; r.BasisLabel = "cooling kW (model)"; }
                    else { r.Quantity = ctx.FloorAreaM2 * 0.08; r.BasisLabel = "cooling kW (≈80 W/m² proxy)"; }
                    break;

                case "m2":
                case "m²":
                    if (Mentions(name, GlazingKeywords))
                    {
                        if (ctx.GlazingAreaM2 > 0) { r.Quantity = ctx.GlazingAreaM2; r.UsedModelQuantity = true; r.BasisLabel = "glazing m² (model)"; }
                        else { r.Quantity = ctx.FloorAreaM2; r.BasisLabel = "floor m² (glazing absent — fallback)"; }
                    }
                    else { r.Quantity = ctx.FloorAreaM2; r.UsedModelQuantity = ctx.FloorAreaM2 > 0; r.BasisLabel = "floor m² (model)"; }
                    break;

                case "nr":
                case "no":
                case "each":
                case "item":
                    if (Mentions(name, FixtureKeywords) && ctx.FixtureCount > 0)
                    {
                        r.Quantity = ctx.FixtureCount; r.UsedModelQuantity = true; r.BasisLabel = "fixtures (model)";
                    }
                    else if (ctx.Occupancy > 0)
                    {
                        r.Quantity = Math.Max(1, ctx.Occupancy / 4.0); r.BasisLabel = "≈1 fixture / 4 ppl (proxy)";
                    }
                    else { r.Quantity = 1; r.BasisLabel = "nominal (1)"; }
                    break;

                case "m3":
                case "m³":
                    r.Quantity = Math.Max(1, ctx.FloorAreaM2 / 100.0);
                    r.BasisLabel = "m³ (≈floor/100 proxy)";
                    break;

                default:
                    r.Quantity = 1;
                    r.BasisLabel = "lump sum (1)";
                    break;
            }

            r.Capex = Math.Round(rate * r.Quantity, 2);
            return r;
        }

        private static bool Mentions(string text, string[] keywords)
        {
            string t = (text ?? "").ToLowerInvariant();
            foreach (var k in keywords) if (t.Contains(k)) return true;
            return false;
        }
    }
}
