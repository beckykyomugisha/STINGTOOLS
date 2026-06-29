using System.Collections.Generic;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS C3 — embodied-ENERGY (MJ/kg) seed registry: keyword resolution
    // (longest-match-wins), project override, and the end-to-end MJ path through
    // SustainMaterialCarbon using a real ICE figure instead of the ratio fallback.
    public class IceEmbodiedEnergyTests
    {
        private static IceEmbodiedEnergyRegistry Corp()
            => IceEmbodiedEnergyRegistry.LoadFromJson(TestData.Read("STING_ICE_EMBODIED_ENERGY.json"));

        [Fact]
        public void Seed_ResolvesCommonMaterials()
        {
            var reg = Corp();
            Assert.True(reg.GetMjPerKg("Concrete C32/40") > 0);
            Assert.True(reg.GetMjPerKg("Reinforcing steel") > 0);
            Assert.True(reg.GetMjPerKg("Softwood timber") > 0);
        }

        [Fact]
        public void Match_LongestKeywordWins()
        {
            var reg = Corp();
            // "reinforced concrete" must beat plain "concrete".
            double rc = reg.GetMjPerKg("Cast-in-situ Reinforced Concrete");
            double plain = reg.GetMjPerKg("Mass Concrete fill");
            Assert.NotEqual(rc, plain);
            Assert.Equal(1.04, rc, 2);
            Assert.Equal(0.95, plain, 2);
        }

        [Fact]
        public void UnknownMaterial_ReturnsZero_NotInvented()
        {
            var reg = Corp();
            Assert.Equal(0, reg.GetMjPerKg("Unobtainium"), 6);
            Assert.Equal(0, reg.GetMjPerKg(null), 6);
        }

        [Fact]
        public void ProjectOverride_ReplacesCorporateRow_ByKeyword()
        {
            const string corp = @"{ ""materials"": [ { ""match"": ""concrete"", ""mjPerKg"": 0.95 } ] }";
            const string proj = @"{ ""materials"": [ { ""match"": ""concrete"", ""mjPerKg"": 1.20, ""source"": ""project EPD"" } ] }";
            var reg = IceEmbodiedEnergyRegistry.LoadFromJson(corp, proj);
            Assert.Equal(1.20, reg.GetMjPerKg("Ready-mix concrete"), 2);
            Assert.Equal("project EPD", reg.Match("concrete").Source);
        }

        [Fact]
        public void ProjectOverride_AddsNewKeyword()
        {
            const string corp = @"{ ""materials"": [ { ""match"": ""steel"", ""mjPerKg"": 20.1 } ] }";
            const string proj = @"{ ""materials"": [ { ""match"": ""rammed earth"", ""mjPerKg"": 0.45 } ] }";
            var reg = IceEmbodiedEnergyRegistry.LoadFromJson(corp, proj);
            Assert.Equal(0.45, reg.GetMjPerKg("Stabilised rammed earth wall"), 2);
            Assert.Equal(20.1, reg.GetMjPerKg("Structural steel"), 2);
        }

        [Fact]
        public void MaterialCarbon_UsesRealIceMj_NotRatioFallback()
        {
            var reg = Corp();
            double mjPerKg = reg.GetMjPerKg("Reinforcing steel");

            // Steel with a per-kg carbon factor + the ICE MJ seed: the energy must
            // come from the real ICE figure, not the carbon×12 ratio.
            var o = SustainMaterialCarbon.Compute(new MaterialCarbonInputs
            {
                Material = "Reinforcing steel", VolumeM3 = 1, DensityKgM3 = 7850,
                NetFactorPerKg = 1.55, EnergyMjPerKg = mjPerKg
            }, new FactorSourceOrder());   // default order permits ICE_v3_MJ

            Assert.Equal("ice-mj-per-kg", o.EnergySource);
            Assert.Equal(7850 * mjPerKg, o.EnergyMj, 1);
        }

        [Fact]
        public void MaterialCarbon_IceMj_DisabledWhenFactorSourcesExcludeMassDb()
        {
            var reg = Corp();
            double mjPerKg = reg.GetMjPerKg("Reinforcing steel");
            var order = new FactorSourceOrder
            {
                EmbodiedEnergy = new List<string> { "EPD_PERT_PENRT" }   // no ICE_v3_MJ
            };
            var o = SustainMaterialCarbon.Compute(new MaterialCarbonInputs
            {
                Material = "Reinforcing steel", VolumeM3 = 1, DensityKgM3 = 7850,
                NetFactorPerKg = 1.55, EnergyMjPerKg = mjPerKg
            }, order);

            // Mass-energy DB excluded ⇒ falls back to the documented ratio, never the
            // ICE per-kg figure.
            Assert.Equal("indicative-ratio", o.EnergySource);
        }
    }
}
