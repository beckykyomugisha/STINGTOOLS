using System.Collections.Generic;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Locks the PROD resolution precedence + source-tiering rules that
    /// TagConfig.GetFamilyAwareProdCodeCore delegates to. Pure — no Revit.
    /// Order under test: project → corporate → LPS → sleeve → category → gen.
    /// </summary>
    public class ProdResolverTests
    {
        private static List<(string, string)> Rules(params (string, string)[] r) => new List<(string, string)>(r);
        private static readonly Dictionary<string, string> ProdMap = new()
        {
            ["Plumbing Fixtures"] = "FIX",
            ["Mechanical Equipment"] = "AHU",
            ["Generic Models"] = "GEN",
        };

        [Fact]
        public void Project_rules_win_over_corporate()
        {
            var proj = Rules(("*BIDET*", "BDT"));
            var corp = Rules(("*BIDET*", "WST"));
            var code = ProdResolver.Resolve("Bidet Wall", "Std", "Plumbing Fixtures", proj, corp, ProdMap, out var src);
            Assert.Equal("BDT", code);
            Assert.Equal("project", src);
        }

        [Fact]
        public void Corporate_used_when_no_project_match()
        {
            var corp = Rules(("*WC*|*TOILET*", "WST"));
            var code = ProdResolver.Resolve("WC Pan", "Close Coupled", "Plumbing Fixtures", null, corp, ProdMap, out var src);
            Assert.Equal("WST", code);
            Assert.Equal("corporate", src);
        }

        [Fact]
        public void Falls_through_to_category_default_when_nothing_matches()
        {
            var corp = Rules(("*URINAL*", "URN"));
            var code = ProdResolver.Resolve("Mystery Fixture", "T1", "Plumbing Fixtures", null, corp, ProdMap, out var src);
            Assert.Equal("FIX", code);
            Assert.Equal("category", src);
        }

        [Fact]
        public void Unmapped_category_yields_gen()
        {
            var code = ProdResolver.Resolve("Thing", "T", "Totally Unknown Category", null, null, ProdMap, out var src);
            Assert.Equal("GEN", code);
            Assert.Equal("gen", src);
        }

        [Fact]
        public void Empty_family_skips_rules_and_uses_category_default()
        {
            // No family name → no pattern matching, straight to category default.
            var proj = Rules(("*ANYTHING*", "XXX"));
            var code = ProdResolver.Resolve("", "", "Mechanical Equipment", proj, null, ProdMap, out var src);
            Assert.Equal("AHU", code);
            Assert.Equal("category", src);
        }

        [Fact]
        public void Lps_resolves_cross_category_before_default()
        {
            // No CSV rule matches, but the family name is an LPS air terminal.
            var code = ProdResolver.Resolve("Air Terminal Rod 1m", "Cu", "Generic Models", null, null, ProdMap, out var src);
            Assert.Equal("ATR", code);
            Assert.Equal("lps", src);
        }

        [Fact]
        public void Lps_bonding_bar_beats_generic_bond()
        {
            // BBR must be checked before the generic BOND→BCN rule (Phase fix).
            var code = ProdResolver.Resolve("LPS Earth Bonding Bar", "", "Electrical Equipment", null, null, ProdMap, out var src);
            Assert.Equal("BBR", code);
            Assert.Equal("lps", src);
        }

        [Fact]
        public void Sleeve_only_in_generic_models()
        {
            var inGm = ProdResolver.Resolve("Wall Sleeve FR", "DN100", "Generic Models", null, null, ProdMap, out var s1);
            Assert.Equal("SLV", inGm);
            Assert.Equal("sleeve", s1);

            // Same name, different category → NOT a sleeve; falls to category default.
            var inPipe = ProdResolver.Resolve("Wall Sleeve FR", "DN100", "Mechanical Equipment", null, null, ProdMap, out var s2);
            Assert.Equal("AHU", inPipe);
            Assert.Equal("category", s2);
        }

        [Fact]
        public void Corporate_match_beats_lps_when_both_apply()
        {
            // A corporate rule for the category takes precedence over the LPS fall-through.
            var corp = Rules(("*EARTH ROD*", "ERX"));
            var code = ProdResolver.Resolve("Earth Rod", "", "Electrical Equipment", null, corp, ProdMap, out var src);
            Assert.Equal("ERX", code);
            Assert.Equal("corporate", src);
        }

        [Theory]
        [InlineData("project", true)]
        [InlineData("corporate", true)]
        [InlineData("lps", true)]
        [InlineData("sleeve", true)]
        [InlineData("category", false)]
        [InlineData("gen", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsSpecific_classifies_source_tiers(string source, bool expected)
        {
            Assert.Equal(expected, ProdResolver.IsSpecific(source));
        }

        [Fact]
        public void Source_constants_match_emitted_strings()
        {
            // Guards against the constants drifting from what Resolve actually emits.
            ProdResolver.Resolve("Bidet", "", "Plumbing Fixtures", Rules(("*BIDET*", "BDT")), null, ProdMap, out var s);
            Assert.Equal(ProdResolver.Sources.Project, s);
            Assert.True(ProdResolver.IsSpecific(ProdResolver.Sources.Corporate));
            Assert.False(ProdResolver.IsSpecific(ProdResolver.Sources.Gen));
        }
    }
}
