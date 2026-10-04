using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core.Electrical;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// The AIC tier a board needs (FaultCurrentEngine / AicRatingCommand). The old lookup
    /// fell back to the LARGEST tier when the fault exceeded every tier — a device rating
    /// below the fault it must break.
    /// </summary>
    public class AicTierTests
    {
        private static readonly double[] Tiers = { 6, 10, 16, 18, 22, 25, 35, 42, 50, 65, 85, 100 };

        [Theory]
        [InlineData(4.0, 6)]      // 4.4 → 6
        [InlineData(9.0, 10)]     // 9.9 → 10
        [InlineData(9.2, 16)]     // 10.12 → 16
        [InlineData(60.0, 85)]    // 66 → 85
        public void Picks_the_smallest_tier_covering_fault_plus_margin(double faultKa, double expected)
            => Assert.Equal(expected, AicTier.Next(faultKa, Tiers));

        [Theory]
        [InlineData(120.0)]       // above every tier
        [InlineData(95.0)]        // 104.5 with margin: above every tier although 100 ≥ 95
        public void A_fault_beyond_every_tier_gets_no_tier_never_a_smaller_one(double faultKa)
        {
            double t = AicTier.Next(faultKa, Tiers);
            Assert.Equal(0, t);
            Assert.Contains("exceeds the largest standard tier (100 kA)", AicTier.NoTierReason(faultKa, Tiers));
        }

        [Fact]
        public void No_tiers_loaded_gives_no_tier_not_the_fault_level()
        {
            Assert.Equal(0, AicTier.Next(12.3, new double[0]));
            Assert.Equal(0, AicTier.Next(12.3, null));
            Assert.Contains("no AIC tiers loaded", AicTier.NoTierReason(12.3, new double[0]));
        }

        [Fact]
        public void Unsorted_tiers_are_searched_in_order()
            => Assert.Equal(25, AicTier.Next(20, new double[] { 100, 25, 6, 50 }));

        /// <summary>STING_AIC_TIERS.json's safetyMarginPct was never read: the code always used 10 %.</summary>
        [Fact]
        public void The_data_file_margin_is_read_not_ignored()
        {
            var set = AicTier.Parse("{\"tiers_kA\":[10,16,25],\"safetyMarginPct\":25}");
            Assert.Equal(25, set.MarginPct);
            Assert.Equal(new double[] { 10, 16, 25 }, set.Tiers);
            // 9 kA: 10 % margin → 10 kA tier; the file's 25 % → 16 kA.
            Assert.Equal(16, AicTier.Next(9, set.Tiers, set.MarginPct));
        }

        [Theory]
        [InlineData("{\"tiers_kA\":[10]}")]
        [InlineData("{\"tiers_kA\":[10],\"safetyMarginPct\":-5}")]
        [InlineData("{\"tiers_kA\":[10],\"safetyMarginPct\":\"ten\"}")]
        public void A_missing_or_invalid_margin_falls_back_to_ten_percent(string json)
            => Assert.Equal(AicTier.DefaultMarginPct, AicTier.Parse(json).MarginPct);

        [Fact]
        public void The_shipped_tier_file_parses_to_sorted_tiers_and_its_margin()
        {
            var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "StingTools.addin"))) dir = dir.Parent;
            Assert.NotNull(dir);
            string json = System.IO.File.ReadAllText(System.IO.Path.Combine(dir.FullName, "StingTools", "Data", "STING_AIC_TIERS.json"));
            var set = AicTier.Parse(json);
            Assert.NotEmpty(set.Tiers);
            Assert.Equal(set.Tiers.OrderBy(x => x), set.Tiers);
            var root = Newtonsoft.Json.Linq.JObject.Parse(json);
            Assert.Equal(root["safetyMarginPct"].Value<double>(), set.MarginPct);
        }

        [Fact]
        public void Unknown_fault_level_gives_no_tier()
        {
            Assert.Equal(0, AicTier.Next(0, Tiers));
            Assert.Equal("fault level not calculated", AicTier.NoTierReason(0, Tiers));
        }
    }
}
