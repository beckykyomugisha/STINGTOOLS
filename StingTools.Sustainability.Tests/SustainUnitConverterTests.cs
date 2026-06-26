using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // Makes the SI/IP "Units" option functional — verify the conversions + that SI
    // is a pass-through (no silent change on the default).
    public class SustainUnitConverterTests
    {
        [Fact]
        public void Si_IsPassThrough()
        {
            Assert.Equal(100, SustainUnitConverter.Eui(100, SustainUnits.SI), 6);
            Assert.Equal(363.3, SustainUnitConverter.CarbonIntensity(363.3, SustainUnits.SI), 6);
            Assert.Equal("kWh/m²·yr", SustainUnitConverter.EuiUnit(SustainUnits.SI));
            Assert.Equal("kgCO₂e/m²", SustainUnitConverter.CarbonIntensityUnit(SustainUnits.SI));
        }

        [Fact]
        public void Ip_ConvertsEui()
        {
            // 100 kWh/m²·yr ≈ 31.70 kBtu/ft²·yr.
            Assert.Equal(31.6998, SustainUnitConverter.Eui(100, SustainUnits.IP), 3);
            Assert.Equal("kBtu/ft²·yr", SustainUnitConverter.EuiUnit(SustainUnits.IP));
        }

        [Fact]
        public void Ip_ConvertsCarbonAndEnergyIntensity()
        {
            Assert.Equal(363.3 * 0.204816, SustainUnitConverter.CarbonIntensity(363.3, SustainUnits.IP), 3);
            Assert.Equal(4359 * 0.088055, SustainUnitConverter.EnergyIntensityMj(4359, SustainUnits.IP), 2);
        }

        [Fact]
        public void Ip_ConvertsWaterAndArea()
        {
            // 35 L/person·day ≈ 9.25 gal/person·day; 2550 m² ≈ 27,448 ft².
            Assert.Equal(35 * 0.264172, SustainUnitConverter.WaterPerPersonDay(35, SustainUnits.IP), 3);
            Assert.Equal(2550 * 10.76391, SustainUnitConverter.Area(2550, SustainUnits.IP), 1);
            Assert.Equal("gal/person·day", SustainUnitConverter.WaterPerPersonDayUnit(SustainUnits.IP));
        }
    }
}
