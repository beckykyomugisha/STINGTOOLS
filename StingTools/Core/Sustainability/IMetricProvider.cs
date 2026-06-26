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
                r.Numbers["energy_savings_pct"] = ctx.Energy.EnergySavingsPct;
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
                r.Numbers["water_savings_pct"] = ctx.Water.WaterSavingsPct;
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
                r.Numbers["embodied_energy_savings_pct"] = ctx.Materials.EmbodiedEnergySavingsPct;
                r.Numbers["gwp_reduction_pct"]           = ctx.Materials.GwpReductionPct;
                r.Bools["wblca_completed"]               = ctx.Materials.WblcaCompleted;
            }
            return r;
        }
    }
}
