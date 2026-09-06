// ══════════════════════════════════════════════════════════════════════════
//  HarvestDimensionRules.cs — which parameters actually define a type, per
//  category.
//
//  FOUND IN A REAL HARVEST. One flat list — Width, Height, Depth, Thickness,
//  Diameter, Length, b, h, d — was asked of every category. On a
//  Concrete-Rectangular-Column it returned:
//
//      b = 450, h = 450, Width = 4500, Height = 12000
//
//  b and h are the section. 4500 x 12000 is not a column; those are extents,
//  and on a column "Width" and "Height" mean something else entirely. The
//  harvested pack therefore proposed minting a column 4.5 m wide, and because
//  a pack is data rather than code nothing would have complained.
//
//  So the list is per category, and a category we do not know about harvests
//  NOTHING rather than guessing. A pack with no dimensions is visibly
//  incomplete; a pack with confident wrong ones is not.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Baseline
{
    public static class HarvestDimensionRules
    {
        /// <summary>
        /// Category → the parameters that define a type's SIZE there.
        ///
        /// Deliberately short. A pack carrying every parameter of every type is
        /// unreviewable, and an unreviewed pack is the thing this whole layer
        /// exists to avoid.
        /// </summary>
        private static readonly Dictionary<string, string[]> ByCategory =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                // A door is a leaf plus a frame; thickness distinguishes an
                // external door from an internal one at the same opening.
                ["Doors"]                    = new[] { "Width", "Height", "Thickness" },
                ["Windows"]                  = new[] { "Width", "Height" },

                // b and h ONLY. This is the row the defect was in: a column's
                // Width/Height are not its section.
                ["Structural Columns"]       = new[] { "b", "h" },
                ["Structural Framing"]       = new[] { "b", "h", "d" },

                // A footing genuinely is measured Width x Length x Thickness.
                ["Structural Foundations"]   = new[] { "Width", "Length", "Thickness" },
            };

        /// <summary>
        /// The parameters worth reading for a category, or EMPTY for one we
        /// have no rule for.
        ///
        /// Empty, not a fallback list. A guess here is silent and ends up in a
        /// catalogue somebody later mints from.
        /// </summary>
        public static IReadOnlyList<string> For(string category)
        {
            if (!string.IsNullOrWhiteSpace(category)
                && ByCategory.TryGetValue(category.Trim(), out var names))
                return names;
            return Array.Empty<string>();
        }

        /// <summary>True when a category has a rule at all.</summary>
        public static bool IsKnown(string category) => For(category).Count > 0;

        /// <summary>Every category with a rule, for the audit text.</summary>
        public static IEnumerable<string> KnownCategories => ByCategory.Keys.OrderBy(k => k);
    }
}
