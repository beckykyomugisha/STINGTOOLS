// ~45 routing docTypes in STING_DRAWING_TYPES.json were never asked for: the
// schematic commands hard-coded their drawing-type ids, so those rules did nothing
// and a project override that re-pointed them was ignored. The diagram generators
// now route through DrawingRouteRequests; these tests keep the table and the
// callers honest in both directions.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class DrawingRouteRequestsTests
    {
        private static DrawingTypeLibrary Shipped() => DrawingCatalogueFixture.Shipped();

        private static DrawingType Route(DrawingTypeLibrary lib, string disc, string docType)
        {
            var ids = lib.DrawingTypes.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
            var id = DrawingRoutingMatcher.FirstMatchId(lib.Routing, disc, "*", docType, null, null,
                r => string.IsNullOrEmpty(r.OptionMatches), i => ids.ContainsKey(i));
            return id == null ? null : ids[id];
        }

        // A literal docType, or the literal inside an anchored ^X$ predicate; null for any other regex.
        private static string Literal(DrawingRoutingRule r)
        {
            if (!string.IsNullOrEmpty(r.DocTypeMatches))
            {
                var m = Regex.Match(r.DocTypeMatches, @"^\^?([A-Za-z0-9_\-]+)\$?$");
                return m.Success ? m.Groups[1].Value : null;
            }
            return r.DocType;
        }

        [Fact]
        public void Every_routing_rule_names_a_drawing_type_that_ships()
        {
            var lib = Shipped();
            var ids = new HashSet<string>(lib.DrawingTypes.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
            var dangling = lib.Routing.Where(r => !ids.Contains(r.DrawingTypeId ?? ""))
                .Select(r => $"{r.Discipline}{r.DisciplineMatches}/{r.DocType}{r.DocTypeMatches} -> {r.DrawingTypeId}").ToList();
            Assert.True(dangling.Count == 0, "Routing rule(s) to no drawing type:\n" + string.Join("\n", dangling));
        }

        [Fact]
        public void Every_routing_docType_is_requested_by_a_caller_or_flagged_with_a_reason()
        {
            var bad = new List<string>();
            foreach (var r in Shipped().Routing)
            {
                if (string.IsNullOrEmpty(r.DocType) && string.IsNullOrEmpty(r.DocTypeMatches)) continue;
                if (r.DocType == "*" && string.IsNullOrEmpty(r.DocTypeMatches)) continue;
                string lit = Literal(r);
                if (lit == null) { bad.Add($"{r.DrawingTypeId}: docTypeMatches '{r.DocTypeMatches}' is not a plain ^CODE$ — review by hand"); continue; }
                if (!DrawingRouteRequests.IsRequested(lit) && !DrawingRouteRequests.Unrequested.ContainsKey(lit))
                    bad.Add($"{lit} (-> {r.DrawingTypeId}): no caller asks for it and it is not in DrawingRouteRequests.Unrequested");
            }
            Assert.True(bad.Count == 0, string.Join("\n", bad.Distinct()));
        }

        [Fact]
        public void A_flag_is_dropped_once_a_caller_asks_and_never_outlives_its_rule()
        {
            var routed = new HashSet<string>(Shipped().Routing.Select(Literal).Where(l => l != null), StringComparer.OrdinalIgnoreCase);
            var stale = DrawingRouteRequests.Unrequested.Keys.Where(k => DrawingRouteRequests.IsRequested(k) || !routed.Contains(k)).ToList();
            Assert.True(stale.Count == 0, "Unrequested entries that are requested or route nothing: " + string.Join(", ", stale));
        }

        [Fact]
        public void Each_diagram_generator_routes_to_its_shipped_type_and_that_type_fits()
        {
            var lib = Shipped();
            foreach (var req in DrawingRouteRequests.DiagramGenerators)
            {
                var t = Route(lib, req.Discipline, req.DocType);
                Assert.True(t != null, $"{req.Caller}: {req.Discipline}/{req.DocType} routes nowhere");
                Assert.Equal(req.FallbackDrawingTypeId, t.Id);
            }
            // Every schematic generator's type is a Schematic (the riser is a Section by design).
            foreach (var req in DrawingRouteRequests.DiagramGenerators.Where(r => r != DrawingRouteRequests.Riser))
                Assert.Equal(DrawingPurpose.Schematic, lib.DrawingTypes.Single(t => t.Id == req.FallbackDrawingTypeId).Purpose);
        }

        [Fact]
        public void Production_names_the_generator_or_says_none_draws_the_schematic()
        {
            Assert.Contains("SLD_Generate", DrawingRouteRequests.SchematicNotProducedReason("elec-sld-A1-NTS"));
            Assert.Contains("no STING generator", DrawingRouteRequests.SchematicNotProducedReason("mep-hvac-schematic-A1"));

            // The schematic types nothing draws, for the record — production skips them.
            var lib = Shipped();
            var orphan = lib.DrawingTypes
                .Where(t => t.Purpose == DrawingPurpose.Schematic && (t.ProductionRules == null || t.ProductionRules.Count == 0))
                .Where(t => DrawingRouteRequests.GeneratorFor(t.Id) == null)
                .Select(t => t.Id).OrderBy(x => x).ToList();
            Assert.Contains("mep-hvac-schematic-A1", orphan);
            Assert.DoesNotContain("elec-fire-alarm-schematic-A1", orphan);
        }
    }
}
