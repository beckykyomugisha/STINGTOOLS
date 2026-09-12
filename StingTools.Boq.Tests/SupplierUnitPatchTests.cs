using System.Collections.Generic;
using System.Linq;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// A type mapping says "this model's `Generic - 225mm` is a roof tile". It
    /// must not be able to say anything else.
    ///
    /// The existing project override replaces a rule WHOLESALE by CommodityKey,
    /// so a file written to add one pattern also sets
    /// SourceUnitsPerSupplierUnit to its default of 1.0 and DefaultWastagePct
    /// to 0 — silently turning "2.4 m² per sheet, 10% waste" into "1 m² per
    /// sheet, none", across the whole schedule, with nothing anywhere saying
    /// why. These tests exist to keep that unrepresentable rather than merely
    /// discouraged.
    /// </summary>
    public class SupplierUnitPatchTests
    {
        private static SupplierUnitTable Table() => new SupplierUnitTable
        {
            Rules =
            {
                new SupplierUnitRule
                {
                    CommodityKey = "roof-sheet", Description = "Corrugated iron sheet",
                    SupplierUnit = "Sheets", SourceUnit = "m2",
                    SourceUnitsPerSupplierUnit = 2.4, RoundUpToWhole = true, DefaultWastagePct = 10,
                    MatchCategories = { "Roofs" }, MatchTypePatterns = { "IT4", "Corrugated" }
                },
                new SupplierUnitRule
                {
                    CommodityKey = "roof-tile", Description = "Clay roof tile",
                    SupplierUnit = "No.", SourceUnit = "m2",
                    SourceUnitsPerSupplierUnit = 0.1, RoundUpToWhole = true, DefaultWastagePct = 8,
                    MatchCategories = { "Roofs" }, MatchTypePatterns = { "Tile" }
                }
            }
        };

        private static MaterialCommodity Roof(string desc, double net = 610.61, double order = 610.62) =>
            new MaterialCommodity
            {
                CommodityKey = desc, Description = desc, SupplierUnit = "m2",
                NetQuantity = net, OrderQuantity = order, ConversionBlocked = true,
                Categories = { "Roofs" }, TypeNames = { desc }
            };

        // ── the file shape is the guarantee ─────────────────────────────────

        [Fact]
        public void A_Patch_Cannot_Express_A_Conversion_Factor()
        {
            // Not "does not" — CANNOT. The type has nowhere to put one, so a
            // pricing dialog cannot restate a measurement rule by accident.
            var fields = typeof(SupplierUnitPatch).GetFields()
                            .Select(f => f.Name).ToArray();

            Assert.DoesNotContain("SourceUnitsPerSupplierUnit", fields);
            Assert.DoesNotContain("DefaultWastagePct", fields);
            Assert.DoesNotContain("SupplierUnit", fields);
            Assert.DoesNotContain("RoundUpToWhole", fields);
        }

        [Fact]
        public void Applying_A_Patch_Leaves_Every_Measurement_Field_Untouched()
        {
            var table = Table();
            var file = new SupplierUnitPatchFile
            {
                TypePatterns = { new SupplierUnitPatch { CommodityKey = "roof-sheet", Pattern = "Generic - 225" } }
            };

            SupplierUnitPatcher.Apply(table, file);

            var rule = table.ResolveByCommodityKey("roof-sheet");
            Assert.Equal(2.4, rule.SourceUnitsPerSupplierUnit);
            Assert.Equal(10, rule.DefaultWastagePct);
            Assert.Equal("Sheets", rule.SupplierUnit);
            Assert.True(rule.RoundUpToWhole);
        }

        [Fact]
        public void The_Pattern_Is_Added_To_The_Rule()
        {
            var table = Table();
            var file = new SupplierUnitPatchFile
            {
                TypePatterns = { new SupplierUnitPatch { CommodityKey = "roof-tile", Pattern = "Generic - 225" } }
            };

            var applied = SupplierUnitPatcher.Apply(table, file);

            Assert.Contains("Generic - 225", table.ResolveByCommodityKey("roof-tile").MatchTypePatterns);
            Assert.Single(applied);
        }

        [Fact]
        public void The_Corporate_Patterns_Survive()
        {
            // Additive, not replacing. IT4 roofs must keep converting.
            var table = Table();
            SupplierUnitPatcher.Apply(table, new SupplierUnitPatchFile
            {
                TypePatterns = { new SupplierUnitPatch { CommodityKey = "roof-sheet", Pattern = "Generic - 225" } }
            });

            Assert.Contains("IT4", table.ResolveByCommodityKey("roof-sheet").MatchTypePatterns);
        }

        [Fact]
        public void Patching_The_Same_Pattern_Twice_Adds_It_Once()
        {
            var table = Table();
            var file = new SupplierUnitPatchFile
            {
                TypePatterns =
                {
                    new SupplierUnitPatch { CommodityKey = "roof-tile", Pattern = "Generic - 225" },
                    new SupplierUnitPatch { CommodityKey = "roof-tile", Pattern = "generic - 225" }
                }
            };

            SupplierUnitPatcher.Apply(table, file);

            Assert.Equal(2, table.ResolveByCommodityKey("roof-tile").MatchTypePatterns.Count);  // "Tile" + one
        }

        [Fact]
        public void A_Patch_For_An_Unknown_Commodity_Changes_Nothing_And_Is_Reported()
        {
            var table = Table();
            var file = new SupplierUnitPatchFile
            {
                TypePatterns = { new SupplierUnitPatch { CommodityKey = "roof-shet", Pattern = "Generic - 225" } }
            };

            Assert.Empty(SupplierUnitPatcher.Apply(table, file));
            Assert.Contains(file.Validate(table), p => p.Contains("not a commodity"));
        }

        [Fact]
        public void A_Pattern_Too_Short_To_Be_Safe_Is_Rejected()
        {
            // "22" would claim "Generic - 225mm" and every 2200-wide door.
            var file = new SupplierUnitPatchFile
            {
                TypePatterns = { new SupplierUnitPatch { CommodityKey = "roof-tile", Pattern = "22" } }
            };

            Assert.Contains(file.Validate(Table()), p => p.Contains("too short"));
        }

        [Fact]
        public void The_Summary_Says_Units_Change_But_Factors_Do_Not()
        {
            string s = SupplierUnitPatcher.Summary(new[] { "Generic - 225 → roof-tile" });

            Assert.Contains("UNIT and quantity", s);
            Assert.Contains("do not change any conversion factor", s);
        }

        [Fact]
        public void A_Project_That_Maps_Nothing_Gets_No_Line()
        {
            Assert.Null(SupplierUnitPatcher.Summary(new string[0]));
            Assert.Null(SupplierUnitPatcher.Summary(null));
        }

        // ── the preview ─────────────────────────────────────────────────────

        [Fact]
        public void The_Plan_Shows_Before_And_After_In_BOTH_Units()
        {
            // 610.61 m² ÷ 2.4 m² per sheet, +10% waste, rounded up.
            var plan = TypePatternPlanner.Plan(Table(), new[] { Roof("Generic - 225mm") },
                                               "roof-sheet", "Generic - 225");

            var m = plan.Matches.Single();
            Assert.Equal("m2", m.BeforeUnit);
            Assert.Equal("Sheets", m.AfterUnit);
            Assert.True(m.AfterQuantity > 0 && m.AfterQuantity < m.BeforeQuantity);
        }

        [Fact]
        public void A_Substring_Pattern_Shows_EVERY_Row_It_Would_Claim()
        {
            // The point of the preview: "225" is a substring test and catches
            // all three roofs, which the author may or may not have meant.
            var plan = TypePatternPlanner.Plan(Table(),
                new[] { Roof("Generic - 225mm"), Roof("Generic - 225mm 2"), Roof("Generic - 225mm 3") },
                "roof-tile", "225mm");

            Assert.Equal(3, plan.Matches.Count);
            Assert.Contains("would claim 3 row(s)", plan.Summary());
        }

        [Fact]
        public void A_Roof_Mapping_Always_Warns_That_Sheet_And_Tile_Both_Look_Right()
        {
            // Both convert from area and both look correct afterwards. This is
            // the confident-wrong-number case, so it is named every time.
            var plan = TypePatternPlanner.Plan(Table(), new[] { Roof("Generic - 225mm") },
                                               "roof-sheet", "Generic - 225");

            Assert.Contains(plan.Warnings, w => w.Contains("Sheet and tile"));
        }

        [Fact]
        public void A_Pattern_Matching_Nothing_Says_So_Rather_Than_Offering_To_Apply()
        {
            var plan = TypePatternPlanner.Plan(Table(), new[] { Roof("Generic - 225mm") },
                                               "roof-tile", "Corrugated");

            Assert.False(plan.CanApply);
            Assert.Contains("matches no commodity row", plan.Summary());
        }

        [Fact]
        public void A_Short_Pattern_Blocks_Rather_Than_Warns()
        {
            var plan = TypePatternPlanner.Plan(Table(), new[] { Roof("Generic - 225mm") }, "roof-tile", "22");

            Assert.False(plan.CanApply);
            Assert.Contains("cannot be applied", plan.Summary());
        }

        [Fact]
        public void An_Unknown_Commodity_Blocks()
        {
            var plan = TypePatternPlanner.Plan(Table(), new[] { Roof("Generic - 225mm") }, "nonsense", "Generic - 225");

            Assert.False(plan.CanApply);
            Assert.Contains(plan.Blockers, b => b.Contains("not a commodity"));
        }

        [Fact]
        public void A_Memorandum_Is_Never_Matched()
        {
            // It carries no rate and no purchasable quantity; re-routing it
            // would give the double-count a unit.
            var memo = Roof("Generic - 225mm");
            memo.IsMemorandum = true;

            Assert.Empty(TypePatternPlanner.Plan(Table(), new[] { memo }, "roof-tile", "Generic - 225").Matches);
        }

        [Fact]
        public void The_Commodity_List_Is_Offered_So_A_Key_Cannot_Be_Misspelt()
        {
            var keys = TypePatternPlanner.Candidates(Table()).Select(r => r.CommodityKey).ToArray();

            Assert.Equal(new[] { "roof-sheet", "roof-tile" }, keys);
        }

        [Fact]
        public void Both_Roof_Commodities_Are_Offered_For_The_Same_Row()
        {
            // The user asked for both: a roof can be mapped to sheeting OR to
            // tiles, and the schedule cannot know which the building has.
            var sheet = TypePatternPlanner.Plan(Table(), new[] { Roof("Generic - 225mm") }, "roof-sheet", "Generic - 225");
            var tile  = TypePatternPlanner.Plan(Table(), new[] { Roof("Generic - 225mm") }, "roof-tile",  "Generic - 225");

            Assert.True(sheet.CanApply);
            Assert.True(tile.CanApply);
            Assert.NotEqual(sheet.Matches.Single().AfterQuantity, tile.Matches.Single().AfterQuantity);
        }
    }
}
