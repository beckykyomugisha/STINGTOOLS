using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Gates over the shipped drawing catalogue (STING_DRAWING_TYPES.json)
    /// that bind the SAME Revit-free code the plugin runs:
    /// <list type="bullet">
    /// <item><b>D-4</b> — the routing table has no shadowed rule, no dangling
    /// id, no unreachable drawing type and no uncompilable regex
    /// (<see cref="DrawingRoutingMatcher.Audit"/>, also DT-104/105 in Revit).</item>
    /// <item><b>D-5</b> — no slot is invalid, out of bounds or overlapping
    /// (<see cref="DrawingSlotGeometry.Check"/>, also DT-055/056/SLT-03 in
    /// Revit), in the JSON and in the compiled-in fallback spool profile.</item>
    /// </list>
    /// </summary>
    public class DrawingCatalogueGateTests
    {
        private static DrawingTypeLibrary Shipped() => DrawingCatalogueFixture.Shipped();
        private static string Source(params string[] parts) => DrawingCatalogueFixture.Source(parts);

        // ── D-4: routing ────────────────────────────────────────────────

        [Fact]
        public void Shipped_routing_has_no_shadowed_dangling_unreachable_or_invalid_rule()
        {
            var lib = Shipped();
            var a = DrawingRoutingMatcher.Audit(lib.Routing, lib.DrawingTypes);
            Assert.True(a.Shadowed.Count == 0,     "Shadowed rules:\n  " + string.Join("\n  ", a.Shadowed));
            Assert.True(a.Dangling.Count == 0,     "Dangling drawingTypeIds:\n  " + string.Join("\n  ", a.Dangling));
            Assert.True(a.Unreachable.Count == 0,  "Drawing types no rule reaches:\n  " + string.Join("\n  ", a.Unreachable));
            Assert.True(a.InvalidRegex.Count == 0, "Uncompilable predicates:\n  " + string.Join("\n  ", a.InvalidRegex));
        }

        [Fact]
        public void Audit_catches_each_defect_it_claims_to()
        {
            // The audit itself must not be vacuous: seed one of each defect
            // into a copy of the shipped table and require it be named.
            var lib = Shipped();
            var routing = lib.Routing.ToList();
            var types = lib.DrawingTypes.ToList();

            var victim = routing.First(r => string.IsNullOrEmpty(r.DisciplineMatches) && r.Discipline != "*" && r.DocType != "*");
            // Shadow: an exact duplicate of `victim`'s key, placed first, pointing at a real type.
            routing.Insert(0, new DrawingRoutingRule { Discipline = victim.Discipline, Phase = victim.Phase,
                DocType = victim.DocType, DrawingTypeId = types[0].Id });
            // Dangling.
            routing.Add(new DrawingRoutingRule { Discipline = "ZZ", DocType = "NOPE", DrawingTypeId = "no-such-type" });
            // Unreachable.
            types.Add(new DrawingType { Id = "orphan-type", Purpose = DrawingPurpose.Plan });
            // Invalid regex.
            routing.Add(new DrawingRoutingRule { DocTypeMatches = "([", DrawingTypeId = types[0].Id });

            var a = DrawingRoutingMatcher.Audit(routing, types);
            Assert.Contains(a.Shadowed, s => s.Contains("-> " + victim.DrawingTypeId + ")"));
            Assert.Contains(a.Dangling, s => s.Contains("no-such-type"));
            Assert.Contains("orphan-type", a.Unreachable);
            Assert.Contains(a.InvalidRegex, s => s.Contains("(["));
        }

        [Fact]
        public void A_dangling_rule_does_not_shadow()
        {
            // E-12: the dispatcher walks past a rule whose id is missing, so
            // the rule behind it still fires and must not be reported.
            var types = new List<DrawingType> { new DrawingType { Id = "real" } };
            var routing = new List<DrawingRoutingRule>
            {
                new DrawingRoutingRule { Discipline = "A", DocType = "PLAN", DrawingTypeId = "gone" },
                new DrawingRoutingRule { Discipline = "A", DocType = "PLAN", DrawingTypeId = "real" },
            };
            var a = DrawingRoutingMatcher.Audit(routing, types);
            Assert.Empty(a.Shadowed);
            Assert.Single(a.Dangling);
            Assert.Empty(a.Unreachable);
        }

        [Theory]
        // (aExact, aRegex, bExact, bRegex, covers)
        [InlineData("*",  null,    "A",  null,   true)]   // wildcard covers literal
        [InlineData("A",  null,    "a",  null,   true)]   // literals are case-insensitive
        [InlineData("A",  null,    "*",  null,   false)]  // literal cannot cover wildcard
        [InlineData(null, ".*",    "*",  null,   false)]  // regex never matches an empty value; wildcard does
        [InlineData(null, "^H$",   "h",  null,   true)]   // regex covers a literal it matches (any case)
        [InlineData(null, "^H$",   "HX", null,   false)]
        [InlineData(null, "^H$",   null, "^H$",  true)]   // identical regex
        [InlineData(null, "^H$",   null, "^HH$", false)]  // different regex: conservative, not a shadow
        [InlineData("H",  null,    null, "^H$",  false)]  // literal vs regex: conservative
        public void Field_coverage_is_conservative(string aEx, string aRx, string bEx, string bRx, bool expected)
            => Assert.Equal(expected, DrawingRoutingMatcher.Covers(aEx, aRx, bEx, bRx));

        [Fact]
        public void Regex_predicates_are_case_insensitive_like_literal_fields()
        {
            // A literal rule has always matched any case; the regex form of the
            // same rule did not, so rewriting one as the other changed routing.
            Assert.True(DrawingRoutingMatcher.MatchesField("SPOOL", null, "spool"));
            Assert.True(DrawingRoutingMatcher.MatchesField(null, "^SPOOL$", "spool"));
            Assert.True(DrawingRoutingMatcher.MatchesField(null, "^mgs-sch$", "MGS-SCH"));
            // Every shipped regex rule still matches its own upper-case key in lower case.
            foreach (var r in Shipped().Routing.Where(x => !string.IsNullOrEmpty(x.DocTypeMatches)))
            {
                var key = r.DocTypeMatches.Trim('^', '$');
                if (!Regex.IsMatch(key, "^[A-Z0-9-]+$")) continue;
                Assert.True(DrawingRoutingMatcher.RegexMatches(r.DocTypeMatches, key.ToLowerInvariant()),
                    $"'{r.DocTypeMatches}' does not match '{key.ToLowerInvariant()}'");
            }
            // Unchanged edges: empty input never matches a regex; a bad pattern never matches.
            Assert.False(DrawingRoutingMatcher.RegexMatches(".*", ""));
            Assert.False(DrawingRoutingMatcher.RegexMatches("([", "anything"));
            Assert.True(DrawingRoutingMatcher.MatchesWildcard("*", null));
        }

        [Fact]
        public void Compiled_in_fallback_routing_is_clean_too()
        {
            // DrawingTypeRegistry.BuildDefaults (Revit-bound) is what loads when
            // the JSON is missing. Its ids and rules are read from source.
            var src = Source("Core", "Drawing", "DrawingTypeRegistry.cs");
            var ids = Regex.Matches(src, @"Make(?:Basic|FabSpool|Schedule)\(\s*""([^""]+)""")
                .Select(m => m.Groups[1].Value).Distinct().ToList();
            var rules = Regex.Matches(src,
                    @"new DrawingRoutingRule \{ Discipline = ""([^""]*)"", DocType = ""([^""]*)"",\s*DrawingTypeId = ""([^""]*)"" \}")
                .Select(m => new DrawingRoutingRule
                {
                    Discipline = m.Groups[1].Value, DocType = m.Groups[2].Value, DrawingTypeId = m.Groups[3].Value,
                }).ToList();
            Assert.True(ids.Count >= 10, $"Parsed {ids.Count} fallback ids — parser broken?");
            Assert.True(rules.Count >= 10, $"Parsed {rules.Count} fallback rules — parser broken?");

            var a = DrawingRoutingMatcher.Audit(rules, ids.Select(i => new DrawingType { Id = i }));
            Assert.True(a.Shadowed.Count == 0,    "Fallback shadowed: " + string.Join("; ", a.Shadowed));
            Assert.True(a.Dangling.Count == 0,    "Fallback dangling: " + string.Join("; ", a.Dangling));
            Assert.True(a.Unreachable.Count == 0, "Fallback unreachable: " + string.Join("; ", a.Unreachable));
        }

        // ── D-5: slots ──────────────────────────────────────────────────

        [Fact]
        public void Shipped_slots_are_valid_in_bounds_and_non_overlapping()
        {
            var lib = Shipped();
            int slots = 0;
            var problems = new List<string>();
            foreach (var t in lib.DrawingTypes)
            {
                slots += t.Slots?.Count ?? 0;
                problems.AddRange(DrawingSlotGeometry.Check(t.Slots).Select(i => $"{t.Id}: {i.Code} {i.Message}"));
            }
            Assert.True(slots >= 100, $"Only {slots} slots bound — binding broken?");
            Assert.True(problems.Count == 0, "Slot geometry defects:\n  " + string.Join("\n  ", problems));
        }

        [Fact]
        public void Compiled_in_fallback_spool_slots_are_clean()
        {
            var src = Source("Core", "Drawing", "DrawingTypeRegistry.cs");
            int start = src.IndexOf("private static DrawingType MakeFabSpool(", StringComparison.Ordinal);
            Assert.True(start > 0, "MakeFabSpool not found");
            int end = src.IndexOf("private static DrawingType", start + 10, StringComparison.Ordinal);
            var body = src.Substring(start, end - start);
            var slots = Regex.Matches(body,
                    @"new DrawingSlot \{ Label = ""([^""]+)"".*?NormX = ([\d.]+), NormY = ([\d.]+), NormW = ([\d.]+), NormH = ([\d.]+)")
                .Select(m => new DrawingSlot
                {
                    Label = m.Groups[1].Value,
                    NormX = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                    NormY = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                    NormW = double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture),
                    NormH = double.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture),
                }).ToList();
            Assert.True(slots.Count >= 6, $"Parsed {slots.Count} spool slots — parser broken?");
            var issues = DrawingSlotGeometry.Check(slots);
            Assert.True(issues.Count == 0, "Fallback spool slot defects: " + string.Join("; ", issues.Select(i => i.Message)));
        }

        [Fact]
        public void Slot_check_reports_each_defect_and_tolerates_shared_edges()
        {
            DrawingSlot S(string l, double x, double y, double w, double h)
                => new DrawingSlot { Label = l, NormX = x, NormY = y, NormW = w, NormH = h };

            // 0.03 + 0.72 is 0.75 only up to floating-point noise — a shared edge, not an overlap.
            Assert.Empty(DrawingSlotGeometry.Check(new[] { S("a", 0.03, 0.05, 0.72, 0.9), S("b", 0.75, 0.05, 0.2, 0.9) }));

            var overlap = DrawingSlotGeometry.Check(new[] { S("a", 0, 0, 0.6, 0.6), S("b", 0.5, 0.5, 0.4, 0.4) });
            Assert.Contains(overlap, i => i.Kind == DrawingSlotGeometry.IssueKind.Overlap && i.Code == "DT-SLT-03" && i.OverlapPct == 1.0);

            Assert.Contains(DrawingSlotGeometry.Check(new[] { S("oob", 0.5, 0, 0.6, 0.5) }),
                i => i.Kind == DrawingSlotGeometry.IssueKind.OutOfBounds && i.Code == "DT-056");
            Assert.Contains(DrawingSlotGeometry.Check(new[] { S("zero", 0.1, 0.1, 0, 0.5) }),
                i => i.Kind == DrawingSlotGeometry.IssueKind.InvalidGeometry && i.Code == "DT-055");
        }
    }
}
