using System;
using System.Collections.Generic;
using StingTools.Core.Variation;
using Xunit;

namespace StingTools.Cost.Tests
{
    // PM-3 — instructed-daywork build-up + register totals.
    //
    // The commercially consequential assertions here are (a) the percentage
    // additions follow the RICS convention gross = net × (1 + pct/100) rather
    // than any ×pct or ×pct/100 misreading, and (b) an attached sheet is
    // excluded from the final-account total so it can never be counted twice.
    public class DayworkBuildUpTests
    {
        private static StarRateLine Hours(string res, double hrs, double rate)
            => new StarRateLine { Resource = res, Hours = hrs, UnitRate = rate, Unit = "hr" };

        private static StarRateLine Qty(string res, double qty, double rate, string unit = "each")
            => new StarRateLine { Resource = res, Quantity = qty, UnitRate = rate, Unit = unit };

        /// <summary>Sheet with RICS-plausible percentages: labour 115% addition,
        /// materials 12%, plant 10%.</summary>
        private static DayworkRecord Sheet() => new DayworkRecord
        {
            InstructionRef = "CI-014",
            Description = "Break out unrecorded concrete obstruction",
            LabourLines = { Hours("General labourer", 16, 5_000), Hours("Foreman", 4, 12_500) },
            PlantLines = { Hours("Breaker + compressor", 8, 30_000) },
            MaterialsLines = { Qty("Disposal skip", 2, 150_000) },
            BuildUp = new DayworkBuildUp
            {
                LabourAdditionPct = 115,
                MaterialsAdditionPct = 12,
                PlantAdditionPct = 10
            }
        };

        [Fact]
        public void Net_prime_cost_sums_each_section_on_its_own_basis()
        {
            var d = Sheet();
            // Labour: 16×5,000 + 4×12,500 = 80,000 + 50,000 = 130,000 (on Hours).
            Assert.Equal(130_000, d.LabourNet);
            // Plant: 8×30,000 = 240,000 (on Hours).
            Assert.Equal(240_000, d.PlantNet);
            // Materials: 2×150,000 = 300,000 (on Quantity).
            Assert.Equal(300_000, d.MaterialsNet);
            Assert.Equal(670_000, d.NetTotal);
        }

        [Fact]
        public void Percentage_additions_are_additions_on_net_not_multipliers()
        {
            var d = Sheet();
            // The convention under test: gross = net × (1 + pct/100).
            Assert.Equal(149_500, d.LabourAddition);      // 130,000 × 1.15 addition
            Assert.Equal(24_000, d.PlantAddition);        // 240,000 × 0.10
            Assert.Equal(36_000, d.MaterialsAddition);    // 300,000 × 0.12
            Assert.Equal(209_500, d.AdditionTotal);

            Assert.Equal(279_500, d.LabourGross);         // 130,000 + 149,500
            Assert.Equal(264_000, d.PlantGross);
            Assert.Equal(336_000, d.MaterialsGross);
            Assert.Equal(879_500, d.GrossTotal);

            // Guard against the "×pct" misreading, which would give 130,000×115.
            Assert.NotEqual(14_950_000, d.LabourAddition);
        }

        [Fact]
        public void Gross_equals_net_plus_additions()
        {
            var d = Sheet();
            Assert.Equal(d.GrossTotal, d.NetTotal + d.AdditionTotal);
        }

        [Fact]
        public void Zero_percentages_price_at_net_prime_cost()
        {
            var d = Sheet();
            d.BuildUp = new DayworkBuildUp
            {
                LabourAdditionPct = 0, MaterialsAdditionPct = 0, PlantAdditionPct = 0
            };
            Assert.Equal(0, d.AdditionTotal);
            Assert.Equal(d.NetTotal, d.GrossTotal);
        }

        [Fact]
        public void Negative_percentages_clamp_to_zero_rather_than_crediting_the_employer()
        {
            var d = Sheet();
            d.BuildUp = new DayworkBuildUp
            {
                LabourAdditionPct = -50, MaterialsAdditionPct = -10, PlantAdditionPct = -10
            };
            Assert.Equal(0, d.AdditionTotal);
            Assert.Equal(d.NetTotal, d.GrossTotal);
        }

        [Fact]
        public void Empty_sheet_is_zero_and_reports_no_resources()
        {
            var d = new DayworkRecord();
            Assert.Equal(0, d.NetTotal);
            Assert.Equal(0, d.AdditionTotal);
            Assert.Equal(0, d.GrossTotal);
            Assert.False(d.HasResources);
        }

        [Fact]
        public void Time_based_lines_price_on_hours_and_material_lines_on_quantity()
        {
            // A line carrying BOTH Hours and Quantity must price on the basis its
            // Unit selects (StarRateLine.BasisQuantity, PM-1) — not Max of the two.
            var d = new DayworkRecord
            {
                LabourLines = { new StarRateLine { Resource = "Fitter", Hours = 10, Quantity = 999, UnitRate = 1_000, Unit = "hr" } },
                MaterialsLines = { new StarRateLine { Resource = "Pipe", Hours = 999, Quantity = 20, UnitRate = 500, Unit = "m" } },
                BuildUp = new DayworkBuildUp { LabourAdditionPct = 0, MaterialsAdditionPct = 0, PlantAdditionPct = 0 }
            };
            Assert.Equal(10_000, d.LabourNet);     // 10 hr × 1,000 — not 999 × 1,000
            Assert.Equal(10_000, d.MaterialsNet);  // 20 m × 500  — not 999 × 500
        }

        [Fact]
        public void Implausible_non_labour_additions_warn_but_labour_does_not()
        {
            // The server defaults (115/110/112) are uniform placeholders: 110% on
            // materials prices them at 2.1× net. Labour at 115% is RICS-typical,
            // so it must NOT warn.
            var w = new DayworkBuildUp().Warnings();   // defaults 115 / 110 / 112
            Assert.Equal(2, w.Count);
            Assert.Contains(w, s => s.Contains("Materials"));
            Assert.Contains(w, s => s.Contains("Plant"));
            Assert.DoesNotContain(w, s => s.Contains("Labour"));

            var sane = new DayworkBuildUp
            {
                LabourAdditionPct = 115, MaterialsAdditionPct = 12, PlantAdditionPct = 10
            };
            Assert.Empty(sane.Warnings());
        }
    }

    public class DayworkRegisterTests
    {
        private static DayworkRecord Priced(double net, string vo = "")
            => new DayworkRecord
            {
                Status = DayworkStatus.Priced,
                VariationNumber = vo,
                LabourLines = { new StarRateLine { Resource = "L", Hours = 1, UnitRate = net, Unit = "hr" } },
                BuildUp = new DayworkBuildUp { LabourAdditionPct = 0, MaterialsAdditionPct = 0, PlantAdditionPct = 0 }
            };

        [Fact]
        public void Attached_sheets_are_excluded_from_the_final_account_total()
        {
            var reg = new DayworkRegister
            {
                Records =
                {
                    Priced(100),              // unattached
                    Priced(250),              // unattached
                    Priced(400, "VO-007"),    // attached — rides its variation
                }
            };
            Assert.Equal(750, reg.PricedGrossTotal);              // register headline
            Assert.Equal(350, reg.UnattachedPricedGrossTotal);    // what the waterfall adds
        }

        [Fact]
        public void Unpriced_sheets_do_not_reach_the_final_account()
        {
            var recorded = Priced(500);
            recorded.Status = DayworkStatus.Recorded;
            var signed = Priced(300);
            signed.Status = DayworkStatus.Signed;

            var reg = new DayworkRegister { Records = { recorded, signed, Priced(100) } };

            Assert.Equal(100, reg.PricedGrossTotal);
            Assert.Equal(100, reg.UnattachedPricedGrossTotal);
            Assert.Equal(800, reg.UnpricedNetTotal);   // 500 + 300 awaiting valuation
        }

        [Fact]
        public void Empty_register_contributes_nothing()
        {
            var reg = new DayworkRegister();
            Assert.Equal(0, reg.PricedGrossTotal);
            Assert.Equal(0, reg.UnattachedPricedGrossTotal);
            Assert.Equal(0, reg.UnpricedNetTotal);
        }

        [Fact]
        public void IsAttached_tracks_the_variation_link()
        {
            Assert.False(Priced(10).IsAttached);
            Assert.False(Priced(10, "   ").IsAttached);   // whitespace is not a link
            Assert.True(Priced(10, "VO-001").IsAttached);
        }

        [Fact]
        public void ById_finds_a_sheet_and_tolerates_misses()
        {
            var a = Priced(10);
            var reg = new DayworkRegister { Records = { a, Priced(20) } };
            Assert.Same(a, reg.ById(a.Id));
            Assert.Null(reg.ById("nope"));
            Assert.Null(reg.ById(null!));
        }
    }

    // The seam PM-3 exists to fill: a priced sheet valued INTO a variation.
    public class DayworkVariationValuationTests
    {
        [Fact]
        public void Daywork_rated_variation_item_carries_the_sheet_gross_and_links_back()
        {
            var sheet = new DayworkRecord
            {
                Description = "Break out obstruction",
                InstructionRef = "CI-014",
                Status = DayworkStatus.Priced,
                LabourLines = { new StarRateLine { Resource = "Labourer", Hours = 10, UnitRate = 5_000, Unit = "hr" } },
                BuildUp = new DayworkBuildUp { LabourAdditionPct = 115, MaterialsAdditionPct = 0, PlantAdditionPct = 0 }
            };
            Assert.Equal(107_500, sheet.GrossTotal);   // 50,000 + 115%

            var item = new VariationItem
            {
                Description = "Dayworks — " + sheet.Description,
                Unit = "sheet",
                Quantity = 1,
                UnitRate = sheet.GrossTotal,
                RateSource = "Daywork",
                DayworkId = sheet.Id
            };

            Assert.Equal(sheet.GrossTotal, item.TotalValue);
            Assert.Equal("Daywork", item.RateSource);
            Assert.Equal(sheet.Id, item.DayworkId);
            // DayworkId is a sibling of StarRateId, not a reuse of it.
            Assert.Equal("", item.StarRateId);
        }

        [Fact]
        public void Attaching_moves_value_from_the_standalone_total_into_the_variation()
        {
            var sheet = new DayworkRecord
            {
                Status = DayworkStatus.Priced,
                LabourLines = { new StarRateLine { Resource = "L", Hours = 1, UnitRate = 1_000, Unit = "hr" } },
                BuildUp = new DayworkBuildUp { LabourAdditionPct = 0, MaterialsAdditionPct = 0, PlantAdditionPct = 0 }
            };
            var reg = new DayworkRegister { Records = { sheet } };
            Assert.Equal(1_000, reg.UnattachedPricedGrossTotal);

            var vo = new VariationInstruction { Number = "VO-009" };
            vo.Items.Add(new VariationItem
            {
                Description = "Dayworks",
                Quantity = 1,
                UnitRate = sheet.GrossTotal,
                RateSource = "Daywork",
                DayworkId = sheet.Id
            });
            sheet.VariationNumber = vo.Number;

            // The value is now in the VO and out of the standalone total — the
            // waterfall adds agreed VOs plus unattached dayworks, so it lands once.
            Assert.Equal(1_000, vo.TotalValue);
            Assert.Equal(0, reg.UnattachedPricedGrossTotal);
            Assert.Equal(1_000, reg.PricedGrossTotal);
        }
    }
}
