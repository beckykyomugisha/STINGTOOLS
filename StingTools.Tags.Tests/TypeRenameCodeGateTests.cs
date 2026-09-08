// ══════════════════════════════════════════════════════════════════════════
//  TypeRenameCodeGateTests.cs — a rename must not write a WORSE product code
//  into the one field the resolver trusts above every rule.
//
//  WHY. `ProdResolver` puts a code DECLARED in the type name above the corporate
//  pattern rules, deliberately: a name that states its answer beats an inference.
//  `TypeRenamePlanner` composes exactly such a name. So a rename is not a
//  cosmetic act — it OVERWRITES the classification, permanently and silently.
//
//  Two rows from a real run on 2026-09-08 prove the failure. Both resolve
//  CORRECTLY today, from a corporate rule, and the rename plan from the same
//  session proposed to change them:
//
//      Floors,Floor,stepsr 7,FSP-CON,corporate,Y   → PLNS_SLB_RC450-Tiled   FSP → SLB
//      Walls,Basic Wall,coping,WCP-CON,corporate,Y → PLNS_WRC_RC250         WCP → WRC
//
//  `CodeFor` has no steps entry for Floors and no coping entry for Walls, so the
//  planner reached for the nearest thing it knew. **It cannot have those entries**:
//  `CodeFor` is keyed on the SUBSTANCE read off the core material, and "steps" and
//  "coping" are what an element IS FOR, not what it is made of — a concrete step
//  and a concrete slab have the same core material and must not have the same
//  code. Adding words to the table cannot fix this class of defect, which is why
//  the fix is a refusal.
//
//  THE RULE, and the line it draws:
//
//      A rename is refused when the proposed name would declare a DIFFERENT code
//      from one the type already resolves to SPECIFICALLY — from a project or
//      corporate rule, an LPS or sleeve special case, or a code already declared
//      in its name.
//
//      It is NOT refused when the current code is the CATEGORY DEFAULT (WL, FL,
//      RF, CLG). That is the absence of an answer, and replacing it is the entire
//      purpose of the command.
//
//  That distinction is load-bearing, and the fixture pins both sides of it:
//  `CLAY BRICK VILLAGE LARGE` resolves WBK from a corporate rule and the proposal
//  declares WBK, so it stands; `Exterior_CreamWhite_230 2` resolves WL from the
//  category default and the proposal declares WBK, so it stands too. A gate that
//  refused every code change would refuse both — and with them nearly every
//  rename the command exists to make.
//
//  The fixture is the two real CSVs from that session, joined: the coverage audit
//  (what each type resolves to, and how) and the rename plan (what was proposed,
//  and off which core material). The core material and thickness reproduce each
//  recorded proposal exactly, which the test asserts before it asserts anything
//  else — a reconstruction that did not reproduce the run would make every
//  verdict below meaningless.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Baseline;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class TypeRenameCodeGateTests
    {
        // ══════════════════════════════════════════════════════════════════════
        //  The resolver, wired exactly as the plugin wires it
        // ══════════════════════════════════════════════════════════════════════

        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_PROD_CODES.csv")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data from " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, "StingTools", "Data");
        }

        private static string FixtureDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(
                       dir.FullName, "StingTools.Tags.Tests", "Fixtures", "host_types_20260908.csv")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate the host-type fixture from " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, "StingTools.Tags.Tests", "Fixtures");
        }

        private static Dictionary<string, List<(string Pattern, string ProdCode)>> _corp;
        private static HashSet<string> _codes;

        private static void LoadRules()
        {
            if (_corp != null) return;
            var corp = new Dictionary<string, List<(string, string)>>(StringComparer.OrdinalIgnoreCase);
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in File.ReadAllLines(Path.Combine(DataDir(), "STING_PROD_CODES.csv")).Skip(1))
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var c = line.Split(',');
                if (c.Length < 3) continue;
                string code = c[0].Trim(), cat = c[1].Trim(), pat = c[2].Trim().ToUpperInvariant();
                if (code.Length == 0) continue;
                codes.Add(code);
                if (cat.Length == 0 || pat.Length == 0) continue;
                if (!corp.TryGetValue(cat, out var list)) corp[cat] = list = new List<(string, string)>();
                list.Add((pat, code));
            }
            Assert.True(codes.Count > 100, "STING_PROD_CODES.csv looks empty: " + codes.Count);
            _codes = codes;
            _corp = corp;
        }

        /// <summary>
        /// The category defaults, copied from <c>TagConfig.DefaultProdMap</c> for the four
        /// layered host categories. These are the codes that mean "nobody has classified
        /// this", and telling them apart from a real answer is the whole gate.
        /// </summary>
        private static readonly Dictionary<string, string> CategoryDefault =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Walls"] = "WL", ["Floors"] = "FL", ["Roofs"] = "RF", ["Ceilings"] = "CLG",
            };

        private static ExistingProdCode Resolve(TypeRenameInput input)
        {
            LoadRules();
            _corp.TryGetValue(input.Category ?? "", out var corpForCat);
            string code = ProdResolver.Resolve(
                input.FamilyName, input.CurrentName, input.Category,
                null, corpForCat, CategoryDefault, out string source, _codes);
            return new ExistingProdCode
            {
                Code = code,
                Source = source,
                IsSpecific = ProdResolver.IsSpecific(source),
            };
        }

        // ══════════════════════════════════════════════════════════════════════
        //  The fixture
        // ══════════════════════════════════════════════════════════════════════

        private sealed class Row
        {
            public string Category, Family, CurrentName, ExistingProd, ExistingSource,
                          CoreMaterial, FinishMaterial, RecordedProposal;
            public double CoreThicknessMm;
            public bool MustPropose;

            public TypeRenameInput ToInput()
            {
                var layers = new List<MaterialLayer>
                {
                    new MaterialLayer
                    {
                        Index = 0, MaterialName = CoreMaterial,
                        ThicknessMm = CoreThicknessMm, IsStructure = true,
                    },
                };
                if (!string.IsNullOrWhiteSpace(FinishMaterial))
                    layers.Add(new MaterialLayer
                    {
                        Index = 1, MaterialName = FinishMaterial, ThicknessMm = 12, IsStructure = false,
                    });
                return new TypeRenameInput
                {
                    Category = Category, FamilyName = Family, CurrentName = CurrentName,
                    Layers = layers, InstanceCount = 5,
                };
            }

            public override string ToString() => $"{Category}/{CurrentName}";
        }

        private static List<Row> Fixture()
        {
            var rows = new List<Row>();
            foreach (string line in File.ReadAllLines(
                         Path.Combine(FixtureDir(), "host_types_20260908.csv"), Encoding.UTF8).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var f = SplitCsv(line);
                Assert.True(f.Count == 10, "Malformed fixture line: " + line);
                rows.Add(new Row
                {
                    Category = f[0], Family = f[1], CurrentName = f[2],
                    ExistingProd = f[3], ExistingSource = f[4],
                    CoreMaterial = f[5],
                    CoreThicknessMm = double.Parse(f[6], System.Globalization.CultureInfo.InvariantCulture),
                    FinishMaterial = f[7], RecordedProposal = f[8],
                    MustPropose = f[9].Trim().Equals("yes", StringComparison.OrdinalIgnoreCase),
                });
            }
            Assert.Equal(6, rows.Count);
            return rows;
        }

        private static List<string> SplitCsv(string line)
        {
            var fields = new List<string>();
            var cur = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c != '"') { cur.Append(c); continue; }
                    if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; continue; }
                    quoted = false;
                }
                else if (c == '"' && cur.Length == 0) quoted = true;
                else if (c == ',') { fields.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(c);
            }
            fields.Add(cur.ToString());
            return fields;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  0. The harness agrees with the Revit run it is standing in for
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void The_Test_Resolver_Reproduces_What_The_Model_Actually_Resolved()
        {
            // The fixture's ExistingProd and ExistingSource columns are copied out of
            // prod_coverage_20260908_221546.csv — a real Revit run. If this test's own
            // wiring of ProdResolver disagreed with it, every verdict below would be
            // measuring the harness rather than the planner.
            var wrong = new List<string>();
            foreach (var r in Fixture())
            {
                var got = Resolve(r.ToInput());
                if (!string.Equals(got.Code, r.ExistingProd, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(got.Source, r.ExistingSource, StringComparison.OrdinalIgnoreCase))
                    wrong.Add($"{r}: run said {r.ExistingProd}/{r.ExistingSource}, "
                            + $"this harness says {got.Code}/{got.Source}");
            }
            Assert.True(wrong.Count == 0, string.Join("\n", wrong));
        }

        [Fact]
        public void The_Reconstructed_Layers_Reproduce_The_Names_The_Run_Proposed()
        {
            // The fixture carries a core material and a thickness, not the whole compound
            // structure. They must compose the SAME name the 2026-09-08 run produced, or
            // the refusals below are being proved against a different type.
            var wrong = new List<string>();
            foreach (var r in Fixture())
            {
                var p = TypeRenamePlanner.Plan(r.ToInput());   // no resolver: the raw proposal
                if (!string.Equals(p.ProposedName, r.RecordedProposal, StringComparison.Ordinal))
                    wrong.Add($"{r}: run proposed '{r.RecordedProposal}', reconstruction gives "
                            + $"'{p.ProposedName ?? "(refused: " + p.Reason + ")"}'");
            }
            Assert.True(wrong.Count == 0, string.Join("\n", wrong));
        }

        // ══════════════════════════════════════════════════════════════════════
        //  1. The gate
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void No_Rename_Replaces_A_Code_That_Already_Resolves_Specifically()
        {
            var misses = new List<string>();
            foreach (var r in Fixture())
            {
                var p = TypeRenamePlanner.Plan(r.ToInput(), Resolve);
                var existing = Resolve(r.ToInput());

                if (r.MustPropose)
                {
                    if (!p.IsProposal)
                        misses.Add($"{r}: REFUSED but must stand — it resolves {existing.Code} "
                                 + $"from {existing.Source}. Reason given: {p.Reason}");
                    continue;
                }

                if (p.IsProposal)
                    misses.Add($"{r}: proposes '{p.ProposedName}' declaring {p.DeclaredCode}, "
                             + $"but the type already resolves {existing.Code} from {existing.Source} "
                             + "— the rename would overwrite a correct code with a worse one.");
            }
            Assert.True(misses.Count == 0,
                $"{misses.Count} of 6 fixture rows disagree:\n  " + string.Join("\n  ", misses));
        }

        [Fact]
        public void The_Refusal_Says_Which_Code_Would_Have_Been_Lost()
        {
            // A refusal a reviewer cannot act on is a refusal they will override.
            var steps = Fixture().First(r => r.CurrentName == "stepsr 7");
            var p = TypeRenamePlanner.Plan(steps.ToInput(), Resolve);

            Assert.Null(p.ProposedName);
            Assert.Contains("FSP", p.Reason);
            Assert.Contains("SLB", p.Reason);
            Assert.Equal("SLB", p.DeclaredCode);
            Assert.Equal("FSP", p.Existing?.Code);
        }

        [Fact]
        public void A_Category_Default_Is_Not_An_Answer_And_Can_Be_Replaced()
        {
            // WL / FL / RF / CLG mean "nobody has classified this". Replacing one is the
            // whole point of the command, and a gate that stopped it would leave the tool
            // able to rename only the types that least need it.
            var wall = Fixture().First(r => r.CurrentName == "Exterior_CreamWhite_230 2");
            var p = TypeRenamePlanner.Plan(wall.ToInput(), Resolve);

            Assert.Equal("PLNS_WBK_ClayBrick205-Plastered", p.ProposedName);
            Assert.Equal("WL", p.Existing?.Code);
            Assert.Equal("category", p.Existing?.Source);
            Assert.False(p.Existing.IsSpecific);
        }

        [Fact]
        public void The_Same_Code_Restated_Is_Not_A_Change()
        {
            // The row that keeps the gate from being "refuse every code change": a
            // corporate rule already says WBK and the proposed name declares WBK, so
            // nothing moves and the rename is pure improvement to the NAME.
            var brick = Fixture().First(r => r.CurrentName.StartsWith("CLAY BRICK VILLAGE LARGE"));
            var p = TypeRenamePlanner.Plan(brick.ToInput(), Resolve);

            Assert.Equal("WBK", p.Existing?.Code);
            Assert.True(p.Existing.IsSpecific);
            Assert.Equal("WBK", p.DeclaredCode);
            Assert.Equal("PLNS_WBK_ClayBrick150", p.ProposedName);
        }

        [Fact]
        public void A_Rename_That_Changes_Nothing_Is_Not_Refused_For_Changing_Something()
        {
            // A project overlay can map a type to a different code from the one its own
            // name declares. The planner then composes the name the type ALREADY has, and
            // a gate that looked only at the codes would report "would change the product
            // code" about a rename that changes no characters at all — and the type would
            // drop out of "already conforms" into "cannot be named".
            var input = new TypeRenameInput
            {
                Category = "Walls",
                FamilyName = "Basic Wall",
                CurrentName = "PLNS_WBL_Blockwork200-Plastered",
                InstanceCount = 4,
                Layers = new List<MaterialLayer>
                {
                    new MaterialLayer { Index = 0, MaterialName = "Plaster - Cement Render 1:4", ThicknessMm = 12 },
                    new MaterialLayer { Index = 1, MaterialName = "Masonry - Hollow Concrete Block 200mm",
                                        ThicknessMm = 200, IsStructure = true },
                },
            };

            var p = TypeRenamePlanner.Plan(input,
                _ => new ExistingProdCode { Code = "WPT", Source = "project", IsSpecific = true });

            Assert.True(p.AlreadyConforms);
            Assert.False(p.RefusedToProtectCode);
        }

        [Fact]
        public void The_Summary_Separates_Cannot_Name_From_Held_Back()
        {
            // "cannot be named from the model" and "would have changed a correct code" are
            // different problems with different fixes, and a reader who sees one count for
            // both will read the refusals as the tool failing.
            var ps = TypeRenamePlanner.PlanAll(
                Fixture().Select(r => r.ToInput()).ToList(), Resolve);

            string s = TypeRenamePlanner.Summary(ps);
            Assert.Contains("4 can be renamed", s);
            Assert.Contains("0 cannot be named", s);
            Assert.Contains("A further 2 were held back", s);
            Assert.Contains("DIFFERENT product code", s);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  2. The same invariant over the shipped catalogue
        // ══════════════════════════════════════════════════════════════════════

        private sealed class Cat
        {
            [JsonProperty("wallTypes")] public List<BaselineHostType> WallTypes;
            [JsonProperty("floorTypes")] public List<BaselineHostType> FloorTypes;
            [JsonProperty("roofTypes")] public List<BaselineHostType> RoofTypes;
            [JsonProperty("ceilingTypes")] public List<BaselineHostType> CeilingTypes;
        }

        private static IEnumerable<(string Category, string Family, BaselineHostType Type)> Catalogue()
        {
            var c = JsonConvert.DeserializeObject<Cat>(
                File.ReadAllText(Path.Combine(DataDir(), "STING_PROJECT_BASELINE.json")));
            foreach (var t in c.WallTypes) yield return ("Walls", "Basic Wall", t);
            foreach (var t in c.FloorTypes) yield return ("Floors", "Floor", t);
            foreach (var t in c.RoofTypes) yield return ("Roofs", "Basic Roof", t);
            foreach (var t in c.CeilingTypes) yield return ("Ceilings", "Compound Ceiling", t);
        }

        private static TypeRenameInput FromCatalogue(string category, string family, BaselineHostType t)
            => new TypeRenameInput
            {
                Category = category,
                FamilyName = family,
                CurrentName = t.Name,
                InstanceCount = 5,
                Layers = (t.Layers ?? new List<BaselineLayer>()).Select((l, i) => new MaterialLayer
                {
                    Index = i,
                    MaterialName = l.Material,
                    ThicknessMm = l.ThicknessMm,
                    IsStructure = string.Equals(l.Function, "Structure", StringComparison.OrdinalIgnoreCase),
                }).ToList(),
            };

        [Fact]
        public void No_Catalogue_Type_Would_Have_Its_Own_Code_Renamed_Away()
        {
            // Every type in the house catalogue already declares its code in its name, so
            // ProdResolver reads it from the "declared" tier — the most specific there is.
            // Re-planning one must therefore either produce the SAME code or refuse. A
            // catalogue type whose own planner disagrees with its own name is a
            // contradiction in the shipped data, not a rename question.
            var misses = new List<string>();
            int checkedCount = 0;
            foreach (var (category, family, t) in Catalogue())
            {
                var input = FromCatalogue(category, family, t);
                var existing = Resolve(input);
                var p = TypeRenamePlanner.Plan(input, Resolve);
                checkedCount++;

                if (!p.IsProposal) continue;              // refused — always safe
                if (string.Equals(p.DeclaredCode, existing.Code, StringComparison.OrdinalIgnoreCase)) continue;

                misses.Add($"{category}/{t.Name}: resolves {existing.Code} ({existing.Source}) "
                         + $"but the plan would declare {p.DeclaredCode} via '{p.ProposedName}'");
            }
            Assert.True(checkedCount >= 20, "catalogue looks empty: " + checkedCount + " types");
            Assert.True(misses.Count == 0,
                $"{misses.Count} of {checkedCount} catalogue types would be downgraded:\n  "
                + string.Join("\n  ", misses));
        }

        // ══════════════════════════════════════════════════════════════════════
        //  3. Concrete Masonry Units are masonry
        // ══════════════════════════════════════════════════════════════════════

        [Theory]
        [InlineData("Concrete Masonry Units", 300, "WBL")]
        [InlineData("Concrete Masonry Unit", 200, "WBL")]
        [InlineData("CMU", 190, "WBL")]
        [InlineData("Masonry - Hollow Concrete Block 200mm", 200, "WBL")]
        [InlineData("Concrete, Cast-in-Place gray", 250, "WRC")]
        public void A_Concrete_Masonry_Unit_Is_Blockwork_Not_Reinforced_Concrete(
            string coreMaterial, double mm, string expectedCode)
        {
            // "Generic - 300mm Masonry", "Generic - 200mm Masonry", "Generic - 150mm
            // Masonry" and "M_Exterior - Brick on CMU" all carry the core material
            // "Concrete Masonry Units" and all four were proposed as PLNS_WRC_* —
            // reinforced concrete — because the substance table saw the word "concrete"
            // and had no longer needle above it. A block wall is masonry; the concrete
            // in its name is what the block is made of, not what the wall is. The last
            // row is the control: real cast in-situ concrete must still read RC.
            var p = TypeRenamePlanner.Plan(new TypeRenameInput
            {
                Category = "Walls",
                FamilyName = "Basic Wall",
                CurrentName = "Generic - " + mm + "mm Masonry",
                InstanceCount = 3,
                Layers = new List<MaterialLayer>
                {
                    new MaterialLayer { Index = 0, MaterialName = coreMaterial, ThicknessMm = mm, IsStructure = true },
                },
            });
            Assert.Equal(expectedCode, p.DeclaredCode);
        }
    }
}
