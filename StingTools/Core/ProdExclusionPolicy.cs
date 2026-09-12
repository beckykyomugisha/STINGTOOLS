// ══════════════════════════════════════════════════════════════════════════
//  ProdExclusionPolicy.cs — the PROD half of "not a product", loaded from
//  STING_PROD_EXCLUSIONS.json with a project override.
//
//  Parsing is kept Revit-free (it takes JSON TEXT, not a Document) so the
//  shipped policy is assertable outside Revit — the same split the material
//  schedule uses, and the reason its data defects were catchable at all.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace StingTools.Core
{
    /// <summary>The JSON shape of STING_PROD_EXCLUSIONS.json.</summary>
    public sealed class ProdExclusionDocument
    {
        [JsonProperty("notAProductCategories")]
        public List<string> NotAProductCategories;

        [JsonProperty("notAProductPatterns")]
        public List<string> NotAProductPatterns;

        [JsonProperty("protectedCategories")]
        public List<string> ProtectedCategories;

        /// <summary>Exact family names that are never products. Exact, so it cannot
        /// misfire the way a substring can — and therefore allowed to override
        /// <see cref="ProtectedCategories"/>.</summary>
        [JsonProperty("notAProductFamilies")]
        public List<string> NotAProductFamilies;
    }

    public static class ProdExclusionPolicy
    {
        /// <summary>
        /// Parse one policy document. Returns null — not an empty policy — when the
        /// text is absent or unreadable, so a caller can tell "no file" from "a file
        /// that excludes nothing". Those mean different things: the second is a
        /// deliberate choice and the first is a missing deployment.
        /// </summary>
        public static ProdExclusionDocument Parse(string json, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(json)) { error = "empty"; return null; }
            try { return JsonConvert.DeserializeObject<ProdExclusionDocument>(json); }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        /// <summary>
        /// Layer a project override over the corporate baseline and build the matcher.
        ///
        /// <para><b>Three keys REPLACE; one UNIONS, and the difference is not a
        /// convenience.</b></para>
        ///
        /// <para><c>notAProductCategories</c>, <c>notAProductPatterns</c> and
        /// <c>protectedCategories</c> replace the list they declare — a null list means
        /// "not stated", an empty list means "exclude nothing", and the two must stay
        /// distinguishable or the key becomes impossible to override (the trap
        /// <c>excludedCategories</c> already documents in the visibility presets). Those
        /// three are all fuzzy or sweeping: a PATTERN can misfire on a name nobody
        /// anticipated and a CATEGORY holds whatever a project models in it, so a project
        /// must be able to say "not that one".</para>
        ///
        /// <para><c>notAProductFamilies</c> UNIONS. It is an EXACT family name — the
        /// reason the corporate file allows it to override <c>protectedCategories</c> at
        /// all — so it cannot misfire, and there is no coherent reason for a project to
        /// need Revit's stock wall-void family counted as a product. Replace-semantics
        /// here meant naming one family silently un-named every other:</para>
        ///
        /// <para>Measured 2026-09-09. A project override was written declaring only
        /// <c>A_Revit_Suv_3d_car</c>, to stop an entourage car being counted. It dropped
        /// the corporate <c>Window-Square Opening</c>, and <b>three wall voids returned to
        /// the PROD coverage denominator</b> — visible in the audit only as the total
        /// moving 414 → 416 while one row left and three arrived. Nothing reported it,
        /// because a denominator has no error state.</para>
        ///
        /// <para>A project that genuinely wants a corporate family back in the count must
        /// say so in the corporate file, where the argument can be had once.</para>
        /// </summary>
        public static ProductExclusion Build(ProdExclusionDocument corporate, ProdExclusionDocument project)
        {
            List<string> Replace(Func<ProdExclusionDocument, List<string>> f)
                => (project != null && f(project) != null) ? f(project)
                 : (corporate != null ? f(corporate) : null);

            return ProductExclusion.Build(
                Replace(d => d.NotAProductCategories),
                Replace(d => d.NotAProductPatterns),
                Replace(d => d.ProtectedCategories),
                Union(corporate?.NotAProductFamilies, project?.NotAProductFamilies));
        }

        /// <summary>
        /// Corporate ∪ project, order-preserving and case-insensitively de-duplicated.
        /// Returns null when NEITHER side states anything, so "no file" stays
        /// distinguishable from "an empty list" — the same reason
        /// <see cref="Parse"/> returns null rather than an empty document.
        /// </summary>
        private static List<string> Union(List<string> corporate, List<string> project)
        {
            if (corporate == null && project == null) return null;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var outList = new List<string>();
            foreach (var src in new[] { corporate, project })
                foreach (string s in src ?? Enumerable.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    if (seen.Add(s.Trim())) outList.Add(s.Trim());
                }
            return outList;
        }
    }
}
