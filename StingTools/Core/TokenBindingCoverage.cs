using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core
{
    /// <summary>How a parameter is bound in a document, for one category.</summary>
    public enum TokenBindingScope
    {
        /// <summary>Not bound to this category at all.</summary>
        Missing = 0,
        /// <summary>Bound per instance. The only scope the tagging pipeline can write.</summary>
        Instance = 1,
        /// <summary>Bound to the type. Invisible to Element.LookupParameter, so a write
        /// to an instance silently no-ops.</summary>
        Type = 2,
    }

    /// <summary>One (parameter, category) binding as the document actually holds it.</summary>
    public sealed class TokenBindingFact
    {
        public string Param { get; set; }
        public string Category { get; set; }
        public TokenBindingScope Scope { get; set; }
    }

    /// <summary>A (parameter, category) pair the tagging pipeline cannot write.</summary>
    public sealed class TokenBindingGap
    {
        public string Param { get; set; }
        public string Category { get; set; }
        public TokenBindingScope Scope { get; set; }
        public string Why { get; set; }
    }

    /// <summary>
    /// Which of the eight tag tokens are actually writable, per category, in THIS document.
    ///
    /// The pipeline derives every token non-empty and then writes it with
    /// ParameterHelpers.SetString, which resolves through Element.LookupParameter —
    /// instance scope only. A token bound to the TYPE, or not bound to the element's
    /// category, therefore fails the write silently. Until 2026-09-17 nothing said so:
    /// the value was derived correctly, discarded, and the resulting blank was reported
    /// downstream as a missing code.
    ///
    /// This answers the question BEFORE tagging, and it derives the expectation rather
    /// than listing it: every token parameter, crossed with the categories actually in
    /// scope. A token added to the registry, or a category added to DiscMap, is covered
    /// without anyone editing this file.
    /// </summary>
    public static class TokenBindingCoverage
    {
        public static List<TokenBindingGap> Analyse(
            IEnumerable<TokenBindingFact> facts,
            IEnumerable<string> tokenParams,
            IEnumerable<string> categoriesInScope)
        {
            var gaps = new List<TokenBindingGap>();
            if (tokenParams == null || categoriesInScope == null) return gaps;

            var index = new Dictionary<string, TokenBindingScope>(StringComparer.Ordinal);
            if (facts != null)
            {
                foreach (var f in facts)
                {
                    if (f == null || string.IsNullOrEmpty(f.Param) || string.IsNullOrEmpty(f.Category))
                        continue;
                    string k = Key(f.Param, f.Category);
                    // A parameter can appear under more than one binding; the writable
                    // one wins, because if ANY binding makes it instance-reachable the
                    // write succeeds.
                    if (index.TryGetValue(k, out var existing) && existing == TokenBindingScope.Instance)
                        continue;
                    index[k] = f.Scope;
                }
            }

            var cats = categoriesInScope.Where(c => !string.IsNullOrWhiteSpace(c))
                                        .Distinct(StringComparer.Ordinal).ToList();

            foreach (string p in tokenParams.Where(p => !string.IsNullOrWhiteSpace(p))
                                            .Distinct(StringComparer.Ordinal))
            {
                foreach (string c in cats)
                {
                    index.TryGetValue(Key(p, c), out var scope);   // absent => Missing (0)
                    if (scope == TokenBindingScope.Instance) continue;

                    gaps.Add(new TokenBindingGap
                    {
                        Param = p,
                        Category = c,
                        Scope = scope,
                        Why = scope == TokenBindingScope.Type
                            ? "bound to the TYPE — a write to an instance silently does nothing, "
                              + "and every instance of a type would share one value"
                            : "not bound to this category — the parameter does not exist on the element",
                    });
                }
            }
            return gaps;
        }

        /// <summary>Gaps rolled up per parameter, worst first. The unit an operator repairs.</summary>
        public static List<KeyValuePair<string, List<TokenBindingGap>>> ByParam(IEnumerable<TokenBindingGap> gaps)
        {
            if (gaps == null) return new List<KeyValuePair<string, List<TokenBindingGap>>>();
            return gaps.GroupBy(g => g.Param, StringComparer.Ordinal)
                       .OrderByDescending(g => g.Count())
                       .ThenBy(g => g.Key, StringComparer.Ordinal)
                       .Select(g => new KeyValuePair<string, List<TokenBindingGap>>(g.Key, g.ToList()))
                       .ToList();
        }

        private static string Key(string param, string category) => param + "\u0001" + category;
    }
}
