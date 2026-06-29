using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS H6 — the EDGE export honours the project's SI/IP units. These pin the
    // conversions + labels the export sheets emit on the IP path (the export builds
    // a ClosedXML workbook, so the testable layer is SustainUnitConverter, which the
    // export now calls for every absolute value + header).
    public class EdgeExportUnitsTests
    {
        [Fact]
        public void Si_IsPassThrough_WithMetricLabels()
        {
            var u = SustainUnits.SI;
            Assert.Equal(120, SustainUnitConverter.Eui(120, u), 6);
            Assert.Equal("kWh/m²·yr", SustainUnitConverter.EuiUnit(u));
            Assert.Equal(1000, SustainUnitConverter.WaterVolume(1000, u), 6);
            Assert.Equal("L", SustainUnitConverter.WaterVolumeUnit(u));
            Assert.Equal(500, SustainUnitConverter.MassCarbon(500, u), 6);
            Assert.Equal("kgCO₂e", SustainUnitConverter.MassCarbonUnit(u));
        }

        [Fact]
        public void Ip_ConvertsEnergyWaterCarbonArea_WithImperialLabels()
        {
            var u = SustainUnits.IP;

            // Energy: EUI + absolute kWh.
            Assert.Equal(120 * SustainUnitConverter.KwhM2ToKbtuFt2, SustainUnitConverter.Eui(120, u), 4);
            Assert.Equal("kBtu/ft²·yr", SustainUnitConverter.EuiUnit(u));
            Assert.Equal(1000 * SustainUnitConverter.KwhToKbtu, SustainUnitConverter.EnergyAbs(1000, u), 3);
            Assert.Equal("kBtu/yr", SustainUnitConverter.EnergyAbsUnit(u));

            // Water: per-person + absolute volume.
            Assert.Equal(40 * SustainUnitConverter.LToGal, SustainUnitConverter.WaterPerPersonDay(40, u), 4);
            Assert.Equal("gal/person·day", SustainUnitConverter.WaterPerPersonDayUnit(u));
            Assert.Equal(50000 * SustainUnitConverter.LToGal, SustainUnitConverter.WaterVolume(50000, u), 2);
            Assert.Equal("gal", SustainUnitConverter.WaterVolumeUnit(u));

            // Carbon: intensity + absolute mass.
            Assert.Equal(300 * SustainUnitConverter.KgM2ToLbFt2, SustainUnitConverter.CarbonIntensity(300, u), 4);
            Assert.Equal("lbCO₂e/ft²", SustainUnitConverter.CarbonIntensityUnit(u));
            Assert.Equal(500 * SustainUnitConverter.KgToLb, SustainUnitConverter.MassCarbon(500, u), 3);
            Assert.Equal("lbCO₂e", SustainUnitConverter.MassCarbonUnit(u));

            // Materials energy intensity + area.
            Assert.Equal(800 * SustainUnitConverter.MjM2ToKbtuFt2, SustainUnitConverter.EnergyIntensityMj(800, u), 4);
            Assert.Equal("kBtu/ft²", SustainUnitConverter.EnergyIntensityUnit(u));
            Assert.Equal(2000 * SustainUnitConverter.M2ToFt2, SustainUnitConverter.Area(2000, u), 2);
            Assert.Equal("ft²", SustainUnitConverter.AreaUnit(u));
        }

        [Fact]
        public void Ip_DiffersFromSi_ForAllExportFigures()
        {
            // Every absolute figure the export emits must differ between SI and IP
            // (i.e. the export is genuinely unit-aware, not always-SI).
            Assert.NotEqual(SustainUnitConverter.Eui(120, SustainUnits.SI),
                            SustainUnitConverter.Eui(120, SustainUnits.IP));
            Assert.NotEqual(SustainUnitConverter.WaterVolume(1000, SustainUnits.SI),
                            SustainUnitConverter.WaterVolume(1000, SustainUnits.IP));
            Assert.NotEqual(SustainUnitConverter.MassCarbon(500, SustainUnits.SI),
                            SustainUnitConverter.MassCarbon(500, SustainUnits.IP));
        }
    }
}
