// Scope-box planner: what a re-plan does to boxes already in the model, and
// footprints that are not square to the grid.
//
//   DTW-92  a turned STING-LOC box is tiled in its own frame, not its bounding box
//   DTW-91  model growth must not renumber or shift the boxes already drawn
//   DTW-90  a saved plan remembers levels by UniqueId, so a renamed level keeps its boxes

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class ScopeBoxReplanTests
    {
        private static string Data(string file)
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "StingTools", "Data"))) d = d.Parent;
            Assert.True(d != null, "could not locate StingTools/Data");
            return Path.Combine(d.FullName, "StingTools", "Data", file);
        }

        private static ScopeBoxPlanRequest Request(params ScopeBoxFootprint[] fps)
        {
            var cat = JObject.Parse(File.ReadAllText(Data("STING_DRAWING_TYPES.json")))["drawingTypes"].ToObject<List<DrawingType>>();
            var req = new ScopeBoxPlanRequest
            {
                DrawingTypes = cat.Where(t => t.Id == "arch-plan-A1-1to100").ToList(),
                Drawables = ScopeBoxSizing.DrawablesFromTitleBlocks(File.ReadAllText(Data("STING_TITLE_BLOCKS.json"))),
                Seeds = { new ScopeBoxSeed { Id = "s", Name = "STING-SEED::50x35", WidthM = 50, DepthM = 35 } },
            };
            req.Footprints.AddRange(fps);
            return req;
        }

        private static ScopeBoxFootprint Rect(double x0, double y0, double x1, double y1, string loc = "BLD1")
            => new ScopeBoxFootprint { Loc = loc, Points = { (x0, y0), (x1, y1), (x0, y1), (x1, y0) } };

        // ── DTW-92: a turned building box ───────────────────────────────

        private static ScopeBoxFootprint Turned(double cx, double cy, double w, double d, double angle)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle);
            var fp = new ScopeBoxFootprint { Loc = "BLD1", AngleRad = angle };
            foreach (var (u, v) in new[] { (-w / 2, -d / 2), (w / 2, -d / 2), (w / 2, d / 2), (-w / 2, d / 2) })
                fp.Points.Add((cx + u * c - v * s, cy + u * s + v * c));
            return fp;
        }

        [Fact]
        public void A_turned_building_box_is_tiled_in_its_own_frame()
        {
            // 45 × 30 m turned 30°: one 50 × 35 box covers it in its own frame; its
            // north-square bounding box (54 × 48.5 m) would need four.
            var res = ScopeBoxPlanner.Plan(Request(Turned(100, 100, 45, 30, Math.PI / 6)));
            var box = Assert.Single(res.Boxes);
            Assert.Equal(Math.PI / 6, box.AngleRad, 6);
            Assert.Equal(100, box.CentreX, 6);
            Assert.Equal(100, box.CentreY, 6);
        }

        // ── DTW-91: growth keeps the boxes already drawn ────────────────

        private static void AddExisting(ScopeBoxPlanRequest req, PlannedScopeBox b)
        {
            req.ExistingNames.Add(b.Name);
            req.ExistingBoxes[b.Name] = new ExistingScopeBox
                { CentreX = b.CentreX, CentreY = b.CentreY, WidthM = b.WidthM, DepthM = b.DepthM, AngleRad = b.AngleRad };
        }

        private static ScopeBoxPlanRequest Planned90x30Existing()
        {
            // 90 × 30 m (+1 m padding): two 50 × 35 boxes, -01 west at x = 21, -02 east at x = 69.
            var first = ScopeBoxPlanner.Plan(Request(Rect(0, 0, 90, 30)));
            Assert.Equal(new[] { "STING-AREA::BLD1-A1-100-01", "STING-AREA::BLD1-A1-100-02" }, first.Boxes.Select(b => b.Name).ToArray());
            Assert.Equal(21, first.Boxes[0].CentreX, 6);
            Assert.Equal(69, first.Boxes[1].CentreX, 6);
            var req = Request();
            foreach (var b in first.Boxes) AddExisting(req, b);
            return req;
        }

        [Fact]
        public void Growth_keeps_every_existing_box_where_it_is_and_numbers_new_ones_after_them()
        {
            var req = Planned90x30Existing();
            req.Footprints.Add(Rect(-30, 0, 90, 30));        // the building grew 30 m west
            var res = ScopeBoxPlanner.Plan(req);

            Assert.DoesNotContain(res.Boxes, b => b.Status == PlannedBoxStatus.Moved);
            var b1 = res.Boxes.Single(b => b.Name == "STING-AREA::BLD1-A1-100-01");
            var b2 = res.Boxes.Single(b => b.Name == "STING-AREA::BLD1-A1-100-02");
            Assert.Equal(PlannedBoxStatus.Exists, b1.Status);
            Assert.Equal(PlannedBoxStatus.Exists, b2.Status);
            Assert.Equal(21, b1.CentreX, 6);
            Assert.Equal(69, b2.CentreX, 6);
            var added = res.Boxes.Single(b => b.Status == PlannedBoxStatus.New);
            Assert.Equal("STING-AREA::BLD1-A1-100-03", added.Name);
            Assert.Equal(-27, added.CentreX, 6);                 // on the same 48 m pitch, west of -01
            Assert.Equal(3, res.Boxes.Count);
        }

        [Fact]
        public void A_shrunk_footprint_keeps_the_box_that_still_covers_it_and_names_the_one_it_dropped()
        {
            var req = Planned90x30Existing();
            req.Footprints.Add(Rect(50, 0, 90, 30));         // only the east part is left
            var res = ScopeBoxPlanner.Plan(req);

            // Before: one tile named -01 was planned at x = 70, so the existing -01 was
            // "Moved" 49 m onto the ground -02 already covers.
            var kept = Assert.Single(res.Boxes);
            Assert.Equal("STING-AREA::BLD1-A1-100-02", kept.Name);
            Assert.Equal(PlannedBoxStatus.Exists, kept.Status);
            Assert.Contains(res.Warnings, w => w.Contains("STING-AREA::BLD1-A1-100-01"));
        }

        [Fact]
        public void A_new_tile_never_takes_a_name_already_in_the_model()
        {
            var req = Planned90x30Existing();
            req.ExistingNames.Add("STING-AREA::BLD1-A1-100-03");   // in the model, could not be measured
            req.Footprints.Add(Rect(-30, 0, 90, 30));
            var res = ScopeBoxPlanner.Plan(req);
            var added = res.Boxes.Single(b => b.Status == PlannedBoxStatus.New);
            Assert.Equal("STING-AREA::BLD1-A1-100-04", added.Name);
        }

        [Fact]
        public void With_no_boxes_in_the_model_the_layout_is_centred_and_numbered_as_before()
        {
            var res = ScopeBoxPlanner.Plan(Request(Rect(-30, 0, 90, 30)));
            Assert.Equal(new[] { -18.0, 30.0, 78.0 }, res.Boxes.Select(b => Math.Round(b.CentreX, 6)).ToArray());
            Assert.Equal(new[] { "-01", "-02", "-03" }, res.Boxes.Select(b => b.Name.Substring(b.Name.Length - 3)).ToArray());
        }

        // ── DTW-90: levels by UniqueId ───────────────────────────────────

        private static readonly (long, string, string, string)[] TwoLevels =
        {
            (1, "uid-gf", "Level 0", "L00"),
            (2, "uid-l1", "Level 1", "L01"),
        };

        [Fact]
        public void An_old_plan_with_no_level_ids_codes_levels_as_before()
        {
            var notes = new List<string>();
            var codes = ScopeBoxPlanner.StableLevelCodes(TwoLevels, null, notes);
            Assert.Equal("L00", codes[1]);
            Assert.Equal("L01", codes[2]);
            Assert.Empty(notes);
        }

        [Fact]
        public void A_renamed_level_keeps_the_code_its_boxes_were_named_with()
        {
            var saved = new Dictionary<string, string> { ["L00"] = "uid-gf", ["L01"] = "uid-l1" };
            // "Level 1" renamed "First Floor" → its name now reads F1.
            var levels = new[] { (1L, "uid-gf", "Level 0", "L00"), (2L, "uid-l1", "First Floor", "F1") };
            var notes = new List<string>();
            var codes = ScopeBoxPlanner.StableLevelCodes(levels, saved, notes);
            Assert.Equal("L01", codes[2]);
            Assert.Contains(notes, n => n.Contains("First Floor") && n.Contains("L01"));
        }

        [Fact]
        public void An_inserted_level_that_reads_a_saved_code_does_not_take_it_from_its_level()
        {
            var saved = new Dictionary<string, string> { ["L00"] = "uid-gf", ["L01"] = "uid-l1" };
            // A transfer level below Level 1 whose name also reads L01. Before, codes were
            // handed out by elevation, so it took L01 and Level 1 became L01-2: every
            // STING-AREA::…::L01 box and the plan's ["L01"] then meant the new level.
            var levels = new[] { (1L, "uid-gf", "Level 0", "L00"), (3L, "uid-tr", "Level 1 transfer", "L01"), (2L, "uid-l1", "Level 1", "L01") };
            var notes = new List<string>();
            var codes = ScopeBoxPlanner.StableLevelCodes(levels, saved, notes);
            Assert.Equal("L01", codes[2]);
            Assert.Equal("L01-2", codes[3]);
            Assert.Contains(notes, n => n.Contains("Level 1 transfer") && n.Contains("L01-2"));
        }

        [Fact]
        public void A_saved_level_that_was_deleted_frees_its_code()
        {
            var saved = new Dictionary<string, string> { ["L01"] = "uid-gone" };
            var notes = new List<string>();
            var codes = ScopeBoxPlanner.StableLevelCodes(TwoLevels, saved, notes);
            Assert.Equal("L01", codes[2]);
            Assert.Contains(notes, n => n.Contains("L01") && n.Contains("no longer"));
        }

        [Fact]
        public void The_saved_plan_records_level_ids_and_a_merge_keeps_them()
        {
            var req = Request(Rect(0, 0, 40, 30));
            var res = ScopeBoxPlanner.Plan(req);
            var file = ScopeBoxPlanFile.From(req, res, new[] { "L01" }, "Model", null,
                new Dictionary<string, string> { ["L01"] = "uid-l1" });
            var round = ScopeBoxPlanFile.FromJson(file.ToJson());
            Assert.Equal("uid-l1", round.LevelIds["L01"]);

            var old = ScopeBoxPlanFile.FromJson("{\"schema\":2,\"levels\":[\"L00\"],\"boxes\":[]}");
            Assert.Null(old.LevelIds);                                         // an old plan reads
            var merged = ScopeBoxPlanFile.Merge(
                ScopeBoxPlanFile.FromJson("{\"schema\":2,\"levelIds\":{\"L00\":\"uid-gf\"},\"boxes\":[]}"), file);
            Assert.Equal("uid-gf", merged.LevelIds["L00"]);
            Assert.Equal("uid-l1", merged.LevelIds["L01"]);
        }

        [Fact]
        public void A_footprint_with_no_angle_of_its_own_follows_the_grid()
        {
            var req = Request(Rect(0, 0, 40, 30));
            req.GridAngleRad = 0.1;
            var res = ScopeBoxPlanner.Plan(req);
            Assert.All(res.Boxes, b => Assert.Equal(0.1, b.AngleRad, 9));
        }
    }
}
