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
            public Dictionary<string, int> IssuesByType { get; set; } = new Dictionary<string, int>();
            public DateTime ScanTime { get; set; }

            public double CompliancePercent =>
                TotalElements > 0 ? TaggedComplete * 100.0 / TotalElements : 0;

            public double StrictPercent =>
                TotalElements > 0 ? FullyResolved * 100.0 / TotalElements : 0;

            /// <summary>RAG status: Red (&lt;50%), Amber (50-80%), Green (&gt;80%)</summary>
            public string RAGStatus
            {
                get
                {
                    double pct = CompliancePercent;
                    if (pct >= 80) return "GREEN";
                    if (pct >= 50) return "AMBER";
                    return "RED";
                }
            }

            /// <summary>Short summary for status bar display.</summary>
            public string StatusBarText =>
                $"{RAGStatus} {CompliancePercent:F0}% | {TaggedComplete}/{TotalElements} tagged | {Untagged} untagged";

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
                var allElements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .ToList();
                foreach (Element elem in allElements)
                {
                    if (elem == null || !elem.IsValidObject) continue;
                    string cat = ParameterHelpers.GetCategoryName(elem);
                    if (!known.Contains(cat)) continue;

                    result.TotalElements++;
                    string tag = ParameterHelpers.GetString(elem, ParamRegistry.TAG1);

                    if (string.IsNullOrEmpty(tag))
                    {
                        result.Untagged++;
                        AddIssue(result, "Untagged");
                    }
                    else if (TagConfig.TagIsFullyResolved(tag))
                    {
                        result.TaggedComplete++;
                        result.FullyResolved++;
                    }
                    else if (TagConfig.TagIsComplete(tag))
                    {
                        result.TaggedComplete++;
                        // Has placeholders — check which tokens are default/placeholder
                        string[] parts = tag.Split(new[] { ParamRegistry.Separator }, StringSplitOptions.None);
                        if (parts.Length >= 8)
                        {
                            if (parts[1] == "XX") AddIssue(result, "Missing LOC");
                            if (parts[2] == "XX" || parts[2] == "ZZ") AddIssue(result, "Missing ZONE");
                            if (parts[3] == "XX") AddIssue(result, "Missing LVL");
                            if (parts[4] == "GEN") AddIssue(result, "Generic SYS");
                            if (parts[5] == "GEN") AddIssue(result, "Generic FUNC");
                            if (parts[6] == "GEN") AddIssue(result, "Generic PROD");
                            if (parts[7] == "0000") AddIssue(result, "SEQ=0000");
                        }
                    }
                    else
                    {
                        result.TaggedIncomplete++;
                        AddIssue(result, "Incomplete tag");
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
}
