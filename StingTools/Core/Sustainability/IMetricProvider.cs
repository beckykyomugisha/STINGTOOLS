// StingTools — Metric provider abstraction (Phase 195, spec §4).
//
// Certifications are DATA, not code. The SchemeEvaluator never names a scheme;
// it pulls named metrics from registered IMetricProviders. A provider may supply
// several metrics (e.g. MaterialsRollup supplies embodied_energy_savings_pct,
// gwp_reduction_pct AND wblca_completed). The MetricProviderRegistry maps a
// scheme gate's "provider" string -> a provider instance.
//
// SchemeContext carries the already-computed estimator results so providers are
// pure adapters — they don't re-run the engines. This keeps the whole layer
// Revit-free and unit-testable.

using System;
using System.Collections.Generic;

namespace StingTools.Core.Sustainability
{
    /// <summary>Everything a provider needs to answer a metric query. Populated by
    /// the dashboard command (which runs the four estimators once), then handed to
    /// every provider so the scheme evaluation is a pure lookup.</summary>
    public class SchemeContext
    {
        public EnergyEstimateResult Energy   { get; set; }
        public WaterEstimateResult  Water    { get; set; }
        public MaterialsRollupResult Materials { get; set; }
        public BaselineResolution   Baseline { get; set; }

        /// <summary>WS B5 — EDGE-app certified savings %, keyed by gate-metric id
        /// (energy_savings_pct / water_savings_pct / embodied_energy_savings_pct).
        /// When present for a metric, the provider returns the official value and
        /// flags it certified (computed), and the evaluator stops treating that gate
        /// as delegated — so the determined EDGE level reflects the official number.</summary>
        public Dictionary<string, double> OfficialOverrides { get; set; }

        public bool HasOfficial(string metric)
            => OfficialOverrides != null && !string.IsNullOrEmpty(metric) && OfficialOverrides.ContainsKey(metric);

        public double GetOfficial(string metric)
            => (OfficialOverrides != null && metric != null && OfficialOverrides.TryGetValue(metric, out var v)) ? v : 0;
    }

    public interface IMetricProvider
    {
        /// <summary>Provider id matching the scheme gate's "provider" field.</summary>
        string Id { get; }
        MetricResult Evaluate(SchemeContext ctx);
    }

    public class MetricProviderRegistry
    {
        private readonly Dictionary<string, IMetricProvider> _providers
            = new Dictionary<string, IMetricProvider>(StringComparer.OrdinalIgnoreCase);

        public void Register(IMetricProvider p)
        {
            if (p != null && !string.IsNullOrEmpty(p.Id)) _providers[p.Id] = p;
        }

        public IMetricProvider Get(string id)
            => (!string.IsNullOrEmpty(id) && _providers.TryGetValue(id, out var p)) ? p : null;

        /// <summary>The standard STING provider set (energy / water / materials).</summary>
        public static MetricProviderRegistry CreateStandard()
        {
            var r = new MetricProviderRegistry();
            r.Register(new AnnualEnergyMetricProvider());
            r.Register(new AnnualWaterMetricProvider());
            r.Register(new MaterialsRollupMetricProvider());
            return r;
        }
    }

    // ── Standard providers — pure adapters over the estimator results ──────

    public class AnnualEnergyMetricProvider : IMetricProvider
    {
        public string Id => "AnnualEnergyEstimator";
        public MetricResult Evaluate(SchemeContext ctx)
        {
            var r = new MetricResult();
            if (ctx?.Energy != null)
            {
                r.Numbers["energy_savings_pct"] = ctx.Energy.EnergySavingsPct;
                r.SetComputed("energy_savings_pct", ctx.Energy.Computed,
                    ctx.Energy.Computed ? null
                        : (ctx.Energy.ZoneCount == 0
                            ? "no Spaces / floor area — add Spaces or enter GFA in Setup"
                            : "zero design energy — check area / occupancy / COP"));
            }
            MetricProviderOfficial.ApplyOfficial(r, ctx,"energy_savings_pct");
            return r;
        }
    }

    public class AnnualWaterMetricProvider : IMetricProvider
    {
        public string Id => "AnnualWaterEstimator";
        public MetricResult Evaluate(SchemeContext ctx)
        {
            var r = new MetricResult();
            if (ctx?.Water != null)
            {
                // EDGE credits alternative water (RWH + greywater) toward the water
                // gate, so the gate uses the alt-inclusive %, not fixture-only.
                r.Numbers["water_savings_pct"] = ctx.Water.WaterSavingsInclAltPct;
                r.SetComputed("water_savings_pct", ctx.Water.Computed,
                    ctx.Water.Computed ? null
                        : "indicative default — no low-flow fixture data read from the model");
            }
            MetricProviderOfficial.ApplyOfficial(r, ctx,"water_savings_pct");
            return r;
        }
    }

    public class MaterialsRollupMetricProvider : IMetricProvider
    {
        public string Id => "MaterialsRollup";
        public MetricResult Evaluate(SchemeContext ctx)
        {
            var r = new MetricResult();
            if (ctx?.Materials != null)
            {
                var m = ctx.Materials;
                r.Numbers["embodied_energy_savings_pct"] = m.EmbodiedEnergySavingsPct;
                r.Numbers["gwp_reduction_pct"]           = m.GwpReductionPct;
                r.Bools["wblca_completed"]               = m.WblcaCompleted;

                // EDGE materials is embodied-energy %. STING can compute an INDICATIVE
                // value only when a real embodied-energy baseline (MJ/m²) exists AND
                // material data resolved; otherwise it's delegated to the EDGE app.
                // (The gate is flagged `delegated` in the scheme so it never blocks the
                // STING-determinable result either way — it's shown beside the official
                // field, not used to award a level.)
                bool energyComputable = m.HasEnergyBaseline && m.FloorAreaM2 > 0 && m.TotalEnergyMj > 0;
                r.SetComputed("embodied_energy_savings_pct", energyComputable,
                    energyComputable ? null
                        : "EDGE app owns the certified materials %; STING tracks selections + indicative MJ");
                r.SetComputed("gwp_reduction_pct", m.Computed,
                    m.Computed ? null
                        : (m.FloorAreaM2 <= 0 ? "no floor area (GFA)"
                                              : $"{m.TotalLines} measured, 0 carbon-stamped — run a carbon pass"));
                r.SetComputed("wblca_completed", m.Computed);
            }
            // EDGE materials gate = embodied-energy %; the official figure overrides
            // the indicative AND makes the (otherwise-delegated) gate evaluable.
            MetricProviderOfficial.ApplyOfficial(r, ctx,"embodied_energy_savings_pct");
            return r;
        }
    }

    // ── WS B5 — shared EDGE-official override applied by every provider ─────
    internal static class MetricProviderOfficial
    {
        public static void ApplyOfficial(MetricResult r, SchemeContext ctx, string metric)
        {
            if (r == null || ctx == null || !ctx.HasOfficial(metric)) return;
            r.Numbers[metric] = ctx.GetOfficial(metric);
            r.SetComputed(metric, true, "EDGE-app official %");
        }
    }
}
