using System.Collections.Generic;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // Spec §14: EDGE all-required AND logic (one gate fails -> fail); level
    // threshold switch (Certified 20 vs Advanced 40); LEED pointSum + band mapping.
    public class SchemeEvaluatorTests
    {
        private static GreenSchemeRegistry LoadShippedSchemes()
        {
            string json = TestData.Read("STING_GREEN_SCHEMES.json");
            return GreenSchemeRegistry.LoadFromJson(json);
        }

        // Build a context whose estimator results are genuinely COMPUTED (real
        // floor area, zones, design energy, baselines) so the not-computed guard
        // doesn't fire — these tests exercise the gate logic, not the zero-data path.
        private static SchemeContext Ctx(double energyPct, double waterPct, double matPct)
        {
            var energy = new EnergyEstimateResult
            {
                EnergySavingsPct = energyPct, FloorAreaM2 = 1000, ZoneCount = 1, BaselineEuiKwhM2Yr = 200
            };
            energy.Design.CoolingKwh = 50000;   // TotalKwh > 0 ⇒ Computed
            var water = new WaterEstimateResult
            {
                WaterSavingsPct = waterPct, BaselineLPersonDay = 50, DesignLPersonDay = 40,
                IsIndicativeDefault = false
            };
            var mat = new MaterialsRollupResult
            {
                EmbodiedEnergySavingsPct = matPct, WblcaCompleted = true,
                FloorAreaM2 = 1000, TotalCarbonKg = 1000, TotalEnergyMj = 12000, HasEnergyBaseline = true
            };
            return new SchemeContext { Energy = energy, Water = water, Materials = mat };
        }

        [Fact]
        public void Edge_Advanced_AllGatesMeet_Passes()
        {
            var reg = LoadShippedSchemes();
            var edge = reg.Get("EDGE");
            Assert.NotNull(edge);

            var providers = MetricProviderRegistry.CreateStandard();
            // 42% energy, 25% water, 22% materials >= Advanced (40/20/20).
            var res = SchemeEvaluator.Evaluate(edge, "Advanced", providers, Ctx(42, 25, 22));

            // STING-determinable result = energy + water (materials is delegated to
            // the EDGE app and never blocks).
            Assert.True(res.Passed);
            Assert.Equal("Advanced", res.AchievedLevel);
            Assert.True(res.Gates.Find(g => g.GateId == "energy").Passed);
            Assert.True(res.Gates.Find(g => g.GateId == "water").Passed);
            Assert.True(res.Gates.Find(g => g.GateId == "materials").Delegated);
        }

        [Fact]
        public void Edge_Advanced_OneGateFails_FailsEntireScheme()
        {
            var reg = LoadShippedSchemes();
            var edge = reg.Get("EDGE");
            var providers = MetricProviderRegistry.CreateStandard();

            // Energy only 18 < 40 (Advanced) -> a determinable gate fails -> scheme fails.
            // (Materials is delegated and can't fail the STING result, so we fail it on
            // a determinable gate — the correct certification-tool behaviour.)
            var res = SchemeEvaluator.Evaluate(edge, "Advanced", providers, Ctx(18, 25, 22));

            Assert.False(res.Passed);
            var energy = res.Gates.Find(g => g.GateId == "energy");
            Assert.False(energy.Passed);
        }

        [Fact]
        public void Edge_LevelThresholdSwitch_CertifiedVsAdvanced()
        {
            var reg = LoadShippedSchemes();
            var edge = reg.Get("EDGE");
            var providers = MetricProviderRegistry.CreateStandard();

            // 30% energy passes Certified (20) but fails Advanced (40).
            var ctx = Ctx(30, 25, 22);

            var certified = SchemeEvaluator.Evaluate(edge, "Certified", providers, ctx);
            Assert.True(certified.Passed);
            var energyCert = certified.Gates.Find(g => g.GateId == "energy");
            Assert.Equal(20, energyCert.Threshold);

            var advanced = SchemeEvaluator.Evaluate(edge, "Advanced", providers, ctx);
            Assert.False(advanced.Passed);
            var energyAdv = advanced.Gates.Find(g => g.GateId == "energy");
            Assert.Equal(40, energyAdv.Threshold);
            Assert.False(energyAdv.Passed);
        }

        [Fact]
        public void Edge_AchievedLevel_ReportsHighestWhenEnergyHigh()
        {
            var reg = LoadShippedSchemes();
            var edge = reg.Get("EDGE");
            var providers = MetricProviderRegistry.CreateStandard();

            // 45% energy, 25% water, 22% materials -> meets Advanced thresholds.
            var res = SchemeEvaluator.Evaluate(edge, "Certified", providers, Ctx(45, 25, 22));
            Assert.Equal("Advanced", res.AchievedLevel);
        }

        [Fact]
        public void Leed_PointSum_StepFunctionAndBand()
        {
            var reg = LoadShippedSchemes();
            var leed = reg.Get("LEED");
            Assert.NotNull(leed);
            var providers = MetricProviderRegistry.CreateStandard();

            // WBLCA done (prereq) + 22% GWP reduction -> step gives 3 pts (>=20).
            // Carbon must be genuinely computed (real area + total carbon) for the
            // gwp gate to score — a zero-carbon model awards 0, not 3.
            var ctx = new SchemeContext
            {
                Materials = new MaterialsRollupResult
                {
                    WblcaCompleted = true,
                    GwpReductionPct = 22,
                    FloorAreaM2 = 1000,
                    TotalCarbonKg = 1000
                }
            };
            var res = SchemeEvaluator.Evaluate(leed, "Certified", providers, ctx);

            Assert.Equal(3, res.TotalPoints);
            // 3 points is below the Certified band (40) -> band None but prereq ok.
            Assert.Equal("None", res.Band);
            var prereq = res.Gates.Find(g => g.GateId == "ec_prereq");
            Assert.True(prereq.Passed);
        }

        [Fact]
        public void Leed_PrereqFails_BandIsNone()
        {
            var reg = LoadShippedSchemes();
            var leed = reg.Get("LEED");
            var providers = MetricProviderRegistry.CreateStandard();

            var ctx = new SchemeContext
            {
                Materials = new MaterialsRollupResult { WblcaCompleted = false, GwpReductionPct = 45 }
            };
            var res = SchemeEvaluator.Evaluate(leed, "Certified", providers, ctx);

            Assert.False(res.Passed);
            Assert.Equal("None", res.Band);
        }

        [Fact]
        public void StepPoints_ReturnsHighestMatchingTier()
        {
            var steps = new List<PointStep>
            {
                new PointStep { Pct = 10, Pts = 1 },
                new PointStep { Pct = 20, Pts = 3 },
                new PointStep { Pct = 40, Pts = 6 }
            };
            Assert.Equal(0, SchemeEvaluator.StepPoints(steps, 5));
            Assert.Equal(1, SchemeEvaluator.StepPoints(steps, 12));
            Assert.Equal(3, SchemeEvaluator.StepPoints(steps, 25));
            Assert.Equal(6, SchemeEvaluator.StepPoints(steps, 50));
        }

        [Fact]
        public void Compare_OperatorsBehaveCorrectly()
        {
            Assert.True(SchemeEvaluator.Compare(40, ">=", 40));
            Assert.False(SchemeEvaluator.Compare(39.9, ">=", 40));
            Assert.True(SchemeEvaluator.Compare(41, ">", 40));
            Assert.True(SchemeEvaluator.Compare(40, "==", 40));
        }

        [Fact]
        public void Edge_ZeroDesignEnergy_IsNotComputed_NeverPasses()
        {
            // The headline bug: a model with no zones gives design EUI 0, so
            // (baseline-0)/baseline = 100%. That must NOT read as a pass.
            var reg = LoadShippedSchemes();
            var edge = reg.Get("EDGE");
            var providers = MetricProviderRegistry.CreateStandard();

            var ctx = new SchemeContext
            {
                Energy   = new EnergyEstimateResult { EnergySavingsPct = 100, ZoneCount = 0, FloorAreaM2 = 0 },
                Water    = new WaterEstimateResult  { WaterSavingsPct = 25, BaselineLPersonDay = 50, DesignLPersonDay = 40 },
                Materials = new MaterialsRollupResult { WblcaCompleted = false }
            };
            var res = SchemeEvaluator.Evaluate(edge, "Advanced", providers, ctx);

            var energy = res.Gates.Find(g => g.GateId == "energy");
            Assert.False(energy.Computed);   // not computed
            Assert.False(energy.Passed);     // therefore never a pass, despite 100%
            Assert.False(res.Passed);
            Assert.Equal("None", res.AchievedLevel);
        }

        [Fact]
        public void Edge_IndicativeDefaultWater_IsNotComputed_NeverPasses()
        {
            // Water flows that are the hardcoded 25%-over-baseline placeholder must
            // be flagged not-computed, not shown as a confident pass.
            var reg = LoadShippedSchemes();
            var edge = reg.Get("EDGE");
            var providers = MetricProviderRegistry.CreateStandard();

            var energy = new EnergyEstimateResult { EnergySavingsPct = 45, FloorAreaM2 = 1000, ZoneCount = 1, BaselineEuiKwhM2Yr = 200 };
            energy.Design.CoolingKwh = 50000;
            var ctx = new SchemeContext
            {
                Energy = energy,
                Water  = new WaterEstimateResult
                {
                    WaterSavingsPct = 25, BaselineLPersonDay = 50, DesignLPersonDay = 37.5,
                    IsIndicativeDefault = true   // placeholder, not model data
                },
                Materials = new MaterialsRollupResult { WblcaCompleted = false }
            };
            var res = SchemeEvaluator.Evaluate(edge, "Advanced", providers, ctx);

            var water = res.Gates.Find(g => g.GateId == "water");
            Assert.False(water.Computed);
            Assert.False(water.Passed);
            Assert.False(res.Passed);   // a determinable gate (water) is not computed
        }

        [Fact]
        public void AddingNewScheme_NeedsNoEngineCode()
        {
            // Spec §13.8 — a hypothetical BREEAM scheme defined purely in JSON runs
            // through the SAME evaluator + providers with zero engine changes.
            string breeam = @"{
              ""schema"": ""sting.green.schemes/v1"",
              ""schemes"": [{
                ""id"": ""BREEAM"", ""name"": ""BREEAM stub"", ""aggregation"": ""all_required"",
                ""levels"": { ""Pass"": { ""energy"": 15 } },
                ""gates"": [
                  { ""id"": ""energy"", ""metric"": ""energy_savings_pct"", ""provider"": ""AnnualEnergyEstimator"", ""operator"": "">="", ""required"": true }
                ]
              }]
            }";
            var reg = GreenSchemeRegistry.LoadFromJson(breeam);
            var scheme = reg.Get("BREEAM");
            Assert.NotNull(scheme);

            var providers = MetricProviderRegistry.CreateStandard();
            var res = SchemeEvaluator.Evaluate(scheme, "Pass", providers, Ctx(18, 0, 0));
            Assert.True(res.Passed);
        }
    }
}
