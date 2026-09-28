using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// ISO 19650 status colouring. One input — the suitability code — derives the
    /// CDE state and the number PRJ_TB_CDE_STATE_INT that shows exactly one
    /// coloured band in the title block. Two writers used to disagree (a revision
    /// sync changed the code and not the state); both now call Derive().
    /// </summary>
    public class SuitabilityPresentationTests
    {
        public static IEnumerable<object[]> AllCodes() => new[]
        {
            "S0", "S1", "S2", "S3", "S4", "S5", "S6", "S7",
            "A1", "A2", "A3", "A4", "A5", "B1", "B2", "B3", "B4", "B5", "CR",
        }.Select(c => new object[] { c });

        [Theory]
        [MemberData(nameof(AllCodes))]
        public void Every_ISO_code_maps_to_a_visible_band(string code)
        {
            var d = SuitabilityPresentation.Derive(code);
            Assert.True(d.IsKnown, code);
            Assert.InRange(d.StateInt, 1, 3);   // ARCHIVED is never implied by a code
            Assert.Equal(SuitabilityPresentation.StateFor(d.CdeStateName), d.State);
        }

        [Theory]
        [InlineData("S0", CdeState.Wip)]
        [InlineData("S2", CdeState.Shared)]
        [InlineData("S4 - FOR APROVAL", CdeState.Shared)]   // typo'd description still yields the code
        [InlineData("a1", CdeState.Published)]
        [InlineData("CR", CdeState.Published)]
        public void Code_decides_the_state(string raw, CdeState expected)
            => Assert.Equal(expected, SuitabilityPresentation.Derive(raw).State);

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("FOR INFO")]
        [InlineData("S9")]
        public void Unknown_code_shows_no_band(string raw)
        {
            var d = SuitabilityPresentation.Derive(raw);
            Assert.Equal(0, d.StateInt);
            Assert.Null(d.CdeStateName);
        }

        [Fact]
        public void Band_formula_compares_the_integer()
        {
            Assert.Equal("PRJ_TB_CDE_STATE_INT = 2", SuitabilityPresentation.BandFormulaFor(CdeState.Shared));
            Assert.Equal("STING_CDE_BAND_SHARED", SuitabilityPresentation.BandParameterFor(CdeState.Shared));
        }

        [Fact]
        public void Palette_defaults_cover_every_band_and_avoid_brand_colours()
        {
            var p = SuitabilityPresentation.ResolvePalette(null);
            Assert.Equal(4, p.Count);
            Assert.DoesNotContain("#F2A341", p.Values);   // corporate amber
            Assert.DoesNotContain("#1F4E79", p.Values);   // corporate navy
            Assert.Equal(p.Count, p.Values.Distinct().Count());
        }

        [Fact]
        public void Palette_data_overrides_and_bad_entries_are_reported_not_applied()
        {
            var problems = new List<string>();
            var p = SuitabilityPresentation.ResolvePalette(new Dictionary<string, CdeBandSpec>
            {
                ["SHARED"] = new CdeBandSpec { State = 2, Color = "#123456" },
                ["PUBLISHED"] = new CdeBandSpec { State = 9, Color = "#000000" },   // wrong state
                ["WIP"] = new CdeBandSpec { State = 1, Color = "grey" },            // not hex
                ["ORANGE"] = new CdeBandSpec { State = 5, Color = "#FFA500" },      // not a state
            }, problems);
            Assert.Equal("#123456", p[CdeState.Shared]);
            Assert.Equal(SuitabilityPresentation.DefaultColors[CdeState.Published], p[CdeState.Published]);
            Assert.Equal(SuitabilityPresentation.DefaultColors[CdeState.Wip], p[CdeState.Wip]);
            Assert.Equal(3, problems.Count);
        }

        [Theory]
        [InlineData("S2", 2, false)]
        [InlineData("A1", 2, true)]      // revision sync changed the code, band still SHARED
        [InlineData("S2", null, true)]   // parameter bound but never written
        [InlineData("S2", 4, false)]     // ARCHIVED from the register outranks the code
        [InlineData("", null, false)]    // nothing to show, nothing shown
        public void Pre_export_catches_a_band_that_disagrees_with_the_code(string code, int? stored, bool blocks)
            => Assert.Equal(blocks, SuitabilityPresentation.Disagreement(code, stored) != null);

        // ── The shipped title-block data ──────────────────────────────────

        private static JObject TitleBlocks()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_TITLE_BLOCKS.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            return JObject.Parse(File.ReadAllText(Path.Combine(dir.FullName, "StingTools", "Data", "STING_TITLE_BLOCKS.json")));
        }

        [Fact]
        public void Shipped_palette_resolves_cleanly()
        {
            var bands = TitleBlocks()["cdeBands"]?.ToObject<Dictionary<string, CdeBandSpec>>();
            Assert.NotNull(bands);
            var problems = new List<string>();
            SuitabilityPresentation.ResolvePalette(bands, problems);
            Assert.Empty(problems);
            Assert.Equal(4, bands.Count);
        }

        /// <summary>
        /// Every family that prints a suitability code carries exactly one band
        /// region, in the same column as that code — and no family without a code
        /// has one (a band there would colour a drawing that states no suitability).
        /// The code label itself must remain: colour is convention, not the record.
        /// </summary>
        [Fact]
        public void Bands_sit_only_beside_a_printed_suitability_code()
        {
            var bad = new List<string>();
            int withCode = 0;
            foreach (var f in TitleBlocks()["families"])
            {
                if (f.Value<bool?>("abstract") == true) continue;
                var id = f.Value<string>("id");
                var codeLabels = (f["labels"] ?? new JArray())
                    .Where(l => l.Value<string>("param") == "PRJ_DWG_SUITABILITY_COD_TXT").ToList();
                var bands = (f["filledRegions"] ?? new JArray())
                    .Where(r => r.Value<string>("role") == FilledRegionRole).ToList();
                if (codeLabels.Count > 0)
                {
                    withCode++;
                    if (bands.Count != 1) { bad.Add($"{id}: prints a suitability code but has {bands.Count} band regions"); continue; }
                    var b = bands[0];
                    double x1 = Math.Min(b["topLeft"][0].Value<double>(), b["bottomRight"][0].Value<double>());
                    double x2 = Math.Max(b["topLeft"][0].Value<double>(), b["bottomRight"][0].Value<double>());
                    double ax = codeLabels[0]["anchor"][0].Value<double>();
                    if (ax < x1 || ax > x2) bad.Add($"{id}: band is not in the suitability column");
                }
                else if (bands.Count > 0)
                    bad.Add($"{id}: has a band but prints no suitability code");
            }
            Assert.True(withCode >= 8, $"only {withCode} families print a suitability code — data did not load as expected");
            Assert.True(bad.Count == 0, string.Join("\n", bad));
        }

        private const string FilledRegionRole = "cdeBand";
    }
}
