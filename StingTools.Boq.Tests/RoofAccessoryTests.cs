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
    /// MATSCHED-T5 — roof edge accessories.
    ///
    /// Of the three accessories a roof edge carries, exactly ONE has a length
    /// the footprint states outright:
    ///
    ///   * FASCIA runs along the eaves. An eave is horizontal, so the boundary's
    ///     plan length IS its length. Measured.
    ///   * BARGE BOARD runs up the RAKE of a gable, longer than the gable's plan
    ///     length by 1/cos(pitch). NOT measured.
    ///   * RIDGE is an internal line the footprint does not carry at all. NOT
    ///     measured.
    ///
    /// The engine can therefore only ever emit one row, and these tests exist
    /// as much to pin what it must NOT emit.
    /// </summary>
    public class RoofAccessoryTakeoffTests
    {
        private static RoofEdgeInput Roof(double eaves) =>
            new RoofEdgeInput { EavesLengthM = eaves, RoofLabel = "IT4 Sheet Roof" };

        [Fact]
        public void An_Eaves_Length_Becomes_A_Fascia_Run()
        {
            var lines = CompoundTakeoff.RoofAccessories(Roof(48.5));

            Assert.Equal(48.5, lines.Single(l => l.Kind == "fascia_board").Quantity, 4);
            Assert.Equal("m", lines.Single(l => l.Kind == "fascia_board").Unit);
        }

        [Fact]
        public void No_Eaves_Length_Emits_Nothing()
        {
            // The boundary said nothing, so neither does the bill. Not a
            // zero-quantity row, which would read as a measurement of nothing.
            Assert.Empty(CompoundTakeoff.RoofAccessories(Roof(0)));
            Assert.Empty(CompoundTakeoff.RoofAccessories(Roof(-12)));
        }

        [Fact]
        public void No_Ridge_Cap_Is_Ever_Emitted()
        {
            // A ridge is an internal geometry line the footprint does not carry.
            // No boundary data could give it, and deriving it from the roof AREA
            // is exactly the guess this feature refuses.
            Assert.DoesNotContain(CompoundTakeoff.RoofAccessories(Roof(100)),
                                  l => l.Kind.IndexOf("ridge", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void No_Barge_Board_Is_Ever_Emitted()
        {
            // The footprint carries the gable's PLAN length; a barge board runs
            // up the rake, which is longer by 1/cos(pitch). Emitting the plan
            // length would under-order on every pitched gable.
            Assert.DoesNotContain(CompoundTakeoff.RoofAccessories(Roof(100)),
                                  l => l.Kind.IndexOf("barge", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void The_Fascia_Is_The_Only_Row_This_Engine_Produces()
        {
            // Stated as a count, so adding a second accessory here has to be a
            // deliberate act that updates this test and its justification.
            Assert.Single(CompoundTakeoff.RoofAccessories(Roof(100)));
        }

        [Fact]
        public void Quantities_Are_Net_Of_Wastage()
        {
            // Wastage lives in exactly ONE place: the supplier-unit rule, which
            // carries 10% for cutting and jointing.
            Assert.Equal(100, CompoundTakeoff.RoofAccessories(Roof(100))
                                             .Single(l => l.Kind == "fascia_board").Quantity);
        }

        [Fact]
        public void An_Unnamed_Roof_Still_Produces_A_Readable_Row()
        {
            var r = Roof(20);
            r.RoofLabel = null;

            Assert.Contains("eaves", CompoundTakeoff.RoofAccessories(r).Single().Description,
                            StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// MATSCHED-T5 — the scan. This tally reports MORE about what was not
    /// measured than about what was, and every one of those sentences is the
    /// deliverable: a reader looking for a ridge cap must find out why there
    /// isn't one, not just fail to find it.
    /// </summary>
    public class RoofAccessoryTallyTests
    {
        private static RoofAccessoryTally Measured() => new RoofAccessoryTally
        {
            RoofsInspected = 3, RoofsWithFootprint = 3, RoofsWithEaves = 3,
            EavesLengthM = 48.5, VergePlanLengthM = 24.0
        };

        [Fact]
        public void Nothing_Inspected_Reports_Nothing()
            => Assert.Null(new RoofAccessoryTally().Summary());

        [Fact]
        public void A_Successful_Scan_Reports_Its_Denominator_And_The_Length()
        {
            string s = Measured().Summary();

            Assert.Contains("3", s);
            Assert.Contains("48.5", s);
        }

        [Fact]
        public void The_Scan_Always_Says_Ridge_And_Barge_Are_Not_Measured()
        {
            // Said whether or not anything matched. These are the two things a
            // reader will look for and not find, and silence there is the exact
            // failure the four layer scans were written to end.
            foreach (var t in new[] { Measured(), new RoofAccessoryTally { RoofsInspected = 1 } })
            {
                string s = t.Summary();
                Assert.Contains("RIDGE", s, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("BARGE", s, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("NOT MEASURED", s, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void The_Verge_Length_Is_Labelled_A_Plan_Length_And_Not_The_Answer()
        {
            // Reporting it is more useful than silence, but a reader must not be
            // able to lift it straight into a bill: the rake is longer.
            string s = Measured().Summary();

            Assert.Contains("24", s);
            Assert.Contains("PLAN", s, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("NOT the", s, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("rake", s, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void No_Gable_Edges_Says_A_Barge_Board_May_Not_Apply()
        {
            // A hip roof has no gable, so there is nothing missing. That is a
            // different message from "we could not measure it".
            var t = Measured();
            t.VergePlanLengthM = 0;

            string s = t.Summary();

            Assert.Contains("No gable edges", s, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("rake", s, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void The_Scan_Refuses_Area_Derivation_Explicitly()
        {
            // Stated in the export, not only in a code comment, because the
            // reader who wants a ridge length is the person most likely to
            // reach for the area.
            string s = Measured().Summary();

            Assert.Contains("AREA", s, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("guess", s, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void The_Scan_Says_Rafters_And_Purlins_Are_A_Scope_Boundary()
        {
            // So their absence is not read as an omission. They decompose
            // already when modelled as Structural Framing.
            string s = Measured().Summary();

            Assert.Contains("rafters", s, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("purlins", s, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Structural Framing", s);
        }

        [Fact]
        public void A_Roof_With_No_Footprint_Is_Reported_On_Its_Own()
        {
            // An extrusion roof or a roof by face carries no boundary sketch.
            // That is a fact about the MODEL and has a different fix from a
            // concrete roof or a missing material.
            var t = new RoofAccessoryTally { RoofsInspected = 4, RoofsWithoutFootprint = 4 };

            string s = t.Summary();

            Assert.Contains("not footprint roofs", s, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("4", s);
        }

        [Fact]
        public void A_Concrete_Roof_Is_Reported_As_Skipped_Not_As_A_Failure()
        {
            var t = new RoofAccessoryTally { RoofsInspected = 2, ConcreteRoofsSkipped = 2 };

            Assert.Contains("concrete", t.Summary(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void An_Opening_In_The_Footprint_Is_Reported_As_Not_Measured()
        {
            // Only the outer loop is measured — an opening's edge is not an eave.
            var t = Measured();
            t.RoofsWithInnerLoops = 1;

            string s = t.Summary();

            Assert.Contains("OUTER", s, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("opening", s, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Reset_Clears_Every_Counter()
        {
            var t = Measured();
            t.RoofsWithoutFootprint = 1;
            t.ConcreteRoofsSkipped = 1;
            t.RoofsWithInnerLoops = 1;

            t.Reset();

            Assert.Null(t.Summary());
        }
    }

    /// <summary>
    /// MATSCHED-T5 — the seam between the fascia code and the three shipped
    /// data files. Each is valid alone; only a comparison catches a commodity
    /// that measures in one unit and buys in another.
    /// </summary>
    public class RoofAccessoryShippedDataTests
    {
        private static string DataFile(string name) =>
            Path.Combine(AppContext.BaseDirectory, "Data", name);

        private static SupplierUnitTable Units() =>
            JsonConvert.DeserializeObject<SupplierUnitTable>(File.ReadAllText(DataFile("STING_SUPPLIER_UNITS.json")));

        private static StageLibrary Stages() =>
            JsonConvert.DeserializeObject<StageLibrary>(File.ReadAllText(DataFile("STING_MATERIAL_STAGES.json")));

        [Fact]
        public void Fascia_Buys_By_The_Unit_It_Is_Measured_In()
        {
            // The unit guard REFUSES to convert on a mismatch, so a rule
            // declaring anything but "m" would print bare metres instead of a
            // length count.
            Assert.Equal("m", Units().ResolveByKind("fascia_board").SourceUnit);
        }

        [Fact]
        public void Fascia_Is_Bought_By_The_Whole_Length()
        {
            var rule = Units().ResolveByKind("fascia_board");

            Assert.Equal("Lengths", rule.SupplierUnit);
            Assert.True(rule.SourceUnitsPerSupplierUnit > 1,
                        "a fascia sold per metre would make the Lengths label a lie");
            Assert.True(rule.RoundUpToWhole, "you cannot buy 3.4 lengths of fascia");
        }

        [Fact]
        public void Fascia_Carries_A_Cutting_Allowance_In_The_One_Place_Wastage_Lives()
        {
            // The engine emits NET, so if the allowance is not here it exists
            // nowhere and the order under-buys on every job.
            Assert.True(Units().ResolveByKind("fascia_board").DefaultWastagePct >= 5,
                        "a fascia with no cutting allowance under-orders on every job");
        }

        [Fact]
        public void Fascia_Has_A_Baseline_Rate()
        {
            var rates = CommodityRateResolver.ParseCsv(
                File.ReadAllLines(DataFile("STING_COMMODITY_RATES.csv")), out var skipped);
            Assert.Empty(skipped);

            var rule = Units().ResolveByKind("fascia_board");
            Assert.True(new CommodityRateResolver(rates, null).Resolve(rule.CommodityKey).RateUGX > 0,
                        "fascia-board is measurable but has no baseline rate");
        }

        [Fact]
        public void Fascia_Routes_To_The_Roof_By_KIND()
        {
            // Resolved against a category that routes SOMEWHERE ELSE and with a
            // blank description, so neither the category nor the roof stage's
            // type patterns (which include "fascia") can carry the assertion.
            // Both of those masked a missing kind route in earlier tasks.
            var lib = Stages();
            var ix = StageIndex.Build(lib.Stages, lib.DefaultStageId);

            Assert.Equal("roof", ix.Resolve("fascia_board", "Walls", ""));
        }

        [Fact]
        public void The_Category_And_Type_Pattern_Routes_Would_Not_Have_Carried_It()
        {
            // Pins the premise above: "Walls" routes elsewhere, and the roof
            // stage's "fascia" typePattern would have matched a description.
            var lib = Stages();
            var ix = StageIndex.Build(lib.Stages, lib.DefaultStageId);

            Assert.Equal("superstructure", ix.Resolve("", "Walls", ""));
            Assert.Equal("roof", ix.Resolve("", "Walls", "", "Fascia board along eaves"));
        }

        [Fact]
        public void Fascia_Is_Not_An_Intermediate_Measure()
        {
            // A length of fascia IS what you buy. Declaring it a memorandum
            // would hard-zero a real cost.
            Assert.DoesNotContain(Stages().IntermediateMeasures,
                r => string.Equals(r.Kind, "fascia_board", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void No_Ridge_Or_Barge_Commodity_Exists_To_Be_Filled_In_Later()
        {
            // The structural half of "not measured". If a rule existed, some
            // future row could quietly convert into it — and a ridge length has
            // no honest source. This fails the day one appears, which is the
            // right moment to argue about it.
            var units = Units();

            Assert.Null(units.ResolveByKind("ridge_cap"));
            Assert.Null(units.ResolveByKind("barge_board"));
            Assert.All(units.Rules, r =>
                Assert.False(r.CommodityKey.IndexOf("ridge", StringComparison.OrdinalIgnoreCase) >= 0
                          || r.CommodityKey.IndexOf("barge", StringComparison.OrdinalIgnoreCase) >= 0,
                            "a ridge/barge commodity exists but no honest length can feed it"));
        }

        [Fact]
        public void End_To_End_A_Fascia_Run_Reaches_The_Page_As_Priced_Lengths()
        {
            // The whole chain through the REAL aggregator: eaves length → stage
            // routing → unit guard → conversion → rate. The description is
            // blanked so only the KIND can route it.
            var stages = Stages();
            var rates = CommodityRateResolver.ParseCsv(
                File.ReadAllLines(DataFile("STING_COMMODITY_RATES.csv")), out _);

            var rows = CompoundTakeoff.RoofAccessories(new RoofEdgeInput { EavesLengthM = 48.5 })
                .Select(l => new ConstituentInput
                {
                    ConstituentKind = l.Kind, Unit = l.Unit, Quantity = l.Quantity,
                    Description = "", Category = "", TypeName = ""
                }).ToList();

            var doc = CommodityAggregator.Build(new AggregatorInputs
            {
                Constituents = rows,
                Units = Units(),
                StageDefs = stages.Stages,
                DefaultStageId = "external",   // so the kind route is observable
                IntermediateMeasures = stages.IntermediateMeasures,
                Rates = new CommodityRateResolver(rates, null),
            });

            var section = doc.Stages.Single(s => s.Commodities.Any(c => c.SourceKind == "fascia_board"));
            var c = section.Commodities.Single(x => x.SourceKind == "fascia_board");

            Assert.Equal("roof", section.StageId);
            Assert.False(c.ConversionBlocked, c.ConversionNote);
            Assert.Equal("Lengths", c.SupplierUnit);
            Assert.Equal(Math.Ceiling(c.OrderQuantity), c.OrderQuantity);
            Assert.True(c.RateUGX > 0, "fascia reached the page unpriced");
            Assert.False(c.IsMemorandum);
        }
    }
}
