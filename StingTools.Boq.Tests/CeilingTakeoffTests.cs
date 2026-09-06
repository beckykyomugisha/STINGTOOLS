using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.BOQ.Takeoff;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MATSCHED-T2 — ceilings.
    ///
    /// `CeilingType` appeared in NO branch of the take-off, so a suspended
    /// gypsum ceiling decomposed into nothing at all — no boards, no furring,
    /// no skim — and did it silently, which is indistinguishable from a model
    /// that has no ceilings.
    ///
    /// These pin the engine half. The layer READ is Revit-side and CANNOT run
    /// here — see the class note on CeilingShippedDataTests.
    /// </summary>
    public class CeilingTakeoffTests
    {
        private static CeilingInput Boarded(double area) => new CeilingInput
        {
            AreaM2 = area,
            BoardLabel = "Gypsum Wall Board",
            FurringMPerM2 = 2.7
        };

        private static CeilingInput Skimmed(double area) => new CeilingInput
        {
            AreaM2 = area,
            PlasterLabel = "Cement Plaster",
            PlasterThicknessM = 0.013,
            PlasterCementBagsPerM3 = 9,
            PlasterSandRatio = 1.25
        };

        [Fact]
        public void A_Boarded_Ceiling_Emits_Board_And_Furring()
        {
            var lines = CompoundTakeoff.Ceiling(Boarded(100));

            Assert.Equal(100, lines.Single(l => l.Kind == "ceiling_board").Quantity);
            Assert.Equal(270, lines.Single(l => l.Kind == "ceiling_furring").Quantity);
        }

        [Fact]
        public void Board_Is_Measured_In_Square_Metres_And_Furring_In_Metres()
        {
            // The unit guard REFUSES to convert on a mismatch, so emitting
            // furring in m2 would silently stop the conversion and print bare
            // square metres where a length count belongs.
            var lines = CompoundTakeoff.Ceiling(Boarded(100));

            Assert.Equal("m2", lines.Single(l => l.Kind == "ceiling_board").Unit);
            Assert.Equal("m", lines.Single(l => l.Kind == "ceiling_furring").Unit);
        }

        [Fact]
        public void Furring_Rides_On_The_Board_Not_On_The_Ceiling()
        {
            // A skim coat on a concrete soffit has no grid. Emitting furring for
            // it would invent a frame the model never described — the same
            // family of guess as pricing a whole wall as paint.
            var lines = CompoundTakeoff.Ceiling(Skimmed(100));

            Assert.DoesNotContain(lines, l => l.Kind == "ceiling_furring");
        }

        [Fact]
        public void No_Furring_Ratio_Emits_Board_But_No_Furring()
        {
            // A missing driver emits NOTHING, never a default quantity.
            var c = Boarded(100);
            c.FurringMPerM2 = 0;

            var lines = CompoundTakeoff.Ceiling(c);

            Assert.Single(lines);
            Assert.Equal("ceiling_board", lines[0].Kind);
        }

        [Fact]
        public void The_Furring_Row_Says_On_Its_Face_That_It_Is_Derived()
        {
            // A ratio must never be presented as a measurement. The banner on
            // the document is one half; the row carrying its own qualification
            // is the other, because a row outlives the dialog it was explained in.
            string d = CompoundTakeoff.Ceiling(Boarded(10))
                                      .Single(l => l.Kind == "ceiling_furring").Description;

            Assert.Contains("derived", d, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not measured", d, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void A_Skimmed_Ceiling_Emits_Plaster_Cement_And_Sand_Not_Board()
        {
            // 100 m2 x 13 mm = 1.3 m3 -> 11.7 bags, 1.625 m3 of sand.
            var lines = CompoundTakeoff.Ceiling(Skimmed(100));

            Assert.DoesNotContain(lines, l => l.Kind == "ceiling_board");
            Assert.Equal(100, lines.Single(l => l.Kind == "plaster").Quantity);
            Assert.Equal(11.7, lines.Single(l => l.Kind == "plaster_cement").Quantity, 3);
            Assert.Equal(1.625, lines.Single(l => l.Kind == "plaster_sand").Quantity, 3);
        }

        [Fact]
        public void A_Skim_With_No_Stated_Thickness_Emits_The_Area_But_No_Cement()
        {
            // The volume driver is missing. A default coat thickness would be an
            // invention; the area is still a real measured quantity, and the
            // conditional intermediate marking leaves it priceable.
            var c = Skimmed(100);
            c.PlasterThicknessM = 0;

            var lines = CompoundTakeoff.Ceiling(c);

            Assert.Single(lines);
            Assert.Equal("plaster", lines[0].Kind);
        }

        [Fact]
        public void A_Boarded_And_Skimmed_Ceiling_Emits_Both()
        {
            // Plasterboard skimmed after fixing genuinely carries both, and the
            // model states both. Collapsing them into one classification would
            // drop whichever lost.
            var c = Boarded(50);
            c.PlasterLabel = "Gypsum Skim";
            c.PlasterThicknessM = 0.003;
            c.PlasterCementBagsPerM3 = 9;
            c.PlasterSandRatio = 1.25;

            var lines = CompoundTakeoff.Ceiling(c);

            Assert.Contains(lines, l => l.Kind == "ceiling_board");
            Assert.Contains(lines, l => l.Kind == "ceiling_furring");
            Assert.Contains(lines, l => l.Kind == "plaster");
        }

        [Fact]
        public void No_Area_Means_No_Ceiling()
        {
            Assert.Empty(CompoundTakeoff.Ceiling(Boarded(0)));
            Assert.Empty(CompoundTakeoff.Ceiling(Boarded(-5)));
        }

        [Fact]
        public void A_Ceiling_Naming_Nothing_Produces_Nothing()
        {
            // Not a zero-quantity row, which would read as a measurement of
            // nothing rather than as an absence of information.
            Assert.Empty(CompoundTakeoff.Ceiling(new CeilingInput { AreaM2 = 100, FurringMPerM2 = 2.7 }));
        }

        [Fact]
        public void Quantities_Are_Net_Of_Wastage()
        {
            // Wastage lives in exactly ONE place: the supplier-unit rule, which
            // carries 10% for board. Applying it here too is the #728 shape.
            Assert.Equal(100, CompoundTakeoff.Ceiling(Boarded(100)).Single(l => l.Kind == "ceiling_board").Quantity);
        }

        [Fact]
        public void No_Paint_Is_Invented_For_A_Ceiling()
        {
            // Nothing in the model states whether a ceiling is painted, and
            // inferring it from the fact that a ceiling exists is the guess that
            // priced whole walls as paint in the rules withdrawn by #710.
            var c = Boarded(100);
            c.PlasterLabel = "Cement Plaster";
            c.PlasterThicknessM = 0.013;

            Assert.DoesNotContain(CompoundTakeoff.Ceiling(c), l => l.Kind.StartsWith("paint"));
        }
    }

    /// <summary>
    /// MATSCHED-T2 — the ceiling classifiers. Board and plaster are two
    /// predicates because a ceiling can carry both; they must nonetheless never
    /// accept the SAME layer, or one surface is measured twice.
    /// </summary>
    public class CeilingClassifierTests
    {
        [Theory]
        [InlineData("Gypsum Wall Board")]
        [InlineData("Plasterboard")]
        [InlineData("Plaster Board")]
        [InlineData("Gyproc")]
        [InlineData("Drywall")]
        [InlineData("Gypsum Board 12.5mm")]
        public void Sheet_Board_Reads_As_Board(string name)
            => Assert.True(FinishTextClassifier.IsCeilingBoard(name), name);

        [Theory]
        [InlineData("Mineral Fibre Ceiling Tile")]
        [InlineData("Acoustic Ceiling Tile")]
        [InlineData("PVC Ceiling")]
        [InlineData("Timber T&G Ceiling")]
        [InlineData("Aluminium Ceiling Panel")]
        public void Things_Not_Bought_By_The_Sheet_Are_Not_Board(string name)
        {
            // Each is a real product bought by the tile or the length. Converting
            // it at 2.88 m2 a sheet would be wrong in both the count and the rate,
            // so it is rejected and NAMED rather than absorbed.
            //
            // NOTE what this proves and what it does NOT. None of these names
            // carries a board word, so they are turned away by the NARROWNESS of
            // the positive pattern — the exclusion list never sees them. That is
            // why the test below exists: this one alone passed unchanged with the
            // exclusion list deleted, which made it a gate that could not fail.
            Assert.False(FinishTextClassifier.IsCeilingBoard(name), name);
        }

        [Theory]
        [InlineData("PVC Wall Board")]
        [InlineData("Timber Wall Board")]
        public void A_Board_Word_Does_Not_Override_A_Non_Sheet_Material(string name)
        {
            // These DO match the positive board pattern, so only the exclusion
            // list can turn them away. This is the case that makes the exclusion
            // load-bearing rather than decorative.
            Assert.False(FinishTextClassifier.IsCeilingBoard(name), name);
        }

        [Fact]
        public void An_Adjective_Does_Not_Disqualify_A_Real_Sheet()
        {
            // The first exclusion draft carried bare "acoustic", which would have
            // rejected acoustic plasterboard — a genuine 1200x2400 sheet.
            // Over-exclusion is not the safe direction: it drops a real material
            // silently, which is the omission this whole task exists to fix.
            Assert.True(FinishTextClassifier.IsCeilingBoard("Acoustic Plasterboard"));
            Assert.True(FinishTextClassifier.IsCeilingBoard("Moisture Resistant Plasterboard"));
        }

        [Theory]
        [InlineData("Cement Plaster")]
        [InlineData("Gypsum Skim")]
        [InlineData("Skim Coat")]
        [InlineData("Render")]
        public void Wet_Coats_Read_As_Ceiling_Plaster(string name)
            => Assert.True(FinishTextClassifier.IsCeilingPlaster(name), name);

        [Theory]
        [InlineData("Gypsum Skim")]
        [InlineData("Gypsum Plaster")]
        public void Gypsum_Alone_Is_The_Material_Not_The_Product(string name)
        {
            // Regression. The first draft of the board pattern matched bare
            // "gypsum", so a wet gypsum skim was classified as sheets of
            // plasterboard — wrong in the count, the rate and the trade. A board
            // word is now required. This test found that defect before it shipped.
            Assert.False(FinishTextClassifier.IsCeilingBoard(name), name);
            Assert.True(FinishTextClassifier.IsCeilingPlaster(name), name);
        }

        [Theory]
        [InlineData("Plasterboard")]
        [InlineData("Gypsum Plaster Board")]
        [InlineData("Plaster Board")]
        public void A_Board_Is_Never_Also_A_Wet_Coat(string name)
        {
            // "Plasterboard" contains "plaster" and is not a wet coat. Board wins
            // outright, so the two predicates can never both accept one layer —
            // enforced, not hoped for.
            Assert.True(FinishTextClassifier.IsCeilingBoard(name), name);
            Assert.False(FinishTextClassifier.IsCeilingPlaster(name), name);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("Concrete")]
        [InlineData("Ceramic Tile")]
        public void Everything_Else_Is_Neither(string name)
        {
            Assert.False(FinishTextClassifier.IsCeilingBoard(name));
            Assert.False(FinishTextClassifier.IsCeilingPlaster(name));
        }
    }

    /// <summary>
    /// MATSCHED-T2 — the scan diagnostic. Silence is a bug: a ceiling that
    /// produced nothing must say whether the model has no ceilings, whether the
    /// type carries no build-up, whether the names went unrecognised, or whether
    /// the instance had no area. Four causes, one identical-looking output.
    /// </summary>
    public class CeilingScanTallyTests
    {
        [Fact]
        public void Nothing_Seen_Reports_Nothing()
            => Assert.Null(new CeilingScanTally().Summary());

        [Fact]
        public void A_Successful_Scan_Reports_Its_Denominator()
        {
            var t = new CeilingScanTally
            { TypesInspected = 6, TypesWithFinishLayer = 5, TypesWithBoard = 3, TypesWithPlaster = 2 };

            string s = t.Summary();

            Assert.Contains("6", s);
            Assert.Contains("3", s);
            Assert.Contains("2", s);
        }

        [Fact]
        public void A_Ceiling_Type_With_No_Compound_Structure_Is_Reported_On_Its_Own()
        {
            // This is the likeliest cause of an empty result and it is a fact
            // about the MODEL. Counting it under "inspected" would hide it.
            var t = new CeilingScanTally { TypesWithoutCompoundStructure = 4 };

            string s = t.Summary();

            Assert.NotNull(s);
            Assert.Contains("no compound structure", s, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("4", s);
        }

        [Fact]
        public void An_Instance_With_No_Area_Is_Reported_Separately_From_A_Naming_Problem()
        {
            var t = new CeilingScanTally
            { TypesInspected = 2, TypesWithFinishLayer = 2, TypesWithBoard = 2, InstancesWithNoArea = 3 };

            Assert.Contains("no computed area", t.Summary(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Rejected_Ceiling_Materials_Are_Named()
        {
            var t = new CeilingScanTally { TypesInspected = 2, TypesWithFinishLayer = 2 };
            t.RejectedMaterials.Add("Mineral Fibre Ceiling Tile");
            t.RejectedMaterials.Add("PVC Ceiling");

            string s = t.Summary();

            Assert.Contains("Mineral Fibre Ceiling Tile", s);
            Assert.Contains("PVC Ceiling", s);
        }

        [Fact]
        public void Rejections_Are_Still_Named_When_Something_Else_Matched()
        {
            // A model with one boarded type and one PVC type must report BOTH.
            // Reporting the rejection only when nothing matched is how a real
            // omission hides behind a partial success.
            var t = new CeilingScanTally
            { TypesInspected = 2, TypesWithFinishLayer = 2, TypesWithBoard = 1 };
            t.RejectedMaterials.Add("PVC Ceiling");

            Assert.Contains("PVC Ceiling", t.Summary());
        }

        [Fact]
        public void No_Finish_Layers_Says_So()
        {
            var t = new CeilingScanTally { TypesInspected = 3, TypesWithFinishLayer = 0 };

            Assert.Contains("no finish layer", t.Summary(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void The_Furring_Banner_Only_Appears_When_Furring_Was_Derived()
        {
            // A banner qualifying a row that was never emitted is noise, and
            // noise is how real banners come to be ignored.
            Assert.Null(new CeilingScanTally().FurringBanner());
            Assert.NotNull(new CeilingScanTally { FurringDerived = true }.FurringBanner());
        }

        [Fact]
        public void The_Furring_Banner_Says_It_Is_Not_A_Measurement()
        {
            string b = new CeilingScanTally { FurringDerived = true }.FurringBanner();

            Assert.Contains("not", b, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("measurement", b, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("FURRING_M_PER_M2", b);   // names the row to edit
        }

        [Fact]
        public void Reset_Clears_Every_Counter()
        {
            var t = new CeilingScanTally
            {
                TypesInspected = 1, TypesWithoutCompoundStructure = 1, TypesWithFinishLayer = 1,
                TypesWithBoard = 1, TypesWithPlaster = 1, InstancesWithNoArea = 1, FurringDerived = true
            };
            t.RejectedMaterials.Add("x");

            t.Reset();

            Assert.Null(t.Summary());
            Assert.Null(t.FurringBanner());
            Assert.Empty(t.RejectedMaterials);
        }
    }

    /// <summary>
    /// MATSCHED-T2 — the seam between the ceiling code and the four shipped data
    /// files it depends on. Each is valid on its own; only a comparison catches
    /// a key the builder composes one way and the CSV spells another, which
    /// fails at runtime as a zero and drops rows without a word.
    /// </summary>
    public class CeilingShippedDataTests
    {
        private static string DataFile(string name) =>
            Path.Combine(AppContext.BaseDirectory, "Data", name);

        private static Dictionary<string, StingTools.UI.MaterialLookupRow> Lookup() =>
            StingTools.UI.MaterialLookupParser.Parse(File.ReadAllLines(DataFile("MATERIAL_LOOKUP.csv")));

        private static StageLibrary Stages() =>
            JsonConvert.DeserializeObject<StageLibrary>(File.ReadAllText(DataFile("STING_MATERIAL_STAGES.json")));

        private static SupplierUnitTable Units() =>
            JsonConvert.DeserializeObject<SupplierUnitTable>(File.ReadAllText(DataFile("STING_SUPPLIER_UNITS.json")));

        [Fact]
        public void The_Furring_Ratio_The_Builder_Asks_For_Resolves_To_A_Real_Figure()
        {
            // The builder asks for exactly "CEILING DEFAULT" / FURRING_M_PER_M2.
            // A typo is not a compile error and not a load error; it is a zero,
            // and a zero silently drops the furring row.
            double v = Lookup()["CEILING DEFAULT"].Properties["FURRING_M_PER_M2"];

            Assert.True(v > 0, "CEILING DEFAULT has no furring ratio");
            // 1.0-6.0 m/m2 spans a sparse 1200 grid to a dense 600x600 one.
            // Outside it is a data-entry slip, not a grid.
            Assert.InRange(v, 1.0, 6.0);
        }

        [Fact]
        public void The_Ceiling_Plaster_Mix_The_Builder_Asks_For_Resolves()
        {
            // The ceiling wet-coat reuses the PLASTER rows rather than minting
            // ceiling-only twins. If those keys move, this fails here instead of
            // going runtime-dead in Revit.
            var row = Lookup()["PLASTER STANDARD"];

            Assert.True(row.Properties["MIX_CEMENT_BAGS_PER_M3"] > 0);
            Assert.True(row.Properties["MIX_SAND_RATIO"] > 0);
        }

        [Fact]
        public void Both_Ceiling_Kinds_Route_To_Finishes()
        {
            var lib = Stages();
            var ix = StageIndex.Build(lib.Stages, lib.DefaultStageId);

            foreach (string kind in new[] { "ceiling_board", "ceiling_furring" })
            {
                // The real path.
                Assert.Equal("finishes", ix.Resolve(kind, "Ceilings", ""));

                // And the one that makes the assertion mean something. The
                // category "Ceilings" ALREADY routes to finishes, so the first
                // line passes whether or not the kind is declared — deleting
                // both kinds from the stage library left it green. Resolving
                // against a category that routes to SUPERSTRUCTURE is what
                // proves the kind route exists and that kind beats category.
                Assert.Equal("finishes", ix.Resolve(kind, "Walls", ""));
            }
        }

        [Fact]
        public void The_Category_Route_Alone_Would_Not_Have_Been_Enough()
        {
            // Pins the premise of the test above: "Walls" really does route
            // somewhere else, so a kind that failed to route would land there.
            var lib = Stages();
            var ix = StageIndex.Build(lib.Stages, lib.DefaultStageId);

            Assert.Equal("superstructure", ix.Resolve("", "Walls", ""));
        }

        [Fact]
        public void Ceiling_Commodities_Buy_By_The_Unit_They_Are_Measured_In()
        {
            // The unit guard REFUSES to convert on a mismatch, so a rule whose
            // sourceUnit disagreed would silently stop converting and print the
            // raw measured figure instead of a sheet or length count.
            var units = Units();

            Assert.Equal("m2", units.ResolveByKind("ceiling_board").SourceUnit);
            Assert.Equal("m", units.ResolveByKind("ceiling_furring").SourceUnit);
        }

        [Fact]
        public void Board_Is_Bought_By_The_Whole_Sheet_At_A_Real_Sheet_Size()
        {
            var rule = Units().ResolveByKind("ceiling_board");

            Assert.Equal(2.88, rule.SourceUnitsPerSupplierUnit, 3);   // 1200 x 2400
            Assert.True(rule.RoundUpToWhole, "you cannot buy 3.4 sheets of board");
        }

        [Fact]
        public void Furring_Is_Bought_By_The_Whole_Length()
        {
            var rule = Units().ResolveByKind("ceiling_furring");

            Assert.Equal(3.6, rule.SourceUnitsPerSupplierUnit, 3);
            Assert.True(rule.RoundUpToWhole, "you cannot buy 4.2 lengths of grid");
        }

        [Fact]
        public void The_Furring_Commodity_Declares_On_Its_Own_Row_That_It_Is_Derived()
        {
            // The rule's description is what the workbook prints — it overrides
            // the row description in the aggregator. If the qualification is not
            // here, it does not reach the page.
            string d = Units().ResolveByKind("ceiling_furring").Description;

            Assert.Contains("DERIVED", d, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not measured", d, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Both_Ceiling_Commodities_Have_A_Baseline_Rate()
        {
            var rates = CommodityRateResolver.ParseCsv(
                File.ReadAllLines(DataFile("STING_COMMODITY_RATES.csv")), out var skipped);
            Assert.Empty(skipped);

            var resolver = new CommodityRateResolver(rates, null);
            var units = Units();

            foreach (string kind in new[] { "ceiling_board", "ceiling_furring" })
            {
                var rule = units.ResolveByKind(kind);
                Assert.True(rule != null, "no supplier-unit rule matches kind " + kind);
                Assert.True(resolver.Resolve(rule.CommodityKey).RateUGX > 0,
                            "commodity " + rule.CommodityKey + " has no baseline rate");
            }
        }

        [Fact]
        public void Ceiling_Board_Is_Not_An_Intermediate_Measure()
        {
            // Board IS what you buy — it is not a memorandum for anything below
            // it. Declaring it as one would hard-zero a real cost.
            Assert.DoesNotContain(Stages().IntermediateMeasures,
                r => string.Equals(r.Kind, "ceiling_board", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(r.Kind, "ceiling_furring", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void A_Ceiling_Plaster_Row_Still_Goes_Memorandum_Behind_Its_Cement()
        {
            // The ceiling reuses the `plaster` kind, so it inherits the existing
            // intermediate declaration — that inheritance is the reason for
            // reusing the kind, and it is worth asserting rather than assuming.
            var doc = new MaterialScheduleDocument();
            doc.Stages.Add(new StageSection
            {
                StageId = "finishes",
                Commodities =
                {
                    new MaterialCommodity { CommodityKey = "Ceiling plaster — Gypsum Skim", SourceKind = "plaster" },
                    new MaterialCommodity { CommodityKey = "cement", SourceKind = "plaster_cement" },
                }
            });

            IntermediateMeasureMarker.Apply(doc, Stages().IntermediateMeasures);

            Assert.True(doc.Stages[0].Commodities[0].IsMemorandum);
            Assert.False(doc.Stages[0].Commodities[1].IsMemorandum);
        }
    }
}
