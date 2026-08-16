// StingTools — Visibility Center · plan + result records
//
// Revit-free. Element identity travels as a long (ElementId.Value) so the planning
// half of the engine can be unit-tested without the Revit API.

using System.Collections.Generic;

namespace StingTools.Core.Visibility
{
    /// <summary>
    /// One element reduced to just what the matcher needs. The Revit-bound
    /// <c>TokenValueHarvester</c> produces these in a single collector pass.
    /// </summary>
    public sealed class VisibilityElementSnapshot
    {
        /// <summary><c>ElementId.Value</c>.</summary>
        public long Id { get; set; }

        /// <summary>BuiltInCategory as an int, or 0 when the element has no category.</summary>
        public int CategoryId { get; set; }

        public string CategoryName { get; set; }

        /// <summary>Token key → raw parameter value. Missing/blank entries mean "unset".</summary>
        public Dictionary<string, string> Tokens { get; set; }
            = new Dictionary<string, string>();

        public string Token(string key)
        {
            if (Tokens == null || key == null) return null;
            string v;
            return Tokens.TryGetValue(key, out v) ? v : null;
        }
    }

    /// <summary>
    /// A view filter the plan needs in order to be applied. Purely descriptive —
    /// no <c>ParameterFilterElement</c> is created until <c>VisibilityEngine.Apply</c>.
    /// </summary>
    public sealed class PlannedFilter
    {
        /// <summary>Deterministic, prefixed with <see cref="VisibilityRuleMatcher.FilterPrefix"/>.</summary>
        public string Name { get; set; }

        public VisibilityRuleKind Kind { get; set; }

        /// <summary>Token key for token filters; null for category filters.</summary>
        public string TokenKey { get; set; }

        /// <summary>Token value for token filters; category name for category filters.</summary>
        public string Value { get; set; }

        /// <summary>BuiltInCategory ints the filter binds to.</summary>
        public List<int> CategoryIds { get; set; } = new List<int>();

        /// <summary>How many scanned elements this filter accounts for — drives the footer.</summary>
        public int MatchCount { get; set; }
    }

    /// <summary>
    /// The output of <c>VisibilityEngine.Plan</c> — computed without writing anything,
    /// so the dropdown can render "will hide 1,204 of 8,331 elements" before the user commits.
    /// </summary>
    public sealed class VisibilityPlan
    {
        /// <summary>Elements the rule set matched.</summary>
        public List<long> MatchedIds { get; set; } = new List<long>();

        /// <summary>Total elements considered, so the UI can show "N of M".</summary>
        public int TotalScanned { get; set; }

        /// <summary>Filters required by ViewFilter mode. Empty in Temporary mode.</summary>
        public List<PlannedFilter> Filters { get; set; } = new List<PlannedFilter>();

        /// <summary>Per-group match counts, keyed by <see cref="VisibilityRule.GroupKey"/>.</summary>
        public Dictionary<string, int> RuleCounts { get; set; }
            = new Dictionary<string, int>();

        /// <summary>
        /// Conditions that stop some or all of this plan from applying, phrased for the user
        /// ("ZONE is not bound to Ducts; 3 categories skipped"). Blockers are <b>reported</b>,
        /// never thrown and never silently swallowed.
        /// </summary>
        public List<string> Blockers { get; set; } = new List<string>();

        /// <summary>True when the set uses ShowOnly semantics (isolate) rather than Hide.</summary>
        public bool IsIsolate { get; set; }

        public VisibilityMode Mode { get; set; } = VisibilityMode.Temporary;

        /// <summary>The set this plan was computed from — Apply needs it to build the
        /// inverted filter for show-only. Not serialised; a plan is transient.</summary>
        public VisibilitySet Set { get; set; }

        /// <summary>Distinct categories present in the scanned scope, for filter binding.</summary>
        public List<int> ScopeCategoryIds { get; set; } = new List<int>();

        /// <summary>Set when the rule set itself is invalid (e.g. mixed Hide + ShowOnly).
        /// A rejected plan must not be applied.</summary>
        public string RejectReason { get; set; }

        public bool IsRejected => !string.IsNullOrEmpty(RejectReason);

        public int MatchCount => MatchedIds == null ? 0 : MatchedIds.Count;

        /// <summary>Applyable when not rejected and there is something to do.</summary>
        public bool CanApply => !IsRejected && MatchCount > 0;

        /// <summary>The dropdown footer line.</summary>
        public string Summary()
        {
            if (IsRejected) return RejectReason;
            string verb = IsIsolate ? "Will isolate" : "Will hide";
            string line = $"{verb} {MatchCount:N0} of {TotalScanned:N0} elements";
            if (Mode == VisibilityMode.ViewFilter && Filters != null && Filters.Count > 0)
                line += $" · {Filters.Count} filter{(Filters.Count == 1 ? "" : "s")}";
            if (Blockers != null && Blockers.Count > 0)
                line += $" · {Blockers.Count} blocker{(Blockers.Count == 1 ? "" : "s")}";
            return line;
        }
    }

    /// <summary>What actually happened when a plan was applied.</summary>
    public sealed class VisibilityResult
    {
        public bool Ok { get; set; }
        public int ElementsAffected { get; set; }
        public int FiltersCreated { get; set; }
        public int FiltersReused { get; set; }
        public int ViewsAffected { get; set; }

        /// <summary>Non-fatal conditions the user needs to know about.</summary>
        public List<string> Blockers { get; set; } = new List<string>();

        /// <summary>Set when the apply failed outright.</summary>
        public string Error { get; set; }

        public string Summary()
        {
            if (!string.IsNullOrEmpty(Error)) return Error;
            var parts = new List<string> { $"{ElementsAffected:N0} elements" };
            if (FiltersCreated > 0) parts.Add($"{FiltersCreated} filter(s) created");
            if (FiltersReused > 0) parts.Add($"{FiltersReused} reused");
            if (ViewsAffected > 1) parts.Add($"{ViewsAffected} views");
            return string.Join(" · ", parts);
        }
    }
}
