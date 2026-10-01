using System.Collections.Generic;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-194..213 — the drawing producer's edge cases, decided Revit-free
    /// (ProductionEdgeDecisions). Each test names the defect it pins.
    /// </summary>
    public class ProductionEdgeDecisionsTests
    {
        // ── DTW-194 ───────────────────────────────────────────────────

        [Fact]
        public void A_run_that_cannot_write_the_counters_says_who_owns_them_and_that_nothing_was_guessed()
        {
            var line = ProductionEdgeDecisions.CountersBlockedLine("owned by Jane (Project Information)");
            Assert.Contains("owned by Jane", line);
            Assert.Contains("never numbered from a guess", line);
            Assert.StartsWith("Run stopped before any drawing was produced", line);
        }

        [Fact]
        public void A_sheet_that_cannot_be_numbered_is_reported_with_its_type_and_reason()
        {
            var line = ProductionEdgeDecisions.SheetNotNumberedLine("arch-plan-A1-1to100", "reload latest");
            Assert.Contains("'arch-plan-A1-1to100'", line);
            Assert.Contains("reload latest", line);
            Assert.Contains("no sheet was made", line);
        }

        [Fact]
        public void A_missing_reason_still_reads_as_a_reason()
            => Assert.Contains("reason unknown", ProductionEdgeDecisions.CountersBlockedLine(null));

        // ── DTW-197 ───────────────────────────────────────────────────

        [Fact]
        public void No_sheet_is_made_before_a_view_exists()
            => Assert.False(ProductionEdgeDecisions.CreateSheetNow(true, sheetKnown: false, sheetAttempted: false, viewProduced: false));

        [Fact]
        public void The_first_produced_view_makes_the_sheet_once()
        {
            Assert.True(ProductionEdgeDecisions.CreateSheetNow(true, false, false, true));
            Assert.False(ProductionEdgeDecisions.CreateSheetNow(true, false, sheetAttempted: true, viewProduced: true));
        }

        [Fact]
        public void An_existing_sheet_or_no_sheet_requested_makes_none()
        {
            Assert.False(ProductionEdgeDecisions.CreateSheetNow(true, sheetKnown: true, sheetAttempted: false, viewProduced: true));
            Assert.False(ProductionEdgeDecisions.CreateSheetNow(false, false, false, true));
        }

        [Theory]
        [InlineData(true, true, 0, 0, true)]    // made, nothing placed: removed
        [InlineData(true, true, 1, 0, false)]   // something placed: kept
        [InlineData(true, false, 0, 0, false)]  // views placed later by the caller: kept
        [InlineData(false, true, 0, 0, false)]  // an existing sheet is never removed
        public void An_empty_new_sheet_is_removed(bool created, bool place, int placed, int reused, bool expected)
            => Assert.Equal(expected, ProductionEdgeDecisions.DiscardNewSheet(created, place, placed, reused));

        // ── DTW-196 ───────────────────────────────────────────────────

        [Fact]
        public void A_fitted_view_keeps_its_fitted_scale_on_a_rerun()
            => Assert.Equal(200, ProductionEdgeDecisions.ScaleOnRefresh(typeScale: 100, fittedScale: 200, fitBaseScale: 100));

        [Fact]
        public void A_changed_type_scale_wins_over_an_old_fit()
            => Assert.Equal(0, ProductionEdgeDecisions.ScaleOnRefresh(typeScale: 50, fittedScale: 200, fitBaseScale: 100));

        [Fact]
        public void No_record_means_the_type_scale()
            => Assert.Equal(0, ProductionEdgeDecisions.ScaleOnRefresh(100, 0, 0));

        [Theory]
        [InlineData(false, false, 0, 100, true)]    // produced before the record: fit again
        [InlineData(false, true, 100, 100, false)]  // recorded, type unchanged: kept
        [InlineData(false, true, 100, 50, true)]    // type scale changed: fit again
        [InlineData(true, false, 0, 100, false)]    // a pinned scale is never fitted
        public void A_placed_view_is_fitted_again_only_when_its_fit_is_missing_or_stale(
            bool pinned, bool recorded, int fitBase, int typeScale, bool expected)
            => Assert.Equal(expected, ProductionEdgeDecisions.RefitOnRerun(pinned, recorded, fitBase, typeScale));

        [Fact]
        public void A_replaced_template_is_named_with_the_way_to_keep_it()
        {
            var line = ProductionEdgeDecisions.TemplateReplacedLine("Level 1 Power", "My Power", "STING - Electrical Plan", "elec-power-A1-1to100");
            Assert.Contains("'My Power' was replaced by 'STING - Electrical Plan'", line);
            Assert.Contains("Lock the view's style", line);
        }

        // ── DTW-198 ───────────────────────────────────────────────────

        [Fact]
        public void Basement_1_and_2_get_distinct_number_tokens()
        {
            var b1 = ProductionEdgeDecisions.NumberLevel("E-{lvl}-{seq:D3}", "Basement 1", null, null);
            var b2 = ProductionEdgeDecisions.NumberLevel("E-{lvl}-{seq:D3}", "Basement 2", null, null);
            Assert.NotEqual(b1, b2);
            Assert.Equal("Basemen1", b1);
            // Through the real number engine, as the producer substitutes it.
            Assert.NotEqual(
                SheetNumberEngine.ApplyTokenPattern("E-{lvl}-{seq:D3}", "E", b1, "", "", "", "Plan", 1, null),
                SheetNumberEngine.ApplyTokenPattern("E-{lvl}-{seq:D3}", "E", b2, "", "", "", "Plan", 1, null));
        }

        [Fact]
        public void An_iso_pattern_keeps_the_iso_level_code()
            => Assert.Equal("B1", ProductionEdgeDecisions.NumberLevel(SheetNumberPolicy.IsoPattern, "Basement 1", "B1", null));

        [Fact]
        public void No_level_falls_back_to_the_profile_level()
            => Assert.Equal("ZZ", ProductionEdgeDecisions.NumberLevel("E-{lvl}", null, null, "ZZ"));

        [Fact]
        public void Sheet_names_carry_the_full_level_name()
        {
            Assert.Equal("Power Layout - Basement 1",
                ProductionEdgeDecisions.SheetName("Power Layout - {lvl}", "E", "Basement 1", "", null, "Plan", 1, null, null));
            Assert.Equal("Power Layout - Ground Floor",
                ProductionEdgeDecisions.SheetName("Power Layout - {lvl}", "E", "Ground Floor", "", null, "Plan", 1, null, null));
        }

        [Fact]
        public void An_area_sheet_name_says_which_area_unless_the_pattern_does()
        {
            Assert.Equal("Power Layout - Level 1 - North",
                ProductionEdgeDecisions.SheetName("Power Layout - {lvl}", "E", "Level 1", "", "North", "Plan", 1, null, "North"));
            Assert.Equal("Power - North",
                ProductionEdgeDecisions.SheetName("Power - {mark}", "E", "Level 1", "", "North", "Plan", 1, null, "North"));
        }

        // ── DTW-213 ───────────────────────────────────────────────────

        [Fact]
        public void A_reused_id_now_a_different_element_does_not_hold_the_name()
        {
            // The rolled-back sheet "E-101" took id 42; Revit gave 42 to a view.
            Assert.False(ProductionEdgeDecisions.StillHolds(false, exists: true, expectedKind: false, "Level 1", "E-101", true));
            // ... or to another sheet numbered differently.
            Assert.False(ProductionEdgeDecisions.StillHolds(false, true, true, "E-205", "E-101", true));
            Assert.True(ProductionEdgeDecisions.StillHolds(false, true, true, "e-101", "E-101", ignoreCase: true));
            Assert.True(ProductionEdgeDecisions.StillHolds(ownerUnknown: true, false, false, null, "E-101", true));
            Assert.False(ProductionEdgeDecisions.StillHolds(false, exists: false, true, null, "E-101", true));
        }

        [Fact]
        public void The_ledger_releases_a_name_whose_reused_owner_no_longer_carries_it()
        {
            var ledger = new BatchNameLedger(new[] { "A-100" }, System.StringComparer.OrdinalIgnoreCase);
            ledger.Record("A-101", 42);
            // Id 42 still exists — the id-only check kept "A-101" taken.
            Assert.True(ledger.Contains("A-101", id => true));
            // By identity: element 42 is now numbered A-300, so A-101 is free.
            Assert.False(ledger.Contains("A-101", (name, id) => ProductionEdgeDecisions.StillHolds(false, true, true, "A-300", name, true)));
            // Pre-existing names have no owner and stay taken.
            Assert.True(ledger.Contains("A-100", (name, id) => false));
        }

        // ── DTW-203 ───────────────────────────────────────────────────

        [Fact]
        public void A_type_adopts_its_replaces_and_the_callers_former_ids_once_each()
        {
            var dt = new DrawingType { Id = "kut-power", Replaces = new List<string> { "elec-power-A1-1to100", " ", "KUT-POWER" } };
            var ids = ProductionEdgeDecisions.FormerIds(dt, new[] { "ELEC-POWER-A1-1to100", "legacy-power" });
            Assert.Equal(new[] { "elec-power-A1-1to100", "legacy-power" }, ids);
        }

        [Fact]
        public void The_doctor_names_the_replacing_type_or_reports_an_orphan()
        {
            var catalogue = new[]
            {
                new DrawingType { Id = "kut-power", Replaces = new List<string> { "elec-power-A1-1to100" } },
                new DrawingType { Id = "arch-plan-A1-1to100" },
            };
            Assert.Null(ProductionEdgeDecisions.ReplacementFor("arch-plan-A1-1to100", catalogue));
            Assert.Equal("kut-power", ProductionEdgeDecisions.ReplacementFor("elec-power-A1-1to100", catalogue));
            Assert.Equal("", ProductionEdgeDecisions.ReplacementFor("gone-type", catalogue));
            Assert.Null(ProductionEdgeDecisions.ReplacementFor(null, catalogue));
        }

        [Fact]
        public void An_absent_replaces_list_is_not_serialised_so_checksums_hold()
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(new DrawingType { Id = "x" });
            Assert.DoesNotContain("\"replaces\"", json);
        }

        // ── DTW-206 ───────────────────────────────────────────────────

        [Fact]
        public void A_box_reaching_the_level_is_accepted()
            => Assert.Null(ProductionEdgeDecisions.BoxMissesLevel("STING::A", "Level 1", -1.0, 20.0, 0.0));

        [Fact]
        public void A_box_above_the_level_is_refused_with_both_extents()
        {
            var why = ProductionEdgeDecisions.BoxMissesLevel("STING::A", "Level 1", 10.0, 20.0, 0.0);
            Assert.NotNull(why);
            Assert.Contains("does not reach level 'Level 1'", why);
            Assert.Contains("extend the box", why);
        }

        [Fact]
        public void A_level_on_the_box_edge_counts_as_reached()
            => Assert.Null(ProductionEdgeDecisions.BoxMissesLevel("b", "L", 0.0, 10.0, 10.0));

        // ── DTW-208 ───────────────────────────────────────────────────

        private static readonly string[] RevitFilters = { "Show All", "Show Complete", "Show New", "Show Previous + Demo" };

        [Fact]
        public void A_new_view_on_show_all_moves_to_show_complete()
            => Assert.Equal("Show Complete", ProductionEdgeDecisions.ProductionPhaseFilter(null, "Show All", false, RevitFilters));

        [Fact]
        public void The_packs_phase_filter_wins_when_the_project_has_it()
            => Assert.Equal("Show New", ProductionEdgeDecisions.ProductionPhaseFilter("show new", "Show All", false, RevitFilters));

        [Fact]
        public void A_missing_pack_filter_falls_back_to_the_default_rule()
            => Assert.Equal("Show Complete", ProductionEdgeDecisions.ProductionPhaseFilter("Show Demo + New", "Show All", false, RevitFilters));

        [Theory]
        [InlineData(null, "Show All", true)]          // the template controls it
        [InlineData(null, "Show New", false)]         // a deliberate filter is left alone
        [InlineData("Show Complete", "Show Complete", false)]  // already there
        public void Otherwise_the_filter_is_left(string pack, string current, bool templateControls)
            => Assert.Null(ProductionEdgeDecisions.ProductionPhaseFilter(pack, current, templateControls, RevitFilters));

        // ── DTW-209 ───────────────────────────────────────────────────

        [Fact]
        public void A_generated_name_follows_the_level_rename()
            => Assert.Equal("Power - Ground Floor", ProductionEdgeDecisions.ReusedSheetRename(
                "Power - Level 0", storedGenerated: "Power - Level 0", legacyExpected: null, expectedNow: "Power - Ground Floor", locked: false));

        [Fact]
        public void A_hand_edited_name_is_left_alone()
            => Assert.Null(ProductionEdgeDecisions.ReusedSheetRename(
                "Power - Entrance Hall", "Power - Level 0", null, "Power - Ground Floor", false));

        [Fact]
        public void A_sheet_from_before_the_record_is_renamed_only_when_it_matches_its_old_level()
        {
            Assert.Equal("Power - Ground Floor", ProductionEdgeDecisions.ReusedSheetRename(
                "Power - Level 0", null, legacyExpected: "Power - Level 0", "Power - Ground Floor", false));
            Assert.Null(ProductionEdgeDecisions.ReusedSheetRename(
                "Power - Lobby", null, "Power - Level 0", "Power - Ground Floor", false));
            Assert.Null(ProductionEdgeDecisions.ReusedSheetRename(
                "Power - Level 0", null, null, "Power - Ground Floor", false));
        }

        [Fact]
        public void A_locked_sheet_keeps_its_name()
            => Assert.Null(ProductionEdgeDecisions.ReusedSheetRename(
                "Power - Level 0", "Power - Level 0", null, "Power - Ground Floor", locked: true));

        // ── DTW-199 ───────────────────────────────────────────────────

        [Fact]
        public void A_view_moved_to_another_sheet_is_reported_as_kept_there()
        {
            var line = ProductionEdgeDecisions.KeptOnOtherSheetLine("Power - Level 1", "E-105", "E-101");
            Assert.Contains("kept on sheet E-105", line);
            Assert.Contains("not placed on E-101", line);
        }
    }
}
