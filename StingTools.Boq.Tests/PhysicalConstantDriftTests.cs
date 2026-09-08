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
    /// THE SAME PHYSICAL CONSTANT MUST NOT BE DECLARED IN TWO FILES.
    ///
    /// How many square metres a roofing sheet covers is a fact about the sheet.
    /// It is currently stated in MATERIAL_LOOKUP.csv (read by the formula
    /// engine) AND in STING_SUPPLIER_UNITS.json (read by the material
    /// schedule), and every shared pair DISAGREES:
    ///
    ///     corrugated      2.7  vs 2.4   m2/sheet
    ///     box profile     3.2  vs 0.86
    ///     clay tile       0.04 vs 0.077
    ///     concrete tile   0.03 vs 0.10
    ///
    /// Nobody noticed because the two files are read by different code for
    /// different purposes and never compared. That is the same shape as every
    /// other defect this schedule has produced: two copies that agree until one
    /// is edited, and only one of them is ever edited.
    ///
    /// This gate cannot resolve those numbers — which is right for a product
    /// figure that has to come from a supplier, not from whoever is editing.
    /// What it does is make the disagreement IMPOSSIBLE TO FORGET and any NEW
    /// duplication impossible to add: a pair not on the dated exceptions list
    /// below fails immediately.
    ///
    /// To clear an exception: get the real figure, put it in ONE file, make the
    /// other read from it, and delete the line. The list only shrinks.
    /// </summary>
    public class PhysicalConstantDriftTests
    {
        private static string Data(string f) =>
            Path.Combine(AppContext.BaseDirectory, "Data", f);

        /// <summary>One constant, as two files state it.</summary>
        private sealed class Pair
        {
            public string What = "";
            public string LookupKey = "";     // MATERIAL_LOOKUP name + property
            public string CommodityKey = "";  // STING_SUPPLIER_UNITS rule
            public double Lookup, Supplier;
            public override string ToString() =>
                $"{What}: MATERIAL_LOOKUP '{LookupKey}' = {Lookup:0.####}  vs  "
              + $"SUPPLIER_UNITS '{CommodityKey}' = {Supplier:0.####}";
        }

        /// <summary>
        /// KNOWN, UNRESOLVED disagreements — dated 2026-09-07.
        ///
        /// Each needs a supplier's coverage table, not a judgement call: the
        /// difference between 2.4 and 2.7 m2 per sheet is 11% of a roof order,
        /// and picking silently is how a guess becomes a standard.
        /// </summary>
        private static readonly HashSet<string> KnownUnresolved = new HashSet<string>
        {
            "ROOF_SHEET CORRUGATED COVERAGE_M2|roof-sheet",
            "ROOF_SHEET BOX_PROFILE COVERAGE_M2|roof-sheet-boxprofile",
            "ROOF_SHEET CLAY_TILE COVERAGE_M2|roof-tile-clay",
            "ROOF_SHEET CONCRETE_TILE COVERAGE_M2|roof-tile-concrete",
            "ROOF_SHEET DEFAULT COVERAGE_M2|roof-tile",
        };

        /// <summary>Which supplier commodity states the same cover as which lookup row.</summary>
        private static readonly (string lookupName, string property, string commodity)[] SharedConstants =
        {
            ("ROOF_SHEET CORRUGATED",    "COVERAGE_M2", "roof-sheet"),
            ("ROOF_SHEET BOX_PROFILE",   "COVERAGE_M2", "roof-sheet-boxprofile"),
            ("ROOF_SHEET CLAY_TILE",     "COVERAGE_M2", "roof-tile-clay"),
            ("ROOF_SHEET CONCRETE_TILE", "COVERAGE_M2", "roof-tile-concrete"),
            ("ROOF_SHEET DEFAULT",       "COVERAGE_M2", "roof-tile"),
        };

        // ── reading the two files ───────────────────────────────────────────

        /// <summary>
        /// MATERIAL_LOOKUP variation rows: NAME,TYPEKEY,PROPERTY,VALUE,UNIT,NOTE.
        /// Parsed here rather than through MaterialLookupCsv because that reaches
        /// for StingToolsApp and a Revit-free gate must not.
        /// </summary>
        private static Dictionary<string, double> LookupConstants()
        {
            var d = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in File.ReadAllLines(Data("MATERIAL_LOOKUP.csv")))
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var p = line.Split(',');
                if (p.Length < 4) continue;
                if (!double.TryParse(p[3].Trim(), System.Globalization.NumberStyles.Any,
                                     System.Globalization.CultureInfo.InvariantCulture, out double v))
                    continue;
                d[$"{p[0].Trim()} {p[1].Trim()} {p[2].Trim()}"] = v;
            }
            return d;
        }

        private static SupplierUnitTable Supplier() =>
            JsonConvert.DeserializeObject<SupplierUnitTable>(
                File.ReadAllText(Data("STING_SUPPLIER_UNITS.json")));

        private static List<Pair> Disagreements()
        {
            var lookup = LookupConstants();
            var units = Supplier();
            var found = new List<Pair>();

            foreach (var (name, prop, commodity) in SharedConstants)
            {
                if (!lookup.TryGetValue($"{name} {prop}", out double l)) continue;
                var rule = units.ResolveByCommodityKey(commodity);
                if (rule == null) continue;

                double s = rule.SourceUnitsPerSupplierUnit;
                if (Math.Abs(l - s) < 0.0005) continue;

                found.Add(new Pair
                {
                    What = prop, LookupKey = $"{name} {prop}", CommodityKey = commodity,
                    Lookup = l, Supplier = s
                });
            }
            return found;
        }

        // ── the gate ────────────────────────────────────────────────────────

        [Fact]
        public void No_UNRECORDED_Constant_Is_Declared_Differently_In_Two_Files()
        {
            var surprises = Disagreements()
                .Where(p => !KnownUnresolved.Contains($"{p.LookupKey}|{p.CommodityKey}"))
                .ToList();

            Assert.True(surprises.Count == 0,
                "A physical constant is stated differently in two shipped data files, and this pair "
              + "is NOT on the dated exceptions list — so it is new.\n\n"
              + string.Join("\n", surprises.Select(p => "  " + p))
              + "\n\nA constant belongs in ONE file. Put the real figure there, make the other read "
              + "from it, and do not add to KnownUnresolved: that list is for the pairs that predate "
              + "this gate and it is only allowed to shrink.");
        }

        [Fact]
        public void Every_Recorded_Disagreement_Still_Exists()
        {
            // The list only shrinks by RECONCILING, never by rotting. A stale
            // entry would silently permit a future re-divergence of that pair.
            var live = Disagreements()
                .Select(p => $"{p.LookupKey}|{p.CommodityKey}").ToHashSet();

            var stale = KnownUnresolved.Where(k => !live.Contains(k)).ToList();

            Assert.True(stale.Count == 0,
                "These pairs are on the unresolved list but no longer disagree:\n  "
              + string.Join("\n  ", stale)
              + "\n\nIf they were reconciled, delete them from KnownUnresolved — leaving them there "
              + "would let that constant diverge again without this gate noticing.");
        }

        [Fact]
        public void The_Exceptions_List_Is_Not_Empty_Only_Because_Nobody_Looked()
        {
            // Documents the count at the time the gate was written. If it drops,
            // somebody reconciled one and should delete its line; if it climbs,
            // the first test has already failed.
            Assert.Equal(5, KnownUnresolved.Count);
        }

        // ── the one that is wrong by KIND, not by degree ────────────────────

        [Fact]
        public void Tiles_Are_Nailed_So_The_Lookup_Gives_Them_ZERO_Screws()
        {
            // MATERIAL_LOOKUP models fastener density per profile and puts clay
            // and concrete tile at 0 — they are nailed every other course, not
            // screwed. STING_CONSUMABLES applies a flat 11/m2 to every covering,
            // so a tiled roof is currently quoted screws it does not use. That
            // is wrong by kind rather than by degree, and unlike the coverage
            // figures it needs no supplier to settle.
            var lookup = LookupConstants();

            Assert.Equal(0, lookup["ROOF_SHEET CLAY_TILE FASTENERS_PER_M2"]);
            Assert.Equal(0, lookup["ROOF_SHEET CONCRETE_TILE FASTENERS_PER_M2"]);
            Assert.True(lookup["ROOF_SHEET CORRUGATED FASTENERS_PER_M2"] > 0);
        }

        [Fact]
        public void The_Lookup_Models_Fastener_Density_Per_PROFILE()
        {
            // Four profiles, four densities. A single flat figure cannot be
            // right for all of them, which is the argument for moving the ratio
            // onto the covering rule rather than keeping it in one consumable.
            var lookup = LookupConstants();
            var densities = new[] { "CORRUGATED", "BOX_PROFILE", "STANDING_SEAM", "FIBRE_CEMENT" }
                .Select(p => lookup[$"ROOF_SHEET {p} FASTENERS_PER_M2"])
                .ToList();

            Assert.Equal(4, densities.Count);
            Assert.True(densities.Distinct().Count() > 1,
                        "if every profile had the same density, a flat figure would be defensible");
        }
    }
}
