// "Duplicate as Dependent": the production option nothing read. These pin when a
// scope-box view becomes a dependent of a per-level parent, and what a re-run does
// with a view that already exists.

using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class DependentViewPlannerTests
    {
        [Theory]
        [InlineData(true,  true,  true,  "FloorPlan",   true)]
        [InlineData(true,  true,  true,  "RCP",         true)]
        [InlineData(true,  true,  true,  "CeilingPlan", true)]
        [InlineData(true,  true,  true,  "Section",     false)] // no per-level parent for a section
        [InlineData(true,  true,  true,  "ThreeD",      false)]
        [InlineData(false, true,  true,  "FloorPlan",   false)] // option not chosen
        [InlineData(true,  false, true,  "FloorPlan",   false)] // no scope box: nothing to crop a dependent to
        [InlineData(true,  true,  false, "FloorPlan",   false)] // no level: no parent identity
        public void Dependents_only_for_scope_box_plans_on_a_level_when_chosen(
            bool option, bool box, bool level, string viewType, bool expected)
            => Assert.Equal(expected, DependentViewPlanner.UsesDependents(option, box, level, viewType));

        [Fact]
        public void Without_dependents_the_ordinary_path_runs()
            => Assert.Equal(DependentViewAction.Independent,
                DependentViewPlanner.ForBoxView(false, true, 0, 10));

        [Fact]
        public void A_box_with_no_view_gets_a_new_dependent()
            => Assert.Equal(DependentViewAction.CreateDependent,
                DependentViewPlanner.ForBoxView(true, false, 0, 10));

        [Fact]
        public void A_dependent_of_this_parent_is_reused()
            => Assert.Equal(DependentViewAction.ReuseDependent,
                DependentViewPlanner.ForBoxView(true, true, 10, 10));

        [Fact]
        public void An_existing_independent_view_is_kept_not_replaced()
            => Assert.Equal(DependentViewAction.KeepExisting,
                DependentViewPlanner.ForBoxView(true, true, 0, 10));

        [Fact]
        public void A_dependent_of_another_parent_is_kept_not_replaced()
            => Assert.Equal(DependentViewAction.KeepExisting,
                DependentViewPlanner.ForBoxView(true, true, 11, 10));

        [Fact]
        public void The_parent_is_named_for_the_browser_and_tagged_apart_from_per_level_views()
        {
            Assert.Equal("Power Layout - Level 1 - Parent", DependentViewPlanner.ParentViewName("Power Layout", "Level 1"));
            Assert.Equal("Power Layout - Level 1 - Parent (RCP)", DependentViewPlanner.ParentViewName("Power Layout", "Level 1", " (RCP)"));
            Assert.False(string.IsNullOrWhiteSpace(DependentViewPlanner.ParentTag));
        }
    }
}
