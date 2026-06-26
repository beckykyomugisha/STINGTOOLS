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

        private static SchemeContext Ctx(double energyPct, double waterPct, double matPct)
        {
            return new SchemeContext
            {
                Energy   = new EnergyEstimateResult { EnergySavingsPct = energyPct },
                Water    = new WaterEstimateResult  { WaterSavingsPct = waterPct },
                Materials = new MaterialsRollupResult
                {
                    EmbodiedEnergySavingsPct = matPct,
                    WblcaCompleted = true
                }
            };
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

            Assert.True(res.Passed);
            Assert.Equal("Advanced", res.AchievedLevel);
            Assert.All(res.Gates, g => Assert.True(g.Passed));
        }

        [Fact]
        public void Edge_Advanced_OneGateFails_FailsEntireScheme()
        {
            var reg = LoadShippedSchemes();
            var edge = reg.Get("EDGE");
            var providers = MetricProviderRegistry.CreateStandard();

            // Energy 42 OK, water 25 OK, materials only 18 < 20 -> overall fail.
            var res = SchemeEvaluator.Evaluate(edge, "Advanced", providers, Ctx(42, 25, 18));

            Assert.False(res.Passed);
            var mat = res.Gates.Find(g => g.GateId == "materials");
            Assert.False(mat.Passed);
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
            var ctx = new SchemeContext
            {
                Materials = new MaterialsRollupResult
                {
                    WblcaCompleted = true,
                    GwpReductionPct = 22
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
