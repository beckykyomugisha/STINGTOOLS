using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.Core.Validation;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// KUT-4 — LOD 100 is a rung that cannot fail, so it must not report a pass.
    ///
    /// <para>Every element in scope satisfied LOD 100 and the run reported <b>100%</b>, which
    /// reads identically to a model that genuinely meets every requirement. A rung that
    /// cannot fail is not a gate, and a report that cannot tell you so is worse than no
    /// report — it is the same vacuous-pass shape as an empty scope, which
    /// <c>LodTally.NoElementsInScope</c> already exists to prevent.</para>
    ///
    /// <para><b>The rung was made non-gating rather than given a requirement.</b> The
    /// BIMForum LOD specification defines 100 as conceptual — an element "may be graphically
    /// represented with a symbol or other generic representation" and its information "may be
    /// derived from other Model Elements" — so there is genuinely nothing per-element to
    /// verify. Inventing a check ("must have a category") would be inventing a standard, and
    /// would pass vacuously anyway: every Revit element has a category by construction.</para>
    /// </summary>
    public class Lod100NonGatingTests
    {
        private static string MatrixPath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(
                       dir.FullName, "StingTools", "Data", "STING_LOD_MATRIX.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data from " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, "StingTools", "Data", "STING_LOD_MATRIX.json");
        }

        private static LodMatrix Shipped()
        {
            var m = JsonConvert.DeserializeObject<LodMatrix>(File.ReadAllText(MatrixPath()));
            Assert.True(m != null && m.CategoryRules.Count > 0, "LOD matrix failed to deserialise");
            return m;
        }

        // ── AssertsNothing, derived rather than named ────────────────────────────

        [Fact]
        public void AnEmptyCheckAssertsNothing()
            => Assert.True(new ResolvedLodCheck().AssertsNothing);

        [Theory]
        [InlineData("geometry")]
        [InlineData("placeholder")]
        [InlineData("generic")]
        [InlineData("manufacturer")]
        [InlineData("clash")]
        [InlineData("param")]
        [InlineData("dim")]
        public void AnySingleRequirementMakesARungGating(string which)
        {
            var c = new ResolvedLodCheck();
            switch (which)
            {
                case "geometry": c.RequireGeometry = true; break;
                case "placeholder": c.ForbidPlaceholderFamilies = true; break;
                case "generic": c.RequireTypeNotGeneric = true; break;
                case "manufacturer": c.RequireManufacturerType = true; break;
                case "clash": c.RequireNoUnresolvedClash = true; break;
                case "param": c.RequiredParams.Add("ASS_TAG_1_TXT"); break;
                case "dim": c.RequiredDims.Add("Length"); break;
            }
            Assert.False(c.AssertsNothing, $"a rung requiring '{which}' must be gating");
        }

        // ── The shipped matrix ───────────────────────────────────────────────────

        /// <summary>LOD 100 asserts nothing in EVERY shipped category rule that defines it.
        /// If one category ever gives it a real check, that category becomes gating on its
        /// own — which the derived flag handles, and which this test will report.</summary>
        [Fact]
        public void ShippedLod100AssertsNothingEverywhereItIsDefined()
        {
            var matrix = Shipped();
            var gating = new List<string>();
            int defined = 0;

            foreach (var rule in matrix.CategoryRules)
            {
                if (rule.Checks == null || !rule.Checks.ContainsKey("100")) continue;
                defined++;
                var resolved = LodRuleResolver.Resolve(matrix, rule.Category, "100");
                if (resolved != null && !resolved.AssertsNothing) gating.Add(rule.Category);
            }

            Assert.True(defined > 0, "no shipped category rule defines LOD 100 at all");
            Assert.True(gating.Count == 0,
                "these categories now state a real LOD 100 requirement, so LOD 100 is no longer "
                + "uniformly non-gating — update KUT-4's reasoning rather than this assertion: "
                + string.Join(", ", gating));
        }

        /// <summary>Every rung the shipped MILESTONES actually bind IS gating. This is the
        /// assertion that matters day to day: a milestone bound to a rung that asserts nothing
        /// would report NOT ASSESSED for an entire deliverable.</summary>
        [Fact]
        public void EveryMilestoneBindsAGatingRung()
        {
            var matrix = Shipped();
            Assert.NotEmpty(matrix.Milestones);

            var offenders = new List<string>();
            foreach (var ms in matrix.Milestones)
            {
                var resolved = LodRuleResolver.Resolve(matrix, "*", ms.Lod.ToString());
                if (resolved == null)
                {
                    offenders.Add($"{ms.Id} (LOD {ms.Lod}) — the \"*\" rule defines no such rung");
                    continue;
                }
                if (resolved.AssertsNothing)
                    offenders.Add($"{ms.Id} (LOD {ms.Lod}) — the rung asserts nothing");
            }
            Assert.True(offenders.Count == 0,
                "milestones bound to a rung that cannot gate:\n  " + string.Join("\n  ", offenders));
        }

        // ── The tally reports words, not a percentage ────────────────────────────

        /// <summary>A rung that asserts nothing reports NOT ASSESSED — not 100%, and not a
        /// fail either.</summary>
        [Fact]
        public void ARungThatAssertsNothingIsNotAPass()
        {
            var t = new LodTally { MilestoneId = "concept", Lod = 100 };
            for (int i = 0; i < 40; i++) t.RecordNotAssessed("Walls");

            Assert.True(t.RungAssertsNothing);
            Assert.False(t.HasMeaningfulResult);
            Assert.Equal(40, t.NotAssessed);
            Assert.Equal(0, t.Total);
            Assert.Equal(0, t.Passed);
            Assert.Equal(0, t.Failed);      // and NOT a fail
            Assert.Equal(40, t.NotAssessedByCategory["Walls"]);
        }

        /// <summary>Not-assessed elements stay OUT of the denominator, so a MIXED run's
        /// percentage describes only what was actually checked. Folding them in as passes
        /// would inflate the score with elements nothing looked at.</summary>
        [Fact]
        public void NotAssessedElementsAreOutsideTheDenominator()
        {
            var t = new LodTally { Lod = 300 };
            t.Total = 10; t.Passed = 8;
            for (int i = 0; i < 5; i++) t.RecordNotAssessed("Generic Models");

            Assert.Equal(80.0, t.OverallPct);      // 8/10, not 13/15
            Assert.True(t.HasMeaningfulResult);
            Assert.False(t.RungAssertsNothing);    // elements WERE assessed
            Assert.Equal(5, t.NotAssessed);
        }

        /// <summary>"The rung asserts nothing" and "the scope was empty" are different facts
        /// and must be distinguishable — one is a matrix problem, the other a model or filter
        /// problem, and a reader needs to know which.</summary>
        [Fact]
        public void RungAssertsNothingIsDistinctFromAnEmptyScope()
        {
            var emptyScope = new LodTally { Lod = 300 };
            Assert.True(emptyScope.NoElementsInScope);
            Assert.False(emptyScope.RungAssertsNothing);

            var nonGating = new LodTally { Lod = 100 };
            nonGating.RecordNotAssessed("Walls");
            Assert.True(nonGating.NoElementsInScope);   // Total is still 0…
            Assert.True(nonGating.RungAssertsNothing);  // …but for a different reason
        }

        /// <summary>The old trap is unchanged and still documented: OverallPct returns 100.0
        /// over an empty run. HasMeaningfulResult is the flag to branch on, and it says no in
        /// both empty cases.</summary>
        [Fact]
        public void OverallPctIsStillATrapAndHasMeaningfulResultIsTheGuard()
        {
            var t = new LodTally { Lod = 100 };
            t.RecordNotAssessed("Walls");
            Assert.Equal(100.0, t.OverallPct);       // the trap, unchanged
            Assert.False(t.HasMeaningfulResult);     // the guard
        }
    }
}
