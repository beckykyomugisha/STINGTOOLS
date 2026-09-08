// ══════════════════════════════════════════════════════════════════════════
//  ProductExclusion.cs — "this element is not a thing you can name", shared.
//
//  Two features ask that question and each had its own copy of the answer:
//  the material schedule (is this a purchasable MATERIAL?) and the PROD
//  coverage audit (is this a taggable PRODUCT?).
//
//  The MECHANISM is identical and lives here once:
//      • an excluded CATEGORY removes the row outright
//      • a description/type PATTERN removes it despite a legitimate category
//      • a PROTECTED category outranks any pattern
//      • a blank pattern is inert, never a wildcard
//      • exclusions are COUNTED, never silently dropped
//
//  The POLICY is NOT shared, and that distinction is the whole point. The
//  material schedule excludes Furniture and Casework — you do not buy a sofa
//  by the cubic metre — but a sofa is a perfectly good PROD product and FUR is
//  a real code. Reusing one list for both questions would trade a wrong number
//  for a differently wrong number. So each caller declares its own lists and
//  this class only decides what those lists MEAN.
//
//  Why a pattern layer exists at all: the first real material export sold an
//  OPENING 1,187 times. `M_GM_OpeningWall_Instance — Opening` is a void, but
//  its category is Generic Models, which elsewhere holds genuine building
//  elements — so no category rule could remove it without hiding real work.
//  The PROD coverage audit on that same model counted those same 1,187 voids
//  as products that failed to get a specific code, reporting 0.8% coverage
//  where the honest figure over real products is 3.1%.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core
{
    /// <summary>Why a row was excluded, or that it was not.</summary>
    public enum ExclusionVerdict
    {
        /// <summary>A real thing. Counts in the denominator.</summary>
        Included = 0,

        /// <summary>Its whole category is out of scope for this question.</summary>
        ByCategory,

        /// <summary>Legitimate category, but this instance is not a thing —
        /// an opening, a muntin pattern, a filled region.</summary>
        ByPattern,
    }

    /// <summary>
    /// An immutable exclusion policy. Build once per run; <see cref="Classify"/>
    /// is called per element, so the sets are pre-built rather than re-scanned.
    /// </summary>
    public sealed class ProductExclusion
    {
        private readonly HashSet<string> _categories;
        private readonly HashSet<string> _protected;
        private readonly List<string> _patterns;

        /// <summary>Nothing is excluded. The correct default: a policy that
        /// silently removes rows nobody asked it to remove is worse than none.</summary>
        public static readonly ProductExclusion None =
            new ProductExclusion(null, null, null);

        private ProductExclusion(
            IEnumerable<string> categories,
            IEnumerable<string> patterns,
            IEnumerable<string> protectedCategories)
        {
            _categories = new HashSet<string>(
                (categories ?? Enumerable.Empty<string>())
                    .Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()),
                StringComparer.OrdinalIgnoreCase);

            _protected = new HashSet<string>(
                (protectedCategories ?? Enumerable.Empty<string>())
                    .Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()),
                StringComparer.OrdinalIgnoreCase);

            // Blank patterns are DROPPED, not kept: "".IndexOf returns 0, so one
            // empty cell in a data file would exclude the entire model.
            _patterns = (patterns ?? Enumerable.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();
        }

        public static ProductExclusion Build(
            IEnumerable<string> excludedCategories,
            IEnumerable<string> excludedDescriptionPatterns,
            IEnumerable<string> protectedCategories)
            => new ProductExclusion(excludedCategories, excludedDescriptionPatterns, protectedCategories);

        /// <summary>True when this policy would exclude nothing, so a caller can
        /// say "no exclusions applied" rather than reporting a silent zero.</summary>
        public bool IsEmpty => _categories.Count == 0 && _patterns.Count == 0;

        public int CategoryCount => _categories.Count;
        public int PatternCount => _patterns.Count;

        /// <summary>
        /// Classify one element. <paramref name="description"/> and
        /// <paramref name="typeName"/> are both searched, joined, because the two
        /// callers carry the identifying text in different fields.
        /// </summary>
        public ExclusionVerdict Classify(string category, string description, string typeName)
        {
            string cat = (category ?? "").Trim();

            if (cat.Length > 0 && _categories.Contains(cat))
                return ExclusionVerdict.ByCategory;

            if (_patterns.Count == 0) return ExclusionVerdict.Included;

            // A door or a window is always a real thing, whatever it is called.
            // The first material export dropped one Windows row as not-a-material
            // while keeping twelve others, because a pattern written for Generic
            // Models voids matched a real window type.
            if (cat.Length > 0 && _protected.Contains(cat))
                return ExclusionVerdict.Included;

            string hay = ((description ?? "") + " " + (typeName ?? ""));
            foreach (string p in _patterns)
                if (hay.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)
                    return ExclusionVerdict.ByPattern;

            return ExclusionVerdict.Included;
        }
    }
}
