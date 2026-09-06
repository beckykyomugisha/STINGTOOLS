using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.Core.Classification;
using Xunit;

namespace StingTools.Boq.Tests
{
    // Phase A (KUT lifecycle) — CSI MasterFormat Nrm2 column + CSI↔NRM2 bridge.
    // Pure logic; no Revit. Validates that one CSI rule resolves both the CSI
    // section and the NRM2 work-section, and that SYS-specific rows bill
    // consistently (Pipes+SAN under plumbing, Pipes+CHW under HVAC).
    public class CsiNrm2BridgeTests
    {
        [Fact]
        public void ParseCsvLines_ReadsOptionalNrm2Column()
        {
            var lines = new[]
            {
                "Category,FamilyRegex,TypeRegex,Sys,Section,Title,Nrm2",
                "Pipes,,,SAN,22 13 16,Sanitary Waste and Vent Piping,32",
                "Pipes,,,CHW,23 21 13,Hydronic Piping,33",
            };
            var rules = CsiMasterFormat.ParseCsvLines(lines);

            Assert.Equal(2, rules.Count);
            Assert.Equal("32", rules[0].Nrm2);
            Assert.Equal("SAN", rules[0].Sys);
            Assert.Equal("33", rules[1].Nrm2);
        }

        [Fact]
        public void ParseCsvLines_LegacySixColumnRow_HasEmptyNrm2()
        {
            var lines = new[]
            {
                "Category,FamilyRegex,TypeRegex,Sys,Section,Title",
                "Floors,,,,09 60 00,Flooring",
            };
            var rules = CsiMasterFormat.ParseCsvLines(lines);

            Assert.Single(rules);
            Assert.Equal("Flooring", rules[0].Title);
            Assert.Equal("", rules[0].Nrm2);   // blank ⇒ BOQ falls back to DeriveNrm2Section
        }

        [Fact]
        public void BuildSectionToNrm2_MapsNormalisedSectionToCode_SkipsBlankNrm2()
        {
            var rules = new List<CsiRule>
            {
                new CsiRule { Section = "22 13 16", Nrm2 = "32" },
                new CsiRule { Section = "23 21 13", Nrm2 = "33" },
                new CsiRule { Section = "09 60 00", Nrm2 = "" },   // no bridge — skipped
            };
            var map = CsiMasterFormat.BuildSectionToNrm2(rules);

            // Keys are whitespace-normalised, so spaced + unspaced reconcile.
            Assert.Equal("32", map[CsiMasterFormat.NormalizeSection("22 13 16")]);
            Assert.Equal("33", map[CsiMasterFormat.NormalizeSection("23 21 13")]);
            Assert.False(map.ContainsKey(CsiMasterFormat.NormalizeSection("09 60 00")));
        }

        [Fact]
        public void BuildSectionToNrm2_FirstRuleWinsOnSectionCollision()
        {
            // Project-overlay rows are loaded first, so the first Nrm2 wins.
            var rules = new List<CsiRule>
            {
                new CsiRule { Section = "22 13 16", Nrm2 = "99" }, // overlay
                new CsiRule { Section = "22 13 16", Nrm2 = "32" }, // corporate
            };
            var map = CsiMasterFormat.BuildSectionToNrm2(rules);
            Assert.Equal("99", map[CsiMasterFormat.NormalizeSection("22 13 16")]);
        }

        // ---- rule-first resolution -------------------------------------------
        // BuildSectionToNrm2 can only hold ONE answer per section, and that is not
        // enough: 03 30 00 Cast-in-Place Concrete is NRM2 5 for a slab and 14 for a
        // wall. Reading the section map alone bills every concrete slab, column,
        // foundation, stair and ramp under masonry, purely because the wall row is
        // listed first. Nrm2For reads the matched RULE first, which is why.
        [Fact]
        public void Nrm2For_PrefersTheMatchedRule_OverTheSectionMap()
        {
            var rules = new List<CsiRule>
            {
                new CsiRule { Category = "Walls",  TypeRegex = "(?i)concrete", Section = "03 30 00", Title = "Cast-in-Place Concrete", Nrm2 = "14" },
                new CsiRule { Category = "Floors", Section = "03 30 00", Title = "Cast-in-Place Concrete", Nrm2 = "5" },
            };
            var map = CsiMasterFormat.BuildSectionToNrm2(rules);
            Assert.Equal("14", map[CsiMasterFormat.NormalizeSection("03 30 00")]);   // first wins, as designed

            var floorRule = CsiMasterFormat.Resolve(rules, "Floors", "Floor", "Slab 200mm", "");
            Assert.Equal("5", CsiMasterFormat.Nrm2For(floorRule, map, "03 30 00"));  // NOT 14

            var wallRule = CsiMasterFormat.Resolve(rules, "Walls", "Basic Wall", "Concrete 200", "");
            Assert.Equal("14", CsiMasterFormat.Nrm2For(wallRule, map, "03 30 00"));
        }

        [Fact]
        public void Nrm2For_FallsBackToTheSectionMap_ThenToNoOpinion()
        {
            var map = new Dictionary<string, string> { [CsiMasterFormat.NormalizeSection("22 13 16")] = "32" };
            // No matched rule (an element carrying a stamped section the map never matched)
            // -> the section map is the fallback.
            Assert.Equal("32", CsiMasterFormat.Nrm2For(null, map, "221316"));
            // A rule with a blank Nrm2 must not out-vote the section map.
            Assert.Equal("32", CsiMasterFormat.Nrm2For(new CsiRule { Section = "22 13 16" }, map, "22 13 16"));
            // Nothing known -> null, meaning "let DeriveNrm2Section decide", NOT a guess.
            Assert.Null(CsiMasterFormat.Nrm2For(null, map, "99 99 99"));
            Assert.Null(CsiMasterFormat.Nrm2For(null, null, "22 13 16"));
            Assert.Null(CsiMasterFormat.Nrm2For(null, map, ""));
        }

        [Fact]
        public void ShippedMap_ConcreteRowsBillUnderTheirOwnSection_NotTheFirstOne()
        {
            var rules = LoadShippedCsiMap();
            var map = CsiMasterFormat.BuildSectionToNrm2(rules);

            // Every one of these resolves to CSI 03 30 00, and every one must keep its
            // own NRM2 answer rather than inheriting the concrete-wall row's.
            foreach (var cat in new[] { "Floors", "Structural Columns", "Structural Foundations", "Stairs", "Ramps" })
            {
                var rule = CsiMasterFormat.Resolve(rules, cat, "", "", "");
                Assert.NotNull(rule);
                Assert.Equal("5", CsiMasterFormat.Nrm2For(rule, map, rule.Section));
            }
            var wall = CsiMasterFormat.Resolve(rules, "Walls", "Basic Wall", "Concrete 200mm", "");
            Assert.Equal("14", CsiMasterFormat.Nrm2For(wall, map, wall.Section));
        }

        private static List<CsiRule> LoadShippedCsiMap()
        {
            string path = Path.Combine(System.AppContext.BaseDirectory, "Data", "STING_CSI_MASTERFORMAT_MAP.csv");
            Assert.True(File.Exists(path), $"Shipped CSI map not found at {path}");
            return CsiMasterFormat.ParseCsvLines(File.ReadAllLines(path));
        }

        [Fact]
        public void ShippedMap_BridgesSysSpecificPipeRows()
        {
            string path = Path.Combine(System.AppContext.BaseDirectory, "Data", "STING_CSI_MASTERFORMAT_MAP.csv");
            Assert.True(File.Exists(path), $"Shipped CSI map not found at {path}");

            var rules = CsiMasterFormat.ParseCsvLines(File.ReadAllLines(path));
            Assert.NotEmpty(rules);

            // Sanity: SAN pipes resolve to plumbing (32), CHW pipes to HVAC (33).
            var san = CsiMasterFormat.Resolve(rules, "Pipes", "", "", "SAN");
            var chw = CsiMasterFormat.Resolve(rules, "Pipes", "", "", "CHW");
            Assert.NotNull(san);
            Assert.NotNull(chw);
            Assert.Equal("32", san.Nrm2);
            Assert.Equal("33", chw.Nrm2);

            // Every shipped row carries an Nrm2 value (the KUT category set is fully bridged).
            Assert.All(rules, r => Assert.False(string.IsNullOrWhiteSpace(r.Nrm2), $"row {r.Category}/{r.Section} missing Nrm2"));
        }
    }
}
