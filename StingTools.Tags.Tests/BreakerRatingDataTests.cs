using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Commands.Electrical.VoltageDrop;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DSCH-25 — protective-device rating lists. STING_WIRE_TABLES.json → breakerSizes is
    /// the ONLY copy: VoltageDropEngine had a second NEC list that stopped at 400 A (the
    /// data stopped at 1200 A), and NECStandards a third. The expected lists below are
    /// written from the standards, not copied from the code or the data, so a drifted
    /// data file fails here.
    /// </summary>
    public class BreakerRatingDataTests
    {
        /// <summary>NEC 2023 Table 240.6(A), fuses and inverse-time circuit breakers.</summary>
        private static readonly int[] NecTable240_6A =
        {
            15, 20, 25, 30, 35, 40, 45, 50, 60, 70, 80, 90, 100, 110, 125, 150, 175, 200,
            225, 250, 300, 350, 400, 450, 500, 600, 700, 800, 1000, 1200, 1600, 2000, 2500,
            3000, 4000, 5000, 6000
        };

        /// <summary>BS EN 60898-1 preferred rated currents.</summary>
        private static readonly int[] Bs60898Preferred = { 6, 8, 10, 13, 16, 20, 25, 32, 40, 50, 63, 80, 100, 125 };

        private static JObject Shipped()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_WIRE_TABLES.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data/STING_WIRE_TABLES.json");
            return JObject.Parse(File.ReadAllText(Path.Combine(dir.FullName, "StingTools", "Data", "STING_WIRE_TABLES.json")));
        }

        [Fact]
        public void Shipped_NEC_list_is_the_whole_of_Table_240_6A()
        {
            var set = VoltageDropEngine.ResolveBreakerSizes(Shipped());
            Assert.Null(set.LoadError);
            Assert.Equal(NecTable240_6A, set.Nec);
        }

        [Fact]
        public void Shipped_NEC_fuse_only_ratings_are_separate_from_the_breaker_list()
        {
            var set = VoltageDropEngine.ResolveBreakerSizes(Shipped());
            Assert.Equal(new[] { 1, 3, 6, 10 }, set.NecFuseAdditional);
            Assert.DoesNotContain(1, set.Nec);
            Assert.DoesNotContain(10, set.Nec);
        }

        [Fact]
        public void Shipped_MCB_list_uses_only_BS_EN_60898_preferred_values()
        {
            var set = VoltageDropEngine.ResolveBreakerSizes(Shipped());
            Assert.NotEmpty(set.Mcb);
            Assert.All(set.Mcb, a => Assert.Contains(a, Bs60898Preferred));
            Assert.Equal(125, set.Mcb.Max());
            Assert.NotEmpty(set.Mccb);
        }

        [Theory]
        [InlineData(14.0, 15)]
        [InlineData(16.0, 20)]
        [InlineData(21.0, 25)]
        [InlineData(95.0, 100)]
        [InlineData(401.0, 450)]
        [InlineData(1250.0, 1600)]
        [InlineData(4500.0, 5000)]
        [InlineData(6000.0, 6000)]
        public void NEC_rounds_up_to_the_next_Table_240_6A_rating(double required, int expected)
            => Assert.Equal(expected, VoltageDropEngine.NextRating(VoltageDropEngine.ResolveBreakerSizes(Shipped()).Nec, required));

        /// <summary>The old code returned the LARGEST rating here — a 7000 A load got a
        /// 400 A device. 0 is "no standard device", which the callers refuse on.</summary>
        [Theory]
        [InlineData(6000.1)]
        [InlineData(7000.0)]
        public void NEC_load_above_the_largest_rating_gets_no_device(double required)
            => Assert.Equal(0, VoltageDropEngine.NextRating(VoltageDropEngine.ResolveBreakerSizes(Shipped()).Nec, required));

        [Fact]
        public void BS_MCB_load_above_125A_gets_no_MCB()
            => Assert.Equal(0, VoltageDropEngine.NextRating(VoltageDropEngine.ResolveBreakerSizes(Shipped()).Mcb, 126));

        [Fact]
        public void Missing_breakerSizes_gives_empty_lists_and_an_error_not_a_fallback()
        {
            var set = VoltageDropEngine.ResolveBreakerSizes(JObject.Parse("{}"));
            Assert.Empty(set.Mcb);
            Assert.Empty(set.Mccb);
            Assert.Empty(set.Nec);
            Assert.Contains("breakerSizes", set.LoadError);
            Assert.Equal(0, VoltageDropEngine.NextRating(set.Nec, 10));
        }

        [Fact]
        public void One_bad_list_is_named_and_the_others_still_load()
        {
            var root = Shipped();
            ((JObject)root["breakerSizes"])["NEC_OCPD"] = new JArray(15, "twenty", 25);
            ((JObject)root["breakerSizes"]).Remove("BS_EN_60947_MCCB");
            var set = VoltageDropEngine.ResolveBreakerSizes(root);
            Assert.Empty(set.Nec);
            Assert.Empty(set.Mccb);
            Assert.NotEmpty(set.Mcb);
            Assert.Contains("NEC_OCPD", set.LoadError);
            Assert.Contains("BS_EN_60947_MCCB is missing", set.LoadError);
        }

        /// <summary>In the test host FindDataFile finds nothing, exactly like a plugin
        /// install with the data file missing: the sizer must refuse and say why.</summary>
        [Fact]
        public void Unloadable_data_file_refuses_and_says_why()
        {
            Assert.Equal(0, VoltageDropEngine.NextStandardBreakerSizeNEC(10));
            Assert.Equal(0, VoltageDropEngine.NextStandardBreakerSizeBS(10));
            Assert.Contains("STING_WIRE_TABLES.json not found", VoltageDropEngine.BreakerSizesLoadError);
        }
    }
}
