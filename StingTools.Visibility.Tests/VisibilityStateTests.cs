// Covers the enhancement runner §1 — the read-back reconciliation.
//
// These are the cases the runner names: nothing hidden; a category hidden; a token filter
// applied; both at once; and a filter present but SetFilterVisibility(true) — applied, yet
// NOT hiding, which must read as visible.
//
// The Revit-bound half (VisibilityStateReader) cannot be reached from here by design; what
// IS testable is the decision layer it feeds, and that is where a wrong answer would flip a
// row's tick and compute the next Apply from a false baseline.

using System.Collections.Generic;
using StingTools.Core.Visibility;
using Xunit;

namespace StingTools.Visibility.Tests
{
    public class VisibilityStateTests
    {
        private const int Ducts = -2008000;
        private const int Pipes = -2008044;

        private static VisibilityElementSnapshot El(
            long id, int cat, string zone = null, string disc = null, string catName = null)
        {
            var s = new VisibilityElementSnapshot
            {
                Id = id,
                CategoryId = cat,
                CategoryName = catName ?? (cat == Ducts ? "Ducts" : "Pipes")
            };
            s.Tokens[VisibilityTokens.Zone] = zone;
            s.Tokens[VisibilityTokens.Disc] = disc;
            return s;
        }

        private static AppliedFilterState TokenFilter(string key, string value, bool hides = true) =>
            new AppliedFilterState
            {
                Name = VisibilityRuleMatcher.FilterName(VisibilityRuleKind.Token, key, value),
                Kind = VisibilityRuleKind.Token,
                TokenKey = key,
                Value = value,
                Hides = hides
            };

        private static HashSet<long> Ids(params long[] ids) => new HashSet<long>(ids);
        private static HashSet<int> Cats(params int[] ids) => new HashSet<int>(ids);
        private static readonly List<AppliedFilterState> NoFilters = new List<AppliedFilterState>();

        // ── Case 1 — nothing hidden ─────────────────────────────────────

        [Fact]
        public void NothingHidden_EveryRowReadsVisible()
        {
            var els = new[] { El(1, Ducts, "Z01"), El(2, Pipes, "Z02") };

            var back = VisibilityStateReconciler.Reconcile(
                els, Ids(1, 2), Ids(), Cats(), NoFilters, temporaryActive: false);

            Assert.False(back.AnythingHidden);
            Assert.Equal(2, back.VisibleCount);
            Assert.Equal(0, back.HiddenCount);
            Assert.False(back.Category(Ducts).IsHidden);
            Assert.False(back.Token(VisibilityTokens.Zone, "Z02").IsHidden);
        }

        [Fact]
        public void NothingHidden_FooterIsNull_SoTheCallerKeepsItsOwnWording()
        {
            var back = VisibilityStateReconciler.Reconcile(
                new[] { El(1, Ducts) }, Ids(1), Ids(), Cats(), NoFilters, false);

            Assert.Null(back.Footer());
        }

        // ── Case 2 — a category hidden ──────────────────────────────────

        [Fact]
        public void HiddenCategory_ReadsUnticked_AndOtherCategoriesDoNot()
        {
            var els = new[] { El(1, Ducts, "Z01"), El(2, Ducts, "Z01"), El(3, Pipes, "Z01") };

            var back = VisibilityStateReconciler.Reconcile(
                els, Ids(3), Ids(), Cats(Ducts), NoFilters, temporaryActive: false);

            Assert.True(back.Category(Ducts).IsHidden);
            Assert.Equal(VisibilityHiddenBy.Category, back.Category(Ducts).By);
            Assert.False(back.Category(Pipes).IsHidden);

            Assert.Equal(2, back.HiddenCount);
            Assert.Equal(2, back.HiddenByCategory);
            Assert.Equal(1, back.VisibleCount);
        }

        [Fact]
        public void HiddenCategory_DoesNotHideATokenRowThatStillHasVisibleElements()
        {
            // Ducts are hidden, Pipes are not — and both carry ZONE Z01. The ZONE row must
            // stay ticked, because unticking it would hide the pipes on the next Apply.
            var els = new[] { El(1, Ducts, "Z01"), El(2, Pipes, "Z01") };

            var back = VisibilityStateReconciler.Reconcile(
                els, Ids(2), Ids(), Cats(Ducts), NoFilters, false);

            var zone = back.Token(VisibilityTokens.Zone, "Z01");
            Assert.False(zone.IsHidden);
            Assert.True(zone.IsPartial);
        }

        // ── Case 3 — a token filter applied ─────────────────────────────

        [Fact]
        public void HidingTokenFilter_ReadsTheTokenRowUnticked()
        {
            var els = new[] { El(1, Ducts, "Z02"), El(2, Ducts, "Z03") };

            var back = VisibilityStateReconciler.Reconcile(
                els, Ids(2), Ids(), Cats(),
                new List<AppliedFilterState> { TokenFilter(VisibilityTokens.Zone, "Z02") },
                temporaryActive: false);

            Assert.True(back.Token(VisibilityTokens.Zone, "Z02").IsHidden);
            Assert.Equal(VisibilityHiddenBy.Filter, back.Token(VisibilityTokens.Zone, "Z02").By);
            Assert.False(back.Token(VisibilityTokens.Zone, "Z03").IsHidden);
            Assert.True(back.SavedToView);
            Assert.Equal(1, back.HiddenByFilter);
        }

        // ── Case 4 — both at once ───────────────────────────────────────

        [Fact]
        public void CategoryAndFilterTogether_AreBothReportedAndAttributedSeparately()
        {
            var els = new[]
            {
                El(1, Ducts, "Z02"),   // hidden category
                El(2, Pipes, "Z02"),   // hidden by the ZONE filter
                El(3, Pipes, "Z03")    // visible
            };

            var back = VisibilityStateReconciler.Reconcile(
                els, Ids(3), Ids(), Cats(Ducts),
                new List<AppliedFilterState> { TokenFilter(VisibilityTokens.Zone, "Z02") },
                temporaryActive: false);

            Assert.True(back.Category(Ducts).IsHidden);
            Assert.True(back.Token(VisibilityTokens.Zone, "Z02").IsHidden);
            Assert.Equal(1, back.HiddenByCategory);
            Assert.Equal(1, back.HiddenByFilter);
            Assert.Equal(2, back.HiddenCount);
            Assert.Equal(1, back.VisibleCount);
            Assert.Equal(3, back.TotalCount);
        }

        [Fact]
        public void CategoryAndFilterTogether_FooterNamesBothAndTheCounts()
        {
            var els = new[] { El(1, Ducts, "Z02"), El(2, Pipes, "Z02"), El(3, Pipes, "Z03") };

            var footer = VisibilityStateReconciler.Reconcile(
                els, Ids(3), Ids(), Cats(Ducts),
                new List<AppliedFilterState> { TokenFilter(VisibilityTokens.Zone, "Z02") },
                false).Footer();

            Assert.Contains("1 category", footer);
            Assert.Contains("ZONE Z02", footer);
            Assert.Contains("1 of 3 visible", footer);
            Assert.Contains("saved to view", footer);
        }

        // ── Case 5 — a filter present but NOT hiding ────────────────────

        [Fact]
        public void FilterAppliedButVisible_ReadsAsVisible()
        {
            // SetFilterVisibility(true): the filter is on the view and does nothing. Reading
            // it as a hide would untick a row whose elements are plainly on screen.
            var els = new[] { El(1, Ducts, "Z02") };

            var back = VisibilityStateReconciler.Reconcile(
                els, Ids(1), Ids(), Cats(),
                new List<AppliedFilterState> { TokenFilter(VisibilityTokens.Zone, "Z02", hides: false) },
                temporaryActive: false);

            Assert.False(back.AnythingHidden);
            Assert.False(back.SavedToView);
            Assert.False(back.Token(VisibilityTokens.Zone, "Z02").IsHidden);
            Assert.Null(back.Footer());
        }

        // ── Temporary hide ──────────────────────────────────────────────

        [Fact]
        public void TemporaryHide_IsAttributedAndCounted()
        {
            var els = new[] { El(1, Ducts, "Z01"), El(2, Pipes, "Z01") };

            var back = VisibilityStateReconciler.Reconcile(
                els, Ids(2), Ids(1), Cats(), NoFilters, temporaryActive: true);

            Assert.True(back.Category(Ducts).IsHidden);
            Assert.Equal(VisibilityHiddenBy.Temporary, back.Category(Ducts).By);
            Assert.Equal(1, back.HiddenByTemporary);
            Assert.Contains("temporary", back.Footer());
        }

        [Fact]
        public void TemporaryIdsAreIgnoredWhenTheModeIsNotActive()
        {
            // IsTemporaryHideIsolateActive() is the gate. With the mode off, a stale candidate
            // set must not be allowed to invent a hide.
            var els = new[] { El(1, Ducts), El(2, Pipes) };

            var back = VisibilityStateReconciler.Reconcile(
                els, Ids(2), Ids(1), Cats(), NoFilters, temporaryActive: false);

            Assert.False(back.AnythingHidden);
            Assert.Equal(1, back.OutOfScopeCount);
        }

        // ── The over-report guard ───────────────────────────────────────

        [Fact]
        public void ElementsNotDrawnAndNotExplained_AreOutOfScope_NotCountedAsHidden()
        {
            // The whole reason the reader cannot just diff a document-scoped collector against
            // a view-scoped one: on a plan view most of the difference is elements the view
            // would never have drawn. Counting those as hidden inflates every number on screen.
            var els = new[] { El(1, Ducts), El(2, Ducts), El(3, Ducts) };

            var back = VisibilityStateReconciler.Reconcile(
                els, Ids(1), Ids(), Cats(), NoFilters, temporaryActive: false);

            Assert.Equal(1, back.VisibleCount);
            Assert.Equal(0, back.HiddenCount);
            Assert.Equal(2, back.OutOfScopeCount);
            Assert.Equal(1, back.TotalCount);
            Assert.False(back.Category(Ducts).IsHidden);
        }

        // ── Stale filters ───────────────────────────────────────────────

        [Fact]
        public void AHidingFilterMatchingNothing_IsNamed_NotSwallowed()
        {
            var els = new[] { El(1, Ducts, "Z01") };

            var back = VisibilityStateReconciler.Reconcile(
                els, Ids(1), Ids(), Cats(),
                new List<AppliedFilterState> { TokenFilter(VisibilityTokens.Zone, "Z09") },
                temporaryActive: false);

            Assert.Single(back.UnmatchedFilterNames);
            Assert.Contains("STING VIS - ZONE=Z09", back.UnmatchedFilterNames[0]);
        }

        // ── Badge ───────────────────────────────────────────────────────

        [Fact]
        public void BadgeTooltip_NamesEachMechanism()
        {
            var els = new[] { El(1, Ducts, "Z02"), El(2, Pipes, "Z02"), El(3, Pipes, "Z03") };

            var tip = VisibilityStateReconciler.Reconcile(
                els, Ids(3), Ids(), Cats(Ducts),
                new List<AppliedFilterState> { TokenFilter(VisibilityTokens.Zone, "Z02") },
                false).BadgeTooltip();

            Assert.Contains("2 of 3 elements hidden", tip);
            Assert.Contains("hidden categories", tip);
            Assert.Contains("saved STING filters", tip);
        }
    }
}
