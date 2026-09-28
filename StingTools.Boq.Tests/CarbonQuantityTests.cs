// CarbonQuantityTests — the unit a carbon factor is in decides what it multiplies.
//
// The carbon tracking command multiplied every factor by mass. MATERIAL_LOOKUP.csv and a
// material's STING_EMB_CARBON_NR are kgCO₂e per m³, so C30 concrete (300 kgCO₂e/m³,
// 2,450 kg/m³) read as 300 × 2,450 = 735,000 kg for one cubic metre instead of 300.
using StingTools.BOQ;
using Xunit;

namespace StingTools.Boq.Tests
{
    public class CarbonQuantityTests
    {
        private static CarbonFactorResult PerM3(double f) => new CarbonFactorResult { Factor = f, PerUnit = CarbonFactorUnit.KgCo2ePerM3 };
        private static CarbonFactorResult PerKg(double f) => new CarbonFactorResult { Factor = f, PerUnit = CarbonFactorUnit.KgCo2ePerKg };

        [Fact]
        public void A_per_m3_factor_multiplies_the_volume_not_the_mass()
        {
            var (fossil, bio) = CarbonQuantity.Split(PerM3(300), volumeM3: 1.0, densityKgM3: 2450, isBiogenic: false);
            Assert.Equal(300, fossil, 6);
            Assert.Equal(0, bio, 6);
        }

        [Fact]
        public void A_per_kg_factor_multiplies_the_mass()
        {
            // 0.5 m³ of steel at 7,850 kg/m³ × 1.55 kgCO₂e/kg = 6,083.75 kg
            var (fossil, bio) = CarbonQuantity.Split(PerKg(1.55), volumeM3: 0.5, densityKgM3: 7850, isBiogenic: false);
            Assert.Equal(6083.75, fossil, 6);
            Assert.Equal(0, bio, 6);
        }

        [Fact]
        public void The_two_units_agree_for_the_same_material()
        {
            // ICE steel 1.55 kg/kg × 7,850 kg/m³ = 12,167.5 kgCO₂e/m³: same answer either way.
            double v = 0.2;
            var perKg = CarbonQuantity.Split(PerKg(1.55), v, 7850, false);
            var perM3 = CarbonQuantity.Split(PerM3(1.55 * 7850), v, 7850, false);
            Assert.Equal(perKg.FossilKg, perM3.FossilKg, 6);
        }

        [Fact]
        public void A_per_m3_fossil_biogenic_split_is_used_as_given()
        {
            var (fossil, bio) = CarbonQuantity.Split(PerM3(-400), 2.0, 480, isBiogenic: true, fossilPerM3: 126, biogenicPerM3: -787);
            Assert.Equal(252, fossil, 6);
            Assert.Equal(-1574, bio, 6);
        }

        [Fact]
        public void Timber_without_a_split_takes_the_timber_per_kg_pair()
        {
            var (fossil, bio) = CarbonQuantity.Split(PerKg(0.31), 1.0, 480, isBiogenic: true);
            Assert.Equal(480 * BiogenicCarbon.TimberFossilPerKg, fossil, 6);
            Assert.Equal(480 * BiogenicCarbon.TimberBiogenicPerKg, bio, 6);
            Assert.True(bio < 0);
        }

        [Theory]
        [InlineData(0.0, 2400.0)]
        [InlineData(-1.0, 2400.0)]
        public void No_volume_is_no_carbon(double volume, double density)
        {
            Assert.Equal((0.0, 0.0), CarbonQuantity.Split(PerM3(300), volume, density, false));
        }

        [Fact]
        public void A_per_kg_factor_with_no_density_is_no_carbon_never_a_guess()
            => Assert.Equal((0.0, 0.0), CarbonQuantity.Split(PerKg(1.55), 1.0, 0, false));

        [Fact]
        public void An_unknown_unit_is_no_carbon()
            => Assert.Equal((0.0, 0.0), CarbonQuantity.Split(
                new CarbonFactorResult { Factor = 300, PerUnit = CarbonFactorUnit.Unknown }, 1.0, 2400, false));
    }
}
