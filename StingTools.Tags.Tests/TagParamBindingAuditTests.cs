// Tests for TagParamBindingAudit - the gate that stops a tag label being bound
// to a parameter its host category does not carry.
//
// The shipped-data test is the point. A tag showing a blank line is invisible
// to every other check in this repo: the family exists, the category resolves,
// the parameter exists, the build is green, and the drawing is wrong.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.Tags;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class TagParamBindingAuditTests
    {
        // ── spec parsing ─────────────────────────────────────────────────────

        [Fact]
        public void PipeSeparatedCategoriesParse()
        {
            HashSet<string> uni;
            var spec = TagParamBindingAudit.ParseSpec(
                new[] { "# comment", "STR_X,Floors|Structural Columns", "" }, out uni);

            Assert.Equal(new[] { "Floors", "Structural Columns" }, spec["STR_X"].OrderBy(x => x));
            Assert.Empty(uni);
        }

        [Fact]
        public void AllMeansUniversalNotACategoryNamedAll()
        {
            HashSet<string> uni;
            var spec = TagParamBindingAudit.ParseSpec(new[] { "RGL_STD_TXT,<ALL>" }, out uni);

            Assert.Contains("RGL_STD_TXT", uni);
            Assert.False(spec.ContainsKey("RGL_STD_TXT"));
        }

        [Fact]
        public void AllPlusExtrasIsUniversalAndKeepsTheExtras()
        {
            // "<ALL>|Project Information" (PRJ_* parameters): universal, and the
            // category outside the universal set is not lost.
            HashSet<string> uni;
            var spec = TagParamBindingAudit.ParseSpec(new[] { "PRJ_NAME_TXT,<ALL>|Project Information" }, out uni);

            Assert.Contains("PRJ_NAME_TXT", uni);
            Assert.Equal(new[] { "Project Information" }, spec["PRJ_NAME_TXT"]);
        }

        [Fact]
        public void ShippedSpecPutsEveryProjectLevelParamOnProjectInformation()
        {
            // ROADMAP PARAM-6 (closed 2026-09-25): a PRJ_* parameter outside the
            // sheet-identity family must reach Project Information, where its readers go.
            string data = DataDir();
            Assert.True(data != null, "StingTools/Data not found");
            HashSet<string> uni;
            var spec = TagParamBindingAudit.ParseSpec(
                File.ReadLines(Path.Combine(data, "RESOLVED_BINDINGS.csv")), out uni);

            var names = uni.Concat(spec.Keys)
                .Where(n => n.StartsWith("PRJ_", StringComparison.Ordinal)
                            && !n.StartsWith("PRJ_TB_", StringComparison.Ordinal)
                            && !n.StartsWith("PRJ_SHEET_", StringComparison.Ordinal)
                            && !n.StartsWith("PRJ_DWG_", StringComparison.Ordinal)
                            && n != "PRJ_STATUS_COD_TXT")
                .Distinct().ToList();
            Assert.True(names.Count > 50, $"only {names.Count} project-level params parsed");
            var missing = names.Where(n => !spec.TryGetValue(n, out var c) || !c.Contains("Project Information")).ToList();
            Assert.True(missing.Count == 0, "not on Project Information: " + string.Join(", ", missing));
        }

        // ── audit logic ──────────────────────────────────────────────────────

        private static Dictionary<string, TagFamilyLabels> Fam(string name, string cat, params string[] ps)
        {
            var f = new TagFamilyLabels { Name = name, Category = cat };
            foreach (var p in ps) f.Params.Add(p);
            return new Dictionary<string, TagFamilyLabels>(StringComparer.Ordinal) { [name] = f };
        }

        [Fact]
        public void AParamBoundElsewhereIsReported()
        {
            HashSet<string> uni;
            var spec = TagParamBindingAudit.ParseSpec(new[] { "STR_X,Floors" }, out uni);

            var bad = TagParamBindingAudit.Audit(
                Fam("Rebar Tag", "Structural Area Reinforcement", "STR_X"), spec, uni);

            var one = Assert.Single(bad);
            Assert.Equal("STR_X", one.Param);
            Assert.Contains("Floors", one.BoundTo);
        }

        [Fact]
        public void AParamBoundToTheRightCategoryIsNotReported()
        {
            HashSet<string> uni;
            var spec = TagParamBindingAudit.ParseSpec(new[] { "STR_X,Floors|Structural Rebar" }, out uni);
            Assert.Empty(TagParamBindingAudit.Audit(Fam("Rebar Tag", "Structural Rebar", "STR_X"), spec, uni));
        }

        [Fact]
        public void UniversalParamsAreNeverReported()
        {
            HashSet<string> uni;
            var spec = TagParamBindingAudit.ParseSpec(new[] { "RGL_STD_TXT,<ALL>" }, out uni);
            Assert.Empty(TagParamBindingAudit.Audit(Fam("Any Tag", "Doors", "RGL_STD_TXT"), spec, uni));
        }

        [Fact]
        public void AParamMissingFromTheSpecEntirelyIsReported()
        {
            // Worse than a mis-scoped binding: nothing binds it anywhere.
            HashSet<string> uni;
            var spec = TagParamBindingAudit.ParseSpec(new string[0], out uni);

            var one = Assert.Single(TagParamBindingAudit.Audit(Fam("T", "Doors", "STR_GHOST"), spec, uni));
            Assert.Contains("absent from the spec", one.BoundTo);
        }

        [Fact]
        public void MultiCategoryFamiliesAreSkipped()
        {
            // They serve many hosts by design, so "its own category" has no
            // single answer - see the LPS reuse families.
            HashSet<string> uni;
            var spec = TagParamBindingAudit.ParseSpec(new[] { "ELC_X,Roofs" }, out uni);
            Assert.Empty(TagParamBindingAudit.Audit(Fam("LPS Tag", "Multi-Category", "ELC_X"), spec, uni));
        }

        [Fact]
        public void TierGateBooleansAreNotSharedParameters()
        {
            HashSet<string> uni;
            var spec = TagParamBindingAudit.ParseSpec(new string[0], out uni);
            Assert.Empty(TagParamBindingAudit.Audit(Fam("T", "Doors", "TAG_PARA_STATE_2_BOOL"), spec, uni));
        }

        [Fact]
        public void NullsAreNotAnError()
        {
            HashSet<string> uni;
            TagParamBindingAudit.ParseSpec(null, out uni);
            Assert.Empty(TagParamBindingAudit.Audit(null, null, null));
        }

        // ── family parsing, both dialects ────────────────────────────────────

        [Fact]
        public void LabelRowsAreCollectedForTheProseDialect()
        {
            var f = TagParamBindingAudit.ParseFamilies(new[]
            {
                "Tag Family #7: STING - Structural Rebar Tag",
                "TAG7: STR_TAG_7_PARA_REBAR_TXT  •  Category: Structural Rebar",
                "#,Tier,Parameter,Prefix,Suffix,Spc,Brk",
                "1,T1,ASS_TAG_1_TXT,,,0,x",
                "2,T1,STR_BAR_MARK_TXT,Mark:,,0,x",
                "3,T2,STR_REBAR_SIZE_MM,Size:,mm,0,x",
            });

            var e = f[TagCategoryNameForms.NormaliseKey("STING - Structural Rebar Tag")];
            Assert.Equal("Structural Rebar", e.Category);
            Assert.Equal(new[] { "ASS_TAG_1_TXT", "STR_BAR_MARK_TXT", "STR_REBAR_SIZE_MM" }, e.Params);
        }

        [Fact]
        public void TheRowDialectContributesItsCategory()
        {
            var f = TagParamBindingAudit.ParseFamilies(new[]
            {
                "TAG_FAMILY,STING - Autoclave Tag,H,Medical Equipment,1,CEQ_X,desc",
            });
            Assert.Equal("Medical Equipment",
                         f[TagCategoryNameForms.NormaliseKey("STING - Autoclave Tag")].Category);
        }

        // ── the shipped data ─────────────────────────────────────────────────

        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            return dir == null ? null : Path.Combine(dir.FullName, "StingTools", "Data");
        }

        [Fact]
        public void EveryTagLabelParameterReachesItsOwnCategory()
        {
            // THE GATE. This would have failed with 128 families on 2026-09-21.
            //
            // The three exemptions are families whose declared category is not in
            // PARAMETER_REGISTRY.json's category_enum_map, so no binding can be
            // resolved for them at all. "Materials" is deliberate - OST_Materials
            // cannot take bound parameters, documented in SharedParamGuids. "Bolt"
            // and "Weld" resolve as TAG categories in Revit (the 2026-09-21 audit
            // placed both, and neither was UNRESOLVED) but have no model-category
            // entry, so their discipline parameters cannot be bound. That is a real
            // limitation, named here rather than hidden.
            var knownUnbindable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Materials", "Bolt", "Weld",
            };

            string data = DataDir();
            Assert.True(data != null && Directory.Exists(data), "StingTools/Data not found");

            string specPath = Path.Combine(data, "RESOLVED_BINDINGS.csv");
            Assert.True(File.Exists(specPath), "RESOLVED_BINDINGS.csv is missing");

            HashSet<string> universal;
            var spec = TagParamBindingAudit.ParseSpec(File.ReadLines(specPath), out universal);
            Assert.True(spec.Count > 500, $"binding spec looks wrong: {spec.Count} scoped params");

            var families = new Dictionary<string, TagFamilyLabels>(StringComparer.Ordinal);
            foreach (var f in Directory.GetFiles(data, "STING_TAG_CONFIG_v5_0_*.csv"))
                foreach (var kv in TagParamBindingAudit.ParseFamilies(File.ReadLines(f)))
                    if (!families.ContainsKey(kv.Key)) families[kv.Key] = kv.Value;

            Assert.True(families.Count > 190, $"expected the tag config, found {families.Count} families");

            var bad = TagParamBindingAudit.Audit(families, spec, universal)
                .Where(b => !knownUnbindable.Contains(b.HostCategory))
                .ToList();

            var byFamily = bad.GroupBy(b => b.FamilyName)
                              .OrderByDescending(g => g.Count())
                              .Select(g => $"{g.Key} [{g.First().HostCategory}]: " +
                                           string.Join(", ", g.Select(b => b.Param).Take(5)))
                              .ToList();

            Assert.True(bad.Count == 0,
                        $"{bad.Count} label parameter(s) across {byFamily.Count} families cannot reach " +
                        "their own category, so those labels render blank:\n  " +
                        string.Join("\n  ", byFamily.Take(20)));
        }

        [Fact]
        public void TheReinforcementTagsShowABarMark()
        {
            // BS 8666 / BS 1192 detailing: the bar mark is the identifier a steel
            // fixer reads. It shipped at Tier 7, gated behind TAG_PARA_STATE_7_BOOL.
            //
            // An earlier version of this comment said the catalogue "only carries
            // tiers 1-2, so it could never render". That was wrong, and the
            // correction matters because it changes what else you would go looking
            // for: tag_style_catalogue.json declares depth_tiers 1-10 and all ten
            // TAG_PARA_STATE_n_BOOL gates are declared in MR_PARAMETERS.txt. What is
            // true is narrower - every per-discipline default is depth_tier 2, the
            // pre-created type variants cover depth 1 and 2 only (tier 3 was dropped
            // 2026-09-17, commit 94704aa6d), and the master carries two ticked gates.
            // So a Tier 7 row renders only if someone deliberately adds and ticks
            // TAG_PARA_STATE_7_BOOL. It is invisible on every default path, which is
            // the wrong place for the primary identifier on a rebar drawing.
            //
            // Deep tiers are progressive disclosure BY DESIGN and most parameters
            // belong there - a cost quote reference at T5 is correct. This is a
            // classification error about one parameter, not a fault in the tier
            // system. A sweep for other identifiers buried at T3+ returned only
            // supplementary data, so the rebar case stands alone.
            string data = DataDir();
            var families = new Dictionary<string, TagFamilyLabels>(StringComparer.Ordinal);
            foreach (var f in Directory.GetFiles(data, "STING_TAG_CONFIG_v5_0_*.csv"))
                foreach (var kv in TagParamBindingAudit.ParseFamilies(File.ReadLines(f)))
                    if (!families.ContainsKey(kv.Key)) families[kv.Key] = kv.Value;

            foreach (var name in new[]
            {
                "STING - Structural Rebar Tag",
                "STING - Structural Area Reinforcement Tag",
                "STING - Structural Path Reinforcement Tag",
                "STING - Structural Fabric Reinforcement Tag",
                "STING - Structural Rebar Couplers Tag",
            })
            {
                string key = TagCategoryNameForms.NormaliseKey(name);
                Assert.True(families.ContainsKey(key), name + " is not in the tag config");
                Assert.Contains("STR_BAR_MARK_TXT", families[key].Params);
            }
        }

        [Fact]
        public void TheBs8666BendingScheduleFieldsExistAndAreBound()
        {
            // BS 8666:2020 bar bending schedule columns. Before 2026-09-21 the
            // bar counts, the cutting length, the A-E bending dimensions and the
            // scheduling radius did not exist as parameters at all, so no
            // schedule could carry them and the "Reinforcement Schedule" was
            // built over Structural Framing with no rebar fields in it.
            //
            // A parameter declared in MR_PARAMETERS.txt but bound to nothing is
            // the same defect one layer down - it exists, and no element carries
            // it - so this asserts BOTH.
            var bs8666 = new[]
            {
                "STR_REBAR_MEMBER_REF_TXT", "STR_BAR_MARK_TXT", "CST_S_REI_TYPE_TXT",
                "STR_REBAR_SIZE_MM", "STR_REBAR_NO_OF_MBRS_NR", "STR_REBAR_BARS_IN_EACH_NR",
                "STR_REBAR_TOTAL_NO_NR", "STR_REBAR_CUT_LENGTH_MM", "STR_REBAR_SHAPE_TXT",
                "STR_REBAR_DIM_A_MM", "STR_REBAR_DIM_B_MM", "STR_REBAR_DIM_C_MM",
                "STR_REBAR_DIM_D_MM", "STR_REBAR_DIM_E_MM", "STR_REBAR_BEND_RADIUS_MM",
                "CST_S_REI_WEIGHT_KG",
            };

            string data = DataDir();
            Assert.True(data != null, "StingTools/Data not found");

            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(Path.Combine(data, "MR_PARAMETERS.txt")))
            {
                if (!line.StartsWith("PARAM" + "	", StringComparison.Ordinal)) continue;
                var f = line.Split('	');
                if (f.Length > 2 && f[2].Length > 0) declared.Add(f[2]);
            }

            var undeclared = bs8666.Where(p => !declared.Contains(p)).ToList();
            Assert.True(undeclared.Count == 0,
                        "BS 8666 fields absent from MR_PARAMETERS.txt:\n  " +
                        string.Join(", ", undeclared));

            HashSet<string> universal;
            var spec = TagParamBindingAudit.ParseSpec(
                File.ReadLines(Path.Combine(data, "RESOLVED_BINDINGS.csv")), out universal);

            var unbound = bs8666
                .Where(p => !universal.Contains(p))
                .Where(p => !spec.ContainsKey(p) || !spec[p].Contains("Structural Rebar"))
                .ToList();
            Assert.True(unbound.Count == 0,
                        "BS 8666 fields not bound to Structural Rebar:\n  " +
                        string.Join(", ", unbound));

            // And the schedule actually asks for them.
            var sched = File.ReadAllText(Path.Combine(data, "MR_SCHEDULES.csv"));
            Assert.Contains("Reinforcement Schedule,Structural Rebar", sched);
            foreach (var p in bs8666)
                Assert.True(sched.Contains(p), "the Reinforcement Schedule omits " + p);
        }

        [Fact]
        public void EveryScheduleFieldReachesItsScheduleCategory()
        {
            // The same defect as the tag gate, one layer over. A schedule field
            // bound to a parameter the schedule's category does not carry renders
            // an EMPTY COLUMN - no error, no warning, a blank column on an issued
            // schedule.
            //
            // Measured 2026-09-21: 143 field references across 210 schedule rows
            // could not render, worst on Generic Models (66), Walls (32) and
            // Electrical Equipment (17). The shipped "View Template Schedule" and
            // "Spare Parameters Schedule" were entirely blank.
            //
            // The 16 names exempted below are Revit BUILT-INS on a sheet schedule
            // (Sheet_Number, Drawn_By, Scale and friends). They are not shared
            // parameters and never will be; a schedule addresses them by their
            // native name.
            var revitBuiltIns = new HashSet<string>(StringComparer.Ordinal)
            {
                "Sheet_Number", "Sheet_Name", "Discipline", "Scale", "Paper_Size",
                "Revision", "Rev_Date", "Status", "Drawn_By", "Checked_By",
                "Approved_By", "Location", "Remarks",
            };

            string data = DataDir();
            Assert.True(data != null, "StingTools/Data not found");

            HashSet<string> universal;
            var spec = TagParamBindingAudit.ParseSpec(
                File.ReadLines(Path.Combine(data, "RESOLVED_BINDINGS.csv")), out universal);

            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(Path.Combine(data, "MR_PARAMETERS.txt")))
            {
                if (!line.StartsWith("PARAM" + "	", StringComparison.Ordinal)) continue;
                var f = line.Split('	');
                if (f.Length > 2 && f[2].Length > 0) declared.Add(f[2]);
            }

            var registry = Newtonsoft.Json.Linq.JObject.Parse(
                File.ReadAllText(Path.Combine(data, "PARAMETER_REGISTRY.json")));
            var knownCats = new HashSet<string>(
                ((Newtonsoft.Json.Linq.JObject)registry["category_enum_map"]).Properties().Select(x => x.Name),
                StringComparer.Ordinal);

            var bad = new List<string>();
            int rows = 0;
            foreach (var line in File.ReadLines(Path.Combine(data, "MR_SCHEDULES.csv")))
            {
                if (!line.StartsWith("SCHEDULE,", StringComparison.Ordinal)) continue;
                var f = TagConfigDeclarations.SplitCsv(line);
                if (f.Count < 8) continue;
                rows++;

                string cat = f[4].Trim();
                // A schedule over something Revit has no category for cannot be
                // judged here - reported by its own check, not silently passed.
                if (!knownCats.Contains(cat)) continue;

                foreach (var raw in f[7].Split(','))
                {
                    string p = raw.Trim();
                    if (p.Length == 0 || revitBuiltIns.Contains(p)) continue;
                    if (universal.Contains(p)) continue;

                    if (!declared.Contains(p))
                    { bad.Add($"{f[3].Trim()} [{cat}]: {p} is not a declared parameter"); continue; }

                    HashSet<string> cats;
                    if (!spec.TryGetValue(p, out cats) || !cats.Contains(cat))
                        bad.Add($"{f[3].Trim()} [{cat}]: {p} is not bound there");
                }
            }

            Assert.True(rows > 150, $"expected the schedule set, parsed {rows} rows");
            Assert.True(bad.Count == 0,
                        $"{bad.Count} schedule field(s) would render an empty column:\n  " +
                        string.Join("\n  ", bad.Take(20)));
        }
    }
}
