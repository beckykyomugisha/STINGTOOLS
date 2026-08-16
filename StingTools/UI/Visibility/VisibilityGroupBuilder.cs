// StingTools — Visibility Center · dropdown group construction
//
// Split out of VisibilityDropdown.xaml.cs so neither file outgrows a readable size. This is
// the only place that turns a harvest + a read-back into rows, so "which state does a row
// open in" has exactly one answer.

using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Visibility;

namespace StingTools.UI.VisibilityCenter
{
    internal static class VisibilityGroupBuilder
    {
        /// <summary>The three category groups, mirroring Revit's own V/G tabs.</summary>
        internal static readonly CategoryGroupKind[] CategoryGroups =
        {
            CategoryGroupKind.Model, CategoryGroupKind.Annotation, CategoryGroupKind.Imports
        };

        /// <summary>
        /// Build the category groups: excluded categories already removed by the harvester,
        /// subcategories nested under their parent, split Model / Annotation / Imports.
        /// Rows open in whatever state <paramref name="readback"/> says the view is in.
        /// </summary>
        internal static List<VisGroupVm> BuildCategoryGroups(
            TokenHarvest harvest, VisibilityReadback readback)
        {
            var tree = VisibilityCategoryTreeBuilder.Build(harvest.Categories, ExclusionsAlreadyApplied);
            var groups = new List<VisGroupVm>();

            foreach (var kind in CategoryGroups)
            {
                var group = new VisGroupVm
                {
                    TokenKey = null,
                    CategoryGroup = kind,
                    Title = VisibilityCategoryTreeBuilder.GroupHeader(kind),
                    // Only Model opens expanded — three expanded category groups plus seven
                    // token groups is a wall of rows on first open.
                    IsExpanded = kind == CategoryGroupKind.Model,
                    IsLoaded = true
                };

                foreach (var node in tree.For(kind))
                    group.Rows.Add(BuildCategoryRow(node, readback, null));

                group.SetCount(group.AllRows().Count(),
                               VisibilityCategoryTreeBuilder.EmptyReason(kind));
                group.ApplySearch(null);
                groups.Add(group);
            }
            return groups;
        }

        // The harvester already applied the project's excludedCategories list, so re-applying
        // it here would double-count. An empty list means "exclude nothing further".
        private static readonly string[] ExclusionsAlreadyApplied = new string[0];

        private static VisRowVm BuildCategoryRow(
            VisibilityCategoryNode node, VisibilityReadback readback, VisRowVm parent)
        {
            var row = new VisRowVm
            {
                Key = node.Name,
                CategoryId = node.CategoryId,
                Count = node.Count,
                Parent = parent
            };

            foreach (var child in node.Children)
                row.Children.Add(BuildCategoryRow(child, readback, row));

            // Children are built first, so a parent's tri-state can be rolled up from real
            // child states instead of guessed from a default.
            var state = readback?.Category(node.CategoryId);
            bool hidden = state != null && state.IsHidden;
            row.OwnHidden = hidden;

            if (row.HasChildren)
                row.RecomputeFromChildren();        // folds in OwnHidden: all on, all off, or the middle
            else
                row.SetChecked(!hidden);

            if (state != null && state.IsHidden) row.HiddenReason = state.Reason;
            return row;
        }

        /// <summary>The seven tag-token groups. Rows are built lazily on first expand.</summary>
        internal static List<VisGroupVm> BuildTokenGroups(
            TokenHarvest harvest, VisibilityReadback readback)
        {
            var groups = new List<VisGroupVm>();
            foreach (var token in VisibilityTokens.All)
            {
                int count = harvest.ValuesFor(token).Count;
                var group = new VisGroupVm
                {
                    TokenKey = token,
                    Title = VisibilityTokens.Label(token),
                    IsExpanded = false,
                    IsLoaded = false
                };
                group.SetCount(count, EmptyTokenReason(token));
                groups.Add(group);
            }
            return groups;
        }

        /// <summary>
        /// Fill a token group's rows. Idempotent — safe to call from the Expanded handler and
        /// from All/None/Invert on a group the user never opened.
        /// </summary>
        internal static void PopulateTokenRows(
            VisGroupVm group, TokenHarvest harvest, VisibilityReadback readback)
        {
            if (group == null || group.IsLoaded || group.TokenKey == null) return;

            foreach (var v in harvest.ValuesFor(group.TokenKey))
            {
                var row = new VisRowVm { Key = v.Value, Count = v.Count };
                var state = readback?.Token(group.TokenKey, v.Value);
                row.SetChecked(state == null || !state.IsHidden);
                if (state != null && state.IsHidden) row.HiddenReason = state.Reason;
                group.Rows.Add(row);
            }
            group.IsLoaded = true;
            group.SetCount(group.Rows.Count, EmptyTokenReason(group.TokenKey));
        }

        /// <summary>
        /// Why a token group is empty. An untagged model expands all seven to nothing, and
        /// "no rows" alone cannot be told apart from "the harvest failed".
        /// </summary>
        internal static string EmptyTokenReason(string tokenKey) =>
            $"no {tokenKey} values in this view — run tagging first";
    }
}
