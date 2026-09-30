// Which drawing types a discipline's per-level plans are: the Project Setup Wizard's
// documentation phase, the HVAC panel's "Per-level + sheets" and the default ticks in
// "Produce & Export" all ask this, through the same routing walk DrawingDispatcher runs.

using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class DisciplinePlanRoutingTests
    {
        // The shipped routing table, walked by the same code DrawingDispatcher.Resolve runs.
        private static Func<string, string, DrawingType> ShippedRoute()
        {
            var lib = DrawingCatalogueFixture.Shipped();
            var byId = lib.DrawingTypes.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
            return (disc, docType) =>
            {
                var id = DrawingRoutingMatcher.FirstMatchId(lib.Routing, disc, null, docType, null, null,
                    null, x => x != null && byId.ContainsKey(x));
                return id == null ? null : byId[id];
            };
        }

        [Fact]
        public void Each_MEP_discipline_routes_its_plan_to_a_per_level_plan_type()
        {
            var r = DisciplinePlanRouting.Select(new[] { "M", "E", "P", "FP", "MG" }, ShippedRoute());
            Assert.Empty(r.Unrouted);
            Assert.Empty(r.NotPerLevel);
            var byDisc = r.Picks.Where(p => p.DocType == "PLAN").ToDictionary(p => p.Discipline, p => p.Type.Id);
            Assert.Equal("mep-plan-A1-1to100", byDisc["M"]);
            Assert.Equal("elec-power-A1-1to100", byDisc["E"]);
            Assert.Equal("plumb-drainage-A1-1to100", byDisc["P"]);
            Assert.Equal("fire-sprinkler-layout-A1-1to100", byDisc["FP"]);
            Assert.Equal("health-medgas-pln-A1-1to100", byDisc["MG"]);
            Assert.All(r.Types, t => Assert.True(DisciplinePlanRouting.IsPerLevel(t), t.Id + " is " + t.Purpose));
        }

        [Fact]
        public void No_phase_means_the_presentation_and_fabrication_variants_are_not_picked()
        {
            var r = DisciplinePlanRouting.Select(new[] { "M" }, ShippedRoute());
            Assert.DoesNotContain(r.Types, t => t.Id == "mep-plan-presentation" || t.Id == "mep-plan-fabrication"
                                                || t.Id == "mep-plan-technical");
        }

        [Fact]
        public void Architecture_gets_its_plan_and_ceiling_plan()
        {
            var r = DisciplinePlanRouting.Select(new[] { "A" }, ShippedRoute());
            Assert.Equal(new[] { "arch-plan-A1-1to100", "arch-rcp-A1-1to100" }, r.Types.Select(t => t.Id).ToArray());
        }

        [Fact]
        public void A_discipline_with_no_plan_route_is_reported_not_dropped()
        {
            var r = DisciplinePlanRouting.Select(new[] { "M", "LV", "G" }, ShippedRoute());
            Assert.Equal(new[] { "LV", "G" }, r.Unrouted.ToArray());
            Assert.Contains(r.Types, t => t.Id == "mep-plan-A1-1to100");
        }

        [Fact]
        public void A_route_to_a_section_is_reported_and_not_produced_per_level()
        {
            var riser = new DrawingType { Id = "elec-riser-A3-1to200", Purpose = DrawingPurpose.Section };
            var r = DisciplinePlanRouting.Select(new[] { "E" }, (d, k) => k == "PLAN" ? riser : null);
            Assert.Empty(r.Picks);
            Assert.Single(r.NotPerLevel);
            Assert.Contains("elec-riser-A3-1to200", r.NotPerLevel[0]);
            Assert.Empty(r.Unrouted); // it routed; it is just not a plan
        }

        [Fact]
        public void Repeated_and_blank_disciplines_are_asked_once_and_types_are_distinct()
        {
            var shared = new DrawingType { Id = "shared-plan", Purpose = DrawingPurpose.Plan };
            int calls = 0;
            var r = DisciplinePlanRouting.Select(new[] { "M", " m ", "", null, "E" },
                (d, k) => { calls++; return k == "PLAN" ? shared : null; });
            Assert.Equal(4, calls); // M and E, PLAN and RCP each
            Assert.Single(r.Types);
            Assert.Equal(new[] { "M", "E" }, r.DisciplinesFor("shared-plan").ToArray());
        }

        [Fact]
        public void FirstMatchId_walks_past_a_dangling_rule_and_honours_the_extra_predicate()
        {
            var routing = new List<DrawingRoutingRule>
            {
                new DrawingRoutingRule { Discipline = "E", DocType = "PLAN", DrawingTypeId = "gone" },
                new DrawingRoutingRule { Discipline = "E", DocType = "PLAN", DrawingTypeId = "blocked" },
                new DrawingRoutingRule { Discipline = "E", DocType = "PLAN", DrawingTypeId = "real" },
            };
            var dangling = new List<string>();
            var id = DrawingRoutingMatcher.FirstMatchId(routing, "E", null, "PLAN", null, null,
                rule => rule.DrawingTypeId != "blocked", x => x != "gone", rule => dangling.Add(rule.DrawingTypeId));
            Assert.Equal("real", id);
            Assert.Equal(new[] { "gone" }, dangling.ToArray());
            Assert.Null(DrawingRoutingMatcher.FirstMatchId(routing, "M", null, "PLAN", null, null, null, x => true));
        }
    }
}
