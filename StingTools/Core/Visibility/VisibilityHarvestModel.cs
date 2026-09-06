// StingTools — Visibility Center · harvest data model
//
// Revit-free by design, and <Compile Include>-linked into StingTools.Visibility.Tests.
// These three POCOs used to live inside TokenValueHarvester.cs; they were moved out so the
// category tree (VisibilityCategoryTree) and the state reconciler (VisibilityStateReconciler)
// can be unit-tested without the Revit API. The SCANNER stays in TokenValueHarvester.cs —
// only the shapes it produces live here.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Visibility
{
    /// <summary>Where a category belongs in Revit's own V/G tab split.</summary>
    public enum CategoryGroupKind
    {
        /// <summary>Model geometry. Also the fallback for anything unclassifiable —
        /// a category is never dropped just because we could not place it.</summary>
        Model,
        Annotation,
        /// <summary>Linked/imported CAD (a DWG or DXF gets one category per file).</summary>
        Imports
    }

    /// <summary>A distinct token value and how many elements carry it — renders as "Z02 (147)".</summary>
    public sealed class TokenValueTally
    {
        public string Value { get; set; }
        public int Count { get; set; }
        public bool IsUnset => VisibilityTokens.IsUnset(Value);
        public string Display => $"{Value} ({Count:N0})";
    }

    /// <summary>A distinct category present in scope, with the metadata the tree builder needs.</summary>
    public sealed class CategoryTally
    {
        /// <summary><c>Category.Id.Value</c> as an int — a BuiltInCategory value for built-in
        /// categories, a real element id for import categories and custom subcategories.</summary>
        public int CategoryId { get; set; }

        public string Name { get; set; }

        public int Count { get; set; }

        /// <summary>
        /// <c>Category.BuiltInCategory.ToString()</c> — "OST_DuctCurves". This is the string the
        /// exclusion list in STING_VISIBILITY_PRESETS.json is written against, so the exclusion
        /// test can stay Revit-free. "OST_Invalid" (or null) for a non-built-in category.
        /// </summary>
        public string BuiltInName { get; set; }

        /// <summary><c>Category.Parent.Id.Value</c>, or 0 when the category is top-level.</summary>
        public int ParentCategoryId { get; set; }

        public CategoryGroupKind Group { get; set; } = CategoryGroupKind.Model;

        public string Display => $"{Name} ({Count:N0})";
    }

    /// <summary>Everything one scan pass produced.</summary>
    public sealed class TokenHarvest
    {
        /// <summary>Per-element snapshots — the planner's input.</summary>
        public List<VisibilityElementSnapshot> Elements { get; set; }
            = new List<VisibilityElementSnapshot>();

        /// <summary>Token key → distinct values, ordered, with counts.</summary>
        public Dictionary<string, List<TokenValueTally>> TokenValues { get; set; }
            = new Dictionary<string, List<TokenValueTally>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Categories present in scope, ordered by display name.</summary>
        public List<CategoryTally> Categories { get; set; } = new List<CategoryTally>();

        /// <summary>Categories dropped by the excludedCategories list, kept for the count line.
        /// Nothing is silently dropped: these still show up in the harvest log.</summary>
        public List<CategoryTally> ExcludedCategories { get; set; } = new List<CategoryTally>();

        public int TotalElements => Elements == null ? 0 : Elements.Count;

        public int ExcludedElementCount =>
            ExcludedCategories == null ? 0 : ExcludedCategories.Sum(c => c.Count);

        public List<TokenValueTally> ValuesFor(string tokenKey)
        {
            List<TokenValueTally> v;
            return TokenValues != null && TokenValues.TryGetValue(tokenKey ?? string.Empty, out v)
                ? v : new List<TokenValueTally>();
        }

        /// <summary>
        /// Recompute a harvest over a different element set, reusing category metadata from an
        /// earlier pass. Used by <see cref="VisibilityStateReader"/> to widen the view-scoped
        /// harvest with the elements that are hidden — a hidden category has no row in a
        /// view-scoped scan, and a row you cannot see is a row you cannot re-tick.
        /// </summary>
        /// <param name="elements">The element set to tally.</param>
        /// <param name="metadata">Category metadata from any earlier harvest; counts are ignored.</param>
        public static TokenHarvest Rebuild(
            IList<VisibilityElementSnapshot> elements, IEnumerable<CategoryTally> metadata)
        {
            var harvest = new TokenHarvest();
            var meta = new Dictionary<int, CategoryTally>();
            foreach (var c in metadata ?? Enumerable.Empty<CategoryTally>())
                if (c != null) meta[c.CategoryId] = c;

            var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in VisibilityTokens.All)
                counts[t] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var cats = new Dictionary<int, CategoryTally>();

            foreach (var el in elements ?? new List<VisibilityElementSnapshot>())
            {
                if (el == null) continue;
                harvest.Elements.Add(el);

                foreach (var t in VisibilityTokens.All)
                {
                    string raw = el.Token(t);
                    string value = string.IsNullOrWhiteSpace(raw) ? VisibilityTokens.Unset : raw.Trim();
                    var bucket = counts[t];
                    int n;
                    bucket[value] = bucket.TryGetValue(value, out n) ? n + 1 : 1;
                }

                if (el.CategoryId == 0) continue;
                CategoryTally ct;
                if (!cats.TryGetValue(el.CategoryId, out ct))
                {
                    CategoryTally src;
                    ct = meta.TryGetValue(el.CategoryId, out src)
                        ? new CategoryTally
                        {
                            CategoryId = src.CategoryId,
                            Name = src.Name,
                            BuiltInName = src.BuiltInName,
                            ParentCategoryId = src.ParentCategoryId,
                            Group = src.Group
                        }
                        : new CategoryTally
                        {
                            CategoryId = el.CategoryId,
                            Name = el.CategoryName ?? el.CategoryId.ToString()
                        };
                    cats[el.CategoryId] = ct;
                }
                ct.Count++;
            }

            foreach (var t in VisibilityTokens.All)
            {
                harvest.TokenValues[t] = counts[t]
                    .Select(p => new TokenValueTally { Value = p.Key, Count = p.Value })
                    .OrderBy(v => v.IsUnset ? 1 : 0)
                    .ThenBy(v => v.Value, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            harvest.Categories = cats.Values
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return harvest;
        }
    }
}
