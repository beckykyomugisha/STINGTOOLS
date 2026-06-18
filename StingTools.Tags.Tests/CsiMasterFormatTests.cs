using System.Collections.Generic;
using StingTools.Core.Classification;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Covers CSI MasterFormat rule parsing, scored resolution (specificity wins,
    /// SYS/family qualifiers, category fallback) and TOC reconciliation. Revit/Excel
    /// IO lives in CsiCommands and is not under test here.
    /// </summary>
    public class CsiMasterFormatTests
    {
        private static readonly string[] Csv =
        {
            "# comment line",
            "Category,FamilyRegex,TypeRegex,Sys,Section,Title",
            "Mechanical Equipment,,,,23 00 00,HVAC",
            "Mechanical Equipment,(?i)ahu|air handling,,,23 73 00,Air-Handling Units",
            "Pipes,,,CHW,23 21 13,Hydronic Piping",
            "Pipes,,,SAN,22 13 16,Sanitary Waste and Vent Piping",
            "Lighting Fixtures,,,,26 51 00,Interior Lighting",
            "",
        };

        private static List<CsiRule> Rules() => CsiMasterFormat.ParseCsvLines(Csv);

        [Fact]
        public void ParseCsvLines_skips_comments_header_and_blanks()
        {
            var rules = Rules();
            Assert.Equal(5, rules.Count);   // 5 data rows only
            Assert.All(rules, r => Assert.NotEqual("Category", r.Category));
        }

        [Fact]
        public void Resolve_family_qualifier_beats_category_fallback()
        {
            var rules = Rules();
            var r = CsiMasterFormat.Resolve(rules, "Mechanical Equipment", "AHU-01 Air Handling Unit", "Type A", "");
            Assert.Equal("23 73 00", r.Section);
        }

        [Fact]
        public void Resolve_category_only_when_family_does_not_match()
        {
            var rules = Rules();
            var r = CsiMasterFormat.Resolve(rules, "Mechanical Equipment", "Generic Box", "", "");
            Assert.Equal("23 00 00", r.Section);
        }

        [Fact]
        public void Resolve_uses_sys_token()
        {
            var rules = Rules();
            Assert.Equal("23 21 13", CsiMasterFormat.Resolve(rules, "Pipes", "Pipe Type", "", "CHW").Section);
            Assert.Equal("22 13 16", CsiMasterFormat.Resolve(rules, "Pipes", "Pipe Type", "", "SAN").Section);
        }

        [Fact]
        public void Resolve_returns_null_when_no_rule_applies()
        {
            var rules = Rules();
            Assert.Null(CsiMasterFormat.Resolve(rules, "Furniture", "Chair", "", ""));
            // Pipes with an unmapped SYS has no category-only fallback → null
            Assert.Null(CsiMasterFormat.Resolve(rules, "Pipes", "Pipe", "", "REFRIG"));
        }

        [Fact]
        public void NormalizeSection_collapses_whitespace_and_uppercases()
        {
            Assert.Equal("23 31 00", CsiMasterFormat.NormalizeSection("  23   31  00 "));
            Assert.Equal("", CsiMasterFormat.NormalizeSection(null));
        }

        // ── Reconcile ───────────────────────────────────────────────────
        [Fact]
        public void Reconcile_finds_gaps_overspec_and_title_mismatches()
        {
            var model = new Dictionary<string, string>
            {
                { "23 31 00", "HVAC Ducts and Casings" },   // in both, matching
                { "26 51 00", "Interior Lighting" },         // in both, title differs in spec
                { "22 40 00", "Plumbing Fixtures" },         // model only → spec gap
            };
            var spec = new Dictionary<string, string>
            {
                { "23 31 00", "HVAC Ducts and Casings" },
                { "26 51 00", "Lighting - Interior" },       // different title
                { "21 13 00", "Sprinkler Systems" },         // spec only → over-spec
            };

            var r = CsiMasterFormat.Reconcile(model, spec);
            Assert.Single(r.SpecGaps);
            Assert.Equal("22 40 00", r.SpecGaps[0].Section);
            Assert.Single(r.OverSpec);
            Assert.Equal("21 13 00", r.OverSpec[0].Section);
            Assert.Single(r.TitleMismatches);
            Assert.Equal("26 51 00", r.TitleMismatches[0].Section);
        }

        [Fact]
        public void Reconcile_missing_title_is_not_a_mismatch()
        {
            var model = new Dictionary<string, string> { { "23 31 00", "Ducts" } };
            var spec = new Dictionary<string, string> { { "23 31 00", "" } };
            var r = CsiMasterFormat.Reconcile(model, spec);
            Assert.Empty(r.TitleMismatches);
            Assert.Empty(r.SpecGaps);
        }
    }
}
