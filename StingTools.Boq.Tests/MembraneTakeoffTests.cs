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
    /// MATSCHED-T3 — sheet membranes.
    ///
    /// `MaterialFunctionAssignment.Membrane` layers were ignored entirely, so a
    /// ground-bearing slab's DPM and a roof's underlay — both real purchased
    /// materials whose area the model already STATES — produced nothing, and
    /// produced it silently.
    ///
    /// These pin the engine half. The layer READ is Revit-side and CANNOT run
    /// here — see the class note on MembraneShippedDataTests.
    /// </summary>
    public class MembraneTakeoffTests
    {
        private static MembraneInput Slab(double area) => new MembraneInput
        {
            AreaM2 = area, DpmLayers = 1, DpmLabel = "1000ga Polythene DPM"
        };

        private static MembraneInput Roof(double area) => new MembraneInput
        {
            AreaM2 = area, UnderlayLayers = 1, UnderlayLabel = "Sarking Membrane"
        };

        [Fact]
        public void A_Slab_Membrane_Emits_Dpm()
        {
            var lines = CompoundTakeoff.Membranes(Slab(120));

            Assert.Equal(120, lines.Single(l => l.Kind == "dpm").Quantity);
            Assert.Equal("m2", lines.Single(l => l.Kind == "dpm").Unit);
        }

        [Fact]
        public void A_Roof_Membrane_Emits_Underlay()
        {
            var lines = CompoundTakeoff.Membranes(Roof(200));

            Assert.Equal(200, lines.Single(l => l.Kind == "roof_underlay").Quantity);
        }

        [Fact]
        public void Dpm_And_Underlay_Are_Different_Commodities()
        {
            // Different products in different roll sizes at different rates.
            // Merging them would average two prices into one wrong one, which is
            // the same defect that sent a brick wall's brick count into the block
            // commodity.
            Assert.DoesNotContain(CompoundTakeoff.Membranes(Slab(10)), l => l.Kind == "roof_underlay");
            Assert.DoesNotContain(CompoundTakeoff.Membranes(Roof(10)), l => l.Kind == "dpm");
        }

        [Fact]
        public void Two_Membrane_Layers_Are_Two_Purchases_Of_That_Area()
        {
            // A build-up declaring two DPM layers really does need twice the
            // roll. Layers are counted, not collapsed - exactly as two tiled
            // faces are two areas of tiling.
            var m = Slab(100);
            m.DpmLayers = 2;

            Assert.Equal(200, CompoundTakeoff.Membranes(m).Single(l => l.Kind == "dpm").Quantity);
        }

        [Fact]
        public void A_Build_Up_With_Both_Emits_Both()
        {
            var m = Slab(50);
            m.UnderlayLayers = 1;
            m.UnderlayLabel = "Breather Membrane";

            var lines = CompoundTakeoff.Membranes(m);

            Assert.Equal(50, lines.Single(l => l.Kind == "dpm").Quantity);
            Assert.Equal(50, lines.Single(l => l.Kind == "roof_underlay").Quantity);
        }

        [Fact]
        public void No_Area_Means_No_Membrane()
        {
            Assert.Empty(CompoundTakeoff.Membranes(Slab(0)));
            Assert.Empty(CompoundTakeoff.Membranes(Slab(-5)));
        }

        [Fact]
        public void No_Layers_Means_No_Membrane()
        {
            // Not a zero-quantity row, which would read as a measurement of
            // nothing rather than as an absence of information.
            Assert.Empty(CompoundTakeoff.Membranes(new MembraneInput { AreaM2 = 100 }));
        }

        [Fact]
        public void A_Negative_Layer_Count_Never_Yields_A_Negative_Quantity()
        {
            var m = Slab(100);
            m.DpmLayers = -2;

            var lines = CompoundTakeoff.Membranes(m);

            Assert.Empty(lines);
        }

        [Fact]
        public void Quantities_Are_Net_Of_Wastage()
        {
            // Laps are a real and substantial allowance on a DPM - 150-300 mm at
            // every joint, plus the turn-up at the perimeter - which is exactly
            // why they belong to the supplier-unit rule where they are visible
            // and arguable, not baked in here where they would be applied twice.
            Assert.Equal(100, CompoundTakeoff.Membranes(Slab(100)).Single(l => l.Kind == "dpm").Quantity);
        }

        [Fact]
        public void An_Unnamed_Membrane_Still_Reads_As_One()
        {
            var m = Slab(20);
            m.DpmLabel = null;

            Assert.Contains("membrane",
                CompoundTakeoff.Membranes(m).Single(l => l.Kind == "dpm").Description,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// MATSCHED-T3 — the classifier. The dangerous case is INSULATION: it sits
    /// in the same build-up, is bought by THICKNESS rather than by the square
    /// metre of roll, and absorbing it into a membrane commodity would silently
    /// mis-price both.
    /// </summary>
    public class MembraneClassifierTests
    {
        [Theory]
        [InlineData("1000ga Polythene DPM")]
        [InlineData("Damp Proof Membrane")]
        [InlineData("Damp-Proof Membrane")]
        [InlineData("Visqueen")]
        [InlineData("Roof Underlay")]
        [InlineData("Sarking Membrane")]
        [InlineData("Breather Membrane")]
        [InlineData("Bitumen Felt")]
        public void Sheet_Membranes_Read_As_Membranes(string name)
            => Assert.True(FinishTextClassifier.IsMembrane(name), name);

        [Theory]
        [InlineData("Rigid Insulation")]
        [InlineData("PIR Insulation Board")]
        [InlineData("Expanded Polystyrene")]
        [InlineData("Rockwool")]
        [InlineData("Mineral Wool")]
        public void Insulation_Is_Never_A_Membrane(string name)
        {
            // The runner's explicit caution. Insulation is a separate commodity
            // bought by thickness; conflating them silently mis-prices both.
            //
            // NOTE what this proves and what it does NOT. None of these names
            // matches the membrane pattern in the first place, so they are
            // turned away by its NARROWNESS and the exclusion list never sees
            // them: this test alone passed unchanged with the exclusion list
            // deleted. The load-bearing cases are the two below.
            Assert.False(FinishTextClassifier.IsMembrane(name), name);
            Assert.True(FinishTextClassifier.IsInsulation(name), name);
        }

        [Fact]
        public void An_Insulation_Board_Named_As_A_Membrane_Is_Still_Insulation()
        {
            // The case that makes the exclusion load-bearing: a name that hits
            // the membrane pattern AND the insulation pattern must lose.
            // "Insulation Membrane" is exactly the kind of vendor name that
            // would otherwise be bought by the roll.
            Assert.False(FinishTextClassifier.IsMembrane("Insulation Membrane"));
            Assert.True(FinishTextClassifier.IsInsulation("Insulation Membrane"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("Concrete")]
        [InlineData("Cement Screed")]
        [InlineData("Ceramic Tile")]
        public void Everything_Else_Is_Not_A_Membrane(string name)
            => Assert.False(FinishTextClassifier.IsMembrane(name));

        [Theory]
        [InlineData("Steps")]          // contains "eps"
        [InlineData("Purlin")]         // contains "pur"
        [InlineData("Respiratory")]    // contains "pir"
        [InlineData("Expansion Joint")]
        public void A_Three_Letter_Product_Code_Needs_Both_Word_Boundaries(string name)
        {
            // Found by a BLIND gate. The exclusion list was written as `eps\b`,
            // and the escapes were eaten on the way into the file, so it shipped
            // as a bare `eps` substring — matching "Steps". Even as intended it
            // was wrong: a right-bounded `eps\b` still matches "Steps"; only
            // `\beps\b` means the product.
            //
            // The cost of getting it wrong is asymmetric and invisible: "Purlin
            // Underlay" is a real membrane that a bare `pur` would have thrown
            // away with no row and no warning.
            Assert.False(FinishTextClassifier.IsInsulation(name), name);
        }

        [Theory]
        [InlineData("PIR Board")]
        [InlineData("XPS Board")]
        [InlineData("EPS 70")]
        public void The_Product_Codes_Still_Match_When_They_Are_The_Product(string name)
        {
            // The other half. Bounding them must not stop them working.
            Assert.True(FinishTextClassifier.IsInsulation(name), name);
            Assert.False(FinishTextClassifier.IsMembrane(name), name);
        }

        [Fact]
        public void A_Purlin_Underlay_Survives_The_Exclusion_List()
        {
            // The concrete case the bare `pur` substring would have discarded.
            Assert.True(FinishTextClassifier.IsMembrane("Purlin Underlay"));
            Assert.Equal("roof_underlay", FinishTextClassifier.MembraneKind("Purlin Underlay", false));
        }

        [Theory]
        [InlineData("1000ga Polythene DPM", false, "dpm")]
        [InlineData("Damp Proof Membrane", true, "dpm")]      // name beats host
        [InlineData("Roof Underlay", false, "roof_underlay")] // name beats host
        [InlineData("Sarking Membrane", true, "roof_underlay")]
        public void The_Name_Decides_When_It_Is_Unambiguous(string name, bool isRoof, string expected)
            => Assert.Equal(expected, FinishTextClassifier.MembraneKind(name, isRoof));

        [Theory]
        [InlineData(false, "dpm")]
        [InlineData(true, "roof_underlay")]
        public void An_Unnamed_Membrane_Is_Decided_By_The_Host(bool isRoof, string expected)
        {
            // The HOST is a fact the model states, not a guess: a membrane in a
            // roof is an underlay, a membrane in a floor is a DPM.
            Assert.Equal(expected, FinishTextClassifier.MembraneKind("Membrane", isRoof));
        }

        [Fact]
        public void A_Non_Membrane_Gets_No_Kind_At_All()
        {
            // "" rather than a default, so a caller cannot accidentally file
            // insulation under one of the two membrane commodities.
            Assert.Equal("", FinishTextClassifier.MembraneKind("Rigid Insulation", false));
            Assert.Equal("", FinishTextClassifier.MembraneKind("Concrete", true));
        }
    }

    /// <summary>
    /// MATSCHED-T3 — the scan diagnostic. This one carries two counters the
    /// others do not, and both report what the take-off deliberately does NOT
    /// price. An omission stated is not the same failure as an omission hidden.
    /// </summary>
    public class MembraneScanTallyTests
    {
        [Fact]
        public void Nothing_Inspected_Reports_Nothing()
            => Assert.Null(new MembraneScanTally().Summary());

        [Fact]
        public void A_Successful_Scan_Reports_Its_Denominator()
        {
            var t = new MembraneScanTally
            { TypesInspected = 9, TypesWithMembraneLayer = 4, TypesMatched = 3 };

            string s = t.Summary();

            Assert.Contains("9", s);
            Assert.Contains("4", s);
            Assert.Contains("3", s);
        }

        [Fact]
        public void Insulation_Is_Reported_Even_When_Membranes_Were_Found()
        {
            // The whole point. Reporting it only on failure would hide a real
            // unpriced material behind a partial success - which is how the
            // ceiling rejection gate came to be blind in the previous task.
            var t = new MembraneScanTally
            { TypesInspected = 3, TypesWithMembraneLayer = 3, TypesMatched = 3, InsulationLayersSeen = 2 };
            t.InsulationMaterials.Add("PIR Insulation Board");

            string s = t.Summary();

            Assert.Contains("insulation", s, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("thickness", s, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("PIR Insulation Board", s);
        }

        [Fact]
        public void Insulation_Is_Reported_When_Nothing_Matched_Too()
        {
            var t = new MembraneScanTally { TypesInspected = 2, InsulationLayersSeen = 1 };

            Assert.Contains("insulation", t.Summary(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Wall_Membranes_Are_Reported_As_A_Stated_Scope_Boundary()
        {
            // Not measuring them is correct - a horizontal DPC occupies one
            // course of a wall face. Not SAYING so would be the silent omission
            // this task exists to remove.
            var t = new MembraneScanTally { WallMembraneLayersSeen = 5 };

            string s = t.Summary();

            Assert.NotNull(s);
            Assert.Contains("wall", s, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not", s, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("5", s);
        }

        [Fact]
        public void Unrecognised_Membrane_Materials_Are_Named()
        {
            var t = new MembraneScanTally { TypesInspected = 2, TypesWithMembraneLayer = 2 };
            t.RejectedMaterials.Add("Air Infiltration Barrier");

            Assert.Contains("Air Infiltration Barrier", t.Summary());
        }

        [Fact]
        public void Rejections_Are_Still_Named_When_Something_Else_Matched()
        {
            var t = new MembraneScanTally
            { TypesInspected = 2, TypesWithMembraneLayer = 2, TypesMatched = 1 };
            t.RejectedMaterials.Add("Air Infiltration Barrier");

            Assert.Contains("Air Infiltration Barrier", t.Summary());
        }

        [Fact]
        public void No_Membrane_Layers_At_All_Says_The_Model_Is_Silent()
        {
            var t = new MembraneScanTally { TypesInspected = 6, TypesWithMembraneLayer = 0 };

            Assert.Contains("does not describe", t.Summary(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Reset_Clears_Every_Counter()
        {
            var t = new MembraneScanTally
            {
                TypesInspected = 1, TypesWithMembraneLayer = 1, TypesMatched = 1,
                InsulationLayersSeen = 1, WallMembraneLayersSeen = 1
            };
            t.RejectedMaterials.Add("x");
            t.InsulationMaterials.Add("y");

            t.Reset();

            Assert.Null(t.Summary());
            Assert.Empty(t.RejectedMaterials);
            Assert.Empty(t.InsulationMaterials);
        }
    }

    /// <summary>
    /// MATSCHED-T3 — the seam between the membrane code and the three shipped
    /// data files it depends on. Each is valid on its own; only a comparison
    /// catches a commodity that measures in one unit and buys in another, which
    /// fails at runtime as a blocked conversion and a bare square-metre figure.
    /// </summary>
    public class MembraneShippedDataTests
    {
        private static string DataFile(string name) =>
            Path.Combine(AppContext.BaseDirectory, "Data", name);

        private static StageLibrary Stages() =>
            JsonConvert.DeserializeObject<StageLibrary>(File.ReadAllText(DataFile("STING_MATERIAL_STAGES.json")));

        private static SupplierUnitTable Units() =>
            JsonConvert.DeserializeObject<SupplierUnitTable>(File.ReadAllText(DataFile("STING_SUPPLIER_UNITS.json")));

        [Fact]
        public void Both_Membrane_Kinds_Have_A_Supplier_Rule()
        {
            var units = Units();

            Assert.Equal("dpm", units.ResolveByKind("dpm").CommodityKey);
            Assert.Equal("roof-underlay", units.ResolveByKind("roof_underlay").CommodityKey);
        }

        [Fact]
        public void Membranes_Buy_By_The_Unit_They_Are_Measured_In()
        {
            // The unit guard REFUSES to convert on a mismatch, so a rule whose
            // sourceUnit disagreed would silently stop converting and print bare
            // square metres where a roll count belongs.
            var units = Units();

            Assert.Equal("m2", units.ResolveByKind("dpm").SourceUnit);
            Assert.Equal("m2", units.ResolveByKind("roof_underlay").SourceUnit);
        }

        [Fact]
        public void Membranes_Are_Bought_By_The_Whole_Roll_At_A_Real_Roll_Size()
        {
            var units = Units();
            var dpm = units.ResolveByKind("dpm");
            var und = units.ResolveByKind("roof_underlay");

            Assert.Equal("Rolls", dpm.SupplierUnit);
            Assert.Equal("Rolls", und.SupplierUnit);
            Assert.Equal(100.0, dpm.SourceUnitsPerSupplierUnit, 3);   // 4 m x 25 m
            Assert.Equal(45.0, und.SourceUnitsPerSupplierUnit, 3);    // 1 m x 45 m
            Assert.True(dpm.RoundUpToWhole, "you cannot buy 2.3 rolls of DPM");
            Assert.True(und.RoundUpToWhole, "you cannot buy 2.3 rolls of underlay");
        }

        [Fact]
        public void Membranes_Carry_A_Lap_Allowance_In_The_One_Place_Wastage_Lives()
        {
            // A DPM laps 150-300 mm at every joint and turns up at the perimeter.
            // The engine emits NET, so if the allowance is not here it does not
            // exist anywhere - and the order under-buys on every job.
            var units = Units();

            Assert.True(units.ResolveByKind("dpm").DefaultWastagePct >= 10,
                        "a DPM with no lap allowance under-orders on every job");
            Assert.True(units.ResolveByKind("roof_underlay").DefaultWastagePct >= 10,
                        "an underlay with no lap allowance under-orders on every job");
        }

        [Fact]
        public void Both_Membrane_Commodities_Have_A_Baseline_Rate()
        {
            var rates = CommodityRateResolver.ParseCsv(
                File.ReadAllLines(DataFile("STING_COMMODITY_RATES.csv")), out var skipped);
            Assert.Empty(skipped);

            var resolver = new CommodityRateResolver(rates, null);
            var units = Units();

            foreach (string kind in new[] { "dpm", "roof_underlay" })
            {
                var rule = units.ResolveByKind(kind);
                Assert.True(rule != null, "no supplier-unit rule matches kind " + kind);
                Assert.True(resolver.Resolve(rule.CommodityKey).RateUGX > 0,
                            "commodity " + rule.CommodityKey + " has no baseline rate");
            }
        }

        [Fact]
        public void Dpm_Routes_To_Substructure_And_Underlay_To_Roof()
        {
            var lib = Stages();
            var ix = StageIndex.Build(lib.Stages, lib.DefaultStageId);

            // Resolved against categories that route SOMEWHERE ELSE, so these
            // assertions actually exercise the kind route. "Floors" and "Roofs"
            // route to superstructure and roof by category, which would make a
            // same-stage assertion pass whether or not the kind is declared —
            // the blind gate found on the ceiling task.
            Assert.Equal("substructure", ix.Resolve("dpm", "Floors", ""));
            Assert.Equal("roof", ix.Resolve("roof_underlay", "Floors", ""));
        }

        [Fact]
        public void The_Category_Route_Alone_Would_Not_Have_Been_Enough()
        {
            // Pins the premise of the test above: "Floors" really does route to
            // superstructure, so a kind that failed to route would land there.
            var lib = Stages();
            var ix = StageIndex.Build(lib.Stages, lib.DefaultStageId);

            Assert.Equal("superstructure", ix.Resolve("", "Floors", ""));
        }

        [Fact]
        public void Neither_Membrane_Is_An_Intermediate_Measure()
        {
            // A roll of DPM IS what you buy. Declaring it a memorandum would
            // hard-zero a real cost.
            Assert.DoesNotContain(Stages().IntermediateMeasures,
                r => string.Equals(r.Kind, "dpm", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(r.Kind, "roof_underlay", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void No_Insulation_Commodity_Exists_To_Absorb_It_Into()
        {
            // The structural half of the runner's caution. If some rule matched
            // an insulation kind by name, insulation could be quietly converted
            // per m2 despite being bought by thickness. Nothing does, and this
            // fails the day something starts to.
            var units = Units();

            Assert.All(units.Rules, r =>
                Assert.DoesNotContain("insulat", r.CommodityKey, StringComparison.OrdinalIgnoreCase));
            Assert.Null(units.ResolveByKind("insulation"));
        }
    }
}
