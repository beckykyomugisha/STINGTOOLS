using System.Collections.Generic;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // Gap fix #3 — when a material resolves NO real carbon factor (absent from the
    // EPD / library / mass DB) but its mass is known, apply a generic indicative
    // class factor so the figure isn't a flat 0. The indicative carbon populates the
    // display + hotspots but must stay OUT of the real WBLCA (CarbonStampedLines /
    // Computed / WblcaCompleted).
    public class MaterialIndicativeCarbonTests
    {
        [Fact]
        public void NoFactor_KnownMass_AppliesIndicativeClassFactor()
        {
            var o = SustainMaterialCarbon.Compute(new MaterialCarbonInputs
            {
                Material = "Concrete C32/40", VolumeM3 = 10, DensityKgM3 = 2400
            }, new FactorSourceOrder());

            // mass 24 000 kg × indicative concrete 0.13 = 3 120.
            Assert.True(o.IndicativeOnly);
            Assert.Equal("indicative-class", o.CarbonSource);
            Assert.Equal(MaterialFactorBasis.PerKgViaDensity, o.Basis);
            Assert.Equal(24000 * 0.13, o.NetCarbonKg, 0);
            Assert.Equal(o.NetCarbonKg, o.FossilCarbonKg, 6);
            Assert.Equal(0, o.BiogenicCarbonKg, 6);
        }

        [Fact]
        public void NoFactor_NoDensity_StaysZero_NotIndicative()
        {
            var o = SustainMaterialCarbon.Compute(new MaterialCarbonInputs
            {
                Material = "Unknown material", VolumeM3 = 5, DensityKgM3 = 0
            }, new FactorSourceOrder());

            Assert.Equal(0, o.NetCarbonKg, 6);
            Assert.False(o.IndicativeOnly);
            Assert.Equal(MaterialFactorBasis.None, o.Basis);
        }

        [Fact]
        public void DisabledPerKgFactor_NotOverriddenByIndicative()
        {
            // Per-kg factor exists but FactorSources disables mass DBs — a deliberate
            // exclusion; the indicative fallback must NOT silently re-add carbon.
            var order = new FactorSourceOrder { EmbodiedCarbon = new List<string> { "EPD_specific" } };
            var o = SustainMaterialCarbon.Compute(new MaterialCarbonInputs
            {
                Material = "Reinforcing steel", VolumeM3 = 2, DensityKgM3 = 7850, NetFactorPerKg = 1.55
            }, order);

            Assert.Equal(0, o.NetCarbonKg, 6);
            Assert.False(o.IndicativeOnly);
            Assert.Contains("disabled-by-factorsources", o.CarbonSource);
        }

        [Fact]
        public void ClassFactor_DiffersByMaterial()
        {
            Assert.Equal(0.13, SustainMaterialCarbon.IndicativeClassFactorPerKg("Concrete"), 3);
            Assert.Equal(1.55, SustainMaterialCarbon.IndicativeClassFactorPerKg("Structural Steel"), 3);
            Assert.Equal(8.50, SustainMaterialCarbon.IndicativeClassFactorPerKg("Aluminium Frame"), 3);
            // Unknown → broad default.
            Assert.Equal(SustainMaterialCarbon.IndicativeDefaultKgCo2ePerKg,
                         SustainMaterialCarbon.IndicativeClassFactorPerKg("Mystery Composite"), 3);
        }

        [Fact]
        public void ClassFactor_LongestKeywordWins()
            // "reinforced concrete" (0.16) must beat the shorter "concrete" (0.13).
            => Assert.Equal(0.16, SustainMaterialCarbon.IndicativeClassFactorPerKg("Reinforced Concrete slab"), 3);

        [Fact]
        public void Rollup_IndicativeOnly_NotCountedAsRealWblca()
        {
            var line = ToLine(SustainMaterialCarbon.Compute(new MaterialCarbonInputs
            {
                Material = "Concrete", VolumeM3 = 10, DensityKgM3 = 2400
            }, new FactorSourceOrder()), "Concrete");

            var res = MaterialsRollup.Rollup(new[] { line }, 2550);

            Assert.True(res.TotalCarbonKg > 0);             // a figure is shown…
            Assert.Equal(0, res.CarbonStampedLines);        // …but it isn't a real WBLCA
            Assert.Equal(1, res.IndicativeCarbonLines);
            Assert.True(res.CarbonIsIndicative);
            Assert.False(res.Computed);                     // gate stays "not computed / indicative"
            Assert.False(res.WblcaCompleted);
            Assert.Single(res.Hotspots);                    // still shows the hotspot
            Assert.Contains(res.Warnings, w => w.Contains("INDICATIVE"));
        }

        [Fact]
        public void Rollup_MixedRealAndIndicative_IsComputed_WithNote()
        {
            var real = ToLine(SustainMaterialCarbon.Compute(new MaterialCarbonInputs
            {
                Material = "Glazing", VolumeM3 = 2, NetFactorPerM3 = 4000
            }, new FactorSourceOrder()), "Glazing");
            var indicative = ToLine(SustainMaterialCarbon.Compute(new MaterialCarbonInputs
            {
                Material = "Concrete", VolumeM3 = 10, DensityKgM3 = 2400
            }, new FactorSourceOrder()), "Concrete");

            var res = MaterialsRollup.Rollup(new[] { real, indicative }, 2550);

            Assert.Equal(1, res.CarbonStampedLines);
            Assert.Equal(1, res.IndicativeCarbonLines);
            Assert.False(res.CarbonIsIndicative);           // not WHOLLY indicative
            Assert.True(res.Computed);                      // at least one real line
            Assert.Contains(res.Warnings, w => w.Contains("indicative class factors"));
        }

        private static MaterialLine ToLine(MaterialCarbonOutputs o, string name) => new MaterialLine
        {
            Material = name, VolumeM3 = o.GrossVolumeM3, MassKg = o.MassKg,
            CarbonKg = o.NetCarbonKg, FossilCarbonKg = o.FossilCarbonKg,
            BiogenicCarbonKg = o.BiogenicCarbonKg, EnergyMj = o.EnergyMj,
            FromEpd = o.FromEpd, CarbonSource = o.CarbonSource, IndicativeOnly = o.IndicativeOnly
        };
    }
}
