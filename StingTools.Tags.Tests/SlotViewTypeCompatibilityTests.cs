using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-63. The producer's SLOT-3 check compared <c>View.ViewType.ToString()</c>
    /// ("FloorPlan", "ThreeD", "CeilingPlan", "DraftingView") with the slot's
    /// STING term ("Plan", "3D", "RCP", "Schematic") raw, so most sheets carried
    /// a spurious "type mismatch" warning. The producer and the placement bridge
    /// now share <see cref="SlotViewTypeCompatibility"/>.
    /// </summary>
    public class SlotViewTypeCompatibilityTests
    {
        // Revit 2025 Autodesk.Revit.DB.ViewType member names.
        private static readonly string[] RevitViewTypes =
        {
            "FloorPlan", "CeilingPlan", "Elevation", "ThreeD", "Schedule", "DrawingSheet",
            "ProjectBrowser", "Report", "DraftingView", "Legend", "EngineeringPlan", "AreaPlan",
            "Section", "Detail", "CostReport", "LoadsReport", "PresureLossReport",
            "ColumnSchedule", "PanelSchedule", "Walkthrough", "Rendering",
            "SystemsAnalysisReport", "Internal", "Undefined",
        };

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_DRAWING_TYPES.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate the repo root");
            return dir.FullName;
        }

        private static IEnumerable<(string id, string label, string vt)> ShippedSlots()
        {
            var json = JObject.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "StingTools", "Data", "STING_DRAWING_TYPES.json")));
            foreach (var t in json["drawingTypes"])
            foreach (var slot in t["slots"] ?? Enumerable.Empty<JToken>())
            {
                var vt = slot["viewType"]?.ToString();
                if (!string.IsNullOrWhiteSpace(vt))
                    yield return (t["id"]?.ToString(), slot["label"]?.ToString(), vt);
            }
        }

        [Theory]
        [InlineData("FloorPlan", "Plan")]
        [InlineData("AreaPlan", "Plan")]
        [InlineData("EngineeringPlan", "Plan")]
        [InlineData("CeilingPlan", "RCP")]
        [InlineData("ThreeD", "3D")]
        [InlineData("ThreeD", "ISO")]
        [InlineData("DraftingView", "Schematic")]
        [InlineData("DraftingView", "Drafting")]
        [InlineData("FloorPlan", "Coordination")]
        [InlineData("ThreeD", "Coordination")]
        [InlineData("Section", "Section")]
        [InlineData("Elevation", "Elevation")]
        [InlineData("Schedule", "Schedule")]
        [InlineData("Legend", "Legend")]
        [InlineData("Detail", "Detail")]
        public void Revit_view_type_fits_its_sting_slot_term(string revit, string slot)
            => Assert.True(SlotViewTypeCompatibility.IsCompatible(revit, slot), $"{revit} should fit '{slot}'");

        [Theory]
        [InlineData("Section", "Plan")]
        [InlineData("FloorPlan", "3D")]
        [InlineData("FloorPlan", "RCP")]
        [InlineData("ThreeD", "Section")]
        [InlineData("Section", "Schematic")]
        [InlineData("Schedule", "Plan")]
        public void Wrong_view_type_is_still_a_mismatch(string revit, string slot)
            => Assert.False(SlotViewTypeCompatibility.IsCompatible(revit, slot), $"{revit} should NOT fit '{slot}'");

        [Fact]
        public void Every_shipped_slot_term_accepts_some_view_and_rejects_some_view()
        {
            // An arm that accepts nothing makes every placement a "mismatch";
            // one that accepts everything means the slot declares nothing.
            var offenders = new List<string>();
            foreach (var (id, label, vt) in ShippedSlots())
            {
                int ok = RevitViewTypes.Count(r => SlotViewTypeCompatibility.IsCompatible(r, vt));
                if (ok == 0 || ok == RevitViewTypes.Length)
                    offenders.Add($"{id}: slot '{label}' viewType '{vt}' accepts {ok}/{RevitViewTypes.Length}");
            }
            Assert.True(offenders.Count == 0, string.Join("\n", offenders.Distinct()));
        }

        [Fact]
        public void Every_shipped_slot_term_is_matched_by_a_revit_view_whose_name_differs()
        {
            // The DTW-63 defect in one assertion: for the slot terms that are
            // not spelled like a Revit enum member, a raw ToString() compare
            // can never succeed — the shared predicate must still accept them.
            foreach (var (id, label, vt) in ShippedSlots())
            {
                if (RevitViewTypes.Contains(vt, StringComparer.OrdinalIgnoreCase)) continue;
                Assert.True(RevitViewTypes.Any(r => SlotViewTypeCompatibility.IsCompatible(r, vt)),
                    $"{id}: slot '{label}' viewType '{vt}' is accepted by no Revit view type");
            }
        }

        [Fact]
        public void Producer_does_not_compare_the_revit_enum_name_with_the_slot_term_raw()
        {
            var src = File.ReadAllText(Path.Combine(RepoRoot(), "StingTools", "Core", "Drawing", "DrawingProducer.cs"));
            Assert.DoesNotContain("string.Equals(vChk.ViewType.ToString(), sp.Slot.ViewType", src);
            Assert.Contains("SlotViewTypeCompatibility.IsCompatible", src);
        }

        [Fact]
        public void Placement_bridge_delegates_to_the_shared_predicate()
        {
            var src = File.ReadAllText(Path.Combine(RepoRoot(), "StingTools", "Core", "Drawing", "SheetPlacementBridge.cs"));
            Assert.Contains("SlotViewTypeCompatibility.IsCompatible", src);
        }
    }
}
