using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using StingTools.Core;
using StingTools.Core.Baseline;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// A rename must not give two types the same name.
    ///
    /// <para><b>What happened.</b> On 2026-09-09 <c>Baseline_RenameTypes</c> was applied to a
    /// delivered model. The log recorded <c>168/168 renamed, 0 failed</c>. Of those 168,
    /// <b>137 collided onto 19 names</b> — and the largest group was <b>87 floor types all
    /// named <c>PLNS_SLB_RC100</c></b>: every screed, tile, carpet, paver and resin in the
    /// project's floor-finish catalogue. Revit's API accepted every duplicate without
    /// throwing, so the per-type <c>try/catch</c> in the command reported total success.
    /// Nothing failed. Everything broke.</para>
    ///
    /// <para><b>Why it collided, and why the answer is to refuse rather than to
    /// disambiguate.</b> All 87 gave the same reason: <c>core 'Concrete, Cast-in-Place gray'
    /// → SLB; no finish layer to read</c>. They are one 100 mm slab of grey concrete wearing
    /// 87 names. The distinction was never in the model — only in the string. A planner that
    /// appended <c>-2</c>, <c>-3</c> … would have manufactured a difference the build-ups do
    /// not contain and made the duplication permanent and invisible; the same objection that
    /// retired every other guess in this codebase. So the gate refuses, and says which peers
    /// it collided with. The refusal is the finding: <b>these types are named, not
    /// modelled</b>, and every quantity taken off them — area, volume, embodied carbon, cost
    /// — has been answering "100 mm of concrete" regardless of what the name promised.</para>
    ///
    /// <para><b>The fixture is the run.</b> <c>Fixtures/type_rename_20260909.csv</c> carries
    /// all 168 proposals from that model: category, current name, the core material and
    /// thickness the planner read, the finish layer where there was one, and the name it
    /// actually produced. <see cref="The_Fixture_Reproduces_The_Recorded_Run"/> asserts the
    /// reconstruction still composes the recorded name — so if this file ever stops
    /// describing reality, that test fails first and the rest cannot quietly pass against
    /// invented data.</para>
    /// </summary>
    public class TypeRenameUniquenessTests
    {
        // ══════════════════════════════════════════════════════════════════════
        //  Fixture
        // ══════════════════════════════════════════════════════════════════════

        private sealed class Row
        {
            public string Category, CurrentName, CoreMaterial, FinishMaterial, RecordedName;
            public double CoreThicknessMm;
        }

        private static string FixtureDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools.Tags.Tests", "Fixtures")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools.Tags.Tests/Fixtures");
            return Path.Combine(dir.FullName, "StingTools.Tags.Tests", "Fixtures");
        }

        private static List<Row> Fixture()
        {
            string path = Path.Combine(FixtureDir(), "type_rename_20260909.csv");
            Assert.True(File.Exists(path), "missing fixture: " + path);

            var rows = new List<Row>();
            foreach (string line in File.ReadAllLines(path).Skip(1))
            {
                var f = SplitCsv(line);
                if (f.Count < 6) continue;
                rows.Add(new Row
                {
                    Category = f[0],
                    CurrentName = f[1],
                    CoreMaterial = f[2],
                    CoreThicknessMm = double.Parse(f[3], CultureInfo.InvariantCulture),
                    FinishMaterial = f[4],
                    RecordedName = f[5],
                });
            }
            Assert.True(rows.Count == 168,
                $"the fixture is the 2026-09-09 run and that run proposed 168 names; found {rows.Count}");
            return rows;
        }

        /// <summary>
        /// Rebuild the compound structure the planner read: a structural core, plus a finish
        /// layer where the run recorded one. Nothing else was used to compose the name.
        /// </summary>
        private static TypeRenameInput InputFor(Row r)
        {
            var layers = new List<MaterialLayer>
            {
                new MaterialLayer { Index = 0, MaterialName = r.CoreMaterial,
                                    ThicknessMm = r.CoreThicknessMm, IsStructure = true },
            };
            if (!string.IsNullOrEmpty(r.FinishMaterial))
                layers.Add(new MaterialLayer { Index = 1, MaterialName = r.FinishMaterial,
                                               ThicknessMm = 10, IsStructure = false });

            return new TypeRenameInput
            {
                Category = r.Category,
                FamilyName = r.Category == "Walls" ? "Basic Wall"
                           : r.Category == "Roofs" ? "Basic Roof" : r.Category.TrimEnd('s'),
                CurrentName = r.CurrentName,
                Originator = "PLNS",
                Layers = layers,
                InstanceCount = 0,
            };
        }

        // ══════════════════════════════════════════════════════════════════════
        //  0. The fixture still describes the run
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The guard on every other test in this file. If the reconstruction stops composing
        /// the name the delivered model actually produced, the fixture has drifted from
        /// reality and no assertion built on it means anything.
        /// </summary>
        [Fact]
        public void The_Fixture_Reproduces_The_Recorded_Run()
        {
            var wrong = new List<string>();
            foreach (var r in Fixture())
            {
                var p = TypeRenamePlanner.Plan(InputFor(r));
                string got = p.ProposedName ?? p.WithheldName;
                if (!string.Equals(got, r.RecordedName, StringComparison.Ordinal))
                    wrong.Add($"{r.Category}/{r.CurrentName}: recorded '{r.RecordedName}', composed '{got ?? "(none)"}'");
            }
            Assert.True(wrong.Count == 0,
                "The fixture no longer reproduces the 2026-09-09 run, so it is describing a planner "
                + "that no longer exists. Fix the fixture before reading anything else in this file:\n  "
                + string.Join("\n  ", wrong.Take(12)));
        }

        // ══════════════════════════════════════════════════════════════════════
        //  1. The gate
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The whole point. After planning, no two types in a category may hold the same name
        /// — counting both the names being proposed and the names being kept.
        /// </summary>
        [Fact]
        public void No_Two_Types_In_A_Category_End_Up_With_The_Same_Name()
        {
            var rows = Fixture();
            var plans = TypeRenamePlanner.PlanAll(rows.Select(InputFor));

            var claims = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in plans)
            {
                string held = p.IsProposal ? p.ProposedName : p.CurrentName;
                string key = p.Category + " " + held;
                if (!claims.TryGetValue(key, out var l)) claims[key] = l = new List<string>();
                l.Add(p.CurrentName);
            }

            var clashes = claims.Where(kv => kv.Value.Count > 1)
                                .OrderByDescending(kv => kv.Value.Count).ToList();

            Assert.True(clashes.Count == 0,
                $"{clashes.Count} name(s) would be held by more than one type. Revit's API does not "
                + "refuse a duplicate type name — it accepted all 137 of these on 2026-09-09 and the "
                + "command reported 0 failures — so this gate is the only thing standing between a "
                + "rename and a catalogue collapsing into one name:\n  "
                + string.Join("\n  ", clashes.Take(8).Select(kv =>
                    $"{kv.Key.Replace(' ', '/')}  <- {kv.Value.Count} types: "
                    + string.Join(", ", kv.Value.Take(4)) + (kv.Value.Count > 4 ? ", …" : ""))));
        }

        /// <summary>
        /// The 87. Named explicitly because a count can be satisfied by refusing everything,
        /// and because this group is the reason the gate exists.
        /// </summary>
        [Fact]
        public void The_Eighty_Seven_Identical_Floor_Types_Are_All_Refused()
        {
            var rows = Fixture();
            var plans = TypeRenamePlanner.PlanAll(rows.Select(InputFor));

            var group = rows.Where(r => r.RecordedName == "PLNS_SLB_RC100").Select(r => r.CurrentName).ToList();
            Assert.True(group.Count == 87, $"the fixture should hold 87 of these; it holds {group.Count}");

            var leaked = plans.Where(p => group.Contains(p.CurrentName) && p.IsProposal).ToList();
            Assert.True(leaked.Count == 0,
                $"{leaked.Count} of the 87 identical floor types would still be renamed. They are one "
                + "100 mm slab of grey concrete under 87 names; renaming any of them destroys the only "
                + "record of what the finish was: "
                + string.Join(", ", leaked.Take(5).Select(p => p.CurrentName)));

            var sample = plans.First(p => p.CurrentName == group[0]);
            Assert.True(sample.RefusedAsDuplicate,
                "refused, but not as a duplicate — the reader needs to know it is a collision and not "
                + "a model that says too little, because the remedy is different");
            Assert.Contains("share", sample.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("PLNS_SLB_RC100", sample.WithheldName);
        }

        /// <summary>
        /// A gate that refuses everything passes the test above and is useless. The 31
        /// proposals that were unique in that run must survive.
        /// </summary>
        [Fact]
        public void A_Name_Claimed_By_Exactly_One_Type_Is_Still_Proposed()
        {
            var rows = Fixture();
            var plans = TypeRenamePlanner.PlanAll(rows.Select(InputFor));

            var once = rows.GroupBy(r => r.Category + " " + r.RecordedName)
                           .Where(g => g.Count() == 1).Select(g => g.First()).ToList();
            Assert.True(once.Count >= 25,
                $"the run produced {once.Count} uniquely-named proposals; too few to prove anything");

            var lost = plans.Where(p => once.Any(r => r.CurrentName == p.CurrentName && r.Category == p.Category)
                                     && !p.IsProposal).ToList();
            Assert.True(lost.Count == 0,
                $"{lost.Count} uniquely-named type(s) were refused anyway, which would make the gate a "
                + "way of doing nothing: " + string.Join(", ", lost.Take(6).Select(p => p.CurrentName)));
        }

        /// <summary>
        /// The collision the corpus does not contain: a rename landing on a name that some
        /// OTHER type is keeping. Revit sees no difference between this and two renames
        /// colliding — it is the same duplicate — but a planner that only compared proposals
        /// against each other would miss it entirely.
        /// </summary>
        [Fact]
        public void A_Rename_Onto_A_Name_Another_Type_Is_Keeping_Is_Refused()
        {
            var willRename = new TypeRenameInput
            {
                Category = "Walls", FamilyName = "Basic Wall", CurrentName = "Party Wall 200",
                Layers = new List<MaterialLayer>
                {
                    new MaterialLayer { Index = 0, MaterialName = "Concrete, Cast-in-Place gray",
                                        ThicknessMm = 200, IsStructure = true },
                },
            };

            // Says nothing the planner can read, so it keeps its name — which happens to be
            // the name the first one wants.
            var willKeep = new TypeRenameInput
            {
                Category = "Walls", FamilyName = "Basic Wall", CurrentName = "PLNS_WRC_RC200",
                Layers = new List<MaterialLayer>
                {
                    new MaterialLayer { Index = 0, MaterialName = "Unobtainium", ThicknessMm = 200, IsStructure = true },
                },
            };

            var plans = TypeRenamePlanner.PlanAll(new[] { willRename, willKeep });
            var mover = plans.First(p => p.CurrentName == "Party Wall 200");

            Assert.False(mover.IsProposal,
                "'Party Wall 200' would be renamed onto 'PLNS_WRC_RC200', which another wall type is "
                + "keeping. Comparing proposals only against other proposals misses this, and Revit "
                + "will not catch it either.");
            Assert.True(mover.RefusedAsDuplicate);
            Assert.Contains("PLNS_WRC_RC200", mover.Reason, StringComparison.Ordinal);
        }

        /// <summary>
        /// Two types in DIFFERENT categories may share a name — Revit scopes type names per
        /// category, and refusing across categories would block correct renames for no reason.
        /// </summary>
        [Fact]
        public void The_Same_Name_In_Two_Different_Categories_Is_Not_A_Collision()
        {
            TypeRenameInput Slab(string category, string family, string current) => new TypeRenameInput
            {
                Category = category, FamilyName = family, CurrentName = current,
                Layers = new List<MaterialLayer>
                {
                    new MaterialLayer { Index = 0, MaterialName = "Concrete, Cast-in-Place gray",
                                        ThicknessMm = 150, IsStructure = true },
                },
            };

            var plans = TypeRenamePlanner.PlanAll(new[]
            {
                Slab("Floors", "Floor", "Slab A"),
                Slab("Roofs", "Basic Roof", "Deck A"),
            });

            Assert.All(plans, p => Assert.True(p.IsProposal,
                $"'{p.CurrentName}' ({p.Category}) was refused: {p.Reason}"));

            // Same substance, same thickness, different category — and both keep the name.
            Assert.Equal(2, plans.Count);
            Assert.All(plans, p => Assert.False(p.RefusedAsDuplicate));
        }

        /// <summary>
        /// A type that already carries the conforming name is not "colliding with itself".
        /// Re-running the command must be a no-op, not a run that refuses everything it did
        /// last time.
        /// </summary>
        [Fact]
        public void Re_Running_After_A_Successful_Rename_Refuses_Nothing()
        {
            var input = new TypeRenameInput
            {
                Category = "Floors", FamilyName = "Floor", CurrentName = "PLNS_SLB_RC150",
                Layers = new List<MaterialLayer>
                {
                    new MaterialLayer { Index = 0, MaterialName = "Concrete, Cast-in-Place gray",
                                        ThicknessMm = 150, IsStructure = true },
                },
            };

            var p = TypeRenamePlanner.PlanAll(new[] { input }).Single();
            Assert.True(p.AlreadyConforms, "the name already matches what would be proposed");
            Assert.False(p.RefusedAsDuplicate, "a type does not collide with itself");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  2. What the refusal has to tell the reader
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A collision and a model that says too little are different problems with different
        /// remedies — model the layers, versus rename the material — so the summary must not
        /// pour them into one number. On the 2026-09-09 run that would have hidden 137 types
        /// inside a "cannot be named" count.
        /// </summary>
        [Fact]
        public void The_Summary_Counts_Collisions_Separately_From_Silence()
        {
            var plans = TypeRenamePlanner.PlanAll(Fixture().Select(InputFor));
            string s = TypeRenamePlanner.Summary(plans);

            int dup = plans.Count(p => p.RefusedAsDuplicate);
            Assert.True(dup >= 100, $"the run collided 137 types; the gate caught {dup}");
            Assert.Contains(dup.ToString(CultureInfo.InvariantCulture), s, StringComparison.Ordinal);
            Assert.Contains("same name", s, StringComparison.OrdinalIgnoreCase);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  3. The silent failure found while building this
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// <see cref="TypeRenamePlanner.Plan"/> wraps the caller's resolver in a bare
        /// <c>catch</c>. That is right — an unavailable resolver must not abort the plan — but
        /// it means a resolver that throws for EVERY type disables the product-code gate
        /// completely and reports a clean run. The counted flag is what makes the difference
        /// visible; without it, "0 refusals" reads identically whether the gate examined every
        /// type or none of them.
        /// </summary>
        [Fact]
        public void A_Resolver_That_Always_Throws_Is_Reported_Not_Swallowed()
        {
            var input = new TypeRenameInput
            {
                Category = "Walls", FamilyName = "Basic Wall", CurrentName = "Some Wall",
                Layers = new List<MaterialLayer>
                {
                    new MaterialLayer { Index = 0, MaterialName = "Concrete, Cast-in-Place gray",
                                        ThicknessMm = 200, IsStructure = true },
                },
            };

            var p = TypeRenamePlanner.PlanAll(
                new[] { input },
                _ => throw new InvalidOperationException("no rules loaded")).Single();

            Assert.True(p.ExistingLookupFailed,
                "the resolver threw and the plan carried on as though it had simply not been asked. "
                + "A dead product-code gate must not look like a gate that found nothing.");
            Assert.Contains("could not be checked", TypeRenamePlanner.Summary(new[] { p }),
                StringComparison.OrdinalIgnoreCase);
        }

        // ══════════════════════════════════════════════════════════════════════

        private static List<string> SplitCsv(string line)
        {
            var o = new List<string>(); var c = new System.Text.StringBuilder(); bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '"')
                {
                    if (q && i + 1 < line.Length && line[i + 1] == '"') { c.Append('"'); i++; }
                    else q = !q;
                }
                else if (ch == ',' && !q) { o.Add(c.ToString()); c.Clear(); }
                else c.Append(ch);
            }
            o.Add(c.ToString());
            return o;
        }
    }
}
