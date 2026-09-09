// ══════════════════════════════════════════════════════════════════════════
//  TypeRenameSubstanceStemTests.cs — W5c. The matcher swap needed STEMS, not
//  just a matcher.
//
//  `TypeRenamePlanner.Match` used `IndexOf` — the #863 shape, in a file that
//  reaches a PROD code. Swapping it for whole-word matching ALONE breaks seven
//  rows of a delivered model, because the data is plural and the needles were
//  singular:
//
//      5 walls   core "Concrete Masonry Units"   Blockwork → RC   (WBL → WRC)
//      2 roofs   core "CLAY TILES PREMIUM 15MM"  Clay Tile Roof → nothing at all
//
//  Verified before the change, against the plan the 2026-09-09 run actually
//  wrote. So the swap ships with `concrete masonry units`, `masonry units` and
//  `clay tiles` beside their singulars, and this file re-runs that corpus and
//  requires ZERO breakages.
//
//  ── THE FIXTURE ────────────────────────────────────────────────────────────
//  `Fixtures/type_rename_core_materials_20260909.csv` is the 294 rows of
//  `type_rename_plan_20260909_075110.csv` whose Reason names a core material,
//  with the proposal that run produced. Reconstructing a type from its CORE
//  MATERIAL is exactly what the planner does, so re-planning each row and
//  comparing against the recorded proposal is a like-for-like re-run rather than
//  a re-statement.
//
//  A different fixture from the one #902 adds, deliberately: that PR is
//  unmerged, and stacking on it is what orphans a change. The two should be
//  reconciled when both land.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using StingTools.Core;
using StingTools.Core.Baseline;
using Xunit;
using Xunit.Abstractions;

namespace StingTools.Tags.Tests
{
    public class TypeRenameSubstanceStemTests
    {
        private readonly ITestOutputHelper _out;
        public TypeRenameSubstanceStemTests(ITestOutputHelper output) => _out = output;

        private sealed class Row
        {
            public string Category, CurrentName, CoreMaterial, RecordedProposal, RecordedOutcome;
            public override string ToString() => $"{Category}/{CurrentName}";
        }

        private static List<Row> _rows;

        private static List<Row> Fixture()
        {
            if (_rows != null) return _rows;
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools.Tags.Tests",
                       "Fixtures", "type_rename_core_materials_20260909.csv")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate the rename fixture from " + AppContext.BaseDirectory);

            var rows = new List<Row>();
            string path = Path.Combine(dir.FullName, "StingTools.Tags.Tests", "Fixtures",
                                       "type_rename_core_materials_20260909.csv");
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var f = SplitCsv(line);
                Assert.True(f.Count == 5, "Malformed fixture line: " + line);
                rows.Add(new Row
                {
                    Category = f[0], CurrentName = f[1], CoreMaterial = f[2],
                    RecordedProposal = f[3], RecordedOutcome = f[4],
                });
            }
            Assert.Equal(294, rows.Count);
            return _rows = rows;
        }

        private static string Replan(Row r)
            => TypeRenamePlanner.Plan(new TypeRenameInput
            {
                Category = r.Category,
                FamilyName = r.Category == "Walls" ? "Basic Wall"
                           : r.Category == "Floors" ? "Floor"
                           : r.Category == "Roofs" ? "Basic Roof" : "Compound Ceiling",
                CurrentName = r.CurrentName,
                InstanceCount = 3,
                Layers = new List<MaterialLayer>
                {
                    new MaterialLayer
                    {
                        Index = 0, MaterialName = r.CoreMaterial,
                        // The recorded proposal carries the size, so the size is not what is
                        // under test here; 100 keeps every row comparable on its SUBSTANCE.
                        ThicknessMm = 100, IsStructure = true,
                    },
                },
            }).ProposedName;

        /// <summary>The substance half of the recorded name — everything up to the size —
        /// because the fixture flattens every row to one 100 mm layer.</summary>
        private static string SubstanceOf(string proposedName)
        {
            if (string.IsNullOrEmpty(proposedName)) return null;
            var parts = proposedName.Split('_');
            if (parts.Length < 3) return proposedName;
            string subtype = parts[2].Split('-')[0];
            return parts[1] + "_" + new string(subtype.TakeWhile(c => !char.IsDigit(c)).ToArray());
        }

        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void Not_One_Row_The_Run_Named_Loses_Its_Substance()
        {
            // W5c's number, re-measured: with whole-word matching and the plural stems,
            // ZERO of the 168 rows the 2026-09-09 run named now resolve to a different
            // substance or to nothing. Without the stems this list is 7.
            var broken = new List<string>();
            foreach (var r in Fixture().Where(x => x.RecordedOutcome == "rename"))
            {
                string was = SubstanceOf(r.RecordedProposal);
                string now = SubstanceOf(Replan(r));
                if (!string.Equals(was, now, StringComparison.Ordinal))
                    broken.Add($"{r}: core '{r.CoreMaterial}'  {was ?? "(none)"} → {now ?? "(none)"}");
            }

            _out.WriteLine($"{Fixture().Count(x => x.RecordedOutcome == "rename")} named rows re-planned, "
                         + $"{broken.Count} broken.");
            foreach (string b in broken) _out.WriteLine("    " + b);

            Assert.True(broken.Count == 0,
                $"{broken.Count} row(s) lost their substance when Match became whole-word:\n  "
              + string.Join("\n  ", broken));
        }

        [Theory]
        [InlineData("Concrete Masonry Units", "Blockwork")]
        [InlineData("Concrete Masonry Unit", "Blockwork")]
        [InlineData("Masonry Units", "Blockwork")]
        [InlineData("CLAY TILES PREMIUM 15MM", "ClayTileRoof")]
        [InlineData("Clay Tile", "ClayTileRoof")]
        public void The_Plural_The_Data_Actually_Uses_Still_Matches(string core, string expectSubtype)
        {
            // The five names that whole-word matching alone would have dropped, pinned by
            // the exact string the model carries. "Units" and "TILES" are why this needed
            // stems and not just a matcher.
            var p = TypeRenamePlanner.Plan(new TypeRenameInput
            {
                Category = core.IndexOf("TILE", StringComparison.OrdinalIgnoreCase) >= 0 ? "Roofs" : "Walls",
                FamilyName = "probe",
                CurrentName = "probe",
                InstanceCount = 1,
                Layers = new List<MaterialLayer>
                {
                    new MaterialLayer { Index = 0, MaterialName = core, ThicknessMm = 200, IsStructure = true },
                },
            });
            Assert.NotNull(p.ProposedName);
            Assert.Contains(expectSubtype, p.ProposedName, StringComparison.Ordinal);
        }

        [Fact]
        public void The_Five_CMU_Walls_Are_Blockwork_And_Not_Reinforced_Concrete()
        {
            // The specific harm: five walls falling back from WBL to WRC means a block wall
            // priced and carbon-counted as in-situ reinforced concrete.
            var cmu = Fixture().Where(r => string.Equals(r.CoreMaterial, "Concrete Masonry Units",
                                                         StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.Equal(5, cmu.Count);
            foreach (var r in cmu)
            {
                string now = Replan(r);
                Assert.NotNull(now);
                Assert.Contains("_WBL_", now, StringComparison.Ordinal);
                Assert.DoesNotContain("_WRC_", now, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void The_Matcher_Is_Whole_Word_Now()
        {
            // The swap itself, at the boundary that motivated it. "PVC SINGLE PLY" must not
            // read as plywood and "DUCTILE" must not read as a clay tile.
            foreach (string core in new[] { "PVC SINGLE PLY 1.5MM", "DUCTILE IRON SHEET" })
            {
                var p = TypeRenamePlanner.Plan(new TypeRenameInput
                {
                    Category = "Floors", FamilyName = "Floor", CurrentName = "probe", InstanceCount = 1,
                    Layers = new List<MaterialLayer>
                    {
                        new MaterialLayer { Index = 0, MaterialName = core, ThicknessMm = 100, IsStructure = true },
                    },
                });
                Assert.Null(p.ProposedName);
                Assert.Contains("names no substance", p.Reason);
            }
        }

        [Fact]
        public void The_Seven_Rows_That_GAIN_A_Name_Are_Wood_Joists_And_Rafters()
        {
            // Sharing the wood vocabulary makes the renamer recognise seven core materials
            // it used to refuse. Every one is literally called a wood joist or rafter, so
            // this is the table catching up with the model rather than a new guess — but it
            // is a behaviour change and it is named rather than counted.
            var gained = Fixture()
                .Where(r => r.RecordedOutcome == "cannot name" && Replan(r) != null)
                .ToList();

            foreach (var r in gained) _out.WriteLine($"    {r}: core '{r.CoreMaterial}' → {Replan(r)}");

            Assert.Equal(7, gained.Count);
            Assert.All(gained, r => Assert.Contains("Wood Joist", r.CoreMaterial, StringComparison.OrdinalIgnoreCase));
            Assert.All(gained, r => Assert.Contains("Timber", Replan(r), StringComparison.Ordinal));
        }

        [Fact]
        public void Every_Other_Refusal_Stays_A_Refusal()
        {
            // The other 119 rows the run could not name must still be unnameable. A matcher
            // change that started naming things is the same defect in the other direction.
            var stillRefused = Fixture()
                .Count(r => r.RecordedOutcome == "cannot name" && Replan(r) == null);
            Assert.Equal(119, stillRefused);
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
    }
}
