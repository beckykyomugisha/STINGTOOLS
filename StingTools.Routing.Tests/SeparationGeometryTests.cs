using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core.Routing;
using Xunit;
using Seg = StingTools.Core.Routing.SeparationGeometry.Seg;

namespace StingTools.Routing.Tests
{
    /// <summary>
    /// DSCH-22. The separation check used the largest rule for a service pair,
    /// so a power/data crossing (50 mm) was held to the parallel open-tray 300 mm.
    /// A first fix chose by direction alone and was reverted: drops are vertical,
    /// so every horizontal neighbour read as a crossing, and pairs with only
    /// parallel/vertical rules then required 0 mm. These tests pin both failures.
    /// </summary>
    public class SeparationGeometryTests
    {
        private static readonly (string, double, string)[] PwrData =
        {
            ("parallel", 200, "PWR_DATA_PARALLEL_ENCLOSED"),
            ("parallel", 300, "PWR_DATA_PARALLEL_ONE_OPEN"),
            ("crossing", 50,  "PWR_DATA_CROSSING"),
        };

        [Fact]
        public void Two_level_runs_at_right_angles_that_overlap_in_plan_cross()
            => Assert.True(SeparationGeometry.IsCrossing(
                new Seg(0, 0, 3, 10, 0, 3), new Seg(5, -5, 3.2, 5, 5, 3.2)));

        [Fact]
        public void A_vertical_drop_never_crosses_a_horizontal_run()
            => Assert.False(SeparationGeometry.IsCrossing(
                new Seg(5, 0, 0, 5, 0, 3), new Seg(0, 0.2, 2, 10, 0.2, 2)));

        [Fact]
        public void A_run_that_stops_short_at_right_angles_is_not_a_crossing()
            => Assert.False(SeparationGeometry.IsCrossing(
                new Seg(0, 0, 3, 10, 0, 3), new Seg(5, 1, 3, 5, 6, 3)));

        [Fact]
        public void Parallel_runs_do_not_cross()
            => Assert.False(SeparationGeometry.IsCrossing(
                new Seg(0, 0, 3, 10, 0, 3), new Seg(0, 0.2, 3, 10, 0.2, 3)));

        [Fact]
        public void A_crossing_is_held_to_the_crossing_rule()
            => Assert.Equal(50, SeparationGeometry.Governing(PwrData, crossing: true)!.Value.Mm);

        [Fact]
        public void Otherwise_the_largest_non_crossing_rule_applies()
            => Assert.Equal(300, SeparationGeometry.Governing(PwrData, crossing: false)!.Value.Mm);

        [Fact]
        public void A_pair_with_only_parallel_rules_still_needs_its_distance_where_it_crosses()
        {
            var gas = new[] { ("parallel", 150.0, "GAS_FROM_ELC") };
            Assert.Equal(150, SeparationGeometry.Governing(gas, crossing: true)!.Value.Mm);
        }

        [Fact]
        public void No_rule_for_the_pair_means_no_requirement()
            => Assert.Null(SeparationGeometry.Governing(Array.Empty<(string, double, string)>(), crossing: false));

        [Fact]
        public void Every_shipped_pair_with_a_rule_requires_a_positive_distance_either_way()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Data", "STING_SEPARATION_RULES.json");
            var rules = JObject.Parse(File.ReadAllText(path))["rules"]!
                .Select(r => (Src: (string)r["source_service"]!, Tgt: (string)r["target_service"]!,
                              Geo: (string?)r["geometry"] ?? "any", Mm: (double)r["min_separation_mm"]!, Id: (string)r["id"]!))
                .ToList();
            Assert.NotEmpty(rules);
            foreach (var pair in rules.GroupBy(r => (r.Src, r.Tgt)))
                foreach (bool crossing in new[] { true, false })
                {
                    var g = SeparationGeometry.Governing(pair.Select(r => (r.Geo, r.Mm, r.Id)), crossing);
                    Assert.True(g is { } v && v.Mm > 0, $"{pair.Key} crossing={crossing} governed by nothing");
                }
        }
    }
}
