using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core
{
    /// <summary>
    /// ENH-003: Lightweight cached compliance scan for live dashboard display.
    /// Provides quick tag completeness stats without full ISO validation overhead.
    /// Thread-safe cached results with configurable stale duration.
    /// </summary>
    public static class ComplianceScan
    {
        // CRASH FIX: Lock protects cache from concurrent read/write races
        // between ComplianceScan.Scan() and InvalidateCache() calls
        private static readonly object _cacheLock = new object();
        private static ComplianceResult _cached;
        private static DateTime _cacheTime = DateTime.MinValue;
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Quick compliance scan results.
        /// </summary>
        public class ComplianceResult
        {
            public int TotalElements { get; set; }
            public int TaggedComplete { get; set; }
            public int TaggedIncomplete { get; set; }
            public int Untagged { get; set; }
            public int FullyResolved { get; set; }
            /// <summary>GAP-006 fix: Elements with REV parameter populated.</summary>
            public int RevisionComplete { get; set; }
            /// <summary>GAP-006 fix: Elements with empty REV parameter.</summary>
            public int RevisionMissing { get; set; }
            /// <summary>GAP-006 fix: Distribution of REV values across elements.</summary>
            public Dictionary<string, int> RevisionDistribution { get; set; } = new Dictionary<string, int>();
            public Dictionary<string, int> IssuesByType { get; set; } = new Dictionary<string, int>();
            public DateTime ScanTime { get; set; }

            public double CompliancePercent =>
                TotalElements > 0 ? TaggedComplete * 100.0 / TotalElements : 0;

            public double StrictPercent =>
                TotalElements > 0 ? FullyResolved * 100.0 / TotalElements : 0;

            /// <summary>GAP-006 fix: Percentage of elements with REV populated.</summary>
            public double RevisionPercent =>
                TotalElements > 0 ? RevisionComplete * 100.0 / TotalElements : 0;

            /// <summary>RAG status: factors in both tagging AND revision completeness.</summary>
            public string RAGStatus
            {
                get
                {
                    double tagPct = CompliancePercent;
                    double revPct = RevisionPercent;
                    // Weighted: 70% tag compliance + 30% revision compliance
                    double combined = (tagPct * 0.7) + (revPct * 0.3);
                    if (combined >= 80) return "GREEN";
                    if (combined >= 50) return "AMBER";
                    return "RED";
                }
            }

            /// <summary>Short summary for status bar display — includes revision status.</summary>
            public string StatusBarText =>
                $"{RAGStatus} {CompliancePercent:F0}% tagged | {RevisionPercent:F0}% REV | {Untagged} untagged";

            /// <summary>Per-discipline compliance breakdown.</summary>
            public Dictionary<string, DiscComplianceData> ByDisc { get; set; } = new Dictionary<string, DiscComplianceData>();

            /// <summary>Top 5 issues for dashboard display.</summary>
            public string TopIssues
            {
                get
                {
                    if (IssuesByType.Count == 0) return "No issues";
                    return string.Join(", ", IssuesByType
                        .OrderByDescending(x => x.Value)
                        .Take(5)
                        .Select(x => $"{x.Key}:{x.Value}"));
                }
            }
        }

        /// <summary>
        /// Run a quick compliance scan (uses cache if recent).
        /// </summary>
        public static ComplianceResult Scan(Document doc, bool forceRefresh = false)
        {
            lock (_cacheLock)
            {
                if (!forceRefresh && _cached != null &&
                    (DateTime.Now - _cacheTime) < CacheLifetime)
                    return _cached;
            }

            var result = new ComplianceResult { ScanTime = DateTime.Now };
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            try
            {
                // Materialize to List to avoid "object not valid" during iteration
                // Performance: use ElementMulticategoryFilter to skip non-taggable elements
                var scanColl = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType();
                var scanCatEnums = SharedParamGuids.AllCategoryEnums;
                if (scanCatEnums != null && scanCatEnums.Length > 0)
                    scanColl.WherePasses(new ElementMulticategoryFilter(
                        new List<BuiltInCategory>(scanCatEnums)));
                var allElements = scanColl.ToList();
                foreach (Element elem in allElements)
                {
                    if (elem == null || !elem.IsValidObject) continue;
                    string cat = ParameterHelpers.GetCategoryName(elem);
                    if (!known.Contains(cat)) continue;

                    result.TotalElements++;
                    string disc = TagConfig.DiscMap.ContainsKey(cat) ? TagConfig.DiscMap[cat] : "X";
                    if (!result.ByDisc.TryGetValue(disc, out var dd))
                    {
                        dd = new DiscComplianceData();
                        result.ByDisc[disc] = dd;
                    }
                    dd.Total++;
                    string tag = ParameterHelpers.GetString(elem, ParamRegistry.TAG1);

                    if (string.IsNullOrEmpty(tag))
                    {
                        result.Untagged++;
                        dd.Untagged++;
                        AddIssue(result, "Untagged");
                    }
                    else if (TagConfig.TagIsFullyResolved(tag))
                    {
                        result.TaggedComplete++;
                        result.FullyResolved++;
                        dd.Tagged++;
                    }
                    else if (TagConfig.TagIsComplete(tag))
                    {
                        result.TaggedComplete++;
                        dd.Tagged++;
                        // Has placeholders — check which tokens are default/placeholder
                        string[] parts = tag.Split(new[] { ParamRegistry.Separator }, StringSplitOptions.None);
                        if (parts.Length >= 8)
                        {
                            if (parts[1] == "XX") { AddIssue(result, "Missing LOC"); dd.MissingLoc++; }
                            if (parts[2] == "XX" || parts[2] == "ZZ") AddIssue(result, "Missing ZONE");
                            if (parts[3] == "XX") AddIssue(result, "Missing LVL");
                            if (parts[4] == "GEN") { AddIssue(result, "Generic SYS"); dd.MissingSys++; }
                            if (parts[5] == "GEN") AddIssue(result, "Generic FUNC");
                            if (parts[6] == "GEN") { AddIssue(result, "Generic PROD"); dd.MissingProd++; }
                            if (parts[7] == "0000") AddIssue(result, "SEQ=0000");
                        }
                    }
                    else
                    {
                        result.TaggedIncomplete++;
                        AddIssue(result, "Incomplete tag");
                    }

                    // GAP-006 fix: Track revision completeness
                    string rev = ParameterHelpers.GetString(elem, ParamRegistry.REV);
                    if (!string.IsNullOrEmpty(rev))
                    {
                        result.RevisionComplete++;
                        if (!result.RevisionDistribution.ContainsKey(rev))
                            result.RevisionDistribution[rev] = 0;
                        result.RevisionDistribution[rev]++;
                    }
                    else
                    {
                        result.RevisionMissing++;
                        AddIssue(result, "Missing REV");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Compliance scan failed: {ex.Message}");
            }

            lock (_cacheLock)
            {
                _cached = result;
                _cacheTime = DateTime.Now;
            }
            return result;
        }

        /// <summary>Invalidate cached results (call after tagging operations).</summary>
        public static void InvalidateCache()
        {
            lock (_cacheLock)
            {
                _cached = null;
                _cacheTime = DateTime.MinValue;
            }
        }

        private static void AddIssue(ComplianceResult result, string issueType)
        {
            if (!result.IssuesByType.ContainsKey(issueType))
                result.IssuesByType[issueType] = 0;
            result.IssuesByType[issueType]++;
        }
    }

    /// <summary>Per-discipline compliance data.</summary>
    public class DiscComplianceData
    {
        public int Total { get; set; }
        public int Tagged { get; set; }
        public int Untagged { get; set; }
        public int MissingLoc { get; set; }
        public int MissingSys { get; set; }
        public int MissingProd { get; set; }
        public double CompliancePct => Total > 0 ? Tagged * 100.0 / Total : 0;
    }
}
