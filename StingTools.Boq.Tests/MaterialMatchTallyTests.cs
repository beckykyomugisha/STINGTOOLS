using System.Collections.Generic;
using System.Linq;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// Material matching arrived with a failure mode that looks exactly like
    /// success-minus-one-pattern: the row still reads "Generic - 225mm, 610 m²,
    /// unpriced", precisely as it did before. Three different causes produce
    /// that same output, and reading a log to tell them apart is a round trip.
    ///
    /// So the scan publishes its own denominator, the way the tiling, screed,
    /// membrane and room-finish scans already do.
    /// </summary>
    public class MaterialMatchTallyTests
    {
        private static SupplierUnitTable Units() => new SupplierUnitTable
        {
            Rules =
            {
                new SupplierUnitRule
                {
                    CommodityKey = "roof-shingle", SupplierUnit = "Bundles", SourceUnit = "m2",
                    SourceUnitsPerSupplierUnit = 3.1, RoundUpToWhole = true,
                    MatchCategories = { "Roofs" },
                    MatchMaterialPatterns = { "shingle" }, MatchTypePatterns = { "shingle" }
                },
                new SupplierUnitRule
                {
                    CommodityKey = "cement", SupplierUnit = "Bags", SourceUnit = "bag",
                    SourceUnitsPerSupplierUnit = 1, MatchKinds = { "mortar_cement" }
                }
            }
        };

        private static AggregatorInputs Inputs(params ConstituentInput[] rows) => new AggregatorInputs
        {
            Constituents = rows.ToList(),
            Units = Units(),
            StageDefs = new List<StageDefinition>
            {
                new StageDefinition { StageId = "roof", Title = "ROOF", Order = 10 }
            },
            DefaultStageId = "roof",
            Rates = new CommodityRateResolver(
                new List<CommodityRate> { new CommodityRate { CommodityKey = "roof-shingle", RateUGX = 110000 } },
                null)
        };

        private static ConstituentInput Roof(string type, string material, double m2 = 610.61) =>
            new ConstituentInput
            {
                ConstituentKind = "", Category = "Roofs", TypeName = type,
                Description = type, MaterialName = material, Unit = "m2", Quantity = m2
            };

        // ── the three outcomes that look identical on the sheet ─────────────

        [Fact]
        public void A_Material_That_Matches_Is_Counted_As_Matched()
        {
            var inputs = Inputs(Roof("Generic - 225mm", "Asphalt Shingle"));

            CommodityAggregator.Build(inputs);

            var s = inputs.MaterialScan;
            Assert.Equal(1, s.RowsInspected);
            Assert.Equal(1, s.RowsWithMaterial);
            Assert.Equal(1, s.MatchedByMaterial);
            Assert.Empty(s.UnplacedMaterials);
        }

        [Fact]
        public void A_Material_That_Matches_Nothing_Is_NAMED()
        {
            // The actionable half. Each name is a pattern worth adding.
            var inputs = Inputs(Roof("Generic - 225mm", "Woven Papyrus Thatch"));

            CommodityAggregator.Build(inputs);

            Assert.True(inputs.MaterialScan.UnplacedMaterials.Contains("Woven Papyrus Thatch"));
            Assert.True(inputs.MaterialScan.UnplacedCategories.Contains("Roofs"));
            Assert.Contains("'Woven Papyrus Thatch'", inputs.MaterialScan.Summary());
        }

        [Fact]
        public void NO_Material_At_All_Is_Called_Out_As_Plumbing_Not_Naming()
        {
            // The case that reads as a broken feature and is not. The material
            // is read off the element, so an empty column means the element has
            // none — or it is not reaching the schedule.
            var inputs = Inputs(Roof("Generic - 225mm", ""));

            CommodityAggregator.Build(inputs);

            string s = inputs.MaterialScan.Summary();
            Assert.Equal(0, inputs.MaterialScan.RowsWithMaterial);
            Assert.Contains("NO row carried a material name at all", s);
            Assert.Contains("not a naming problem", s);
            Assert.Contains("Check one element", s);
        }

        // ── the denominator ─────────────────────────────────────────────────

        [Fact]
        public void A_Row_With_Its_Own_Constituent_Kind_Is_Not_Inspected()
        {
            // Cement is decided by kind. Counting it would report a coverage
            // the feature was never asked for, and make the ratio meaningless.
            var inputs = Inputs(new ConstituentInput
            {
                ConstituentKind = "mortar_cement", Unit = "bag", Quantity = 10, MaterialName = "Cement"
            });

            CommodityAggregator.Build(inputs);

            Assert.Equal(0, inputs.MaterialScan.RowsInspected);
            Assert.Null(inputs.MaterialScan.Summary());
        }

        [Fact]
        public void A_Category_NO_Commodity_Rule_Names_Is_Not_Inspected()
        {
            // A second project reported "200 rows could be identified by
            // material, 3 matched" and listed 56 unplaced materials led by
            // '911 CARRERA S - BODY COLOR' - a Porsche in the entourage.
            //
            // A car's paint is never a building commodity. Counting it made a
            // working feature read as a 1.5% success rate and buried the
            // materials that DO need a pattern.
            var inputs = Inputs(new ConstituentInput
            {
                ConstituentKind = "", Category = "Furniture", TypeName = "Sofa",
                Description = "Sofa", MaterialName = "911 CARRERA S - BODY COLOR",
                Unit = "each", Quantity = 1
            });

            CommodityAggregator.Build(inputs);

            Assert.Equal(0, inputs.MaterialScan.RowsInspected);
            Assert.Empty(inputs.MaterialScan.UnplacedMaterials);
        }

        [Fact]
        public void A_Category_A_Rule_DOES_Name_Is_Still_Inspected()
        {
            // The other half: scoping must not quietly stop looking at roofs.
            var inputs = Inputs(Roof("Generic - 225mm", "Woven Papyrus Thatch"));

            CommodityAggregator.Build(inputs);

            Assert.Equal(1, inputs.MaterialScan.RowsInspected);
            Assert.True(inputs.MaterialScan.UnplacedMaterials.Contains("Woven Papyrus Thatch"));
        }

        [Fact]
        public void A_Scan_That_Inspected_Nothing_Reports_Nothing()
        {
            Assert.Null(new MaterialMatchTally().Summary());
        }

        [Fact]
        public void A_Well_Named_Model_Says_Material_Matching_Changed_Nothing()
        {
            // Matched on TYPE, not material — the expected outcome when the
            // model is named properly, and worth saying so it does not read as
            // a failure.
            var inputs = Inputs(Roof("Shingle roof 25mm", "Some Unlisted Backing"));

            CommodityAggregator.Build(inputs);

            var s = inputs.MaterialScan;
            Assert.Equal(0, s.MatchedByMaterial);
            Assert.Empty(s.UnplacedMaterials);
            Assert.Contains("material matching changed nothing here", s.Summary());
        }

        [Fact]
        public void The_Counts_Read_As_A_Ratio_Somebody_Can_Act_On()
        {
            var inputs = Inputs(
                Roof("Generic - 225mm", "Asphalt Shingle"),
                Roof("Generic - 225mm 2", "Woven Papyrus Thatch"),
                Roof("Generic - 225mm 3", ""));

            CommodityAggregator.Build(inputs);

            string s = inputs.MaterialScan.Summary();
            Assert.Contains("3 row(s) could be identified by material", s);
            Assert.Contains("2 carried a material name", s);
            Assert.Contains("1 matched a commodity by it", s);
        }

        [Fact]
        public void Reset_Clears_Everything_So_A_Second_Build_Does_Not_Accumulate()
        {
            var t = new MaterialMatchTally { RowsInspected = 5, RowsWithMaterial = 4, MatchedByMaterial = 3 };
            t.UnplacedMaterials.Add("x");
            t.UnplacedCategories.Add("Roofs");

            t.Reset();

            Assert.Equal(0, t.RowsInspected);
            Assert.Empty(t.UnplacedMaterials);
            Assert.Null(t.Summary());
        }
    }
}
