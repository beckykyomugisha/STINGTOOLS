using System.Collections.Generic;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Per-family QR configuration — the flexibility half.
    ///
    /// WHY THIS EXISTS, IN NUMBERS
    /// ---------------------------
    /// Three sheets exported from a live project on 2026-09-14 each reserve a QR
    /// cell, already drawn and labelled, and each puts it somewhere different:
    ///
    ///     cover  "SCAN · VERIFY ISSUE"    x 196-224 mm   y 403-407 mm
    ///     001    "SCAN CURRENT ISSUE"     x 767-799 mm   y 109-113 mm
    ///     002    "SCAN CURRENT ISSUE"     x 591-622 mm   y  34- 38 mm
    ///
    /// None of those families is in STING_TITLE_BLOCKS.json, so the slot lookup
    /// finds nothing, and no single fallback corner can be right for all three. The
    /// first cut would have stamped all three in the wrong place — on drawings that
    /// had already been exported to a shared CDE folder.
    ///
    /// So a title block states its own cell. These tests pin the parsing of that
    /// statement, including the sloppy forms a person types into a Revit parameter
    /// box at 5pm.
    /// </summary>
    public class SheetQrConfigTests
    {
        private const double Default = 20.0;

        // ── The anchor parameter ──────────────────────────────────────────────

        [Fact]
        public void A_json_anchor_is_read()
        {
            var a = SheetQrConfig.ParseAnchor("{\"x\":701,\"y\":85,\"size\":24}", Default);

            Assert.NotNull(a);
            Assert.Equal(701, a.XMm, 3);
            Assert.Equal(85, a.YMm, 3);
            Assert.Equal(24, a.SizeMm, 3);
            Assert.Equal(QrAnchorSource.FamilyParameter, a.Source);
        }

        [Theory]
        [InlineData("701,85,24")]
        [InlineData("701 85 24")]
        [InlineData("  701 , 85 , 24  ")]
        [InlineData("701;85;24")]
        public void A_typed_anchor_is_read_too(string raw)
        {
            // Nobody hand-types JSON into a Revit parameter box. Rejecting these would
            // mean the feature only works for people who already know it works.
            var a = SheetQrConfig.ParseAnchor(raw, Default);

            Assert.NotNull(a);
            Assert.Equal(701, a.XMm, 3);
            Assert.Equal(85, a.YMm, 3);
            Assert.Equal(24, a.SizeMm, 3);
        }

        [Fact]
        public void An_anchor_without_a_size_takes_the_default()
        {
            var a = SheetQrConfig.ParseAnchor("701,85", Default);

            Assert.NotNull(a);
            Assert.Equal(Default, a.SizeMm, 3);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("somewhere near the scan box")]
        [InlineData("701")]                                  // one number names no point
        [InlineData("{\"x\":701}")]                          // no y
        [InlineData("{ not json at all")]
        public void An_unusable_anchor_is_null_rather_than_a_guess(string raw)
        {
            // Null sends the caller to the fallback, which SAYS it fell back. A guessed
            // anchor would place a stamp somewhere plausible-looking and silent.
            Assert.Null(SheetQrConfig.ParseAnchor(raw, Default));
        }

        [Theory]
        [InlineData("{\"x\":701,\"y\":85,\"size\":0}")]
        [InlineData("{\"x\":701,\"y\":85,\"size\":-5}")]
        public void A_zero_or_negative_size_falls_back_to_the_default_not_to_nothing(string raw)
        {
            // A zero-size anchor would place a stamp with no extent — it renders as
            // nothing, which looks exactly like the feature being switched off.
            var a = SheetQrConfig.ParseAnchor(raw, Default);
            Assert.NotNull(a);
            Assert.Equal(Default, a.SizeMm, 3);
        }

        [Fact]
        public void Negative_coordinates_parse_because_only_the_placer_may_reject_them()
        {
            // Parsing and validating are different jobs. SheetQrPlacement owns the
            // "is this on the paper" decision and reports WHY it refused; swallowing
            // it here would lose that sentence.
            var a = SheetQrConfig.ParseAnchor("{\"x\":1143,\"y\":-29,\"size\":26}", Default);
            Assert.NotNull(a);
            Assert.Equal(-29, a.YMm, 3);
        }

        // ── The three real sheets ─────────────────────────────────────────────

        [Theory]
        [InlineData(196.0, 403.0, 24.0)]   // cover  — mid-left, high up
        [InlineData(767.0, 109.0, 24.0)]   // 001    — bottom-right
        [InlineData(591.0, 34.0, 24.0)]    // 002    — bottom-middle
        public void Each_of_the_three_real_cells_round_trips(double x, double y, double size)
        {
            // Pinned as VALUES because these are the coordinates that proved a single
            // fallback corner cannot work. If a future refactor reintroduces a fixed
            // position, these are the cases it has to explain away.
            var raw = $"{{\"x\":{x},\"y\":{y},\"size\":{size}}}";
            var a = SheetQrConfig.ParseAnchor(raw, Default);

            Assert.NotNull(a);
            Assert.Equal(x, a.XMm, 3);
            Assert.Equal(y, a.YMm, 3);

            // And the placement layer accepts it, on an A1 sheet.
            var plan = SheetQrPlacement.Plan(a.ToRect(), new QrRect(0, 0, 841, 594));
            Assert.Equal(QrPlacementSource.Slot, plan.Source);
            Assert.Equal(x + size / 2, plan.CentreXMm, 3);
        }

        // ── The family's own slot map ─────────────────────────────────────────

        [Fact]
        public void A_qr_slot_in_the_family_slot_map_is_found_by_purpose_tag()
        {
            const string json = @"[
              {""name"":""S01"",""purposeTag"":""main-plan"",""x"":10,""y"":120,""w"":810,""h"":470},
              {""name"":""QRBOX"",""purposeTag"":""qr-code"",""x"":701,""y"":85,""w"":24,""h"":24}
            ]";

            var a = SheetQrConfig.ParseSlotMap(json, Default);

            Assert.NotNull(a);
            Assert.Equal(701, a.XMm, 3);
            Assert.Equal(24, a.SizeMm, 3);
            Assert.Equal(QrAnchorSource.FamilySlotMap, a.Source);
        }

        [Fact]
        public void A_slot_merely_NAMED_QR_is_found_too()
        {
            // Families authored before the purposeTag vocabulary existed still name
            // the cell. Requiring the tag would exclude exactly the older title blocks
            // most likely to need this.
            var a = SheetQrConfig.ParseSlotMap(@"[{""name"":""QR"",""x"":50,""y"":20,""w"":18,""h"":18}]", Default);

            Assert.NotNull(a);
            Assert.Equal(50, a.XMm, 3);
        }

        [Fact]
        public void A_non_square_slot_is_fitted_to_its_short_side()
        {
            var a = SheetQrConfig.ParseSlotMap(@"[{""purposeTag"":""qr-code"",""x"":0,""y"":0,""w"":60,""h"":25}]", Default);

            Assert.NotNull(a);
            Assert.Equal(25, a.SizeMm, 3);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData(@"[{""purposeTag"":""main-plan"",""x"":10,""y"":120,""w"":810,""h"":470}]")]
        public void A_slot_map_with_no_qr_cell_is_null(string json)
            => Assert.Null(SheetQrConfig.ParseSlotMap(json, Default));

        // ── The payload template ──────────────────────────────────────────────

        [Fact]
        public void A_template_renders_its_tokens()
        {
            var tokens = new Dictionary<string, string>
            {
                ["project"] = "KUT", ["sheet"] = "M-101", ["rev"] = "P03",
            };

            var s = SheetQrConfig.RenderTemplate("https://cde.client.com/{project}/{sheet}?rev={rev}", tokens);

            Assert.Equal("https://cde.client.com/KUT/M-101?rev=P03", s);
        }

        [Fact]
        public void A_template_can_wrap_the_standard_link_rather_than_replace_it()
        {
            var tokens = new Dictionary<string, string> { ["url"] = "https://app.planscape.build/s/KUT/M-101" };

            var s = SheetQrConfig.RenderTemplate("{url}&src=print", tokens);

            Assert.Equal("https://app.planscape.build/s/KUT/M-101&src=print", s);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void No_template_means_null_so_the_caller_uses_the_standard_link(string t)
        {
            // The default MUST stay the thing the Planscape scanner understands. A
            // template applied by accident would produce codes our own app rejects,
            // which is the exact defect this whole feature began as.
            Assert.Null(SheetQrConfig.RenderTemplate(t, new Dictionary<string, string>()));
        }

        [Fact]
        public void An_unknown_token_is_left_literal_rather_than_blanked()
        {
            // Matches TitleBlockParamApplier. A typo then shows up on the drawing as
            // {sheetnumber} — visible and fixable — instead of silently vanishing.
            var s = SheetQrConfig.RenderTemplate("x/{sheetnumber}", new Dictionary<string, string> { ["sheet"] = "M-101" });

            Assert.Equal("x/{sheetnumber}", s);
        }

        [Fact]
        public void Tokens_are_case_insensitive_in_practice()
        {
            var tokens = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase) { ["Sheet"] = "M-101" };
            Assert.Equal("M-101", SheetQrConfig.RenderTemplate("{Sheet}", tokens));
        }

        // ── The size override ─────────────────────────────────────────────────

        [Theory]
        [InlineData("30", 30.0)]
        [InlineData("30mm", 30.0)]
        [InlineData(" 18 ", 18.0)]
        [InlineData("12.5", 12.5)]
        public void A_size_override_is_read(string raw, double expected)
            => Assert.Equal(expected, SheetQrConfig.ParseSize(raw).Value, 3);

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("big")]
        [InlineData("0")]
        [InlineData("-5")]
        public void An_unusable_size_is_null_never_zero(string raw)
        {
            // A 0 would place an invisible stamp — indistinguishable from no stamp.
            Assert.Null(SheetQrConfig.ParseSize(raw));
        }

        // ── FormatAnchor ↔ ParseAnchor ───────────────────────────────────────
        //
        // These two are a pair. FormatAnchor is what StingQrAnchorSchema stores in
        // Extensible Storage, ParseAnchor is what SheetQrStamper reads back, and a
        // drift between them would strand every anchor ever recorded — the operator
        // picks a cell, is told it was saved, and the stamp still lands in a corner.
        // The Revit half cannot be tested here, so the CONTRACT is tested instead.

        [Theory]
        [InlineData(701.0, 85.0, 24.0)]
        [InlineData(0.0, 0.0, 12.0)]
        [InlineData(1153.25, 10.5, 26.0)]
        [InlineData(216.0, 10.0, 20.0)]
        public void A_formatted_anchor_parses_back_to_itself(double x, double y, double size)
        {
            var back = SheetQrConfig.ParseAnchor(SheetQrConfig.FormatAnchor(x, y, size), 0.0);

            Assert.NotNull(back);
            Assert.Equal(x, back.XMm, 2);
            Assert.Equal(y, back.YMm, 2);
            Assert.Equal(size, back.SizeMm, 2);
        }

        [Fact]
        public void A_formatted_anchor_carries_its_own_size_not_the_default()
        {
            // Read with a default of 0: if FormatAnchor ever stopped emitting "size",
            // this would come back 0 and the stamper would refuse it as unusable
            // rather than quietly substituting the planner default.
            var back = SheetQrConfig.ParseAnchor(SheetQrConfig.FormatAnchor(50, 60, 18), 0.0);

            Assert.NotNull(back);
            Assert.Equal(18.0, back.SizeMm, 2);
        }

        // ── Fact normalisers ─────────────────────────────────────────────────
        //
        // Both exist because the PRINTED form of a value and its PAYLOAD form are
        // different things. The sheet says "025 / 100" and "LOD 300" because a human
        // reads those next to other codes; the QR wants the numbers. Left raw, the
        // producer's alphanumeric folding turns every space and slash into '.', so
        // "025 / 100" would ship as "025...100" — six wasted characters out of a
        // budget measured in tens.

        [Theory]
        [InlineData("025 / 100", "25.100")]
        [InlineData("25 OF 100", "25.100")]
        [InlineData("1/12", "1.12")]
        [InlineData("007 / 009", "7.9")]
        public void The_sheet_position_becomes_two_bare_numbers(string raw, string expected)
            => Assert.Equal(expected, SheetQrConfig.NormaliseSheetOfTotal(raw));

        [Fact]
        public void A_zero_padded_sheet_position_does_not_become_empty()
        {
            // TrimStart('0') on "000" leaves nothing at all; the result must still be
            // a number rather than a stray separator.
            Assert.Equal("0.100", SheetQrConfig.NormaliseSheetOfTotal("000 / 100"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void An_absent_sheet_position_is_null(string raw)
            => Assert.Null(SheetQrConfig.NormaliseSheetOfTotal(raw));

        [Fact]
        public void A_sheet_position_with_only_one_number_is_passed_through()
        {
            // Better to carry something a person can interpret than to invent a total.
            Assert.Equal("25", SheetQrConfig.NormaliseSheetOfTotal("25"));
        }

        [Theory]
        [InlineData("LOD 300", "300")]
        [InlineData("LOD350", "350")]
        [InlineData("200", "200")]
        [InlineData(" lod 400 ", "400")]
        public void The_lod_becomes_the_number_alone(string raw, string expected)
            => Assert.Equal(expected, SheetQrConfig.NormaliseLod(raw));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void An_absent_lod_is_null(string raw)
            => Assert.Null(SheetQrConfig.NormaliseLod(raw));

        [Fact]
        public void A_non_numeric_lod_is_passed_through_not_dropped()
            => Assert.Equal("TBC", SheetQrConfig.NormaliseLod("TBC"));

        [Fact]
        public void A_formatted_anchor_uses_a_dot_decimal_separator()
        {
            // On a comma-decimal locale a culture-sensitive format would emit
            // {"x":701,5} — valid-looking, unparseable, and only reproducible on
            // machines nobody tests on.
            var json = SheetQrConfig.FormatAnchor(701.5, 85.25, 24.0);

            Assert.Contains("701.5", json);
            Assert.Contains("85.25", json);
            Assert.DoesNotContain("701,5", json);
        }
    }
}
