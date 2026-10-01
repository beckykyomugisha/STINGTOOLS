// StingTools — Template Manager · MR_SCHEDULES.csv VIEW_FILTER rule text
//
// DTW-168: CreateFiltersCommand's CSV phase looked for "Rule=…, Param=…,
// Value=…" in column 7, a grammar no VIEW_FILTER row uses. The rows keep their
// rule as prose in column 8 — "System_Type Contains Chilled Water Supply",
// "Fire_Rating HasValue", "ASS_TAG_1 HasNoValue",
// "Rule1: Pipes | System Classification equals Sanitary" — so every row fell
// through to a CATEGORY-ONLY filter that matched everything in its categories:
// "Chilled Water Supply" coloured every pipe.
//
// This parses column 8 (Revit-free, so ViewFilterRuleTextTests can hold every
// shipped row to it). A row is either explicitly category-only
// ("Type=Visibility (no parameter rules)"), a rule, or unparseable — and an
// unparseable row is refused by the command, never widened.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace StingTools.Core.Drawing
{
    internal enum ViewFilterRuleKind { CategoryOnly, Rule, Unparseable }

    internal sealed class ViewFilterRuleSpec
    {
        public ViewFilterRuleKind Kind { get; set; }
        /// <summary>The parameter as written in the CSV ("System_Type").</summary>
        public string ParamText { get; set; }
        /// <summary>Resolved parameter name: a BuiltInParameter member or a shared-parameter name.</summary>
        public string Param { get; set; }
        /// <summary>True when <see cref="Param"/> is a BuiltInParameter member name.</summary>
        public bool IsBuiltIn { get; set; }
        /// <summary>Canonical operator: Equals / NotEquals / Contains / NotContains / BeginsWith / HasValue / HasNoValue.</summary>
        public string Op { get; set; }
        public string Value { get; set; }
        public string Reason { get; set; }
    }

    internal static class ViewFilterRuleText
    {
        private static readonly string[] Ops =
            { "HasNoValue", "HasValue", "NotContains", "NotEquals", "BeginsWith", "Contains", "Equals" };

        /// <summary>
        /// CSV parameter wording → the parameter it means. BuiltInParameter names are
        /// marked builtin; the rest are STING shared parameters. A name not listed is
        /// used as written (it may be a project parameter); the command refuses the
        /// row if it does not resolve.
        /// </summary>
        internal static readonly IReadOnlyDictionary<string, (string Param, bool BuiltIn)> ParamAliases =
            new Dictionary<string, (string, bool)>(StringComparer.OrdinalIgnoreCase)
        {
            ["System_Type"]           = ("RBS_SYSTEM_NAME_PARAM", true),
            ["System Type"]           = ("RBS_SYSTEM_NAME_PARAM", true),
            ["System Name"]           = ("RBS_SYSTEM_NAME_PARAM", true),
            ["System Classification"] = ("RBS_SYSTEM_CLASSIFICATION_PARAM", true),
            ["Fire_Rating"]           = ("FIRE_RATING", true),
            ["Fire Rating"]           = ("FIRE_RATING", true),
            ["Acoustic_Rating"]       = ("PER_ACOUSTIC_RATING_TXT", false),
            ["ASS_TAG_1"]             = ("ASS_TAG_1_TXT", false),
        };

        internal static ViewFilterRuleSpec Parse(string text)
        {
            var t = (text ?? "").Trim();
            if (t.Length == 0)
                return new ViewFilterRuleSpec { Kind = ViewFilterRuleKind.Unparseable, Reason = "empty rule column" };
            if (t.IndexOf("no parameter rules", StringComparison.OrdinalIgnoreCase) >= 0)
                return new ViewFilterRuleSpec { Kind = ViewFilterRuleKind.CategoryOnly };

            // "Rule1: Pipes | System Classification equals Sanitary" — drop the
            // label and the category scope (the row's categories already say it).
            var m = Regex.Match(t, @"^Rule\d*\s*:\s*(?:[^|]*\|)?\s*(.+)$", RegexOptions.IgnoreCase);
            if (m.Success) t = m.Groups[1].Value.Trim();

            foreach (var op in Ops)
            {
                var om = Regex.Match(t, @"^(?<p>.+?)\s+" + op + @"(?:\s+(?<v>.*))?$", RegexOptions.IgnoreCase);
                if (!om.Success) continue;
                var paramText = om.Groups["p"].Value.Trim();
                var value = om.Groups["v"].Success ? om.Groups["v"].Value.Trim() : "";
                bool presence = op == "HasValue" || op == "HasNoValue";
                if (paramText.Length == 0)
                    return Bad(text, "no parameter before the operator");
                if (!presence && value.Length == 0)
                    return Bad(text, $"'{op}' needs a value");
                if (presence && value.Length > 0)
                    return Bad(text, $"'{op}' takes no value");

                var spec = new ViewFilterRuleSpec
                {
                    Kind = ViewFilterRuleKind.Rule, ParamText = paramText, Op = op, Value = value,
                    Param = paramText, IsBuiltIn = false,
                };
                if (ParamAliases.TryGetValue(paramText, out var alias))
                { spec.Param = alias.Param; spec.IsBuiltIn = alias.BuiltIn; }
                return spec;
            }
            return Bad(text, "no recognised operator (Equals / NotEquals / Contains / NotContains / BeginsWith / HasValue / HasNoValue)");
        }

        private static ViewFilterRuleSpec Bad(string text, string why)
            => new ViewFilterRuleSpec { Kind = ViewFilterRuleKind.Unparseable, Reason = $"'{text}': {why}" };
    }
}
