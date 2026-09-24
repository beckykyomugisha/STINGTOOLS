using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// familyMatch narrows an annotation rule inside its category (DRAW-8: roof
    /// plans tag rainwater outlets among Plumbing Fixtures and roof lights among
    /// Windows). The failure it must never have is "invalid pattern -> match
    /// everything", which would tag every basin and wall window on the roof plan.
    /// </summary>
    public class RuleFamilyFilterTests
    {
        [Fact]
        public void No_pattern_is_no_filter()
        {
            Assert.Null(RuleFamilyFilter.Compile(null, out var err));
            Assert.Null(err);
            Assert.True(RuleFamilyFilter.Matches(null, "Basin", "Std"));
        }

        [Fact]
        public void Invalid_pattern_reports_and_yields_no_regex()
        {
            Assert.Null(RuleFamilyFilter.Compile("roof(", out var err));
            Assert.NotNull(err);
        }

        [Theory]
        [InlineData("STING - Rainwater Outlet", "100mm", true)]
        [InlineData("Roof Drain - Siphonic", "75", true)]
        [InlineData("RWO", "Standard", true)]
        [InlineData("Wash Basin", "600", false)]
        [InlineData("WC - Close Coupled", "Std", false)]
        public void Roof_plan_rwo_pattern_selects_outlets_only(string family, string type, bool expected)
            => Assert.Equal(expected, RuleFamilyFilter.Matches(RoofPattern("Plumbing Fixtures"), family, type));

        [Theory]
        [InlineData("Rooflight - Flat", "1200x1200", true)]
        [InlineData("Skylight", "Fixed", true)]
        [InlineData("Window - Casement", "1200x1500", false)]
        public void Roof_plan_rooflight_pattern_selects_rooflights_only(string family, string type, bool expected)
            => Assert.Equal(expected, RuleFamilyFilter.Matches(RoofPattern("Windows"), family, type));

        [Fact]
        public void Every_familyMatch_in_the_catalogue_compiles()
        {
            var bad = Catalogue().DrawingTypes
                .SelectMany(dt => (dt.Annotation?.Rules ?? new System.Collections.Generic.List<AutoAnnotationRule>())
                    .Where(r => !string.IsNullOrEmpty(r.FamilyMatch))
                    .Select(r => (dt.Id, r)))
                .Where(x => { RuleFamilyFilter.Compile(x.r.FamilyMatch, out var e); return e != null; })
                .Select(x => $"{x.Id}: {x.r.FamilyMatch}").ToList();
            Assert.True(bad.Count == 0, string.Join("\n", bad));
        }

        /// <summary>
        /// A shell heredoc turned the "\b" of a word boundary into a JSON "\b" —
        /// a BACKSPACE — while writing the roof pattern above. The regex still
        /// compiled and silently matched nothing. No catalogue string ever has a
        /// reason to hold a control character, so any one is a corruption.
        /// </summary>
        [Fact]
        public void Catalogue_strings_carry_no_control_characters()
        {
            var root = Newtonsoft.Json.Linq.JToken.Parse(File.ReadAllText(CataloguePath()));
            var bad = root.SelectTokens("$..*")
                .Where(t => t.Type == Newtonsoft.Json.Linq.JTokenType.String)
                .Where(t => ((string)t).Any(c => c < 0x20))
                .Select(t => t.Path).ToList();
            Assert.True(bad.Count == 0, "control characters at:\n" + string.Join("\n", bad.Take(20)));
        }

        private static System.Text.RegularExpressions.Regex RoofPattern(string category)
        {
            var rule = Catalogue().DrawingTypes.Single(d => d.Id == "arch-roof-A1-1to100")
                .Annotation.Rules.Single(r => r.Category == category && r.RuleType == "AutoTag");
            var rx = RuleFamilyFilter.Compile(rule.FamilyMatch, out var err);
            Assert.Null(err);
            Assert.NotNull(rx);
            return rx;
        }

        private static string CataloguePath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_DRAWING_TYPES.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            return Path.Combine(dir.FullName, "StingTools", "Data", "STING_DRAWING_TYPES.json");
        }

        private static DrawingTypeLibrary Catalogue()
            => JsonConvert.DeserializeObject<DrawingTypeLibrary>(File.ReadAllText(CataloguePath()));
    }
}
