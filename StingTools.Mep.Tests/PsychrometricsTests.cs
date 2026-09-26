using System;
using StingTools.Core.Hvac;
using Xunit;

namespace StingTools.Mep.Tests
{
    public class PsychrometricsTests
    {
        // Reference values: ASHRAE Handbook — Fundamentals (SI), Ch. 1, Tables 1 and 3.
        [Theory]
        [InlineData(20.0, 2339.2, 0.002)]
        [InlineData(100.0, 101418.0, 0.002)]
        [InlineData(-10.0, 259.87, 0.002)]   // over ice
        [InlineData(0.01, 611.66, 0.003)]
        public void SaturationPressureMatchesAshraeTable(double tC, double expectedPa, double relTol)
        {
            double pws = Psychrometrics.SaturationPressurePa(tC);
            Assert.InRange(pws, expectedPa * (1 - relTol), expectedPa * (1 + relTol));
        }

        [Fact]
        public void StandardAtmosphereAt1500mMatchesAshraeTable()
        {
            Assert.Equal(84.556, Psychrometrics.PressureAtElevation(1500), 2);
            Assert.Equal(101.325, Psychrometrics.PressureAtElevation(0), 6);
        }

        [Fact]
        public void TwentyFiveDegreesFiftyPercentState()
        {
            var s = Psychrometrics.FromDryBulbRh(25, 0.5);
            Assert.Equal(0.00988, s.HumidityRatio, 4);
            Assert.Equal(50.32, s.EnthalpyKJkg, 1);
            // Chart values: dew point ≈ 13.9 °C, wet bulb ≈ 17.9 °C.
            Assert.InRange(s.DewPointC, 13.7, 14.0);
            Assert.InRange(s.WetBulbC, 17.7, 18.1);
            Assert.Equal(0.5, s.RelativeHumidity, 6);
        }

        [Fact]
        public void WetBulbRoundTripsThroughHumidityRatio()
        {
            var s = Psychrometrics.FromDryBulbWetBulb(32, 22);
            Assert.Equal(22.0, s.WetBulbC, 3);
            Assert.True(s.RelativeHumidity > 0.3 && s.RelativeHumidity < 0.5);
        }

        [Fact]
        public void SaturatedAirHasEqualDryBulbWetBulbAndDewPoint()
        {
            var s = Psychrometrics.FromDryBulbRh(15, 1.0);
            Assert.Equal(15.0, s.WetBulbC, 2);
            Assert.Equal(15.0, s.DewPointC, 2);
        }

        [Fact]
        public void MixingIsLinearInHumidityRatioAndEnthalpy()
        {
            var a = Psychrometrics.FromDryBulbRh(32, 0.4);
            var b = Psychrometrics.FromDryBulbRh(24, 0.5);
            var m = Psychrometrics.Mix(a, b, 0.25);
            Assert.Equal(0.25 * a.HumidityRatio + 0.75 * b.HumidityRatio, m.HumidityRatio, 9);
            Assert.Equal(0.25 * a.EnthalpyKJkg + 0.75 * b.EnthalpyKJkg, m.EnthalpyKJkg, 6);
            Assert.InRange(m.DryBulbC, b.DryBulbC, a.DryBulbC);
        }

        [Fact]
        public void CoolingCoilBalancesEnergyAndMass()
        {
            var on = Psychrometrics.FromDryBulbRh(27, 0.5);
            var coil = Psychrometrics.CoolingCoil(on, 13, 0.95, 2.0);

            Assert.Equal(coil.TotalKw, coil.SensibleKw + coil.LatentKw, 6);
            Assert.True(coil.LatentKw > 0, "Air at 27 °C / 50 % cooled to 13 °C / 95 % is dehumidified.");
            double m = 2.0 / on.SpecificVolume;
            Assert.Equal(m * (on.EnthalpyKJkg - coil.Leaving.EnthalpyKJkg), coil.TotalKw, 6);
            Assert.Equal(m * (on.HumidityRatio - coil.Leaving.HumidityRatio) * 3600, coil.CondensateLh, 6);
            Assert.InRange(coil.Shr, 0.4, 1.0);
        }

        [Fact]
        public void ApparatusDewPointIsOnTheSaturationCurveAndBelowLeaving()
        {
            var on = Psychrometrics.FromDryBulbRh(27, 0.5);
            var coil = Psychrometrics.CoolingCoil(on, 13, 0.90, 1.0);
            Assert.False(double.IsNaN(coil.ApparatusDewPointC));
            Assert.True(coil.ApparatusDewPointC < coil.Leaving.DryBulbC);
            Assert.InRange(coil.BypassFactor, 0.0, 1.0);

            // The ADP lies on the straight line entering → leaving, extended.
            double s = (coil.ApparatusDewPointC - on.DryBulbC) / (coil.Leaving.DryBulbC - on.DryBulbC);
            double wLine = on.HumidityRatio + s * (coil.Leaving.HumidityRatio - on.HumidityRatio);
            Assert.Equal(Psychrometrics.SaturationHumidityRatio(coil.ApparatusDewPointC), wLine, 5);
        }

        [Fact]
        public void SensibleOnlyCoolingHasNoAdpAndNoCondensate()
        {
            var on = Psychrometrics.FromDryBulbRh(24, 0.3);
            // Leaving 18 °C at 95 % would need MORE moisture than enters —
            // a coil cannot add water, so this is sensible cooling at entering W.
            var coil = Psychrometrics.CoolingCoil(on, 18, 0.95, 1.0);
            Assert.Equal(on.HumidityRatio, coil.Leaving.HumidityRatio, 9);
            Assert.Equal(0, coil.CondensateLh, 9);
            Assert.True(double.IsNaN(coil.ApparatusDewPointC));
            Assert.Equal(1.0, coil.Shr, 6);
        }

        [Fact]
        public void RejectsImpossibleInputs()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Psychrometrics.FromDryBulbRh(20, 1.2));
            Assert.Throws<ArgumentOutOfRangeException>(() => Psychrometrics.FromDryBulbWetBulb(20, 22));
            Assert.Throws<ArgumentOutOfRangeException>(() => Psychrometrics.FromDryBulbHumidityRatio(20, -0.001));
        }
    }
}
