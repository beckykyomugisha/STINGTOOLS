using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MATSCHED-T4 — ratio-derived consumables.
    ///
    /// T1-T3 measured what the model STATES. Nothing here is stated anywhere:
    /// hoop iron, binding wire, formwork nails and roofing fasteners exist in
    /// the schedule only because a table says how much of each goes with work
    /// that WAS measured.
    ///
    /// A ratio presented as a measurement is worse than no number at all,
    /// because nobody checks it. These tests exist mostly to pin the two things
    /// that keep that from happening: NOTHING is emitted without a measured
    /// driver, and everything emitted says on its face that it was derived.
    /// </summary>
    public class ConsumablesCalculatorTests
    {
        private static ConsumableRule Rule(string kind, string driver, double per, string unit) =>
            new ConsumableRule
            {
                ConstituentKind = kind, Description = kind, Driver = driver,
                PerDriver = per, Unit = unit, SourceNote = "test"
            };

        private static List<ConsumableRule> AllFour() => new List<ConsumableRule>
        {
            Rule("hoop_iron", "walled_area_m2", 1.2, "m"),
            Rule("binding_wire", "rebar_kg", 0.0125, "kg"),
            Rule("formwork_nails", "formwork_m2", 0.2, "kg"),
            Rule("roof_fastener", "roof_covering_m2", 11.0, "nr"),
        };

        [Fact]
        public void Each_Consumable_Is_Its_Driver_Times_Its_Ratio()
        {
            var d = new ConsumableDrivers
            { WalledAreaM2 = 500, RebarKg = 8000, FormworkM2 = 300, RoofCoveringM2 = 250 };

            var rows = ConsumablesCalculator.Quantify(d, AllFour(), new ConsumablesTally());

            Assert.Equal(600, rows.Single(r => r.ConstituentKind == "hoop_iron").Quantity, 6);
            Assert.Equal(100, rows.Single(r => r.ConstituentKind == "binding_wire").Quantity, 6);
            Assert.Equal(60, rows.Single(r => r.ConstituentKind == "formwork_nails").Quantity, 6);
            Assert.Equal(2750, rows.Single(r => r.ConstituentKind == "roof_fastener").Quantity, 6);
        }

        [Fact]
        public void Each_Consumable_Carries_The_Unit_Its_Ratio_Is_Expressed_In()
        {
            // The aggregator REFUSES to convert when a measured unit disagrees
            // with the supplier rule's sourceUnit, so a wrong unit here does not
            // fail loudly — it prints a raw figure where an order quantity
            // belongs.
            var d = new ConsumableDrivers
            { WalledAreaM2 = 1, RebarKg = 1, FormworkM2 = 1, RoofCoveringM2 = 1 };

            var rows = ConsumablesCalculator.Quantify(d, AllFour(), new ConsumablesTally());

            Assert.Equal("m", rows.Single(r => r.ConstituentKind == "hoop_iron").Unit);
            Assert.Equal("kg", rows.Single(r => r.ConstituentKind == "binding_wire").Unit);
            Assert.Equal("kg", rows.Single(r => r.ConstituentKind == "formwork_nails").Unit);
            Assert.Equal("nr", rows.Single(r => r.ConstituentKind == "roof_fastener").Unit);
        }

        [Fact]
        public void A_Zero_Driver_Emits_NOTHING()
        {
            // THE hard requirement of this task. Not a minimum, not a fixed
            // quantity, not a zero-quantity row — a zero-quantity row reads as a
            // measurement of nothing rather than as an absence of information.
            var d = new ConsumableDrivers { WalledAreaM2 = 0, RebarKg = 0, FormworkM2 = 0, RoofCoveringM2 = 0 };

            Assert.Empty(ConsumablesCalculator.Quantify(d, AllFour(), new ConsumablesTally()));
        }

        [Fact]
        public void One_Absent_Driver_Does_Not_Suppress_The_Others()
        {
            // A job with no steel still buys hoop iron.
            var d = new ConsumableDrivers { WalledAreaM2 = 100, RebarKg = 0 };

            var rows = ConsumablesCalculator.Quantify(d, AllFour(), new ConsumablesTally());

            Assert.Single(rows);
            Assert.Equal("hoop_iron", rows[0].ConstituentKind);
        }

        [Fact]
        public void A_Negative_Driver_Emits_Nothing_Rather_Than_A_Negative_Quantity()
        {
            var d = new ConsumableDrivers { WalledAreaM2 = -50 };

            Assert.Empty(ConsumablesCalculator.Quantify(d, AllFour(), new ConsumablesTally()));
        }

        [Fact]
        public void A_Rule_Naming_An_Unknown_Driver_Never_Fires()
        {
            // Dead config is worse than absent config: it advertises coverage
            // the export does not have. Same invariant as Every_Rule_Is_Reachable.
            var rules = new List<ConsumableRule> { Rule("mystery", "concrete_m3", 1.0, "kg") };
            var tally = new ConsumablesTally();

            Assert.Empty(ConsumablesCalculator.Quantify(
                new ConsumableDrivers { WalledAreaM2 = 100 }, rules, tally));
            Assert.Contains(tally.UnusableRules, u => u.Contains("mystery"));
        }

        [Fact]
        public void A_Rule_With_No_Ratio_Or_No_Unit_Never_Fires()
        {
            var rules = new List<ConsumableRule>
            {
                Rule("no_ratio", "walled_area_m2", 0, "m"),
                Rule("no_unit", "walled_area_m2", 1.0, ""),
            };
            var tally = new ConsumablesTally();

            Assert.Empty(ConsumablesCalculator.Quantify(
                new ConsumableDrivers { WalledAreaM2 = 100 }, rules, tally));
            Assert.Equal(2, tally.UnusableRules.Count);
        }

        [Fact]
        public void An_Empty_Rule_Table_Produces_Nothing_However_Large_The_Drivers()
        {
            // The no-hardcoded-ratio guard, stated behaviourally. Every ratio
            // lives in STING_CONSUMABLES.json; the calculator knows none of its
            // own. If a default ever creeps into the C#, a project that empties
            // the table would still get quantities — and would have no way to
            // tell where they came from.
            var d = new ConsumableDrivers
            { WalledAreaM2 = 10000, RebarKg = 500000, FormworkM2 = 9000, RoofCoveringM2 = 4000 };

            Assert.Empty(ConsumablesCalculator.Quantify(d, new List<ConsumableRule>(), new ConsumablesTally()));
        }

        [Fact]
        public void Every_Row_Is_Traceable_To_The_Driver_It_Came_From()
        {
            // A derived row that looks like a measured one in the audit trail is
            // the thing this whole task is written to avoid.
            var rows = ConsumablesCalculator.Quantify(
                new ConsumableDrivers { RebarKg = 1000 }, AllFour(), new ConsumablesTally());

            Assert.Equal("derived:rebar_kg", rows.Single().TraceRef);
        }

        [Fact]
        public void A_Consumable_Never_Becomes_The_Driver_Of_Another_Consumable()
        {
            // The rows Quantify returns carry kinds no driver reads, so feeding
            // its own output back in produces nothing. Pins the ordering the
            // builder relies on: drivers are read, THEN rows are appended.
            var first = ConsumablesCalculator.Quantify(
                new ConsumableDrivers { WalledAreaM2 = 100, RebarKg = 1000, FormworkM2 = 50 },
                AllFour(), new ConsumablesTally());

            var second = ConsumableDrivers.From(first, null);

            Assert.Equal(0, second.WalledAreaM2);
            Assert.Equal(0, second.RebarKg);
            Assert.Equal(0, second.FormworkM2);
            Assert.Equal(0, second.RoofCoveringM2);
        }
    }

    /// <summary>
    /// MATSCHED-T4 — driver extraction. The drivers are summed from the SAME
    /// constituent rows the bill is built from, NET of wastage, in their
    /// measured units. Getting a unit wrong here is silent: it inflates a
    /// consumable rather than failing.
    /// </summary>
    public class ConsumableDriverTests
    {
        private static ConstituentInput Row(string kind, string unit, double qty,
                                            string category = "", string typeName = "") =>
            new ConstituentInput
            {
                ConstituentKind = kind, Unit = unit, Quantity = qty,
                Category = category, TypeName = typeName
            };

        private static SupplierUnitTable Units() =>
            JsonConvert.DeserializeObject<SupplierUnitTable>(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "STING_SUPPLIER_UNITS.json")));

        [Fact]
        public void Walling_Sums_Blockwork_And_Brickwork()
        {
            var d = ConsumableDrivers.From(new[]
            {
                Row("blockwork", "m2", 174.60),
                Row("brickwork", "m2", 389.62),
            }, null);

            Assert.Equal(564.22, d.WalledAreaM2, 4);
        }

        [Fact]
        public void Rebar_Sums_Rebar_But_Not_Mesh()
        {
            // Mesh maps to the same COMMODITY as rebar but is bought as sheets;
            // it needs no tying wire the way loose bars do.
            //
            // The mesh row here is deliberately measured in KG, not in the m2 it
            // is normally measured in. With m2 this test stayed green when `mesh`
            // was admitted into the rebar branch — the UNIT check turned it away
            // and the kind check was never exercised, so the test proved "sums kg
            // but not m2", which is a different claim.
            var d = ConsumableDrivers.From(new[]
            {
                Row("rebar", "kg", 8000),
                Row("mesh", "kg", 400),
            }, null);

            Assert.Equal(8000, d.RebarKg, 4);
        }

        [Fact]
        public void Rebar_Measured_In_The_Wrong_Unit_Is_Not_Added_To_A_Mass()
        {
            // The rebar branch's own unit check. Its first version used a `mesh`
            // row, which was BLIND: mesh never enters this branch — the kind
            // check turns it away first — so dropping the unit check changed
            // nothing. Only a row that IS rebar and is NOT in kilograms can
            // exercise it.
            var d = ConsumableDrivers.From(new[] { Row("rebar", "m2", 400) }, null);

            Assert.Equal(0, d.RebarKg);
            Assert.Contains(d.UnitMismatches, m => m.Contains("rebar"));
        }

        [Fact]
        public void Formwork_Measured_As_An_Item_Is_Not_Added_To_A_Square_Metre_Total()
        {
            // VoidSlab emits `formwork` with unit "item" and quantity 1 when the
            // pots are permanent formwork. Adding that 1 to a square-metre total
            // is the "Bricks · No. · 364.31" defect in miniature.
            var d = ConsumableDrivers.From(new[]
            {
                Row("formwork", "m2", 300),
                Row("formwork", "item", 1),
            }, null);

            Assert.Equal(300, d.FormworkM2, 4);
            Assert.Contains(d.UnitMismatches, m => m.Contains("item"));
        }

        [Fact]
        public void A_Unit_Mismatch_Is_Recorded_Not_Silently_Dropped()
        {
            var d = ConsumableDrivers.From(new[] { Row("blockwork", "nr", 2292) }, null);

            Assert.Equal(0, d.WalledAreaM2);
            Assert.NotEmpty(d.UnitMismatches);
        }

        [Fact]
        public void Roof_Covering_Is_Resolved_Through_The_Same_Table_The_Aggregator_Uses()
        {
            // The roof covering carries NO constituent kind — the supplier table
            // matches it by category plus type pattern. Re-deriving that match
            // with a second copy of the rules is how two files come to disagree
            // without anyone comparing them.
            var d = ConsumableDrivers.From(new[]
            {
                Row("", "m2", 856, "Roofs", "IT4 Corrugated Sheet G28"),
            }, Units());

            Assert.Equal(856, d.RoofCoveringM2, 4);
        }

        [Fact]
        public void A_Roof_Area_That_Matches_No_Covering_Rule_Is_Not_Counted_As_One()
        {
            // THE load-bearing case. This row is on a roof AND measured in m²,
            // so neither the category nor the unit can turn it away — only the
            // supplier table's type patterns can, and that is exactly the
            // decision being delegated to them.
            //
            // Written after the concrete-slab test below was found BLIND: with a
            // m³ row, replacing the commodity-key check with a bare
            // `Category == "Roofs"` left it green, because the unit check caught
            // it first.
            var units = Units();

            var d = ConsumableDrivers.From(new[]
            {
                Row("", "m2", 400, "Roofs", "Green Roof Substrate Buildup"),
            }, units);

            Assert.Equal(0, d.RoofCoveringM2);
        }

        [Fact]
        public void A_Concrete_Roof_Slab_Is_Not_Counted_As_Covering()
        {
            // A concrete roof slab decomposes into concrete/rebar/formwork and
            // matches no covering rule. Counting it would order roofing screws
            // for a slab. NOTE: it is the m³ unit that turns this one away, not
            // the covering check — see the test above for that.
            var d = ConsumableDrivers.From(new[]
            {
                Row("concrete", "m3", 137, "Roofs", "STING RC Roof Slab 225"),
            }, Units());

            Assert.Equal(0, d.RoofCoveringM2);
        }

        [Fact]
        public void No_Rows_Means_No_Drivers()
        {
            var d = ConsumableDrivers.From(new ConstituentInput[0], null);

            Assert.Equal(0, d.WalledAreaM2);
            Assert.Equal(0, d.RebarKg);
            Assert.Equal(0, d.FormworkM2);
            Assert.Equal(0, d.RoofCoveringM2);
        }
    }

    /// <summary>
    /// MATSCHED-T4 — the diagnostic and the honesty banner. The banner is the
    /// deliverable here as much as the quantities are: a ratio that does not
    /// announce itself as a ratio is the failure mode this task exists inside.
    /// </summary>
    public class ConsumablesTallyTests
    {
        private static ConsumablesTally Fired()
        {
            var t = new ConsumablesTally();
            t.Consider("hoop_iron");
            t.Fired("hoop_iron", "walled_area_m2", 1.2, "m", 564.22);
            return t;
        }

        [Fact]
        public void Nothing_Considered_Reports_Nothing()
        {
            // An invented zero would read as a finding.
            Assert.Null(new ConsumablesTally().Summary());
            Assert.Null(new ConsumablesTally().Banner());
        }

        [Fact]
        public void A_Successful_Run_Reports_Its_Denominator()
        {
            var t = new ConsumablesTally();
            for (int i = 0; i < 4; i++) t.Consider("k" + i);
            t.Fired("hoop_iron", "walled_area_m2", 1.2, "m", 500);

            string s = t.Summary();

            Assert.Contains("4", s);
            Assert.Contains("1", s);
        }

        [Fact]
        public void An_Absent_Driver_Is_Named_And_Explained_As_Intended_Behaviour()
        {
            // The commonest outcome and the one most easily mistaken for a bug:
            // a project with no modelled steel gets no binding wire.
            var t = new ConsumablesTally();
            t.Consider("binding_wire");
            t.RejectDriverAbsent("binding_wire", "rebar_kg");

            string s = t.Summary();

            Assert.Contains("rebar_kg", s);
            Assert.Contains("binding_wire", s);
            Assert.Contains("intended", s, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void An_Unusable_Rule_Is_Named_So_It_Can_Be_Removed()
        {
            var t = new ConsumablesTally();
            t.Consider("mystery");
            t.RejectUnknownDriver("mystery", "concrete_m3");

            string s = t.Summary();

            Assert.Contains("mystery", s);
            Assert.Contains("never fire", s, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void A_Unit_Mismatch_Reaches_The_Same_Line()
        {
            var t = new ConsumablesTally();
            t.Consider("formwork_nails");
            t.UnitMismatches.Add("formwork in 'item'");

            Assert.Contains("formwork in 'item'", t.Summary());
        }

        [Fact]
        public void The_Banner_Only_Appears_When_Something_Was_Derived()
        {
            // A banner qualifying rows that were never emitted is noise, and
            // noise is how real banners come to be ignored.
            var t = new ConsumablesTally();
            t.Consider("hoop_iron");
            t.RejectDriverAbsent("hoop_iron", "walled_area_m2");

            Assert.NotNull(t.Summary());     // the scan still reports
            Assert.Null(t.Banner());         // the banner does not
        }

        [Fact]
        public void The_Banner_Says_These_Are_Practice_Heuristics_Not_A_Standard()
        {
            // The exact wording pattern SiteToolsCalculator established, so the
            // two ratio-derived sources in this document read alike.
            string b = Fired().Banner();

            Assert.Contains("PRACTICE HEURISTICS", b);
            Assert.Contains("not a standard", b, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Review before issue", b, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void The_Banner_Says_The_Numbers_Are_Derived_And_Not_Measured()
        {
            string b = Fired().Banner();

            Assert.Contains("DERIVED", b, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not", b, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("measured", b, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void The_Banner_Quotes_The_Actual_Ratio_And_Driver_So_It_Can_Be_Checked()
        {
            // The difference between a disclaimer and a diagnostic. A reader can
            // compare 1.2 m/m² against the specified course interval without
            // opening the JSON.
            string b = Fired().Banner();

            Assert.Contains("hoop_iron", b);
            Assert.Contains("1.2", b);
            Assert.Contains("564.22", b);
            Assert.Contains("walling", b, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void The_Banner_Names_The_File_To_Edit()
        {
            string b = Fired().Banner();

            Assert.Contains("STING_CONSUMABLES.json", b);
            Assert.Contains("consumables.json", b);
        }

        [Fact]
        public void Reset_Clears_Everything()
        {
            var t = Fired();
            t.RejectDriverAbsent("x", "rebar_kg");
            t.RejectNoRatio("y");
            t.UnitMismatches.Add("z");

            t.Reset();

            Assert.Null(t.Summary());
            Assert.Null(t.Banner());
        }
    }

    /// <summary>
    /// MATSCHED-T4 — the seam between the consumables code and the four shipped
    /// data files. Each is valid alone; only a comparison catches a rule whose
    /// emitted unit and supplier sourceUnit disagree, which fails at runtime as
    /// a blocked conversion and a bare figure on the page.
    /// </summary>
    public class ConsumablesShippedDataTests
    {
        private static string DataFile(string name) =>
            Path.Combine(AppContext.BaseDirectory, "Data", name);

        private static ConsumablesLibrary Consumables() =>
            JsonConvert.DeserializeObject<ConsumablesLibrary>(File.ReadAllText(DataFile("STING_CONSUMABLES.json")));

        private static SupplierUnitTable Units() =>
            JsonConvert.DeserializeObject<SupplierUnitTable>(File.ReadAllText(DataFile("STING_SUPPLIER_UNITS.json")));

        private static StageLibrary Stages() =>
            JsonConvert.DeserializeObject<StageLibrary>(File.ReadAllText(DataFile("STING_MATERIAL_STAGES.json")));

        [Fact]
        public void The_Shipped_Library_Parses_And_Is_Not_Empty()
        {
            // Newtonsoft leaves a mistyped field at its default rather than
            // throwing, so valid JSON plus a green build can still be
            // runtime-dead. This deserialises with the real POCO.
            var lib = Consumables();

            Assert.NotNull(lib);
            Assert.NotEmpty(lib.Rules);
        }

        [Fact]
        public void Every_Shipped_Rule_Names_A_Known_Driver()
        {
            foreach (var r in Consumables().Rules)
                Assert.True(ConsumablesCalculator.IsKnownDriver(r.Driver),
                            $"rule '{r.ConstituentKind}' names driver '{r.Driver}', which can never resolve");
        }

        [Fact]
        public void Every_Shipped_Rule_States_Where_Its_Figure_Came_From()
        {
            // The one field that makes this table honest rather than magic. A
            // ratio without a stated source is indistinguishable from a guess.
            foreach (var r in Consumables().Rules)
            {
                Assert.False(string.IsNullOrWhiteSpace(r.SourceNote),
                             $"rule '{r.ConstituentKind}' carries no sourceNote");
                Assert.True(r.SourceNote.Trim().Length >= 40,
                            $"rule '{r.ConstituentKind}' has a sourceNote too short to explain anything");
            }
        }

        [Fact]
        public void Every_Shipped_Rule_Has_A_Positive_Ratio_And_A_Unit()
        {
            foreach (var r in Consumables().Rules)
            {
                Assert.True(r.PerDriver > 0, $"rule '{r.ConstituentKind}' has no ratio");
                Assert.False(string.IsNullOrWhiteSpace(r.Unit), $"rule '{r.ConstituentKind}' has no unit");
            }
        }

        [Fact]
        public void Every_Shipped_Rule_Emits_The_Unit_Its_Commodity_Is_Bought_In()
        {
            // The unit guard REFUSES to convert on a mismatch, so a rule emitting
            // "m2" into a commodity bought per "m" would stop converting and
            // print a bare figure where an order quantity belongs.
            var units = Units();
            foreach (var r in Consumables().Rules)
            {
                var rule = units.ResolveByKind(r.ConstituentKind);
                Assert.True(rule != null, $"no supplier-unit rule matches kind '{r.ConstituentKind}'");
                Assert.Equal(StingTools.BOQ.BoqUnits.Normalise(rule.SourceUnit),
                             StingTools.BOQ.BoqUnits.Normalise(r.Unit));
            }
        }

        [Fact]
        public void Every_Consumable_Commodity_Has_A_Baseline_Rate()
        {
            var rates = CommodityRateResolver.ParseCsv(
                File.ReadAllLines(DataFile("STING_COMMODITY_RATES.csv")), out var skipped);
            Assert.Empty(skipped);

            var resolver = new CommodityRateResolver(rates, null);
            var units = Units();

            foreach (var r in Consumables().Rules)
            {
                var rule = units.ResolveByKind(r.ConstituentKind);
                Assert.True(resolver.Resolve(rule.CommodityKey).RateUGX > 0,
                            $"commodity '{rule.CommodityKey}' is derivable but has no baseline rate");
            }
        }

        [Fact]
        public void Every_Consumable_Commodity_Declares_On_Its_Own_Row_That_It_Is_Derived()
        {
            // The rule's description is what the aggregator prints — it
            // overrides the row's own. If the qualification is not here, it does
            // not reach the page, and the banner alone can be scrolled past.
            var units = Units();
            foreach (var r in Consumables().Rules)
            {
                string d = units.ResolveByKind(r.ConstituentKind).Description;
                Assert.Contains("DERIVED", d, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("not measured", d, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void Every_Consumable_Kind_Routes_By_KIND_Not_By_Category()
        {
            // Resolved against a category that routes SOMEWHERE ELSE, so the
            // assertion exercises the kind route. Three gates in T1-T3 were found
            // blind exactly here: a same-stage category made them pass whether or
            // not the kind was declared.
            var lib = Stages();
            var ix = StageIndex.Build(lib.Stages, lib.DefaultStageId);

            Assert.Equal("superstructure", ix.Resolve("hoop_iron", "Roofs", ""));
            Assert.Equal("superstructure", ix.Resolve("binding_wire", "Roofs", ""));
            Assert.Equal("superstructure", ix.Resolve("formwork_nails", "Roofs", ""));
            Assert.Equal("roof", ix.Resolve("roof_fastener", "Walls", ""));
        }

        [Fact]
        public void The_Category_Routes_Alone_Would_Not_Have_Been_Enough()
        {
            // Pins the premise of the test above.
            var lib = Stages();
            var ix = StageIndex.Build(lib.Stages, lib.DefaultStageId);

            Assert.Equal("roof", ix.Resolve("", "Roofs", ""));
            Assert.Equal("superstructure", ix.Resolve("", "Walls", ""));
        }

        [Fact]
        public void No_Consumable_Is_An_Intermediate_Measure()
        {
            // A roll of hoop iron IS what you buy. Declaring one a memorandum
            // would hard-zero a real cost.
            var kinds = Consumables().Rules.Select(r => r.ConstituentKind).ToList();
            Assert.DoesNotContain(Stages().IntermediateMeasures,
                r => kinds.Contains(r.Kind, StringComparer.OrdinalIgnoreCase));
        }

        [Fact]
        public void Consumables_Are_Bought_By_The_Whole_Pack()
        {
            // You cannot buy 3.4 rolls of hoop iron or 2.7 packs of screws.
            var units = Units();
            foreach (var r in Consumables().Rules)
                Assert.True(units.ResolveByKind(r.ConstituentKind).RoundUpToWhole,
                            $"'{r.ConstituentKind}' is bought in fractions of a pack");
        }

        [Fact]
        public void The_Shipped_Ratios_Stay_In_A_Believable_Band()
        {
            // Not a rubber stamp: each band is the range a QS would argue inside,
            // and a figure outside it is a data-entry slip rather than a
            // different practice. The source notes give the derivations.
            var by = Consumables().Rules.ToDictionary(r => r.ConstituentKind, StringComparer.OrdinalIgnoreCase);

            // One strip every 3rd-6th course of 200 mm block is 0.8-1.7 m/m².
            Assert.InRange(by["hoop_iron"].PerDriver, 0.5, 3.0);
            // 1.0-1.5% of rebar mass is the practice range; allow a little either side.
            Assert.InRange(by["binding_wire"].PerDriver, 0.008, 0.02);
            // 0.15-0.25 kg/m² for sawn-timber formwork through several reuses.
            Assert.InRange(by["formwork_nails"].PerDriver, 0.1, 0.4);
            // Every second corrugation on purlins at 0.9-1.2 m is roughly 8-14/m².
            Assert.InRange(by["roof_fastener"].PerDriver, 6.0, 20.0);
        }

        [Fact]
        public void The_Library_Note_Says_It_Is_Not_A_Standard()
        {
            // The JSON, the class header and the export banner must all say the
            // same thing — this is the JSON half.
            string note = Consumables().Note ?? "";

            Assert.Contains("NOT A STANDARD", note, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("consumables.json", note);
        }

        [Fact]
        public void End_To_End_The_Shipped_Table_Reaches_The_Page_As_Priced_Packs()
        {
            // The whole chain on real data, through the REAL aggregator: shipped
            // ratios → quantities → stage routing → unit guard → conversion →
            // rate.
            //
            // The first version of this called SupplierUnitConverter directly and
            // was BLIND: the converter converts whatever it is handed, and the
            // unit guard lives in CommodityAggregator — so changing a rule's unit
            // to something its commodity is not bought in left the test green.
            var lib = Consumables();
            var stages = Stages();
            var drivers = new ConsumableDrivers
            { WalledAreaM2 = 564.22, RebarKg = 8000, FormworkM2 = 300, RoofCoveringM2 = 856 };

            var rows = ConsumablesCalculator.Quantify(drivers, lib.Rules, new ConsumablesTally());
            Assert.Equal(lib.Rules.Count, rows.Count);

            var rates = CommodityRateResolver.ParseCsv(
                File.ReadAllLines(DataFile("STING_COMMODITY_RATES.csv")), out _);

            var doc = CommodityAggregator.Build(new AggregatorInputs
            {
                Constituents = rows,
                Units = Units(),
                StageDefs = stages.Stages,
                DefaultStageId = stages.DefaultStageId,
                IntermediateMeasures = stages.IntermediateMeasures,
                Rates = new CommodityRateResolver(rates, null),
            });

            var byKind = doc.Stages.SelectMany(s => s.Commodities)
                            .ToDictionary(c => c.SourceKind, StringComparer.OrdinalIgnoreCase);

            foreach (var r in lib.Rules)
            {
                Assert.True(byKind.ContainsKey(r.ConstituentKind),
                            r.ConstituentKind + " never reached the document");
                var c = byKind[r.ConstituentKind];

                Assert.False(c.ConversionBlocked,
                             $"{r.ConstituentKind} was refused conversion: {c.ConversionNote}");
                Assert.True(c.OrderQuantity > 0, r.ConstituentKind + " ordered nothing");
                Assert.Equal(Math.Ceiling(c.OrderQuantity), c.OrderQuantity);   // whole packs
                Assert.True(c.RateUGX > 0, r.ConstituentKind + " reached the page unpriced");
                Assert.False(c.IsMemorandum, r.ConstituentKind + " was hard-zeroed as a memorandum");
            }
        }

        [Fact]
        public void End_To_End_The_Consumables_Land_In_The_Sections_They_Belong_To()
        {
            var lib = Consumables();
            var stages = Stages();
            var rows = ConsumablesCalculator.Quantify(
                new ConsumableDrivers { WalledAreaM2 = 500, RebarKg = 8000, FormworkM2 = 300, RoofCoveringM2 = 856 },
                lib.Rules, new ConsumablesTally());

            // Descriptions are BLANKED so that only the CONSTITUENT KIND can
            // route these rows.
            //
            // With the shipped description in place this test was blind: "Roofing
            // screws / nails" contains "roofing", which is one of the roof
            // stage's typePatterns, so the row reached the roof section even with
            // roof_fastener deleted from the stage library. Kind is resolved
            // before typePattern, so the shipped behaviour was never wrong — but
            // a test that cannot see the kind route disappear is not testing it.
            foreach (var r in rows) r.Description = "";

            // The DEFAULT stage is deliberately swapped away from superstructure.
            //
            // Second blindness found here: superstructure IS the shipped default,
            // so hoop_iron, binding_wire and formwork_nails landed there whether
            // or not their kind route existed — deleting all three from the stage
            // library left this test green. Pointing the default at a stage none
            // of them belongs to makes every one of the four routes observable.
            var doc = CommodityAggregator.Build(new AggregatorInputs
            {
                Constituents = rows,
                Units = Units(),
                StageDefs = stages.Stages,
                DefaultStageId = "external",
            });

            string StageOf(string kind) => doc.Stages
                .First(s => s.Commodities.Any(c =>
                    string.Equals(c.SourceKind, kind, StringComparison.OrdinalIgnoreCase))).StageId;

            Assert.Equal("superstructure", StageOf("hoop_iron"));
            Assert.Equal("superstructure", StageOf("binding_wire"));
            Assert.Equal("superstructure", StageOf("formwork_nails"));
            Assert.Equal("roof", StageOf("roof_fastener"));
        }
    }
}
