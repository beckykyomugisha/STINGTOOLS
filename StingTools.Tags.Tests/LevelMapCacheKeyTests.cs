using System;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>DTW-138 — the session level-map cache must miss after a level rename or
    /// insert, or a spatial_codes.json edit, and hit when nothing changed.</summary>
    public class LevelMapCacheKeyTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

        private static string Key(string l1Name = "Level 1", double l2Elev = 12.0, bool insert = false, DateTime? codes = null)
        {
            var levels = new System.Collections.Generic.List<(long, string, double, bool?)>
            {
                (311, l1Name, 0.0, true), (312, "Level 2", l2Elev, true),
            };
            if (insert) levels.Add((400, "Mezzanine", 6.0, true));
            return LevelMapCacheKey.Compose("C:\\p.rvt|42", levels, codes ?? T0);
        }

        [Fact]
        public void NothingChanged_SameKey() => Assert.Equal(Key(), Key());

        [Fact]
        public void LevelOrderDoesNotMatter()
        {
            var a = LevelMapCacheKey.Compose("d", new[] { (1L, "A", 0.0, (bool?)true), (2L, "B", 3.0, (bool?)true) }, null);
            var b = LevelMapCacheKey.Compose("d", new[] { (2L, "B", 3.0, (bool?)true), (1L, "A", 0.0, (bool?)true) }, null);
            Assert.Equal(a, b);
        }

        [Fact]
        public void Rename_Misses() => Assert.NotEqual(Key(), Key(l1Name: "Ground Floor"));

        [Fact]
        public void Insert_Misses() => Assert.NotEqual(Key(), Key(insert: true));

        [Fact]
        public void ElevationChange_Misses() => Assert.NotEqual(Key(), Key(l2Elev: 13.0));

        [Fact]
        public void SpatialCodesEdit_Misses() => Assert.NotEqual(Key(), Key(codes: T0.AddSeconds(5)));
    }
}
