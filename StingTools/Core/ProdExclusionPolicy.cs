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
        /// <para>An override REPLACES a list it declares and leaves the others alone —
        /// a null list means "not stated", an empty list means "exclude nothing". The
        /// two must stay distinguishable or the key becomes impossible to override,
        /// which is the trap <c>excludedCategories</c> already documents in the
        /// visibility presets.</para>
        /// </summary>
        public static ProductExclusion Build(ProdExclusionDocument corporate, ProdExclusionDocument project)
        {
            List<string> Pick(Func<ProdExclusionDocument, List<string>> f)
                => (project != null && f(project) != null) ? f(project)
                 : (corporate != null ? f(corporate) : null);

            return ProductExclusion.Build(
                Pick(d => d.NotAProductCategories),
                Pick(d => d.NotAProductPatterns),
                Pick(d => d.ProtectedCategories),
                Pick(d => d.NotAProductFamilies));
        }
    }
}
