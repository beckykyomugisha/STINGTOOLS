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

        // ── #554: matching and display are two jobs, so assert both ─────
        //
        // This test used to demand that NormalizeSection RETURN the spaced form,
        // which conflated them. NormalizeSection removes all whitespace on
        // purpose — that is what lets SpecLink's "23 05 00" and a model's
        // "230500" reconcile to one key. Asserting spaced output here would have
        // meant reverting the reconciliation fix.
        //
        // So the test now pins the seam rather than picking a side: normalisation
        // is whitespace-INSENSITIVE, and FormatSection renders the canonical CSI
        // spacing for anything a human reads.

        [Fact]
        public void NormalizeSection_strips_all_whitespace_so_spaced_and_unspaced_match()
        {
            // The point of normalisation: every spelling collapses to ONE key.
            Assert.Equal("233100", CsiMasterFormat.NormalizeSection("  23   31  00 "));
            Assert.Equal("233100", CsiMasterFormat.NormalizeSection("23 31 00"));
            Assert.Equal("233100", CsiMasterFormat.NormalizeSection("233100"));

            // Stated as an equality between spellings, not just as literals — this
            // is the property SpecLink reconciliation actually depends on.
            Assert.Equal(CsiMasterFormat.NormalizeSection("23 31 00"),
                         CsiMasterFormat.NormalizeSection("233100"));

            Assert.Equal("", CsiMasterFormat.NormalizeSection(null));
            Assert.Equal("", CsiMasterFormat.NormalizeSection("   "));

            // Dots survive, so a child section stays distinct from its parent.
            Assert.Equal("230500.13", CsiMasterFormat.NormalizeSection("23 05 00.13"));
            Assert.NotEqual(CsiMasterFormat.NormalizeSection("23 05 00"),
                            CsiMasterFormat.NormalizeSection("23 05 00.13"));
        }

        [Fact]
        public void FormatSection_renders_the_canonical_CSI_spacing()
        {
            // Whatever spelling goes in, CSI's own form comes out.
            Assert.Equal("23 31 00", CsiMasterFormat.FormatSection("233100"));
            Assert.Equal("23 31 00", CsiMasterFormat.FormatSection("  23   31  00 "));
            Assert.Equal("22 40 00", CsiMasterFormat.FormatSection("224000"));

            // Level-4 child sections keep their suffix.
            Assert.Equal("23 05 00.13", CsiMasterFormat.FormatSection("230500.13"));

            // Shorter valid levels are still pairs.
            Assert.Equal("23", CsiMasterFormat.FormatSection("23"));
            Assert.Equal("23 05", CsiMasterFormat.FormatSection("2305"));

            Assert.Equal("", CsiMasterFormat.FormatSection(null));

            // Anything not a recognisable CSI number is returned UNCHANGED rather
            // than forced into pairs. Inventing a shape for input we do not
            // understand would print a confident wrong section number.
            Assert.Equal("23050", CsiMasterFormat.FormatSection("23050"));     // odd length
            Assert.Equal("DIV23", CsiMasterFormat.FormatSection("div23"));     // not digits
        }

        [Fact]
        public void Matching_is_whitespace_insensitive_while_output_stays_canonically_spaced()
        {
            // The two halves of #554 in one statement, end to end: a model that
            // stores sections UNSPACED still reconciles against a SPACED spec, and
            // what gets reported back is spaced regardless of which side it came
            // from.
            var model = new Dictionary<string, string>
            {
                { "233100", "HVAC Ducts and Casings" },  // unspaced, as models store it
                { "224000", "Plumbing Fixtures" },       // unspaced, model only → gap
            };
            var spec = new Dictionary<string, string>
            {
                { "23 31 00", "HVAC Ducts and Casings" }, // spaced, as SpecLink exports it
                { "21 13 00", "Sprinkler Systems" },      // spaced, spec only → over-spec
            };

            var r = CsiMasterFormat.Reconcile(model, spec);

            // Matching worked across the spelling difference: 23 31 00 is NOT a gap.
            Assert.Single(r.SpecGaps);
            Assert.Equal("22 40 00", r.SpecGaps[0].Section);   // reported spaced, given unspaced
            Assert.Single(r.OverSpec);
            Assert.Equal("21 13 00", r.OverSpec[0].Section);   // reported spaced, given spaced
            Assert.Empty(r.TitleMismatches);
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
            // #554 — reported identity is canonically spaced, NOT the normalised
            // matching key. These read "22 40 00", not "224000".
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
