// StingTools — Visibility Center · category tree
//
// Revit-free. Turns the flat CategoryTally list the harvester produces into the three
// groups Revit's own V/G dialog uses (Model / Annotation / Imports), with subcategories
// nested under Category.Parent.
//
// The whole point is that NOTHING IS SILENTLY DROPPED. A category that is excluded lands
// in Excluded (and is reported); a category we cannot classify lands in Model rather than
// vanishing; a child whose parent is not in scope is promoted to top level rather than
// being orphaned out of the list.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Visibility
{
    /// <summary>One row of the category list: a category, plus any subcategories under it.</summary>
    public sealed class VisibilityCategoryNode
    {
        public int CategoryId { get; set; }
        public string Name { get; set; }

        /// <summary>Elements whose own category is exactly this one.</summary>
        public int Count { get; set; }

        public CategoryGroupKind Group { get; set; }

        public List<VisibilityCategoryNode> Children { get; set; }
            = new List<VisibilityCategoryNode>();

        /// <summary>This node plus every descendant — what a parent row's count should read.</summary>
        public int TotalCount => Count + Children.Sum(c => c.TotalCount);

        public bool HasChildren => Children != null && Children.Count > 0;
    }

    /// <summary>The grouped, nested category list plus an honest account of what was left out.</summary>
    public sealed class VisibilityCategoryTree
    {
        public List<VisibilityCategoryNode> Model { get; set; } = new List<VisibilityCategoryNode>();
        public List<VisibilityCategoryNode> Annotation { get; set; } = new List<VisibilityCategoryNode>();
        public List<VisibilityCategoryNode> Imports { get; set; } = new List<VisibilityCategoryNode>();

        /// <summary>Categories removed by the excludedCategories list — reported, never silent.</summary>
        public List<CategoryTally> Excluded { get; set; } = new List<CategoryTally>();

        public int ExcludedElementCount => Excluded.Sum(c => c.Count);

        public List<VisibilityCategoryNode> For(CategoryGroupKind group)
        {
            switch (group)
            {
                case CategoryGroupKind.Annotation: return Annotation;
                case CategoryGroupKind.Imports:    return Imports;
                default:                           return Model;
            }
        }

        /// <summary>Every node in every group, parents and children alike.</summary>
        public IEnumerable<VisibilityCategoryNode> Flatten()
        {
            foreach (var root in Model.Concat(Annotation).Concat(Imports))
            {
                yield return root;
                foreach (var child in root.Children) yield return child;
            }
        }
    }

    /// <summary>Builds a <see cref="VisibilityCategoryTree"/> from flat tallies.</summary>
    public static class VisibilityCategoryTreeBuilder
    {
        /// <summary>
        /// The shipped default exclusions, used only when neither the corporate baseline nor the
        /// project override declares an <c>excludedCategories</c> list. Kept here, next to the
        /// code that consumes it, so a blank/absent JSON key still produces a usable list rather
        /// than a screen of Cameras and Section Boxes.
        /// <para>Grids and Levels are deliberately NOT excluded — hiding those is a real and
        /// common request.</para>
        /// </summary>
        public static readonly string[] DefaultExclusions =
        {
            "OST_Cameras", "OST_Views", "OST_SectionBox", "OST_ScopeBoxes"
        };

        /// <summary>
        /// Group, nest and filter. <paramref name="excludedBuiltInNames"/> holds BuiltInCategory
        /// names ("OST_Cameras"); a null list means "use <see cref="DefaultExclusions"/>", an
        /// empty list means "exclude nothing" — that distinction is what makes the JSON key
        /// genuinely overridable per project.
        /// </summary>
        public static VisibilityCategoryTree Build(
            IEnumerable<CategoryTally> tallies, IEnumerable<string> excludedBuiltInNames)
        {
            var tree = new VisibilityCategoryTree();
            var source = (tallies ?? Enumerable.Empty<CategoryTally>()).Where(t => t != null).ToList();

            var excluded = new HashSet<string>(
                excludedBuiltInNames ?? DefaultExclusions, StringComparer.OrdinalIgnoreCase);

            // Pass 1 — split off the exclusions, and build a node per surviving category.
            var nodes = new Dictionary<int, VisibilityCategoryNode>();
            foreach (var t in source)
            {
                if (!string.IsNullOrEmpty(t.BuiltInName) && excluded.Contains(t.BuiltInName))
                {
                    tree.Excluded.Add(t);
                    continue;
                }

                // A duplicate id would silently lose a tally, so fold the counts instead.
                VisibilityCategoryNode existing;
                if (nodes.TryGetValue(t.CategoryId, out existing))
                {
                    existing.Count += t.Count;
                    continue;
                }

                nodes[t.CategoryId] = new VisibilityCategoryNode
                {
                    CategoryId = t.CategoryId,
                    Name = string.IsNullOrWhiteSpace(t.Name) ? t.CategoryId.ToString() : t.Name,
                    Count = t.Count,
                    Group = t.Group
                };
            }

            // Pass 2 — nest. A child whose parent is not in scope is promoted to top level:
            // dropping it would hide real elements behind a row that does not exist.
            var parentOf = source
                .Where(t => t.ParentCategoryId != 0 && nodes.ContainsKey(t.CategoryId))
                .GroupBy(t => t.CategoryId)
                .ToDictionary(g => g.Key, g => g.First().ParentCategoryId);

            var isChild = new HashSet<int>();
            foreach (var kv in parentOf)
            {
                VisibilityCategoryNode parent, child;
                if (!nodes.TryGetValue(kv.Value, out parent)) continue;   // parent out of scope → stays top-level
                if (!nodes.TryGetValue(kv.Key, out child)) continue;
                if (ReferenceEquals(parent, child)) continue;             // self-parent guard

                parent.Children.Add(child);
                // A child inherits its parent's group so a subcategory never lands in a
                // different tab from the thing it belongs to.
                child.Group = parent.Group;
                isChild.Add(kv.Key);
            }

            // Pass 3 — bucket the roots, sorted by name; children sorted within each parent.
            foreach (var node in nodes.Values.Where(n => !isChild.Contains(n.CategoryId)))
                tree.For(node.Group).Add(node);

            foreach (var list in new[] { tree.Model, tree.Annotation, tree.Imports })
            {
                list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                foreach (var n in list)
                    n.Children.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }

            return tree;
        }

        /// <summary>Header label for a group — "MODEL CATEGORIES", "ANNOTATION", "IMPORTS".</summary>
        public static string GroupHeader(CategoryGroupKind group)
        {
            switch (group)
            {
                case CategoryGroupKind.Annotation: return "ANNOTATION CATEGORIES";
                case CategoryGroupKind.Imports:    return "IMPORTS / LINKED CAD";
                default:                           return "MODEL CATEGORIES";
            }
        }

        /// <summary>
        /// Why a group is empty, phrased for the user. Returned for every group so the dropdown
        /// never shows a blank expander that could equally mean "nothing here" or "the scan
        /// failed" — the distinction between those two is the whole point.
        /// </summary>
        public static string EmptyReason(CategoryGroupKind group)
        {
            switch (group)
            {
                case CategoryGroupKind.Annotation:
                    return "no annotation in this view";
                case CategoryGroupKind.Imports:
                    return "no linked or imported CAD in this view";
                default:
                    return "no model elements in this view";
            }
        }
    }
}
