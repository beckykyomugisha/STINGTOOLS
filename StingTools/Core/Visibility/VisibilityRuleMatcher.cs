// StingTools — Visibility Center · the pure matching core
//
// Revit-free. This is "the rule-matching half of the engine" — VisibilityEngine.Plan
// harvests snapshots from Revit and delegates the actual decision-making here, which is
// what makes the planning path unit-testable without a Revit host.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Visibility
{
    /// <summary>
    /// Evaluates a <see cref="VisibilitySet"/> against element snapshots and names the
    /// view filters a plan needs. No Revit types, no I/O, no writes.
    /// </summary>
    public static class VisibilityRuleMatcher
    {
        /// <summary>
        /// Every filter this feature creates carries this prefix. <b>It is the contract</b>
        /// that lets <c>Vis_PurgeFilters</c> and <c>Vis_ResetAll</c> find and delete STING's
        /// filters deterministically without touching a user's own filters.
        /// </summary>
        public const string FilterPrefix = "STING VIS - ";

        private const string CategoryInfix = "Cat ";

        // ── Validation ──────────────────────────────────────────────────

        /// <summary>
        /// Returns null when the set is coherent, or a user-facing reason when it is not.
        /// Mixing Hide and ShowOnly is rejected explicitly rather than silently resolved,
        /// because either resolution would surprise half the users who hit it.
        /// </summary>
        public static string Validate(VisibilitySet set)
        {
            if (set == null) return "No visibility set supplied.";
            if (set.Rules == null || set.Rules.Count == 0) return null; // empty is valid, just a no-op

            bool hasHide = false, hasShowOnly = false;
            foreach (var r in set.Rules)
            {
                if (r == null) continue;
                if (r.Action == VisibilityAction.ShowOnly) hasShowOnly = true;
                else hasHide = true;
            }

            if (hasHide && hasShowOnly)
                return "This set mixes Hide and Show-only rules, which have opposite meanings. " +
                       "Split them into two sets, or pick one action for the whole set.";

            foreach (var r in set.Rules)
            {
                if (r == null) continue;
                if (r.Kind == VisibilityRuleKind.Token && !VisibilityTokens.IsKnown(r.TokenKey))
                    return $"Unknown tag token '{r.TokenKey}'. Expected one of: {string.Join(", ", VisibilityTokens.All)}.";
            }
            return null;
        }

        /// <summary>True when any rule in the set asks for isolate semantics.</summary>
        public static bool IsIsolate(VisibilitySet set) =>
            set?.Rules != null && set.Rules.Any(r => r != null && r.Action == VisibilityAction.ShowOnly);

        // ── Matching ────────────────────────────────────────────────────

        /// <summary>
        /// Does this element satisfy the set? Rules are grouped by
        /// <see cref="VisibilityRule.GroupKey"/>; within a group the rules OR, across
        /// groups they AND. An empty set matches nothing.
        /// </summary>
        public static bool Matches(VisibilityElementSnapshot el, VisibilitySet set)
        {
            if (el == null || set?.Rules == null || set.Rules.Count == 0) return false;

            foreach (var group in GroupRules(set))
            {
                bool groupHit = false;
                foreach (var rule in group.Value)
                {
                    if (MatchesRule(el, rule)) { groupHit = true; break; }
                }
                if (!groupHit) return false;   // AND across groups
            }
            return true;
        }

        /// <summary>Single-rule test: values within the rule OR.</summary>
        public static bool MatchesRule(VisibilityElementSnapshot el, VisibilityRule rule)
        {
            if (el == null || rule == null) return false;

            if (rule.Kind == VisibilityRuleKind.Category)
                return el.CategoryId != 0 && el.CategoryId == rule.CategoryId;

            if (rule.Values == null || rule.Values.Count == 0) return false;

            string actual = el.Token(rule.TokenKey);
            foreach (var wanted in rule.Values)
            {
                // "(unset)" matches null AND empty-string AND whitespace.
                if (VisibilityTokens.IsUnset(wanted))
                {
                    if (VisibilityTokens.IsUnset(actual)) return true;
                    continue;
                }
                if (VisibilityTokens.IsUnset(actual)) continue;
                if (string.Equals(actual.Trim(), wanted.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>Rules bucketed by group key, preserving declaration order.</summary>
        public static Dictionary<string, List<VisibilityRule>> GroupRules(VisibilitySet set)
        {
            var groups = new Dictionary<string, List<VisibilityRule>>(StringComparer.OrdinalIgnoreCase);
            if (set?.Rules == null) return groups;

            foreach (var r in set.Rules)
            {
                if (r == null) continue;
                List<VisibilityRule> bucket;
                if (!groups.TryGetValue(r.GroupKey, out bucket))
                {
                    bucket = new List<VisibilityRule>();
                    groups[r.GroupKey] = bucket;
                }
                bucket.Add(r);
            }
            return groups;
        }

        // ── Planning (pure half) ────────────────────────────────────────

        /// <summary>
        /// Compute matches, per-group counts and the required filter list. Writes nothing.
        /// Revit-specific blockers (view template lock, non-filterable category, unbound
        /// shared parameter) are appended by <c>VisibilityEngine.Plan</c> afterwards.
        /// </summary>
        public static VisibilityPlan PlanCore(
            IEnumerable<VisibilityElementSnapshot> elements,
            VisibilitySet set,
            VisibilityMode mode)
        {
            var plan = new VisibilityPlan { Mode = mode, IsIsolate = IsIsolate(set) };

            string reject = Validate(set);
            if (reject != null) { plan.RejectReason = reject; return plan; }

            var snapshots = elements as IList<VisibilityElementSnapshot>
                            ?? (elements ?? Enumerable.Empty<VisibilityElementSnapshot>()).ToList();
            plan.TotalScanned = snapshots.Count;

            // An empty rule set is a legitimate no-op: zero matches, and NOT a blocker.
            if (set?.Rules == null || set.Rules.Count == 0) return plan;

            var groups = GroupRules(set);
            foreach (var key in groups.Keys) plan.RuleCounts[key] = 0;

            foreach (var el in snapshots)
            {
                bool all = true;
                foreach (var group in groups)
                {
                    bool hit = group.Value.Any(r => MatchesRule(el, r));
                    if (hit) plan.RuleCounts[group.Key] = plan.RuleCounts[group.Key] + 1;
                    else all = false;
                }
                if (all) plan.MatchedIds.Add(el.Id);
            }

            if (mode == VisibilityMode.ViewFilter)
                plan.Filters = BuildFilters(set, snapshots);

            return plan;
        }

        /// <summary>One filter per distinct (kind, token, value) — deterministic and de-duplicated.</summary>
        private static List<PlannedFilter> BuildFilters(
            VisibilitySet set, IList<VisibilityElementSnapshot> snapshots)
        {
            var byName = new Dictionary<string, PlannedFilter>(StringComparer.OrdinalIgnoreCase);

            foreach (var rule in set.Rules)
            {
                if (rule == null) continue;

                if (rule.Kind == VisibilityRuleKind.Category)
                {
                    string catName = rule.CategoryName ?? rule.CategoryId.ToString();
                    string name = FilterName(VisibilityRuleKind.Category, null, catName);
                    PlannedFilter pf;
                    if (!byName.TryGetValue(name, out pf))
                    {
                        pf = new PlannedFilter
                        {
                            Name = name,
                            Kind = VisibilityRuleKind.Category,
                            Value = catName
                        };
                        byName[name] = pf;
                    }
                    if (!pf.CategoryIds.Contains(rule.CategoryId)) pf.CategoryIds.Add(rule.CategoryId);
                    pf.MatchCount += snapshots.Count(s => MatchesRule(s, rule));
                    continue;
                }

                foreach (var value in rule.Values ?? new List<string>())
                {
                    string name = FilterName(VisibilityRuleKind.Token, rule.TokenKey, value);
                    PlannedFilter pf;
                    if (!byName.TryGetValue(name, out pf))
                    {
                        pf = new PlannedFilter
                        {
                            Name = name,
                            Kind = VisibilityRuleKind.Token,
                            TokenKey = rule.TokenKey,
                            Value = value
                        };
                        byName[name] = pf;
                    }
                    var single = new VisibilityRule
                    {
                        Kind = VisibilityRuleKind.Token,
                        TokenKey = rule.TokenKey,
                        Values = new List<string> { value }
                    };
                    pf.MatchCount += snapshots.Count(s => MatchesRule(s, single));
                }
            }
            return byName.Values.ToList();
        }

        // ── Filter naming (round-trippable) ─────────────────────────────

        /// <summary>
        /// Deterministic filter name: <c>"STING VIS - ZONE=Z02"</c> or
        /// <c>"STING VIS - Cat Ducts"</c>. Round-trips through <see cref="TryParseFilterName"/>.
        /// </summary>
        public static string FilterName(VisibilityRuleKind kind, string tokenKey, string value)
        {
            if (kind == VisibilityRuleKind.Category)
                return FilterPrefix + CategoryInfix + (value ?? string.Empty);

            string v = string.IsNullOrWhiteSpace(value) ? VisibilityTokens.Unset : value;
            return FilterPrefix + (tokenKey ?? string.Empty) + "=" + v;
        }

        /// <summary>True when this name belongs to the Visibility Center.</summary>
        public static bool IsStingVisibilityFilter(string name) =>
            !string.IsNullOrEmpty(name) &&
            name.StartsWith(FilterPrefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Inverse of <see cref="FilterName"/>. Splits on the <b>first</b> '=' so a token key
        /// is always recovered cleanly.
        /// </summary>
        public static bool TryParseFilterName(
            string name, out VisibilityRuleKind kind, out string tokenKey, out string value)
        {
            kind = VisibilityRuleKind.Category;
            tokenKey = null;
            value = null;
            if (!IsStingVisibilityFilter(name)) return false;

            string body = name.Substring(FilterPrefix.Length);

            if (body.StartsWith(CategoryInfix, StringComparison.OrdinalIgnoreCase))
            {
                kind = VisibilityRuleKind.Category;
                value = body.Substring(CategoryInfix.Length);
                return true;
            }

            int eq = body.IndexOf('=');
            if (eq <= 0) return false;

            kind = VisibilityRuleKind.Token;
            tokenKey = body.Substring(0, eq);
            value = body.Substring(eq + 1);
            return true;
        }
    }
}
