using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        /// <summary>LOGIC-01: Use int + Interlocked for atomic scanning guard instead of volatile bool race.</summary>
        private static int _scanning = 0; // 0 = not scanning, 1 = scanning

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
            /// <summary>A5: Elements with empty STATUS parameter.</summary>
            public int StatusMissing { get; set; }
            /// <summary>STATUS value distribution for dashboard display.</summary>
            public Dictionary<string, int> StatusDistribution { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            /// <summary>A5: Elements with at least one empty discipline container.</summary>
            public int ContainersMissing { get; set; }
            /// <summary>Per-container empty counts for granular compliance drill-down.</summary>
            public Dictionary<string, int> EmptyContainerCounts { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            /// <summary>Total container parameter checks performed.</summary>
            public int TotalContainerChecks { get; set; }
            /// <summary>FIX-12: Elements marked as stale (spatial context may have changed).</summary>
            public int StaleCount { get; set; }
            /// <summary>Phase 39: Sheet-level compliance tracking.</summary>
            public int TotalSheets { get; set; }
            /// <summary>Phase 39: Sheets with SHT_TAG_1 populated.</summary>
            public int SheetsTagged { get; set; }
            /// <summary>Phase 39: Sheets without SHT_TAG_1.</summary>
            public int SheetsUntagged { get; set; }
            /// <summary>Phase 39: Sheet tagging compliance percentage.</summary>
            public double SheetCompliancePct => TotalSheets > 0 ? SheetsTagged * 100.0 / TotalSheets : 0;
            /// <summary>AE-05: Per-token empty count for granular compliance reporting.</summary>
            public Dictionary<string, int> EmptyTokenCounts { get; } = new Dictionary<string, int>
            {
                ["DISC"]=0, ["LOC"]=0, ["ZONE"]=0, ["LVL"]=0,
                ["SYS"]=0, ["FUNC"]=0, ["PROD"]=0, ["SEQ"]=0,
                ["STATUS"]=0, ["REV"]=0
            };

            /// <summary>Phase 48: Per-phase compliance breakdown for BIM coordinators tracking stage progress.</summary>
            public Dictionary<string, PhaseComplianceData> ByPhase { get; set; } = new Dictionary<string, PhaseComplianceData>();

            /// <summary>Phase 48: Count of elements with placeholder tokens (GEN/XX/ZZ/0000) —
            /// "complete" but not production-ready.</summary>
            public int PlaceholderCount { get; set; }
            public Dictionary<string, int> IssuesByType { get; set; } = new Dictionary<string, int>();
            public DateTime ScanTime { get; set; }

            /// <summary>A5: Data completeness across tags, STATUS, and containers (0-100%).</summary>
            public double DataCompletenessPercent => TotalElements == 0 ? 0 :
                100.0 * (TaggedComplete + (TotalElements - StatusMissing) + (TotalElements - ContainersMissing))
                / (TotalElements * 3.0);

            public double CompliancePercent =>
                TotalElements > 0 ? TaggedComplete * 100.0 / TotalElements : 0;

            public double StrictPercent =>
                TotalElements > 0 ? FullyResolved * 100.0 / TotalElements : 0;

            /// <summary>GAP-006 fix: Percentage of elements with REV populated.</summary>
            public double RevisionPercent =>
                TotalElements > 0 ? RevisionComplete * 100.0 / TotalElements : 0;

            /// <summary>LOGIC-003: Percentage of tagged elements that have all applicable containers populated.
            /// Separate from CompliancePercent — an element can be "tagged" (TAG1 exists) but have empty
            /// discipline containers, which breaks COBie export and platform deliverables.</summary>
            public double ContainerCompletePct =>
                TaggedComplete > 0 ? (TaggedComplete - ContainersMissing) * 100.0 / TaggedComplete : 0;

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

            /// <summary>Short summary for status bar display — includes revision, STATUS, container, and stale counts.</summary>
            public string StatusBarText =>
                $"{RAGStatus} {CompliancePercent:F0}% tagged | {RevisionPercent:F0}% REV | " +
                $"{(ContainersMissing > 0 ? $"{ContainerCompletePct:F0}% containers | " : "")}" +
                $"{(StatusMissing > 0 ? $"{StatusMissing} no-STATUS | " : "")}" +
                $"{(StaleCount > 0 ? $"{StaleCount} stale | " : "")}" +
                $"{(SheetsUntagged > 0 ? $"{SheetsUntagged}/{TotalSheets} sheets untagged | " : "")}" +
                $"{Untagged} untagged";

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
            // HR-03: Non-blocking scan — don't hold lock during full element iteration
            // to prevent UI freeze on large models (10k+ elements).

            // Fast path: return cached if valid (brief lock)
            lock (_cacheLock)
            {
                if (!forceRefresh && _cached != null &&
                    (DateTime.Now - _cacheTime) < CacheLifetime)
                    return _cached;
            }

            // LOGIC-01 + Phase 39: Atomic compare-exchange prevents concurrent scan race.
            // Return stale cached result (never null) during scan to prevent dashboard flicker.
            if (Interlocked.CompareExchange(ref _scanning, 1, 0) != 0)
            {
                lock (_cacheLock)
                {
                    return _cached ?? new ComplianceResult { ScanTime = DateTime.Now };
                }
            }

            try
            {
                var result = new ComplianceResult { ScanTime = DateTime.Now };
                var known = new HashSet<string>(TagConfig.DiscMap.Keys);

                try
                {
                    // PERF-06: Use lazy iterator instead of .ToList() — avoids allocating
                    // a List<Element> of 10K+ elements. Revit's collector is natively lazy.
                    var scanColl = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType();
                    var scanCatEnums = SharedParamGuids.AllCategoryEnums;
                    if (scanCatEnums != null && scanCatEnums.Length > 0)
                        scanColl.WherePasses(new ElementMulticategoryFilter(
                            new List<BuiltInCategory>(scanCatEnums)));
                    foreach (Element elem in scanColl)
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
                        else
                        {
                            // Parse tag segments to determine completeness
                            string[] parts = tag.Split(new[] { ParamRegistry.Separator }, StringSplitOptions.None);
                            int po = !string.IsNullOrEmpty(TagConfig.TagPrefix) ? 1 : 0;
                            int so = !string.IsNullOrEmpty(TagConfig.TagSuffix) ? 1 : 0;
                            bool hasCorrectSegments = parts.Length >= 8 + po + so
                                && parts.Skip(po).Take(8).All(p => !string.IsNullOrWhiteSpace(p));

                            if (!hasCorrectSegments)
                            {
                                result.TaggedIncomplete++;
                                AddIssue(result, "Incomplete tag");
                            }
                            else
                            {
                                bool hasPlaceholders = TagConfig.TagHasPlaceholders(tag);
                                result.TaggedComplete++;
                                dd.Tagged++;
                                if (!hasPlaceholders)
                                    result.FullyResolved++;
                                else
                                    result.PlaceholderCount++;

                                // Drill into specific token issues regardless of placeholder status
                                if (parts[po + 1] == "XX") { AddIssue(result, "Missing LOC"); dd.MissingLoc++; }
                                if (parts[po + 2] == "XX" || parts[po + 2] == "ZZ") AddIssue(result, "Missing ZONE");
                                if (parts[po + 3] == "XX") AddIssue(result, "Missing LVL");
                                if (parts[po + 4] == "GEN") { AddIssue(result, "Generic SYS"); dd.MissingSys++; }
                                if (parts[po + 5] == "GEN") AddIssue(result, "Generic FUNC");
                                if (parts[po + 6] == "GEN") { AddIssue(result, "Generic PROD"); dd.MissingProd++; }
                                if (parts[po + 7] == "0000") AddIssue(result, "SEQ=0000");

                                string[] tokenKeys = new[] {"DISC","LOC","ZONE","LVL","SYS","FUNC","PROD","SEQ"};
                                for (int ti = 0; ti < tokenKeys.Length && (ti + po) < parts.Length; ti++)
                                {
                                    string part = parts[ti + po];
                                    if (string.IsNullOrWhiteSpace(part) || part == "XX" || part == "ZZ"
                                        || part == "GEN" || part == "0000")
                                    {
                                        if (result.EmptyTokenCounts.ContainsKey(tokenKeys[ti]))
                                            result.EmptyTokenCounts[tokenKeys[ti]]++;
                                    }
                                }
                            }
                        }

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
                            result.EmptyTokenCounts["REV"]++;
                            AddIssue(result, "Missing REV");
                        }

                        // A5: STATUS completeness with value distribution tracking
                        string status = ParameterHelpers.GetString(elem, ParamRegistry.STATUS);
                        if (string.IsNullOrEmpty(status))
                        {
                            result.StatusMissing++;
                            result.EmptyTokenCounts["STATUS"]++;
                            AddIssue(result, "Missing STATUS");
                        }
                        else
                        {
                            if (!result.StatusDistribution.ContainsKey(status))
                                result.StatusDistribution[status] = 0;
                            result.StatusDistribution[status]++;
                        }

                        // Phase 48: Phase-based compliance breakdown
                        try
                        {
                            string phaseName = "";
                            var phaseParam = elem.get_Parameter(BuiltInParameter.PHASE_CREATED);
                            if (phaseParam != null)
                            {
                                var phaseEl = doc.GetElement(phaseParam.AsElementId());
                                if (phaseEl != null) phaseName = phaseEl.Name;
                            }
                            if (string.IsNullOrEmpty(phaseName)) phaseName = "Unknown";
                            if (!result.ByPhase.TryGetValue(phaseName, out var pd))
                            {
                                pd = new PhaseComplianceData();
                                result.ByPhase[phaseName] = pd;
                            }
                            pd.Total++;
                            if (!string.IsNullOrEmpty(tag)) pd.Tagged++;
                            else pd.Untagged++;
                        }
                        catch (Exception ex) { StingLog.Warn($"Phase compliance: {ex.Message}"); }

                        // A5: Container check with per-container tracking
                        // PERF-05: Skip expensive container check when TAG1 is empty — if the
                        // element has no tag, containers are guaranteed empty (saves 50-70% scan time)
                        if (!string.IsNullOrEmpty(tag))
                        {
                            try
                            {
                                var containers = ParamRegistry.ContainersForCategory(cat);
                                if (containers != null && containers.Length > 0)
                                {
                                    int emptyCount = 0;
                                    for (int ci = 0; ci < containers.Length; ci++)
                                    {
                                        result.TotalContainerChecks++;
                                        if (string.IsNullOrEmpty(ParameterHelpers.GetString(elem, containers[ci].ParamName)))
                                        {
                                            emptyCount++;
                                            string cName = containers[ci].ParamName;
                                            if (!result.EmptyContainerCounts.ContainsKey(cName))
                                                result.EmptyContainerCounts[cName] = 0;
                                            result.EmptyContainerCounts[cName]++;
                                        }
                                    }
                                    if (emptyCount > 0) result.ContainersMissing++;
                                }
                            }
                            catch (Exception ex) { StingLog.Warn($"Container completeness check failed: {ex.Message}"); }
                        }

                        try
                        {
                            Parameter stalePar = elem.LookupParameter(ParamRegistry.STALE);
                            if (stalePar != null && stalePar.StorageType == StorageType.Integer && stalePar.AsInteger() == 1)
                            {
                                result.StaleCount++;
                                AddIssue(result, "Stale element");
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"Stale element check failed: {ex.Message}"); }
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Compliance scan failed: {ex.Message}");
                }

                // Phase 39: Sheet compliance scan
                try
                {
                    var sheets = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheet))
                        .Cast<ViewSheet>()
                        .ToList();
                    result.TotalSheets = sheets.Count;
                    foreach (var sheet in sheets)
                    {
                        string shtTag = ParameterHelpers.GetString(sheet, ParamRegistry.SHT_TAG_1);
                        if (!string.IsNullOrEmpty(shtTag))
                            result.SheetsTagged++;
                        else
                            result.SheetsUntagged++;
                    }
                    if (result.SheetsUntagged > 0)
                        AddIssue(result, $"Sheets untagged");
                }
                catch (Exception ex) { StingLog.Warn($"Sheet compliance scan: {ex.Message}"); }

                // Write result under brief lock
                lock (_cacheLock)
                {
                    _cached = result;
                    _cacheTime = DateTime.Now;
                }
                return result;
            }
            finally
            {
                Interlocked.Exchange(ref _scanning, 0);
            }
        }

        /// <summary>
        /// Phase 40: View-scoped compliance scan for quick per-view feedback.
        /// Does NOT use or update the project-level cache.
        /// Useful after AutoTag on a single view to show view-specific compliance.
        /// </summary>
        public static ComplianceResult ScanView(Document doc, View view)
        {
            var result = new ComplianceResult { ScanTime = DateTime.Now };
            if (doc == null || view == null) return result;
            try
            {
                var known = new HashSet<string>(TagConfig.DiscMap.Keys);
                var scanColl = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType();
                var catEnums = SharedParamGuids.AllCategoryEnums;
                if (catEnums != null && catEnums.Length > 0)
                    scanColl.WherePasses(new ElementMulticategoryFilter(
                        new List<BuiltInCategory>(catEnums)));

                foreach (Element elem in scanColl)
                {
                    if (elem == null || !elem.IsValidObject) continue;
                    string cat = ParameterHelpers.GetCategoryName(elem);
                    if (!known.Contains(cat)) continue;

                    result.TotalElements++;
                    string tag = ParameterHelpers.GetString(elem, ParamRegistry.TAG1);
                    if (string.IsNullOrEmpty(tag))
                    {
                        result.Untagged++;
                    }
                    else
                    {
                        string[] parts = tag.Split(new[] { ParamRegistry.Separator }, StringSplitOptions.None);
                        int po = !string.IsNullOrEmpty(TagConfig.TagPrefix) ? 1 : 0;
                        int so = !string.IsNullOrEmpty(TagConfig.TagSuffix) ? 1 : 0;
                        bool hasCorrectSegments = parts.Length >= 8 + po + so
                            && parts.Skip(po).Take(8).All(p => !string.IsNullOrWhiteSpace(p));

                        if (!hasCorrectSegments)
                            result.TaggedIncomplete++;
                        else
                        {
                            result.TaggedComplete++;
                            if (!TagConfig.TagHasPlaceholders(tag))
                                result.FullyResolved++;
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ScanView: {ex.Message}"); }
            return result;
        }

        /// <summary>
        /// LOG-01 FIX: Thread-safe accessor for cached result without triggering a new scan.
        /// Returns null if no cached result exists. Uses lock to prevent torn reads.
        /// </summary>
        public static ComplianceResult GetCached()
        {
            lock (_cacheLock) { return _cached; }
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

    /// <summary>Phase 48: Per-phase compliance data for stage tracking.</summary>
    public class PhaseComplianceData
    {
        public int Total { get; set; }
        public int Tagged { get; set; }
        public int Untagged { get; set; }
        public double CompliancePct => Total > 0 ? Tagged * 100.0 / Total : 0;
    }
}
