// ══════════════════════════════════════════════════════════════════════════
//  BOQCostManager.cs — Phase 3 of the BOQ & Cost Manager.
//  Central engine. Builds a BOQDocument from the Revit model, writes cost
//  parameters back to elements and the ProjectInformation record, persists
//  JSON snapshots, compares snapshots, reconciles provisional sums and feeds
//  cash-flow generation for the 4D/5D tab.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.BIMManager;
using StingTools.BOQ.MeasurementStandard;
using StingTools.BOQ.Rates;
using StingTools.BOQ.Sync;
using StingTools.BOQ.Takeoff;
using StingTools.Core;
using StingTools.Core.Storage;
using StingTools.Temp;

namespace StingTools.BOQ
{
    internal static class BOQCostManager
    {
        // Newtonsoft settings shared by every snapshot write — indented, ignores nulls,
        // culture-invariant date format so snapshots round-trip across locales.
        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DateFormatString = "yyyy-MM-dd HH:mm:ss",
            Culture = CultureInfo.InvariantCulture
        };

        // Snapshots are capped at 20 per project — older ones pruned automatically.
        internal const int MaxSnapshotsRetained = 20;

        // Embodied-carbon lifecycle discount rate (UK Treasury Green Book default).
        private const double LifecycleDiscountRate = 0.035;
        private const int LifecycleYears = 25;

        // ══════════════════════════════════════════════════════════════════
        //  Public API — BuildBOQDocument
        //  Single entry point for building a complete BOQ from the model.
        //  Reusable from the WPF panel, the Excel exporter and the snapshot
        //  machinery. Never writes to the model — callers drive writes via
        //  WriteElementParameters / WriteProjectParameters / SaveSnapshot.
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds a complete BOQDocument for the model. Reads cost rates from
        /// cost_rates_5d.csv (configurable via TagConfig.CostRatesFileName),
        /// falls back to COBie type map and finally Scheduling4DEngine
        /// defaults. Merges manual/PS rows from project_boq_manual.json so
        /// a QS can author extra line items without modelling them.
        /// </summary>
        // ── Linked-model inclusion (per-link, persisted) ─────────────────────
        // The set holds the Titles of loaded links whose quantities are folded
        // into the takeoff. Persisted to <project>/_BIM_COORD/boq_links.json so
        // the choice survives reopen (sustainable) and is per-link (flexible).
        // Empty ⇒ host model only. Every consumer (panel, QTO export, snapshots)
        // reads the same persisted set, so the takeoff is consistent everywhere.
        private static readonly Dictionary<string, HashSet<string>> _linkInclusionCache
            = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        // ── P1.2 — per-link takeoff cache ────────────────────────────────────
        // RefreshAsync runs BuildBOQDocument synchronously, and STEP 6c walks every
        // linked document on every rebuild (filter toggle / rate edit / grouping
        // change). On a federated model that re-traverse freezes Revit. We cache the
        // RAW per-link line items (post-BuildLineItemFromElement, PRE-aggregate /
        // PRE-neutralise) keyed on the linked file's PathName, so a host-side
        // refresh reuses them without re-reading the link's Revit DB. Grouping is
        // applied AFTER the cache (cheap CPU on the cached rows) so it stays correct
        // across grouping changes without invalidating. Cleared on document close.
        private sealed class LinkTakeoffCacheEntry { public List<BOQLineItem> RawItems; }

        private static readonly Dictionary<string, LinkTakeoffCacheEntry> _linkTakeoffCache
            = new Dictionary<string, LinkTakeoffCacheEntry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Drop cached per-link takeoffs. Call when a link may have changed
        /// (document close, manual rebuild). Selection changes do NOT need this — the
        /// cache is keyed per-link, so toggling which links are included just changes
        /// which keys get looked up; a reload of the same selection still hits the
        /// cache.</summary>
        internal static void InvalidateLinkCache()
        {
            lock (_linkTakeoffCache) { _linkTakeoffCache.Clear(); }
        }

        // ── Phase 2D — incremental host take-off ─────────────────────────────
        //  The host walk (CollectCandidateElements + BuildLineItemFromElement
        //  per element) is the heavy part of a full build. We cache the RAW host
        //  line items (pre-aggregate / pre-group / pre-override, keyed per
        //  document) exactly like the per-link cache, plus a per-document dirty
        //  set fed by StingCostDirtyMarker (an IUpdater watching cost categories).
        //  On an incremental refresh only the dirty + newly-added elements are
        //  re-taken-off; removed elements drop their cached line; everything else
        //  reuses the cached row. Aggregation / grouping / overrides / line-refs
        //  then run identically to a full build, so the result is correct BY
        //  CONSTRUCTION whenever the dirty set is complete. Any change the dirty
        //  set can't safely localise (type / material / level / grid edits, rate
        //  or measurement config changes, a tracking gap) forces a full rebuild.
        //  Incremental only runs when the panel asks for it AND the marker is
        //  enabled; a full rebuild is always available and always correct.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<BOQLineItem>> _hostTakeoffCache
            = new System.Collections.Concurrent.ConcurrentDictionary<string, List<BOQLineItem>>(StringComparer.OrdinalIgnoreCase);

        private sealed class HostIncrementalState
        {
            public readonly HashSet<long> Dirty = new HashSet<long>();
            public bool ForceFull = true;   // first build is always full
            public readonly object Lock = new object();
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, HostIncrementalState> _hostIncremental
            = new System.Collections.Concurrent.ConcurrentDictionary<string, HostIncrementalState>(StringComparer.OrdinalIgnoreCase);

        // Set while a plugin-owned transaction runs (e.g. BuildBOQDocument's own
        // STEP 9 stale-flag write, or WriteElementParameters) so the dirty marker
        // doesn't self-dirty every costed element on each build — which would make
        // the next "incremental" refresh re-walk the whole model. Single API
        // thread ⇒ a plain static is safe.
        private static volatile bool _suppressDirty;
        internal static bool IsDirtySuppressed => _suppressDirty;

        private static HostIncrementalState IncrementalState(Document doc)
            => _hostIncremental.GetOrAdd(doc?.PathName ?? "default", _ => new HostIncrementalState());

        /// <summary>IUpdater hook — record elements changed in any way so the next
        /// incremental refresh re-takes-off only those.</summary>
        internal static void MarkHostDirty(Document doc, IEnumerable<long> ids)
        {
            if (doc == null || ids == null || _suppressDirty) return;
            var st = IncrementalState(doc);
            lock (st.Lock) foreach (var id in ids) if (id > 0) st.Dirty.Add(id);
        }

        /// <summary>IUpdater hook — a broad-impact change (type / material / level /
        /// grid) was seen; the cached rows can't be trusted, so force a full rebuild.</summary>
        internal static void ForceHostFull(Document doc)
        {
            if (doc == null) return;
            string key = doc.PathName ?? "default";
            _hostTakeoffCache.TryRemove(key, out _);
            var st = IncrementalState(doc);
            lock (st.Lock) { st.ForceFull = true; st.Dirty.Clear(); }
        }

        /// <summary>Global host-cache invalidation — used when rate / carbon /
        /// measurement config changes (Cost_ReloadRules, measurement-standard
        /// switch), since those affect every cached row.</summary>
        internal static void InvalidateHostCache()
        {
            _hostTakeoffCache.Clear();
            foreach (var st in _hostIncremental.Values)
                lock (st.Lock) { st.ForceFull = true; st.Dirty.Clear(); }
        }

        /// <summary>Drop the per-document host cache + dirty state (document close).</summary>
        internal static void ClearHostIncremental(Document doc)
        {
            string key = doc?.PathName ?? "default";
            _hostTakeoffCache.TryRemove(key, out _);
            _hostIncremental.TryRemove(key, out _);
        }

        // Host raw-item builder — full walk, or incremental (dirty + added only).
        private static List<BOQLineItem> BuildHostRawItems(Document doc, HashSet<string> knownCats,
            Dictionary<string, (double rate, string unit)> csvRates,
            Dictionary<string, string> cobieCostCodes,
            IMeasurementStandard measStd, bool allowIncremental)
        {
            string key = doc?.PathName ?? "default";
            var st = IncrementalState(doc);
            bool haveCache = _hostTakeoffCache.TryGetValue(key, out var cached) && cached != null;
            bool forceFull; HashSet<long> dirtySnapshot;
            lock (st.Lock) { forceFull = st.ForceFull; dirtySnapshot = new HashSet<long>(st.Dirty); }

            bool incremental = allowIncremental && haveCache && !forceFull;

            // The element collection (cheap iteration) runs in both paths; the
            // expensive BuildLineItemFromElement is what incremental skips.
            var currentElements = CollectCandidateElements(doc, knownCats);

            if (!incremental)
            {
                var items = new List<BOQLineItem>(currentElements.Count);
                foreach (var el in currentElements)
                {
                    var line = BuildLineItemFromElement(doc, el, csvRates, cobieCostCodes, measStd);
                    if (line != null) items.Add(line);
                }
                StoreHostCache(key, items, st);
                return items;
            }

            // ── Incremental ──────────────────────────────────────────────────
            var currentById = new Dictionary<long, Element>();
            foreach (var e in currentElements)
            {
                long id = e.Id?.Value ?? -1;
                if (id > 0) currentById[id] = e;
            }
            var currentIds = new HashSet<long>(currentById.Keys);

            var cachedById = new Dictionary<long, BOQLineItem>();
            foreach (var it in cached)
                if (it.RevitElementId > 0 && !cachedById.ContainsKey(it.RevitElementId))
                    cachedById[it.RevitElementId] = it;

            // Re-take-off = newly-added (in current, not cached) ∪ dirty-existing.
            var reTakeoff = new HashSet<long>();
            foreach (var id in currentIds) if (!cachedById.ContainsKey(id)) reTakeoff.Add(id);
            foreach (var id in dirtySnapshot) if (currentIds.Contains(id)) reTakeoff.Add(id);

            var result = new List<BOQLineItem>(cached.Count + reTakeoff.Count);
            foreach (var it in cached)
            {
                if (!currentIds.Contains(it.RevitElementId)) continue;   // removed → drop
                if (reTakeoff.Contains(it.RevitElementId)) continue;     // will rebuild
                result.Add(it.Clone());
            }
            int rebuilt = 0;
            foreach (var id in reTakeoff)
            {
                if (!currentById.TryGetValue(id, out var el) || el == null) continue;
                var line = BuildLineItemFromElement(doc, el, csvRates, cobieCostCodes, measStd);
                if (line != null) { result.Add(line); rebuilt++; }
            }
            StingLog.Info($"BOQ incremental host take-off: re-took-off {rebuilt} of {currentIds.Count} element(s) " +
                          $"(cache had {cachedById.Count}).");
            StoreHostCache(key, result, st);
            return result;
        }

        private static void StoreHostCache(string key, List<BOQLineItem> items, HostIncrementalState st)
        {
            // Store an isolated clone so a later caller mutating the returned rows
            // (aggregate / group / override) can never corrupt the cache.
            _hostTakeoffCache[key] = items.Select(i => i.Clone()).ToList();
            lock (st.Lock) { st.ForceFull = false; st.Dirty.Clear(); }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Phase 2E — WBS / CBS assignment. Map rule wins; else inherit the
        //  WBS of the 4D ScheduleTask the element belongs to (one breakdown
        //  structure shared by the 4D programme and the 5D bill).
        // ══════════════════════════════════════════════════════════════════
        private static void ApplyWbsCbs(Document doc, List<BOQLineItem> items)
        {
            if (items == null || items.Count == 0) return;
            try
            {
                var rules = BoqWbsMapStore.Load(doc)?.Rules ?? new List<BoqWbsRule>();
                var taskWbs = BuildElementTaskWbsIndex(doc);

                foreach (var item in items)
                {
                    string wbs = "", cbs = "";
                    foreach (var r in rules)
                    {
                        if (r.Matches(item)) { wbs = r.Wbs ?? ""; cbs = r.Cbs ?? ""; break; }
                    }

                    // Fallback — inherit WBS from the linked ScheduleTask.
                    if (string.IsNullOrEmpty(wbs) && taskWbs.Count > 0)
                    {
                        foreach (long id in EnumerateItemElementIds(item))
                        {
                            if (taskWbs.TryGetValue(id, out string tw) && !string.IsNullOrEmpty(tw)) { wbs = tw; break; }
                        }
                    }

                    item.WbsCode = wbs;
                    item.CbsCode = cbs;
                }
            }
            catch (Exception ex) { StingLog.Warn($"ApplyWbsCbs: {ex.Message}"); }
        }

        /// <summary>elementId → ScheduleTask.Wbs from the unified 4D schedule store.</summary>
        private static Dictionary<long, string> BuildElementTaskWbsIndex(Document doc)
        {
            var map = new Dictionary<long, string>();
            try
            {
                var model = StingTools.Core.Schedule.ScheduleStore.Load(doc);
                if (model?.Tasks == null) return map;
                foreach (var t in model.Tasks)
                {
                    if (string.IsNullOrEmpty(t?.Wbs) || t.ElementIds == null) continue;
                    foreach (long id in t.ElementIds) if (id > 0 && !map.ContainsKey(id)) map[id] = t.Wbs;
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildElementTaskWbsIndex: {ex.Message}"); }
            return map;
        }

        private static IEnumerable<long> EnumerateItemElementIds(BOQLineItem item)
        {
            if (item == null) yield break;
            if (item.ConstituentElementIds != null && item.ConstituentElementIds.Count > 0)
                foreach (long id in item.ConstituentElementIds) yield return id;
            else if (item.RevitElementId > 0)
                yield return item.RevitElementId;
        }

        private static string LinkSelectionPath(Document doc)
        {
            string parent = System.IO.Path.GetDirectoryName(doc?.PathName ?? "");
            if (string.IsNullOrEmpty(parent)) return null;   // unsaved doc — memory only
            return System.IO.Path.Combine(parent, "_BIM_COORD", "boq_links.json");
        }

        // P2.3 — per-link instance multiplier. boq_links.json now carries the
        // inclusion set AND an optional Title → multiply-by-instance-count map
        // (default off). A model legitimately placed twice (mirrored wings) can
        // be opted in to be taken off ×N. Back-compatible: a legacy bare array
        // is read as "included only, no multipliers".
        private sealed class LinksConfig
        {
            [Newtonsoft.Json.JsonProperty("included")]
            public List<string> Included { get; set; } = new List<string>();
            [Newtonsoft.Json.JsonProperty("multiply")]
            public Dictionary<string, bool> Multiply { get; set; }
                = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }

        // Per-doc multiply map cache (parallels _linkInclusionCache).
        private static readonly Dictionary<string, Dictionary<string, bool>> _linkMultiplyCache
            = new Dictionary<string, Dictionary<string, bool>>(StringComparer.OrdinalIgnoreCase);

        private static LinksConfig LoadLinksConfigRaw(Document doc)
        {
            var cfg = new LinksConfig();
            try
            {
                string path = LinkSelectionPath(doc);
                if (path == null || !System.IO.File.Exists(path)) return cfg;
                string json = System.IO.File.ReadAllText(path);
                string trimmed = json.TrimStart();
                if (trimmed.StartsWith("["))
                {
                    // Legacy format — bare array of titles.
                    var titles = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(json);
                    if (titles != null) cfg.Included = titles.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
                }
                else
                {
                    var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<LinksConfig>(json);
                    if (parsed != null)
                    {
                        cfg.Included = parsed.Included ?? new List<string>();
                        cfg.Multiply = parsed.Multiply ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadLinksConfigRaw: {ex.Message}"); }
            return cfg;
        }

        private static void SaveLinksConfig(Document doc, HashSet<string> included, Dictionary<string, bool> multiply)
        {
            try
            {
                string path = LinkSelectionPath(doc);
                if (path == null) return;
                var cfg = new LinksConfig
                {
                    Included = included.ToList(),
                    Multiply = multiply ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                };
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllText(path,
                    Newtonsoft.Json.JsonConvert.SerializeObject(cfg, Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn($"SaveLinksConfig: {ex.Message}"); }
        }

        /// <summary>Titles of loaded links included in the takeoff for this doc
        /// (loaded from boq_links.json on first ask, cached per doc path).</summary>
        internal static HashSet<string> GetIncludedLinkTitles(Document doc)
        {
            string key = doc?.PathName ?? "";
            lock (_linkInclusionCache)
            {
                if (_linkInclusionCache.TryGetValue(key, out var cached)) return cached;
                var cfg = LoadLinksConfigRaw(doc);
                var set = new HashSet<string>(
                    cfg.Included.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()),
                    StringComparer.OrdinalIgnoreCase);
                _linkInclusionCache[key] = set;
                lock (_linkMultiplyCache) { _linkMultiplyCache[key] = cfg.Multiply; }
                return set;
            }
        }

        /// <summary>P2.3 — Title → "multiply by instance count" opt-in map for this doc.</summary>
        internal static Dictionary<string, bool> GetLinkMultiplyMap(Document doc)
        {
            string key = doc?.PathName ?? "";
            lock (_linkMultiplyCache)
            {
                if (_linkMultiplyCache.TryGetValue(key, out var cached)) return cached;
            }
            // Force a load (populates both caches).
            GetIncludedLinkTitles(doc);
            lock (_linkMultiplyCache)
            {
                return _linkMultiplyCache.TryGetValue(key, out var m)
                    ? m : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>Persist the per-link inclusion set + refresh the cache (preserves the multiply map).</summary>
        internal static void SetIncludedLinkTitles(Document doc, IEnumerable<string> titles)
        {
            string key = doc?.PathName ?? "";
            var set = new HashSet<string>(
                (titles ?? Enumerable.Empty<string>()).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()),
                StringComparer.OrdinalIgnoreCase);
            var mult = GetLinkMultiplyMap(doc);
            lock (_linkInclusionCache) { _linkInclusionCache[key] = set; }
            SaveLinksConfig(doc, set, mult);
        }

        /// <summary>P2.3 — persist the per-link multiply opt-in map (preserves the inclusion set).</summary>
        internal static void SetLinkMultiplyMap(Document doc, Dictionary<string, bool> multiply)
        {
            string key = doc?.PathName ?? "";
            var map = multiply ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            lock (_linkMultiplyCache) { _linkMultiplyCache[key] = map; }
            SaveLinksConfig(doc, GetIncludedLinkTitles(doc), map);
        }

        internal static BOQDocument BuildBOQDocument(Document doc, IEnumerable<BOQLineItem> extraManualRows = null,
            BoqGroupingMode grouping = BoqGroupingMode.WorkSection, bool allowIncremental = false)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            // Phase 2D — suppress the dirty marker for the duration of the build so
            // the build's own STEP 9 stale-flag transaction doesn't self-dirty every
            // costed element (which would make the next incremental refresh re-walk
            // the whole model). Single API thread ⇒ no user edit is suppressed.
            bool prevSuppress = _suppressDirty;
            _suppressDirty = true;
            try
            {
            return BuildBOQDocumentCore(doc, extraManualRows, grouping, allowIncremental);
            }
            finally { _suppressDirty = prevSuppress; }
        }

        private static BOQDocument BuildBOQDocumentCore(Document doc, IEnumerable<BOQLineItem> extraManualRows,
            BoqGroupingMode grouping, bool allowIncremental)
        {
            var boq = new BOQDocument
            {
                ProjectName = ReadProjectName(doc),
                DocumentTitle = ReadProjectDocumentTitle(doc),
                SnapshotLabel = "Live",
                SnapshotType = "Live",
                SnapshotDate = DateTime.UtcNow
            };

            // ── STEP 1: Load config ──────────────────────────────────────
            // WP1 — markup % come from the tender-config store (BOQ_TENDER_*),
            // the SAME keys BOQProfessionalExportCommand reads, so the panel KPI
            // and the professional workbook's Contract Sum reconcile to one
            // number. The legacy COST_* keys remain as a back-compat fallback.
            boq.PrelimPct = TagConfig.GetConfigDouble("BOQ_TENDER_PRELIMINARIES_PCT",
                              TagConfig.GetConfigDouble("COST_PRELIMINARIES_PCT", 12.0));
            boq.ContingencyPct = TagConfig.GetConfigDouble("BOQ_TENDER_CONTINGENCY_PCT",
                              TagConfig.GetConfigDouble("COST_CONTINGENCY_PCT", 10.0));
            boq.OverheadPct = TagConfig.GetConfigDouble("BOQ_TENDER_OHP_PCT",
                              TagConfig.GetConfigDouble("COST_OVERHEAD_PROFIT_PCT", 8.0));
            boq.VatPct = TagConfig.GetConfigDouble("BOQ_TENDER_VAT_PCT", 18.0);
            boq.ExchangeRateUgxPerUsd = TagConfig.GetConfigDouble("BOQ_TENDER_UGX_PER_USD",
                              TagConfig.GetConfigDouble("UGX_PER_USD", 3700.0));
            boq.ProjectBudgetUGX = ReadProjectBudget(doc);

            // Phase 2A — active measurement standard (project_config.json key set
            // by Cost_SetMeasurementStandard). Drives rules-based measurement
            // (NRM2 vs CESMM4 give different nets) + classification + description.
            boq.MeasurementStandardId = TagConfig.GetConfigValue("COST_MEASUREMENT_STANDARD");
            if (string.IsNullOrWhiteSpace(boq.MeasurementStandardId)) boq.MeasurementStandardId = "nrm2";
            var measStd = MeasurementStandardRegistry.Get(boq.MeasurementStandardId);
            // Phase 2A — drop the per-document void index so this take-off re-reads
            // current floor/roof/ceiling openings (and link openings).
            MeasurementDeductionEngine.ResetCaches();

            // G3 — itemised preliminaries schedule (flat % stays the default).
            var prelims = BoqPrelimsStore.Load(doc);
            boq.PrelimsItemised = prelims.Enabled;
            boq.PrelimLines = prelims.Lines ?? new List<BoqPrelimLine>();

            // ── STEP 2: Load rate tables (3-source merge) ────────────────
            //   (a) project cost_rates_5d.csv  — highest priority
            //   (b) COBie type map             — category → cost-rate code
            //   (c) Scheduling4DEngine defaults — lowest priority
            Dictionary<string, (double rate, string unit)> csvRates = LoadCsvRates();
            Dictionary<string, string> cobieCostCodes = LoadCobieCostCodes();

            // ── STEP 3: Load embodied carbon factors ─────────────────────
            CarbonTrackingEngine.EnsureLoaded();
            // G5 — drop the cached EPD map so an edit to boq_epd_map.json is
            // picked up on this Refresh.
            BoqEpdStore.Invalidate(doc);

            // ── STEP 4 + 5: Collect + cost host elements ─────────────────
            // Phase 2D — full walk, or incremental (dirty + added only) when the
            // panel asks and a trusted cache exists. The result is the SAME raw
            // host item set a full walk would produce (correct by construction);
            // STEP 6 onward runs identically either way.
            var knownCats = new HashSet<string>(TagConfig.DiscMap.Keys, StringComparer.OrdinalIgnoreCase);
            var items = BuildHostRawItems(doc, knownCats, csvRates, cobieCostCodes, measStd, allowIncremental);

            // ── STEP 6: Merge manual + PS rows ───────────────────────────
            var manualStore = LoadManualStore(doc);
            if (manualStore?.ManualRows != null)
                items.AddRange(manualStore.ManualRows.Select(r => r.Clone()));
            if (extraManualRows != null)
                items.AddRange(extraManualRows.Select(r => r.Clone()));

            // ── STEP 6b (P1.2): Aggregate near-identical modelled rows ───
            // Collapse 7 identical showers into 1 row (Qty 7), preserving the
            // constituent element ids for drill-down. Manual/PS rows pass
            // through untouched. Toggle off via COST_AGGREGATE_SIMILAR=false.
            // The grouping mode feeds the aggregation key (P2.2) so similar
            // items collapse within the active spatial dimension.
            items = AggregateLineItems(items, grouping);

            // ── STEP 6c: Linked-model takeoff (opt-in) ───────────────────
            // Quantify elements in loaded Revit links when enabled. Linked rows
            // are READ-ONLY for cost write-back: a link element's id can collide
            // with a host id and stamp the wrong element, so RevitElementId is
            // forced to -1 and IfcQuantitySetWriter / CostStamp / select-in-Revit
            // all skip them — they contribute quantity + cost + carbon only,
            // tagged "[Linked: <model>]".
            var includedLinks = GetIncludedLinkTitles(doc);
            if (includedLinks.Count > 0)
            {
                try
                {
                    var linkItems = CollectLinkedItems(doc, knownCats, csvRates, cobieCostCodes, grouping, includedLinks, measStd);
                    if (linkItems.Count > 0) items.AddRange(linkItems);
                }
                catch (Exception ex) { StingLog.Warn($"BOQ linked-model takeoff: {ex.Message}"); }
            }

            // ── STEP 6d (Phase 2E): assign user-defined WBS / CBS ────────
            // From the WBS map (boq_wbs_map.json), falling back to the linked 4D
            // ScheduleTask's WBS. Runs before grouping so the Wbs / Cbs grouping
            // modes can file the bill by the client's breakdown structure. Applied
            // on every build so a map edit takes effect without a cache rebuild.
            ApplyWbsCbs(doc, items);

            // ── STEP 7: Group into sections ──────────────────────────────
            boq.Sections = GroupIntoSections(items, grouping);

            // ── STEP 7b (Phase 108f): apply persisted model-row overrides.
            // This runs AFTER the full line-item list is assembled so rate +
            // description + note survive BuildBOQDocument rebuilds regardless
            // of whether the background CST_RATE_SOURCE write completed.
            ApplyModelOverrides(doc, boq);

            // ── STEP 8: Assign BOQ line refs across the whole document ───
            AssignBoqLineRefs(boq);

            // ── STEP 9 (N+9): Clear ASS_CST_STALE_BOOL on elements that
            //                  have just been re-costed. The flag was set by
            //                  StingStaleMarker on material change; now that
            //                  this row has its fresh rate + carbon, the
            //                  flag stops being true. Count the refresh so
            //                  the BOQ dashboard can surface it.
            boq.StaleRowsRefreshed = ClearStaleFlagsForCostedRows(doc, boq);

            return boq;
        }

        /// <summary>
        /// N+9 — On every BOQ build, any element whose row has now been
        /// re-costed clears its ASS_CST_STALE_BOOL = "1" flag (set by
        /// StingStaleMarker on a previous material change). Returns the
        /// number of elements whose flag was cleared so the BOQ
        /// dashboard can colour-banner the refresh.
        ///
        /// Caller owns the transaction. Falls back gracefully when the
        /// parameter isn't bound on the project.
        /// </summary>
        private static int ClearStaleFlagsForCostedRows(Document doc, BOQDocument boq)
        {
            if (doc == null || boq == null) return 0;
            int cleared = 0;
            try
            {
                using (var t = new Transaction(doc, "STING BOQ Clear Stale Flags"))
                {
                    t.Start();
                    foreach (var item in boq.AllItems)
                    {
                        if (item.RevitElementId < 0) continue;
                        try
                        {
                            var el = doc.GetElement(new ElementId(item.RevitElementId));
                            if (el == null) continue;
                            var p = el.LookupParameter("ASS_CST_STALE_BOOL");
                            if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) continue;
                            string cur = p.AsString();
                            if (string.Equals(cur, "1", StringComparison.Ordinal))
                            {
                                p.Set("0");
                                cleared++;
                            }
                        }
                        catch (Exception ex) { StingLog.WarnRateLimited("ClearStale", $"ClearStale {item.RevitElementId}: {ex.Message}"); }
                    }
                    t.Commit();
                }
                if (cleared > 0) StingLog.Info($"BOQ build: refreshed {cleared} stale element row(s).");
            }
            catch (Exception ex) { StingLog.Warn($"ClearStaleFlagsForCostedRows: {ex.Message}"); }
            return cleared;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Per-element line builder — pipeline adapted from
        //  SchedulingCommands.ElementCostTraceCommand so rate-source precedence
        //  and quantity derivation stay consistent across the codebase.
        // ══════════════════════════════════════════════════════════════════

        private static BOQLineItem BuildLineItemFromElement(Document doc, Element el,
            Dictionary<string, (double rate, string unit)> csvRates,
            Dictionary<string, string> cobieCostCodes,
            IMeasurementStandard std = null)
        {
            string catName = ParameterHelpers.GetCategoryName(el);
            if (string.IsNullOrEmpty(catName)) return null;

            // Skip phase-demolished or temporary elements — they don't belong in the cost plan.
            if (IsPhaseDemolished(doc, el)) return null;

            // (a) Rate lookup — CSV by category → CSV by PROD code → COBie type map → default
            string rateSource;
            int rateConfidence;
            (double rate, string unit, string description) picked = ResolveRate(
                doc, el, catName, csvRates, cobieCostCodes, out rateSource, out rateConfidence,
                out double? splitLabour, out double? splitPlant, out double? splitMaterial);
            if (picked.rate <= 0) rateConfidence = Math.Max(20, rateConfidence); // confidence floor for zero-rate rows

            string unit = string.IsNullOrEmpty(picked.unit) ? "each" : picked.unit;

            // Phase 2A — rules-based measurement. Gross modelled geometry →
            // standard deductions (NRM2/CESMM4 openings & voids) → visible
            // wastage step → net measured quantity. Raw geometry survives in
            // GrossQuantity; the gross→net derivation is captured in
            // MeasurementNote for the audit surface. Falls back to the legacy
            // DeriveQuantity (gross × waste) when no standard is supplied.
            double quantity;
            double grossQty = 0, deductQty = 0, wasteQty = 0;
            string measNote = null;
            if (std != null)
            {
                quantity = MeasureQuantity(el, unit, catName, std,
                    out grossQty, out deductQty, out wasteQty, out measNote);
            }
            else
            {
                quantity = DeriveQuantity(el, unit);
                grossQty = quantity;
            }

            // (b) Currency — CA-1: RateUSD is derived from the SAME doc-scoped FX
            // (UGX_PER_USD) the rate registry used to rebase, via the one converter,
            // so the UGX and USD figures on a row are never inconsistent.
            double exchangeRate = TagConfig.GetConfigDouble("UGX_PER_USD", 3700.0);
            double ugxPerGbp = TagConfig.GetConfigDouble("UGX_PER_GBP", 4700.0);
            double rateUgx = picked.rate;
            double rateUsd = Math.Round(
                StingTools.BOQ.Rates.RateCurrency.FromUgx(rateUgx, "USD", exchangeRate, ugxPerGbp), 2);

            // (c) NRM2 paragraph — prefer the previously-resolved value on the element,
            //      then a template resolution, then a safe fallback.
            string paragraph = ResolveNrm2Paragraph(doc, el, catName);

            // (d) Embodied carbon — WP-C: A1-A3 FOSSIL headline + separate biogenic
            // line (RICS WLCA) + estimated flag (+ G5 data-quality + source + material)
            double carbonKg = ComputeElementCarbonSplit(el, quantity, unit,
                out string carbonSource, out string carbonQuality, out string carbonMaterial,
                out double biogenicKg, out bool carbonEstimated);

            // (e) Lifecycle cost (capital + simple NPV maintenance)
            double lifecycleUgx = ComputeLifecycleCost(rateUgx * quantity, catName);
            // CA-4 — TRUE whole-life cost: fold the monetised embodied carbon
            // (carbon price × A1-A3) into the LCC. Operational carbon is
            // building-level (added by the EDGE LCC), so 0 here. Zero-impact
            // until COST_CARBON_PRICE_UGX_PER_KG is set.
            double lifecycleInclCarbonUgx = StingTools.Core.Sustainability.CarbonLcc
                .LifecycleCostInclCarbonUgx(lifecycleUgx, carbonKg, 0,
                    CarbonPriceUgxPerKg(), LifecycleYears, LifecycleDiscountRate * 100.0);

            string disc = ResolveDiscipline(el, catName);
            string nrm2Section = DeriveNrm2Section(doc, el, catName, disc);
            string sectionName = picked.description;
            if (string.IsNullOrEmpty(sectionName)) sectionName = catName;

            var line = new BOQLineItem
            {
                NRM2Section = nrm2Section,
                Category = catName,
                Discipline = disc,
                ItemName = GetElementDisplayName(el),
                FamilyName = GetFamilyName(el),
                TypeName = el.Name ?? "",
                Quantity = quantity,
                Unit = unit,
                GrossQuantity = grossQty,
                DeductionQuantity = deductQty,
                WastageQuantity = wasteQty,
                MeasurementNote = measNote,
                RateUGX = rateUgx,
                RateUSD = rateUsd,
                EmbodiedCarbonKg = carbonKg,
                BiogenicKg = biogenicKg,
                CarbonEstimated = carbonEstimated,
                LifecycleCostUGX = lifecycleUgx,
                LifecycleCostInclCarbonUGX = lifecycleInclCarbonUgx,
                ResolvedNRM2Paragraph = paragraph,
                Note = "",
                Source = BOQRowSource.Model,
                SnapshotRef = "",
                RevitElementId = el.Id?.Value ?? -1,
                UniqueId = el.UniqueId,
                Level = GetLevelName(doc, el),
                Location = GetLocationName(doc, el),
                Zone = GetZoneName(el),
                LastCosted = DateTime.UtcNow,
                RateSource = rateSource,
                RateConfidence = rateConfidence,
                LabourUGX = splitLabour,     // G4 — L/P/M split (null when source gives none)
                PlantUGX = splitPlant,
                MaterialUGX = splitMaterial,
                CarbonSource = carbonSource, // G5 — carbon factor provenance + quality
                CarbonQuality = carbonQuality,
                CarbonMaterial = carbonMaterial
            };

            // Mark provisional sums on the element if configured via existing parameter.
            bool isPS = ParameterHelpers.GetInt(el, "CST_PROVISIONAL_SUM", 0) == 1;
            if (isPS) line.Source = BOQRowSource.ProvisionalSum;

            return line;
        }

        // ── Published per-element cost seam (P0-7) ─────────────────────────
        //
        // Other subsystems (4D/5D cash-flow, BIMManager 5D export, COBie
        // replacement-cost fallback) must NOT run their own take-off + qty×rate.
        // They acquire an ElementCostContext once, then call CostElement per
        // element to get a BOQLineItem costed by the EXACT canonical procedure
        // (ResolveRate → MeasureQuantity → carbon). This is the single source of
        // truth the consolidation-invariant tests pin.

        /// <summary>
        /// Per-document costing context: the rate tables + active measurement
        /// standard, loaded once. Mirrors the STEP 1-3 setup BuildBOQDocument
        /// performs so a per-element cost matches a whole-bill cost exactly.
        /// </summary>
        internal sealed class ElementCostContext
        {
            public Dictionary<string, (double rate, string unit)> CsvRates;
            public Dictionary<string, string> CobieCostCodes;
            public IMeasurementStandard Std;

            public static ElementCostContext Build(Document doc)
            {
                string stdId = TagConfig.GetConfigValue("COST_MEASUREMENT_STANDARD");
                if (string.IsNullOrWhiteSpace(stdId)) stdId = "nrm2";
                // Same load + cache-prime as BuildBOQDocumentCore so carbon /
                // deductions resolve identically on the per-element path.
                MeasurementDeductionEngine.ResetCaches();
                CarbonTrackingEngine.EnsureLoaded();
                try { BoqEpdStore.Invalidate(doc); } catch (Exception ex) { StingLog.Warn($"ElementCostContext EPD: {ex.Message}"); }
                return new ElementCostContext
                {
                    CsvRates = LoadCsvRates(),
                    CobieCostCodes = LoadCobieCostCodes(),
                    Std = MeasurementStandardRegistry.Get(stdId)
                };
            }
        }

        /// <summary>
        /// Cost a single element through the canonical procedure. Returns null
        /// for elements that carry no cost (no category / phase-demolished).
        /// Identical to the per-element row BuildBOQDocument produces (pre
        /// aggregation), so consumers can read line.TotalUGX / Quantity / Unit
        /// without re-implementing a take-off.
        /// </summary>
        internal static BOQLineItem CostElement(Document doc, Element el, ElementCostContext ctx)
        {
            if (doc == null || el == null) return null;
            var c = ctx ?? ElementCostContext.Build(doc);
            return BuildLineItemFromElement(doc, el, c.CsvRates, c.CobieCostCodes, c.Std);
        }

        /// <summary>
        /// Project a built BOQDocument's modelled + manual + PS line items into
        /// the Document-free <see cref="Boq5DRow"/> rows the 5D estimate
        /// assembler consumes. The canonical per-line total (TotalUGX) flows
        /// through verbatim — no qty×rate is recomputed downstream.
        /// </summary>
        internal static List<Boq5DRow> ProjectTo5DRows(BOQDocument boq)
        {
            var rows = new List<Boq5DRow>();
            if (boq == null) return rows;
            foreach (var it in boq.AllItems)
            {
                rows.Add(new Boq5DRow
                {
                    Category = it.Category ?? "",
                    Discipline = it.Discipline ?? "",
                    Quantity = it.Quantity,
                    Unit = it.Unit,
                    RateUgx = it.RateUGX,
                    LineTotalUgx = it.TotalUGX,
                    Description = string.IsNullOrEmpty(it.ItemName) ? it.Category : it.ItemName
                });
            }
            return rows;
        }

        // ── Rate resolution ────────────────────────────────────────────────

        private static (double rate, string unit, string description) ResolveRate(
            Document doc, Element el, string catName,
            Dictionary<string, (double rate, string unit)> csvRates,
            Dictionary<string, string> cobieCostCodes,
            out string rateSource, out int rateConfidence,
            out double? splitLabour, out double? splitPlant, out double? splitMaterial)
        {
            splitLabour = splitPlant = splitMaterial = null;
            // P0 refactor — delegate to the pluggable rate-provider chain.
            // The 5 legacy passes are now individual providers registered
            // with RateProviderRegistry; behaviour is preserved while
            // allowing new providers (BCIS, Spon's, project rate card) to
            // slot in without editing this method.
            double ugxPerUsd = TagConfig.GetConfigDouble("UGX_PER_USD", 3700.0);
            double ugxPerGbp = TagConfig.GetConfigDouble("UGX_PER_GBP", 4700.0);

            var registry = RateProviderRegistry.Get(doc, csvRates, cobieCostCodes, ugxPerUsd, ugxPerGbp);
            var req = new RateRequest
            {
                CategoryName = catName ?? "",
                Discipline = ResolveDiscipline(el, catName),
                ProdCode = ParameterHelpers.GetString(el, ParamRegistry.PROD) ?? "",
                MatCode = ParameterHelpers.GetString(el, "MAT_CODE") ?? "",
                Unit = csvRates != null && csvRates.TryGetValue(catName ?? "", out var hint) ? hint.unit : "",
                CurrencyCode = "UGX",
                AsOf = DateTime.UtcNow,
                Element = el
            };

            var lookup = registry.Resolve(req);
            if (lookup == null || lookup.UnitRate <= 0)
            {
                rateSource = "None";
                rateConfidence = 20;
                return (0, "each", catName);
            }

            // Map provider id back to the legacy RateSource label so the
            // rest of the codebase (heat-map, schedules, exports) keeps
            // working without changes.
            rateSource = MapProviderIdToLegacySource(lookup.SourceId);
            rateConfidence = lookup.Confidence;
            splitLabour = lookup.LabourRate;     // G4 — propagate optional L/P/M split
            splitPlant = lookup.PlantRate;
            splitMaterial = lookup.MaterialRate;
            return (lookup.UnitRate, lookup.Unit, lookup.MatchedKey ?? catName);
        }

        // Normalises unit strings so a CSV "m²" matches a rule's "m2".
        // Returns true when the units denote the same quantity dimension.
        private static bool UnitsAlign(string ruleUnit, string callerUnit)
        {
            string a = NormaliseUnit(ruleUnit);
            string b = NormaliseUnit(callerUnit);
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        // internal so IfcQuantitySetWriter (same assembly) can canonicalise
        // units against the same table — avoids the m²/m2 glyph mismatch that
        // silently zeroed every exported Qto (P0-1).
        internal static string NormaliseUnit(string u)
        {
            if (string.IsNullOrEmpty(u)) return "";
            string s = u.Trim().ToLowerInvariant();
            switch (s)
            {
                case "m²": case "sqm": case "m2": return "m2";
                case "m³": case "cum": case "m3": return "m3";
                case "lm": case "lin-m": case "linear-m": case "m": return "m";
                case "tonne": case "tonnes": case "t": case "kg": return "kg";
                case "no": case "nr": case "item": case "each": case "ea": return "each";
                default: return s;
            }
        }

        // Legacy RateSource labels — preserved so heat-maps and schedules built
        // against the old shape keep working. PM-7: delegates to the one shared
        // map (Rates.RateSourceLabels) so CostStamp can't drift from this.
        private static string MapProviderIdToLegacySource(string providerId)
            => StingTools.BOQ.Rates.RateSourceLabels.ToLegacy(providerId);

        // ── Quantity derivation ────────────────────────────────────────────
        // Adapted from SchedulingCommands.ElementCostTraceCommand.DeriveQuantity
        // so cost totals exactly match the existing 5D Cost Trace output.

        private static double DeriveQuantity(Element el, string unit)
        {
            // P0 refactor — first consult the data-driven TakeoffRuleRegistry.
            // When a rule matches AND its declared unit aligns with the
            // caller's requested unit, the rule's quantitySource +
            // unitConversion drive the quantity. If the units disagree we
            // fall back to legacy logic so a CSV rate at "each" cannot be
            // accidentally combined with an area quantity.
            try
            {
                Document doc = el?.Document;
                if (doc != null)
                {
                    string catName = ParameterHelpers.GetCategoryName(el);
                    string disc = ResolveDiscipline(el, catName);
                    string prod = ParameterHelpers.GetString(el, ParamRegistry.PROD) ?? "";
                    var rule = TakeoffRuleRegistry.Get(doc).Match(catName, disc, prod);
                    if (rule != null && UnitsAlign(rule.Unit, unit))
                    {
                        double q = TakeoffRuleRegistry.EvaluateQuantity(el, rule);
                        // Apply rule-level wastage (P0 reserves; full waste
                        // pipeline lands in P5.2 once star-rates use it).
                        if (rule.WastePercent > 0)
                            q *= 1.0 + rule.WastePercent / 100.0;
                        return q;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"DeriveQuantity rule lookup: {ex.Message}"); }

            // Legacy fallback — preserved verbatim for back-compat, except
            // Z-21: a wastage allowance is now applied to genuinely-measured
            // quantities so the fallback path stops under-quantifying (audit
            // §6.3 — waste was previously applied only on the TakeoffRule path).
            // Z-21b: waste is single-surface — applied to the QUANTITY only,
            // never the rate (the ES rate-override no longer inflates the rate
            // by WastePercent — see RateProviders ExtensibleStorageRateProvider).
            // An explicit per-element StingCostRateOverride.WastePercent wins
            // here (honoured on the quantity side); otherwise the project knob
            // COST_DEFAULT_WASTE_PCT (default 5%). Applied via WasteFactor.Apply
            // only to measured material units — never to "each"/"item" counts or
            // the 1.0 "couldn't-measure" placeholders.
            try
            {
                double overrideWaste = 0;
                try { overrideWaste = StingCostRateOverrideSchema.Read(el)?.WastePercent ?? 0; }
                catch (Exception exr) { StingLog.WarnRateLimited("DeriveQuantity.OvrWaste", $"override waste read: {exr.Message}"); }
                // PM-5 — per-material/category waste table: override wins, else the
                // NRM2-typical allowance for this category (rebar 2.5 / timber 10 /
                // tiling 10 …), else the project default knob. Same table the carbon
                // path resolves through, so quantity is grossed up identically.
                double wastePct = WasteTable.ResolveWastePercent(
                    null, el.Category?.Name, overrideWaste,
                    TagConfig.GetConfigDouble("COST_DEFAULT_WASTE_PCT", 5.0));

                // Z-23b — discipline-specific MEASURED ADDITIONS, SEPARATE from the
                // general waste above. NRM2: rebar laps are a measured addition to
                // reinforcement mass (not waste); concrete over-order is a
                // procurement buffer. Both knobs DEFAULT 0 (off) → no change until a
                // project opts in. When enabled they are summed with wastePct and
                // applied ONCE (MeasuredAddition.GrossUp) — never a second waste pass.
                double rebarLap = MeasuredAddition.RebarLapPercent(
                    IsRebarElement(el), TagConfig.GetConfigDouble("REBAR_LAP_ALLOWANCE_PCT", 0.0));
                double concreteBuffer = MeasuredAddition.ConcreteOverOrderPercent(
                    IsConcreteElement(el), TagConfig.GetConfigDouble("CONCRETE_OVERORDER_PCT", 0.0));

                switch ((unit ?? "").ToLowerInvariant())
                {
                    case "m²":
                    case "m2":
                    case "sqm":
                        Parameter areaP = el.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                        if (areaP != null && areaP.HasValue)
                            return WasteFactor.Apply(areaP.AsDouble() * 0.092903, unit, wastePct); // ft² → m²
                        return MeasuredFallback(unit);
                    case "m³":
                    case "m3":
                    case "cum":
                        Parameter volP = el.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
                        if (volP != null && volP.HasValue)
                            // waste + (opt-in) concrete over-order buffer, once.
                            return MeasuredAddition.GrossUp(volP.AsDouble() * 0.0283168, unit, wastePct, concreteBuffer); // ft³ → m³
                        return MeasuredFallback(unit);
                    case "m":
                        if (el.Location is LocationCurve lc)
                            return WasteFactor.Apply(lc.Curve.Length * 0.3048, unit, wastePct); // ft → m
                        Parameter lenP = el.LookupParameter("Length")
                            ?? el.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                        if (lenP != null && lenP.HasValue)
                            return WasteFactor.Apply(lenP.AsDouble() * 0.3048, unit, wastePct);
                        return MeasuredFallback(unit);
                    case "kg":
                    case "tonne":
                    case "tonnes":
                        Parameter massP = el.LookupParameter("Weight") ?? el.LookupParameter("Mass");
                        if (massP != null && massP.HasValue)
                            // waste + (opt-in) rebar lap allowance, once.
                            return MeasuredAddition.GrossUp(massP.AsDouble(), unit, wastePct, rebarLap);
                        return MeasuredFallback(unit);
                    default:
                        return MeasuredFallback(unit);
                }
            }
            catch (Exception ex) { StingLog.Warn($"DeriveQuantity({unit}): {ex.Message}"); return MeasuredFallback(unit); }
        }

        /// <summary>
        /// WP2 — the "could not measure" fallback. MEASURED units (m/m²/m³/kg)
        /// return 0 (a visible sentinel the uncosted-at-risk rollup catches and
        /// the build down-grades to low confidence) instead of a fake 1.0; count
        /// units ('each'/'item'/'nr') legitimately stay 1.
        /// </summary>
        private static double MeasuredFallback(string unit)
        {
            switch ((unit ?? "").Trim().ToLowerInvariant())
            {
                case "each": case "item": case "nr": case "no": case "": return 1.0;
                default: return 0.0;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Phase 2A — rules-based measurement pipeline.
        //  Gross geometry → standard deductions (openings/voids) → visible
        //  wastage step → net measured quantity, with an auditable
        //  gross→net derivation note. The net is the value used for cost;
        //  the gross is preserved so nothing is lost.
        // ══════════════════════════════════════════════════════════════════

        internal static double MeasureQuantity(Element el, string unit, string catName,
            IMeasurementStandard std, out double gross, out double deduction,
            out double wastage, out string note)
        {
            gross = DeriveGrossQuantity(el, unit);
            deduction = 0; wastage = 0; note = null;
            double net = gross;
            try
            {
                // 1. Standard deductions (NRM2/CESMM4 openings & voids).
                double netOfDeductions = gross;
                if (std != null && el != null)
                {
                    var scratch = new BOQLineItem
                    {
                        Quantity = gross,
                        Unit = unit,
                        Category = catName,
                        Discipline = DisciplineForCategory(catName)
                    };
                    netOfDeductions = std.ApplyDeductions(scratch, el);
                    if (double.IsNaN(netOfDeductions) || netOfDeductions < 0) netOfDeductions = gross;
                }
                deduction = Math.Max(0, gross - netOfDeductions);

                // 2. Wastage — a distinct, visible step (never folded into the
                //    rate), applied once to the post-deduction base. Mirrors the
                //    existing DeriveQuantity waste + measured-addition behaviour.
                double wastePct = EffectiveWastePercent(el, unit, catName, std);
                double grossedUp = wastePct > 0
                    ? netOfDeductions * (1.0 + wastePct / 100.0)
                    : netOfDeductions;
                wastage = Math.Max(0, grossedUp - netOfDeductions);
                net = grossedUp;

                string measureLabel = ResolveMeasureLabel(el, catName, std);
                note = BuildMeasurementNote(measureLabel, unit, gross, deduction, wastePct, net);
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("MeasureQuantity", $"MeasureQuantity({unit}): {ex.Message}");
                net = gross; deduction = 0; wastage = 0; note = null;
            }
            return net;
        }

        /// <summary>
        /// Raw modelled geometry for a unit — the SAME geometry resolution as
        /// DeriveQuantity but WITHOUT the wastage / measured-addition gross-up
        /// (which the measurement pipeline applies as a separate visible step).
        /// </summary>
        private static double DeriveGrossQuantity(Element el, string unit)
        {
            // Data-driven take-off rule path — raw EvaluateQuantity, no waste.
            try
            {
                Document doc = el?.Document;
                if (doc != null)
                {
                    string catName = ParameterHelpers.GetCategoryName(el);
                    string disc = ResolveDiscipline(el, catName);
                    string prod = ParameterHelpers.GetString(el, ParamRegistry.PROD) ?? "";
                    var rule = TakeoffRuleRegistry.Get(doc).Match(catName, disc, prod);
                    if (rule != null && UnitsAlign(rule.Unit, unit))
                        return TakeoffRuleRegistry.EvaluateQuantity(el, rule);
                }
            }
            catch (Exception ex) { StingLog.Warn($"DeriveGrossQuantity rule lookup: {ex.Message}"); }

            // Legacy geometry — raw measure, no waste.
            try
            {
                switch ((unit ?? "").ToLowerInvariant())
                {
                    case "m²": case "m2": case "sqm":
                        Parameter areaP = el.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                        return (areaP != null && areaP.HasValue) ? areaP.AsDouble() * 0.092903 : MeasuredFallback(unit);
                    case "m³": case "m3": case "cum":
                        Parameter volP = el.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
                        if (volP != null && volP.HasValue && volP.AsDouble() > 0) return volP.AsDouble() * 0.0283168;
                        // WP2 — host volume is often empty on framing/columns; fall
                        // back to true solid geometry (m³) before the sentinel.
                        double geomM3 = ReadGeometryVolumeM3(el);
                        return geomM3 > 0 ? geomM3 : MeasuredFallback(unit);
                    case "m":
                        if (el.Location is LocationCurve lc) return lc.Curve.Length * 0.3048;
                        Parameter lenP = el.LookupParameter("Length")
                            ?? el.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                        return (lenP != null && lenP.HasValue) ? lenP.AsDouble() * 0.3048 : MeasuredFallback(unit);
                    case "kg": case "tonne": case "tonnes":
                        Parameter massP = el.LookupParameter("Weight") ?? el.LookupParameter("Mass");
                        return (massP != null && massP.HasValue) ? massP.AsDouble() : MeasuredFallback(unit);
                    default:
                        return MeasuredFallback(unit);
                }
            }
            catch (Exception ex) { StingLog.Warn($"DeriveGrossQuantity({unit}): {ex.Message}"); return MeasuredFallback(unit); }
        }

        /// <summary>
        /// The effective wastage % to apply (matches DeriveQuantity's existing
        /// behaviour, plus the optional per-category override from the NRM2/CESMM4
        /// measurement-rules JSON). Priority: measurement-rule pinned % →
        /// take-off-rule % → legacy override/COST_DEFAULT_WASTE_PCT + opt-in
        /// measured additions (rebar lap on mass, concrete over-order on volume).
        /// </summary>
        private static double EffectiveWastePercent(Element el, string unit, string catName, IMeasurementStandard std)
        {
            try
            {
                Document doc = el?.Document;
                string disc = ResolveDiscipline(el, catName);

                // 1. Measurement-rule per-category wastage wins when pinned (>= 0).
                if (doc != null && std != null)
                {
                    try
                    {
                        var reg = MeasurementRuleRegistry.Get(doc, std.Id);
                        var mrule = reg.Match(catName, disc, null);
                        if (mrule != null && mrule.WastePercent >= 0) return mrule.WastePercent;
                    }
                    catch (Exception exr) { StingLog.WarnRateLimited("EffWaste.Rule", $"meas-rule waste: {exr.Message}"); }
                }

                // 2. Take-off rule wastage (the existing data-driven path).
                if (doc != null)
                {
                    string prod = ParameterHelpers.GetString(el, ParamRegistry.PROD) ?? "";
                    var trule = TakeoffRuleRegistry.Get(doc).Match(catName, disc, prod);
                    if (trule != null && UnitsAlign(trule.Unit, unit)) return trule.WastePercent;
                }

                // 3. Legacy fallback — measured units only.
                if (!WasteFactor.AppliesTo(unit)) return 0;
                double overrideWaste = 0;
                try { overrideWaste = StingCostRateOverrideSchema.Read(el)?.WastePercent ?? 0; }
                catch (Exception exr) { StingLog.WarnRateLimited("EffWaste.Ovr", $"override waste: {exr.Message}"); }
                // PM-5 — per-material/category waste table (catName is in scope).
                double wastePct = WasteTable.ResolveWastePercent(
                    null, catName, overrideWaste,
                    TagConfig.GetConfigDouble("COST_DEFAULT_WASTE_PCT", 5.0));

                string nu = (unit ?? "").ToLowerInvariant();
                if (nu == "kg" || nu == "tonne" || nu == "tonnes")
                    wastePct += MeasuredAddition.RebarLapPercent(
                        IsRebarElement(el), TagConfig.GetConfigDouble("REBAR_LAP_ALLOWANCE_PCT", 0.0));
                else if (nu == "m³" || nu == "m3" || nu == "cum")
                    wastePct += MeasuredAddition.ConcreteOverOrderPercent(
                        IsConcreteElement(el), TagConfig.GetConfigDouble("CONCRETE_OVERORDER_PCT", 0.0));
                return wastePct;
            }
            catch (Exception ex) { StingLog.WarnRateLimited("EffWaste", $"EffectiveWastePercent: {ex.Message}"); return 0; }
        }

        /// <summary>
        /// Slice 3 — the NRM2 measurement-point label for a category, so the
        /// audit note reads "Centre-line 12.40 m" / "Running girth 8.30 m" /
        /// "Area 43.0 m²" rather than a bare "Gross". Resolved from the active
        /// standard's measurement rule; defaults to "Gross" when no rule matches.
        /// </summary>
        private static string ResolveMeasureLabel(Element el, string catName, IMeasurementStandard std)
        {
            try
            {
                if (el?.Document == null || std == null) return "Gross";
                var reg = MeasurementRuleRegistry.Get(el.Document, std.Id);
                var rule = reg.Match(catName, DisciplineForCategory(catName), null);
                if (rule == null) return "Gross";
                switch ((rule.Measure ?? "").ToLowerInvariant())
                {
                    case "length": return "Centre-line";
                    case "girth":  return "Running girth";
                    case "area":   return "Area";
                    case "volume": return "Volume";
                    default:       return "Gross";
                }
            }
            catch { return "Gross"; }
        }

        private static string BuildMeasurementNote(string measureLabel, string unit,
            double gross, double deduction, double wastePct, double net)
        {
            string u = unit ?? "";
            string lbl = string.IsNullOrEmpty(measureLabel) ? "Gross" : measureLabel;
            var sb = new StringBuilder();
            sb.Append($"{lbl} {gross:0.##} {u}");
            if (deduction > 0.0005) sb.Append($" − openings/voids {deduction:0.##} {u}");
            if (wastePct > 0) sb.Append($" + wastage {wastePct:0.#}%");
            sb.Append($" = {net:0.##} {u}");
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Measurement note for an aggregated (collapsed) row. The per-element
        /// wastage % varies across the group, so the aggregate states absolute
        /// summed quantities rather than a single %.
        /// </summary>
        private static string BuildAggregateMeasurementNote(BOQLineItem agg, int count)
        {
            string u = agg.Unit ?? "";
            var sb = new StringBuilder();
            sb.Append($"{count}× — gross {agg.GrossQuantity:0.##} {u}");
            if (agg.DeductionQuantity > 0.0005) sb.Append($" − openings/voids {agg.DeductionQuantity:0.##} {u}");
            if (agg.WastageQuantity > 0.0005) sb.Append($" + wastage {agg.WastageQuantity:0.##} {u}");
            sb.Append($" = {agg.Quantity:0.##} {u}");
            return sb.ToString().Trim();
        }

        // ── NRM2 paragraph resolution ──────────────────────────────────────

        private static readonly Regex _tokenRx = new Regex(@"\[([a-zA-Z0-9_]+)\]", RegexOptions.Compiled);

        private static string ResolveNrm2Paragraph(Document doc, Element el, string catName)
        {
            // (i) Use the previously stored paragraph if it has no unresolved [tokens]
            string stored = ParameterHelpers.GetString(el, "ASS_NRM2_PARA_TXT");
            if (!string.IsNullOrEmpty(stored) && !_tokenRx.IsMatch(stored)) return stored;

            // (ii) BOQ-12 — Material-aware template selection. The template
            // library is queried with the element + category as before; the
            // material name + class are then folded into the resolved
            // paragraph so a "Generic" family doesn't end up with a
            // category-only description.
            string matName = null, matClass = null;
            try
            {
                matName = GetPrimaryMaterialName(el);
                if (!string.IsNullOrEmpty(matName) && doc != null)
                {
                    // WP6 — O(1) per-document name→Material cache instead of a
                    // fresh FilteredElementCollector(Material) for every BOQ row.
                    var mat = StingTools.UI.MaterialNameCache.ResolveMaterial(doc, matName);
                    matClass = mat?.MaterialClass;
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveNrm2Paragraph material: {ex.Message}"); }

            try
            {
                var all = BOQTemplateLibrary.LoadAll(doc, StingToolsApp.DataPath);
                var tpl = BOQTemplateLibraryExtensions.SelectBestTemplate(all, catName, el);
                if (tpl != null)
                {
                    string resolved = BOQTemplateLibraryExtensions.ResolveForElement(tpl, el, doc);
                    if (!string.IsNullOrEmpty(resolved))
                    {
                        // Prepend the material class as a qualifier when the
                        // template doesn't already mention it. WP6 — was a tangled
                        // triple-negation (`!IndexOf(...).Equals(-1) is false`) that
                        // evaluated to "not found", so the class was NEVER prepended.
                        if (!string.IsNullOrEmpty(matClass) &&
                            resolved.IndexOf(matClass, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            resolved = $"{matClass}: {resolved}";
                        }
                        return resolved;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveNrm2Paragraph template: {ex.Message}"); }

            // (iii) Safe fallback — material-qualified when known, category-
            // only otherwise. QS can override later in the Excel roundtrip.
            string qualifier = !string.IsNullOrEmpty(matClass) ? matClass.ToLower() + " "
                              : !string.IsNullOrEmpty(matName) ? matName.ToLower() + " "
                              : "";
            return $"Supply and fix {qualifier}{catName?.ToLower()}.";
        }

        // ── Carbon + lifecycle ─────────────────────────────────────────────

        private static double ComputeElementCarbon(Element el, double quantity, string unit)
            => ComputeElementCarbon(el, quantity, unit, out _, out _, out _);

        /// <summary>
        /// WP0 — the ONE per-element embodied-carbon (A1–A3) entry point shared
        /// across the plugin. Routes through <see cref="CarbonFactorResolver"/>
        /// (EPD → material param → lookup CSV → legacy) with correct per-m³ /
        /// per-kg unit handling, so the BOQ build, the design-option roll-up
        /// (<c>OptionCostCarbonCalculator</c>) and the EN 15978 stage tracker
        /// (<c>CarbonStageTracker</c>) all compute the SAME number from the SAME
        /// source instead of three divergent dictionaries. <paramref name="volM3"/>
        /// is the element volume in m³; pass ≤0 to skip (returns 0).
        /// </summary>
        internal static double ComputeElementCarbonKg(Element el, double volM3)
            => (el == null || volM3 <= 0) ? 0 : ComputeElementCarbon(el, volM3, "m³");

        // G5 — detailed overload: also reports the carbon factor SOURCE, the
        // data-quality band (Verified-EPD / Database / Missing) and the primary
        // material so the BOQ row can carry a carbon-confidence indicator and the
        // carbon-gap report can list weak/missing factors.
        // WP-C — back-compat overload: returns the A1-A3 FOSSIL headline only.
        private static double ComputeElementCarbon(Element el, double quantity, string unit,
            out string carbonSource, out string carbonQuality, out string carbonMaterial)
            => ComputeElementCarbonSplit(el, quantity, unit,
                   out carbonSource, out carbonQuality, out carbonMaterial, out _, out _);

        /// <summary>
        /// WP-C — the ONE carbon computation, RICS WLCA convention: returns the
        /// A1-A3 FOSSIL headline and reports the separate A1-A3 BIOGENIC term
        /// (≤ 0, timber only) + whether the volume was geometry-real or estimated.
        /// Splits per-material (each material its own fossil/biogenic factor) off
        /// REAL per-material / solid volumes, with the SAME per-element WasteFactor
        /// the cost uses — so cost and carbon waste agree and the fossil/biogenic
        /// split is identical regardless of which resolver tier fires.
        /// </summary>
        private static double ComputeElementCarbonSplit(Element el, double quantity, string unit,
            out string carbonSource, out string carbonQuality, out string carbonMaterial,
            out double biogenicKg, out bool estimated)
        {
            carbonSource = "none"; carbonQuality = BoqEpdStore.QualityMissing; carbonMaterial = "";
            biogenicKg = 0; estimated = false;
            try
            {
                if (el == null) return 0;
                double wastePct = ResolveElementWastePct(el);

                // Multi-material split — real per-material volumes.
                var ids = el.GetMaterialIds(false);
                if (ids != null && ids.Count >= 2)
                {
                    double fossil = 0, bio = 0, dominantVol = -1; string domMat = "", domSrc = "none"; bool any = false;
                    foreach (var mid in ids)
                    {
                        double volFt3;
                        try { volFt3 = el.GetMaterialVolume(mid); } catch { volFt3 = 0; }
                        if (volFt3 <= 0) continue;
                        double mVolM3 = WasteFactor.Apply(volFt3 * 0.0283168, "m3", wastePct);
                        string mName = (el.Document.GetElement(mid) as Material)?.Name ?? "";
                        if (string.IsNullOrEmpty(mName)) continue;

                        var res = CarbonFactorResolver.Resolve(el.Document, mName);
                        if (res.Factor > 0)
                        {
                            any = true;
                            AddMaterialCarbon(el.Document, mName, mVolM3, ref fossil, ref bio);
                        }
                        if (mVolM3 > dominantVol)
                        {
                            dominantVol = mVolM3; domMat = mName;
                            domSrc = string.IsNullOrEmpty(res.Source) ? "none" : res.Source;
                        }
                    }
                    if (any && (fossil != 0 || bio != 0))
                    {
                        carbonSource = domSrc; carbonQuality = BoqEpdStore.QualityForSource(domSrc); carbonMaterial = domMat;
                        biogenicKg = Math.Round(bio, 2);
                        return Math.Round(fossil, 2);
                    }
                }

                // Single-material.
                string material = GetPrimaryMaterialName(el);
                carbonMaterial = material ?? "";
                if (string.IsNullOrEmpty(material)) return 0;
                var resolved = CarbonFactorResolver.Resolve(el.Document, material);
                carbonSource = string.IsNullOrEmpty(resolved.Source) ? "none" : resolved.Source;
                carbonQuality = BoqEpdStore.QualityForSource(carbonSource);
                if (resolved.Factor <= 0) { carbonQuality = BoqEpdStore.QualityMissing; return 0; }

                // WP-C — drive off REAL geometry; estimate only when absent.
                double volM3 = RealOrEstimatedVolumeM3(el, quantity, unit, material, out estimated);
                volM3 = WasteFactor.Apply(volM3, "m3", wastePct);

                double fos = 0, bg = 0;
                AddMaterialCarbon(el.Document, material, volM3, ref fos, ref bg);
                biogenicKg = Math.Round(bg, 2);
                if (estimated && carbonQuality != BoqEpdStore.QualityMissing
                    && !carbonQuality.StartsWith("Verified", StringComparison.OrdinalIgnoreCase))
                    carbonQuality = "Estimated";
                return Math.Round(fos, 2);
            }
            catch (Exception ex) { StingLog.Warn($"ComputeElementCarbonSplit: {ex.Message}"); return 0; }
        }

        /// <summary>WP-C — accumulate one material's A1-A3 fossil + biogenic carbon
        /// from a volume (m³). Fossil is the headline; biogenic (≤ 0) is the RICS
        /// WLCA separate line. Resolution: an explicit material/library fossil &
        /// biogenic split wins; else timber uses the ICE <see cref="BiogenicCarbon"/>
        /// fossil/biogenic factors (tier-independent); else a non-bio material's net
        /// factor IS its fossil (biogenic 0).</summary>
        private static void AddMaterialCarbon(Document doc, string mName, double volM3,
            ref double fossil, ref double biogenic)
        {
            if (string.IsNullOrEmpty(mName) || volM3 <= 0) return;
            var res = CarbonFactorResolver.Resolve(doc, mName);
            if (res.Factor <= 0) return;
            double density = EstimateDensityKgPerM3(mName);
            double netPerM3 = res.PerUnit == CarbonFactorUnit.KgCo2ePerKg ? res.Factor * density : res.Factor;

            double fossilPerM3 = CarbonFactorResolver.GetCarbonFossilPerM3(doc, mName);
            double bioPerM3 = CarbonFactorResolver.GetCarbonBiogenicPerM3(doc, mName);
            if (fossilPerM3 <= 0 && bioPerM3 == 0)
            {
                if (BiogenicCarbon.IsBiogenic(mName) && density > 0)
                {
                    fossilPerM3 = BiogenicCarbon.TimberFossilPerKg * density;
                    bioPerM3 = BiogenicCarbon.TimberBiogenicPerKg * density;
                }
                else { fossilPerM3 = netPerM3; bioPerM3 = 0; }
            }
            else if (fossilPerM3 <= 0)
            {
                fossilPerM3 = netPerM3;   // biogenic split present, fossil missing → net proxy
            }
            fossil += volM3 * fossilPerM3;
            biogenic += volM3 * bioPerM3;
        }

        /// <summary>WP-C — the carbon volume from REAL geometry where possible.
        /// m³-measured rows use the (deducted, wasted) measured quantity; otherwise
        /// the element's real solid volume; only a genuine no-geometry element falls
        /// back to the guessed thickness / cross-section estimate (estimated = true).</summary>
        private static double RealOrEstimatedVolumeM3(Element el, double quantity, string unit, string material, out bool estimated)
        {
            estimated = false;
            string u = (unit ?? "").Trim().ToLowerInvariant();
            if (u == "m³" || u == "m3" || u == "cum") return quantity;   // measured volume is authoritative
            double real = ReadGeometryVolumeM3(el);
            if (real <= 0) real = ReadElementVolumeM3(el);
            if (real > 0) return real;
            estimated = true;
            return EstimateVolumeM3(el, quantity, unit, material);
        }

        private static double ResolveElementWastePct(Element el)
        {
            double overrideWaste = 0;
            try { overrideWaste = StingCostRateOverrideSchema.Read(el)?.WastePercent ?? 0; }
            catch (Exception ex) { StingLog.WarnRateLimited("Carbon.OvrWaste", $"override waste read: {ex.Message}"); }
            // PM-5 — carbon path resolves the SAME per-material/category waste table.
            return WasteTable.ResolveWastePercent(null, el.Category?.Name, overrideWaste,
                TagConfig.GetConfigDouble("COST_DEFAULT_WASTE_PCT", 5.0));
        }

        /// <summary>
        /// R-4 — Volume estimator that works for volumetric, surface, AND
        /// linear elements. Returns 0 only when no volume / area / length
        /// is exposed (i.e. point-instance families with no geometry).
        /// </summary>
        private static double EstimateVolumeM3(Element el, double quantity, string unit, string material)
        {
            if (string.IsNullOrEmpty(unit)) unit = "each";
            try
            {
                // Volumetric — direct.
                if (unit == "m³" || unit == "m3") return quantity;

                // Surface — area × default layer thickness (read from material lookup
                // when not exposed on the element).
                if (unit == "m²" || unit == "m2")
                {
                    double thicknessMm = ReadLayerThicknessMm(el);
                    if (thicknessMm <= 0)
                    {
                        // Sensible defaults so we don't return zero for paint / membrane.
                        string lc = (material ?? "").ToLowerInvariant();
                        thicknessMm = lc.Contains("paint") || lc.Contains("coating") ? 0.15
                                    : lc.Contains("membrane") || lc.Contains("dpm") ? 1.5
                                    : lc.Contains("plaster") || lc.Contains("gypsum") ? 12.5
                                    : lc.Contains("insulation") ? 50.0
                                    : 10.0;
                    }
                    return quantity * (thicknessMm / 1000.0);
                }

                // Linear — length × cross-section read from element when present.
                if (unit == "m")
                {
                    double areaMm2 = ReadCrossSectionMm2(el);
                    if (areaMm2 <= 0) areaMm2 = 1000.0; // default Ø35.7 mm circular equiv (2·√(1000/π)) — caller can override via param
                    return quantity * (areaMm2 / 1_000_000.0);
                }

                // R2-1 — "each" / point-instance / any other unit: recover a REAL
                // element volume so a per-m³ carbon factor still yields non-zero
                // embodied carbon. Previously this branch returned 0 unconditionally,
                // which silently zeroed embodied carbon for every each-priced family
                // (doors, windows, MEP equipment, fixtures, furniture, stairs,
                // sprinklers, fire/comms devices). Resolution order:
                //   (a) exposed volume parameter (same HOST_VOLUME_COMPUTED path the
                //       m³ takeoff uses in DeriveQuantity), then a generic "Volume";
                //   (b) actual solid geometry summed from get_Geometry;
                //   (c) mass ÷ density (gives a volume so the per-m³ factor is honoured).
                // Genuine kg-unit factors never reach here — they are mass-multiplied
                // in ComputeElementCarbon's KgCo2ePerKg branch — so this is safe.

                // (a) Exposed volume parameter — ft³ → m³.
                double volM3 = ReadElementVolumeM3(el);
                if (volM3 > 0) return quantity > 0 ? volM3 * quantity : volM3;

                // (b) Actual solid geometry — ft³ → m³.
                double geomM3 = ReadGeometryVolumeM3(el);
                if (geomM3 > 0) return quantity > 0 ? geomM3 * quantity : geomM3;

                // (c) Mass ÷ density fallback so the per-m³ factor is still applied.
                double massKg = EstimateMassKg(el, quantity, unit);
                if (massKg > 0)
                {
                    double density = EstimateDensityKgPerM3(material);
                    if (density > 0) return massKg / density;
                }

                // True point family with no geometry / mass — nothing to estimate.
                return 0;
            }
            catch (Exception ex) { StingLog.Warn($"EstimateVolumeM3 ({unit}): {ex.Message}"); return 0; }
        }

        private static double ReadLayerThicknessMm(Element el)
        {
            try
            {
                var p = el.LookupParameter("Thickness") ?? el.LookupParameter("Width");
                if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                {
                    // Internal feet → millimetres.
                    return UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Millimeters);
                }
            }
            catch (Exception ex) { StingLog.WarnRateLimited("VolEst.Layer", $"ReadLayerThicknessMm: {ex.Message}"); }
            return 0;
        }

        private static double ReadCrossSectionMm2(Element el)
        {
            try
            {
                // Pipes / conduits expose Outside Diameter; cable trays expose Width × Height.
                var od = el.LookupParameter("Outside Diameter");
                if (od != null && od.HasValue && od.StorageType == StorageType.Double)
                {
                    double dMm = UnitUtils.ConvertFromInternalUnits(od.AsDouble(), UnitTypeId.Millimeters);
                    return Math.PI * (dMm / 2.0) * (dMm / 2.0);
                }
                var w = el.LookupParameter("Width");
                var h = el.LookupParameter("Height");
                if (w != null && w.HasValue && h != null && h.HasValue &&
                    w.StorageType == StorageType.Double && h.StorageType == StorageType.Double)
                {
                    double wMm = UnitUtils.ConvertFromInternalUnits(w.AsDouble(), UnitTypeId.Millimeters);
                    double hMm = UnitUtils.ConvertFromInternalUnits(h.AsDouble(), UnitTypeId.Millimeters);
                    return wMm * hMm;
                }
            }
            catch (Exception ex) { StingLog.WarnRateLimited("VolEst.Xs", $"ReadCrossSectionMm2: {ex.Message}"); }
            return 0;
        }

        /// <summary>
        /// R2-1 — Reads an exposed element volume in m³. Prefers the built-in
        /// HOST_VOLUME_COMPUTED (the same source DeriveQuantity uses for the m³
        /// takeoff) then a generic "Volume" parameter. Internal ft³ → m³ (×0.0283168).
        /// Returns 0 when no volume parameter is exposed.
        /// </summary>
        private static double ReadElementVolumeM3(Element el)
        {
            try
            {
                Parameter volP = el.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
                if (volP == null || !volP.HasValue)
                    volP = el.LookupParameter("Volume");
                if (volP != null && volP.HasValue && volP.StorageType == StorageType.Double)
                {
                    double ft3 = volP.AsDouble();
                    if (ft3 > 0) return ft3 * 0.0283168; // ft³ → m³
                }
            }
            catch (Exception ex) { StingLog.WarnRateLimited("VolEst.Param", $"ReadElementVolumeM3: {ex.Message}"); }
            return 0;
        }

        /// <summary>
        /// R2-1 — Sums actual solid geometry volume for point-instance families
        /// (doors, windows, MEP equipment, fixtures) that expose no volume
        /// parameter. Recurses one level into GeometryInstances. Internal ft³ → m³.
        /// Returns 0 when no solid geometry is available.
        /// </summary>
        private static double ReadGeometryVolumeM3(Element el)
        {
            try
            {
                // WP-M — Fine to match TakeoffRule.ReadSolidVolumeFt3, so the same
                // element measures identically whichever solid reader the path uses.
                var opt = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false, DetailLevel = ViewDetailLevel.Fine };
                GeometryElement ge = el.get_Geometry(opt);
                if (ge == null) return 0;
                double ft3 = SumSolidVolumeFt3(ge);
                if (ft3 > 0) return ft3 * 0.0283168; // ft³ → m³
            }
            catch (Exception ex) { StingLog.WarnRateLimited("VolEst.Geom", $"ReadGeometryVolumeM3: {ex.Message}"); }
            return 0;
        }

        private static double SumSolidVolumeFt3(GeometryElement ge)
        {
            double total = 0;
            foreach (GeometryObject go in ge)
            {
                if (go is Solid s && s.Volume > 0) total += s.Volume;
                else if (go is GeometryInstance gi)
                {
                    GeometryElement inst = gi.GetInstanceGeometry();
                    if (inst != null) total += SumSolidVolumeFt3(inst);
                }
            }
            return total;
        }

        /// <summary>
        /// Simple lifecycle cost: capital + 25y NPV of annual maintenance cost.
        /// Maintenance fraction driven by COBIE_TYPE_MAP.csv MaintenanceFreqMonths
        /// column when present (falls back to 2%/y for hard assets, 0.5%/y for shell).
        /// Discount rate = 3.5% (UK Treasury Green Book).
        /// </summary>
        /// <summary>CA-4 — the project carbon price (UGX per kgCO₂e) from
        /// project_config.json (COST_CARBON_PRICE_UGX_PER_KG). 0 = carbon not
        /// priced, so the carbon-inclusive LCC equals the plain LCC.</summary>
        internal static double CarbonPriceUgxPerKg()
            => TagConfig.GetConfigDouble(StingTools.Core.Sustainability.CarbonLcc.CarbonPriceConfigKey, 0.0);

        private static double ComputeLifecycleCost(double capitalUgx, string catName)
        {
            if (capitalUgx <= 0) return 0;
            double annualMaintenance = capitalUgx * EstimateAnnualMaintenanceRate(catName);
            double npvFactor = 0;
            for (int y = 1; y <= LifecycleYears; y++)
                npvFactor += 1.0 / Math.Pow(1 + LifecycleDiscountRate, y);
            return Math.Round(capitalUgx + annualMaintenance * npvFactor, 0);
        }

        private static double EstimateAnnualMaintenanceRate(string catName)
        {
            if (string.IsNullOrEmpty(catName)) return 0.02;
            string lower = catName.ToLowerInvariant();
            if (lower.Contains("foundation") || lower.Contains("structural")) return 0.005;
            if (lower.Contains("wall") || lower.Contains("floor") || lower.Contains("roof")) return 0.01;
            if (lower.Contains("duct") || lower.Contains("pipe") || lower.Contains("mechanical")) return 0.03;
            if (lower.Contains("electrical") || lower.Contains("lighting")) return 0.025;
            if (lower.Contains("furniture") || lower.Contains("casework")) return 0.04;
            return 0.02;
        }

        private static double EstimateMassKg(Element el, double quantity, string unit)
        {
            try
            {
                Parameter massP = el.LookupParameter("Weight") ?? el.LookupParameter("Mass");
                if (massP != null && massP.HasValue) return massP.AsDouble();

                // Density fallback — only applies when we have a volume measurement.
                if ((unit == "m³" || unit == "m3") && quantity > 0)
                {
                    double density = EstimateDensityKgPerM3(GetPrimaryMaterialName(el));
                    return quantity * density;
                }
            }
            catch (Exception ex) { StingLog.Warn($"EstimateMassKg: {ex.Message}"); }
            return 0;
        }

        private static double EstimateDensityKgPerM3(string material)
        {
            if (string.IsNullOrWhiteSpace(material)) return 1000;

            // N+7 — Single-source resolution. MaterialLookupCsv corporate
            // library wins; the legacy hard-coded keyword switch is now
            // last-resort fallback only.
            try
            {
                double libVal = StingTools.UI.MaterialLookupCsv.GetDensity(material);
                if (libVal > 0) return libVal;
            }
            catch (Exception ex) { StingLog.WarnRateLimited("EstimateDensity.Lookup", $"EstimateDensity lookup: {ex.Message}"); }

            string lower = material.ToLowerInvariant();
            // BOQ-accuracy audit F4/F5: reinforced concrete ≈ 2450 kg/m³; softwood
            // density aligned to the MATERIAL_LOOKUP corrected value (480, CIBSE
            // 420–550) so the cost-mass and carbon-mass paths use one density.
            if (lower.Contains("reinforced") && lower.Contains("concrete")) return 2450;
            if (lower.Contains("concrete")) return 2400;
            if (lower.Contains("steel")) return 7850;
            if (lower.Contains("hardwood")) return 700;
            if (lower.Contains("timber") || lower.Contains("wood") || lower.Contains("softwood")) return 480;
            if (lower.Contains("alumin")) return 2700;
            if (lower.Contains("glass")) return 2500;
            if (lower.Contains("brick")) return 1920;
            if (lower.Contains("insulation")) return 40;
            if (lower.Contains("plaster") || lower.Contains("gypsum")) return 1250;
            return 1000;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Parameter write-back
        //  Writes CST_* / ASS_NRM2_PARA_* / ASS_BOQ_* parameters on elements
        //  (only when values differ — dirty check) and updates ProjectInfo
        //  project-level parameters. Caller supplies the transaction so
        //  multiple operations can be batched within a single undo entry.
        // ══════════════════════════════════════════════════════════════════

        internal static int WriteElementParameters(Document doc, IEnumerable<BOQLineItem> items)
        {
            if (items == null) return 0;
            int written = 0;
            // WP-M — aggregated constituents re-measure through the SAME MeasureQuantity
            // path the bill uses (NRM2/CESMM opening/void deductions + waste), so the
            // stamped CST_QTY_MEASURED / CST_MODELED_TOTAL_UGX matches the net bill row
            // instead of the legacy DeriveQuantity (which skipped deductions and could
            // over-stamp a row above its net bill quantity).
            string aggStdId = TagConfig.GetConfigValue("COST_MEASUREMENT_STANDARD");
            if (string.IsNullOrWhiteSpace(aggStdId)) aggStdId = "nrm2";
            var aggStd = MeasurementStandard.MeasurementStandardRegistry.Get(aggStdId);
            foreach (var item in items)
            {
                // P1.2 — aggregated rows stamp EVERY constituent element. Shared
                // per-unit fields (rate / source / section / paragraph /
                // confidence) write to all; quantity / total / carbon /
                // lifecycle are re-derived per element so each carries its own
                // measured figure rather than the merged sum. Single-element +
                // manual rows keep exact prior behaviour.
                List<long> ids =
                    (item.ConstituentElementIds != null && item.ConstituentElementIds.Count > 0)
                        ? item.ConstituentElementIds
                        : (item.RevitElementId >= 0 ? new List<long> { item.RevitElementId } : null);
                if (ids == null) continue;
                bool aggregated = ids.Count > 1;

                foreach (long rid in ids)
                {
                    Element el;
                    try { el = doc.GetElement(new ElementId(rid)); }
                    catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); continue; }
                    if (el == null) continue;

                    double qty = aggregated
                        ? MeasureQuantity(el, item.Unit,
                            string.IsNullOrEmpty(item.Category) ? ParameterHelpers.GetCategoryName(el) : item.Category,
                            aggStd, out _, out _, out _, out _)
                        : item.Quantity;
                    double totalUgx = Math.Round(qty * item.RateUGX, 0);
                    double carbonKg = aggregated
                        ? ComputeElementCarbon(el, qty, item.Unit) : item.EmbodiedCarbonKg;
                    double lifecycleUgx = aggregated
                        ? ComputeLifecycleCost(item.RateUGX * qty, item.Category) : item.LifecycleCostUGX;

                    // Rate fields — always write both currencies so the element
                    // stays currency-agnostic across sessions (Gap G3).
                    WriteIfChanged(el, "CST_UNIT_RATE_UGX", item.RateUGX.ToString("F0", CultureInfo.InvariantCulture), ref written);
                    WriteIfChanged(el, "CST_UNIT_RATE_USD", item.RateUSD.ToString("F2", CultureInfo.InvariantCulture), ref written);
                    WriteIfChanged(el, "CST_QTY_MEASURED", $"{qty:F3} {item.Unit}", ref written);

                    // Computed total — stored as NUMBER parameter.
                    // CA-5 — ADDITIVITY: CST_MODELED_TOTAL_UGX is per-ELEMENT
                    // (qty × this element's resolved rate). Σ over modelled
                    // elements reconstructs the modelled-works subtotal and is the
                    // correct weighting base for EVM/SOV %-completion. It is NOT
                    // expected to equal an AGGREGATED bill row's TotalUGX
                    // line-by-line: the bill may collapse mixed-rate near-identical
                    // elements into one row at a representative rate, so per-element
                    // Σ and the aggregated bill agree at the subtotal level, not row
                    // by row. Both now resolve rates from the SAME provider chain.
                    TrySetNumber(el, "CST_MODELED_TOTAL_UGX", totalUgx, ref written);

                    WriteIfChanged(el, "CST_RATE_SOURCE", item.RateSource ?? "", ref written);

                    if (!string.IsNullOrEmpty(item.SnapshotRef))
                        WriteIfChanged(el, "CST_BOQ_SNAPSHOT_REF", item.SnapshotRef, ref written);

                    // Paragraph — audit trail (Phase 11D)
                    string currentPara = ParameterHelpers.GetString(el, "ASS_NRM2_PARA_TXT") ?? "";
                    if (!string.IsNullOrEmpty(item.ResolvedNRM2Paragraph) && currentPara != item.ResolvedNRM2Paragraph)
                    {
                        if (!string.IsNullOrEmpty(currentPara))
                        {
                            ParameterHelpers.SetString(el, "ASS_NRM2_PARA_PREV_TXT", currentPara, overwrite: true);
                        }
                        ParameterHelpers.SetString(el, "ASS_NRM2_PARA_TXT", item.ResolvedNRM2Paragraph, overwrite: true);
                        ParameterHelpers.SetString(el, "ASS_NRM2_PARA_DATE_TXT",
                            DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), overwrite: true);
                        ParameterHelpers.SetString(el, "ASS_NRM2_PARA_AUTHOR_TXT", Environment.UserName ?? "", overwrite: true);
                        written++;
                    }

                    // Line ref is write-once — never overwrite an explicit user-assigned ref (Gap G8)
                    if (!string.IsNullOrEmpty(item.BOQLineRef))
                    {
                        string existingRef = ParameterHelpers.GetString(el, "ASS_BOQ_LINE_REF");
                        if (string.IsNullOrEmpty(existingRef))
                        {
                            ParameterHelpers.SetString(el, "ASS_BOQ_LINE_REF", item.BOQLineRef, overwrite: true);
                            written++;
                        }
                    }

                    if (!string.IsNullOrEmpty(item.Category))
                        WriteIfChanged(el, "ASS_BOQ_SECTION_NAME", item.Category, ref written);

                    TrySetNumber(el, "CST_EMBODIED_CARBON_KG", carbonKg, ref written);
                    TrySetNumber(el, "CST_LIFECYCLE_COST_UGX", lifecycleUgx, ref written);
                    ParameterHelpers.SetInt(el, "CST_RATE_CONFIDENCE", item.RateConfidence, overwrite: true);
                }
            }
            return written;
        }

        internal static void WriteProjectParameters(Document doc, BOQDocument boq)
        {
            if (doc?.ProjectInformation == null || boq == null) return;
            Element pi = doc.ProjectInformation;
            int dummy = 0;

            TrySetNumber(pi, "PROJECT_BUDGET_UGX", boq.ProjectBudgetUGX, ref dummy);
            TrySetNumber(pi, "CST_BUDGET_VARIANCE_UGX", boq.BudgetVarianceUGX, ref dummy);
            TrySetNumber(pi, "CST_BOQ_COVERAGE_PCT", boq.BudgetCoveragePct, ref dummy);
            ParameterHelpers.SetString(pi, "CST_LAST_COSTED_DATE",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), overwrite: true);
        }

        // ── Helpers used by WriteElementParameters ────────────────────────

        private static void WriteIfChanged(Element el, string paramName, string value, ref int counter)
        {
            if (el == null || string.IsNullOrEmpty(paramName)) return;
            string current = ParameterHelpers.GetString(el, paramName);
            if (current == value) return;
            if (ParameterHelpers.SetString(el, paramName, value ?? "", overwrite: true)) counter++;
        }

        private static void TrySetNumber(Element el, string paramName, double value, ref int counter)
        {
            try
            {
                Parameter p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly) return;
                // Only write when the displayed value differs — prevents dirtying the
                // transaction when the model already has the right value.
                double current = p.HasValue ? p.AsDouble() : double.NaN;
                if (!double.IsNaN(current) && Math.Abs(current - value) < 1e-6) return;
                if (p.StorageType == StorageType.Double) { p.Set(value); counter++; }
                else if (p.StorageType == StorageType.Integer) { p.Set((int)Math.Round(value)); counter++; }
                else p.Set(value.ToString("F2", CultureInfo.InvariantCulture));
            }
            catch (Exception ex) { StingLog.Warn($"TrySetNumber({paramName}): {ex.Message}"); }
        }

        private static double ReadProjectBudget(Document doc)
        {
            if (doc?.ProjectInformation == null) return 0;
            Parameter p = doc.ProjectInformation.LookupParameter("PROJECT_BUDGET_UGX");
            if (p != null && p.HasValue)
            {
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.String
                    && double.TryParse(p.AsString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
                    return d;
            }
            // Fallback — project_config.json PROJECT_BUDGET_UGX
            return TagConfig.GetConfigDouble("PROJECT_BUDGET_UGX", 0);
        }

        /// <summary>
        /// Read the "Project Name" field from the Project Information dialog.
        /// doc.ProjectInformation.Name returns the ELEMENT name (an internal
        /// identifier), not the value the user types into "Project Name".
        /// That field is bound to BuiltInParameter.PROJECT_NAME.
        /// </summary>
        private static string ReadProjectName(Document doc)
        {
            try
            {
                var pi = doc?.ProjectInformation;
                if (pi != null)
                {
                    string v = pi.get_Parameter(BuiltInParameter.PROJECT_NAME)?.AsString();
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                    if (!string.IsNullOrWhiteSpace(pi.Name)) return pi.Name;
                }
            }
            catch (Exception ex) { StingLog.Warn($"ReadProjectName: {ex.Message}"); }
            return !string.IsNullOrWhiteSpace(doc?.Title) ? doc.Title : "Unknown project";
        }

        /// <summary>
        /// BOQ document title — combines the Project Number (if set) with
        /// "Bill of Quantities" so the exported workbook and the header
        /// strip identify the project at a glance.
        /// </summary>
        private static string ReadProjectDocumentTitle(Document doc)
        {
            try
            {
                string num = doc?.ProjectInformation?.get_Parameter(BuiltInParameter.PROJECT_NUMBER)?.AsString();
                if (!string.IsNullOrWhiteSpace(num))
                    return $"Bill of Quantities — {num}";
            }
            catch (Exception ex) { StingLog.Warn($"ReadProjectDocumentTitle: {ex.Message}"); }
            return "Bill of Quantities";
        }

        // ══════════════════════════════════════════════════════════════════
        //  Snapshot persistence — save, list, load and prune.
        //  Snapshots are plain JSON under {projectDir}/STING_BIM_MANAGER/.
        //  The same dir hosts every other BIM-manager sidecar so backups
        //  and CDE transmittals pick them up automatically.
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Persist the BOQ to a timestamped JSON snapshot. Also stamps the
        /// snapshot label onto every modeled element (CST_BOQ_SNAPSHOT_REF)
        /// so a line in the BOQ can always be traced back to the source
        /// element at the moment it was costed. Caller supplies a transaction
        /// context inside which the element stamping runs; budget/variance
        /// write-back uses the same transaction.
        /// </summary>
        internal static string SaveSnapshot(Document doc, BOQDocument boq, string label, string snapshotType)
        {
            if (doc == null || boq == null) throw new ArgumentNullException();
            string safeLabel = MakeSafeFileName(label ?? "snapshot");
            string safeType = MakeSafeFileName(snapshotType ?? "Manual");
            string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
            string path = Path.Combine(bimDir,
                $"boq_snapshot_{safeType}_{safeLabel}_{DateTime.Now:yyyyMMdd_HHmmss}.json");

            boq.SnapshotLabel = label;
            boq.SnapshotType = snapshotType;
            boq.SnapshotDate = DateTime.UtcNow;

            // Stamp the snapshot reference onto every row before serialising.
            foreach (var it in boq.AllItems)
                it.SnapshotRef = label;

            // P1: compute canonical checksum BEFORE writing so it can be
            // serialised into the snapshot file's audit trail and used by
            // the server to detect duplicate pushes.
            string checksum = BoqSnapshotHasher.ComputeChecksum(boq);

            try
            {
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(boq, _jsonSettings));
                if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
                else File.Move(tmp, path);

                // Sidecar meta — checksum + future sync state. Lives next
                // to the snapshot json so it survives independently of any
                // server round-trip.
                WriteSnapshotMetaSidecar(path, checksum, label, snapshotType);

                StingLog.Info($"BOQ snapshot saved: {Path.GetFileName(path)} ({boq.AllItems.Count} items, checksum={Shorten(checksum)})");
            }
            catch (Exception ex) { StingLog.Error("BOQ snapshot save", ex); throw; }

            PruneSnapshots(doc);

            // P1: fire-and-forget server push. Failures fall through to
            // "Pending" state in the sidecar and the background sync
            // scheduler retries. Snapshot save success is independent of
            // network availability.
            TryPushSnapshotAsync(doc, boq, checksum, label, path);

            return path;
        }

        // P1 — async push wrapper. Non-blocking, swallows exceptions, and
        // updates the sidecar with the resulting SyncState.
        private static void TryPushSnapshotAsync(Document doc, BOQDocument boq,
            string checksum, string label, string snapshotPath)
        {
            try
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var result = await BoqSyncCoordinator.PushSnapshotAsync(doc, boq, checksum, label);
                        UpdateSnapshotMetaSidecar(snapshotPath, result);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"TryPushSnapshotAsync: {ex.Message}");
                    }
                });
            }
            catch (Exception ex) { StingLog.Warn($"TryPushSnapshotAsync schedule: {ex.Message}"); }
        }

        // Sidecar file format: <snapshot.json>.meta.json carrying
        // { checksum, label, type, savedUtc, syncState, serverBaselineId, syncDetail }.
        private static void WriteSnapshotMetaSidecar(string snapshotPath, string checksum,
            string label, string snapshotType)
        {
            try
            {
                string metaPath = snapshotPath + ".meta.json";
                var meta = new
                {
                    checksum,
                    label,
                    type = snapshotType,
                    savedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    syncState = "Local",
                    serverBaselineId = (string)null,
                    syncDetail = ""
                };
                File.WriteAllText(metaPath, JsonConvert.SerializeObject(meta, _jsonSettings));
            }
            catch (Exception ex) { StingLog.Warn($"WriteSnapshotMetaSidecar: {ex.Message}"); }
        }

        private static void UpdateSnapshotMetaSidecar(string snapshotPath, BoqSyncResult result)
        {
            try
            {
                string metaPath = snapshotPath + ".meta.json";
                if (!File.Exists(metaPath)) return;
                var existing = JObject.Parse(File.ReadAllText(metaPath));
                existing["syncState"] = result?.SyncState ?? "Pending";
                existing["serverBaselineId"] = result?.ServerBaselineId?.ToString();
                existing["syncDetail"] = result?.Detail ?? "";
                existing["linesCreated"] = result?.LinesCreated ?? 0;
                existing["linesUpdated"] = result?.LinesUpdated ?? 0;
                existing["lastSyncedUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                File.WriteAllText(metaPath, existing.ToString(Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn($"UpdateSnapshotMetaSidecar: {ex.Message}"); }
        }

        private static string Shorten(string s)
            => string.IsNullOrEmpty(s) ? "" : (s.Length <= 8 ? s : s.Substring(0, 8));

        /// <summary>Load a snapshot JSON. Returns null on any failure.</summary>
        internal static BOQDocument LoadSnapshot(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try { return JsonConvert.DeserializeObject<BOQDocument>(File.ReadAllText(path), _jsonSettings); }
            catch (Exception ex) { StingLog.Warn($"LoadSnapshot({Path.GetFileName(path)}): {ex.Message}"); return null; }
        }

        /// <summary>
        /// Enumerate all available BOQ snapshots. Cheap — reads only the top
        /// of each file via a lazy JObject Parse that still gives us the KPI
        /// header (label, type, grand total).
        /// </summary>
        internal static List<BOQSnapshotMeta> ListSnapshots(Document doc)
        {
            var list = new List<BOQSnapshotMeta>();
            try
            {
                string dir = BIMManagerEngine.GetBIMManagerDir(doc);
                if (!Directory.Exists(dir)) return list;
                foreach (string f in Directory.EnumerateFiles(dir, "boq_snapshot_*.json"))
                {
                    try
                    {
                        // Filename shape:  boq_snapshot_{type}_{label}_{yyyyMMdd_HHmmss}.json
                        string stem = Path.GetFileNameWithoutExtension(f);
                        var parts = stem.Split('_');
                        if (parts.Length < 5) continue;
                        string type = parts[2];
                        string dateStr = parts[parts.Length - 2] + "_" + parts[parts.Length - 1];
                        string label = string.Join("_", parts.Skip(3).Take(parts.Length - 5));
                        DateTime.TryParseExact(dateStr, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out DateTime dt);

                        double total = 0;
                        double netExVat = 0;   // CA-2 — net-of-VAT basis for the lifecycle
                        try
                        {
                            // Only parse the single top-level property we need.
                            using (var sr = new StreamReader(f))
                            using (var jr = new JsonTextReader(sr))
                            {
                                var jo = JObject.Load(jr);
                                if (jo != null && jo["Sections"] is JArray secs)
                                {
                                    foreach (var sec in secs)
                                    {
                                        if (sec["Items"] is JArray its)
                                        {
                                            foreach (var it in its)
                                            {
                                                double q = it.Value<double?>("Quantity") ?? 0;
                                                double r = it.Value<double?>("RateUGX") ?? 0;
                                                total += q * r;
                                            }
                                        }
                                    }
                                    // WP1 — read the snapshot's ACTUAL markup %
                                    // (incl. VAT + itemised prelims) and apply the
                                    // ONE canonical waterfall so the dropdown total
                                    // equals what BuildBOQDocument computed. Old
                                    // snapshots lacking a field fall back to the
                                    // tender defaults.
                                    double works = total;
                                    double pre = jo.Value<double?>("PrelimPct") ?? 12.0;
                                    double con = jo.Value<double?>("ContingencyPct") ?? 10.0;
                                    double oh  = jo.Value<double?>("OverheadPct") ?? 8.0;
                                    double vat = jo.Value<double?>("VatPct") ?? 18.0;
                                    bool prelimsItemised = jo.Value<bool?>("PrelimsItemised") ?? false;
                                    double prelimsAbs = works * pre / 100.0;
                                    if (prelimsItemised && jo["PrelimLines"] is JArray plArr)
                                    {
                                        try
                                        {
                                            var plLines = plArr.ToObject<List<BoqPrelimLine>>();
                                            if (plLines != null && plLines.Count > 0)
                                                prelimsAbs = plLines.Sum(l => l.AmountFor(works));
                                        }
                                        catch (Exception ex) { StingLog.Warn($"ListSnapshots prelimLines {Path.GetFileName(f)}: {ex.Message}"); }
                                    }
                                    var mk = BoqTotals.Compute(works, prelimsAbs, oh, con, vat);
                                    total = mk.GrandTotal;
                                    netExVat = mk.NetExVat;   // CA-2 — net-of-VAT contract-sum basis
                                }
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"ListSnapshots parse {Path.GetFileName(f)}: {ex.Message}"); }

                        // P1: enrich with sidecar meta (checksum, sync state)
                        // when available. Sidecar is best-effort — missing
                        // sidecar leaves the new fields at their defaults.
                        string checksum = "";
                        Guid? serverBaselineId = null;
                        string syncState = "Local";
                        try
                        {
                            string metaPath = f + ".meta.json";
                            if (File.Exists(metaPath))
                            {
                                var m = JObject.Parse(File.ReadAllText(metaPath));
                                checksum = m.Value<string>("checksum") ?? "";
                                syncState = m.Value<string>("syncState") ?? "Local";
                                string srvId = m.Value<string>("serverBaselineId");
                                if (Guid.TryParse(srvId, out Guid g)) serverBaselineId = g;
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"ListSnapshots meta {Path.GetFileName(f)}: {ex.Message}"); }

                        list.Add(new BOQSnapshotMeta
                        {
                            Path = f, Label = label, Type = type, Date = dt, GrandTotalUGX = total,
                            NetExVatUGX = netExVat,
                            Checksum = checksum,
                            ServerBaselineId = serverBaselineId,
                            SyncState = syncState
                        });
                    }
                    catch (Exception ex) { StingLog.Warn($"ListSnapshots inner: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ListSnapshots: {ex.Message}"); }
            return list.OrderByDescending(s => s.Date).ToList();
        }

        private static void PruneSnapshots(Document doc)
        {
            try
            {
                var all = ListSnapshots(doc);
                if (all.Count <= MaxSnapshotsRetained) return;
                foreach (var old in all.Skip(MaxSnapshotsRetained))
                {
                    try { File.Delete(old.Path); }
                    catch (Exception ex) { StingLog.Warn($"PruneSnapshots delete {old.Path}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"PruneSnapshots: {ex.Message}"); }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Snapshot comparison — builds a structured diff between two
        //  snapshots suitable for rendering in a StingResultPanel or a
        //  dedicated Excel "Snapshot Comparison" sheet.
        // ══════════════════════════════════════════════════════════════════

        internal static BOQSnapshotDiff CompareSnapshots(string pathA, string pathB)
        {
            var a = LoadSnapshot(pathA);
            var b = LoadSnapshot(pathB);
            return BuildDiff(a, b);
        }

        /// <summary>
        /// Phase 2C — diff the LIVE bill (newer, B) against a saved snapshot
        /// (older, A) so the drift check can compare the current model to the
        /// last saved baseline without writing the live bill to disk first.
        /// </summary>
        internal static BOQSnapshotDiff CompareLiveToSnapshot(string olderSnapshotPath, BOQDocument liveNewer)
        {
            var a = LoadSnapshot(olderSnapshotPath);
            return BuildDiff(a, liveNewer);
        }

        private static BOQSnapshotDiff BuildDiff(BOQDocument a, BOQDocument b)
        {
            var diff = new BOQSnapshotDiff();
            if (a == null || b == null) return diff;

            diff.LabelA = a.SnapshotLabel; diff.LabelB = b.SnapshotLabel;
            diff.TypeA = a.SnapshotType; diff.TypeB = b.SnapshotType;
            diff.DateA = a.SnapshotDate; diff.DateB = b.SnapshotDate;
            diff.TotalA = a.GrandTotalUGX; diff.TotalB = b.GrandTotalUGX;
            diff.ModeledA = a.ModeledTotalUGX; diff.ModeledB = b.ModeledTotalUGX;
            diff.ProvA = a.ProvTotalUGX; diff.ProvB = b.ProvTotalUGX;
            diff.CarbonA = a.TotalCarbonKg; diff.CarbonB = b.TotalCarbonKg;

            // Match items by BOQLineRef first, then by Category+ItemName composite.
            var aByKey = IndexByKey(a);
            var bByKey = IndexByKey(b);
            var keys = new HashSet<string>(aByKey.Keys);
            foreach (var k in bByKey.Keys) keys.Add(k);

            foreach (var key in keys)
            {
                aByKey.TryGetValue(key, out BOQLineItem ai);
                bByKey.TryGetValue(key, out BOQLineItem bi);
                var cd = new CategoryDiff
                {
                    NRM2Section = bi?.NRM2Section ?? ai?.NRM2Section,
                    Name = bi?.Category ?? ai?.Category,
                    Discipline = bi?.Discipline ?? ai?.Discipline,
                    QtyA = ai?.Quantity ?? 0,
                    QtyB = bi?.Quantity ?? 0,
                    RateA = ai?.RateUGX ?? 0,
                    RateB = bi?.RateUGX ?? 0,
                    TotalA = ai?.TotalUGX ?? 0,
                    TotalB = bi?.TotalUGX ?? 0
                };
                cd.ChangeType = ClassifyChange(ai, bi);
                cd.ChangeReason = BuildChangeReason(cd, ai, bi);
                if (cd.ChangeType != BOQChangeType.NoChange) diff.CategoryDiffs.Add(cd);
            }

            // Section-level rollup
            var rolled = new Dictionary<string, SectionDiff>(StringComparer.OrdinalIgnoreCase);
            foreach (var cd in diff.CategoryDiffs)
            {
                string key = $"{cd.NRM2Section}|{cd.Discipline}";
                if (!rolled.TryGetValue(key, out var sd))
                {
                    sd = new SectionDiff
                    {
                        NRM2Section = cd.NRM2Section, Name = cd.Name, Discipline = cd.Discipline
                    };
                    rolled[key] = sd;
                }
                sd.TotalA += cd.TotalA;
                sd.TotalB += cd.TotalB;
            }
            diff.SectionDiffs = rolled.Values.OrderBy(s => s.NRM2Section).ToList();

            diff.PlainSummary = BuildPlainSummary(diff);
            return diff;
        }

        /// <summary>Stable diff-matching key for a line — BOQ line ref when set,
        /// else category + item name. Shared by the snapshot diff and the Phase
        /// 2C drift check so both classify the same way.</summary>
        internal static string LineKey(BOQLineItem it)
        {
            if (it == null) return "";
            return !string.IsNullOrEmpty(it.BOQLineRef)
                ? "ref:" + it.BOQLineRef
                : "cat:" + (it.Category ?? "") + "|" + (it.ItemName ?? "");
        }

        private static Dictionary<string, BOQLineItem> IndexByKey(BOQDocument d)
        {
            var map = new Dictionary<string, BOQLineItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in d.AllItems) map[LineKey(it)] = it;
            return map;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Phase 2C — auto-reprice drifted lines. Re-runs the rate-provider
        //  chain (incl. the live feeds from 2B) for the given elements and
        //  pins the fresh rate via the model-override sidecar. Manual
        //  Override rows are left untouched. MUST run on the Revit API thread
        //  (reads element params through the provider chain).
        // ══════════════════════════════════════════════════════════════════

        internal sealed class RepriceOutcome
        {
            public int Considered;
            public int Repriced;
            public int SkippedOverride;
            public int NoRate;
            public int Unchanged;
            public double OldTotalUgx;
            public double NewTotalUgx;
            public List<string[]> Rows = new List<string[]>();   // [category, oldRate, newRate, source]
        }

        internal static RepriceOutcome RepriceElements(Document doc, IEnumerable<long> elementIds)
        {
            var outcome = new RepriceOutcome();
            if (doc == null || elementIds == null) return outcome;
            try
            {
                var csvRates = LoadCsvRates();
                var cobie = LoadCobieCostCodes();
                double ugxPerUsd = TagConfig.GetConfigDouble("UGX_PER_USD", 3700.0);
                double ugxPerGbp = TagConfig.GetConfigDouble("UGX_PER_GBP", 4700.0);
                var registry = RateProviderRegistry.Get(doc, csvRates, cobie, ugxPerUsd, ugxPerGbp);

                foreach (long id in elementIds.Distinct())
                {
                    Element el;
                    try { el = doc.GetElement(new ElementId(id)); }
                    catch (Exception ex) { StingLog.WarnRateLimited("Reprice.GetEl", $"GetElement({id}): {ex.Message}"); continue; }
                    if (el == null) continue;
                    outcome.Considered++;

                    // Never silently clobber a manual override.
                    string curSource = ParameterHelpers.GetString(el, "CST_RATE_SOURCE") ?? "";
                    if (string.Equals(curSource, "Override", StringComparison.OrdinalIgnoreCase))
                    { outcome.SkippedOverride++; continue; }

                    string catName = ParameterHelpers.GetCategoryName(el);
                    if (string.IsNullOrEmpty(catName)) continue;

                    var req = new RateRequest
                    {
                        CategoryName = catName,
                        Discipline = DisciplineForCategory(catName),
                        ProdCode = ParameterHelpers.GetString(el, ParamRegistry.PROD) ?? "",
                        MatCode = ParameterHelpers.GetString(el, "MAT_CODE") ?? "",
                        Unit = csvRates != null && csvRates.TryGetValue(catName, out var hint) ? hint.unit : "",
                        CurrencyCode = "UGX",
                        AsOf = DateTime.UtcNow,
                        Element = el
                    };

                    var lk = registry.Resolve(req);
                    if (lk == null || lk.UnitRate <= 0) { outcome.NoRate++; continue; }

                    double newRate = lk.UnitRate;   // already converted to UGX
                    double oldRate = 0;
                    double.TryParse(ParameterHelpers.GetString(el, "CST_UNIT_RATE_UGX"),
                        NumberStyles.Any, CultureInfo.InvariantCulture, out oldRate);

                    // Only pin a fresh rate when it actually moved (>1%) so we
                    // don't freeze every drifted line at an unchanged value.
                    if (oldRate > 0 && Math.Abs(newRate - oldRate) / oldRate <= 0.01)
                    { outcome.Unchanged++; outcome.OldTotalUgx += oldRate; outcome.NewTotalUgx += oldRate; continue; }

                    string src = MapProviderIdToLegacySource(lk.SourceId);
                    UpsertModelOverride(doc, new BOQModelOverride
                    {
                        UniqueId = el.UniqueId,
                        ElementId = id,
                        RateUGX = newRate,
                        RateUSD = ugxPerUsd > 0 ? Math.Round(newRate / ugxPerUsd, 2) : 0,
                        RateSource = src,
                        ModifiedBy = Environment.UserName ?? ""
                    });
                    outcome.Repriced++;
                    outcome.OldTotalUgx += oldRate;
                    outcome.NewTotalUgx += newRate;
                    outcome.Rows.Add(new[]
                    {
                        catName,
                        oldRate.ToString("N0", CultureInfo.InvariantCulture),
                        newRate.ToString("N0", CultureInfo.InvariantCulture),
                        src
                    });
                }
            }
            catch (Exception ex) { StingLog.Error("BOQ RepriceElements", ex); }
            return outcome;
        }

        private static BOQChangeType ClassifyChange(BOQLineItem a, BOQLineItem b)
        {
            if (a == null && b != null)
                return b.Source == BOQRowSource.ProvisionalSum ? BOQChangeType.PSAdded : BOQChangeType.NewItem;
            if (a != null && b == null) return BOQChangeType.ItemRemoved;
            if (a == null || b == null) return BOQChangeType.NoChange;
            if (a.Source != BOQRowSource.Model && b.Source == BOQRowSource.Model)
                return BOQChangeType.SourcePromoted;
            bool qtyChanged = a.Quantity > 0 && Math.Abs(b.Quantity - a.Quantity) / a.Quantity > 0.001;
            bool rateChanged = a.RateUGX > 0 && Math.Abs(b.RateUGX - a.RateUGX) / a.RateUGX > 0.01;
            if (rateChanged && !qtyChanged) return BOQChangeType.RateRevised;
            if (qtyChanged && !rateChanged) return BOQChangeType.QtyChanged;
            if (qtyChanged && rateChanged) return BOQChangeType.RateRevised; // dominant narrative
            return BOQChangeType.NoChange;
        }

        private static string BuildChangeReason(CategoryDiff cd, BOQLineItem a, BOQLineItem b)
        {
            switch (cd.ChangeType)
            {
                case BOQChangeType.RateRevised:
                    return $"Rate revised {cd.Name} UGX {cd.RateA:N0} → {cd.RateB:N0}/unit.";
                case BOQChangeType.QtyChanged:
                    string dir = cd.QtyB > cd.QtyA ? "increased" : "reduced";
                    return $"{cd.Name} {dir} {cd.QtyA:N1} → {cd.QtyB:N1} {b?.Unit ?? a?.Unit}.";
                case BOQChangeType.NewItem:
                    return $"{cd.QtyB:N0} {b?.Unit} newly modeled.";
                case BOQChangeType.ItemRemoved:
                    return "Removed since last snapshot.";
                case BOQChangeType.PSAdded:
                    return $"PC sum registered: {b?.Note ?? cd.Name}.";
                case BOQChangeType.SourcePromoted:
                    return "Promoted from manual row to modeled element.";
                default:
                    return "";
            }
        }

        private static string BuildPlainSummary(BOQSnapshotDiff d)
        {
            string sign = d.NetChange >= 0 ? "+" : "";
            var parts = new List<string>
            {
                $"Net movement between '{d.LabelA}' and '{d.LabelB}' is {sign}UGX {d.NetChange:N0} ({d.NetChangePct:+0.0;-0.0;0.0}%)."
            };
            var top = d.CategoryDiffs.OrderByDescending(c => Math.Abs(c.Delta)).Take(3).ToList();
            if (top.Count > 0)
            {
                parts.Add("Largest movements: " + string.Join("; ",
                    top.Select(c => $"{c.Name} {(c.Delta >= 0 ? "+" : "")}{c.Delta:N0}")) + ".");
            }
            if (Math.Abs(d.NetCarbonChange) > 1)
                parts.Add($"Embodied carbon moved by {d.NetCarbonChange:+#,##0;-#,##0;0} kgCO2e.");
            return string.Join(" ", parts);
        }

        // ══════════════════════════════════════════════════════════════════
        //  Manual store / provisional sum reconciliation
        // ══════════════════════════════════════════════════════════════════

        internal static string GetManualStorePath(Document doc)
            => Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc), "project_boq_manual.json");

        internal static BOQManualStore LoadManualStore(Document doc)
        {
            string path = GetManualStorePath(doc);
            if (!File.Exists(path)) return new BOQManualStore();
            try { return JsonConvert.DeserializeObject<BOQManualStore>(File.ReadAllText(path), _jsonSettings) ?? new BOQManualStore(); }
            catch (Exception ex) { StingLog.Warn($"LoadManualStore: {ex.Message}"); return new BOQManualStore(); }
        }

        internal static List<BOQLineItem> LoadManualRows(Document doc)
            => LoadManualStore(doc)?.ManualRows ?? new List<BOQLineItem>();

        internal static void SaveManualRows(Document doc, List<BOQLineItem> rows, double projectBudgetUgx)
        {
            var store = new BOQManualStore
            {
                SchemaVersion = "1.1",
                ProjectBudgetUGX = projectBudgetUgx,
                LastSaved = DateTime.UtcNow,
                LastSavedBy = Environment.UserName ?? "",
                ManualRows = rows ?? new List<BOQLineItem>()
            };
            string path = GetManualStorePath(doc);
            try
            {
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(store, _jsonSettings));
                if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
                else File.Move(tmp, path);
                StingLog.Info($"BOQ manual store saved: {store.ManualRows.Count} manual/PS rows, budget UGX {projectBudgetUgx:N0}");
            }
            catch (Exception ex) { StingLog.Error("SaveManualRows", ex); throw; }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Phase 108f — model-row override sidecar
        //  {projectDir}/_bim_manager/project_boq_model_overrides.json
        //  Survives the StingCommandHandler single-_commandTag race and any
        //  failures of the async BOQWriteItemParams ExternalEvent.
        // ══════════════════════════════════════════════════════════════════

        private static readonly object _overridesLock = new object();

        internal static string GetModelOverridesPath(Document doc)
            => Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc), "project_boq_model_overrides.json");

        internal static BOQModelOverridesStore LoadModelOverrides(Document doc)
        {
            string path = GetModelOverridesPath(doc);
            if (!File.Exists(path)) return new BOQModelOverridesStore();
            try { return JsonConvert.DeserializeObject<BOQModelOverridesStore>(File.ReadAllText(path), _jsonSettings) ?? new BOQModelOverridesStore(); }
            catch (Exception ex) { StingLog.Warn($"LoadModelOverrides: {ex.Message}"); return new BOQModelOverridesStore(); }
        }

        internal static void SaveModelOverrides(Document doc, BOQModelOverridesStore store)
        {
            if (store == null) return;
            store.LastSaved = DateTime.UtcNow;
            store.LastSavedBy = Environment.UserName ?? "";
            string path = GetModelOverridesPath(doc);
            lock (_overridesLock)
            {
                try
                {
                    string tmp = path + ".tmp";
                    File.WriteAllText(tmp, JsonConvert.SerializeObject(store, _jsonSettings));
                    if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
                    else File.Move(tmp, path);
                }
                catch (Exception ex) { StingLog.Error("SaveModelOverrides", ex); throw; }
            }
        }

        /// <summary>
        /// Upsert a single model-row override. Called from the WPF thread
        /// directly after the user commits a cell edit on a modeled row —
        /// no ExternalEvent hop, so the write is durable before the panel
        /// even finishes the CellEditEnding handler.
        /// </summary>
        internal static void UpsertModelOverride(Document doc, BOQModelOverride ov)
        {
            if (doc == null || ov == null) return;
            if (string.IsNullOrEmpty(ov.UniqueId) && ov.ElementId <= 0) return;
            lock (_overridesLock)
            {
                var store = LoadModelOverrides(doc);
                // Match by UniqueId first (stable), fall back to ElementId
                var existing = !string.IsNullOrEmpty(ov.UniqueId)
                    ? store.Overrides.FirstOrDefault(o => o.UniqueId == ov.UniqueId)
                    : store.Overrides.FirstOrDefault(o => o.ElementId == ov.ElementId);
                if (existing != null)
                {
                    if (ov.RateUGX.HasValue) existing.RateUGX = ov.RateUGX;
                    if (ov.RateUSD.HasValue) existing.RateUSD = ov.RateUSD;
                    if (ov.NRM2Paragraph != null) existing.NRM2Paragraph = ov.NRM2Paragraph;
                    if (ov.Note != null) existing.Note = ov.Note;
                    if (ov.RateSource != null) existing.RateSource = ov.RateSource;
                    existing.Modified = DateTime.UtcNow;
                    existing.ModifiedBy = Environment.UserName ?? "";
                    if (ov.ElementId > 0) existing.ElementId = ov.ElementId; // refresh the current-session id
                }
                else
                {
                    ov.Modified = DateTime.UtcNow;
                    ov.ModifiedBy = Environment.UserName ?? "";
                    store.Overrides.Add(ov);
                }
                SaveModelOverrides(doc, store);
            }
        }

        /// <summary>
        /// Apply all persisted model-row overrides onto freshly-built
        /// BOQLineItems. Called near the end of BuildBOQDocument after model
        /// items are constructed but before manual/PS items are merged.
        /// </summary>
        private static void ApplyModelOverrides(Document doc, BOQDocument boq)
        {
            if (doc == null || boq == null) return;
            BOQModelOverridesStore store;
            try { store = LoadModelOverrides(doc); }
            catch (Exception ex) { StingLog.Warn($"ApplyModelOverrides load: {ex.Message}"); return; }
            if (store?.Overrides == null || store.Overrides.Count == 0) return;

            // Index overrides by UniqueId (primary) and ElementId (fallback).
            var byUid = new Dictionary<string, BOQModelOverride>(StringComparer.Ordinal);
            var byEid = new Dictionary<long, BOQModelOverride>();
            foreach (var ov in store.Overrides)
            {
                if (!string.IsNullOrEmpty(ov.UniqueId)) byUid[ov.UniqueId] = ov;
                if (ov.ElementId > 0) byEid[ov.ElementId] = ov;
            }

            double rate = TagConfig.GetConfigDouble("UGX_PER_USD", 3700.0);
            int applied = 0;
            foreach (var item in boq.AllItems)
            {
                if (item.Source != BOQRowSource.Model) continue;
                BOQModelOverride ov = null;
                if (!string.IsNullOrEmpty(item.UniqueId)) byUid.TryGetValue(item.UniqueId, out ov);
                if (ov == null && item.RevitElementId > 0) byEid.TryGetValue(item.RevitElementId, out ov);
                if (ov == null) continue;

                if (ov.RateUGX.HasValue)
                {
                    item.RateUGX = ov.RateUGX.Value;
                    item.RateUSD = ov.RateUSD ?? (rate > 0 ? Math.Round(item.RateUGX / rate, 2) : 0);
                    item.RateSource = string.IsNullOrEmpty(ov.RateSource) ? "Override" : ov.RateSource;
                    item.RateConfidence = 100;
                    // G4 — a single-number manual override has no split; drop any
                    // inherited L/P/M so the columns don't show a stale breakdown.
                    item.LabourUGX = item.PlantUGX = item.MaterialUGX = null;
                }
                if (!string.IsNullOrEmpty(ov.NRM2Paragraph)) item.ResolvedNRM2Paragraph = ov.NRM2Paragraph;
                if (!string.IsNullOrEmpty(ov.Note)) item.Note = ov.Note;
                applied++;
            }
            if (applied > 0)
                StingLog.Info($"BOQ: applied {applied} model-row override(s) from sidecar.");
        }

        /// <summary>
        /// Identify candidate promotions from provisional sums to modeled
        /// elements. For each PS row, search modeled rows of the same category
        /// whose total is within ±30% of the PS total. Ranks by closeness.
        /// Caller confirms which matches to apply.
        /// </summary>
        internal static List<BOQReconcileMatch> ReconcileProvisionals(Document doc, BOQDocument boq)
        {
            var results = new List<BOQReconcileMatch>();
            if (boq == null) return results;
            var psRows = boq.AllItems.Where(i => i.Source == BOQRowSource.ProvisionalSum).ToList();
            var modeledByCategory = boq.AllItems
                .Where(i => i.Source == BOQRowSource.Model)
                .GroupBy(i => i.Category ?? "", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var ps in psRows)
            {
                if (!modeledByCategory.TryGetValue(ps.Category ?? "", out var candidates) || candidates.Count == 0)
                    continue;
                double psTotal = ps.TotalUGX;
                if (psTotal <= 0) continue;
                foreach (var mod in candidates)
                {
                    // Z-23 (6.6): rank by magnitude (closeness), but keep the SIGN so
                    // the QS sees overrun (+) vs credit-back (−). abs() alone hid it.
                    double signed = mod.TotalUGX - psTotal;
                    double diff = Math.Abs(signed);
                    double ratio = diff / psTotal;
                    if (ratio > 0.3) continue;
                    double confidence = Math.Round((1 - ratio) * 100, 0);
                    string direction = signed > 0 ? "overrun" : signed < 0 ? "credit" : "exact";
                    results.Add(new BOQReconcileMatch
                    {
                        PSRow = ps,
                        ModeledRow = mod,
                        ConfidencePct = confidence,
                        SignedDeltaUGX = signed,
                        Reason = $"{ps.Category} modeled is {ratio * 100:F0}% {direction} vs PS ({signed:+#,##0;-#,##0;0} UGX)"
                    });
                }
            }
            return results.OrderByDescending(m => m.ConfidencePct).ToList();
        }

        // ══════════════════════════════════════════════════════════════════
        //  Cash-flow generation wrapped around the BOQ
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Build a monthly cash-flow forecast JSON object using BOQ totals.
        /// Modeled costs distributed linearly across the active phase span,
        /// provisional costs placed at the end of the project (or instructed
        /// phase if the PS row's Note contains "phase:XXX"). Returns a JObject
        /// matching the shape consumed by Scheduling4DEngine.GenerateCashFlow
        /// downstream.
        /// </summary>
        internal static JObject GenerateCashFlowWithBOQ(Document doc, BOQDocument boq)
        {
            var root = new JObject();
            if (boq == null) return root;
            var monthly = new JArray();

            // Use the project's phases (if defined) to pick start + end months.
            DateTime start = DateTime.Now.Date;
            DateTime end = start.AddMonths(18);
            try
            {
                var phases = new FilteredElementCollector(doc).OfClass(typeof(Phase))
                    .Cast<Phase>().ToList();
                if (phases.Count >= 2)
                {
                    // Earliest + latest phase as rough project envelope — overridable by config.
                    start = DateTime.Now.Date;
                    end = DateTime.Now.AddMonths(Math.Max(6, phases.Count * 3));
                }
            }
            catch (Exception ex) { StingLog.Warn($"GenerateCashFlowWithBOQ phases: {ex.Message}"); }

            int months = Math.Max(1, (end.Year - start.Year) * 12 + (end.Month - start.Month) + 1);
            double modeledPerMonth = boq.ModeledTotalUGX / months;
            double runningTotal = 0;
            DateTime cursor = start;
            for (int i = 0; i < months; i++)
            {
                double thisMonth = modeledPerMonth;
                // PS rows at final month (simplest distribution — future work: parse "phase:" hints)
                if (i == months - 1) thisMonth += boq.ProvTotalUGX;
                runningTotal += thisMonth;
                monthly.Add(new JObject
                {
                    ["month"] = cursor.ToString("yyyy-MM"),
                    ["period_cost_ugx"] = Math.Round(thisMonth, 0),
                    ["cumulative_ugx"] = Math.Round(runningTotal, 0)
                });
                cursor = cursor.AddMonths(1);
            }

            root["project_name"] = boq.ProjectName;
            root["generated_at"] = DateTime.UtcNow.ToString("o");
            root["modeled_total_ugx"] = boq.ModeledTotalUGX;
            root["provisional_total_ugx"] = boq.ProvTotalUGX;
            root["grand_total_ugx"] = boq.GrandTotalUGX;
            root["budget_ugx"] = boq.ProjectBudgetUGX;
            root["monthly"] = monthly;
            return root;
        }

        // ══════════════════════════════════════════════════════════════════
        //  BOQ Health Score (Phase 11C)
        //  Weighted 0-100 scoring across seven factors. Surfaced as a KPI
        //  card in both the BOQ panel and the BIM Coordination Center.
        // ══════════════════════════════════════════════════════════════════

        // WP2 — categories that legitimately carry no measured cost.
        private static readonly HashSet<string> _freeCategoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Rooms", "Spaces", "Areas", "Zones", "HVAC Zones" };

        private static bool IsFreeCategoryForCost(string cat)
            => !string.IsNullOrEmpty(cat) && _freeCategoryNames.Contains(cat.Trim());

        private static bool IsMeasuredUnit(string unit)
        {
            switch ((unit ?? "").Trim().ToLowerInvariant())
            {
                case "each": case "item": case "nr": case "no": case "": return false;
                default: return true;
            }
        }

        /// <summary>
        /// WP2 — the document-level uncosted / at-risk rollup. The export confidence
        /// floor comes from COST_MIN_RATE_CONFIDENCE_EXPORT (default 60).
        /// </summary>
        internal static BoqUncostedRollup ComputeUncostedRollup(BOQDocument boq, double minConfidence)
        {
            var r = new BoqUncostedRollup();
            if (boq == null) return r;
            var model = boq.AllItems.Where(i => i.Source == BOQRowSource.Model).ToList();
            if (model.Count == 0) return r;

            // Proxy unit rates (median of priced rows) for the value-at-risk figure.
            var byUnit = model.Where(i => i.RateUGX > 0 && !string.IsNullOrEmpty(i.Unit))
                .GroupBy(i => i.Unit.Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => Median(g.Select(i => i.RateUGX)));
            double overallMedian = Median(model.Where(i => i.RateUGX > 0).Select(i => i.RateUGX));

            foreach (var i in model)
            {
                if (IsFreeCategoryForCost(i.Category)) continue;
                bool measured = IsMeasuredUnit(i.Unit);
                if (measured && i.Quantity <= 0.0001) r.CouldNotMeasureCount++;

                bool zeroRate = i.RateUGX <= 0 ||
                    string.Equals(i.RateSource, "None", StringComparison.OrdinalIgnoreCase);
                if (zeroRate)
                {
                    r.ZeroRateCount++;
                    double q = Math.Max(i.Quantity, 0);
                    r.QtyAtRisk += q;
                    double proxy = byUnit.TryGetValue((i.Unit ?? "").Trim().ToLowerInvariant(), out var m) ? m : overallMedian;
                    r.ValueAtRiskUGX += q * proxy;
                }
                else if (i.RateConfidence < minConfidence)
                {
                    r.LowConfidenceCount++;
                }
            }
            return r;
        }

        private static double Median(IEnumerable<double> values)
        {
            var list = values?.Where(v => v > 0).OrderBy(v => v).ToList();
            if (list == null || list.Count == 0) return 0;
            int mid = list.Count / 2;
            return list.Count % 2 == 1 ? list[mid] : (list[mid - 1] + list[mid]) / 2.0;
        }

        internal static double MinRateConfidenceForExport()
            => TagConfig.GetConfigDouble("COST_MIN_RATE_CONFIDENCE_EXPORT", 60.0);

        internal static BOQHealthScore ComputeBOQHealth(BOQDocument boq)
        {
            var score = new BOQHealthScore();
            if (boq == null || boq.AllItems.Count == 0)
            {
                score.Grade = "Poor";
                score.Issues.Add("No items in BOQ.");
                return score;
            }

            // Factor 1 — paragraph coverage (25 pts at 90%+)
            double paraPct = boq.ParagraphCoveragePct;
            score.ParagraphCoverageScore = paraPct >= 90 ? 25 : paraPct >= 70 ? 18 : paraPct >= 50 ? 10 : 3;

            // Factor 2 — rate confidence (20 pts at avg 75+)
            double avgConf = boq.AverageRateConfidence;
            score.RateConfidenceScore = avgConf >= 75 ? 20 : avgConf >= 60 ? 14 : avgConf >= 40 ? 8 : 2;

            // Factor 3 — token completeness (15 pts if no [token] remaining)
            int tokenStragglers = boq.AllItems.Count(i => _tokenRx.IsMatch(i.ResolvedNRM2Paragraph ?? ""));
            score.TokenCompletenessScore = tokenStragglers == 0 ? 15 : tokenStragglers <= 5 ? 10 : 3;

            // Factor 4 — line ref completeness (15 pts if all have a ref)
            int missingRefs = boq.AllItems.Count(i => string.IsNullOrEmpty(i.BOQLineRef));
            score.LineRefScore = missingRefs == 0 ? 15 : missingRefs <= 3 ? 10 : 4;

            // Factor 5 — budget (10 pts when budget set AND coverage within 80-110%)
            double cov = boq.BudgetCoveragePct;
            score.BudgetScore = boq.ProjectBudgetUGX > 0 && cov >= 80 && cov <= 110 ? 10
                : boq.ProjectBudgetUGX > 0 ? 5 : 0;

            // Factor 6 — PS description completeness (10 pts when all PS have a note)
            var ps = boq.AllItems.Where(i => i.Source == BOQRowSource.ProvisionalSum).ToList();
            int psMissing = ps.Count(i => string.IsNullOrWhiteSpace(i.Note) && string.IsNullOrWhiteSpace(i.ResolvedNRM2Paragraph));
            score.PSDescriptionScore = ps.Count == 0 ? 10 : psMissing == 0 ? 10 : psMissing <= 2 ? 6 : 2;

            // Factor 7 — carbon coverage (5 pts when ≥50% of items have carbon data)
            int withCarbon = boq.AllItems.Count(i => i.EmbodiedCarbonKg > 0);
            double carbonPct = 100.0 * withCarbon / boq.AllItems.Count;
            score.CarbonScore = carbonPct >= 50 ? 5 : carbonPct >= 25 ? 3 : 0;

            score.OverallScore = Math.Round(
                score.ParagraphCoverageScore + score.RateConfidenceScore +
                score.TokenCompletenessScore + score.LineRefScore +
                score.BudgetScore + score.PSDescriptionScore + score.CarbonScore, 0);

            // WP2 — uncosted / could-not-measure rows are a correctness problem,
            // not a cosmetic one: penalise (capped) so a bill with invisible
            // zero-value lines can't read "Excellent", and surface the exposure.
            var uncosted = ComputeUncostedRollup(boq, MinRateConfidenceForExport());
            if (uncosted.ZeroRateCount > 0)
            {
                score.OverallScore = Math.Max(0, score.OverallScore - Math.Min(15, uncosted.ZeroRateCount));
                score.Issues.Add($"{uncosted.ZeroRateCount} measured row(s) have NO rate — ≈ UGX {uncosted.ValueAtRiskUGX:N0} at risk (proxy median rates).");
                score.Recommendations.Add("Price the unrated rows (QS round-trip or rate library) before tender/professional export.");
            }
            if (uncosted.CouldNotMeasureCount > 0)
            {
                score.OverallScore = Math.Max(0, score.OverallScore - Math.Min(8, uncosted.CouldNotMeasureCount));
                score.Issues.Add($"{uncosted.CouldNotMeasureCount} measured row(s) could not be measured (quantity 0) — check geometry / takeoff rule unit.");
            }
            if (uncosted.LowConfidenceCount > 0)
                score.Recommendations.Add($"{uncosted.LowConfidenceCount} row(s) priced below the export confidence floor ({MinRateConfidenceForExport():F0}).");

            score.Grade = score.OverallScore >= 85 ? "Excellent"
                : score.OverallScore >= 70 ? "Good"
                : score.OverallScore >= 50 ? "Fair" : "Poor";

            // Issues + recommendations
            if (paraPct < 90)
                score.Issues.Add($"Paragraph coverage {paraPct:F0}% — {boq.AllItems.Count - boq.ResolvedParagraphCount} item(s) lack an NRM2 description.");
            if (avgConf < 75)
                score.Issues.Add($"Rate confidence average {avgConf:F0} — rates need verification or CSV overrides.");
            if (tokenStragglers > 0)
                score.Issues.Add($"{tokenStragglers} paragraph(s) still contain unresolved [tokens].");
            if (missingRefs > 0)
                score.Issues.Add($"{missingRefs} item(s) missing BOQ line reference.");
            if (boq.ProjectBudgetUGX <= 0)
                score.Recommendations.Add("Set a project budget via the BOQ panel Budget button.");
            if (psMissing > 0)
                score.Recommendations.Add($"Add scope notes to {psMissing} provisional sum(s) before handover.");
            if (carbonPct < 50)
                score.Recommendations.Add("Carbon coverage below 50% — populate MAT_CARBON_FACTOR on primary materials.");
            return score;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Utility helpers — private
        // ══════════════════════════════════════════════════════════════════

        // Internal so PlumbingBOQEnricher (and future supplemental builders)
        // can reuse the canonical CSV reader instead of duplicating it.
        internal static Dictionary<string, (double rate, string unit)> LoadCsvRates()
        {
            var rates = new Dictionary<string, (double rate, string unit)>(StringComparer.OrdinalIgnoreCase);
            string costFile = TagConfig.CostRatesFileName ?? "cost_rates_5d.csv";
            string path = StingToolsApp.FindDataFile(costFile);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return rates;
            try
            {
                string[] lines = File.ReadAllLines(path);
                if (lines.Length < 2) return rates;
                string header = lines[0].ToLowerInvariant();
                bool is7Col = header.Contains("mat_code");

                // CA-1 — explicit one-wins de-duplication. The first row for a key
                // wins (top of file is authoritative); a later duplicate is skipped
                // and logged, so a QS sees the collision instead of a silent
                // last-row-wins overwrite. Applies to category, MAT_CODE keys alike.
                int dupes = 0;
                void Put(string key, double rate, string unit)
                {
                    if (string.IsNullOrEmpty(key)) return;
                    string k = key.Trim();
                    if (rates.ContainsKey(k))
                    {
                        dupes++;
                        StingLog.WarnRateLimited("LoadCsvRates.Dupe",
                            $"LoadCsvRates: duplicate rate key '{k}' in {costFile} — keeping first, skipping later row.");
                        return;
                    }
                    rates[k] = (rate, string.IsNullOrEmpty(unit) ? "each" : unit);
                }

                for (int i = 1; i < lines.Length; i++)
                {
                    string[] cols = StingToolsApp.ParseCsvLine(lines[i]);
                    if (cols.Length < 3) continue;
                    if (is7Col && cols.Length >= 7)
                    {
                        // Category, MAT_CODE, MAT_DISCIPLINE, Unit_Rate_USD, Unit_Rate_UGX, Unit, Description
                        if (double.TryParse(cols[4], NumberStyles.Any, CultureInfo.InvariantCulture, out double rateUgx))
                        {
                            Put(cols[0], rateUgx, cols[5].Trim());
                            Put(cols[1], rateUgx, cols[5].Trim());
                        }
                    }
                    else if (double.TryParse(cols[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double rate3))
                    {
                        Put(cols[0], rate3, cols.Length > 2 ? cols[2].Trim() : "each");
                    }
                }
                if (dupes > 0)
                    StingLog.Warn($"LoadCsvRates: {dupes} duplicate rate key(s) in {costFile} skipped (first-wins).");
            }
            catch (Exception ex) { StingLog.Warn($"LoadCsvRates: {ex.Message}"); }
            return rates;
        }

        internal static Dictionary<string, string> LoadCobieCostCodes()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string path = StingToolsApp.FindDataFile("COBIE_TYPE_MAP.csv");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return map;
            try
            {
                string[] lines = File.ReadAllLines(path);
                if (lines.Length < 2) return map;
                var headers = StingToolsApp.ParseCsvLine(lines[0]).Select(h => h.ToLowerInvariant()).ToArray();
                int catCol = Array.FindIndex(headers, h => h.Contains("category"));
                int codeCol = Array.FindIndex(headers, h => h.Contains("cost") && h.Contains("code"));
                if (catCol < 0 || codeCol < 0) return map;
                for (int i = 1; i < lines.Length; i++)
                {
                    string[] cols = StingToolsApp.ParseCsvLine(lines[i]);
                    if (cols.Length <= Math.Max(catCol, codeCol)) continue;
                    string cat = cols[catCol].Trim();
                    string code = cols[codeCol].Trim();
                    if (!string.IsNullOrEmpty(cat) && !string.IsNullOrEmpty(code)) map[cat] = code;
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadCobieCostCodes: {ex.Message}"); }
            return map;
        }

        // P1.1 — non-measurable categories that must never reach takeoff.
        // 2D content (detail components, filled regions, lines, annotation,
        // text, dimensions) and linked/imported geometry carry no measurable
        // quantity and otherwise leak in as "Qty 1.000 each" noise rows
        // (e.g. a "Small Power legend.dwg" filled region repeated 275×).
        private static readonly HashSet<BuiltInCategory> _defaultExcludedBic = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_DetailComponents,
            BuiltInCategory.OST_FilledRegion,
            BuiltInCategory.OST_Lines,            // model + detail lines
            BuiltInCategory.OST_SketchLines,
            BuiltInCategory.OST_GenericAnnotation,
            BuiltInCategory.OST_TextNotes,
            BuiltInCategory.OST_Dimensions,
            BuiltInCategory.OST_RvtLinks,
            BuiltInCategory.OST_RasterImages,
        };

        /// <summary>
        /// Category display names the project wants excluded from takeoff, on
        /// top of <see cref="_defaultExcludedBic"/>. Data-driven via the config
        /// key COST_TAKEOFF_EXCLUDE_CATEGORIES (comma-separated). Empty by
        /// default so the hard-coded BIC set governs out of the box.
        /// </summary>
        private static HashSet<string> BuildExcludedCategoryNames()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string csv = TagConfig.GetConfigValue("COST_TAKEOFF_EXCLUDE_CATEGORIES");
                if (!string.IsNullOrWhiteSpace(csv))
                    foreach (var token in csv.Split(','))
                        if (!string.IsNullOrWhiteSpace(token)) set.Add(token.Trim());
            }
            catch (Exception ex) { StingLog.Warn($"BuildExcludedCategoryNames: {ex.Message}"); }
            return set;
        }

        /// <summary>
        /// STEP 6c — quantify elements in every loaded Revit link. Each link is
        /// aggregated on its own, then its rows are neutralised for host
        /// write-back (RevitElementId = -1, UniqueId cleared, constituents
        /// dropped) and tagged "[Linked: &lt;model&gt;]". Unloaded links are
        /// skipped. Quantities are parameter-derived, so the link transform does
        /// not affect them.
        /// </summary>
        /// <summary>P2.3 — count loaded RevitLinkInstances per linked-document Title.</summary>
        internal static Dictionary<string, int> CountLinkInstancesByTitle(Document doc)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (doc == null) return counts;
            try
            {
                foreach (var rli in new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
                {
                    Document ld = null; try { ld = rli.GetLinkDocument(); } catch { }
                    if (ld == null) continue;   // unloaded instance — not quantifiable
                    string t; try { t = ld.Title; } catch { continue; }
                    if (string.IsNullOrWhiteSpace(t)) continue;
                    counts[t] = counts.TryGetValue(t, out int n) ? n + 1 : 1;
                }
            }
            catch (Exception ex) { StingLog.Warn($"CountLinkInstancesByTitle: {ex.Message}"); }
            return counts;
        }

        private static List<BOQLineItem> CollectLinkedItems(
            Document doc, HashSet<string> knownCategories,
            Dictionary<string, (double rate, string unit)> csvRates,
            Dictionary<string, string> cobieCostCodes,
            BoqGroupingMode grouping,
            HashSet<string> includedTitles,
            IMeasurementStandard measStd)
        {
            var result = new List<BOQLineItem>();
            var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // P2.3 — count loaded link instances per Title so an opted-in,
            // multiply-placed link (e.g. mirrored wings) can be taken off ×N.
            var instanceCounts = CountLinkInstancesByTitle(doc);
            var multiplyMap = GetLinkMultiplyMap(doc);

            var links = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>();

            foreach (var rli in links)
            {
                Document ld = null;
                try { ld = rli.GetLinkDocument(); } catch { }
                if (ld == null) continue;   // link not loaded / not found

                string linkName;
                try { linkName = ld.Title; } catch { linkName = "link"; }

                // Per-link gate. Only links the user ticked are quantified; a
                // shared link placed multiple times is taken off exactly once.
                if (includedTitles != null && includedTitles.Count > 0 && !includedTitles.Contains(linkName)) continue;
                if (!seenTitles.Add(linkName)) continue;

                // P1.2 — reuse the raw per-link takeoff when it's already cached so a
                // host-side refresh doesn't re-walk this link's Revit DB.
                string cacheKey;
                try { cacheKey = string.IsNullOrEmpty(ld.PathName) ? linkName : ld.PathName; }
                catch { cacheKey = linkName; }

                List<BOQLineItem> rawItems = null;
                bool cacheHit;
                lock (_linkTakeoffCache)
                {
                    cacheHit = _linkTakeoffCache.TryGetValue(cacheKey, out var entry)
                        && entry?.RawItems != null;
                    if (cacheHit) rawItems = entry.RawItems.Select(x => x.Clone()).ToList();
                }

                if (!cacheHit)
                {
                    var linkEls = CollectCandidateElements(ld, knownCategories);
                    rawItems = new List<BOQLineItem>(linkEls.Count);
                    foreach (var el in linkEls)
                    {
                        var line = BuildLineItemFromElement(ld, el, csvRates, cobieCostCodes, measStd);
                        if (line != null) rawItems.Add(line);
                    }
                    // Store an isolated clone so a later caller mutating the returned
                    // rows can never corrupt the cache.
                    lock (_linkTakeoffCache)
                    {
                        _linkTakeoffCache[cacheKey] = new LinkTakeoffCacheEntry
                        { RawItems = rawItems.Select(x => x.Clone()).ToList() };
                    }
                    StingLog.Info($"BOQ linked-model takeoff (cache MISS — walked link): "
                        + $"{rawItems.Count} raw row(s) from '{linkName}'.");
                }
                else
                {
                    StingLog.Info($"BOQ linked-model takeoff (cache hit — link not re-walked): "
                        + $"{rawItems.Count} raw row(s) from '{linkName}'.");
                }

                // Aggregate + neutralise on the (cloned) raw rows every time so
                // grouping changes stay correct without invalidating the cache.
                var linkItems = AggregateLineItems(rawItems, grouping);

                // P2.3 — opted-in multiply-placed link: scale quantity + carbon
                // (cost is derived from Quantity) by the loaded-instance count.
                instanceCounts.TryGetValue(linkName, out int instCount);
                bool multiply = instCount > 1
                    && multiplyMap != null && multiplyMap.TryGetValue(linkName, out bool on) && on;

                foreach (var li in linkItems)
                {
                    li.RevitElementId = -1;                       // never resolve against the host doc
                    li.UniqueId = "";                             // avoid cross-doc id reuse
                    li.ConstituentElementIds = new List<long>();  // link-doc ids — not host-resolvable
                    li.SourceModel = linkName;                    // drives "Group by Source model"
                    string tag = multiply ? $"[Linked: {linkName} ×{instCount}]" : $"[Linked: {linkName}]";
                    if (multiply)
                    {
                        li.Quantity *= instCount;                 // TotalUGX/USD derive from Quantity
                        li.EmbodiedCarbonKg *= instCount;         // stored field — scale explicitly
                        li.BiogenicKg *= instCount;               // WP-C — biogenic scales with the count too
                    }
                    li.Note = string.IsNullOrEmpty(li.Note) ? tag : $"{li.Note} {tag}";
                }
                if (multiply)
                    StingLog.Info($"BOQ linked-model multiplier: '{linkName}' taken off ×{instCount} ({linkItems.Count} row(s)).");
                result.AddRange(linkItems);
            }
            return result;
        }

        private static List<Element> CollectCandidateElements(Document doc, HashSet<string> knownCategories)
        {
            var list = new List<Element>();
            var excludedNames = BuildExcludedCategoryNames();
            int excluded = 0, optionAlternates = 0;
            // WP2 — bill the MAIN model + each set's PRIMARY design option only;
            // never the alternates (which multiply quantities by the option count).
            // Configurable: set COST_BILL_PRIMARY_OPTION_ONLY = false to bill all.
            bool primaryOptionOnly = GetConfigBool("COST_BILL_PRIMARY_OPTION_ONLY", true);
            var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            var catEnums = SharedParamGuids.AllCategoryEnums;
            if (catEnums != null && catEnums.Length > 0)
                collector = collector.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));
            foreach (Element el in collector)
            {
                // P1.1 — CAD imports carry no measurable model quantity.
                if (el is ImportInstance) { excluded++; continue; }

                // WP2 — design-option double-count guard.
                if (primaryOptionOnly)
                {
                    try
                    {
                        var dopt = el.DesignOption;
                        if (dopt != null && !dopt.IsPrimary) { optionAlternates++; continue; }
                    }
                    catch (Exception ex) { StingLog.WarnRateLimited("BOQ.DesignOpt", $"DesignOption read: {ex.Message}"); }
                }

                // P1.1 — reject 2D / annotation / link categories.
                Category cObj = el.Category;
                if (cObj != null && cObj.Id != null && cObj.Id.Value < 0
                    && _defaultExcludedBic.Contains((BuiltInCategory)cObj.Id.Value))
                {
                    excluded++;
                    continue;
                }

                string cat = ParameterHelpers.GetCategoryName(el);
                if (string.IsNullOrEmpty(cat)) continue;
                if (excludedNames.Contains(cat)) { excluded++; continue; }
                if (!knownCategories.Contains(cat)) continue;
                if (cat.Equals("Rooms", StringComparison.OrdinalIgnoreCase)
                    || cat.Equals("Spaces", StringComparison.OrdinalIgnoreCase)
                    || cat.Equals("Areas", StringComparison.OrdinalIgnoreCase))
                    continue;
                list.Add(el);
            }
            if (excluded > 0)
                StingLog.Info($"BOQ takeoff: excluded {excluded} non-measurable element(s) " +
                              "(2D content / annotation / CAD imports).");
            if (optionAlternates > 0)
                StingLog.Info($"BOQ takeoff: skipped {optionAlternates} non-primary design-option " +
                              "alternate(s) (COST_BILL_PRIMARY_OPTION_ONLY).");
            return list;
        }

        private static bool IsPhaseDemolished(Document doc, Element el)
        {
            try
            {
                Parameter demP = el.get_Parameter(BuiltInParameter.PHASE_DEMOLISHED);
                if (demP != null && demP.HasValue)
                {
                    ElementId id = demP.AsElementId();
                    if (id != null && id.Value > 0) return true;
                }
            }
            catch (Exception ex) { StingLog.Warn($"IsPhaseDemolished: {ex.Message}"); }
            return false;
        }

        // ══════════════════════════════════════════════════════════════════
        //  P1.2 — Aggregation
        //  Collapse near-identical modelled line items into one row carrying
        //  the summed quantity + constituent element ids. Reversible: the
        //  panel drills down via ConstituentElementIds (P2). Manual/PS rows
        //  are NEVER aggregated — they pass through untouched.
        //  grouping: when a spatial grouping mode is active (P2.2) the matching
        //  spatial field (Level / Zone / Location) joins the key so similar
        //  items aggregate WITHIN a level/zone/room rather than across the
        //  whole project.
        // ══════════════════════════════════════════════════════════════════
        internal static List<BOQLineItem> AggregateLineItems(
            List<BOQLineItem> items, BoqGroupingMode grouping = BoqGroupingMode.WorkSection)
        {
            if (items == null || items.Count == 0) return items;
            if (!GetConfigBool("COST_AGGREGATE_SIMILAR", true)) return items;

            var result = new List<BOQLineItem>(items.Count);
            var modelRows = new List<BOQLineItem>();
            foreach (var it in items)
            {
                if (it.Source == BOQRowSource.Model) modelRows.Add(it);
                else result.Add(it);          // manual / PS pass through verbatim
            }

            // The spatial portion of the aggregation key tracks the grouping
            // dimension so a By-Level bill never merges identical items across
            // two levels into one row.
            string SpatialPart(BOQLineItem i)
            {
                switch (grouping)
                {
                    case BoqGroupingMode.Level:
                    case BoqGroupingMode.LevelThenWorkSection: return i.Level ?? "";
                    case BoqGroupingMode.Zone:                 return i.Zone ?? "";
                    case BoqGroupingMode.Location:             return i.Location ?? "";
                    // Phase 2E — WBS/CBS are assigned in a post-aggregate pass from
                    // (category/discipline/nrm2/level/zone), so keep level+zone in the
                    // aggregation key here so a By-WBS bill never merges rows that the
                    // map would file under different WBS codes.
                    case BoqGroupingMode.Wbs:
                    case BoqGroupingMode.Cbs:                  return (i.Level ?? "") + "~" + (i.Zone ?? "");
                    default:                                   return "";
                }
            }

            string Key(BOQLineItem i) => string.Join("|", new[]
            {
                i.NRM2Section ?? "", i.Category ?? "", i.Discipline ?? "",
                i.FamilyName ?? "", i.TypeName ?? "", i.Unit ?? "",
                SpatialPart(i)
            });

            double ugxPerUsd = TagConfig.GetConfigDouble("UGX_PER_USD", 3700.0);

            foreach (var grp in modelRows.GroupBy(Key))
            {
                var rows = grp.ToList();
                if (rows.Count == 1)
                {
                    var only = rows[0];
                    only.SimilarCount = 1;
                    only.AggregationKey = grp.Key;
                    only.ConstituentElementIds = only.RevitElementId >= 0
                        ? new List<long> { only.RevitElementId } : new List<long>();
                    result.Add(only);
                    continue;
                }

                // Representative = most-confident (then highest rate) so the
                // merged row keeps a real, defensible per-unit rate.
                var rep = rows.OrderByDescending(r => r.RateConfidence)
                              .ThenByDescending(r => r.RateUGX)
                              .First();
                var agg = rep.Clone();
                agg.Id = Guid.NewGuid().ToString("N");
                agg.SimilarCount = rows.Count;
                agg.AggregationKey = grp.Key;
                agg.Quantity = rows.Sum(r => r.Quantity);
                // Phase 2A — sum the measurement audit fields so the Gross/Deduct/
                // Waste columns stay consistent with the summed Net (= Quantity)
                // on a collapsed row, and rebuild the note for the aggregate.
                agg.GrossQuantity = rows.Sum(r => r.GrossQuantity);
                agg.DeductionQuantity = rows.Sum(r => r.DeductionQuantity);
                agg.WastageQuantity = rows.Sum(r => r.WastageQuantity);
                if (agg.GrossQuantity > 0)
                    agg.MeasurementNote = BuildAggregateMeasurementNote(agg, rows.Count);
                agg.EmbodiedCarbonKg = rows.Sum(r => r.EmbodiedCarbonKg);
                agg.BiogenicKg = rows.Sum(r => r.BiogenicKg);   // WP-C — biogenic aggregates with fossil
                agg.LifecycleCostUGX = rows.Sum(r => r.LifecycleCostUGX);
                agg.LifecycleCostInclCarbonUGX = rows.Sum(r => r.LifecycleCostInclCarbonUGX); // CA-4
                agg.RateConfidence = rows.Min(r => r.RateConfidence);
                agg.ConstituentElementIds = rows
                    .Where(r => r.RevitElementId >= 0)
                    .Select(r => r.RevitElementId)
                    .ToList();
                agg.Level = UniformOr(rows.Select(r => r.Level));
                agg.Location = UniformOr(rows.Select(r => r.Location));

                // Rate disagreement within a group → take the modal rate
                // (most-confident wins ties) and warn. Per-unit rates for the
                // same family+type should match; differences usually mean an
                // ad-hoc per-element override.
                var distinctRates = rows.Select(r => Math.Round(r.RateUGX, 2)).Distinct().ToList();
                if (distinctRates.Count > 1)
                {
                    double modalRate = rows
                        .GroupBy(r => Math.Round(r.RateUGX, 2))
                        .OrderByDescending(g => g.Count())
                        .ThenByDescending(g => g.Max(r => r.RateConfidence))
                        .First().Key;
                    agg.RateUGX = modalRate;
                    agg.RateUSD = ugxPerUsd > 0 ? Math.Round(modalRate / ugxPerUsd, 2) : 0;
                    // G4 — the representative's L/P/M split no longer sums to the
                    // chosen modal rate; drop it rather than show a misleading split.
                    agg.LabourUGX = agg.PlantUGX = agg.MaterialUGX = null;
                    StingLog.WarnRateLimited("BOQAggRate",
                        $"Aggregated row '{agg.ItemName}' had {distinctRates.Count} differing rates; " +
                        $"took modal {modalRate:N0} UGX across {rows.Count} elements.");
                }

                agg.Note = AppendNote(agg.Note, $"Aggregated {rows.Count} similar elements.");
                result.Add(agg);
            }
            return result;
        }

        /// <summary>Returns the shared value when every entry agrees, else a
        /// "(various)" sentinel so the row stays filterable.</summary>
        private static string UniformOr(IEnumerable<string> values, string sentinel = "(various)")
        {
            var distinct = values
                .Select(v => v ?? "")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return distinct.Count == 1 ? distinct[0] : sentinel;
        }

        private static string AppendNote(string existing, string addition)
        {
            if (string.IsNullOrEmpty(addition)) return existing;
            return string.IsNullOrEmpty(existing) ? addition : $"{existing} {addition}";
        }

        /// <summary>Reads a boolean config knob. Accepts true/false, 1/0,
        /// yes/no. Missing key → defaultValue.</summary>
        private static bool GetConfigBool(string key, bool defaultValue)
        {
            try
            {
                string raw = TagConfig.GetConfigValue(key);
                if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
                raw = raw.Trim().ToLowerInvariant();
                if (raw == "true" || raw == "1" || raw == "yes" || raw == "on") return true;
                if (raw == "false" || raw == "0" || raw == "no" || raw == "off") return false;
            }
            catch (Exception ex) { StingLog.Warn($"GetConfigBool({key}): {ex.Message}"); }
            return defaultValue;
        }

        // ══════════════════════════════════════════════════════════════════
        //  P2.2 — Grouping strategies.
        //  WorkSection = elemental NRM2 bill (default, unchanged). Level / Zone
        //  / Location = locational bills. LevelThenWorkSection = the proper
        //  NRM2 locational bill (level heading, NRM2 § within).
        // ══════════════════════════════════════════════════════════════════
        private static List<BOQSection> GroupIntoSections(
            List<BOQLineItem> items, BoqGroupingMode grouping = BoqGroupingMode.WorkSection)
        {
            switch (grouping)
            {
                case BoqGroupingMode.Level:
                    return GroupBySpatial(items, i => Blank(i.Level, "(no level)"));
                case BoqGroupingMode.Zone:
                    return GroupBySpatial(items, i => Blank(i.Zone, "(no zone)"));
                case BoqGroupingMode.Location:
                    return GroupBySpatial(items, i => Blank(i.Location, "(no location)"));
                case BoqGroupingMode.SourceModel:
                    return GroupBySpatial(items, i => Blank(i.SourceModel, "Host model"));
                case BoqGroupingMode.Wbs:
                    return GroupBySpatial(items, i => Blank(i.WbsCode, "(no WBS)"));
                case BoqGroupingMode.Cbs:
                    return GroupBySpatial(items, i => Blank(i.CbsCode, "(no CBS)"));
                case BoqGroupingMode.LevelThenWorkSection:
                    return GroupByLevelThenSection(items);
                case BoqGroupingMode.WorkSection:
                default:
                    return GroupByWorkSection(items);
            }
        }

        private static string Blank(string s, string fallback)
            => string.IsNullOrWhiteSpace(s) ? fallback : s;

        private static List<BOQLineItem> OrderItemsForSection(IEnumerable<BOQLineItem> items)
            => items.OrderBy(x => (int)x.Source)
                    .ThenBy(x => ParseSectionInt(x.NRM2Section))
                    .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.ItemName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

        private static List<BOQSection> GroupByWorkSection(List<BOQLineItem> items)
        {
            var groups = items
                .GroupBy(i => (i.NRM2Section ?? "00", i.Discipline ?? "X"))
                .OrderBy(g => ParseSectionInt(g.Key.Item1))
                .ThenBy(g => g.Key.Item2, StringComparer.OrdinalIgnoreCase);

            var sections = new List<BOQSection>();
            foreach (var g in groups)
            {
                sections.Add(new BOQSection
                {
                    NRM2Section = g.Key.Item1,
                    Discipline = g.Key.Item2,
                    Name = GuessSectionName(g.Key.Item1, g.First().Category),
                    Items = OrderItemsForSection(g)
                });
            }
            return sections;
        }

        /// <summary>One section per spatial value (level / zone / location).
        /// Items within a section read in NRM2 trade order. NRM2Section is left
        /// blank so AssignBoqLineRefs uses an ordinal prefix.</summary>
        private static List<BOQSection> GroupBySpatial(
            List<BOQLineItem> items, Func<BOQLineItem, string> keySelector)
        {
            var groups = items
                .GroupBy(keySelector)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            var sections = new List<BOQSection>();
            foreach (var g in groups)
            {
                var discs = g.Select(x => x.Discipline ?? "X")
                             .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                sections.Add(new BOQSection
                {
                    NRM2Section = "",                       // ordinal ref prefix
                    Discipline = discs.Count == 1 ? discs[0] : "*",
                    Name = g.Key,
                    Items = OrderItemsForSection(g)
                });
            }
            return sections;
        }

        /// <summary>Locational NRM2 bill — level heading, NRM2 § within. Keeps
        /// the numeric NRM2 ref prefix.</summary>
        private static List<BOQSection> GroupByLevelThenSection(List<BOQLineItem> items)
        {
            var groups = items
                .GroupBy(i => (Level: Blank(i.Level, "(no level)"),
                               Sec: i.NRM2Section ?? "00",
                               Disc: i.Discipline ?? "X"))
                .OrderBy(g => g.Key.Level, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => ParseSectionInt(g.Key.Sec))
                .ThenBy(g => g.Key.Disc, StringComparer.OrdinalIgnoreCase);

            var sections = new List<BOQSection>();
            foreach (var g in groups)
            {
                sections.Add(new BOQSection
                {
                    NRM2Section = g.Key.Sec,
                    Discipline = g.Key.Disc,
                    Name = $"{g.Key.Level} — {GuessSectionName(g.Key.Sec, g.First().Category)}",
                    Items = OrderItemsForSection(g)
                });
            }
            return sections;
        }

        private static int ParseSectionInt(string s)
        {
            if (int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out int v)) return v;
            return 99;
        }

        private static string GuessSectionName(string section, string firstCategory)
        {
            // Map common NRM2 sections to human-readable names. Fallback = category.
            switch ((section ?? "").Trim())
            {
                case "1": return "Demolitions";
                case "2": return "Substructure";
                case "3": return "Groundworks";
                case "4": return "Foundations";
                case "5": return "In-situ concrete";
                case "14": return "Masonry";
                case "15": return "Structural metalwork";
                case "16": return "Carpentry";
                case "17": return "Cladding and covering";
                case "18": return "Waterproofing";
                case "19": return "Linings, sheathing and dry partitioning";
                case "20": return "Windows, doors and stairs";
                case "21": return "Surface finishes";
                case "22": return "Furniture, fittings and equipment";
                case "23": return "Building fabric sundries";
                case "30": return "Drainage above ground";
                case "31": return "Drainage below ground";
                case "32": return "Piped supply systems";
                case "33": return "Mechanical services";
                case "34": return "Electrical services";
                case "35": return "Lighting and small power";
                case "36": return "Security and fire alarm";
                default: return string.IsNullOrEmpty(firstCategory) ? "General" : firstCategory;
            }
        }

        private static void AssignBoqLineRefs(BOQDocument boq)
        {
            int secOrdinal = 0;
            foreach (var section in boq.Sections)
            {
                secOrdinal++;
                // Work-section bills keep the numeric NRM2 prefix (e.g. "14.1.3");
                // spatial bills (blank NRM2Section) fall back to a document
                // section ordinal so refs stay unique + sequential.
                string prefix = string.IsNullOrWhiteSpace(section.NRM2Section)
                    ? secOrdinal.ToString(CultureInfo.InvariantCulture)
                    : section.NRM2Section;
                // PM-1 — the middle segment was a hard-coded "1" (every ref
                // {prefix}.1.{n}), so the promised hierarchy was dead and the wrong
                // ref was stamped onto elements. It now increments per sub-section
                // group (by Category) within the work section, with the row index
                // reset per group → a real NRM2-style {section}.{sub}.{item} ref.
                var subIndexByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var rowBySub = new Dictionary<int, int>();
                int nextSub = 0;
                foreach (var item in section.Items)
                {
                    string subKey = item.Category ?? "";
                    if (!subIndexByKey.TryGetValue(subKey, out int sub))
                    {
                        sub = ++nextSub;
                        subIndexByKey[subKey] = sub;
                    }
                    rowBySub.TryGetValue(sub, out int row);
                    row++;
                    rowBySub[sub] = row;
                    item.BOQLineRef = $"{prefix}.{sub}.{row}";
                }
            }
        }

        private static string DeriveNrm2Section(Document doc, Element el, string catName, string disc)
        {
            // P0 refactor — first consult the data-driven TakeoffRuleRegistry
            // so a QS can author section overrides in
            // STING_TAKEOFF_RULES.json / takeoff_rules.json without code
            // changes. Fall back to the legacy hard-coded map when no rule
            // matches.
            try
            {
                if (doc != null)
                {
                    string prod = ParameterHelpers.GetString(el, ParamRegistry.PROD) ?? "";
                    var rule = TakeoffRuleRegistry.Get(doc).Match(catName, disc, prod);
                    if (rule != null && !string.IsNullOrEmpty(rule.Nrm2Section))
                        return rule.Nrm2Section;
                }
            }
            catch (Exception ex) { StingLog.Warn($"DeriveNrm2Section rule lookup: {ex.Message}"); }

            if (string.IsNullOrEmpty(catName)) return "99";
            string lower = catName.ToLowerInvariant();
            // Hardcoded mapping covering the common Revit categories. QS can override via
            // ASS_BOQ_SECTION_NAME / CATEGORY_NRM2_MAP config key (future work).
            if (lower.Contains("foundation")) return "4";
            if (lower.Contains("column") || lower.Contains("framing") || lower.Contains("truss") || lower.Contains("beam")) return "15";
            if (lower.Contains("wall") && !lower.Contains("curtain")) return "14";
            if (lower.Contains("floor") || lower.Contains("slab")) return "5";
            if (lower.Contains("roof") || lower.Contains("fascia") || lower.Contains("gutter")) return "17";
            if (lower.Contains("door") || lower.Contains("window") || lower.Contains("stair") || lower.Contains("ramp")) return "20";
            if (lower.Contains("ceiling")) return "19";
            if (lower.Contains("curtain") || lower.Contains("mullion")) return "17";
            if (lower.Contains("furniture") || lower.Contains("casework") || lower.Contains("equipment")) return "22";
            if (lower.Contains("duct") || lower.Contains("pipe") || lower.Contains("mechanical")) return "33";
            if (lower.Contains("plumbing") || lower.Contains("sanitary")) return "32";
            if (lower.Contains("electrical") || lower.Contains("conduit") || lower.Contains("cable")) return "34";
            if (lower.Contains("lighting")) return "35";
            if (lower.Contains("fire") || lower.Contains("security") || lower.Contains("nurse")) return "36";
            return disc == "S" ? "15" : disc == "M" ? "33" : disc == "E" ? "34" : disc == "P" ? "32" : "23";
        }

        private static string DisciplineForCategory(string catName)
        {
            if (string.IsNullOrEmpty(catName)) return "X";
            if (TagConfig.DiscMap != null && TagConfig.DiscMap.TryGetValue(catName, out string disc)) return disc;
            return "X";
        }

        /// <summary>
        /// WP2 — prefer the element's own ISO-19650 discipline token
        /// (ASS_DISCIPLINE_COD_TXT) over the category default, so a service
        /// modelled on a dual-use or mis-mapped category classifies + matches
        /// take-off rules by its real discipline. Falls back to the category map.
        /// </summary>
        private static string ResolveDiscipline(Element el, string catName)
        {
            try
            {
                string d = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                if (!string.IsNullOrWhiteSpace(d)) return d.Trim();
            }
            catch (Exception ex) { StingLog.WarnRateLimited("ResolveDisc", $"ResolveDiscipline: {ex.Message}"); }
            return DisciplineForCategory(catName);
        }

        private static string MakeSafeFileName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "item";
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars().Concat(new[] { ' ', '/', '\\' }));
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s) sb.Append(invalid.Contains(c) ? '-' : c);
            string r = sb.ToString().Trim('-');
            return string.IsNullOrEmpty(r) ? "item" : r;
        }

        private static string GetElementDisplayName(Element el)
        {
            string fam = GetFamilyName(el);
            string typ = el.Name ?? "";
            if (!string.IsNullOrEmpty(fam) && !string.IsNullOrEmpty(typ) && !fam.Equals(typ, StringComparison.OrdinalIgnoreCase))
                return $"{fam} — {typ}";
            return !string.IsNullOrEmpty(typ) ? typ : fam;
        }

        private static string GetFamilyName(Element el)
        {
            try
            {
                if (el is FamilyInstance fi) return fi.Symbol?.Family?.Name ?? "";
                var typeId = el.GetTypeId();
                if (typeId != null && typeId.Value > 0)
                {
                    Element t = el.Document.GetElement(typeId);
                    if (t is FamilySymbol fs) return fs.Family?.Name ?? "";
                    if (t != null) return t.Name ?? "";
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetFamilyName: {ex.Message}"); }
            return "";
        }

        private static string GetLevelName(Document doc, Element el)
        {
            try
            {
                Parameter lp = el.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
                if (lp != null && lp.HasValue) return lp.AsValueString() ?? lp.AsString() ?? "";
                ElementId lvlId = el.LevelId;
                if (lvlId != null && lvlId.Value > 0)
                {
                    Element lv = doc.GetElement(lvlId);
                    if (lv != null) return lv.Name;
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetLevelName: {ex.Message}"); }
            return "";
        }

        private static string GetLocationName(Document doc, Element el)
        {
            // Prefer ASS_LOC_TXT if tagged; otherwise room.
            string loc = ParameterHelpers.GetString(el, "ASS_LOC_TXT");
            if (!string.IsNullOrEmpty(loc)) return loc;
            try
            {
                var room = ParameterHelpers.GetRoomAtElement(doc, el);
                if (room != null) return room.Name ?? "";
            }
            catch (Exception ex) { StingLog.Warn($"GetLocationName: {ex.Message}"); }
            return "";
        }

        private static string GetZoneName(Element el)
        {
            // P2.2 — zone grouping key. Prefer the ISO 19650 ZONE token.
            string zone = ParameterHelpers.GetString(el, "ASS_ZONE_TXT");
            return string.IsNullOrEmpty(zone) ? "" : zone;
        }

        private static string GetPrimaryMaterialName(Element el)
        {
            try
            {
                var ids = el.GetMaterialIds(false);
                if (ids != null && ids.Count > 0)
                {
                    // WP2 — deterministic: the DOMINANT material by volume, not the
                    // non-deterministic .First(), so a compound assembly's density /
                    // carbon / description don't flip between sessions.
                    ElementId best = ids.First();
                    double bestVol = -1;
                    foreach (var id in ids)
                    {
                        double v;
                        try { v = el.GetMaterialVolume(id); } catch { v = 0; }
                        if (v > bestVol) { bestVol = v; best = id; }
                    }
                    Material m = el.Document.GetElement(best) as Material;
                    if (m != null) return m.Name ?? "";
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetPrimaryMaterialName: {ex.Message}"); }
            return "";
        }

        // Z-23b — discipline detection for the opt-in measured additions.
        // Only consulted when the knobs are enabled (default 0 → never fires).
        private static bool IsRebarElement(Element el)
        {
            try
            {
                if (el?.Category != null)
                {
                    if (el.Category.Id.Value == (long)BuiltInCategory.OST_Rebar) return true;
                    string cat = el.Category.Name?.ToLowerInvariant() ?? "";
                    if (cat.Contains("rebar") || cat.Contains("reinforc")) return true;
                }
                string m = GetPrimaryMaterialName(el).ToLowerInvariant();
                return m.Contains("rebar") || m.Contains("reinforc");
            }
            catch (Exception ex) { StingLog.WarnRateLimited("IsRebar", $"IsRebarElement: {ex.Message}"); return false; }
        }

        private static bool IsConcreteElement(Element el)
        {
            try
            {
                return GetPrimaryMaterialName(el).ToLowerInvariant().Contains("concrete");
            }
            catch (Exception ex) { StingLog.WarnRateLimited("IsConcrete", $"IsConcreteElement: {ex.Message}"); return false; }
        }
    }
}
