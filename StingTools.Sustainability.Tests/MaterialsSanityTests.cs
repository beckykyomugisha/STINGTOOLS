using System.Collections.Generic;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS I5 — materials sanity + coverage: warn when one hotspot dominates or the
    // total is implausibly high, and surface coverage (stamped/measured, EPD count).
    public class MaterialsSanityTests
    {
        private static MaterialLine Line(string mat, double carbon, bool epd = false, bool indicative = false)
            => new MaterialLine { Material = mat, CarbonKg = carbon, FossilCarbonKg = carbon, FromEpd = epd, IndicativeOnly = indicative };

        [Fact]
        public void DominantHotspot_OverShare_IsFlagged()
        {
            // Steel purlins ~92% of the total — the live-model error.
            var lines = new List<MaterialLine>
            {
                Line("Steel Purlins", 4775),
                Line("Concrete", 300),
                Line("Blockwork", 125),
            };
            var res = MaterialsRollup.Rollup(lines, floorAreaM2: 1.0);   // tiny area to also trip ceiling
            Assert.True(res.DominantHotspotImplausible);
            Assert.Equal("Steel Purlins", res.DominantHotspotMaterial);
            Assert.True(res.DominantHotspotSharePct > 60);
            Assert.Contains(res.Warnings, w => w.Contains("Steel Purlins") && w.Contains("quantity/factor"));
        }

        [Fact]
        public void IntensityOverCeiling_IsFlagged()
        {
            var lines = new List<MaterialLine> { Line("Concrete", 5_212_000) };
            var res = MaterialsRollup.Rollup(lines, floorAreaM2: 1000);   // 5212 kgCO2e/m²
            Assert.True(res.IntensityImplausible);
            Assert.Contains(res.Warnings, w => w.Contains("implausibly high"));
        }

        [Fact]
        public void NormalBuilding_NoSanityFlags()
        {
            var lines = new List<MaterialLine>
            {
                Line("Concrete", 250_000), Line("Steel", 150_000), Line("Blockwork", 100_000),
                Line("Timber", 50_000), Line("Glass", 40_000),
            };
            var res = MaterialsRollup.Rollup(lines, floorAreaM2: 2000);   // ~295 kgCO2e/m²
            Assert.False(res.DominantHotspotImplausible);
            Assert.False(res.IntensityImplausible);
        }

        [Fact]
        public void Coverage_ReportsStampedMeasuredEpd()
        {
            // 2 stamped (1 EPD) of 3 measured; 1 indicative.
            var lines = new List<MaterialLine>
            {
                Line("Concrete", 250_000, epd: true),
                Line("Steel", 150_000),
                Line("Blockwork", 100_000, indicative: true),
            };
            var res = MaterialsRollup.Rollup(lines, floorAreaM2: 2000);
            Assert.Equal(3, res.TotalLines);
            Assert.Equal(2, res.CarbonStampedLines);
            Assert.Equal(1, res.IndicativeCarbonLines);
            Assert.Equal(1, res.LinesFromEpd);
            Assert.Contains("2/3 carbon-stamped", res.CoverageSummary);
            Assert.Contains("1 EPD", res.CoverageSummary);
        }
    }
}
