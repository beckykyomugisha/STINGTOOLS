// Scope-box planner: the Revit-free half.
//
// The Revit API cannot create or resize a scope box, so the planner decides
// everything a person would — which boxes, what size, where, what name — and
// the Revit layer only copies a seed, moves it and renames it. These tests pin
// the decisions against the shipped catalogue and title blocks, so a data edit
// that breaks sizing fails here rather than as a wrong-size box in a model.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class ScopeBoxPlanningTests
    {
        private static string Data(string file)
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "StingTools", "Data"))) d = d.Parent;
            Assert.True(d != null, "could not locate StingTools/Data");
            return Path.Combine(d.FullName, "StingTools", "Data", file);
        }

        private static List<DrawingType> Catalogue()
            => JObject.Parse(File.ReadAllText(Data("STING_DRAWING_TYPES.json")))["drawingTypes"].ToObject<List<DrawingType>>();

        private static Dictionary<string, (double W, double H)> Drawables()
            => ScopeBoxSizing.DrawablesFromTitleBlocks(File.ReadAllText(Data("STING_TITLE_BLOCKS.json")));

        // ── names ────────────────────────────────────────────────────────

        [Theory]
        [InlineData("STING::arch-plan-A1-1to100::L02", ScopeBoxKind.DrawingType)]
        [InlineData("STING-LOC::BLD1",                 ScopeBoxKind.Building)]
        [InlineData("STING-AREA::A1-100-01",           ScopeBoxKind.Area)]
        [InlineData("sting-area::a1-100-01::L02",      ScopeBoxKind.Area)]
        [InlineData("STING-SEED::80x55",               ScopeBoxKind.Seed)]
        [InlineData("Scope Box 1",                     ScopeBoxKind.Plain)]
        [InlineData("",                                ScopeBoxKind.Plain)]
        public void Every_prefix_names_one_kind(string name, ScopeBoxKind kind)
            => Assert.Equal(kind, ScopeBoxNames.Classify(name));

        [Fact]
        public void An_area_name_can_never_be_read_as_a_drawing_type_box()
        {
            // "STING::AREA::A01" would bind to a drawing type called AREA. The area
            // prefix exists so ScopeBoxBinder's "STING::" test never matches it.
            Assert.False(ScopeBoxNames.ComposeArea("A1-100-01", "L02")
                .StartsWith(ScopeBoxNames.DrawingTypePrefix, StringComparison.OrdinalIgnoreCase));
            Assert.False(ScopeBoxNames.SeedPrefix.StartsWith(ScopeBoxNames.DrawingTypePrefix, StringComparison.OrdinalIgnoreCase));
        }

        [Theory]
        [InlineData("A1-100-01", null)]
        [InlineData("BLD1-A1-100-12", "L02")]
        public void Area_names_round_trip(string area, string level)
        {
            var name = ScopeBoxNames.ComposeArea(area, level);
            Assert.True(ScopeBoxNames.TryParseArea(name, out var a, out var l, out var why), why);
            Assert.Equal(area, a);
            Assert.Equal(level, l);
        }

        [Theory]
        [InlineData("STING-AREA::")]
        [InlineData("STING-AREA::has space")]
        [InlineData("STING-AREA::A::B::C")]
        public void A_malformed_area_name_says_why(string name)
        {
            Assert.False(ScopeBoxNames.TryParseArea(name, out _, out _, out var why));
            Assert.Equal(ScopeBoxNames.AreaPatternReason, why);
        }

        [Fact]
        public void A_non_area_name_is_not_an_error()
        {
            Assert.False(ScopeBoxNames.TryParseArea("STING::arch-plan-A1-1to100", out _, out _, out var why));
            Assert.Null(why);
        }

        [Theory]
        [InlineData(80, 55, "STING-SEED::80x55")]
        [InlineData(59.5, 42.04, "STING-SEED::59.5x42")]
        public void Seed_names_print_metres(double w, double d, string expected)
            => Assert.Equal(expected, ScopeBoxNames.ComposeSeed(w, d));

        // ── sizing against the shipped data ──────────────────────────────

        [Fact]
        public void The_shipped_title_blocks_give_A1_landscape_its_drawable_area()
        {
            var dr = Drawables();
            Assert.True(dr.TryGetValue("A1|Landscape", out var a1), "A1 landscape drawable missing");
            Assert.Equal(821, a1.W, 1);
            Assert.Equal(474, a1.H, 1);
        }

        [Fact]
        public void Every_plan_type_that_crops_by_scope_box_gets_a_size()
        {
            // A plan type the planner cannot size is a type the planner silently cannot
            // serve. Every one must resolve against the shipped title blocks.
            var dr = Drawables();
            var candidates = Catalogue().Where(t => ScopeBoxSizing.IsAreaCandidate(t, out _)).ToList();
            Assert.True(candidates.Count >= 40, $"expected the plan catalogue; found {candidates.Count}");
            var bad = new List<string>();
            foreach (var t in candidates)
                if (!ScopeBoxSizing.TryMaxExtent(t, dr, ScopeBoxSizing.DefaultFitFactor, out var w, out var d, out var why) || w <= 0 || d <= 0)
                    bad.Add($"{t.Id}: {why}");
            Assert.True(bad.Count == 0, string.Join("\n", bad));
        }

        [Fact]
        public void An_A1_plan_at_1_to_100_allows_about_53_by_38_metres()
        {
            var t = Catalogue().Single(x => x.Id == "arch-plan-A1-1to100");
            Assert.True(ScopeBoxSizing.TryMaxExtent(t, Drawables(), 0.9, out var w, out var d, out var why), why);
            Assert.Equal(821 * 0.72 * 100 / 1000.0 * 0.9, w, 3);
            Assert.Equal(474 * 0.90 * 100 / 1000.0 * 0.9, d, 3);
            Assert.Equal("A1-100", ScopeBoxSizing.SizeClassKey(t));
        }

        [Fact]
        public void Schedules_and_3D_are_not_area_candidates_and_say_why()
        {
            var cat = Catalogue();
            foreach (var t in cat.Where(x => x.Purpose == "Schedule" || x.Purpose == "3D"))
            {
                Assert.False(ScopeBoxSizing.IsAreaCandidate(t, out var why));
                Assert.False(string.IsNullOrWhiteSpace(why));
            }
        }

        [Fact]
        public void A_paper_the_title_blocks_do_not_describe_is_refused_not_guessed()
        {
            var t = Catalogue().Single(x => x.Id == "arch-plan-A1-1to100");
            var clone = JsonConvert.DeserializeObject<DrawingType>(JsonConvert.SerializeObject(t));
            clone.PaperSize = "B7";
            Assert.False(ScopeBoxSizing.TryMaxExtent(clone, Drawables(), 0.9, out _, out _, out var why));
            Assert.Contains("B7", why);
        }

        // ── seeds ────────────────────────────────────────────────────────

        private static ScopeBoxSeed Seed(string id, double w, double d) => new ScopeBoxSeed { Id = id, Name = ScopeBoxNames.ComposeSeed(w, d), WidthM = w, DepthM = d };

        [Fact]
        public void The_largest_seed_that_fits_is_chosen()
        {
            var seeds = new[] { Seed("s", 20, 15), Seed("m", 50, 35), Seed("l", 80, 55) };
            var (s, rot) = ScopeBoxPlanner.PickSeed(seeds, 53.2, 38.4);
            Assert.Equal("m", s.Id);
            Assert.False(rot);
        }

        [Fact]
        public void A_seed_is_turned_when_only_that_way_fits()
        {
            var (s, rot) = ScopeBoxPlanner.PickSeed(new[] { Seed("p", 35, 50) }, 53.2, 38.4);
            Assert.Equal("p", s.Id);
            Assert.True(rot);
        }

        [Fact]
        public void No_seed_fits_returns_none()
        {
            var (s, _) = ScopeBoxPlanner.PickSeed(new[] { Seed("big", 80, 55) }, 53.2, 38.4);
            Assert.Null(s);
        }

        // ── tiling ───────────────────────────────────────────────────────

        [Theory]
        [InlineData(30, 50, 2, 1)]
        [InlineData(50, 50, 2, 1)]
        [InlineData(51, 50, 2, 2)]
        [InlineData(98, 50, 2, 2)]
        [InlineData(99, 50, 2, 3)]
        public void Boxes_needed_along_a_span(double span, double box, double overlap, int expected)
            => Assert.Equal(expected, ScopeBoxPlanner.CountAlong(span, box, overlap));

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.3)]
        [InlineData(-1.1)]
        public void Tiles_cover_every_point_of_the_footprint(double angle)
        {
            var rnd = new Random(7);
            var pts = Enumerable.Range(0, 200).Select(_ => (rnd.NextDouble() * 140 - 20, rnd.NextDouble() * 90 + 5)).ToList();
            const double w = 50, d = 35, overlap = 2;
            var tiles = ScopeBoxPlanner.Tile(pts, angle, w, d, overlap, 1.0);
            double c = Math.Cos(angle), s = Math.Sin(angle);
            foreach (var (x, y) in pts)
            {
                bool covered = tiles.Any(t =>
                {
                    double du = (x - t.X) * c + (y - t.Y) * s, dv = -(x - t.X) * s + (y - t.Y) * c;
                    return Math.Abs(du) <= w / 2 + 1e-6 && Math.Abs(dv) <= d / 2 + 1e-6;
                });
                Assert.True(covered, $"({x:F1},{y:F1}) is outside every box at angle {angle}");
            }
        }

        [Fact]
        public void Neighbours_overlap_by_exactly_the_overlap_and_read_left_to_right_top_to_bottom()
        {
            var pts = new List<(double, double)> { (0, 0), (130, 70) };
            var tiles = ScopeBoxPlanner.Tile(pts, 0, 50, 35, 2, 0);
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, tiles.Select(t => t.Index).ToArray());
            Assert.Equal(48, tiles[1].X - tiles[0].X, 6);       // 50 - 2
            Assert.Equal(-33, tiles[3].Y - tiles[0].Y, 6);      // next row is below: 35 - 2
            Assert.True(tiles[0].Y > tiles[8].Y);
        }

        [Fact]
        public void A_small_footprint_gets_one_box_centred_on_it()
        {
            var tiles = ScopeBoxPlanner.Tile(new List<(double, double)> { (10, 10), (30, 20) }, 0, 50, 35, 2, 1);
            Assert.Single(tiles);
            Assert.Equal(20, tiles[0].X, 6);
            Assert.Equal(15, tiles[0].Y, 6);
        }

        // ── the plan ─────────────────────────────────────────────────────

        private static ScopeBoxPlanRequest Request(params ScopeBoxSeed[] seeds)
        {
            var cat = Catalogue();
            return new ScopeBoxPlanRequest
            {
                DrawingTypes = cat.Where(t => t.Id == "arch-plan-A1-1to100" || t.Id == "mep-hvac-duct-A1-1to100" || t.Id == "mep-plantroom-A1-1to50").ToList(),
                Drawables = Drawables(),
                Footprints = { new ScopeBoxFootprint { Loc = "BLD1", Points = { (0, 0), (100, 60) } } },
                Seeds = seeds.ToList(),
            };
        }

        [Fact]
        public void Types_sharing_paper_and_scale_share_one_box_size()
        {
            var res = ScopeBoxPlanner.Plan(Request(Seed("a", 50, 35), Seed("b", 20, 8)));
            Assert.Equal(new[] { "A1-50", "A1-100" }, res.Classes.Select(c => c.Key).ToArray());
            var c100 = res.Classes.Single(c => c.Key == "A1-100");
            Assert.Equal(2, c100.DrawingTypeIds.Count);
            Assert.Equal("a", c100.Seed.Id);
            Assert.Equal("b", res.Classes.Single(c => c.Key == "A1-50").Seed.Id);
            Assert.All(res.Boxes, b => Assert.Equal(PlannedBoxStatus.New, b.Status));
            Assert.All(res.Boxes, b => Assert.True(ScopeBoxNames.TryParseArea(b.Name, out _, out _, out _), b.Name));
            Assert.Contains(res.Boxes, b => b.Name == "STING-AREA::BLD1-A1-100-01");
        }

        [Fact]
        public void A_class_with_no_seed_is_planned_but_not_created_and_says_what_to_draw()
        {
            var res = ScopeBoxPlanner.Plan(Request(Seed("a", 50, 35)));
            var missing = Assert.Single(res.MissingSeeds);
            Assert.Equal("A1-50", missing.ClassKey);
            Assert.Contains("Draw a scope box no larger than", missing.Instruction);
            Assert.All(res.Boxes.Where(b => b.ClassKey == "A1-50"), b => Assert.Equal(PlannedBoxStatus.NoSeed, b.Status));
            Assert.True(res.CountToCreate > 0);
        }

        [Fact]
        public void A_box_already_in_the_project_is_not_created_again()
        {
            var req = Request(Seed("a", 50, 35), Seed("b", 20, 8));
            req.ExistingNames.Add("STING-AREA::BLD1-A1-100-01");
            var res = ScopeBoxPlanner.Plan(req);
            Assert.Equal(PlannedBoxStatus.Exists, res.Boxes.Single(b => b.Name == "STING-AREA::BLD1-A1-100-01").Status);
        }

        [Fact]
        public void Per_level_footprints_put_the_level_in_the_name()
        {
            var req = Request(Seed("a", 50, 35), Seed("b", 20, 8));
            req.Footprints[0].Level = "L02";
            var res = ScopeBoxPlanner.Plan(req);
            Assert.All(res.Boxes, b => Assert.EndsWith("::L02", b.Name));
        }

        [Fact]
        public void Two_footprints_with_one_code_are_reported()
        {
            var req = Request(Seed("a", 50, 35), Seed("b", 20, 8));
            req.Footprints.Add(new ScopeBoxFootprint { Loc = "BLD1", Points = { (500, 500), (520, 520) } });
            var res = ScopeBoxPlanner.Plan(req);
            Assert.Contains(res.Warnings, w => w.Contains("planned twice"));
        }

        [Fact]
        public void The_saved_plan_tells_production_what_each_box_is_for()
        {
            var req = Request(Seed("a", 50, 35), Seed("b", 20, 8));
            var res = ScopeBoxPlanner.Plan(req);
            var file = ScopeBoxPlanFile.FromJson(ScopeBoxPlanFile.From(req, res, new[] { "L01", "L02" }, "Buildings").ToJson());

            Assert.True(file.TryResolve("STING-AREA::BLD1-A1-100-01", out var types, out var levels, out var why), why);
            Assert.Contains("arch-plan-A1-1to100", types);
            Assert.Equal(new[] { "L01", "L02" }, levels);

            Assert.False(file.TryResolve("STING-AREA::NOPE-01", out _, out _, out why));
            Assert.Contains("not in the saved plan", why);
            Assert.False(file.TryResolve("STING::arch-plan-A1-1to100", out _, out _, out why));
        }

        [Fact]
        public void Planning_a_second_building_keeps_the_first_buildings_boxes()
        {
            var reqA = Request(Seed("a", 50, 35), Seed("b", 20, 8));
            var fileA = ScopeBoxPlanFile.From(reqA, ScopeBoxPlanner.Plan(reqA), new[] { "L01" }, "Buildings");

            var reqB = Request(Seed("a", 50, 35), Seed("b", 20, 8));
            reqB.Footprints[0].Loc = "BLD2";
            reqB.DrawingTypes = reqB.DrawingTypes.Where(t => t.Id == "arch-plan-A1-1to100").ToList();
            var fileB = ScopeBoxPlanFile.From(reqB, ScopeBoxPlanner.Plan(reqB), new[] { "L02" }, "Buildings");

            var merged = ScopeBoxPlanFile.Merge(fileA, fileB);
            Assert.True(merged.TryResolve("STING-AREA::BLD1-A1-50-01", out _, out _, out var why), why);   // A kept
            Assert.True(merged.TryResolve("STING-AREA::BLD2-A1-100-01", out var types, out var levels, out why), why);
            Assert.Equal(new[] { "arch-plan-A1-1to100" }, types);                                        // B's class replaced A's
            Assert.Equal(new[] { "L02" }, levels);                                                       // settings are the latest
            Assert.Equal(merged.Boxes.Count, merged.Boxes.Select(b => b.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Fact]
        public void Merging_onto_no_saved_plan_is_the_new_plan()
        {
            var req = Request(Seed("a", 50, 35));
            var f = ScopeBoxPlanFile.From(req, ScopeBoxPlanner.Plan(req), null, "Model");
            Assert.Same(f, ScopeBoxPlanFile.Merge(null, f));
        }

        [Fact]
        public void A_malformed_plan_file_throws_rather_than_reading_as_no_plan()
        {
            Assert.Null(ScopeBoxPlanFile.FromJson(""));
            Assert.ThrowsAny<JsonException>(() => ScopeBoxPlanFile.FromJson("{ \"boxes\": [ }"));
        }

        // ── colour ───────────────────────────────────────────────────────

        private static ScopeBoxStyle ShippedStyle() => ScopeBoxStyle.Load(File.ReadAllText(Data("STING_SCOPE_BOX_STYLE.json")), null);

        [Fact]
        public void Every_shipped_colour_is_valid_hex()
        {
            var st = ShippedStyle();
            Assert.True(st.Palette.Count >= 8);
            foreach (var c in st.Palette.Concat(st.DisciplineColours.Values))
                Assert.True(ScopeBoxStyle.TryParseHex(c, out _, out _, out _), c);
        }

        [Fact]
        public void Every_discipline_in_the_catalogue_has_a_colour()
        {
            var st = ShippedStyle();
            var missing = Catalogue().Select(t => t.Discipline).Where(d => !string.IsNullOrEmpty(d)).Distinct()
                .Where(d => !st.DisciplineColours.ContainsKey(d)).ToList();
            Assert.True(missing.Count == 0, "no colour for: " + string.Join(", ", missing));
        }

        [Fact]
        public void The_same_keys_always_get_the_same_distinct_colours()
        {
            var st = ShippedStyle();
            var a = st.Assign(ScopeBoxColourMode.SizeClass, new[] { "A1-100", "A1-50", "A3-20" });
            var b = st.Assign(ScopeBoxColourMode.SizeClass, new[] { "A3-20", "A1-50", "A1-100", "A1-50" });
            Assert.Equal(a.OrderBy(k => k.Key), b.OrderBy(k => k.Key));
            Assert.Equal(3, a.Values.Distinct().Count());
        }

        [Fact]
        public void A_project_style_overrides_only_what_it_names()
        {
            var st = ScopeBoxStyle.Load(File.ReadAllText(Data("STING_SCOPE_BOX_STYLE.json")),
                "{ \"defaults\": { \"overlapM\": 5 }, \"disciplineColours\": { \"A\": \"#000000\" } }");
            Assert.Equal(5, st.Values.OverlapM);
            Assert.Equal(0.9, st.Values.FitFactor);
            Assert.Equal("#000000", st.DisciplineColours["A"]);
            Assert.True(st.DisciplineColours.ContainsKey("M"));
        }

        [Theory]
        [InlineData("#1F77B4", true)]
        [InlineData("1F77B4", true)]
        [InlineData("#1F77B", false)]
        [InlineData("blue", false)]
        public void Hex_colours_parse_or_are_refused(string hex, bool ok)
            => Assert.Equal(ok, ScopeBoxStyle.TryParseHex(hex, out _, out _, out _));

        [Fact]
        public void Colour_keys_come_from_what_the_box_is()
        {
            var area = new ScopeBoxColourSubject { Name = "STING-AREA::ANNEX-A1-100-02::L03", SizeClass = "A1-100", Loc = "ANNEX", Discipline = "A" };
            Assert.Equal("A1-100", area.KeyFor(ScopeBoxColourMode.SizeClass));
            Assert.Equal("ANNEX", area.KeyFor(ScopeBoxColourMode.Building));
            Assert.Equal("L03", area.KeyFor(ScopeBoxColourMode.Level));
            Assert.Equal("Area", area.KeyFor(ScopeBoxColourMode.Kind));
            Assert.Null(area.KeyFor(ScopeBoxColourMode.Off));

            var loc = new ScopeBoxColourSubject { Name = "STING-LOC::BLD2" };
            Assert.Equal("BLD2", loc.KeyFor(ScopeBoxColourMode.Building));
            Assert.Null(loc.KeyFor(ScopeBoxColourMode.SizeClass));
        }
    }
}
