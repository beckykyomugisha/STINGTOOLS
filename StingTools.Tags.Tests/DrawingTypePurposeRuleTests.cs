// Elevations, sections and details must not carry the floor plan's rules.
//
// Four drawing types (arch-elev, arch-interior-elev, arch-section, arch-detail) shipped
// with the plan's rule list copied in: area tags where no area can appear, room tags on
// elevations, and the wall-length / opening chains, which dimension each wall's PLAN
// length — walls seen end-on included. Same defect the RCP had. The rule for what does
// not belong lives in AnnotationRuleKinds.NotForPurpose, which the validator (DT-139-PURPOSE)
// also reads, so the catalogue and a project's own drawing types are held to one rule.

using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class DrawingTypePurposeRuleTests
    {
        private static JObject Catalogue()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "StingTools", "Data"))) d = d.Parent;
            Assert.True(d != null, "StingTools/Data not found");
            return JObject.Parse(File.ReadAllText(Path.Combine(d.FullName, "StingTools", "Data", "STING_DRAWING_TYPES.json")));
        }

        [Fact]
        public void No_elevation_section_or_detail_carries_a_plan_only_rule()
        {
            var bad = (from t in Catalogue()["drawingTypes"]
                       from r in (t["annotation"]?["rules"] as JArray) ?? new JArray()
                       where (bool?)r["enabled"] != false
                       let why = AnnotationRuleKinds.NotForPurpose((string)r["ruleType"], (string)r["category"], (string)t["purpose"])
                       where why != null
                       select $"{t["id"]} ({t["purpose"]}): {r["ruleType"]} on {r["category"]} — {why}").ToList();
            Assert.True(bad.Count == 0, "Plan rules copied onto a non-plan drawing type:\n" + string.Join("\n", bad));
        }

        [Fact]
        public void The_rule_is_live_and_lets_real_elevation_rules_through()
        {
            // Controls: the check fires on the copied rules, and stays quiet on the ones that belong.
            Assert.NotNull(AnnotationRuleKinds.NotForPurpose("AutoDimWallLength", "Walls", "Elevation"));
            Assert.NotNull(AnnotationRuleKinds.NotForPurpose("AutoTag", "Areas", "Section"));
            Assert.NotNull(AnnotationRuleKinds.NotForPurpose("AutoTagRoomName", "Rooms", "Detail"));
            Assert.Null(AnnotationRuleKinds.NotForPurpose("AutoTag", "Rooms", "Section"));   // rooms in sections are fine
            Assert.Null(AnnotationRuleKinds.NotForPurpose("AutoDimWallLength", "Walls", "Plan"));
            Assert.Null(AnnotationRuleKinds.NotForPurpose("AutoTag", "Doors", "Elevation"));
            Assert.Null(AnnotationRuleKinds.NotForPurpose("AutoDim", "Levels", "Elevation"));
            Assert.Null(AnnotationRuleKinds.NotForPurpose("MaterialTag", "Walls", "Elevation"));
        }
    }
}
