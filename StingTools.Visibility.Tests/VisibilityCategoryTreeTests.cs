// Covers the enhancement runner §2 — making the category list usable on a real model.
//
// The observed defect on a 97-element architectural view: 20 rows including Cameras (2),
// Views (2), Scope Boxes (2), Section Boxes (1), two DXF import categories, and Runs /
// Supports / Top Rails sitting as siblings of Railings. All three fixes — exclusion,
// nesting, Model/Annotation/Imports split — are asserted here.

using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Visibility;
using Xunit;

namespace StingTools.Visibility.Tests
{
    public class VisibilityCategoryTreeTests
    {
        private static CategoryTally Cat(
            int id, string name, string builtIn = null, int count = 1,
            int parent = 0, CategoryGroupKind group = CategoryGroupKind.Model) =>
            new CategoryTally
            {
                CategoryId = id,
                Name = name,
                BuiltInName = builtIn,
                Count = count,
                ParentCategoryId = parent,
                Group = group
            };

        // ── Exclusion ───────────────────────────────────────────────────

        [Theory]
        [InlineData("OST_Cameras")]
        [InlineData("OST_Views")]
        [InlineData("OST_SectionBox")]
        [InlineData("OST_ScopeBoxes")]
        public void ViewManagementCategories_AreExcluded(string builtIn)
        {
            var tree = VisibilityCategoryTreeBuilder.Build(
                new[] { Cat(1, "Thing", builtIn), Cat(2, "Ducts", "OST_DuctCurves") },
                VisibilityCategoryTreeBuilder.DefaultExclusions);

            Assert.DoesNotContain(tree.Flatten(), n => n.CategoryId == 1);
            Assert.Contains(tree.Flatten(), n => n.Name == "Ducts");
        }

        [Fact]
        public void ExcludedCategories_AreReported_NotSilentlyDropped()
        {
            var tree = VisibilityCategoryTreeBuilder.Build(
                new[] { Cat(1, "Cameras", "OST_Cameras", count: 2) },
                VisibilityCategoryTreeBuilder.DefaultExclusions);

            Assert.Single(tree.Excluded);
            Assert.Equal("Cameras", tree.Excluded[0].Name);
            Assert.Equal(2, tree.ExcludedElementCount);
        }

        [Theory]
        [InlineData("OST_Grids", "Grids")]
        [InlineData("OST_Levels", "Levels")]
        public void GridsAndLevels_AreKept_HidingThoseIsARealRequest(string builtIn, string name)
        {
            var tree = VisibilityCategoryTreeBuilder.Build(
                new[] { Cat(1, name, builtIn) }, VisibilityCategoryTreeBuilder.DefaultExclusions);

            Assert.Contains(tree.Flatten(), n => n.Name == name);
            Assert.Empty(tree.Excluded);
        }

        [Fact]
        public void AnEmptyExclusionList_ExcludesNothing()
        {
            // A project overriding "excludedCategories": [] must get everything back.
            var tree = VisibilityCategoryTreeBuilder.Build(
                new[] { Cat(1, "Cameras", "OST_Cameras") }, new string[0]);

            Assert.Contains(tree.Flatten(), n => n.Name == "Cameras");
            Assert.Empty(tree.Excluded);
        }

        [Fact]
        public void ANullExclusionList_FallsBackToTheShippedDefaults()
        {
            var tree = VisibilityCategoryTreeBuilder.Build(
                new[] { Cat(1, "Cameras", "OST_Cameras") }, null);

            Assert.Single(tree.Excluded);
        }

        // ── Nesting ─────────────────────────────────────────────────────

        [Fact]
        public void Subcategories_NestUnderTheirParent()
        {
            var tree = VisibilityCategoryTreeBuilder.Build(new[]
            {
                Cat(10, "Railings", "OST_StairsRailing", count: 4),
                Cat(11, "Runs",      count: 2, parent: 10),
                Cat(12, "Supports",  count: 3, parent: 10),
                Cat(13, "Top Rails", count: 1, parent: 10)
            }, new string[0]);

            Assert.Single(tree.Model);
            var railings = tree.Model[0];
            Assert.Equal("Railings", railings.Name);
            Assert.Equal(3, railings.Children.Count);
            Assert.Equal(new[] { "Runs", "Supports", "Top Rails" },
                         railings.Children.Select(c => c.Name).ToArray());
        }

        [Fact]
        public void AParentRowCountsItselfPlusItsChildren()
        {
            var tree = VisibilityCategoryTreeBuilder.Build(new[]
            {
                Cat(10, "Railings", count: 4),
                Cat(11, "Runs",     count: 2, parent: 10),
                Cat(12, "Supports", count: 3, parent: 10)
            }, new string[0]);

            Assert.Equal(9, tree.Model[0].TotalCount);
            Assert.Equal(4, tree.Model[0].Count);
        }

        [Fact]
        public void AChildWhoseParentIsOutOfScope_IsPromoted_NotDropped()
        {
            // The parent category has no elements in this view, so it has no tally. Dropping
            // the child would hide real elements behind a row that does not exist.
            var tree = VisibilityCategoryTreeBuilder.Build(
                new[] { Cat(11, "Runs", count: 2, parent: 999) }, new string[0]);

            Assert.Single(tree.Model);
            Assert.Equal("Runs", tree.Model[0].Name);
        }

        [Fact]
        public void AChildInheritsItsParentsGroup()
        {
            var tree = VisibilityCategoryTreeBuilder.Build(new[]
            {
                Cat(10, "Railings", count: 1, group: CategoryGroupKind.Model),
                Cat(11, "Runs", count: 1, parent: 10, group: CategoryGroupKind.Annotation)
            }, new string[0]);

            Assert.Single(tree.Model);
            Assert.Empty(tree.Annotation);
            Assert.Equal(CategoryGroupKind.Model, tree.Model[0].Children[0].Group);
        }

        // ── Model / Annotation / Imports split ──────────────────────────

        [Fact]
        public void CategoriesSplitIntoTheThreeGroupsRevitItselfUses()
        {
            var tree = VisibilityCategoryTreeBuilder.Build(new[]
            {
                Cat(1, "Ducts",  "OST_DuctCurves", group: CategoryGroupKind.Model),
                Cat(2, "Duct Tags", "OST_DuctTags", group: CategoryGroupKind.Annotation),
                Cat(3, "ACAD-masindi TP.dxf", group: CategoryGroupKind.Imports)
            }, new string[0]);

            Assert.Equal("Ducts", Assert.Single(tree.Model).Name);
            Assert.Equal("Duct Tags", Assert.Single(tree.Annotation).Name);
            Assert.Equal("ACAD-masindi TP.dxf", Assert.Single(tree.Imports).Name);
        }

        [Fact]
        public void EveryGroupHasAHeaderAndAnEmptyReason()
        {
            foreach (var kind in new[]
                     {
                         CategoryGroupKind.Model, CategoryGroupKind.Annotation, CategoryGroupKind.Imports
                     })
            {
                Assert.False(string.IsNullOrWhiteSpace(VisibilityCategoryTreeBuilder.GroupHeader(kind)));
                Assert.False(string.IsNullOrWhiteSpace(VisibilityCategoryTreeBuilder.EmptyReason(kind)));
            }
        }

        [Fact]
        public void RootsAreSortedByName()
        {
            var tree = VisibilityCategoryTreeBuilder.Build(
                new[] { Cat(1, "Walls"), Cat(2, "Ducts"), Cat(3, "Pipes") }, new string[0]);

            Assert.Equal(new[] { "Ducts", "Pipes", "Walls" },
                         tree.Model.Select(n => n.Name).ToArray());
        }

        [Fact]
        public void DuplicateTalliesForOneCategory_FoldTheirCounts_RatherThanLosingOne()
        {
            var tree = VisibilityCategoryTreeBuilder.Build(
                new[] { Cat(1, "Ducts", count: 3), Cat(1, "Ducts", count: 4) }, new string[0]);

            Assert.Equal(7, Assert.Single(tree.Model).Count);
        }

        [Fact]
        public void ASelfParentingCategoryDoesNotRecurse()
        {
            var tree = VisibilityCategoryTreeBuilder.Build(
                new[] { Cat(5, "Odd", count: 1, parent: 5) }, new string[0]);

            Assert.Single(tree.Model);
            Assert.Empty(tree.Model[0].Children);
        }
    }
}
