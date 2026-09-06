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
    /// MATSCHED-T1 — screed.
    ///
    /// A 40 mm Cement Screed substrate layer contributed nothing to any export,
    /// because the layer walk asked only one question of only the finish layers:
    /// "is this a tile?". Every screeded floor in every project was therefore
    /// missing its cement and its sand, silently, with no row and no warning.
    ///
    /// These pin the engine half. The layer READ is Revit-side and CANNOT run
    /// here — see the class note on ScreedShippedDataTests.
    /// </summary>
    public class ScreedTakeoffTests
    {
        private static ScreedInput Floor(double area, double thicknessM) => new ScreedInput
        {
            AreaM2 = area,
            ThicknessM = thicknessM,
            ScreedLabel = "Cement Screed",
            CementBagsPerM3 = 9,
            SandRatio = 1.25
        };

        [Fact]
        public void Screed_Emits_Area_Cement_And_Sand()
        {
            // 100 m2 x 40 mm = 4 m3 -> 36 bags of cement, 5 m3 of sand.
            var lines = CompoundTakeoff.Screed(Floor(100, 0.040));

            Assert.Equal(100, lines.Single(l => l.Kind == "screed").Quantity);
            Assert.Equal(36, lines.Single(l => l.Kind == "screed_cement").Quantity);
            Assert.Equal(5, lines.Single(l => l.Kind == "screed_sand").Quantity);
        }

        [Fact]
        public void The_Cement_Is_Bought_By_The_Bag_And_The_Sand_By_The_Cubic_Metre()
        {
            // The unit guard in CommodityAggregator REFUSES to convert when a
            // measured unit disagrees with the rule's sourceUnit, so emitting
            // "m3" of cement here would stop the conversion dead and print bare
            // cubic metres where a bag count belongs.
            var lines = CompoundTakeoff.Screed(Floor(100, 0.040));

            Assert.Equal("m2", lines.Single(l => l.Kind == "screed").Unit);
            Assert.Equal("bag", lines.Single(l => l.Kind == "screed_cement").Unit);
            Assert.Equal("m3", lines.Single(l => l.Kind == "screed_sand").Unit);
        }

        [Fact]
        public void No_Stated_Thickness_Means_No_Screed_At_All()
        {
            // The driver is the layer's declared width. A screed with no
            // thickness has no volume; emitting a default one would invent
            // cement that nobody ordered. Nothing is emitted — not even a
            // zero-quantity area row, which would read as a measurement.
            Assert.Empty(CompoundTakeoff.Screed(Floor(100, 0)));
            Assert.Empty(CompoundTakeoff.Screed(Floor(100, -0.04)));
        }

        [Fact]
        public void No_Area_Means_No_Screed()
        {
            Assert.Empty(CompoundTakeoff.Screed(Floor(0, 0.040)));
            Assert.Empty(CompoundTakeoff.Screed(Floor(-5, 0.040)));
        }

        [Fact]
        public void Quantities_Are_Net_Of_Wastage()
        {
            // Wastage lives in exactly ONE place: the supplier-unit rule.
            // Applying it here as well is what delivered blocks at ~10% when the
            // rule said 5%, with nothing to say which allowance was which.
            var lines = CompoundTakeoff.Screed(Floor(100, 0.050));

            Assert.Equal(100, lines.Single(l => l.Kind == "screed").Quantity);
            Assert.Equal(45, lines.Single(l => l.Kind == "screed_cement").Quantity);  // 5 m3 x 9
        }

        [Fact]
        public void Missing_Mix_Ratios_Emit_The_Area_But_No_Cement_Or_Sand()
        {
            // A screed name with no row in MATERIAL_LOOKUP must not silently
            // invent cement from a zero ratio.
            var s = Floor(50, 0.040);
            s.CementBagsPerM3 = 0;
            s.SandRatio = 0;

            var lines = CompoundTakeoff.Screed(s);

            Assert.Single(lines);
            Assert.Equal("screed", lines[0].Kind);
        }

        [Fact]
        public void An_Unnamed_Screed_Still_Reads_As_A_Screed()
        {
            var s = Floor(20, 0.040);
            s.ScreedLabel = null;

            var lines = CompoundTakeoff.Screed(s);

            Assert.Equal(3, lines.Count);
            Assert.Contains("screed", lines.Single(l => l.Kind == "screed").Description,
                            StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// MATSCHED-T1 — the classifier. Narrow on purpose: a false positive here
    /// does not add a material, it DOUBLE-COUNTS one, because plaster, render
    /// and mortar are already measured on the wall and masonry paths.
    /// </summary>
    public class ScreedClassifierTests
    {
        [Theory]
        [InlineData("Cement Screed")]
        [InlineData("Screed")]
        [InlineData("Sand-Cement Screed")]
        [InlineData("SAND CEMENT")]
        [InlineData("Cement/Sand")]
        [InlineData("Granolithic Screed")]
        public void Screed_Names_Read_As_Screed(string name)
            => Assert.True(FinishTextClassifier.IsScreed(name), name);

        [Theory]
        [InlineData("Cement Plaster")]
        [InlineData("Sand-Cement Plaster")]
        [InlineData("External Render")]
        [InlineData("Bedding Mortar")]
        [InlineData("Sand-Cement Mortar")]
        [InlineData("Gypsum Skim")]
        public void The_Things_Already_Measured_Elsewhere_Are_Not_Screed(string name)
            => Assert.False(FinishTextClassifier.IsScreed(name), name);

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("Concrete")]
        [InlineData("Plywood")]
        [InlineData("Rigid Insulation")]
        public void Everything_Else_Is_Not_Screed(string name)
            => Assert.False(FinishTextClassifier.IsScreed(name));

        [Theory]
        [InlineData("Ceramic Tile")]
        [InlineData("Porcelain Tile")]
        [InlineData("Terrazzo")]
        [InlineData("Granite")]
        [InlineData("Terrazzo Screed")]
        public void Nothing_Is_Both_A_Tile_And_A_Screed(string name)
        {
            // Two sources must never measure the same surface. Disjointness is
            // enforced in IsScreed rather than left to the hope that the two
            // patterns never overlap — "Terrazzo Screed" is the case that proves
            // they can.
            Assert.False(FinishTextClassifier.IsTile(name) && FinishTextClassifier.IsScreed(name), name);
        }

        [Theory]
        [InlineData("Cement Screed", "STANDARD")]
        [InlineData("Screed", "STANDARD")]
        [InlineData("Granolithic Screed", "HEAVY_DUTY")]
        [InlineData("Heavy-Duty Screed", "HEAVY_DUTY")]
        [InlineData(null, "STANDARD")]
        public void ScreedKey_Maps_To_Keys_The_Table_Actually_Has(string name, string expected)
            => Assert.Equal(expected, FinishTextClassifier.ScreedKey(name));
    }

    /// <summary>
    /// MATSCHED-T1 — the scan diagnostic. Silence is a bug: an export with no
    /// screed rows must say WHICH of the four possible reasons produced it.
    /// The message is the whole value of a diagnostic, so the message is the
    /// part under test.
    /// </summary>
    public class ScreedScanTallyTests
    {
        [Fact]
        public void Nothing_Inspected_Reports_Nothing()
        {
            // An invented zero would read as a finding. No scan, no line.
            Assert.Null(new ScreedScanTally().Summary());
        }

        [Fact]
        public void A_Successful_Scan_Reports_Its_Denominator()
        {
            var t = new ScreedScanTally
            { TypesInspected = 12, TypesWithCandidateLayer = 9, TypesMatched = 4 };

            string s = t.Summary();

            Assert.Contains("12", s);
            Assert.Contains("9", s);
            Assert.Contains("4", s);
        }

        [Fact]
        public void No_Match_With_Rejected_Names_Names_Them()
        {
            var t = new ScreedScanTally { TypesInspected = 3, TypesWithCandidateLayer = 3 };
            t.RejectedMaterials.Add("Plywood Deck");
            t.RejectedMaterials.Add("Rigid Insulation");

            string s = t.Summary();

            Assert.Contains("Plywood Deck", s);
            Assert.Contains("Rigid Insulation", s);
        }

        [Fact]
        public void No_Candidate_Layers_Says_The_Model_Is_Silent_Not_The_Plugin()
        {
            var t = new ScreedScanTally { TypesInspected = 5, TypesWithCandidateLayer = 0 };

            Assert.Contains("does not", t.Summary(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Candidate_Layers_With_No_Named_Material_Say_So()
        {
            var t = new ScreedScanTally { TypesInspected = 5, TypesWithCandidateLayer = 5 };

            Assert.Contains("no named material", t.Summary(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void A_Zero_Thickness_Screed_Is_Reported_Separately_From_A_Rejection()
        {
            // This is the one failure the other counters cannot express: the
            // NAME was right and the DRIVER was missing. Folding it into
            // "rejected" would send someone to fix the classifier instead of
            // the layer width.
            var t = new ScreedScanTally
            { TypesInspected = 2, TypesWithCandidateLayer = 2, MatchedButZeroThickness = 2 };

            string s = t.Summary();

            Assert.Contains("zero", s, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("thickness", s, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Reset_Clears_Every_Counter()
        {
            var t = new ScreedScanTally
            { TypesInspected = 1, TypesWithCandidateLayer = 1, TypesMatched = 1, MatchedButZeroThickness = 1 };
            t.RejectedMaterials.Add("x");

            t.Reset();

            Assert.Null(t.Summary());
            Assert.Empty(t.RejectedMaterials);
        }
    }

    /// <summary>
    /// MATSCHED-T1 — the seam between the screed code and the three shipped
    /// data files it depends on. Each file is valid on its own; only a
    /// comparison catches a key the builder composes one way and the CSV spells
    /// another, which fails at runtime as a zero and drops rows without a word.
    /// </summary>
    public class ScreedShippedDataTests
    {
        private static string DataFile(string name) =>
            Path.Combine(AppContext.BaseDirectory, "Data", name);

        private static Dictionary<string, StingTools.UI.MaterialLookupRow> Lookup() =>
            StingTools.UI.MaterialLookupParser.Parse(File.ReadAllLines(DataFile("MATERIAL_LOOKUP.csv")));

        private static StageLibrary Stages() =>
            JsonConvert.DeserializeObject<StageLibrary>(File.ReadAllText(DataFile("STING_MATERIAL_STAGES.json")));

        private static SupplierUnitTable Units() =>
            JsonConvert.DeserializeObject<SupplierUnitTable>(File.ReadAllText(DataFile("STING_SUPPLIER_UNITS.json")));

        [Theory]
        [InlineData("SCREED STANDARD")]
        [InlineData("SCREED HEAVY_DUTY")]
        [InlineData("SCREED DEFAULT")]
        public void Every_Screed_Key_The_Builder_Composes_Resolves_To_Real_Ratios(string key)
        {
            // The builder asks for exactly these keys — "SCREED " + ScreedKey(name).
            // A typo is not a compile error and not a load error; it is a zero,
            // and a zero silently drops the cement and sand rows.
            var row = Lookup()[key];

            Assert.True(row.Properties["MIX_CEMENT_BAGS_PER_M3"] > 0, key + " has no cement ratio");
            Assert.True(row.Properties["MIX_SAND_RATIO"] > 0, key + " has no sand ratio");
        }

        [Fact]
        public void ScreedKey_Only_Ever_Names_A_Row_That_Exists()
        {
            // The classifier and the table are edited independently. A key the
            // classifier can return but the table does not carry falls to
            // DEFAULT silently, which is a wrong mix rather than an error.
            var lookup = Lookup();
            foreach (string name in new[] { "Cement Screed", "Granolithic Screed", "Heavy-Duty Screed", "", null })
                Assert.True(lookup.ContainsKey("SCREED " + FinishTextClassifier.ScreedKey(name)),
                            "ScreedKey returned a key with no row for name: " + (name ?? "(null)"));
        }

        [Fact]
        public void The_Screed_Mix_Stays_In_A_Believable_Band()
        {
            // 4-14 bags/m3 spans a weak 1:8 to a strong 1:3. Outside it is a
            // data-entry slip, not a mix.
            foreach (string key in new[] { "SCREED STANDARD", "SCREED HEAVY_DUTY", "SCREED DEFAULT" })
            {
                Assert.InRange(Lookup()[key].Properties["MIX_CEMENT_BAGS_PER_M3"], 4.0, 14.0);
                Assert.InRange(Lookup()[key].Properties["MIX_SAND_RATIO"], 0.5, 2.0);
            }
        }

        [Fact]
        public void A_Screed_Mix_Matches_The_Mortar_Mix_It_Claims_To_Be()
        {
            // Two figures for one ratio is a disagreement nobody would ever
            // compare. A 1:4 screed and a 1:4 mortar consume the same cement.
            var lookup = Lookup();
            Assert.Equal(lookup["MORTAR 1:4"].Properties["CEMENT_BAGS_PER_M3"],
                         lookup["SCREED STANDARD"].Properties["MIX_CEMENT_BAGS_PER_M3"]);
            Assert.Equal(lookup["MORTAR 1:3"].Properties["CEMENT_BAGS_PER_M3"],
                         lookup["SCREED HEAVY_DUTY"].Properties["MIX_CEMENT_BAGS_PER_M3"]);
        }

        [Fact]
        public void Every_Screed_Kind_Routes_To_Finishes()
        {
            // A screed is a finish. The regression this guards shipped once:
            // category-matched rules inherited the ELEMENT's stage and filed
            // wall paint under SUPERSTRUCTURE. Screed comes off a FLOOR, so
            // without an explicit kind route it would land in the frame.
            var lib = Stages();
            var ix = StageIndex.Build(lib.Stages, lib.DefaultStageId);

            foreach (string kind in new[] { "screed", "screed_cement", "screed_sand" })
                Assert.Equal("finishes", ix.Resolve(kind, "Floors", ""));
        }

        [Fact]
        public void Screed_Cement_And_Sand_Reuse_The_Existing_Commodities()
        {
            // Minting a "screed cement" commodity beside "cement" would split
            // one order into two part-loads and round each up separately.
            var units = Units();

            Assert.Equal("cement", units.ResolveByKind("screed_cement").CommodityKey);
            Assert.Equal("sand", units.ResolveByKind("screed_sand").CommodityKey);
        }

        [Fact]
        public void Screed_Constituents_Buy_By_The_Unit_They_Are_Measured_In()
        {
            // The unit guard REFUSES to convert on a mismatch, so a rule whose
            // sourceUnit disagreed would silently stop converting and print the
            // raw measured figure instead of an order quantity.
            var units = Units();

            Assert.Equal("bag", units.ResolveByKind("screed_cement").SourceUnit);
            Assert.Equal("m3", units.ResolveByKind("screed_sand").SourceUnit);
        }

        [Fact]
        public void Screed_Constituents_Have_A_Baseline_Rate()
        {
            var rates = CommodityRateResolver.ParseCsv(
                File.ReadAllLines(DataFile("STING_COMMODITY_RATES.csv")), out _);
            var resolver = new CommodityRateResolver(rates, null);
            var units = Units();

            foreach (string kind in new[] { "screed_cement", "screed_sand" })
            {
                var rule = units.ResolveByKind(kind);
                Assert.True(rule != null, "no supplier-unit rule matches kind " + kind);
                Assert.True(resolver.Resolve(rule.CommodityKey).RateUGX > 0,
                            "commodity " + rule.CommodityKey + " has no baseline rate");
            }
        }

        [Fact]
        public void Screed_Is_Declared_As_An_Intermediate_With_Both_Its_Children()
        {
            // An intermediate that is not declared becomes a priceable duplicate
            // of what it decomposes into — the screeded area charged once as m2
            // and again as its cement and sand.
            var rule = Stages().IntermediateMeasures.SingleOrDefault(
                r => string.Equals(r.Kind, "screed", StringComparison.OrdinalIgnoreCase));

            Assert.True(rule != null, "screed is emitted but not declared in intermediateMeasures");
            Assert.Contains("screed_cement", rule.Children);
            Assert.Contains("screed_sand", rule.Children);
        }

        [Fact]
        public void The_Screed_Area_Row_Goes_Memorandum_Once_Its_Cement_Arrives()
        {
            // The declaration alone is not the behaviour. This runs the marker
            // over a document shaped like a real one and asserts the money.
            var doc = new MaterialScheduleDocument();
            doc.Stages.Add(new StageSection
            {
                StageId = "finishes",
                Commodities =
                {
                    new MaterialCommodity { CommodityKey = "Screed — Cement Screed", SourceKind = "screed" },
                    new MaterialCommodity { CommodityKey = "cement", SourceKind = "screed_cement" },
                    new MaterialCommodity { CommodityKey = "sand", SourceKind = "screed_sand" },
                }
            });

            IntermediateMeasureMarker.Apply(doc, Stages().IntermediateMeasures);

            Assert.True(doc.Stages[0].Commodities[0].IsMemorandum);
            Assert.False(doc.Stages[0].Commodities[1].IsMemorandum);
            Assert.False(doc.Stages[0].Commodities[2].IsMemorandum);
        }

        [Fact]
        public void A_Screed_Area_With_No_Cement_Behind_It_Stays_Priceable()
        {
            // Turning a double-count into an OMISSION is worse: a duplicated
            // line is at least arguable, a missing one is invisible.
            var doc = new MaterialScheduleDocument();
            doc.Stages.Add(new StageSection
            {
                StageId = "finishes",
                Commodities = { new MaterialCommodity { CommodityKey = "Screed", SourceKind = "screed" } }
            });

            IntermediateMeasureMarker.Apply(doc, Stages().IntermediateMeasures);

            Assert.False(doc.Stages[0].Commodities[0].IsMemorandum);
        }
    }
}
