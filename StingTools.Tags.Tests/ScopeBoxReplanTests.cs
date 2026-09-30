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
