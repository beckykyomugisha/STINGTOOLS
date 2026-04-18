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
            foreach (var rule in _rules)
            {
                bool fa = string.IsNullOrEmpty(rule.FilterA) || FilterMatches(rule.FilterA, a) || FilterMatches(rule.FilterA, b);
                bool fb = string.IsNullOrEmpty(rule.FilterB) || FilterMatches(rule.FilterB, b) || FilterMatches(rule.FilterB, a);
                if (!(fa && fb)) continue;
                var v = rule.Predicate(h, a, b);
                if (v != ClashVerdict.Keep)
                {
                    result.Verdict = v;
                    result.VerdictRuleId = rule.Id;
                    return result;
                }
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
