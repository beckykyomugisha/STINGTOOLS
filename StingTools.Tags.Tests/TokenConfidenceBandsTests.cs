using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>TAGACC-18: the Token Confidence Audit's bands, and the
    /// vocabulary it shares with the writer (TokenAutoPopulator.PopulateAll).</summary>
    public class TokenConfidenceBandsTests
    {
        [Theory]
        [InlineData("TYPE_OVERRIDE", "High")]
        [InlineData("Room", "High")]
        [InlineData("Workset", "High")]
        [InlineData("ScopeBox", "High")]
        [InlineData("ProjectInfo", "Medium")]
        [InlineData("Proximity", "Medium")]
        [InlineData("Default", "Low")]
        [InlineData("", "Low")]
        [InlineData(null, "Low")]
        [InlineData("Banana", "Low")]
        public void Loc_bands(string source, string expected)
            => Assert.Equal(expected, TokenConfidenceBands.ClassifyLoc(source).Band.ToString());

        [Theory]
        [InlineData("TYPE_OVERRIDE", "High")]
        [InlineData("Room", "High")]
        [InlineData("ScopeBox", "High")]   // STING-ZONE:: boxes — was Low
        [InlineData("Proximity", "Medium")] // was Low
        [InlineData("Default", "Low")]
        [InlineData("", "Low")]
        [InlineData("Banana", "Low")]
        public void Zone_bands(string source, string expected)
            => Assert.Equal(expected, TokenConfidenceBands.ClassifyZone(source).Band.ToString());

        [Theory]
        [InlineData(1, "High")]
        [InlineData(5, "High")]
        [InlineData(6, "Medium")]
        [InlineData(7, "Low")]
        [InlineData(0, "Low")]
        public void Sys_bands(int layer, string expected)
            => Assert.Equal(expected, TokenConfidenceBands.ClassifySys(layer).Band.ToString());

        [Fact]
        public void Every_non_high_band_carries_a_reason()
        {
            var all = new[] { "ProjectInfo", "Proximity", "Default", "", "x" }
                .Select(TokenConfidenceBands.ClassifyLoc)
                .Concat(new[] { "Proximity", "Default", "", "x" }.Select(TokenConfidenceBands.ClassifyZone))
                .Concat(new[] { 0, 6, 7 }.Select(TokenConfidenceBands.ClassifySys));
            foreach (var c in all)
                Assert.False(string.IsNullOrWhiteSpace(c.Reason), $"{c.Band} band with no reason");
        }

        [Fact]
        public void Loc_default_is_the_source_not_the_value()
        {
            Assert.True(TokenConfidenceBands.IsLocDefault("Default"));
            Assert.False(TokenConfidenceBands.IsLocDefault("ScopeBox"));
            Assert.False(TokenConfidenceBands.IsLocDefault(""));
        }

        /// <summary>
        /// Every source string the writer records must be one the classifier knows.
        /// This is the drift that made STING-ZONE:: scope boxes read as defaults.
        /// </summary>
        [Theory]
        [InlineData("LOC_SOURCE", "locSource", 6)]
        [InlineData("ZONE_SOURCE", "zoneSource", 4)]
        public void Writer_vocabulary_is_classified(string param, string local, int minLiterals)
        {
            var path = Path.Combine(DrawingCatalogueFixture.RepoRoot(), "StingTools", "Core", "ParameterHelpers.cs");
            string src = File.ReadAllText(path);

            var literals = new HashSet<string>();
            // Direct writes: SetString/SetIfEmpty(el, ParamRegistry.X_SOURCE, "Value"...
            foreach (Match m in Regex.Matches(src, @"ParamRegistry\." + param + @",\s*""([A-Za-z_]+)"""))
                literals.Add(m.Groups[1].Value);
            // The computed source: string locSource = a ? "ScopeBox" : ... ;  and later reassignments.
            foreach (Match m in Regex.Matches(src, @"\b" + local + @"\s*=\s*([^;]+);"))
                foreach (Match q in Regex.Matches(m.Groups[1].Value, @"""([A-Za-z_]+)"""))
                    literals.Add(q.Groups[1].Value);

            // Non-vacuous: if the regexes stop matching, fail rather than pass on nothing.
            Assert.True(literals.Count >= minLiterals,
                $"Only {literals.Count} {param} literal(s) found ({string.Join(", ", literals)}) — has the writer moved?");

            foreach (var lit in literals)
            {
                var c = param == "LOC_SOURCE" ? TokenConfidenceBands.ClassifyLoc(lit) : TokenConfidenceBands.ClassifyZone(lit);
                Assert.DoesNotContain("unrecognised", c.Reason);
            }
        }
    }
}
