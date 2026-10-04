using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StingTools.Core
{
    // ─────────────────────────────────────────────────────────────────────────
    // TAGACC-27 — is there anywhere for a tag to be written?
    //
    // Batch Tag used to derive every token for every element, try to write them,
    // read back blanks, and only then report "NOT WRITTEN … could not be written"
    // per category and per token. On a model where Load Shared Parameters had
    // never run (2026-10-02, KUT Interior model) that was 99 s and 3,121 refusals
    // to say one thing: the token parameters do not exist here. The real cause
    // sat in one line at the bottom, under 100 "malformed tag '-------'"
    // warnings that pointed somewhere else.
    //
    // The command probes ONE element per category (instance lookup, the same
    // scope SetString writes through) and hands the result here. Revit-free so
    // the verdict and its wording are tested without Revit.
    // ─────────────────────────────────────────────────────────────────────────

    internal enum TokenBindingVerdict
    {
        /// <summary>Every probed category can hold every token parameter.</summary>
        AllBound,
        /// <summary>Some categories cannot: their elements will be refused, the rest tag.</summary>
        SomeCategoriesUnbound,
        /// <summary>No category can hold a complete tag: the run would write nothing.</summary>
        NothingBound,
        /// <summary>Nothing was probed (no taggable elements, or no token list loaded).</summary>
        NotProbed,
    }

    internal sealed class TokenBindingProbe
    {
        public TokenBindingProbe(string category, int elementCount, IEnumerable<string> missingParams)
        {
            Category = category ?? "";
            ElementCount = elementCount;
            MissingParams = (missingParams ?? Enumerable.Empty<string>())
                .Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.Ordinal).ToList();
        }

        public string Category { get; }
        public int ElementCount { get; }
        public IReadOnlyList<string> MissingParams { get; }
        public bool FullyBound => MissingParams.Count == 0;
    }

    internal sealed class TokenBindingResult
    {
        public TokenBindingVerdict Verdict { get; set; }
        public IReadOnlyList<TokenBindingProbe> Unbound { get; set; } = Array.Empty<TokenBindingProbe>();
        public int UnboundElementCount { get; set; }
        public int TotalElementCount { get; set; }
        /// <summary>One-line summary for a status line or log; empty when AllBound.</summary>
        public string Summary { get; set; } = "";
        /// <summary>Full text for a refusal dialog; empty unless NothingBound.</summary>
        public string RefusalText { get; set; } = "";
    }

    internal static class TokenBindingPreflight
    {
        /// <summary>How many unbound categories the summary names before "+N more".</summary>
        public const int MaxNamedCategories = 6;

        public static TokenBindingResult Evaluate(IEnumerable<TokenBindingProbe> probes)
        {
            var list = (probes ?? Enumerable.Empty<TokenBindingProbe>())
                .Where(p => p != null && p.ElementCount > 0).ToList();
            var result = new TokenBindingResult
            {
                TotalElementCount = list.Sum(p => p.ElementCount),
            };
            if (list.Count == 0)
            {
                result.Verdict = TokenBindingVerdict.NotProbed;
                return result;
            }

            var unbound = list.Where(p => !p.FullyBound)
                .OrderByDescending(p => p.ElementCount)
                .ThenBy(p => p.Category, StringComparer.Ordinal)
                .ToList();
            result.Unbound = unbound;
            result.UnboundElementCount = unbound.Sum(p => p.ElementCount);

            if (unbound.Count == 0)
            {
                result.Verdict = TokenBindingVerdict.AllBound;
                return result;
            }

            result.Verdict = unbound.Count == list.Count
                ? TokenBindingVerdict.NothingBound
                : TokenBindingVerdict.SomeCategoriesUnbound;

            string named = NameCategories(unbound);
            if (result.Verdict == TokenBindingVerdict.NothingBound)
            {
                result.Summary = $"STING token parameters are not bound in this model — nothing can be tagged "
                               + $"({result.TotalElementCount:N0} element(s) in {list.Count} categor{(list.Count == 1 ? "y" : "ies")}).";
                var sb = new StringBuilder();
                sb.AppendLine("Batch Tag stopped before writing anything.");
                sb.AppendLine();
                sb.AppendLine("The STING token parameters (" + string.Join(", ", unbound
                    .SelectMany(p => p.MissingParams).Distinct(StringComparer.Ordinal).Take(8)) + ")");
                sb.AppendLine("are not reachable on any element in this model, so every tag would be refused.");
                sb.AppendLine();
                sb.AppendLine("Fix: with THIS model active, run Load Shared Parameters, then Batch Tag again.");
                sb.AppendLine("Bindings belong to each model — running it on another open model does not count.");
                sb.AppendLine();
                sb.Append("If they are loaded, check Manage > Project Parameters: each must be an INSTANCE ");
                sb.Append("parameter and include the categories being tagged.");
                result.RefusalText = sb.ToString();
            }
            else
            {
                result.Summary = $"Token parameters missing on {unbound.Count} categor{(unbound.Count == 1 ? "y" : "ies")} "
                               + $"({result.UnboundElementCount:N0} element(s) will be refused): {named}";
            }
            return result;
        }

        private static string NameCategories(IReadOnlyList<TokenBindingProbe> unbound)
        {
            var shown = unbound.Take(MaxNamedCategories).Select(p => p.Category);
            string s = string.Join(", ", shown);
            int more = unbound.Count - MaxNamedCategories;
            return more > 0 ? s + $" +{more} more" : s;
        }
    }
}
