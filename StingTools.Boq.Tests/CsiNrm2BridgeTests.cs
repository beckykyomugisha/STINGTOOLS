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
