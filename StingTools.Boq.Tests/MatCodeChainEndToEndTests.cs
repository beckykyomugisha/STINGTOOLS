// ══════════════════════════════════════════════════════════════════════════
//  MatCodeChainEndToEndTests.cs — W4. One row, register to rate.
//
//      register row FLR-028   ->  a material carrying MAT_CODE = FLR-028
//                             ->  a floor type whose PRIMARY layer is that material
//                             ->  the BOQ resolves MatCode = "FLR-028"
//                             ->  CsvRateProvider Pass 3 matches
//                             ->  Provenance "<rate file> MAT_CODE match"
//
//  THE ASSERTION IS THE PROVENANCE STRING, not the number. A rate that happens
//  to be right for another reason is not this chain working — and on the shipped
//  rate table every one of these categories has a Pass 4 category row, so a test
//  that checked only the figure would pass on a category average and prove
//  nothing. That is exactly the shape this codebase produces.
//
//  WHAT THIS WOULD HAVE CAUGHT. Every link here existed and was wired four
//  phases ago; MAT_CODE was bound to Materials only, the BOQ read it off a wall,
//  and Pass 3 has never once fired. Nothing failed. This test fails.
//
//  The rate table used is a SYNTHETIC one keyed on FLR-028, because the shipped
//  cost_rates_5d.csv shares no key with the register — see
//  MaterialCodeResolutionTests.The_Shipped_Rate_Table_Shares_No_Key_With_The_
//  Register, which pins that and is why W2's measured rate delta is zero. The
//  chain is proven here on the rate card a project would have to write; the
//  reason it does not fire on shipped data is proven there. Both are true and
//  they are different facts.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.BOQ.Rates;
using StingTools.Core;
using StingTools.Core.Materials;
using Xunit;
using Xunit.Abstractions;

namespace StingTools.Boq.Tests
{
    public class MatCodeChainEndToEndTests
    {
        private readonly ITestOutputHelper _out;
        public MatCodeChainEndToEndTests(ITestOutputHelper output) => _out = output;

        private const string RateFile = "project_rate_card.csv";

        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "BLE_MATERIALS.csv")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data from " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, "StingTools", "Data");
        }

        /// <summary>MAT_NAME and MAT_CODE for FLR-028, read from the SHIPPED register.
        /// Not restated here: a copy that drifted from the file would pass while the
        /// thing it describes was wrong.</summary>
        private static (string name, string code) Flr028()
        {
            foreach (string f in new[] { "BLE_MATERIALS.csv", "MEP_MATERIALS.csv" })
            {
                var lines = File.ReadAllLines(Path.Combine(DataDir(), f));
                int h = Array.FindIndex(lines, l => l.ToUpperInvariant().Contains("MAT_CODE"));
                if (h < 0) continue;
                var hdr = Split(lines[h]);
                int ci = hdr.FindIndex(c => c.Trim().Equals("MAT_CODE", StringComparison.OrdinalIgnoreCase));
                int ni = hdr.FindIndex(c => c.Trim().Equals("MAT_NAME", StringComparison.OrdinalIgnoreCase));
                for (int i = h + 1; i < lines.Length; i++)
                {
                    var c = Split(lines[i]);
                    if (c.Count > Math.Max(ci, ni)
                        && c[ci].Trim().Equals("FLR-028", StringComparison.OrdinalIgnoreCase))
                        return (c[ni].Trim(), c[ci].Trim());
                }
            }
            Assert.Fail("FLR-028 is not in the shipped register");
            return (null, null);
        }

        private static List<string> Split(string line)
        {
            var fields = new List<string>();
            var cur = new System.Text.StringBuilder();
            bool q = false;
            foreach (char ch in line ?? "")
            {
                if (q) { if (ch == '"') q = false; else cur.Append(ch); }
                else if (ch == '"' && cur.Length == 0) q = true;
                else if (ch == ',') { fields.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(ch);
            }
            fields.Add(cur.ToString());
            return fields;
        }

        /// <summary>A project rate card that keys BOTH the category and FLR-028, at
        /// DIFFERENT rates. The difference is what makes the assertion meaningful: if
        /// Pass 3 does not fire, Pass 4 answers with the other number.</summary>
        private static Dictionary<string, (double rate, string unit)> RateCard()
            => new Dictionary<string, (double rate, string unit)>(StringComparer.OrdinalIgnoreCase)
            {
                ["Floors"]   = (444000, "m2"),   // Pass 4 — the category average
                ["FLR-028"]  = (612500, "m2"),   // Pass 3 — this material specifically
            };

        // ══════════════════════════════════════════════════════════════════════
        //  The chain
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void FLR_028_Walks_From_The_Register_To_A_Rate()
        {
            var (matName, code) = Flr028();
            Assert.Equal("FLR-028", code);
            Assert.False(string.IsNullOrWhiteSpace(matName));

            // LINK 1 — the material carries the register's code. This is what W1's
            // stamp writes and what Materials_StampCodes backfills; here it is the
            // name→code map the resolver is handed.
            var codeByMaterial = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [matName] = code,
                ["Gypsum Wall Board"] = "WL-999",   // a skin, deliberately coded
            };

            // LINK 2 — a floor type whose PRIMARY layer is that material. The skin is
            // listed first and is thinner, which is how a finish came to name a roof.
            var layers = new List<MaterialLayer>
            {
                new MaterialLayer { Index = 0, MaterialName = "Gypsum Wall Board", ThicknessMm = 12.5, IsStructure = false },
                new MaterialLayer { Index = 1, MaterialName = matName,             ThicknessMm = 150,  IsStructure = true  },
            };

            // LINK 3 — what BOQCostManager now puts in RateRequest.MatCode. This is the
            // same call both BOQ sites and CostStamp make.
            var resolved = MaterialCodeResolution.Resolve(layers, codeByMaterial, null, "");
            Assert.Equal("FLR-028", resolved.Code);
            Assert.Equal(MatCodeSource.PrimaryLayerMaterial, resolved.Source);
            Assert.Equal(matName, resolved.MaterialName);

            // LINK 4 — the rate chain. Same code CsvRateProvider runs.
            var m = CsvRateLookup.Resolve(RateCard(), RateFile,
                        categoryName: "Floors", discipline: "A", prodCode: "",
                        systemType: "", matCode: resolved.Code);

            Assert.NotNull(m);

            // THE ASSERTION. Not the number — the reason.
            Assert.Equal(RateFile + " MAT_CODE match", m.Provenance);
            Assert.Equal("FLR-028", m.MatchedKey);
            Assert.Equal(RateResolutionLevel.Material, m.Level);
            Assert.Equal(85, m.Confidence);
            Assert.Equal(612500, m.UnitRate);
            Assert.Equal("m2", m.Unit);

            _out.WriteLine("FLR-028  " + matName);
            _out.WriteLine("  -> " + resolved);
            _out.WriteLine("  -> " + m.Provenance + " @ " + m.UnitRate + " UGX/" + m.Unit);
        }

        [Fact]
        public void Without_The_Code_The_Same_Element_Prices_Off_The_Category_Average()
        {
            // The world before W2, in one assertion: MatCode empty, so Pass 3 is skipped
            // and Pass 4 answers. Same element, same rate card, a DIFFERENT number and a
            // different sentence — which is why the sentence is what gets asserted.
            var m = CsvRateLookup.Resolve(RateCard(), RateFile,
                        categoryName: "Floors", discipline: "A", prodCode: "",
                        systemType: "", matCode: "");

            Assert.NotNull(m);
            Assert.Equal(RateFile + " category average (Floors)", m.Provenance);
            Assert.Equal(RateResolutionLevel.Category, m.Level);
            Assert.Equal(70, m.Confidence);
            Assert.Equal(444000, m.UnitRate);
        }

        [Fact]
        public void The_Skin_Never_Prices_The_Floor()
        {
            // The failure the chain must not have: the finish layer is coded WL-999 and
            // listed first. If the resolver ever returns it, this floor prices as
            // plasterboard and the provenance still reads "MAT_CODE match" — a confident
            // wrong answer at confidence 85, which is worse than the empty one.
            var (matName, _) = Flr028();
            var card = RateCard();
            card["WL-999"] = (35000, "m2");

            var layers = new List<MaterialLayer>
            {
                new MaterialLayer { Index = 0, MaterialName = "Gypsum Wall Board", ThicknessMm = 12.5, IsStructure = false },
                new MaterialLayer { Index = 1, MaterialName = matName,             ThicknessMm = 150,  IsStructure = true  },
            };
            var codeByMaterial = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [matName] = "FLR-028",
                ["Gypsum Wall Board"] = "WL-999",
            };

            var resolved = MaterialCodeResolution.Resolve(layers, codeByMaterial, "Gypsum Wall Board", "");
            var m = CsvRateLookup.Resolve(card, RateFile, "Floors", "A", "", "", resolved.Code);

            Assert.Equal("FLR-028", m.MatchedKey);
            Assert.NotEqual(35000, m.UnitRate);
        }

        [Fact]
        public void A_More_Specific_Pass_Still_Outranks_The_Material()
        {
            // Pass 3 sits BELOW the product passes on purpose. A DISC|PROD row must still
            // win, or W2 would have quietly demoted the most specific key in the chain.
            var card = RateCard();
            card["A|FL"] = (700000, "m2");

            var m = CsvRateLookup.Resolve(card, RateFile,
                        categoryName: "Floors", discipline: "A", prodCode: "FL",
                        systemType: "", matCode: "FLR-028");

            Assert.Equal(RateFile + " product match (A|FL)", m.Provenance);
            Assert.Equal(97, m.Confidence);
            Assert.Equal(RateResolutionLevel.Product, m.Level);
        }

        [Fact]
        public void A_Code_The_Rate_Card_Does_Not_Know_Falls_Through_Rather_Than_Guessing()
        {
            var m = CsvRateLookup.Resolve(RateCard(), RateFile,
                        categoryName: "Floors", discipline: "A", prodCode: "",
                        systemType: "", matCode: "FLR-999");

            // Falls to the category average, and SAYS so. It does not invent a material
            // rate, and it does not report a category rate as a material match.
            Assert.Equal(RateFile + " category average (Floors)", m.Provenance);
            Assert.Equal(RateResolutionLevel.Category, m.Level);
        }

        [Fact]
        public void An_Empty_Rate_Card_Answers_Nothing_Rather_Than_Zero()
        {
            Assert.Null(CsvRateLookup.Resolve(
                new Dictionary<string, (double, string)>(), RateFile, "Floors", "A", "", "", "FLR-028"));
            Assert.Null(CsvRateLookup.Resolve(null, RateFile, "Floors", "A", "", "", "FLR-028"));
        }

        [Fact]
        public void The_Pass_Order_Is_Specificity_Order()
        {
            // K-16b found category consulted first and RETURNING, which made the product
            // pass dead and priced a fire door and a cupboard door alike. Asserted as an
            // order rather than as five separate cases, so a reordering fails here.
            var card = new Dictionary<string, (double rate, string unit)>(StringComparer.OrdinalIgnoreCase)
            {
                ["A|FL"] = (5, "m2"), ["FL"] = (4, "m2"), ["Floors|Podium"] = (3, "m2"),
                ["FLR-028"] = (2, "m2"), ["Floors"] = (1, "m2"),
            };
            var expected = new[]
            {
                (RateResolutionLevel.Product,  97, 5.0),
                (RateResolutionLevel.Product,  95, 4.0),
                (RateResolutionLevel.System,   92, 3.0),
                (RateResolutionLevel.Material, 85, 2.0),
                (RateResolutionLevel.Category, 70, 1.0),
            };

            // Strip the winning key one at a time; the next-most-specific must answer.
            foreach (var (level, conf, rate) in expected)
            {
                var m = CsvRateLookup.Resolve(card, RateFile, "Floors", "A", "FL", "Podium", "FLR-028");
                Assert.Equal(level, m.Level);
                Assert.Equal(conf, m.Confidence);
                Assert.Equal(rate, m.UnitRate);
                card.Remove(m.MatchedKey);
            }
            Assert.Empty(card);
        }
    }
}
