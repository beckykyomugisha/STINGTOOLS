// StingTools — Visibility Center · rule data model
//
// Revit-free by design. This file (and VisibilityPlan.cs / VisibilityRuleMatcher.cs /
// VisibilityPresetStore.cs) is <Compile Include>-linked into StingTools.Visibility.Tests,
// so it must never reference Autodesk.Revit.*. Category identity travels as an int
// (the BuiltInCategory value), not as an ElementId.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace StingTools.Core.Visibility
{
    /// <summary>What a rule keys off: a Revit category, or an ISO 19650 tag token.</summary>
    public enum VisibilityRuleKind { Category, Token }

    /// <summary>Hide the matched elements, or show only them (isolate).</summary>
    public enum VisibilityAction { Hide, ShowOnly }

    /// <summary>
    /// Temporary = <c>View.HideElementsTemporary</c>: instant, session-only, does NOT print
    /// and does not survive closing the view. ViewFilter = <c>ParameterFilterElement</c> +
    /// <c>View.SetFilterVisibility</c>: persists in the view, prints, pushable to a template.
    /// </summary>
    public enum VisibilityMode { Temporary, ViewFilter }

    /// <summary>Which view(s) an apply lands on.</summary>
    public enum VisibilityTarget { ActiveView, SelectedViews, AllViewsOnSheet, ViewTemplate }

    /// <summary>
    /// One visibility predicate. A rule is either a category rule (<see cref="CategoryId"/>)
    /// or a token rule (<see cref="TokenKey"/> + <see cref="Values"/>).
    /// </summary>
    /// <remarks>
    /// <para><b>Matching contract</b> — this is the whole semantics, stated once:</para>
    /// <list type="bullet">
    /// <item><description>Values <b>within</b> one rule are OR-ed: <c>ZONE ∈ {Z02, Z03}</c>.</description></item>
    /// <item><description>Rules are grouped by (<see cref="Kind"/>, <see cref="TokenKey"/>).
    /// Rules in the <b>same</b> group are OR-ed — two category rules mean "Ducts OR Pipes",
    /// which is what ticking two boxes means. AND-ing them would match nothing, since an
    /// element has exactly one category.</description></item>
    /// <item><description>Groups are AND-ed with each other:
    /// <c>ZONE ∈ {Z02} AND LOC ∈ {BLD1}</c> matches only elements satisfying both.</description></item>
    /// <item><description>Mixing <see cref="VisibilityAction.Hide"/> and
    /// <see cref="VisibilityAction.ShowOnly"/> in one set is <b>rejected with a message</b>,
    /// never silently resolved — see <see cref="VisibilityRuleMatcher.Validate"/>.</description></item>
    /// </list>
    /// </remarks>
    public sealed class VisibilityRule
    {
        [JsonProperty("kind")]
        [JsonConverter(typeof(StringEnumConverter))]
        public VisibilityRuleKind Kind { get; set; }

        /// <summary>BuiltInCategory as an int. Only meaningful when <see cref="Kind"/> is Category.</summary>
        [JsonProperty("categoryId", NullValueHandling = NullValueHandling.Ignore)]
        public int CategoryId { get; set; }

        /// <summary>Human-readable category name, carried for filter naming and reporting.
        /// Only meaningful when <see cref="Kind"/> is Category.</summary>
        [JsonProperty("categoryName", NullValueHandling = NullValueHandling.Ignore)]
        public string CategoryName { get; set; }

        /// <summary>One of <see cref="VisibilityTokens.All"/> — "DISC"|"LOC"|"ZONE"|"LVL"|"SYS"|"FUNC"|"PROD".
        /// Only meaningful when <see cref="Kind"/> is Token.</summary>
        [JsonProperty("tokenKey", NullValueHandling = NullValueHandling.Ignore)]
        public string TokenKey { get; set; }

        /// <summary>OR-ed values. <see cref="VisibilityTokens.Unset"/> matches null and "".</summary>
        [JsonProperty("values", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Values { get; set; } = new List<string>();

        [JsonProperty("action")]
        [JsonConverter(typeof(StringEnumConverter))]
        public VisibilityAction Action { get; set; } = VisibilityAction.Hide;

        /// <summary>Grouping key: same key = OR-ed together, different keys = AND-ed.</summary>
        [JsonIgnore]
        public string GroupKey =>
            Kind == VisibilityRuleKind.Category ? "CAT" : "TOK:" + (TokenKey ?? string.Empty);
    }

    /// <summary>A named, serialisable bundle of rules — what a preset round-trips to.</summary>
    public sealed class VisibilitySet
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("mode")]
        [JsonConverter(typeof(StringEnumConverter))]
        public VisibilityMode Mode { get; set; } = VisibilityMode.Temporary;

        [JsonProperty("target")]
        [JsonConverter(typeof(StringEnumConverter))]
        public VisibilityTarget Target { get; set; } = VisibilityTarget.ActiveView;

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        /// <summary>"corporate" for baseline entries, "project" for the per-project override file.</summary>
        [JsonProperty("origin", NullValueHandling = NullValueHandling.Ignore)]
        public string Origin { get; set; } = "corporate";

        [JsonProperty("rules")]
        public List<VisibilityRule> Rules { get; set; } = new List<VisibilityRule>();
    }

    /// <summary>Root document of STING_VISIBILITY_PRESETS.json and the project override.</summary>
    public sealed class VisibilityPresetLibrary
    {
        [JsonProperty("version")]
        public int Version { get; set; } = 1;

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        /// <summary>
        /// BuiltInCategory names the category list leaves out — view-management categories
        /// (Cameras, Views, Section Boxes, Scope Boxes) that are not model content.
        /// <para><b>null and empty mean different things and the default must stay null.</b>
        /// null = "this file says nothing", so the corporate baseline (or, failing that,
        /// <see cref="VisibilityCategoryTreeBuilder.DefaultExclusions"/>) applies. An explicit
        /// <c>[]</c> = "this project excludes nothing", and must be honoured. Initialising this
        /// to an empty list would collapse the two and make the key impossible to override —
        /// exactly the Newtonsoft silent-default failure the preset parser guards against.</para>
        /// </summary>
        [JsonProperty("excludedCategories", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> ExcludedCategories { get; set; }

        [JsonProperty("presets")]
        public List<VisibilitySet> Presets { get; set; } = new List<VisibilitySet>();
    }

    /// <summary>
    /// The seven ISO 19650 tag token keys the Visibility Center understands, and the
    /// <c>(unset)</c> sentinel. Token keys are stable identifiers used in filter names and
    /// preset JSON; the <i>parameter</i> each maps to is resolved at runtime through
    /// <c>ParamRegistry</c> (never hardcoded), so a renamed project keeps matching.
    /// </summary>
    public static class VisibilityTokens
    {
        public const string Disc = "DISC";
        public const string Loc  = "LOC";
        public const string Zone = "ZONE";
        public const string Lvl  = "LVL";
        public const string Sys  = "SYS";
        public const string Func = "FUNC";
        public const string Prod = "PROD";

        /// <summary>Synthetic value matching a null OR empty token — "hide everything untagged".</summary>
        public const string Unset = "(unset)";

        /// <summary>Tag-segment order, matching ParamRegistry slots 0..6.</summary>
        public static readonly string[] All = { Disc, Loc, Zone, Lvl, Sys, Func, Prod };

        /// <summary>Long labels for the dropdown's group headers.</summary>
        public static string Label(string tokenKey)
        {
            switch (tokenKey)
            {
                case Disc: return "DISCIPLINE";
                case Loc:  return "LOCATION";
                case Zone: return "ZONE";
                case Lvl:  return "LEVEL";
                case Sys:  return "SYSTEM";
                case Func: return "FUNCTION";
                case Prod: return "PRODUCT";
                default:   return tokenKey ?? string.Empty;
            }
        }

        public static bool IsKnown(string tokenKey)
        {
            if (string.IsNullOrEmpty(tokenKey)) return false;
            foreach (var t in All)
                if (string.Equals(t, tokenKey, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>True when <paramref name="value"/> should be treated as unset
        /// (null, empty, whitespace, or the explicit sentinel).</summary>
        public static bool IsUnset(string value) =>
            string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, Unset, StringComparison.OrdinalIgnoreCase);
    }
}
