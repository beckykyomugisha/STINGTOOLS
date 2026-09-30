// fix/review-electrical — the ids the arc-flash and LPS commands stamped
// ("elec-arc-flash-schedule", "elec-arc-flash-labels", "elec-lps-coverage-A3") were in no
// drawing type, so those views belonged to no type and no sheet; the door-diagram sheet
// was keyed by board name, so a rename minted a second sheet; and the drainage schematic
// was the only plumbing type numbered PH-. These pin the fixes against the shipped JSON.

using System;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class ElectricalDrawingRouteTests
    {
        private static DrawingTypeLibrary Shipped() => DrawingCatalogueFixture.Shipped();

        private static DrawingType Route(DrawingTypeLibrary lib, string disc, string docType)
        {
            var ids = lib.DrawingTypes.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
            var id = DrawingRoutingMatcher.FirstMatchId(lib.Routing, disc, "*", docType, null, null,
                r => string.IsNullOrEmpty(r.OptionMatches), i => ids.ContainsKey(i));
            return id == null ? null : ids[id];
        }

        [Theory]
        [InlineData("ARC_FLASH_SCHEDULE", "elec-arc-flash-schedule", DrawingPurpose.Schedule)]
        [InlineData("ARC_FLASH_LABELS", "elec-arc-flash-labels", DrawingPurpose.Schematic)]
        [InlineData("LPS_COVERAGE", "elec-lps-coverage-A3", DrawingPurpose.Schematic)]
        public void The_formerly_dangling_stamp_ids_are_drawing_types_routed_by_their_key(string docType, string id, string purpose)
        {
            var lib = Shipped();
            var t = Route(lib, "E", docType);
            Assert.True(t != null, $"E / {docType} routes nowhere");
            Assert.Equal(id, t.Id);
            Assert.Equal(purpose, t.Purpose);
            Assert.False(string.IsNullOrWhiteSpace(t.SheetNumberPattern));
            Assert.False(string.IsNullOrWhiteSpace(t.TitleBlockFamily));

            var req = DrawingRouteRequests.All.Single(r => r.DocType == docType);
            Assert.Equal("E", req.Discipline);
            Assert.Equal(id, req.FallbackDrawingTypeId);
        }

        [Fact]
        public void Every_request_falls_back_to_a_shipped_type()
        {
            var ids = Shipped().DrawingTypes.Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = DrawingRouteRequests.All.Where(r => !ids.Contains(r.FallbackDrawingTypeId))
                .Select(r => $"{r.Caller}: {r.FallbackDrawingTypeId}").ToList();
            Assert.True(missing.Count == 0, "Requests whose fall-back id is in no drawing type: " + string.Join(", ", missing));
        }

        [Fact]
        public void The_arc_flash_schedule_is_a_schedule_request_with_production_rules()
        {
            Assert.Contains(DrawingRouteRequests.ArcFlashSchedule, DrawingRouteRequests.Schedules);
            Assert.DoesNotContain(DrawingRouteRequests.ArcFlashSchedule, DrawingRouteRequests.DiagramGenerators);
            var t = Shipped().DrawingTypes.Single(x => x.Id == "elec-arc-flash-schedule");
            Assert.NotNull(t.ProductionRules);
            Assert.NotEmpty(t.ProductionRules);
        }

        [Fact]
        public void The_lps_coverage_view_is_stamped_but_left_for_a_person_to_place()
        {
            Assert.Contains(DrawingRouteRequests.LpsCoverage, DrawingRouteRequests.UnplacedGenerators);
            Assert.DoesNotContain(DrawingRouteRequests.LpsCoverage, DrawingRouteRequests.DiagramGenerators);
            Assert.Same(DrawingRouteRequests.LpsCoverage, DrawingRouteRequests.GeneratorFor("elec-lps-coverage-A3"));
            string why = DrawingRouteRequests.SchematicNotProducedReason("elec-lps-coverage-A3");
            Assert.Contains("LPS_PlanVisualise", why);
            Assert.Contains("place it on a sheet", why);
            Assert.Contains("Elec_ArcFlashLabels", DrawingRouteRequests.SchematicNotProducedReason("elec-arc-flash-labels"));
        }

        [Fact]
        public void Plumbing_schematics_share_the_P_prefix()
        {
            var t = Shipped().DrawingTypes.Single(x => x.Id == "plumb-drainage-schematic-A1");
            Assert.StartsWith("P-", t.SheetNumberPattern);
            Assert.DoesNotContain(Shipped().DrawingTypes, x => (x.SheetNumberPattern ?? "").StartsWith("PH-", StringComparison.Ordinal));
        }

        [Fact]
        public void Sheet_number_patterns_are_not_shared_by_the_new_types()
        {
            var lib = Shipped();
            foreach (var id in new[] { "elec-arc-flash-schedule", "elec-arc-flash-labels", "elec-lps-coverage-A3", "plumb-drainage-schematic-A1" })
            {
                var pattern = lib.DrawingTypes.Single(t => t.Id == id).SheetNumberPattern;
                Assert.Single(lib.DrawingTypes, t => t.SheetNumberPattern == pattern);
            }
        }
    }

    public class BoardNamingTests
    {
        [Theory]
        [InlineData("DB-L1", "Type A", 7, "DB-L1")]
        [InlineData("  DB-L1 ", "Type A", 7, "DB-L1")]
        [InlineData("", "Type A", 7, "Type A")]
        [InlineData(null, "  ", 7, "Board 7")]
        public void The_board_name_is_panel_name_then_element_name(string panelName, string elementName, long id, string expected)
            => Assert.Equal(expected, BoardNaming.Resolve(panelName, elementName, id));

        [Fact]
        public void Door_diagram_sheets_are_keyed_by_element_id_so_a_rename_keeps_the_sheet()
        {
            Assert.Equal(BoardNaming.DoorDiagramSheetTag(42), BoardNaming.DoorDiagramSheetTag(42));
            Assert.NotEqual(BoardNaming.DoorDiagramSheetTag(42), BoardNaming.DoorDiagramSheetTag(43));
            Assert.DoesNotContain("DB-L1", BoardNaming.DoorDiagramSheetTag(42));
            // Distinct from the panel-schedule sheet of the same board.
            Assert.NotEqual(BoardNaming.ScheduleSheetTag(42), BoardNaming.DoorDiagramSheetTag(42));
            Assert.Equal("PANEL-42", BoardNaming.ScheduleSheetTag(42));
        }

        [Fact]
        public void A_name_keyed_sheet_from_before_is_adopted_but_never_another_boards_id_sheet()
        {
            Assert.True(BoardNaming.IsLegacyDoorDiagramTagFor("PANEL-DOOR:DB-L1", "DB-L1"));
            Assert.True(BoardNaming.IsLegacyDoorDiagramTagFor("PANEL-DOOR:DB-L1", " DB-L1 "));
            Assert.False(BoardNaming.IsLegacyDoorDiagramTagFor("PANEL-DOOR:DB-L2", "DB-L1"));
            Assert.False(BoardNaming.IsLegacyDoorDiagramTagFor(BoardNaming.DoorDiagramSheetTag(42), "#42"));
            Assert.False(BoardNaming.IsLegacyDoorDiagramTagFor("", "DB-L1"));
            Assert.False(BoardNaming.IsLegacyDoorDiagramTagFor("PANEL-DOOR:", ""));
        }
    }
}
