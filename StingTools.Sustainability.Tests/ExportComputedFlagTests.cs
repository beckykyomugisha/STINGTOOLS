using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS I4 — the EDGE export must print "not computed / indicative default" for any
    // gate whose Computed flag is false (matching the dashboard), never a bare number.
    public class ExportComputedFlagTests
    {
        [Fact]
        public void Computed_PrintsTheValue()
        {
            Assert.Equal("42.0%", SustainExportFormat.GateValue(true, 42.0, "0.0", "%"));
        }

        [Fact]
        public void NotComputed_PrintsIndicativeText_NotANumber()
        {
            string cell = SustainExportFormat.GateValue(false, 42.0, "0.0", "%");
            Assert.Equal(SustainExportFormat.NotComputed, cell);
            Assert.DoesNotContain("42", cell);
        }

        [Fact]
        public void IndicativeWaterResult_IsNotComputed_SoExportSuppressesNumber()
        {
            // A water result with no model fixtures is the indicative default → not
            // computed → the export must not emit its %.
            var w = new WaterEstimateResult { IsIndicativeDefault = true, BaselineLPersonDay = 40, DesignLPersonDay = 30, WaterSavingsInclAltPct = 25 };
            Assert.False(w.Computed);
            Assert.Equal(SustainExportFormat.NotComputed,
                SustainExportFormat.GateValue(w.Computed, w.WaterSavingsInclAltPct, "0.0", "%"));
        }

        [Fact]
        public void BlockedRun_ForcesNotComputed_EvenIfEstimatorComputed()
        {
            // Estimator says computed, but the run is blocked (location/use unset) →
            // export folds readiness in and still suppresses the number.
            bool ready = false;
            bool estimatorComputed = true;
            string cell = SustainExportFormat.GateValue(ready && estimatorComputed, 33.3, "0.0", "%");
            Assert.Equal(SustainExportFormat.NotComputed, cell);
        }

        [Fact]
        public void Delegated_NeverBareNumber()
        {
            string cell = SustainExportFormat.Delegated(18.0, "0.0", "%");
            Assert.Contains("EDGE app", cell);
            Assert.Contains("indicative", cell);
        }
    }
}
