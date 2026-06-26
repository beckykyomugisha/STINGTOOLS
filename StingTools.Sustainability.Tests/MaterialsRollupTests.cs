using System.Collections.Generic;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // Spec §14: dual-metric resolution; hotspot identification; EPD-specific
    // factor preferred when SUS_EPD_REF_TXT set (modelled here via FromEpd lines).
    public class MaterialsRollupTests
    {
        private static List<MaterialLine> Lines() => new List<MaterialLine>
        {
            new MaterialLine { Material = "Concrete C32/40", Category = "Structure", VolumeM3 = 200, CarbonKg = 60000, EnergyMj = 800000 },
            new MaterialLine { Material = "Reinforcing steel", Category = "Structure", VolumeM3 = 5, CarbonKg = 30000, EnergyMj = 350000, FromEpd = true },
            new MaterialLine { Material = "Glazing", Category = "Envelope", VolumeM3 = 2, CarbonKg = 8000, EnergyMj = 90000 },
        };

        [Fact]
        public void Rollup_ComputesBothMetricIntensities()
        {
            var res = MaterialsRollup.Rollup(Lines(), floorAreaM2: 2550);

            // Total carbon 98000 kg / 2550 m2 = 38.4 kgCO2e/m2.
            Assert.Equal(98000.0 / 2550.0, res.CarbonIntensityKgM2, 2);
            // Total energy 1,240,000 MJ / 2550 = 486.3 MJ/m2.
            Assert.Equal(1240000.0 / 2550.0, res.EnergyIntensityMjM2, 1);
            // Carbon and energy are never conflated.
            Assert.NotEqual(res.CarbonIntensityKgM2, res.EnergyIntensityMjM2);
        }

        [Fact]
        public void Rollup_IdentifiesThreeCarbonHotspots()
        {
            var res = MaterialsRollup.Rollup(Lines(), 2550);
            Assert.Equal(3, res.Hotspots.Count);
            // Largest carbon contributor first (concrete 60000).
            Assert.Equal("Concrete C32/40", res.Hotspots[0].Material);
            Assert.True(res.Hotspots[0].SharePct > res.Hotspots[1].SharePct);
        }

        [Fact]
        public void Rollup_WblcaCompleted_WhenCarbonPresent()
        {
            var res = MaterialsRollup.Rollup(Lines(), 2550);
            Assert.True(res.WblcaCompleted);
        }

        [Fact]
        public void Rollup_EmptyLines_FlagsNoCarbonData()
        {
            var res = MaterialsRollup.Rollup(new List<MaterialLine>(), 2550);
            Assert.False(res.WblcaCompleted);
            Assert.Contains(res.Warnings, w => w.Contains("No embodied-carbon"));
        }

        [Fact]
        public void Rollup_GwpReduction_VsCarbonBaseline()
        {
            // Design 38.4 kgCO2e/m2 vs baseline 50 -> ~23% reduction.
            var res = MaterialsRollup.Rollup(Lines(), 2550, carbonBaselineKgM2: 50);
            double design = 98000.0 / 2550.0;
            double expected = (50 - design) / 50 * 100;
            Assert.Equal(expected, res.GwpReductionPct, 2);
        }

        [Fact]
        public void Rollup_EmbodiedEnergySavings_VsEnergyBaseline()
        {
            var res = MaterialsRollup.Rollup(Lines(), 2550, energyBaselineMjM2: 600);
            double design = 1240000.0 / 2550.0;
            double expected = (600 - design) / 600 * 100;
            Assert.Equal(expected, res.EmbodiedEnergySavingsPct, 2);
        }

        [Fact]
        public void Rollup_NoEnergyBaseline_DelegatesToEdgeApp()
        {
            var res = MaterialsRollup.Rollup(Lines(), 2550, energyBaselineMjM2: null);
            Assert.Equal(0, res.EmbodiedEnergySavingsPct);
            Assert.Contains(res.Warnings, w => w.Contains("delegated"));
        }

        [Fact]
        public void Rollup_CountsEpdBackedLines()
        {
            var res = MaterialsRollup.Rollup(Lines(), 2550);
            Assert.Equal(1, res.LinesFromEpd);
            Assert.Equal(3, res.TotalLines);
        }
    }
}
