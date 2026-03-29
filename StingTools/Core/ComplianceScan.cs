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
        /// <summary>Phase 78: Timestamp of last scan start for timeout recovery.</summary>
        private static DateTime _lastScanStart = DateTime.MinValue;

        // PERF-R1: Static cached arrays to avoid per-element allocations in hot scan loop
        // DI-001 FIX: Use mutable field so separator refreshes on cache invalidation
        private static string[] _separatorArray;
        private static readonly string[] _tokenKeys = { "DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ" };

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
                TaggedComplete > 0 ? Math.Max(0, TaggedComplete - ContainersMissing) * 100.0 / TaggedComplete : 0;

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
                    (DateTime.UtcNow - _cacheTime) < CacheLifetime)
                    return _cached;
            }

            // C-06 FIX: Atomic compare-exchange prevents concurrent scan race.
            // Return stale cached result during scan to prevent dashboard flicker.
            // Previously returned empty ComplianceResult when _cached was null on first scan,
            // causing 0% compliance RED flash. Now returns a "pending" result with -1 TotalElements
            // so callers can detect "no data yet" vs "0% compliant".
            // Phase 78: Timeout recovery — if scanning flag stuck for >60s (Revit hang/crash mid-scan),
            // auto-reset to allow new scans. Prevents permanent lock-out of compliance dashboard.
            // CS-01 FIX: Use UtcNow for all cache math to prevent DST fall-back freezing cache for 1 hour
            if (_scanning == 1 && (DateTime.UtcNow - _lastScanStart).TotalSeconds > 60)
            {
                StingLog.Warn("ComplianceScan: scanning flag stuck >60s — auto-resetting");
                Interlocked.Exchange(ref _scanning, 0);
            }

            if (Interlocked.CompareExchange(ref _scanning, 1, 0) != 0)
            {
                lock (_cacheLock)
                {
                    if (_cached != null) return _cached;
                    // First scan still in progress — return a distinguishable "pending" result
                    // with non-alarming defaults instead of 0/0 = RED
                    return new ComplianceResult
                    {
                        ScanTime = DateTime.UtcNow,
                        TotalElements = -1 // sentinel: callers should treat -1 as "scan pending"
                    };
                }
            }

            try
            {
                _lastScanStart = DateTime.UtcNow;
                var result = new ComplianceResult { ScanTime = DateTime.UtcNow };
                var known = new HashSet<string>(TagConfig.DiscMap.Keys);
                if (known.Count == 0)
                {
                    StingLog.Warn("ComplianceScan: DiscMap is empty — no categories to scan. Load TagConfig first.");
                    return result;
                }

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
                    // PERF-CRIT: Scan timeout — abort after 8 seconds to prevent blocking
                    // UI thread on very large models (50K+ elements). Partial results still useful.
                    var scanStart = DateTime.UtcNow;
                    const int ScanTimeoutMs = 8000;

                    foreach (Element elem in scanColl)
                    {
                        if (elem == null || !elem.IsValidObject) continue;
                        string cat = ParameterHelpers.GetCategoryName(elem);
                        if (!known.Contains(cat)) continue;

                        result.TotalElements++;

                        // PERF-CRIT: Check timeout every 500 elements to avoid DateTime overhead
                        if (result.TotalElements % 500 == 0 &&
                            (DateTime.UtcNow - scanStart).TotalMilliseconds > ScanTimeoutMs)
                        {
                            StingLog.Warn($"ComplianceScan: timeout at {result.TotalElements} elements " +
                                $"({(DateTime.UtcNow - scanStart).TotalMilliseconds:F0}ms). " +
                                "Results are partial — run manually for full scan.");
                            break;
                        }
                        string disc = TagConfig.DiscMap.TryGetValue(cat, out var discVal) ? discVal : "X";
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
                            // PERF-R1: Use cached separator array; replace LINQ Skip/Take/All with for-loop
                            // DI-001: Rebuild separator array from ParamRegistry if null (first scan or after InvalidateCache)
                            if (_separatorArray == null)
                                _separatorArray = new[] { ParamRegistry.Separator };
                            string[] parts = tag.Split(_separatorArray, StringSplitOptions.None);
                            int po = !string.IsNullOrEmpty(TagConfig.TagPrefix) ? 1 : 0;
                            int so = !string.IsNullOrEmpty(TagConfig.TagSuffix) ? 1 : 0;
                            bool hasCorrectSegments = parts.Length >= 8 + po + so;
                            if (hasCorrectSegments)
                            {
                                for (int si = po; si < po + 8; si++)
                                {
                                    if (string.IsNullOrWhiteSpace(parts[si]))
                                    { hasCorrectSegments = false; break; }
                                }
                            }

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

                                // PERF-R1: Use static readonly token keys array instead of per-element allocation
                                for (int ti = 0; ti < _tokenKeys.Length && (ti + po) < parts.Length; ti++)
                                {
                                    string part = parts[ti + po];
                                    if (string.IsNullOrWhiteSpace(part) || part == "XX" || part == "ZZ"
                                        || part == "GEN" || part == "0000")
                                    {
                                        result.EmptyTokenCounts.TryGetValue(_tokenKeys[ti], out int etc);
                                        result.EmptyTokenCounts[_tokenKeys[ti]] = etc + 1;
                                    }
                                }
                            }
                        }

                        string rev = ParameterHelpers.GetString(elem, ParamRegistry.REV);
                        if (!string.IsNullOrEmpty(rev))
                        {
                            result.RevisionComplete++;
                            result.RevisionDistribution.TryGetValue(rev, out int rdc);
                            result.RevisionDistribution[rev] = rdc + 1;
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
                            result.StatusDistribution.TryGetValue(status, out int sdc);
                            result.StatusDistribution[status] = sdc + 1;
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
                                            result.EmptyContainerCounts.TryGetValue(cName, out int ecc);
                                            result.EmptyContainerCounts[cName] = ecc + 1;
                                        }
                                    }
                                    if (emptyCount > 0) result.ContainersMissing++;
                                }
                            }
                            catch (Exception ex) { StingLog.Warn($"Container completeness check failed: {ex.Message}"); }
                        }

                        try
                        {
                            // F-07: Use cached GetInt instead of direct LookupParameter bypass
                            if (ParameterHelpers.GetInt(elem, ParamRegistry.STALE, 0) == 1)
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
                    // F-13: Iterate collector directly — avoid .ToList() allocation on large sheet sets
                    var sheetColl = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheet))
                        .Cast<ViewSheet>();
                    foreach (var sheet in sheetColl)
                    {
                        result.TotalSheets++;
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
                    _cacheTime = DateTime.UtcNow;
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
            var result = new ComplianceResult { ScanTime = DateTime.UtcNow };
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

                // F-12: Cache separator array once before the loop — avoids per-element allocation
                var viewSepArray = new[] { ParamRegistry.Separator };
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
                        string[] parts = tag.Split(viewSepArray, StringSplitOptions.None);
                        int po = !string.IsNullOrEmpty(TagConfig.TagPrefix) ? 1 : 0;
                        int so = !string.IsNullOrEmpty(TagConfig.TagSuffix) ? 1 : 0;
                        // PERF-R10: Use for-loop instead of LINQ Skip/Take/All to match Scan() pattern
                        bool hasCorrectSegments = false;
                        if (parts.Length >= 8 + po + so)
                        {
                            hasCorrectSegments = true;
                            for (int si = po; si < po + 8; si++)
                            {
                                if (string.IsNullOrWhiteSpace(parts[si])) { hasCorrectSegments = false; break; }
                            }
                        }

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
                _incrementalCount = 0;
                // DI-001 FIX: Null out separator so it's re-read from ParamRegistry on next scan
                _separatorArray = null;
            }
        }

        // FUT-16: Incremental update tracking
        private static int _incrementalCount = 0;
        private const int MaxIncrementalUpdates = 1000;

        /// <summary>FUT-16: O(1) incremental compliance update instead of full O(n) rescan.
        /// Adjusts cached counters in-place when a single element's tag status changes.
        /// Falls back to full rescan after 1000 incremental updates to prevent drift.
        /// NOTE: TotalElements is NOT adjusted — this method is only valid for status changes
        /// on already-scanned elements. New elements require a full rescan.</summary>
        public static void IncrementalUpdate(string oldTag, string newTag, string disc)
        {
            lock (_cacheLock)
            {
                if (_cached == null) return; // No cache to update
                // PERF-R1: Use plain increment inside lock (Interlocked unnecessary under lock)
                if (++_incrementalCount > MaxIncrementalUpdates)
                {
                    // Drift guard: force full rescan
                    _cached = null;
                    _cacheTime = DateTime.MinValue;
                    _incrementalCount = 0;
                    return;
                }

                bool wasTagged = !string.IsNullOrEmpty(oldTag);
                bool isTagged = !string.IsNullOrEmpty(newTag);
                bool wasComplete = wasTagged && TagConfig.TagIsComplete(oldTag);
                bool isComplete = isTagged && TagConfig.TagIsComplete(newTag);

                // CRIT-03: Guard all counter decrements to prevent negative values
                // when IncrementalUpdate is called for new elements not in the original scan
                if (!wasTagged && isTagged) _cached.Untagged = Math.Max(0, _cached.Untagged - 1);
                else if (wasTagged && !isTagged) _cached.Untagged++;

                // Update tagged complete count
                if (!wasComplete && isComplete) _cached.TaggedComplete++;
                else if (wasComplete && !isComplete) _cached.TaggedComplete = Math.Max(0, _cached.TaggedComplete - 1);

                // Update tagged incomplete
                bool wasIncomplete = wasTagged && !wasComplete;
                bool isIncomplete = isTagged && !isComplete;
                if (!wasIncomplete && isIncomplete) _cached.TaggedIncomplete++;
                else if (wasIncomplete && !isIncomplete) _cached.TaggedIncomplete = Math.Max(0, _cached.TaggedIncomplete - 1);

                // Phase 86: Track FullyResolved (complete + no placeholders) to prevent StrictPercent drift
                bool wasResolved = wasComplete && !TagConfig.TagHasPlaceholders(oldTag ?? "");
                bool isResolved = isComplete && !TagConfig.TagHasPlaceholders(newTag ?? "");
                if (!wasResolved && isResolved) _cached.FullyResolved++;
                else if (wasResolved && !isResolved) _cached.FullyResolved = Math.Max(0, _cached.FullyResolved - 1);

                // Also track placeholder transitions
                bool wasPlaceholder = wasComplete && !wasResolved;
                bool isPlaceholder = isComplete && !isResolved;
                if (!wasPlaceholder && isPlaceholder) _cached.PlaceholderCount++;
                else if (wasPlaceholder && !isPlaceholder) _cached.PlaceholderCount = Math.Max(0, _cached.PlaceholderCount - 1);

                // Update per-discipline counts
                if (!string.IsNullOrEmpty(disc) && _cached.ByDisc != null)
                {
                    if (!_cached.ByDisc.TryGetValue(disc, out var dd))
                    {
                        // ME-01 FIX: Initialize Tagged/Untagged based on current element state
                        dd = new DiscComplianceData { Total = 1, Tagged = isTagged ? 1 : 0, Untagged = isTagged ? 0 : 1 };
                        _cached.ByDisc[disc] = dd;
                    }
                    else
                    {
                        if (!wasTagged && isTagged) { dd.Tagged++; dd.Untagged = Math.Max(0, dd.Untagged - 1); }
                        else if (wasTagged && !isTagged) { dd.Tagged = Math.Max(0, dd.Tagged - 1); dd.Untagged++; }
                    }
                }

                // CS-01 FIX: Use UtcNow consistently for cache staleness (DST-safe)
                _cacheTime = DateTime.UtcNow;
            }
        }

        private static void AddIssue(ComplianceResult result, string issueType)
        {
            result.IssuesByType.TryGetValue(issueType, out int ibtc);
            result.IssuesByType[issueType] = ibtc + 1;
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

    /// <summary>FUT-02: Per-linked-model compliance data for federated model coordination.</summary>
    public class LinkedModelCompliance
    {
        public string LinkName { get; set; }
        public string LinkPath { get; set; }
        public int TotalElements { get; set; }
        public int TaggedComplete { get; set; }
        public int Untagged { get; set; }
        public double CompliancePct => TotalElements > 0 ? TaggedComplete * 100.0 / TotalElements : 0;
        public string RAGStatus => CompliancePct >= 80 ? "GREEN" : CompliancePct >= 50 ? "AMBER" : "RED";
    }

    /// <summary>FUT-02: Aggregated compliance across host + all linked Revit models.</summary>
    public class FederatedComplianceResult
    {
        public ComplianceScan.ComplianceResult HostResult { get; set; }
        public List<LinkedModelCompliance> LinkedResults { get; set; } = new List<LinkedModelCompliance>();
        public int TotalAcrossAll => (HostResult?.TotalElements ?? 0) + LinkedResults.Sum(l => l.TotalElements);
        public int TaggedAcrossAll => (HostResult?.TaggedComplete ?? 0) + LinkedResults.Sum(l => l.TaggedComplete);
        public double FederatedCompliancePct => TotalAcrossAll > 0 ? TaggedAcrossAll * 100.0 / TotalAcrossAll : 0;
        public string FederatedRAG => FederatedCompliancePct >= 80 ? "GREEN" : FederatedCompliancePct >= 50 ? "AMBER" : "RED";
    }

    /// <summary>FUT-02: Scan compliance across host document and all linked Revit models.</summary>
    public static class FederatedComplianceScanner
    {
        public static FederatedComplianceResult ScanFederated(Document hostDoc)
        {
            var result = new FederatedComplianceResult();

            // Scan host model
            ComplianceScan.InvalidateCache();
            result.HostResult = ComplianceScan.Scan(hostDoc);

            // Scan each linked model
            try
            {
                var linkInstances = new FilteredElementCollector(hostDoc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .ToList();

                foreach (var linkInst in linkInstances)
                {
                    try
                    {
                        var linkType = hostDoc.GetElement(linkInst.GetTypeId()) as RevitLinkType;
                        if (linkType == null) continue;

                        Document linkedDoc = linkInst.GetLinkDocument();
                        if (linkedDoc == null) continue;

                        // Quick compliance scan of linked document
                        var linkResult = new LinkedModelCompliance
                        {
                            LinkName = linkType.Name ?? linkInst.Name,
                            LinkPath = linkedDoc.PathName ?? ""
                        };

                        var elems = new FilteredElementCollector(linkedDoc)
                            .WhereElementIsNotElementType()
                            .WherePasses(new ElementMulticategoryFilter(SharedParamGuids.AllCategoryEnums));

                        int linkTotal = 0;
                        foreach (var el in elems)
                        {
                            linkTotal++;
                            string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                            if (!string.IsNullOrEmpty(tag1) && TagConfig.TagIsComplete(tag1))
                                linkResult.TaggedComplete++;
                            else if (string.IsNullOrEmpty(tag1))
                                linkResult.Untagged++;
                        }
                        linkResult.TotalElements = linkTotal;

                        result.LinkedResults.Add(linkResult);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"FederatedScan link '{linkInst.Name}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"FederatedScan: {ex.Message}"); }

            return result;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // Phase 56: COMPLIANCE TREND TRACKER
    // Persists daily compliance snapshots for trend analysis
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Phase 56: Tracks compliance over time for morning briefings and coordination reports.
    /// Persists to .sting_compliance_trend.json sidecar alongside the .rvt file.
    /// </summary>
    internal static class ComplianceTrendTracker
    {
        private const int MaxDays = 90;

        /// <summary>Record today's compliance snapshot.</summary>
        public static void RecordSnapshot(Document doc, ComplianceScan.ComplianceResult result)
        {
            if (doc == null || result == null || string.IsNullOrEmpty(doc.PathName)) return;
            try
            {
                string path = System.IO.Path.ChangeExtension(doc.PathName, ".sting_compliance_trend.json");
                var entries = LoadEntries(path);
                string today = DateTime.UtcNow.ToString("yyyy-MM-dd");

                // Update today's entry or add new
                var existing = entries.FirstOrDefault(e => e.Date == today);
                if (existing != null)
                {
                    existing.CompliancePct = result.CompliancePercent;
                    existing.StaleCount = result.StaleCount;
                    existing.Warnings = doc.GetWarnings()?.Count ?? 0;
                    existing.PlaceholderCount = result.PlaceholderCount;
                }
                else
                {
                    entries.Add(new TrendEntry
                    {
                        Date = today,
                        CompliancePct = result.CompliancePercent,
                        TotalElements = result.TotalElements,
                        TaggedComplete = result.TaggedComplete,
                        StaleCount = result.StaleCount,
                        Warnings = doc.GetWarnings()?.Count ?? 0,
                        PlaceholderCount = result.PlaceholderCount
                    });
                }

                // Cap at MaxDays
                if (entries.Count > MaxDays)
                    entries = entries.OrderByDescending(e => e.Date).Take(MaxDays).ToList();

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(entries,
                    Newtonsoft.Json.Formatting.Indented);
                System.IO.File.WriteAllText(path, json);
            }
            catch (Exception ex) { StingLog.Warn($"ComplianceTrendTracker: {ex.Message}"); }
        }

        /// <summary>Get trend direction over last N days.</summary>
        public static (string direction, double delta) GetTrend(Document doc, int days = 7)
        {
            if (doc == null || string.IsNullOrEmpty(doc.PathName))
                return ("unknown", 0);
            try
            {
                string path = System.IO.Path.ChangeExtension(doc.PathName, ".sting_compliance_trend.json");
                var entries = LoadEntries(path);
                if (entries.Count < 2) return ("insufficient data", 0);

                var sorted = entries.OrderBy(e => e.Date).ToList();
                int startIdx = Math.Max(0, sorted.Count - days);
                double first = sorted[startIdx].CompliancePct;
                double last = sorted[^1].CompliancePct;
                double delta = last - first;

                string dir = delta > 2 ? "improving" : delta < -2 ? "declining" : "stable";
                return (dir, delta);
            }
            catch { return ("unknown", 0); }
        }

        private static List<TrendEntry> LoadEntries(string path)
        {
            try
            {
                if (System.IO.File.Exists(path))
                {
                    string json = System.IO.File.ReadAllText(path);
                    return Newtonsoft.Json.JsonConvert.DeserializeObject<List<TrendEntry>>(json)
                        ?? new List<TrendEntry>();
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadTrendEntries: {ex.Message}"); }
            return new List<TrendEntry>();
        }

        /// <summary>Daily compliance snapshot entry.</summary>
        public class TrendEntry
        {
            [Newtonsoft.Json.JsonProperty("date")] public string Date { get; set; }
            [Newtonsoft.Json.JsonProperty("compliance_pct")] public double CompliancePct { get; set; }
            [Newtonsoft.Json.JsonProperty("total")] public int TotalElements { get; set; }
            [Newtonsoft.Json.JsonProperty("tagged")] public int TaggedComplete { get; set; }
            [Newtonsoft.Json.JsonProperty("stale")] public int StaleCount { get; set; }
            [Newtonsoft.Json.JsonProperty("warnings")] public int Warnings { get; set; }
            [Newtonsoft.Json.JsonProperty("placeholders")] public int PlaceholderCount { get; set; }
        }
    }
}
