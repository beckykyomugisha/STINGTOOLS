using System.Collections.Generic;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS A3 — material-carbon resolution shared with the BOQ takeoff:
    //   per-m³ AND per-kg-via-density factors, wastage allowance, fossil/biogenic
    //   WLCA split (sequestration credited into net), and FactorSources order
    //   driving whether a mass-based DB per-kg fallback is permitted.
    public class MaterialCarbonTests
    {
        private static FactorSourceOrder DefaultOrder() => new FactorSourceOrder();

        [Fact]
        public void PerM3Factor_AppliesWaste_ToMeasuredVolume()
        {
            var o = SustainMaterialCarbon.Compute(new MaterialCarbonInputs
            {
                Material = "Concrete C32/40", VolumeM3 = 10, DensityKgM3 = 2400,
                WastePercent = 5, NetFactorPerM3 = 120
            }, DefaultOrder());

            Assert.Equal(10.5, o.GrossVolumeM3, 3);          // waste grosses the volume
            Assert.Equal(25200, o.MassKg, 1);                // 10.5 × 2400
            Assert.Equal(1260, o.NetCarbonKg, 1);            // 10.5 × 120
            Assert.Equal(MaterialFactorBasis.PerM3, o.Basis);
            Assert.Equal(1260, o.FossilCarbonKg, 1);
            Assert.Equal(0, o.BiogenicCarbonKg, 6);
        }

        [Fact]
        public void PerKgFactor_AppliedViaDensity_ClosesTheDroppedGap()
        {
            // Steel: only a per-kg (ICE) factor — the OLD engine dropped this entirely.
            var o = SustainMaterialCarbon.Compute(new MaterialCarbonInputs
            {
                Material = "Reinforcing steel", VolumeM3 = 2, DensityKgM3 = 7850,
                WastePercent = 0, NetFactorPerM3 = 0, NetFactorPerKg = 1.55
            }, DefaultOrder());

            Assert.Equal(15700, o.MassKg, 1);                // 2 × 7850
            Assert.Equal(24335, o.NetCarbonKg, 1);           // 15700 × 1.55
            Assert.Equal(MaterialFactorBasis.PerKgViaDensity, o.Basis);
            Assert.Equal("ice-per-kg", o.CarbonSource);
        }

        [Fact]
        public void FactorSources_RemovingMassDb_DisablesPerKgFallback()
        {
            var order = new FactorSourceOrder
            {
                // No ICE / Ecoinvent — only volumetric EPD/EC3 datasets permitted.
                EmbodiedCarbon = new List<string> { "EPD_specific", "EC3_regional" }
            };
            var o = SustainMaterialCarbon.Compute(new MaterialCarbonInputs
            {
                Material = "Reinforcing steel", VolumeM3 = 2, DensityKgM3 = 7850,
                NetFactorPerKg = 1.55
            }, order);

            Assert.Equal(0, o.NetCarbonKg, 6);               // per-kg fallback disabled
            Assert.Equal(MaterialFactorBasis.None, o.Basis);
            Assert.Contains("disabled-by-factorsources", o.CarbonSource);
        }

        [Fact]
        public void Timber_CreditsBiogenicSequestration_IntoNet()
        {
            // No explicit split → derived from the ICE BiogenicCarbon constants.
            var o = SustainMaterialCarbon.Compute(new MaterialCarbonInputs
            {
                Material = "Softwood Timber", VolumeM3 = 5, DensityKgM3 = 480
            }, DefaultOrder());

            Assert.Equal(2400, o.MassKg, 1);                 // 5 × 480
            Assert.Equal(2400 * 0.263, o.FossilCarbonKg, 1); // 631.2
            Assert.Equal(2400 * -1.64, o.BiogenicCarbonKg, 1);// -3936
            Assert.Equal(631.2 - 3936, o.NetCarbonKg, 1);    // net is dragged negative
            Assert.True(o.NetCarbonKg < o.FossilCarbonKg);   // credit genuinely applied
        }

        [Fact]
        public void ExplicitPerM3Split_SetsNetToFossilPlusBiogenic()
        {
            var o = SustainMaterialCarbon.Compute(new MaterialCarbonInputs
            {
                Material = "CLT panel", VolumeM3 = 10, DensityKgM3 = 500,
                FossilFactorPerM3 = 63, BiogenicFactorPerM3 = -120
            }, DefaultOrder());

            Assert.Equal(630, o.FossilCarbonKg, 1);
            Assert.Equal(-1200, o.BiogenicCarbonKg, 1);
            Assert.Equal(-570, o.NetCarbonKg, 1);
        }

        [Fact]
        public void Energy_PrefersEpdPerM3_OverRatioFallback()
        {
            var o = SustainMaterialCarbon.Compute(new MaterialCarbonInputs
            {
                Material = "Glazing", VolumeM3 = 2, NetFactorPerM3 = 4000,
                EnergyMjPerM3 = 5000
            }, DefaultOrder());

            Assert.Equal(10000, o.EnergyMj, 1);              // 2 × 5000 (EPD PERT+PENRT)
            Assert.Equal("epd-pert-penrt", o.EnergySource);
        }

        [Fact]
        public void Energy_RatioFallback_WhenNoFactorStamped()
        {
            var o = SustainMaterialCarbon.Compute(new MaterialCarbonInputs
            {
                Material = "Blockwork", VolumeM3 = 1, NetFactorPerM3 = 100
            }, DefaultOrder());

            // 100 kgCO2e fossil × 12 MJ/kgCO2e indicative ratio.
            Assert.Equal(1200, o.EnergyMj, 1);
            Assert.Equal("indicative-ratio", o.EnergySource);
        }

        [Fact]
        public void Energy_PerKgIce_WhenMassEnergyDbPermitted()
        {
            var o = SustainMaterialCarbon.Compute(new MaterialCarbonInputs
            {
                Material = "Reinforcing steel", VolumeM3 = 1, DensityKgM3 = 7850,
                NetFactorPerKg = 1.55, EnergyMjPerKg = 20
            }, DefaultOrder());   // default order includes ICE_v3_MJ

            Assert.Equal(157000, o.EnergyMj, 1);             // 7850 × 20
            Assert.Equal("ice-mj-per-kg", o.EnergySource);
        }

        [Fact]
        public void PerM3_Preferred_WhenBothFactorsPresent()
        {
            var o = SustainMaterialCarbon.Compute(new MaterialCarbonInputs
            {
                Material = "Aluminium", VolumeM3 = 1, DensityKgM3 = 2700,
                NetFactorPerM3 = 100, NetFactorPerKg = 2
            }, DefaultOrder());

            Assert.Equal(MaterialFactorBasis.PerM3, o.Basis);
            Assert.Equal(100, o.NetCarbonKg, 1);             // volumetric wins
        }

        [Fact]
        public void ZeroVolume_ProducesEmptyResult()
        {
            var o = SustainMaterialCarbon.Compute(new MaterialCarbonInputs
            {
                Material = "x", VolumeM3 = 0, NetFactorPerM3 = 100
            }, DefaultOrder());
            Assert.Equal(0, o.NetCarbonKg, 6);
            Assert.Equal(MaterialFactorBasis.None, o.Basis);
        }

        [Fact]
        public void Rollup_FoldsSplit_AndBiogenicTotalsAcrossLines()
        {
            var steel = SustainMaterialCarbon.Compute(new MaterialCarbonInputs
            {
                Material = "Reinforcing steel", VolumeM3 = 2, DensityKgM3 = 7850, NetFactorPerKg = 1.55
            }, DefaultOrder());
            var timber = SustainMaterialCarbon.Compute(new MaterialCarbonInputs
            {
                Material = "Glulam", VolumeM3 = 5, DensityKgM3 = 480
            }, DefaultOrder());

            var lines = new List<MaterialLine>
            {
                new MaterialLine { Material = "Reinforcing steel", VolumeM3 = 2, MassKg = steel.MassKg,
                    CarbonKg = steel.NetCarbonKg, FossilCarbonKg = steel.FossilCarbonKg,
                    BiogenicCarbonKg = steel.BiogenicCarbonKg, EnergyMj = steel.EnergyMj },
                new MaterialLine { Material = "Glulam", VolumeM3 = 5, MassKg = timber.MassKg,
                    CarbonKg = timber.NetCarbonKg, FossilCarbonKg = timber.FossilCarbonKg,
                    BiogenicCarbonKg = timber.BiogenicCarbonKg, EnergyMj = timber.EnergyMj },
            };

            var res = MaterialsRollup.Rollup(lines, floorAreaM2: 1000);

            Assert.True(res.TotalBiogenicCarbonKg < 0);                       // sequestration present
            Assert.Equal(steel.FossilCarbonKg + timber.FossilCarbonKg, res.TotalFossilCarbonKg, 1);
            Assert.Equal(steel.MassKg + timber.MassKg, res.TotalMassKg, 1);
            Assert.True(res.WblcaCompleted);                                  // computed even though timber net ≤ 0
        }
    }
}
