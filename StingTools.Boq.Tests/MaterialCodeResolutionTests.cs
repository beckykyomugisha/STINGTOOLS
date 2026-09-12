// ══════════════════════════════════════════════════════════════════════════
//  MaterialCodeResolutionTests.cs — the gate on W2.
//
//  W2 changes what `RateRequest.MatCode` contains, which is what the BOQ's
//  most specific rate lookup keys on. Two things therefore need proving, and
//  they are different questions:
//
//    1. THE DECISION IS RIGHT. Layers in, code out, with the core layer
//       answering and never the finish skin. Tested here against a plain layer
//       list and a name→code map, no Revit.
//
//    2. WHAT IT ACTUALLY MOVES. Measured against the SHIPPED rate table, and
//       the answer today is NOTHING — see The_Shipped_Rate_Table_Shares_No_Key.
//       That test exists because a rate change nobody counted is the failure
//       mode this codebase produces, and because "Pass 3 can now fire" and
//       "Pass 3 now fires" are not the same sentence.
//
//  RED / GREEN, recorded 2026-09-10 on this machine:
//
//    A_Layered_Element_Answers_With_Its_CORE
//      RED   (resolution reads the first layer instead of the selector)
//            FLR-SKIN — the finish, which is the #873 defect exactly
//      GREEN FLR-CORE
//
//    An_Element_With_No_Coded_Material_Stays_Empty
//      RED   (empty falls through to the first material's code)  FLR-SKIN
//      GREEN ""
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.Core;
using StingTools.Core.Materials;
using Xunit;

namespace StingTools.Boq.Tests
{
    public class MaterialCodeResolutionTests
    {
        private static Dictionary<string, string> Map(params (string name, string code)[] pairs)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (n, c) in pairs) d[n] = c;
            return d;
        }

        /// <summary>A rendered masonry wall: thin gypsum skin listed FIRST, thick
        /// structural brick core second. The shape that produced #873.</summary>
        private static List<MaterialLayer> RenderedMasonryWall() => new List<MaterialLayer>
        {
            new MaterialLayer { Index = 0, MaterialName = "GYPSUM PLASTER 15MM", ThicknessMm = 15,  IsStructure = false },
            new MaterialLayer { Index = 1, MaterialName = "CLAY BRICK 200MM",    ThicknessMm = 200, IsStructure = true  },
            new MaterialLayer { Index = 2, MaterialName = "GYPSUM PLASTER 15MM", ThicknessMm = 15,  IsStructure = false },
        };

        // ══════════════════════════════════════════════════════════════════════
        //  1. The core answers, not the skin
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void A_Layered_Element_Answers_With_Its_CORE()
        {
            var r = MaterialCodeResolution.Resolve(
                RenderedMasonryWall(),
                Map(("GYPSUM PLASTER 15MM", "WL-SKIN"), ("CLAY BRICK 200MM", "WL-CORE")),
                singleMaterialName: "GYPSUM PLASTER 15MM",
                elementOwnCode: "");

            Assert.Equal("WL-CORE", r.Code);
            Assert.Equal(MatCodeSource.PrimaryLayerMaterial, r.Source);
            Assert.Equal("CLAY BRICK 200MM", r.MaterialName);
        }

        [Fact]
        public void A_Layered_Element_Whose_Core_Has_No_Code_Does_Not_Fall_Back_To_The_Skin()
        {
            // The whole point. Falling through here would hand back the finish
            // material's code at confidence 85 — a confident wrong rate, which is
            // worse than the empty one this replaces.
            var r = MaterialCodeResolution.Resolve(
                RenderedMasonryWall(),
                Map(("GYPSUM PLASTER 15MM", "WL-SKIN")),   // core has NO code
                singleMaterialName: "GYPSUM PLASTER 15MM",
                elementOwnCode: "");

            Assert.Equal("", r.Code);
            Assert.False(r.Resolved);
            Assert.Equal(MatCodeSource.None, r.Source);
        }

        [Fact]
        public void The_Thickest_Structural_Layer_Wins_Over_A_Thicker_Non_Structural_One()
        {
            var layers = new List<MaterialLayer>
            {
                new MaterialLayer { Index = 0, MaterialName = "SCREED",   ThicknessMm = 300, IsStructure = false },
                new MaterialLayer { Index = 1, MaterialName = "CONCRETE", ThicknessMm = 100, IsStructure = true  },
            };
            var r = MaterialCodeResolution.Resolve(layers,
                Map(("SCREED", "FLR-SCR"), ("CONCRETE", "FLR-RC")), null, "");

            Assert.Equal("FLR-RC", r.Code);   // PrimaryMaterialSelector's rule, unchanged
        }

        // ══════════════════════════════════════════════════════════════════════
        //  2. Elements with no compound structure
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void An_Element_With_No_Layers_Uses_Its_Single_Material()
        {
            var r = MaterialCodeResolution.Resolve(
                layers: null,
                codeByMaterialName: Map(("COPPER PIPE 22MM", "PIPE-014")),
                singleMaterialName: "COPPER PIPE 22MM",
                elementOwnCode: "");

            Assert.Equal("PIPE-014", r.Code);
            Assert.Equal(MatCodeSource.SingleMaterial, r.Source);
        }

        [Fact]
        public void An_Element_With_Nothing_At_All_Resolves_Empty()
        {
            var r = MaterialCodeResolution.Resolve(null, Map(), null, "");
            Assert.Equal("", r.Code);
            Assert.False(r.Resolved);
            Assert.Equal(MatCodeSource.None, r.Source);
        }

        [Fact]
        public void Layers_That_Name_No_Material_Are_The_Same_As_No_Layers()
        {
            var blank = new List<MaterialLayer>
            {
                new MaterialLayer { Index = 0, MaterialName = "",   ThicknessMm = 100, IsStructure = true },
                new MaterialLayer { Index = 1, MaterialName = null, ThicknessMm = 50,  IsStructure = false },
            };
            var r = MaterialCodeResolution.Resolve(blank,
                Map(("STEEL DECK", "STR-009")), "STEEL DECK", "");

            Assert.Equal("STR-009", r.Code);
            Assert.Equal(MatCodeSource.SingleMaterial, r.Source);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  3. The element's own MAT_CODE — a fallback, not an invitation
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void The_Elements_Own_Code_Is_The_Last_Resort_Not_The_First()
        {
            var r = MaterialCodeResolution.Resolve(
                RenderedMasonryWall(),
                Map(("CLAY BRICK 200MM", "WL-CORE")),
                singleMaterialName: null,
                elementOwnCode: "WL-STAMPED-ON-INSTANCE");

            // The material wins. Binding MAT_CODE to host categories was considered
            // and refused; this fallback exists so that day needs no second visit,
            // not so an instance stamp can outrank the register row.
            Assert.Equal("WL-CORE", r.Code);
            Assert.Equal(MatCodeSource.PrimaryLayerMaterial, r.Source);
        }

        [Fact]
        public void The_Elements_Own_Code_Answers_When_No_Material_Does()
        {
            var r = MaterialCodeResolution.Resolve(null, Map(), null, "  WL-042  ");
            Assert.Equal("WL-042", r.Code);
            Assert.Equal(MatCodeSource.ElementParameter, r.Source);
        }

        [Fact]
        public void Whitespace_Is_Not_A_Code()
        {
            var r = MaterialCodeResolution.Resolve(null, Map(), null, "   ");
            Assert.False(r.Resolved);
            Assert.Equal(MatCodeSource.None, r.Source);
        }

        [Fact]
        public void A_Map_Entry_That_Is_Blank_Is_Not_A_Code()
        {
            // A material bound to MAT_CODE but never stamped reads as "". That must be
            // "no code", not a code of length zero that satisfies a null check upstream.
            var r = MaterialCodeResolution.Resolve(
                RenderedMasonryWall(), Map(("CLAY BRICK 200MM", "")), null, "");
            Assert.False(r.Resolved);
        }

        [Fact]
        public void Material_Names_Match_Case_Insensitively_And_Trimmed()
        {
            var layers = new List<MaterialLayer>
            {
                new MaterialLayer { Index = 0, MaterialName = "  clay brick 200mm  ", ThicknessMm = 200, IsStructure = true },
            };
            var r = MaterialCodeResolution.Resolve(layers, Map(("CLAY BRICK 200MM", "WL-CORE")), null, "");
            Assert.Equal("WL-CORE", r.Code);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  4. WHAT THIS ACTUALLY MOVES — measured, not assumed
        // ══════════════════════════════════════════════════════════════════════

        private static DirectoryInfo RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "cost_rates_5d.csv")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data from " + AppContext.BaseDirectory);
            return dir;
        }

        private static string Data(string file)
            => Path.Combine(RepoRoot().FullName, "StingTools", "Data", file);

        /// <summary>Every MAT_CODE the governed register issues.</summary>
        private static HashSet<string> RegisterCodes()
        {
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in new[] { "BLE_MATERIALS.csv", "MEP_MATERIALS.csv" })
            {
                var lines = File.ReadAllLines(Data(f));
                int hdr = Array.FindIndex(lines, l => l.ToUpperInvariant().Contains("MAT_CODE"));
                Assert.True(hdr >= 0, f + " has no MAT_CODE header");
                var cols = Split(lines[hdr]);
                int ci = cols.FindIndex(c => c.Trim().Equals("MAT_CODE", StringComparison.OrdinalIgnoreCase));
                for (int i = hdr + 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]) || lines[i].TrimStart().StartsWith("#")) continue;
                    var f2 = Split(lines[i]);
                    if (f2.Count > ci && f2[ci].Trim().Length > 0) codes.Add(f2[ci].Trim());
                }
            }
            return codes;
        }

        /// <summary>Every key LoadCsvRatesUncached would register from the shipped
        /// 8-column rate table — DISC|PROD, Category and the MAT_CODE column.</summary>
        private static HashSet<string> RateTableKeys()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lines = File.ReadAllLines(Data("cost_rates_5d.csv"));
            for (int i = 1; i < lines.Length; i++)
            {
                var c = Split(lines[i]);
                if (c.Count < 8) continue;
                if (c[1].Trim().Length > 0 && c[3].Trim().Length > 0) keys.Add(c[3].Trim() + "|" + c[1].Trim());
                if (c[0].Trim().Length > 0) keys.Add(c[0].Trim());
                if (c[2].Trim().Length > 0) keys.Add(c[2].Trim());
            }
            return keys;
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

        [Fact]
        public void The_Shipped_Rate_Table_Shares_No_Key_With_The_Register()
        {
            // ── THE RATE DELTA W2 CAUSES, ON SHIPPED CORPORATE DATA: ZERO. ──
            //
            // The register issues 1,279 codes shaped FLR-028 / CLG-052 / WL-128.
            // cost_rates_5d.csv has a column CALLED MAT_CODE holding 43 three-letter
            // abbreviations — FLR, CLG, WAL, AHU — which are PROD-shaped, not register
            // codes. The intersection is EMPTY, so Pass 3 still cannot match against
            // the shipped table however well MatCode is now resolved.
            //
            // That is the same defect the brief found, one file over: a lookup wired
            // end to end whose two halves speak different vocabularies. It is pinned
            // here rather than fixed because changing 43 rate keys IS a cost change and
            // needs its own evidence — and because the day somebody adds register codes
            // to a rate card, this test must fail and make them count what moved.
            var codes = RegisterCodes();
            var keys = RateTableKeys();

            Assert.Equal(1279, codes.Count);
            var shared = codes.Where(keys.Contains).OrderBy(x => x).ToList();

            Assert.True(shared.Count == 0,
                "Register codes are now rate-table keys, so Pass 3 (MATERIAL, confidence 85) "
              + "will fire where it never has. This is a RATE CHANGE. Count what moved, then "
              + "update this test:\n  " + string.Join("\n  ", shared.Take(20)));
        }

        [Fact]
        public void The_Rate_Tables_MAT_CODE_Column_Is_Prod_Shaped_Not_Register_Shaped()
        {
            // Stated as a fact about the data so the reason the delta is zero is
            // readable, and so a later cleanup of either file is a visible event.
            var lines = File.ReadAllLines(Data("cost_rates_5d.csv"));
            var col = new List<string>();
            for (int i = 1; i < lines.Length; i++)
            {
                var c = Split(lines[i]);
                if (c.Count >= 8 && c[2].Trim().Length > 0) col.Add(c[2].Trim());
            }
            Assert.NotEmpty(col);

            // Register codes all carry a hyphen-and-number tail. None of these do.
            Assert.All(col, v => Assert.DoesNotContain("-", v));
            Assert.All(RegisterCodes().Take(50), v => Assert.Contains("-", v));
        }
    }
}
