// Covers runner §2 cases 1, 2, 3 and 6 — the matching semantics that are the whole
// contract of the feature, plus the empty-set case that must NOT report a blocker.

using System.Collections.Generic;
using StingTools.Core.Visibility;
using Xunit;

namespace StingTools.Visibility.Tests
{
    public class VisibilityMatcherTests
    {
        private static VisibilityElementSnapshot El(
            long id, int cat = 100, string zone = null, string loc = null, string disc = null)
        {
            var s = new VisibilityElementSnapshot { Id = id, CategoryId = cat, CategoryName = "Cat" + cat };
            s.Tokens[VisibilityTokens.Zone] = zone;
            s.Tokens[VisibilityTokens.Loc] = loc;
            s.Tokens[VisibilityTokens.Disc] = disc;
            return s;
        }

        private static VisibilityRule Token(string key, VisibilityAction action, params string[] values) =>
            new VisibilityRule
            {
                Kind = VisibilityRuleKind.Token,
                TokenKey = key,
                Values = new List<string>(values),
                Action = action
            };

        private static VisibilitySet Set(params VisibilityRule[] rules) =>
            new VisibilitySet { Name = "t", Rules = new List<VisibilityRule>(rules) };

        // ── §2.1 — values within a rule OR ──────────────────────────────

        [Theory]
        [InlineData("Z02", true)]
        [InlineData("Z03", true)]
        [InlineData("Z01", false)]
        public void ValuesWithinOneRule_AreOred(string zone, bool expected)
        {
            var set = Set(Token(VisibilityTokens.Zone, VisibilityAction.Hide, "Z02", "Z03"));
            Assert.Equal(expected, VisibilityRuleMatcher.Matches(El(1, zone: zone), set));
        }

        // ── §2.1 — rules across kinds AND ───────────────────────────────

        [Fact]
        public void RulesAcrossTokens_AreAnded()
        {
            var set = Set(
                Token(VisibilityTokens.Zone, VisibilityAction.Hide, "Z02"),
                Token(VisibilityTokens.Loc, VisibilityAction.Hide, "BLD1"));

            Assert.True(VisibilityRuleMatcher.Matches(El(1, zone: "Z02", loc: "BLD1"), set));
            Assert.False(VisibilityRuleMatcher.Matches(El(2, zone: "Z02", loc: "BLD2"), set));
            Assert.False(VisibilityRuleMatcher.Matches(El(3, zone: "Z09", loc: "BLD1"), set));
        }

        [Fact]
        public void CategoryAndToken_AreAnded()
        {
            var set = Set(
                new VisibilityRule { Kind = VisibilityRuleKind.Category, CategoryId = 100, CategoryName = "Ducts" },
                Token(VisibilityTokens.Zone, VisibilityAction.Hide, "Z02"));

            Assert.True(VisibilityRuleMatcher.Matches(El(1, cat: 100, zone: "Z02"), set));
            Assert.False(VisibilityRuleMatcher.Matches(El(2, cat: 200, zone: "Z02"), set));
        }

        [Fact]
        public void TwoCategoryRules_AreOred_NotAnded()
        {
            // An element has exactly one category, so AND-ing two category rules would
            // match nothing — which is never what ticking two boxes means.
            var set = Set(
                new VisibilityRule { Kind = VisibilityRuleKind.Category, CategoryId = 100, CategoryName = "Ducts" },
                new VisibilityRule { Kind = VisibilityRuleKind.Category, CategoryId = 200, CategoryName = "Pipes" });

            Assert.True(VisibilityRuleMatcher.Matches(El(1, cat: 100), set));
            Assert.True(VisibilityRuleMatcher.Matches(El(2, cat: 200), set));
            Assert.False(VisibilityRuleMatcher.Matches(El(3, cat: 300), set));
        }

        // ── §2.2 — mixed Hide + ShowOnly is rejected ────────────────────

        [Fact]
        public void MixedHideAndShowOnly_IsRejectedWithAMessage()
        {
            var set = Set(
                Token(VisibilityTokens.Zone, VisibilityAction.Hide, "Z02"),
                Token(VisibilityTokens.Loc, VisibilityAction.ShowOnly, "BLD1"));

            string reason = VisibilityRuleMatcher.Validate(set);

            Assert.False(string.IsNullOrWhiteSpace(reason));
            Assert.Contains("Hide", reason);
            Assert.Contains("Show-only", reason);
        }

        [Fact]
        public void MixedActions_RejectThePlan_AndPlanIsNotApplyable()
        {
            var set = Set(
                Token(VisibilityTokens.Zone, VisibilityAction.Hide, "Z02"),
                Token(VisibilityTokens.Loc, VisibilityAction.ShowOnly, "BLD1"));

            var plan = VisibilityRuleMatcher.PlanCore(
                new[] { El(1, zone: "Z02", loc: "BLD1") }, set, VisibilityMode.Temporary);

            Assert.True(plan.IsRejected);
            Assert.False(plan.CanApply);
            Assert.Equal(plan.RejectReason, plan.Summary());
        }

        [Fact]
        public void UniformActions_AreNotRejected()
        {
            var set = Set(
                Token(VisibilityTokens.Zone, VisibilityAction.ShowOnly, "Z02"),
                Token(VisibilityTokens.Loc, VisibilityAction.ShowOnly, "BLD1"));

            Assert.Null(VisibilityRuleMatcher.Validate(set));
            Assert.True(VisibilityRuleMatcher.IsIsolate(set));
        }

        [Fact]
        public void UnknownTokenKey_IsRejected()
        {
            var set = Set(Token("NOPE", VisibilityAction.Hide, "x"));
            Assert.Contains("Unknown tag token", VisibilityRuleMatcher.Validate(set));
        }

        // ── §2.3 — (unset) matches null AND empty string ────────────────

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void UnsetSentinel_MatchesNullAndBlank(string actual)
        {
            var set = Set(Token(VisibilityTokens.Zone, VisibilityAction.Hide, VisibilityTokens.Unset));
            Assert.True(VisibilityRuleMatcher.Matches(El(1, zone: actual), set));
        }

        [Fact]
        public void UnsetSentinel_DoesNotMatchAPopulatedValue()
        {
            var set = Set(Token(VisibilityTokens.Zone, VisibilityAction.Hide, VisibilityTokens.Unset));
            Assert.False(VisibilityRuleMatcher.Matches(El(1, zone: "Z01"), set));
        }

        [Fact]
        public void RealValue_DoesNotMatchAnUnsetElement()
        {
            var set = Set(Token(VisibilityTokens.Zone, VisibilityAction.Hide, "Z01"));
            Assert.False(VisibilityRuleMatcher.Matches(El(1, zone: null), set));
            Assert.False(VisibilityRuleMatcher.Matches(El(2, zone: ""), set));
        }

        [Fact]
        public void ValueMatching_IsCaseAndWhitespaceInsensitive()
        {
            var set = Set(Token(VisibilityTokens.Zone, VisibilityAction.Hide, "z02"));
            Assert.True(VisibilityRuleMatcher.Matches(El(1, zone: " Z02 "), set));
        }

        // ── §2.6 — empty rule set: zero matches, NO blockers ────────────

        [Fact]
        public void PlanCore_OnEmptyRuleSet_HasZeroMatchesAndNoBlockers()
        {
            var set = new VisibilitySet { Name = "empty", Rules = new List<VisibilityRule>() };
            var elements = new[] { El(1, zone: "Z01"), El(2, zone: "Z02") };

            var plan = VisibilityRuleMatcher.PlanCore(elements, set, VisibilityMode.Temporary);

            Assert.False(plan.IsRejected);
            Assert.Equal(0, plan.MatchCount);
            Assert.Empty(plan.Blockers);
            Assert.Equal(2, plan.TotalScanned);
        }

        // ── Unresolved category rules must not fail silently ────────────

        [Fact]
        public void UnresolvedCategoryRule_RaisesABlockerNamingIt()
        {
            // What a typo in STING_VISIBILITY_PRESETS.json looks like after the engine's
            // resolution pass: the OST_ name survives, CategoryId stays 0.
            var set = Set(new VisibilityRule
            {
                Kind = VisibilityRuleKind.Category,
                CategoryId = 0,
                CategoryName = "OST_DuctCurve"   // real name is OST_DuctCurves
            });

            var plan = VisibilityRuleMatcher.PlanCore(
                new[] { El(1, cat: 100), El(2, cat: 200) }, set, VisibilityMode.Temporary);

            Assert.Equal(0, plan.MatchCount);
            var blocker = Assert.Single(plan.Blockers);
            Assert.Contains("OST_DuctCurve", blocker);
            Assert.Contains("doesn't have", blocker);
        }

        [Fact]
        public void UnresolvedCategoryRule_IsReportedOncePerDistinctName()
        {
            var set = Set(
                new VisibilityRule { Kind = VisibilityRuleKind.Category, CategoryName = "OST_Typo" },
                new VisibilityRule { Kind = VisibilityRuleKind.Category, CategoryName = "OST_Typo" },
                new VisibilityRule { Kind = VisibilityRuleKind.Category, CategoryName = "OST_Other" });

            var plan = VisibilityRuleMatcher.PlanCore(new[] { El(1) }, set, VisibilityMode.Temporary);

            Assert.Equal(2, plan.Blockers.Count);
        }

        [Fact]
        public void CategoryRuleWithNoNameAtAll_IsStillReported()
        {
            var set = Set(new VisibilityRule { Kind = VisibilityRuleKind.Category });

            var plan = VisibilityRuleMatcher.PlanCore(new[] { El(1) }, set, VisibilityMode.Temporary);

            Assert.Contains("(unnamed)", Assert.Single(plan.Blockers));
        }

        [Fact]
        public void ResolvedCategoryRules_RaiseNoBlocker()
        {
            var set = Set(new VisibilityRule
            {
                Kind = VisibilityRuleKind.Category,
                CategoryId = 100,
                CategoryName = "Ducts"
            });

            var plan = VisibilityRuleMatcher.PlanCore(new[] { El(1, cat: 100) }, set, VisibilityMode.Temporary);

            Assert.Empty(plan.Blockers);
            Assert.Equal(1, plan.MatchCount);
        }

        [Fact]
        public void OneBadCategoryAmongGood_StillReportsWhileTheRestMatch()
        {
            // Same group, so the category rules OR — the good one still matches.
            var set = Set(
                new VisibilityRule { Kind = VisibilityRuleKind.Category, CategoryId = 100, CategoryName = "Ducts" },
                new VisibilityRule { Kind = VisibilityRuleKind.Category, CategoryName = "OST_Nope" });

            var plan = VisibilityRuleMatcher.PlanCore(
                new[] { El(1, cat: 100), El(2, cat: 999) }, set, VisibilityMode.Temporary);

            Assert.Equal(1, plan.MatchCount);
            Assert.Contains("OST_Nope", Assert.Single(plan.Blockers));
        }

        [Fact]
        public void TokenRules_AreNotAffectedByTheCategoryCheck()
        {
            var set = Set(Token(VisibilityTokens.Zone, VisibilityAction.Hide, "Z02"));
            var plan = VisibilityRuleMatcher.PlanCore(new[] { El(1, zone: "Z02") }, set, VisibilityMode.Temporary);

            Assert.Empty(plan.Blockers);
        }

        [Fact]
        public void PlanCore_CountsMatchesAndTotal()
        {
            var set = Set(Token(VisibilityTokens.Zone, VisibilityAction.Hide, "Z02"));
            var elements = new[]
            {
                El(1, zone: "Z02"), El(2, zone: "Z02"), El(3, zone: "Z01"), El(4, zone: null)
            };

            var plan = VisibilityRuleMatcher.PlanCore(elements, set, VisibilityMode.Temporary);

            Assert.Equal(2, plan.MatchCount);
            Assert.Equal(4, plan.TotalScanned);
            Assert.Contains("2", plan.Summary());
            Assert.Contains("hide", plan.Summary());
        }

        [Fact]
        public void PlanCore_ShowOnlySet_ReportsIsolate()
        {
            var set = Set(Token(VisibilityTokens.Zone, VisibilityAction.ShowOnly, "Z02"));
            var plan = VisibilityRuleMatcher.PlanCore(new[] { El(1, zone: "Z02") }, set, VisibilityMode.Temporary);

            Assert.True(plan.IsIsolate);
            Assert.Contains("isolate", plan.Summary());
        }

        [Fact]
        public void PlanCore_ViewFilterMode_PlansOneFilterPerDistinctValue()
        {
            var set = Set(Token(VisibilityTokens.Zone, VisibilityAction.Hide, "Z02", "Z03"));
            var elements = new[] { El(1, zone: "Z02"), El(2, zone: "Z03"), El(3, zone: "Z01") };

            var plan = VisibilityRuleMatcher.PlanCore(elements, set, VisibilityMode.ViewFilter);

            Assert.Equal(2, plan.Filters.Count);
            Assert.All(plan.Filters, f => Assert.StartsWith(VisibilityRuleMatcher.FilterPrefix, f.Name));
        }

        [Fact]
        public void PlanCore_TemporaryMode_PlansNoFilters()
        {
            var set = Set(Token(VisibilityTokens.Zone, VisibilityAction.Hide, "Z02"));
            var plan = VisibilityRuleMatcher.PlanCore(new[] { El(1, zone: "Z02") }, set, VisibilityMode.Temporary);

            Assert.Empty(plan.Filters);
        }
    }
}
