// ══════════════════════════════════════════════════════════════════════════
//  CompoundLayerPlanTests.cs — W3 and W2. What the register asks for, and every
//  substitution made getting there, asserted against the SHIPPED register.
//
//  The old BuildLayers returned a plain list of Revit layers. Everything it
//  invented, dropped or substituted on the way was either a rate-limited log
//  line or nothing at all, and the caller counted a type built from a default
//  material at a default thickness exactly the same as one built from the row.
//
//  These tests are driven by BLE_MATERIALS.csv and MEP_MATERIALS.csv rather than
//  by hand-written rows, because the numbers are the point: a comment claiming
//  the fallback "covers 165+ BLE rows" was out by nearly two-fold, and nothing
//  in the repository would have said so.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using StingTools.Core.Materials;
using Xunit;
using Xunit.Abstractions;

namespace StingTools.Tags.Tests
{
    public class CompoundLayerPlanTests
    {
        private readonly ITestOutputHelper _out;
        public CompoundLayerPlanTests(ITestOutputHelper output) => _out = output;

        // The positional constants CompoundTypeCreator uses, asserted below against the
        // shipped headers rather than trusted.
        private const int ColCode = 3, ColName = 6, ColThicknessMm = 9;
        private const int ColLayer1Start = 16, LayerStride = 3, MaxLayers = 5;

        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "BLE_MATERIALS.csv")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data from " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, "StingTools", "Data");
        }

        private sealed class Row
        {
            public string[] Cols;
            public string Code => Cols.Length > ColCode ? Cols[ColCode].Trim() : "";
            public string Name => Cols.Length > ColName ? Cols[ColName].Trim() : "";
        }

        private static Dictionary<string, List<Row>> _files;

        private static Dictionary<string, List<Row>> Files()
        {
            if (_files != null) return _files;
            _files = new Dictionary<string, List<Row>>(StringComparer.Ordinal);
            foreach (string f in new[] { "BLE_MATERIALS.csv", "MEP_MATERIALS.csv" })
            {
                var lines = File.ReadAllLines(Path.Combine(DataDir(), f), Encoding.UTF8)
                                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                                .ToList();
                Assert.True(lines.Count > 100, f + " looks empty: " + lines.Count + " lines");
                _files[f] = lines.Skip(1).Select(l => new Row { Cols = Split(l) }).ToList();
            }
            return _files;
        }

        private static CompoundLayerPlan PlanOf(Row r)
        {
            var mats = new List<string>();
            var thicks = new List<string>();
            var funcs = new List<string>();
            for (int i = 0; i < MaxLayers; i++)
            {
                int b = ColLayer1Start + (i * LayerStride);
                mats.Add(b < r.Cols.Length ? r.Cols[b] : "");
                thicks.Add(b + 1 < r.Cols.Length ? r.Cols[b + 1] : "");
                funcs.Add(b + 2 < r.Cols.Length ? r.Cols[b + 2] : "");
            }
            double total = 0;
            if (r.Cols.Length > ColThicknessMm)
                double.TryParse(r.Cols[ColThicknessMm].Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out total);
            return CompoundLayerPlanner.Plan(mats, thicks, funcs, r.Name, total);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  0. The positional read is still pointing at the right columns
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void The_Column_Constants_Still_Match_The_Shipped_Headers()
        {
            // CompoundTypeCreator reads these files by POSITION. These files carry 72
            // columns and grow; a column inserted above 16 would make every layer read
            // return a colour or a cost, and every type would still be created. Checked
            // rather than assumed — it was the first hypothesis for the 87 and it is wrong.
            foreach (string f in new[] { "BLE_MATERIALS.csv", "MEP_MATERIALS.csv" })
            {
                var header = Split(File.ReadAllLines(Path.Combine(DataDir(), f), Encoding.UTF8)
                                       .First(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#")));
                Assert.Equal("MAT_CODE", header[ColCode].Trim());
                Assert.Equal("MAT_NAME", header[ColName].Trim());
                Assert.Equal("MAT_THICKNESS_MM", header[ColThicknessMm].Trim());
                for (int i = 0; i < MaxLayers; i++)
                {
                    int b = ColLayer1Start + (i * LayerStride);
                    Assert.Equal($"MAT_LAYER_{i + 1}_MATERIAL", header[b].Trim());
                    Assert.Equal($"MAT_LAYER_{i + 1}_THICKNESS_MM", header[b + 1].Trim());
                    Assert.Equal($"MAT_LAYER_{i + 1}_FUNCTION", header[b + 2].Trim());
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  1. W3 — the measurement the comment got wrong
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void The_Homogeneous_Path_Is_Forty_Per_Cent_Of_The_REGISTER()
        {
            // The comment being replaced said the fallback "covers 165+ BLE rows with no
            // layer data". It is 326 - out by nearly two-fold, and the comment was the only
            // record. MEP adds 120 more, so 446 of 1,279 rows declare no layer at all.
            //
            // This is a fact about the FILES. It is not the number of types affected: see
            // the test below, which counts only the rows a shipped command actually reads.
            // Reporting the file figure as the blast radius is how "165+" survived.
            var ble = Files()["BLE_MATERIALS.csv"].Select(PlanOf).ToList();
            var mep = Files()["MEP_MATERIALS.csv"].Select(PlanOf).ToList();

            Assert.Equal(815, ble.Count);
            Assert.Equal(464, mep.Count);

            int bleHomo = ble.Count(p => p.Outcome == LayerPlanOutcome.NoLayersDeclared);
            int mepHomo = mep.Count(p => p.Outcome == LayerPlanOutcome.NoLayersDeclared);
            _out.WriteLine($"homogeneous: BLE {bleHomo}, MEP {mepHomo}, total {bleHomo + mepHomo} "
                         + $"of {ble.Count + mep.Count}");
            Assert.Equal(326, bleHomo);
            Assert.Equal(120, mepHomo);
        }

        /// <summary>MAT_ELEMENT_TYPE filters, copied from the four commands in
        /// FamilyCommands.cs. The rows nothing selects are not this feature's problem, and
        /// counting them as if they were is how a "165+" became a blast radius.</summary>
        private static readonly (string Command, string[] Filters)[] HostCommands =
        {
            ("Walls",    new[] { "A-STR", "A-ASM", "A-BLK" }),
            ("Floors",   new[] { "A-FLR" }),
            ("Ceilings", new[] { "A-CLG" }),
            ("Roofs",    new[] { "A-RF" }),
        };

        private const int ColElementType = 4;

        [Fact]
        public void Across_The_Rows_The_COMMANDS_Read_Nothing_Is_Invented_Or_Dropped_Today()
        {
            // The operative measurement, and the one that answers W2's question. The four
            // host commands select 380 of the 815 BLE rows; across those, NOT ONE trips an
            // invented thickness, a dropped layer or an R-value in the material column.
            //
            // So making those three FATAL costs nothing today. That is the whole argument
            // for making them fatal rather than a footnote: it is free now, and the next
            // register will not be this one.
            var byCommand = new List<string>();
            int rows = 0, declared = 0, homo = 0, failed = 0;
            int invented = 0, dropped = 0;

            foreach (var (command, filters) in HostCommands)
            {
                var sel = Files()["BLE_MATERIALS.csv"]
                          .Where(r => r.Cols.Length > ColElementType
                                   && filters.Contains(r.Cols[ColElementType].Trim(),
                                                       StringComparer.OrdinalIgnoreCase))
                          .ToList();
                var plans = sel.Select(PlanOf).ToList();
                int d = plans.Count(x => x.Outcome == LayerPlanOutcome.Declared);
                int h = plans.Count(x => x.Outcome == LayerPlanOutcome.NoLayersDeclared);
                int f = plans.Count(x => x.Outcome == LayerPlanOutcome.ParseFailure);
                int inv = plans.Sum(x => x.Issues.Count(i => i.Kind == LayerIssueKind.ThicknessInvented));
                int dr = plans.Sum(x => x.Issues.Count(i => i.Kind == LayerIssueKind.LayerDropped));

                byCommand.Add($"{command,-9} rows={sel.Count,4} declared={d,4} homogeneous={h,4} "
                            + $"parseFailure={f,3} invented={inv,3} dropped={dr,3}");
                rows += sel.Count; declared += d; homo += h; failed += f;
                invented += inv; dropped += dr;
            }
            foreach (string l in byCommand) _out.WriteLine(l);

            Assert.Equal(380, rows);
            Assert.Equal(329, declared);
            Assert.Equal(51, homo);
            Assert.Equal(0, failed);
            Assert.Equal(0, invented);
            Assert.Equal(0, dropped);
        }

        [Fact]
        public void A_Row_That_Declares_Layers_Is_Built_From_Them_And_Never_Homogenised()
        {
            // The rule W3 asks for, over the whole register: if the row declares a layer,
            // the plan is Declared or ParseFailure — never the homogeneous path, which is
            // reserved for rows that declare nothing.
            var wrong = new List<string>();
            foreach (var kv in Files())
                foreach (var r in kv.Value)
                {
                    bool declaresAny = Enumerable.Range(0, MaxLayers).Any(i =>
                    {
                        int b = ColLayer1Start + (i * LayerStride);
                        return b < r.Cols.Length && !string.IsNullOrWhiteSpace(r.Cols[b]);
                    });
                    var p = PlanOf(r);
                    if (declaresAny && p.Outcome == LayerPlanOutcome.NoLayersDeclared)
                        wrong.Add($"{kv.Key} {r.Code} {r.Name}");
                }
            Assert.True(wrong.Count == 0,
                "rows that declare layers and were homogenised anyway:\n  " + string.Join("\n  ", wrong));
        }

        [Fact]
        public void A_Single_Layer_Row_Is_Built_From_Its_OWN_Material_Not_A_Default()
        {
            // W3's other requirement. FLR-001 declares one 50 mm layer of CEMENT SCREED;
            // the old fallback would have given it the caller's defaultMatId at
            // defaultThickMm. That distinction is the whole finish catalogue.
            var r = Files()["BLE_MATERIALS.csv"].Single(x => x.Code == "FLR-001");
            var p = PlanOf(r);

            Assert.Equal(LayerPlanOutcome.Declared, p.Outcome);
            var layer = Assert.Single(p.Layers);
            Assert.Equal("CEMENT SCREED", layer.Material);
            Assert.Equal(50.0, layer.ThicknessMm);
            Assert.Contains("SUBSTRATE", layer.Function);
            Assert.True(p.AsDeclared);
        }

        [Fact]
        public void No_FLR_Row_Takes_The_Homogeneous_Path()
        {
            // The measurement that answers the 87 question in the NEGATIVE, and the reason
            // this change does not claim to explain them. All 95 carry a valid layer 1, so
            // the fallback fires for none of them — whatever made 87 identical 100 mm
            // concrete floors, it was not this path.
            var flr = Files()["BLE_MATERIALS.csv"]
                      .Where(r => r.Code.StartsWith("FLR-", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.Equal(95, flr.Count);

            var plans = flr.Select(PlanOf).ToList();
            Assert.Empty(plans.Where(p => p.Outcome == LayerPlanOutcome.NoLayersDeclared));
            Assert.Empty(plans.Where(p => p.Outcome == LayerPlanOutcome.ParseFailure));
            Assert.Equal(95, plans.Count(p => p.Outcome == LayerPlanOutcome.Declared));

            // And none of them plans a 100 mm layer of a Revit stock material.
            Assert.Empty(plans.SelectMany(p => p.Layers)
                              .Where(l => l.Material.IndexOf("Cast-in-Place",
                                          StringComparison.OrdinalIgnoreCase) >= 0));
        }

        // ══════════════════════════════════════════════════════════════════════
        //  2. W2 — every substitution is carried out of the function
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void A_Missing_Thickness_Is_Reported_And_Fatal_Not_Silently_Ten_Millimetres()
        {
            // The old line was `if (layerThickMm <= 0) layerThickMm = 10;` with no warning.
            var p = CompoundLayerPlanner.Plan(
                new[] { "CEMENT SCREED" }, new[] { "" }, new[] { "SUBSTRATE" }, "A ROW", 50);

            Assert.Equal(LayerPlanOutcome.ParseFailure, p.Outcome);
            Assert.False(p.AsDeclared);
            var issue = p.Issues.First(i => i.Kind == LayerIssueKind.ThicknessInvented);
            Assert.Equal(1, issue.Slot);
            Assert.Contains("empty", issue.Detail);
            Assert.True(issue.IsFatal);

            // And an unparseable cell says what it read, rather than reporting "empty".
            var bad = CompoundLayerPlanner.Plan(
                new[] { "CEMENT SCREED" }, new[] { "50 mm" }, new[] { "" }, "A ROW", 50);
            Assert.Contains("'50 mm'", bad.Issues.First().Detail);
        }

        [Fact]
        public void Revits_One_Millimetre_Floor_Is_Reported_But_Not_Fatal()
        {
            // The one substitution that is NOT a guess: no caller can honour 0.5 mm, so
            // raising it is Revit's constraint rather than an invented quantity. 180 rows
            // of the shipped register take this path, so making it fatal would fail a
            // seventh of the file for something nobody can do anything about.
            var p = CompoundLayerPlanner.Plan(
                new[] { "FINISH-SEAL" }, new[] { "0.5" }, new[] { "FINISH 2" }, "A ROW", 14);

            Assert.Equal(LayerPlanOutcome.Declared, p.Outcome);
            Assert.True(p.AsDeclared);
            var issue = Assert.Single(p.Issues);
            Assert.Equal(LayerIssueKind.ThicknessFloored, issue.Kind);
            Assert.False(issue.IsFatal);
            Assert.Equal(1.0, p.Layers.Single().ThicknessMm);
        }

        [Fact]
        public void A_Dropped_Layer_Is_Fatal_Because_A_Missing_Layer_Is_A_Changed_Quantity()
        {
            // The >500 mm rule exists for cable cross-sections stored in the thickness
            // column. It fires on NO row of either shipped file — asserted below — so this
            // is a guard, not a fix. It is fatal because a 600 mm foundation layer leaves
            // by the same door.
            var p = CompoundLayerPlanner.Plan(
                new[] { "COPPER", "PVC" }, new[] { "600.0", "2.0" }, new[] { "", "" }, "A CABLE", 0);

            Assert.Single(p.Layers);
            Assert.Equal("PVC", p.Layers[0].Material);
            var dropped = p.Issues.First(i => i.Kind == LayerIssueKind.LayerDropped);
            Assert.Equal("COPPER", dropped.Material);
            Assert.Contains("cross-section", dropped.Detail);
            Assert.True(dropped.IsFatal);
            Assert.False(p.AsDeclared);

            // And the threshold does NOT catch the example its own comment cites. The
            // shipped comment says "mm\u00b2 stored as mm, e.g. 300.0 for a 300mm\u00b2
            // conductor" \u2014 300 is below 500, so that conductor is read as a 300 mm
            // layer and built. Asserted rather than tidied: the rule and its stated reason
            // do not match, no shipped row trips the rule at all, and changing the
            // threshold would be a quantity change made on no evidence.
            var notDropped = CompoundLayerPlanner.Plan(
                new[] { "COPPER" }, new[] { "300.0" }, new[] { "" }, "A CABLE", 0);
            Assert.Single(notDropped.Layers);
            Assert.Equal(300.0, notDropped.Layers[0].ThicknessMm);
            Assert.Empty(notDropped.Issues);
        }

        [Fact]
        public void The_Rows_No_Command_Reads_Are_Where_The_Bad_Cells_Live()
        {
            // Stated rather than left as a gap. Across ALL 1,279 rows the substitutions do
            // fire, and every one is in a row no host command selects:
            //
            //   181 unreadable thickness cells — 9 on A-FIN finishes, 172 on MEP dampers
            //                                    and equipment, which have no layers at all
            //    92 R-values written into a material column, all outside the four filters
            //     0 cells above 500 mm anywhere in either file
            //
            // Measured so that the day one of them moves INTO a command's filter, this
            // fails and somebody looks at the row rather than at a 10 mm default.
            var all = Files().SelectMany(kv => kv.Value).Select(PlanOf).ToList();
            var dropped = all.SelectMany(x => x.Issues).Where(i => i.Kind == LayerIssueKind.LayerDropped).ToList();
            var invented = all.SelectMany(x => x.Issues).Where(i => i.Kind == LayerIssueKind.ThicknessInvented).ToList();

            _out.WriteLine($"across all {all.Count} rows: dropped {dropped.Count}, invented {invented.Count}");
            foreach (var i in dropped.Take(5)) _out.WriteLine("   drop:   " + i);
            foreach (var i in invented.Take(5)) _out.WriteLine("   invent: " + i);

            Assert.Equal(92, dropped.Count);
            Assert.Equal(301, invented.Count);
            // None above 500 mm — that rule is a pure guard, in both files.
            Assert.Empty(dropped.Where(i => i.Detail.Contains("exceeds")));
        }

        [Fact]
        public void A_Sparse_Slot_Row_Reads_The_Slots_It_Populated_Not_The_First_N()
        {
            // The count-as-index defect. WL-024 populates slots 2, 3 and 4; the old reader
            // counted 3 and read slots 1, 2, 3 — a blank material at slot 1 (which is where
            // both of the register's two invented thicknesses came from) and slot 4 dropped.
            // Both are A-FIN rows, so no host command reads them today, which is why the
            // defect never surfaced as a wrong floor. It is still the reason the register's
            // only two invented thicknesses existed, and it would bite the moment either
            // row moved into a filter.
            foreach (string code in new[] { "WL-024", "WL-037" })
            {
                var r = Files()["BLE_MATERIALS.csv"].Single(x => x.Code == code);
                var p = PlanOf(r);
                _out.WriteLine($"{code} {r.Name}: {p.Outcome} — {p.Describe()}");

                Assert.Equal(LayerPlanOutcome.Declared, p.Outcome);
                Assert.All(p.Layers, l => Assert.False(string.IsNullOrWhiteSpace(l.Material)));
                Assert.DoesNotContain(p.Layers, l => l.Slot == 1);   // slot 1 really is blank

                // Slot 1 holds a material with a 0.0 thickness, so it is a declared layer
                // that cannot be read — reported, where the old reader put 10 mm there.
                Assert.Contains(p.Issues, i => i.Kind == LayerIssueKind.ThicknessInvented && i.Slot == 1);
            }
        }

        [Fact]
        public void A_Row_Whose_Declared_Layers_All_Fail_Is_A_Parse_Failure_Not_A_Material()
        {
            // The distinction the old fallback could not make: this row and a genuinely
            // single-material row both came out as one default layer.
            var failed = CompoundLayerPlanner.Plan(
                new[] { "CEMENT SCREED" }, new[] { "oops" }, new[] { "" }, "A ROW", 50);
            Assert.Equal(LayerPlanOutcome.ParseFailure, failed.Outcome);
            Assert.Empty(failed.Layers);

            var homogeneous = CompoundLayerPlanner.Plan(
                new string[0], new string[0], new string[0], "SOLID TIMBER", 45);
            Assert.Equal(LayerPlanOutcome.NoLayersDeclared, homogeneous.Outcome);
            Assert.Equal("SOLID TIMBER", homogeneous.Layers.Single().Material);
            Assert.Equal(45, homogeneous.Layers.Single().ThicknessMm);
            Assert.True(homogeneous.AsDeclared);
        }

        [Fact]
        public void A_Row_With_No_Layers_And_No_Thickness_Says_So_Rather_Than_Inventing()
        {
            var p = CompoundLayerPlanner.Plan(
                new string[0], new string[0], new string[0], "A ROW", 0);
            Assert.Equal(LayerPlanOutcome.NoLayersDeclared, p.Outcome);
            Assert.Equal(1.0, p.Layers.Single().ThicknessMm);
            var issue = Assert.Single(p.Issues);
            Assert.Equal(LayerIssueKind.ThicknessInvented, issue.Kind);
            Assert.True(issue.IsFatal);
            Assert.False(p.AsDeclared);
        }

        [Fact]
        public void The_Summary_Distinguishes_A_Run_That_Read_From_A_Run_That_Invented()
        {
            // "A run that silently invented 40 thicknesses and a run that read 40 real ones
            // must not print the same line."
            var read = Enumerable.Range(0, 3).Select(_ => CompoundLayerPlanner.Plan(
                new[] { "CEMENT SCREED" }, new[] { "50" }, new[] { "" }, "R", 50)).ToList();
            var invented = Enumerable.Range(0, 3).Select(_ => CompoundLayerPlanner.Plan(
                new[] { "CEMENT SCREED" }, new[] { "" }, new[] { "" }, "R", 50)).ToList();

            string a = CompoundLayerPlanner.Summary(read);
            string b = CompoundLayerPlanner.Summary(invented);
            Assert.NotEqual(a, b);
            Assert.Contains("3 built from their declared layers", a);
            Assert.Contains("0 thickness(es) could not be read", a);
            Assert.Contains("3 declare layers that could not be read", b);
            Assert.Contains("3 thickness(es) could not be read", b);
        }

        private static string[] Split(string line)
        {
            var fields = new List<string>();
            var cur = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < (line ?? "").Length; i++)
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
            return fields.ToArray();
        }
    }
}
