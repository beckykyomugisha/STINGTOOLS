// Round 8 authoring / config defects (DTW-178..193) in the Drawing Type
// editor, the Excel round-trip and the registry's project-override layer.
// The Revit-free halves are exercised directly; the Revit-bound halves are
// held by source guards where nothing else can reach them.

using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class DrawingTypeAuthoringTests
    {
        private static DrawingRoutingRule Rule(string disc, string docType, string target,
            string option = null, string origin = null)
            => new DrawingRoutingRule { Discipline = disc, Phase = "*", DocType = docType,
                                        DrawingTypeId = target, OptionMatches = option, Origin = origin };

        // ── DTW-185 ───────────────────────────────────────────────────────

        [Fact]
        public void Signature_distinguishes_rules_that_differ_only_by_optionMatches()
        {
            var ve = Rule("A", "PLAN", "pres-plan", option: "^VE");
            var baseline = Rule("A", "PLAN", "arch-plan-A1-1to100", option: "^Main Model$");
            Assert.NotEqual(DrawingRoutingMatcher.Signature(ve), DrawingRoutingMatcher.Signature(baseline));
        }

        [Fact]
        public void Signature_covers_every_predicate_the_matcher_reads()
        {
            // Each predicate property, set alone, must move the signature. A new
            // predicate added to DrawingRoutingRule without joining the signature
            // fails here instead of collapsing rules silently at load.
            var predicates = typeof(DrawingRoutingRule).GetProperties()
                .Where(p => p.PropertyType == typeof(string) && p.CanWrite
                            && p.Name != nameof(DrawingRoutingRule.DrawingTypeId)
                            && p.Name != nameof(DrawingRoutingRule.Origin))
                .ToList();
            Assert.True(predicates.Count >= 9, "predicate reflection found " + predicates.Count);
            var plain = DrawingRoutingMatcher.Signature(Rule("A", "PLAN", "x"));
            foreach (var p in predicates)
            {
                var r = Rule("A", "PLAN", "x");
                p.SetValue(r, "^Z9$");
                Assert.True(DrawingRoutingMatcher.Signature(r) != plain, p.Name + " is not part of the signature");
            }
        }

        [Fact]
        public void Registry_merge_uses_the_shared_signature()
        {
            var src = DrawingCatalogueFixture.Source("Core", "Drawing", "DrawingTypeRegistry.cs");
            Assert.Contains("DrawingRoutingMatcher.", src);
            Assert.DoesNotContain("rule.ProjectCodeMatches ?? \"\");", src);
        }
    }
}
