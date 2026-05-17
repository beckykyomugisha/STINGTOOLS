// ClashRuleEngine.cs — applies tier-1 rules to each hit and produces a classified result.
using System.Collections.Generic;

namespace StingTools.Core.Clash
{
    public sealed class ClassifiedClash
    {
        public ClashHit Hit;
        public ElementFacts FactsA;
        public ElementFacts FactsB;
        public ClashCell MatrixCell;
        public ClashVerdict Verdict;
        public string VerdictRuleId;
    }

    public sealed class ClashRuleEngine
    {
        private readonly List<ClashRuleDefinition> _rules;
        public ClashRuleEngine(List<ClashRuleDefinition> rules = null)
        {
            _rules = rules ?? ClashRule.BuiltIns();
        }

        public ClassifiedClash Classify(ClashHit h, ElementFacts a, ElementFacts b, ClashCell matrix)
        {
            var result = new ClassifiedClash
            {
                Hit = h, FactsA = a, FactsB = b, MatrixCell = matrix, Verdict = ClashVerdict.Keep
            };
            // E4: Collect every matching rule's verdict and return the
            //     strictest. Strictness ordering: Pseudo > Intentional > Keep.
            //     Prior code returned on first non-Keep verdict, which made
            //     rule order load-bearing — R001 (tessellation, Pseudo) HAD
            //     to be listed before R008 (joint, Intentional) or large
            //     slivers escaped as Intentional rather than dropping. With
            //     strictest-wins semantics, ordering becomes pure config.
            ClashRuleDefinition pseudoRule = null, intentionalRule = null;
            foreach (var rule in _rules)
            {
                bool fa = string.IsNullOrEmpty(rule.FilterA) || FilterMatches(rule.FilterA, a) || FilterMatches(rule.FilterA, b);
                bool fb = string.IsNullOrEmpty(rule.FilterB) || FilterMatches(rule.FilterB, b) || FilterMatches(rule.FilterB, a);
                if (!(fa && fb)) continue;
                // C5: Predicate now takes the rule definition so it can read
                //     project-overridable thresholds from rule.Params with the
                //     hardcoded constant as fallback.
                var v = rule.Predicate(h, a, b, rule);
                if (v == ClashVerdict.Pseudo && pseudoRule == null) pseudoRule = rule;
                else if (v == ClashVerdict.Intentional && intentionalRule == null) intentionalRule = rule;
            }
            if (pseudoRule != null)
            {
                result.Verdict = ClashVerdict.Pseudo;
                result.VerdictRuleId = pseudoRule.Id;
            }
            else if (intentionalRule != null)
            {
                result.Verdict = ClashVerdict.Intentional;
                result.VerdictRuleId = intentionalRule.Id;
            }
            return result;
        }

        private static bool FilterMatches(string filter, ElementFacts f)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            var kv = filter.Split(new[] { '=' }, 2);
            if (kv.Length != 2) return false;
            string actual = f.Get(kv[0].Trim());
            string expected = kv[1].Trim();
            if (expected.EndsWith("*"))
                return actual.StartsWith(expected.Substring(0, expected.Length - 1), System.StringComparison.OrdinalIgnoreCase);
            return string.Equals(actual, expected, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
