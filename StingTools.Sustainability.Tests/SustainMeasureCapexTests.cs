using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS A5 — measure capex sized from REAL model quantities (PV kWp, glazing m²,
    // fixture count) instead of crude floor-area proxies; falls back to a
    // documented proxy + flags it when the model quantity is absent.
    public class SustainMeasureCapexTests
    {
        private static GreenMeasure Measure(string name, string unit, double rate, string gate = "energy")
            => new GreenMeasure
            {
                Id = name, Name = name, Gate = gate,
                Cost = new CostHandle { Unit = unit, DefaultRate = rate }
            };

        [Fact]
        public void Kwp_SizedFromModelPvCapacity()
        {
            var r = SustainMeasureCapex.Compute(
                Measure("Rooftop PV", "kWp", 900),
                new MeasureQuantityContext { PvKwp = 50 });

            Assert.Equal(50, r.Quantity, 3);
            Assert.Equal(45000, r.Capex, 2);     // 50 × 900
            Assert.True(r.UsedModelQuantity);
            Assert.Contains("model", r.BasisLabel);
        }

        [Fact]
        public void GlazingMeasure_SizedFromModelGlazingArea_NotFloor()
        {
            var r = SustainMeasureCapex.Compute(
                Measure("Low-e glazing upgrade", "m2", 120),
                new MeasureQuantityContext { GlazingAreaM2 = 300, FloorAreaM2 = 2550 });

            Assert.Equal(300, r.Quantity, 3);    // glazing, not floor
            Assert.Equal(36000, r.Capex, 2);
            Assert.True(r.UsedModelQuantity);
            Assert.Contains("glazing", r.BasisLabel);
        }

        [Fact]
        public void GlazingMeasure_FallsBackToFloor_WhenNoGlazingModelled()
        {
            var r = SustainMeasureCapex.Compute(
                Measure("Solar control glazing", "m2", 120),
                new MeasureQuantityContext { GlazingAreaM2 = 0, FloorAreaM2 = 2550 });

            Assert.Equal(2550, r.Quantity, 3);
            Assert.False(r.UsedModelQuantity);   // proxy, flagged
            Assert.Contains("fallback", r.BasisLabel);
        }

        [Fact]
        public void GenericM2Measure_SizedFromFloorArea()
        {
            var r = SustainMeasureCapex.Compute(
                Measure("Cool roof coating", "m2", 25),
                new MeasureQuantityContext { FloorAreaM2 = 1000, GlazingAreaM2 = 300 });

            Assert.Equal(1000, r.Quantity, 3);   // not glazing — no glazing keyword
            Assert.Contains("floor", r.BasisLabel);
        }

        [Fact]
        public void FixtureMeasure_SizedFromModelFixtureCount()
        {
            var r = SustainMeasureCapex.Compute(
                Measure("Low-flow WC fittings", "nr", 80, gate: "water"),
                new MeasureQuantityContext { FixtureCount = 42, Occupancy = 200 });

            Assert.Equal(42, r.Quantity, 3);     // real fixtures, not occupancy/4
            Assert.Equal(3360, r.Capex, 2);
            Assert.True(r.UsedModelQuantity);
            Assert.Contains("fixtures", r.BasisLabel);
        }

        [Fact]
        public void FixtureMeasure_FallsBackToOccupancyProxy_WhenNoFixtures()
        {
            var r = SustainMeasureCapex.Compute(
                Measure("Low-flow taps", "nr", 80, gate: "water"),
                new MeasureQuantityContext { FixtureCount = 0, Occupancy = 200 });

            Assert.Equal(50, r.Quantity, 3);     // 200 / 4
            Assert.False(r.UsedModelQuantity);
            Assert.Contains("proxy", r.BasisLabel);
        }

        [Fact]
        public void CoolingKw_PrefersModelCapacity_OverFloorProxy()
        {
            var withModel = SustainMeasureCapex.Compute(
                Measure("High-COP chiller", "kw", 300),
                new MeasureQuantityContext { CoolingKw = 200, FloorAreaM2 = 1000 });
            Assert.Equal(200, withModel.Quantity, 3);
            Assert.True(withModel.UsedModelQuantity);

            var proxy = SustainMeasureCapex.Compute(
                Measure("High-COP chiller", "kw", 300),
                new MeasureQuantityContext { CoolingKw = 0, FloorAreaM2 = 1000 });
            Assert.Equal(80, proxy.Quantity, 3); // 1000 × 0.08
            Assert.False(proxy.UsedModelQuantity);
            Assert.Contains("proxy", proxy.BasisLabel);
        }

        [Fact]
        public void UnknownUnit_FallsBackToLumpSum()
        {
            var r = SustainMeasureCapex.Compute(
                Measure("BMS optimisation", "lump", 15000),
                new MeasureQuantityContext { FloorAreaM2 = 1000 });
            Assert.Equal(1, r.Quantity, 3);
            Assert.Equal(15000, r.Capex, 2);
            Assert.Contains("lump", r.BasisLabel);
        }

        [Fact]
        public void NullContext_DoesNotThrow_AndUnsetPvIsZero()
        {
            var r = SustainMeasureCapex.Compute(Measure("PV", "kWp", 900), null);
            Assert.Equal(0, r.Quantity, 6);
            Assert.Equal(0, r.Capex, 6);
            Assert.False(r.UsedModelQuantity);
        }

        // ── Review fix #1 — explicit quantityBasis disambiguates the "nr" unit ──

        private static GreenMeasure MeasureBasis(string name, string unit, double rate, string basis,
                                                 string desc = "", string gate = "water")
            => new GreenMeasure
            {
                Id = name, Name = name, Gate = gate, Description = desc,
                Cost = new CostHandle { Unit = unit, DefaultRate = rate, QuantityBasis = basis }
            };

        [Fact]
        public void Basis_System_IsOneUnit_NotMultipliedByFixtureCount()
        {
            // The grey-water regression: a whole-building SYSTEM whose description
            // mentions "WC" used to trip the fixture-keyword path and get multiplied
            // by the fixture count (12000 × 57 = 684000). With basis "system" it is 1.
            var r = SustainMeasureCapex.Compute(
                MeasureBasis("Grey-water reuse", "nr", 12000, "system",
                             desc: "Treated grey-water reused for WC flushing / irrigation."),
                new MeasureQuantityContext { FixtureCount = 57, Occupancy = 200, FloorAreaM2 = 2000 });

            Assert.Equal(1, r.Quantity, 6);
            Assert.Equal(12000, r.Capex, 2);     // NOT 684000
            Assert.True(r.UsedModelQuantity);    // a definite qty, not a proxy estimate
            Assert.Contains("system", r.BasisLabel);
        }

        [Fact]
        public void Basis_PerFixture_UsesFixtureCount_EvenWithoutAFixtureKeyword()
        {
            var r = SustainMeasureCapex.Compute(
                MeasureBasis("Aerated outlet retrofit", "nr", 65, "perFixture"),  // no fixture word
                new MeasureQuantityContext { FixtureCount = 42, Occupancy = 200 });

            Assert.Equal(42, r.Quantity, 6);
            Assert.Equal(2730, r.Capex, 2);
            Assert.True(r.UsedModelQuantity);
            Assert.Contains("fixtures", r.BasisLabel);
        }

        [Fact]
        public void Basis_PerKwp_OverridesUnit()
        {
            var r = SustainMeasureCapex.Compute(
                MeasureBasis("PV array", "nr", 900, "perKwp", gate: "energy"),  // unit lies; basis wins
                new MeasureQuantityContext { PvKwp = 30, FixtureCount = 50 });

            Assert.Equal(30, r.Quantity, 6);
            Assert.Equal(27000, r.Capex, 2);
            Assert.Contains("kWp", r.BasisLabel);
        }

        [Fact]
        public void EmptyBasis_FallsBackToUnitInference()
        {
            var r = SustainMeasureCapex.Compute(
                MeasureBasis("Rooftop PV", "kWp", 900, ""),   // empty basis ⇒ legacy unit path
                new MeasureQuantityContext { PvKwp = 50 });
            Assert.Equal(50, r.Quantity, 6);
            Assert.Equal(45000, r.Capex, 2);
        }

        [Fact]
        public void UnknownBasis_FallsBackToUnitInference()
        {
            var r = SustainMeasureCapex.Compute(
                MeasureBasis("Cool roof", "m2", 25, "perSquareCubit"),  // unknown ⇒ infer from unit
                new MeasureQuantityContext { FloorAreaM2 = 1000 });
            Assert.Equal(1000, r.Quantity, 6);
            Assert.Contains("floor", r.BasisLabel);
        }

        [Fact]
        public void ShippedGreywaterMeasure_ResolvesToOneSystem_NotPerFixture()
        {
            // Regression against the REAL corporate JSON: load the shipped measures,
            // size grey-water against a model with fixtures, and prove it's one system.
            var reg = GreenMeasureRegistry.LoadFromJson(TestData.Read("STING_GREEN_MEASURES.json"));
            var grey = reg.Get("greywater_reuse");
            Assert.NotNull(grey);
            Assert.Equal("system", grey.Cost.QuantityBasis);

            var r = SustainMeasureCapex.Compute(grey,
                new MeasureQuantityContext { FixtureCount = 57, FloorAreaM2 = 2000, Occupancy = 200 });
            Assert.Equal(1, r.Quantity, 6);
            Assert.Equal(grey.Cost.DefaultRate, r.Capex, 2);   // one system × rate, not × 57
        }
    }
}
