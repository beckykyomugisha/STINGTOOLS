using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// 136 strings in the drawing data carried UTF-8-read-as-cp1252 corruption
    /// ("â€”" for "—"), five of them filter NAMES — a filter's identity in a
    /// project. The data is repaired; AecFilterFactory uses Garble to find a
    /// filter a project created under the corrupted name and rename it rather
    /// than mint a twin. These pin both directions and keep the data clean.
    /// </summary>
    public class Utf8MojibakeTests
    {
        [Theory]
        [InlineData("STING - Arch: Low U-Value Glazing (≤1.4)")]
        [InlineData("Presentation — Salmon")]
        public void Garble_then_repair_round_trips(string good)
        {
            var bad = Utf8Mojibake.Garble(good);
            Assert.NotNull(bad);
            Assert.NotEqual(good, bad);
            Assert.Equal(good, Utf8Mojibake.Repair(bad));
        }

        [Fact]
        public void Garble_reproduces_the_exact_legacy_name()
            => Assert.Equal("STING - Struct: Rebar â‰¤12mm", Utf8Mojibake.Garble("STING - Struct: Rebar ≤12mm"));

        [Theory]
        [InlineData("plain ascii")]
        [InlineData("STING - QA: Data Gate — Red")]   // legitimate em dash
        [InlineData("25 × 25 mm")]
        public void Repair_leaves_clean_text_alone(string clean)
            => Assert.Equal(clean, Utf8Mojibake.Repair(clean));

        [Fact]
        public void Ascii_has_no_garbled_form()
            => Assert.Null(Utf8Mojibake.Garble("STING - Plumb: Dead Legs"));

        [Theory]
        [InlineData("STING_AEC_FILTERS.json")]
        [InlineData("STING_VIEW_STYLE_PACKS.json")]
        [InlineData("STING_DRAWING_TYPES.json")]
        public void Drawing_data_carries_no_mojibake(string file)
        {
            var root = JToken.Parse(File.ReadAllText(Path.Combine(DataDir(), file)));
            var bad = root.SelectTokens("$..*")
                .Where(t => t.Type == JTokenType.String)
                .Select(t => (Path: t.Path, V: (string)t))
                .Where(x => Utf8Mojibake.Repair(x.V) != x.V)
                .Select(x => $"{x.Path}: {x.V}").ToList();
            Assert.True(bad.Count == 0, $"{bad.Count} corrupted string(s):\n" + string.Join("\n", bad.Take(15)));
        }

        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_DRAWING_TYPES.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            return Path.Combine(dir.FullName, "StingTools", "Data");
        }
    }
}
