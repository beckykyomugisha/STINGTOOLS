using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    /// <summary>
    /// DSCH-27 — parse SEQ_RANGE_ALLOCATION from project_config.json.
    ///
    /// The key is the element's DISC code (TAGGING_GUIDE §8: "M": [1, 9999],
    /// "E": [10000, 19999] …): federated discipline models number into disjoint
    /// ranges so merged asset tags cannot collide. A value is either [min, max] or
    /// { "min": n, "max": n }. Until this parser existed TagConfig.SeqRangeAllocation
    /// was declared and read but never loaded, so the allocation never applied.
    ///
    /// Revit-free. Malformed entries are skipped and reported in <c>problems</c>,
    /// never guessed.
    /// </summary>
    public static class SeqRangeAllocationParser
    {
        public static Dictionary<string, (int Min, int Max)> Parse(JToken token, List<string> problems)
        {
            var result = new Dictionary<string, (int Min, int Max)>(StringComparer.OrdinalIgnoreCase);
            if (token == null || token.Type == JTokenType.Null) return result;
            if (token is not JObject obj)
            {
                problems?.Add("SEQ_RANGE_ALLOCATION must be an object keyed by DISC code.");
                return result;
            }
            foreach (var prop in obj.Properties())
            {
                if (prop.Name.StartsWith("_", StringComparison.Ordinal)) continue;
                int? min = null, max = null;
                if (prop.Value is JArray arr && arr.Count == 2)
                {
                    min = AsInt(arr[0]); max = AsInt(arr[1]);
                }
                else if (prop.Value is JObject range)
                {
                    min = AsInt(range["min"]); max = AsInt(range["max"]);
                }
                if (min == null || max == null)
                {
                    problems?.Add($"SEQ_RANGE_ALLOCATION.{prop.Name}: expected [min, max] or {{\"min\": n, \"max\": n}}; ignored.");
                    continue;
                }
                if (min.Value < 1 || max.Value < min.Value)
                {
                    problems?.Add($"SEQ_RANGE_ALLOCATION.{prop.Name}: range {min}-{max} is not 1 <= min <= max; ignored.");
                    continue;
                }
                result[prop.Name] = (min.Value, max.Value);
            }
            return result;
        }

        private static int? AsInt(JToken t)
            => t != null && (t.Type == JTokenType.Integer) ? (int?)t.Value<int>() : null;
    }
}
