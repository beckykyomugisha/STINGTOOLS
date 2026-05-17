// StingTools — ULSystemMatcher.
//
// Phase 178f. Reads the ulSystemMatch[] block on a swap-registry
// candidate and returns the best UL/EN-1366-3 system reference for a
// given penetration instance. Used by SwapToManufacturer to stamp
// PEN_CERTIFICATION_TXT with the vendor's certified system the moment
// the user picks the manufacturer family — turns "swap to product" +
// "pick a UL system" into one click.
//
// Match rules (all optional; missing field = "any"):
//   fireRatingPattern : regex against PEN_FIRE_RATING_TXT
//   hostTypePattern   : regex against PEN_HOST_TYPE_TXT (FLOOR/WALL/BEAM)
//   minOdMm / maxOdMm : numeric range against PEN_OD_MM
// First match wins. The matcher returns null when nothing matches —
// SwapToManufacturer falls back to the seed's PEN_CERTIFICATION_TXT
// default.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Core.Symbols
{
    public static class ULSystemMatcher
    {
        public sealed class MatchResult
        {
            public string UlSystem { get; set; } = "";
            public string Source   { get; set; } = "";   // candidate label
            public string Rule     { get; set; } = "";   // pattern that matched
        }

        /// <summary>
        /// Pick the first UL system whose match rule fits the given
        /// penetration instance. Returns null when the candidate has
        /// no ulSystemMatch[] block or nothing matches.
        /// </summary>
        public static MatchResult Match(JObject candidate, FamilyInstance penetrationInstance)
        {
            if (candidate == null || penetrationInstance == null) return null;
            var rules = candidate["ulSystemMatch"] as JArray;
            if (rules == null || rules.Count == 0) return null;

            string fireRating = ParameterHelpers.GetString(penetrationInstance, "PEN_FIRE_RATING_TXT") ?? "";
            string hostType   = ParameterHelpers.GetString(penetrationInstance, "PEN_HOST_TYPE_TXT")   ?? "";
            string odTxt      = ParameterHelpers.GetString(penetrationInstance, "PEN_OD_MM")           ?? "0";
            double odMm = 0;
            double.TryParse(odTxt, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out odMm);

            foreach (var ruleToken in rules)
            {
                if (!(ruleToken is JObject rule)) continue;
                if (!RegexLikeMatch((string)rule["fireRatingPattern"], fireRating)) continue;
                if (!RegexLikeMatch((string)rule["hostTypePattern"],   hostType))   continue;

                double minMm = (double?)rule["minOdMm"] ?? double.NegativeInfinity;
                double maxMm = (double?)rule["maxOdMm"] ?? double.PositiveInfinity;
                if (odMm < minMm || odMm > maxMm) continue;

                string ul = (string)rule["ulSystem"];
                if (string.IsNullOrEmpty(ul)) continue;
                return new MatchResult
                {
                    UlSystem = ul,
                    Source   = (string)candidate["label"] ?? "",
                    Rule     = $"rating='{(string)rule["fireRatingPattern"]}' host='{(string)rule["hostTypePattern"]}' od=[{minMm}..{maxMm}]",
                };
            }
            return null;
        }

        /// <summary>
        /// List every UL system rule on a candidate (with no instance
        /// filtering) — used to populate a "pick a system" dialog when
        /// multiple rules apply or when the user wants to override the
        /// auto-matched choice.
        /// </summary>
        public static List<string> EnumerateSystems(JObject candidate)
        {
            var list = new List<string>();
            if (candidate == null) return list;
            var rules = candidate["ulSystemMatch"] as JArray;
            if (rules == null) return list;
            foreach (var r in rules)
            {
                string ul = (string)r["ulSystem"];
                if (!string.IsNullOrEmpty(ul) && !list.Contains(ul)) list.Add(ul);
            }
            return list;
        }

        private static bool RegexLikeMatch(string pattern, string value)
        {
            if (string.IsNullOrEmpty(pattern)) return true; // unspecified = any
            try
            {
                return System.Text.RegularExpressions.Regex.IsMatch(value ?? "", pattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }
    }
}
