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

            /// <summary>Per-discipline compliance breakdown.</summary>
            public Dictionary<string, DiscComplianceData> ByDisc { get; set; }
                = new Dictionary<string, DiscComplianceData>(StringComparer.OrdinalIgnoreCase);

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

                    // Per-discipline tracking (Item 18)
                    string disc = ParameterHelpers.GetString(elem, ParamRegistry.DISC);
                    if (string.IsNullOrEmpty(disc))
                        disc = TagConfig.DiscMap.TryGetValue(cat, out string dv) ? dv : "?";

                    if (!result.ByDisc.TryGetValue(disc, out var discData))
                    {
                        discData = new DiscComplianceData();
                        result.ByDisc[disc] = discData;
                    }
                    discData.Total++;

                    if (string.IsNullOrEmpty(ParameterHelpers.GetString(elem, ParamRegistry.LOC))) discData.MissingLoc++;
                    if (string.IsNullOrEmpty(ParameterHelpers.GetString(elem, ParamRegistry.SYS))) discData.MissingSys++;
                    if (string.IsNullOrEmpty(ParameterHelpers.GetString(elem, ParamRegistry.PROD))) discData.MissingProd++;

                    string tag = ParameterHelpers.GetString(elem, ParamRegistry.TAG1);

                    if (string.IsNullOrEmpty(tag))
                    {
                        result.Untagged++;
                        discData.Untagged++;
                        AddIssue(result, "Untagged");
                    }
                    else if (TagConfig.TagIsFullyResolved(tag))
                    {
                        result.TaggedComplete++;
                        result.FullyResolved++;
                        discData.Tagged++;
                    }
                    else if (TagConfig.TagIsComplete(tag))
                    {
                        result.TaggedComplete++;
                        discData.Tagged++;
                        // Has placeholders — check which tokens
                        string[] parts = tag.Split(ParamRegistry.Separator[0]);
                        if (parts.Length >= 8)
                        {
                            if (parts[1] == "XX") AddIssue(result, "Missing LOC");
                            if (parts[2] == "XX" || parts[2] == "ZZ") AddIssue(result, "Missing ZONE");
                            if (parts[7] == "0000") AddIssue(result, "SEQ=0000");
                        }
                    }
                    else
                    {
                        result.TaggedIncomplete++;
                        discData.Untagged++;
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
