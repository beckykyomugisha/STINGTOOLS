using StingTools.Core.Hvac.Loads;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS O — occupancy plausibility honesty flag. A modelled headcount that's
    // implausibly dense for the resolved use is FLAGGED (never changed), so an
    // inflated EUI reads as suspect. Thresholds come from data (profile density +
    // override factor), not hardcoded constants.
    public class OccupancyPlausibilityTests
    {
        private static double ResiDensity()
            => LoadProfileLibrary.FromJson(TestData.Read("STING_LOAD_PROFILES.json"))
               .ResolveForUse("residential").Profile.OccupantDensityM2PerPerson;   // ~35

        // The Tendo case: 170 m² dwelling, modelled occupancy 17 (≈10 m²/p, office density).
        [Fact]
        public void Residential17_OnModel_FiresDenseFlag()
        {
            double expected = ResiDensity();
            var r = OccupancyPlausibility.Evaluate(170, 17, "model", "residential", expected);
            Assert.True(r.Flagged);
            Assert.True(r.IsDense);
            Assert.Equal(10, r.ActualDensityM2PerPerson, 0);     // 170 / 17 ≈ 10 m²/p
            Assert.Contains("unusually dense", r.Message);
            Assert.Contains("source: model", r.Message);
        }

        [Fact]
        public void Residential5_OnModel_IsClean_NoFlag()
        {
            double expected = ResiDensity();
            var r = OccupancyPlausibility.Evaluate(170, 5, "model", "residential", expected);
            Assert.False(r.Flagged);                              // 170 / 5 = 34 ≈ profile 35
            Assert.Equal("", r.Message);
        }

        [Fact]
        public void UserExplicitTotal_IsNeverFlagged_EvenIfDense()
        {
            // The user owns an explicit total — a dense value is their choice, not an artifact.
            var r = OccupancyPlausibility.Evaluate(170, 17, "setup", "residential", ResiDensity());
            Assert.False(r.Flagged);
        }

        [Fact]
        public void ThresholdIsDataDriven_FactorFromExpectedDensity_NotAMagicConstant()
        {
            // The same 17 people that flags for residential (~35 m²/p) does NOT flag for
            // office (~10 m²/p): the threshold tracks the resolved profile density.
            var lib = LoadProfileLibrary.FromJson(TestData.Read("STING_LOAD_PROFILES.json"));
            double officeDensity = lib.ResolveForUse("office").Profile.OccupantDensityM2PerPerson;
            var office = OccupancyPlausibility.Evaluate(170, 17, "model", "office", officeDensity);
            Assert.False(office.Flagged);   // 170/17 ≈ 10 == office expected → fine

            var resi = OccupancyPlausibility.Evaluate(170, 17, "model", "residential", ResiDensity());
            Assert.True(resi.Flagged);      // same headcount, different use → flagged
        }

        [Fact]
        public void OverrideFactor_ChangesSensitivity()
        {
            double expected = ResiDensity();
            // A lenient factor (0.25 → dense only below ~8.75 m²/p) does NOT flag 10 m²/p.
            Assert.False(OccupancyPlausibility.Evaluate(170, 17, "model", "residential", expected, denseFactor: 0.25).Flagged);
            // The seed factor 0.5 (below ~17.5 m²/p) does.
            Assert.True(OccupancyPlausibility.Evaluate(170, 17, "model", "residential", expected, denseFactor: 0.5).Flagged);
        }

        [Fact]
        public void SparseValue_FlagsLowerPriority()
        {
            // 1 person in 170 m² residential ≈ 170 m²/p >> 4 × 35 → sparse flag.
            var r = OccupancyPlausibility.Evaluate(170, 1, "model", "residential", ResiDensity());
            Assert.True(r.Flagged);
            Assert.True(r.IsSparse);
        }

        [Fact]
        public void NoneSourceOrZero_NeverFlags()
        {
            Assert.False(OccupancyPlausibility.Evaluate(170, 0, "none", "residential", ResiDensity()).Flagged);
            Assert.False(OccupancyPlausibility.Evaluate(0, 17, "model", "residential", ResiDensity()).Flagged);
        }
    }
}
