// StingTools — Drawing Template Manager
//
// DrawingRoutingMatcher — the Revit-free half of DrawingDispatcher: how one
// routing rule field is matched, and a static audit of a routing table.
//
// Compiled into StingTools.Tags.Tests, which runs Audit over the shipped
// STING_DRAWING_TYPES.json and fails on a shadowed rule, a dangling
// drawingTypeId, an unreachable drawing type, or a regex that does not
// compile. Before this the only signal for any of those was a routing call
// that quietly resolved to a different profile (or to null) at runtime.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace StingTools.Core.Drawing
{
    public static class DrawingRoutingMatcher
    {
        // Case-insensitive, like the literal fields. A literal rule
        // `docType: "spool"` has always matched "SPOOL"; the regex form of the
        // same rule, `docTypeMatches: "^spool$"`, did not — so rewriting a
        // literal rule as a regex (to add an alternation, say) silently
        // changed which calls it caught. Every other comparison the
        // dispatcher makes is case-insensitive; the regex predicates now are
        // too. Every shipped pattern is upper-case against upper-case codes,
        // so no shipped rule changes behaviour.
        internal const RegexOptions PredicateOptions =
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

        /// <summary>Regex predicate beats the exact field when both are set;
        /// an unset predicate falls through to the wildcard match.</summary>
        public static bool MatchesField(string exact, string regexPattern, string actual)
        {
            if (!string.IsNullOrEmpty(regexPattern)) return RegexMatches(regexPattern, actual);
            return MatchesWildcard(exact, actual);
        }

        /// <summary>Unanchored, case-insensitive. An empty input never
        /// matches; an invalid pattern never matches (Audit reports it).</summary>
        public static bool RegexMatches(string pattern, string actual)
        {
            if (string.IsNullOrEmpty(actual)) return false;
            try { return Regex.IsMatch(actual, pattern, PredicateOptions, MatchTimeout); }
            catch (ArgumentException) { return false; }
            catch (RegexMatchTimeoutException) { return false; }
        }

        /// <summary>Null, empty or "*" matches anything (including no value);
        /// otherwise an ordinal case-insensitive equality.</summary>
        public static bool MatchesWildcard(string ruleValue, string actual)
        {
            if (IsWildcard(ruleValue)) return true;
            if (string.IsNullOrEmpty(actual)) return false;
            return string.Equals(ruleValue, actual, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWildcard(string v) => string.IsNullOrEmpty(v) || v == "*";

        // ─────────────────────────────────────────────────────────────────
        //  Static audit
        // ─────────────────────────────────────────────────────────────────

        public sealed class RoutingAudit
        {
            /// <summary>"#index → id" for rules whose drawingTypeId names no type.</summary>
            public List<string> Dangling { get; } = new List<string>();
            /// <summary>"#later shadowed by #earlier" — the later rule can never fire.</summary>
            public List<string> Shadowed { get; } = new List<string>();
            /// <summary>Drawing type ids that no live (non-shadowed, non-dangling) rule targets.</summary>
            public List<string> Unreachable { get; } = new List<string>();
            /// <summary>"#index field: pattern — error" for patterns that do not compile.</summary>
            public List<string> InvalidRegex { get; } = new List<string>();

            public bool IsClean =>
                Dangling.Count == 0 && Shadowed.Count == 0 && Unreachable.Count == 0 && InvalidRegex.Count == 0;
        }

        /// <summary>
        /// Audit a routing table against its drawing types.
        /// <para><b>Shadowing is conservative</b>: rule B is reported only when
        /// an earlier rule A provably matches every input B matches (field by
        /// field — see <see cref="Covers"/>) and A resolves to a real type.
        /// A dangling A does not shadow: since E-12 the dispatcher walks past
        /// a rule whose target is missing. Regex-vs-regex subsumption is only
        /// recognised for identical patterns and the match-everything
        /// patterns, so a clever overlap can escape — it cannot be a false
        /// alarm.</para>
        /// </summary>
        public static RoutingAudit Audit(IList<DrawingRoutingRule> routing, IEnumerable<DrawingType> types)
        {
            var audit = new RoutingAudit();
            var ids = new HashSet<string>(
                (types ?? Enumerable.Empty<DrawingType>()).Where(t => t != null && !string.IsNullOrEmpty(t.Id)).Select(t => t.Id),
                StringComparer.OrdinalIgnoreCase);
            routing = routing ?? new List<DrawingRoutingRule>();

            var live = new bool[routing.Count];
            for (int j = 0; j < routing.Count; j++)
            {
                var b = routing[j];
                if (b == null) continue;

                foreach (var (name, pattern) in Patterns(b))
                {
                    if (string.IsNullOrEmpty(pattern)) continue;
                    try { _ = new Regex(pattern, PredicateOptions, MatchTimeout); }
                    catch (ArgumentException ex) { audit.InvalidRegex.Add($"#{j} {name}: '{pattern}' — {ex.Message}"); }
                }

                bool dangling = string.IsNullOrEmpty(b.DrawingTypeId) || !ids.Contains(b.DrawingTypeId);
                if (dangling) audit.Dangling.Add($"#{j} -> '{b.DrawingTypeId ?? "(null)"}'");

                int shadowedBy = -1;
                for (int i = 0; i < j && shadowedBy < 0; i++)
                {
                    var a = routing[i];
                    if (a == null) continue;
                    if (string.IsNullOrEmpty(a.DrawingTypeId) || !ids.Contains(a.DrawingTypeId)) continue;
                    if (Shadows(a, b)) shadowedBy = i;
                }
                if (shadowedBy >= 0)
                    audit.Shadowed.Add($"#{j} ({Describe(b)} -> {b.DrawingTypeId}) is shadowed by #{shadowedBy} " +
                                       $"({Describe(routing[shadowedBy])} -> {routing[shadowedBy].DrawingTypeId})");

                live[j] = !dangling && shadowedBy < 0;
            }

            var reached = new HashSet<string>(
                routing.Where((r, k) => r != null && live[k]).Select(r => r.DrawingTypeId),
                StringComparer.OrdinalIgnoreCase);
            foreach (var id in ids.OrderBy(x => x, StringComparer.Ordinal))
                if (!reached.Contains(id)) audit.Unreachable.Add(id);

            return audit;
        }

        /// <summary>True when every input rule <paramref name="b"/> matches is
        /// also matched by <paramref name="a"/>.</summary>
        public static bool Shadows(DrawingRoutingRule a, DrawingRoutingRule b)
        {
            if (a == null || b == null) return false;
            return Covers(a.Discipline, a.DisciplineMatches, b.Discipline, b.DisciplineMatches)
                && Covers(a.Phase,      a.PhaseMatches,      b.Phase,      b.PhaseMatches)
                && Covers(a.DocType,    a.DocTypeMatches,    b.DocType,    b.DocTypeMatches)
                && PredicateCovers(a.LevelMatches,       b.LevelMatches)
                && PredicateCovers(a.ProjectCodeMatches, b.ProjectCodeMatches)
                && PredicateCovers(a.OptionMatches,      b.OptionMatches);
        }

        /// <summary>
        /// Field-level subsumption: does A's (exact, regex) accept every value
        /// B's (exact, regex) accepts?
        /// </summary>
        public static bool Covers(string aExact, string aRegex, string bExact, string bRegex)
        {
            bool aIsRegex = !string.IsNullOrEmpty(aRegex);
            bool bIsRegex = !string.IsNullOrEmpty(bRegex);

            if (!aIsRegex)
            {
                if (IsWildcard(aExact)) return true;                  // A accepts everything
                if (bIsRegex) return false;                           // conservative
                if (IsWildcard(bExact)) return false;                 // B accepts more than A's literal
                return string.Equals(aExact, bExact, StringComparison.OrdinalIgnoreCase);
            }

            // A is a regex: it never accepts an empty value, so it cannot
            // cover a B wildcard (which does).
            if (!bIsRegex)
            {
                if (IsWildcard(bExact)) return false;
                return RegexMatches(aRegex, bExact);                  // B accepts exactly bExact (any case)
            }
            return string.Equals(aRegex, bRegex, StringComparison.Ordinal) || IsMatchEverything(aRegex);
        }

        // An optional predicate (level / project code / option): absent means
        // "no constraint". A covers B when A has no constraint, or the same one.
        private static bool PredicateCovers(string a, string b)
            => string.IsNullOrEmpty(a) || string.Equals(a, b, StringComparison.Ordinal);

        private static bool IsMatchEverything(string pattern)
            => pattern == ".*" || pattern == "^.*$" || pattern == ".+" || pattern == "^.+$" || pattern == ".*?";

        private static IEnumerable<(string, string)> Patterns(DrawingRoutingRule r)
        {
            yield return ("disciplineMatches", r.DisciplineMatches);
            yield return ("phaseMatches", r.PhaseMatches);
            yield return ("docTypeMatches", r.DocTypeMatches);
            yield return ("levelMatches", r.LevelMatches);
            yield return ("projectCodeMatches", r.ProjectCodeMatches);
            yield return ("optionMatches", r.OptionMatches);
        }

        private static string Describe(DrawingRoutingRule r)
        {
            string F(string exact, string rx) => string.IsNullOrEmpty(rx) ? (exact ?? "*") : "/" + rx + "/";
            return $"{F(r.Discipline, r.DisciplineMatches)},{F(r.Phase, r.PhaseMatches)},{F(r.DocType, r.DocTypeMatches)}";
        }
    }
}
