using StingTools.Core.Hvac.Loads;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS A2 — the energy estimate now consumes a REAL envelope (conduction +
    // per-façade solar) that the orchestrator derives via the shared
    // EnvelopeDetector. These tests prove the envelope genuinely moves the result:
    // a temperate vs hot-tropical office produce sensibly different EUIs, and the
    // envelope U-value changes the load (acceptance criterion 1).
    public class EnvelopeEnergyTests
    {
        private static ClimateMonthlySite Climate(double meanDb, double ghi)
        {
            var s = new ClimateMonthlySite { Id = "c", AnnualGhiKwhM2Yr = ghi * 365 };
            for (int m = 0; m < 12; m++) { s.MeanDbC[m] = meanDb; s.GhiKwhM2Day[m] = ghi; s.MeanRhPct[m] = 70; }
            return s;
        }

        private static LoadZone OfficeWithEnvelope(double wallU = 0.30, double windowU = 1.4, double shgc = 0.4)
        {
            var z = new LoadZone
            {
                Id = "z", Name = "office", FloorAreaM2 = 1000, HeightM = 3,
                OccupantCount = 80, LightingWPerM2 = 9, EquipmentWPerM2 = 12,
                CoolingSetpointC = 24, HeatingSetpointC = 21,
                OaLpsPerPerson = 10, OaLpsPerM2 = 0.3, InfiltrationAch = 0.3
            };
            z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.ExteriorWall, AreaM2 = 240, UvalueWm2K = wallU, OrientationDeg = 180 });
            z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.Window, AreaM2 = 120, UvalueWm2K = windowU, SHGC = shgc, OrientationDeg = 180 });
            z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.Roof, AreaM2 = 1000, UvalueWm2K = 0.20, OrientationDeg = 0 });
            return z;
        }

        [Fact]
        public void TemperateVsTropicalOffice_ProduceDifferentEui()
        {
            // Same building, same envelope — only the climate site differs.
            var tropical = AnnualEnergyEstimator.Estimate(new[] { OfficeWithEnvelope() }, Climate(30, 5.5), null, 3.0);
            var temperate = AnnualEnergyEstimator.Estimate(new[] { OfficeWithEnvelope() }, Climate(11, 2.8), null, 3.0);

            Assert.True(tropical.DesignEuiKwhM2Yr > 0);
            Assert.True(temperate.DesignEuiKwhM2Yr > 0);
            // The two climates must produce sensibly different EUIs (not a fixed
            // climate-independent number).
            double rel = System.Math.Abs(tropical.DesignEuiKwhM2Yr - temperate.DesignEuiKwhM2Yr)
                         / temperate.DesignEuiKwhM2Yr;
            Assert.True(rel > 0.10, $"EUIs too close: tropical {tropical.DesignEuiKwhM2Yr:F1} vs temperate {temperate.DesignEuiKwhM2Yr:F1}");
            // Tropical is cooling-led; temperate carries meaningful heating.
            Assert.True(tropical.Design.CoolingKwh > temperate.Design.CoolingKwh);
            Assert.True(temperate.Design.HeatingKwh > tropical.Design.HeatingKwh);
        }

        [Fact]
        public void EnvelopeUValue_AffectsConductionLoad()
        {
            var cold = Climate(2, 1.5);
            var leaky = AnnualEnergyEstimator.Estimate(new[] { OfficeWithEnvelope(wallU: 1.2, windowU: 3.0) }, cold, null, 3.0);
            var tight = AnnualEnergyEstimator.Estimate(new[] { OfficeWithEnvelope(wallU: 0.15, windowU: 0.8) }, cold, null, 3.0);

            // A high-U envelope loses more heat ⇒ more heating energy than a tight one.
            Assert.True(leaky.Design.HeatingKwh > tight.Design.HeatingKwh);
        }

        [Fact]
        public void EnvelopeShgc_AffectsSolarCoolingLoad()
        {
            var hot = Climate(31, 6.0);
            var clear = AnnualEnergyEstimator.Estimate(new[] { OfficeWithEnvelope(shgc: 0.7) }, hot, null, 3.0);
            var solarControl = AnnualEnergyEstimator.Estimate(new[] { OfficeWithEnvelope(shgc: 0.25) }, hot, null, 3.0);

            // Lower-SHGC glazing lets in less solar ⇒ less cooling.
            Assert.True(clear.Design.CoolingKwh > solarControl.Design.CoolingKwh);
        }
    }
}
