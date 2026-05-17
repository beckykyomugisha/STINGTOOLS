using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace StingTools.Core
{
    // ════════════════════════════════════════════════════════════════════════════
    //  REAL-TIME AUTO-TAGGER (IUpdater)
    //
    //  Registers a DocumentChanged listener that auto-tags newly placed elements
    //  the moment they appear in the model. This eliminates the need for manual
    //  batch tag runs — true zero-touch BIM.
    //
    //  Registration: Called from StingToolsApp.OnStartup()
    //  Trigger: Element addition in any tagged category
    //  Action: SpatialAutoDetect + PopulateAll + BuildAndWriteTag inline
    //  Performance: Suppresses redundant triggers via HashSet of processed IDs
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// IUpdater that auto-tags elements when they are placed in the model.
    /// Register via StingAutoTagger.Register(app) on startup.
    /// Toggle on/off via AutoTaggerToggleCommand.
    /// </summary>
    public class StingAutoTagger : IUpdater
    {
        private static StingAutoTagger _instance;
        private static UpdaterId _updaterId;
        private static bool _enabled;
        // CRASH FIX: Lock protects _recentlyProcessed from concurrent access
        // if IUpdater fires during command execution or document switching
        private static readonly object _processedLock = new object();
        private static readonly HashSet<long> _recentlyProcessed = new HashSet<long>();
        private static readonly Queue<long> _recentlyProcessedQueue = new Queue<long>();
        private static volatile int _processedCount;
        private static bool _visualTaggingEnabled = false;
        private static HashSet<string> _allowedDiscs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // D1: Cached context to avoid rebuilding PopulationContext on every trigger
        private static TokenAutoPopulator.PopulationContext _cachedCtx;
        private static HashSet<string> _cachedExistingTags;
        private static Dictionary<string, int> _cachedSeqCounters;
        private static bool _contextInvalid = true;
        // PERF-07: TTL-based context rebuild to avoid redundant rebuilds in multi-command workflows.
        // 30 s matches SpatialAutoDetect room-index TTL so a typical auto-tag → save → combine
        // chain (≤10 s) never triggers a redundant context rebuild.
        private static DateTime _contextCacheTime = DateTime.MinValue;
        private const int ContextCacheTtlMs = 30000;
        // G2.3: Cached formulas and grid lines for pipeline helper
        private static List<Temp.FormulaEngine.FormulaDefinition> _formulas;
        private static List<Grid> _gridLines;

        // LOG-04: Token hash cache to skip redundant TAG7 rebuilds
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, string>
            _tag7HashCache = new System.Collections.Concurrent.ConcurrentDictionary<long, string>();

        // A-6: bounded LRU eviction for _tag7HashCache to prevent unbounded
        // memory growth in long sessions. Mirrors the _elementVersionHash
        // 20%-eviction pattern (~lines 790-802) — once the cache exceeds
        // _tag7CacheCap entries, the oldest 20% are dropped.
        private const int _tag7CacheCap = 10000;

        /// <summary>
        /// A-6: store a TAG7 hash for an element id. Performs the same
        /// 20%-eviction-at-cap dance used elsewhere so callers don't have to.
        /// </summary>
        public static void StoreTag7Hash(long elementId, string hash)
        {
            if (string.IsNullOrEmpty(hash)) return;
            _tag7HashCache[elementId] = hash;

            if (_tag7HashCache.Count > _tag7CacheCap)
            {
                int original = _tag7HashCache.Count;
                int target = original / 5;
                int evicted = 0;
                foreach (var kvp in _tag7HashCache)
                {
                    if (evicted >= target) break;
                    _tag7HashCache.TryRemove(kvp.Key, out _);
                    evicted++;
                }
                StingLog.Info($"_tag7HashCache: evicted {evicted} of {original} entries (cap {_tag7CacheCap}).");
            }
        }

        /// <summary>A-6: read a previously cached TAG7 hash; returns null when absent.</summary>
        public static string TryGetTag7Hash(long elementId)
        {
            if (_tag7HashCache.TryGetValue(elementId, out var h))
            {
                StingLog.RecordHit(StingLog.CacheKind.Tag7Hash); // E-1
                return h;
            }
            StingLog.RecordMiss(StingLog.CacheKind.Tag7Hash); // E-1
            return null;
        }

        // BUG-04: ExternalEvent queue for deferred tag processing
        private static readonly ConcurrentQueue<ElementId> _pendingQueue = new ConcurrentQueue<ElementId>();
        private static ExternalEvent _autoTagEvent;
        private static AutoTagQueueHandler _autoTagHandler;

        // R-02: Deferred queue for elements skipped due to workset ownership
        // Drained on DocumentSynchronizedWithCentral to retry after sync
        private static readonly ConcurrentQueue<ElementId> _deferredElements = new ConcurrentQueue<ElementId>();

        // AT-01: Cap deferred queue to prevent unbounded memory growth if sync never happens
        private const int MaxDeferredQueueSize = 5000;

        /// <summary>R-02: Enqueue an element ID for retry after sync-to-central.</summary>
        // M-06 FIX: Track dropped elements for diagnostic purposes
        private static int _droppedElementCount = 0;

        /// <summary>Phase 78: Track dropped element IDs for sidecar recovery.</summary>
        private static readonly System.Collections.Concurrent.ConcurrentBag<long> _droppedElementIds = new();

        /// <summary>Cap for the _droppedElementIds bag to avoid unbounded memory growth
        /// when large batches repeatedly overflow the deferred queue.</summary>
        private const int MaxDroppedIdsBagSize = 50_000;

        public static void EnqueueDeferred(ElementId id)
        {
            // SAFETY-002: The Count check and subsequent Enqueue are NOT atomic.
            // A concurrent thread could push Count over MaxDeferredQueueSize between
            // the check and the Enqueue.  This is an accepted benign race — the queue
            // may transiently exceed the cap by the number of concurrent callers,
            // which is bounded and harmless.  Full locking would serialize all IUpdater
            // triggers and is not worth the throughput cost.
            if (_deferredElements.Count >= MaxDeferredQueueSize)
            {
                _droppedElementCount++;
                // SAFETY-003: Cap the dropped-IDs bag to prevent unbounded memory growth
                // when elements overflow repeatedly (e.g. batch import without a central sync).
                if (_droppedElementIds.Count < MaxDroppedIdsBagSize)
                    _droppedElementIds.Add(id.Value); // Phase 78: Track for sidecar recovery
                // M-06 FIX: Throttle logging — only log every 100th drop to avoid log spam
                // but use Error level (not Warn) so it's visible in diagnostics
                if (_droppedElementCount <= 5 || _droppedElementCount % 100 == 0)
                    StingLog.Error($"AutoTagger: deferred queue at capacity ({MaxDeferredQueueSize}), " +
                        $"dropped {_droppedElementCount} elements total. Sync to central to drain queue.");
                return;
            }
            _deferredElements.Enqueue(id);
        }

        /// <summary>Phase 78: Persist dropped element IDs to sidecar for retry on next session.
        /// Called on document close to ensure no elements are permanently lost from auto-tagger.</summary>
        public static void SaveDroppedElementsSidecar(Document doc)
        {
            if (_droppedElementIds.IsEmpty) return;
            try
            {
                string projectPath = doc?.PathName;
                if (string.IsNullOrEmpty(projectPath)) return;
                string sidecarPath = ProjectFolderEngine.GetDataPath(doc, "deferred_elements.json");
                if (string.IsNullOrEmpty(sidecarPath))
                    sidecarPath = System.IO.Path.ChangeExtension(projectPath, ".sting_deferred_elements.json");
                var ids = _droppedElementIds.ToArray();
                string json = $"{{\"version\":\"1.0\",\"timestamp\":\"{DateTime.Now:o}\",\"dropped_count\":{ids.Length},\"element_ids\":[{string.Join(",", ids)}]}}";
                string tempPath = sidecarPath + ".tmp";
                System.IO.File.WriteAllText(tempPath, json);
                System.IO.File.Move(tempPath, sidecarPath, true);
                StingLog.Info($"AutoTagger: saved {ids.Length} dropped element IDs to sidecar for recovery");

                // TAG-DEFERRED-OVERFLOW-01: Clear the in-memory bag so the next document
                // doesn't inherit dropped IDs from the previous one. The sidecar is the
                // durable record from this point forward.
                while (_droppedElementIds.TryTake(out _)) { }
                _droppedElementCount = 0;
            }
            catch (Exception ex) { StingLog.Warn($"Deferred sidecar save: {ex.Message}"); }
        }

        /// <summary>TAG-DEFERRED-OVERFLOW-01: On document open, restore element IDs that
        /// previously overflowed the deferred queue and re-enqueue any that still resolve
        /// to live elements in the model. Loaded once per document; the sidecar is rotated
        /// to a `.consumed` filename so a second open does not re-queue stale IDs.
        /// Returns the number of elements re-enqueued.</summary>
        public static int LoadDroppedElementsSidecar(Document doc)
        {
            try
            {
                string projectPath = doc?.PathName;
                if (string.IsNullOrEmpty(projectPath)) return 0;
                string sidecarPath = ProjectFolderEngine.GetDataPath(doc, "deferred_elements.json");
                if (string.IsNullOrEmpty(sidecarPath) || !System.IO.File.Exists(sidecarPath))
                    sidecarPath = System.IO.Path.ChangeExtension(projectPath, ".sting_deferred_elements.json");
                if (!System.IO.File.Exists(sidecarPath)) return 0;

                string json = System.IO.File.ReadAllText(sidecarPath);
                var parsed = Newtonsoft.Json.Linq.JObject.Parse(json);
                var idArr = parsed["element_ids"] as Newtonsoft.Json.Linq.JArray;
                if (idArr == null || idArr.Count == 0)
                {
                    // Empty sidecar — clean up and exit.
                    try { System.IO.File.Delete(sidecarPath); } catch { /* best effort */ }
                    return 0;
                }

                int restored = 0;
                int missing = 0;
                foreach (var token in idArr)
                {
                    // CS7036 fix: JToken.Value<T>() requires a key when called on JObject;
                    // for a JArray element we want the scalar conversion via cast / ToObject<T>().
                    long raw = (long)token;
                    ElementId id = new ElementId(raw);
                    Element el = null;
                    try { el = doc.GetElement(id); } catch { /* ignore — id may belong to a deleted element */ }
                    if (el == null || !el.IsValidObject) { missing++; continue; }
                    EnqueueDeferred(id);
                    restored++;
                }

                // Rotate the sidecar so a re-open of the same document doesn't replay it.
                try
                {
                    string consumed = sidecarPath + ".consumed";
                    if (System.IO.File.Exists(consumed)) System.IO.File.Delete(consumed);
                    System.IO.File.Move(sidecarPath, consumed);
                }
                catch (Exception rotEx) { StingLog.Warn($"Deferred sidecar rotate: {rotEx.Message}"); }

                StingLog.Info($"AutoTagger: restored {restored} dropped element IDs from sidecar " +
                    $"({missing} no longer resolved). Will retry on next sync-to-central.");
                return restored;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Deferred sidecar load: {ex.Message}");
                return 0;
            }
        }

        /// <summary>Phase 78: Get dropped element count for dashboard display.</summary>
        public static int DroppedElementCount => _droppedElementCount;

        /// <summary>R-02: Drain and discard the deferred queue (call on DocumentClosed).</summary>
        public static void ClearDeferredQueue()
        {
            while (_deferredElements.TryDequeue(out _)) { }
        }

        /// <summary>R-02: Get deferred elements for retry processing.</summary>
        public static List<ElementId> DrainDeferredQueue()
        {
            var result = new List<ElementId>();
            while (_deferredElements.TryDequeue(out ElementId id))
                result.Add(id);
            return result;
        }

        /// <summary>Invalidate cached context (call after external tagging operations).</summary>
        public static void InvalidateContext()
        {
            _contextInvalid = true;
            _tag7HashCache.Clear();
            // PERF-R2: Do NOT clear _elementVersionHash here — it tracks geometry changes
            // for stale detection, not tag state. Clearing it causes ALL previously-marked
            // elements to be re-marked as stale on their next modification even if nothing changed.
            // FIX-N04: Reset failure counter so auto-tagger can recover after external fixes
            _consecutiveFailures = 0;
            WasAutoDisabled = false;
            // R2-FIX: Clear container cache so reloaded ParamRegistry is reflected
            ParamRegistry.ClearContainerCache();
            // A3: Clear processed cache on context invalidation to prevent stale-skip on document reload
            lock (_processedLock)
            {
                _recentlyProcessed.Clear();
                _recentlyProcessedQueue.Clear();
                _processedCount = 0;
            }
        }

        private readonly AddInId _addinId;

        public StingAutoTagger(AddInId addinId)
        {
            _addinId = addinId;
            _updaterId = new UpdaterId(addinId, new Guid("B3F7A9C1-D5E2-4F8A-9B1C-E7D3F6A2B8C4"));
        }

        public UpdaterId GetUpdaterId() => _updaterId;
        public string GetUpdaterName() => "STING Auto-Tagger";
        public string GetAdditionalInformation() => "Auto-tags newly placed elements with ISO 19650 codes";
        public ChangePriority GetChangePriority() => ChangePriority.FloorsRoofsStructuralWalls;

        public static bool IsEnabled => _enabled;
        public static int ProcessedCount => _processedCount;
        /// <summary>True if the auto-tagger was disabled automatically due to repeated failures.</summary>
        public static bool WasAutoDisabled { get; private set; }

        /// <summary>
        /// Register the updater with Revit. Called from OnStartup.
        /// Starts in disabled state — user must toggle on explicitly.
        ///
        /// CRASH FIX: Do NOT add triggers at registration.  Even when disabled
        /// via DisableUpdater(), Revit still evaluates the 22-category trigger
        /// filter on every element change in the document.  For large operations
        /// (815 materials, batch paste, etc.) this adds overhead and — combined
        /// with co-loaded plugins that also have updaters — can destabilise
        /// Revit's internal change processing pipeline.
        ///
        /// Triggers are now only added when the user explicitly enables the
        /// auto-tagger via Toggle(), and removed when they disable it.
        /// </summary>
        public static void Register(UIControlledApplication application)
        {
            try
            {
                _instance = new StingAutoTagger(application.ActiveAddInId);
                UpdaterRegistry.RegisterUpdater(_instance, true);

                // NO triggers added here — they are added when user enables
                // the auto-tagger via Toggle() and removed when they disable it.
                // This prevents Revit from evaluating the 22-category filter on
                // every element change while the auto-tagger is off.
                UpdaterRegistry.DisableUpdater(_updaterId);
                _enabled = false;

                // BUG-04: Create ExternalEvent handler for deferred tag processing
                _autoTagHandler = new AutoTagQueueHandler();
                _autoTagEvent = ExternalEvent.Create(_autoTagHandler);
                StingLog.Info("StingAutoTagger: registered (disabled by default, no triggers)");
            }
            catch (Exception ex)
            {
                StingLog.Error("StingAutoTagger: registration failed", ex);
            }
        }

        /// <summary>Unregister on shutdown.</summary>
        public static void Unregister()
        {
            try
            {
                if (_updaterId != null)
                    UpdaterRegistry.UnregisterUpdater(_updaterId);
                StingLog.Info("StingAutoTagger: unregistered");
            }
            catch (Exception ex) { StingLog.Warn($"StingAutoTagger unregister failed: {ex.Message}"); }
        }

        /// <summary>Toggle the auto-tagger on/off.</summary>
        public static bool Toggle()
        {
            if (_updaterId == null) return false;

            if (_enabled)
            {
                // Remove triggers so Revit stops evaluating the category filter
                try { UpdaterRegistry.RemoveAllTriggers(_updaterId); }
                catch (Exception ex) { StingLog.Warn($"StingAutoTagger: RemoveAllTriggers: {ex.Message}"); }

                UpdaterRegistry.DisableUpdater(_updaterId);
                _enabled = false;
                _contextInvalid = true;
                StingLog.Info("StingAutoTagger: disabled (triggers removed)");
            }
            else
            {
                // Add triggers for element addition in tagged categories
                try
                {
                    var multiCatFilter = CreateMultiCategoryFilter();
                    UpdaterRegistry.AddTrigger(_updaterId, multiCatFilter,
                        Element.GetChangeTypeElementAddition());
                }
                catch (Exception ex)
                {
                    StingLog.Error("StingAutoTagger: failed to add triggers", ex);
                    return false;
                }

                UpdaterRegistry.EnableUpdater(_updaterId);
                _enabled = true;
                WasAutoDisabled = false;
                _consecutiveFailures = 0;
                // PERF-R2: Acquire lock before clearing to prevent ConcurrentModificationException
                lock (_processedLock)
                {
                    _recentlyProcessed.Clear();
                    _recentlyProcessedQueue.Clear();
                    _processedCount = 0;
                }
                StingLog.Info("StingAutoTagger: enabled (triggers active)");
            }
            return _enabled;
        }

        /// <summary>Enable or disable visual tag placement during auto-tagging.
        /// FIX-10.1: Persists the setting to project_config.json.</summary>
        public static void SetVisualTagging(bool enabled)
        {
            _visualTaggingEnabled = enabled;
            try { TagConfig.SetConfigValue("AUTO_TAGGER_VISUAL", enabled); }
            catch (Exception ex) { StingLog.Warn($"SetVisualTagging persist: {ex.Message}"); }
        }
        /// <summary>FIX-10.2: Set visual tagging state without persisting (used during config load to avoid re-save loop).</summary>
        public static void SetVisualTaggingQuiet(bool enabled) { _visualTaggingEnabled = enabled; }
        /// <summary>Get current visual tagging state.</summary>
        public static bool IsVisualTaggingEnabled => _visualTaggingEnabled;

        /// <summary>Set the allowed discipline filter. Empty set = all disciplines.
        /// GAP-AT-03: Also persists to project_config.json for restoration on document open.</summary>
        public static void SetDisciplineFilter(IEnumerable<string> discs)
        {
            _allowedDiscs = discs != null
                ? new HashSet<string>(discs, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Persist filter to config so it survives document close/reopen
            try
            {
                TagConfig.SetConfigValue("AUTO_TAGGER_DISC_FILTER",
                    _allowedDiscs.Count > 0 ? string.Join(",", _allowedDiscs) : "");
            }
            catch (Exception ex) { StingLog.Warn($"Persist disc filter: {ex.Message}"); }
        }

        /// <summary>GAP-AT-03: Restore discipline filter from config (called on DocumentOpened).</summary>
        public static void RestoreDisciplineFilter()
        {
            try
            {
                string filterStr = TagConfig.GetConfigValue("AUTO_TAGGER_DISC_FILTER");
                if (!string.IsNullOrEmpty(filterStr))
                {
                    var discs = filterStr.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                    _allowedDiscs = new HashSet<string>(discs, StringComparer.OrdinalIgnoreCase);
                    if (discs.Count > 0)
                        StingLog.Info($"AutoTagger: discipline filter restored ({string.Join(",", discs)})");
                }
            }
            catch (Exception ex) { StingLog.Warn($"Restore disc filter: {ex.Message}"); }
        }

        /// <summary>
        /// Called by Revit when elements are added to tagged categories.
        /// Auto-populates tokens and builds ISO 19650 tag.
        ///
        /// CRASH FIX: Wrapped in defensive guards to prevent Revit crash:
        /// - Element count limit per trigger (max 50 elements)
        /// - Null/disposed element checks
        /// - Full exception isolation (IUpdater exceptions crash Revit)
        /// - Automatic disable on repeated failures
        /// </summary>
        private static int _consecutiveFailures;
        private const int MaxFailuresBeforeAutoDisable = 3;
        private const int MaxElementsPerTrigger = 50;

        /// <summary>
        /// BUG-04: IUpdater Execute now only enqueues ElementIds for deferred processing.
        /// All Parameter.Set() calls are performed in the AutoTagQueueHandler via
        /// ExternalEvent, ensuring proper Transaction context and clean undo stack.
        /// </summary>
        public void Execute(UpdaterData data)
        {
            if (!_enabled) return;

            try
            {
                Document doc = data.GetDocument();
                if (doc == null || !doc.IsValidObject) return;

                // FIX-02: Declare enqueued counter (was missing — caused CS0103)
                int enqueued = 0;
                var addedIds = data.GetAddedElementIds();
                if (addedIds == null || addedIds.Count == 0) return;

                // Guard: limit elements per trigger to prevent performance issues
                // GAP-AT-01: For bulk paste (>50 elements), queue for deferred processing
                // instead of silently skipping. Elements will be tagged on next command or sync.
                if (addedIds.Count > MaxElementsPerTrigger)
                {
                    foreach (ElementId id in addedIds)
                        EnqueueDeferred(id);
                    StingLog.Info($"StingAutoTagger: bulk paste of {addedIds.Count} elements queued for deferred processing " +
                        $"(exceeds per-trigger limit of {MaxElementsPerTrigger})");
                    return;
                }

                // D1: Use cached context; rebuild only when invalidated
                // PERF-07: TTL-based context rebuild — avoids redundant rebuilds when
                // multiple commands invalidate context in rapid succession (e.g., AutoTag → Combine → AutoTag)
                bool ttlExpired = (DateTime.UtcNow - _contextCacheTime).TotalMilliseconds > ContextCacheTtlMs;
                if (_contextInvalid || _cachedCtx == null || ttlExpired)
                {
                    _cachedCtx = TokenAutoPopulator.PopulationContext.Build(doc);
                    // H-03 FIX: Validate context was actually built. Build() can return null
                    // on corrupted documents. Without this check, we'd NullReferenceException
                    // downstream and permanently disable the auto-tagger.
                    if (_cachedCtx == null)
                    {
                        StingLog.Error("AutoTagger: PopulationContext.Build returned null — skipping auto-tag cycle");
                        return;
                    }
                    var built = TagConfig.BuildTagIndexAndCounters(doc);
                    _cachedExistingTags = built.Item1;
                    _cachedSeqCounters = built.Item2;
                    _formulas = TagPipelineHelper.LoadFormulas();
                    _gridLines = TagPipelineHelper.LoadGridLines(doc);
                    _contextInvalid = false;
                    _contextCacheTime = DateTime.UtcNow;
                    StingLog.Info($"AutoTagger: context rebuilt ({_cachedExistingTags.Count} existing tags, {_formulas.Count} formulas, {_gridLines.Count} grids)");
                }
                var ctx = _cachedCtx;
                // H-03 FIX: Secondary null guard in case context was invalidated between rebuild and use
                if (ctx == null) return;
                var existingTags = _cachedExistingTags;
                var seqCounters = _cachedSeqCounters;
                if (existingTags == null || seqCounters == null)
                {
                    StingLog.Warn("AutoTagger: null existingTags or seqCounters after context rebuild");
                    return;
                }

                // Cache per-event properties we'd otherwise resolve for every
                // element: Application.Username crosses a P/Invoke boundary and
                // IsWorkshared walks the document state.
                string currentUser = null;
                bool isWorkshared = false;
                try { isWorkshared = doc.IsWorkshared; } catch { }
                if (isWorkshared)
                {
                    try { currentUser = doc.Application.Username; } catch { }
                }

                foreach (ElementId id in addedIds)
                {
                    // Skip recently processed (avoid re-trigger loops)
                    bool alreadyDone;
                    lock (_processedLock) { alreadyDone = _recentlyProcessed.Contains(id.Value); }
                    if (alreadyDone) continue;

                    Element el = doc.GetElement(id);
                    if (el == null || !el.IsValidObject) continue;

                    string catName = ParameterHelpers.GetCategoryName(el);
                    if (string.IsNullOrEmpty(catName)) continue;
                    if (!TagConfig.DiscMap.TryGetValue(catName, out string elemDisc)) continue;

                    // Discipline filter
                    if (_allowedDiscs.Count > 0)
                    {
                        if (!_allowedDiscs.Contains(elemDisc)) continue;
                    }

                    // Workset filter — R-02: defer elements on unowned worksets instead of dropping
                    if (isWorkshared)
                    {
                        try
                        {
                            WorksetId wsId = el.WorksetId;
                            if (wsId != null && wsId != WorksetId.InvalidWorksetId)
                            {
                                var wsInfo = WorksharingUtils.GetWorksharingTooltipInfo(doc, el.Id);
                                if (!string.IsNullOrEmpty(wsInfo.Owner)
                                    && wsInfo.Owner != currentUser
                                    && wsInfo.Owner != "")
                                {
                                    EnqueueDeferred(id);
                                    StingLog.Info($"AutoTagger: deferred {id.Value} (workset not owned by {currentUser}) — will retry after sync");
                                    continue;
                                }
                            }
                        }
                        catch (Exception wsEx) { StingLog.Warn($"AutoTagger workset check: {wsEx.Message}"); }
                    }

                    _pendingQueue.Enqueue(id);
                    enqueued++; // FIX-02: variable declared below
                }

                // FIX-02: Raise ExternalEvent to process queue on Revit API thread with proper Transaction
                if (!_pendingQueue.IsEmpty && _autoTagEvent != null)
                {
                    _autoTagEvent.Raise();
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("StingAutoTagger.Execute (enqueue)", ex);
            }
        }

        /// <summary>
        /// BUG-04: IExternalEventHandler that processes queued auto-tag elements
        /// with a proper Transaction, preserving undo stack integrity.
        /// </summary>
        internal class AutoTagQueueHandler : IExternalEventHandler
        {
            private const int MaxPerBatch = 50;

            public string GetName() => "STING Auto-Tag Queue";

            public void Execute(UIApplication app)
            {
                if (_pendingQueue.IsEmpty) return;

                Document doc = app.ActiveUIDocument?.Document;
                if (doc == null || !doc.IsValidObject) return;

                try
                {
                    // HI-01 FIX: Add TTL check matching synchronous handler pattern
                    bool ttlExpired = (DateTime.UtcNow - _contextCacheTime).TotalMilliseconds > ContextCacheTtlMs;
                    if (_contextInvalid || _cachedCtx == null || ttlExpired)
                    {
                        _cachedCtx = TokenAutoPopulator.PopulationContext.Build(doc);
                        // H-03 FIX: Validate context in queue handler too
                        if (_cachedCtx == null)
                        {
                            StingLog.Error("AutoTagQueueHandler: PopulationContext.Build returned null — skipping batch");
                            return;
                        }
                        var built = TagConfig.BuildTagIndexAndCounters(doc);
                        _cachedExistingTags = built.Item1;
                        _cachedSeqCounters = built.Item2;
                        // FIX-02: Also reload formulas and grid lines on context rebuild
                        _formulas = TagPipelineHelper.LoadFormulas();
                        _gridLines = TagPipelineHelper.LoadGridLines(doc);
                        _contextInvalid = false;
                        _contextCacheTime = DateTime.UtcNow;
                        StingLog.Info($"AutoTagger: context rebuilt ({_cachedExistingTags.Count} existing tags, {_formulas.Count} formulas)");
                    }
                    var ctx = _cachedCtx;
                    if (ctx == null) return; // H-03 secondary guard
                    var existingTags = _cachedExistingTags;
                    var seqCounters = _cachedSeqCounters;

                    // C-7: resolve the active view's DrawingType discipline (if
                    // any) once for this drain pass so per-element decisions in
                    // ProcessBatch are an O(1) string compare.
                    string activeViewDtDiscipline = ResolveActiveViewDrawingTypeDiscipline(app, doc);

                    // A-3: Drain the entire pending queue inside a single TransactionGroup
                    // so the user sees one undo entry for a full bulk-paste session.
                    // Each MaxPerBatch chunk still commits its own child Transaction (preserves
                    // IUpdater safety + per-chunk rollback on failure), but they assimilate into
                    // one parent group for a clean undo stack and one journal entry.
                    int totalProcessed = 0;
                    int totalAttempted = 0;
                    using (var txGroup = new TransactionGroup(doc, "STING Auto-Tag"))
                    {
                        txGroup.Start();

                        while (!_pendingQueue.IsEmpty)
                        {
                            var batch = new List<ElementId>();
                            while (batch.Count < MaxPerBatch && _pendingQueue.TryDequeue(out ElementId eid))
                                batch.Add(eid);
                            if (batch.Count == 0) break;
                            totalAttempted += batch.Count;

                            int processed = ProcessBatch(doc, batch, ctx, existingTags, seqCounters, activeViewDtDiscipline);
                            totalProcessed += processed;
                        }

                        txGroup.Assimilate();
                    }

                    // A-3 / A-8: Save SEQ sidecar once after the group assimilates,
                    // not per child batch. Guarantees the sidecar always reflects a committed state.
                    try { TagConfig.SaveSeqSidecar(doc, seqCounters); }
                    catch (Exception ssEx) { StingLog.Warn($"AutoTagger SaveSeqSidecar: {ssEx.Message}"); }

                    ComplianceScan.InvalidateCache();
                    _consecutiveFailures = 0;

                    if (totalProcessed > 0)
                        StingLog.Info($"AutoTagger queue: processed {totalProcessed}/{totalAttempted} elements (single undo entry)");
                }
                catch (Exception ex)
                {
                    StingLog.Error("AutoTagQueueHandler.Execute", ex);
                }
            }

            /// <summary>
            /// A-3: Per-chunk processing wrapped in one Transaction. Returns the
            /// number of elements successfully tagged in this chunk. Failure of an
            /// individual element is swallowed and logged; chunk-level Transaction
            /// failure is logged and the chunk's contribution is zero.
            /// </summary>
            private int ProcessBatch(
                Document doc,
                List<ElementId> batch,
                TokenAutoPopulator.PopulationContext ctx,
                HashSet<string> existingTags,
                Dictionary<string, int> seqCounters,
                string activeViewDtDiscipline)
            {
                int processed = 0;
                try
                {
                    using (var trans = new Transaction(doc, "STING Auto-Tag (chunk)"))
                    {
                        trans.Start();
                        foreach (ElementId id in batch)
                        {
                            // Skip recently processed
                            bool alreadyDone;
                            lock (_processedLock) { alreadyDone = _recentlyProcessed.Contains(id.Value); }
                            if (alreadyDone) continue;

                            Element el = doc.GetElement(id);
                            if (el == null || !el.IsValidObject) continue;

                            string catName = ParameterHelpers.GetCategoryName(el);
                            if (string.IsNullOrEmpty(catName)) continue;

                            // C-7: when the active view is stamped with a
                            // DrawingType whose discipline is not "*", defer
                            // off-discipline elements rather than tag them
                            // and clutter the view.
                            if (!string.IsNullOrEmpty(activeViewDtDiscipline)
                                && !DoesElementMatchDiscipline(el, catName, activeViewDtDiscipline))
                            {
                                EnqueueDeferred(id);
                                continue;
                            }

                            try
                            {
                                // GAP-AQ: Use unified RunFullPipeline for all 11 canonical steps
                                // (replaces inline pipeline that was missing CategorySkipList,
                                //  CategoryForceSys, CategoryTokenOverrides, TokenLock, AuditTrail,
                                //  and had NativeMapper in wrong order)
                                bool pipelineOk = TagPipelineHelper.RunFullPipeline(
                                    doc, el, ctx, existingTags, seqCounters,
                                    _formulas, _gridLines,
                                    overwrite: false,
                                    skipComplete: true,
                                    collisionMode: TagCollisionMode.AutoIncrement);

                                if (!pipelineOk) continue;

                                // Visual tag placement
                                if (_visualTaggingEnabled && doc.ActiveView != null
                                    && !(doc.ActiveView is ViewSheet)
                                    && doc.ActiveView.CanBePrinted)
                                {
                                    try
                                    {
                                        View view = doc.ActiveView;
                                        BoundingBoxXYZ elBb = el.get_BoundingBox(view);
                                        if (elBb != null)
                                        {
                                            FamilySymbol tagType = Tags.TagPlacementEngine.FindTagType(doc, el.Category);
                                            if (tagType != null)
                                            {
                                                XYZ elCenter = Tags.TagPlacementEngine.GetElementCenter(el, view);
                                                double offset = Tags.TagPlacementEngine.GetModelOffset(view);
                                                int preferred = Tags.TagPlacementEngine.GetPreferredSide(catName);
                                                XYZ[] candidates = Tags.TagPlacementEngine.GetCandidateOffsets(offset);
                                                XYZ bestPos = elCenter + candidates[preferred < candidates.Length ? preferred : 0];

                                                // Task 5: variant resolution before IndependentTag.Create
                                                ElementId tagTypeId = tagType.Id;
                                                try
                                                {
                                                    string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                                                    ElementId vId = Tags.TagStyleEngine.ResolveTagTypeForPlacement(doc, tagType, disc);
                                                    if (vId != ElementId.InvalidElementId) tagTypeId = vId;
                                                }
                                                catch (Exception ex) { StingLog.Warn($"ResolveTagTypeForPlacement (autotagger): {ex.Message}"); }

                                                IndependentTag.Create(
                                                    doc, tagTypeId, view.Id, new Reference(el),
                                                    false, TagOrientation.Horizontal, bestPos);
                                            }
                                        }
                                    }
                                    catch (Exception vex)
                                    {
                                        StingLog.Warn($"AutoTagger visual tag: {vex.Message}");
                                    }
                                }

                                lock (_processedLock)
                                {
                                    _recentlyProcessed.Add(id.Value);
                                    _recentlyProcessedQueue.Enqueue(id.Value);
                                    _processedCount++;

                                    if (_recentlyProcessed.Count > 10000)
                                    {
                                        // Evict oldest 20% (2000 of 10000) — matches the
                                        // _tag7HashCache 20%-eviction pattern for consistency.
                                        int toRemove = 2000;
                                        while (toRemove-- > 0 && _recentlyProcessedQueue.Count > 0)
                                        {
                                            long oldest = _recentlyProcessedQueue.Dequeue();
                                            _recentlyProcessed.Remove(oldest);
                                        }
                                    }
                                }

                                // Phase 175 — symbol overlay auto-placement.
                                try
                                {
                                    if (IsSymbolAutoPlaceEnabled(doc) && doc.ActiveView != null
                                        && !(doc.ActiveView is ViewSheet))
                                    {
                                        var concept = StingTools.Core.Symbols.SymbolConceptRegistry
                                            .GetConceptsForCategory(el.Category?.Name)
                                            .FirstOrDefault();
                                        if (concept != null)
                                        {
                                            string std = StingTools.Core.Symbols.SymbolStandardResolver
                                                .ResolveStandard(doc, doc.ActiveView, el);
                                            StingTools.Core.Symbols.SymbolOverlayManager
                                                .PlaceSymbolOverlay(doc, doc.ActiveView, el,
                                                    concept.ConceptId, std);
                                        }
                                    }
                                }
                                catch (Exception sEx)
                                { StingLog.Warn($"AutoTagger symbol overlay: {sEx.Message}"); }

                                processed++;
                            }
                            catch (Exception elEx)
                            {
                                StingLog.Warn($"AutoTagger queue element {id.Value}: {elEx.Message}");
                            }
                        }
                        trans.Commit();
                    }
                }
                catch (Exception batchEx)
                {
                    StingLog.Error($"AutoTagQueueHandler.ProcessBatch ({batch.Count} elements)", batchEx);
                    // A-3: chunk-level failure rolls back this chunk only; outer
                    // TransactionGroup continues with the next chunk.
                    _consecutiveFailures++;

                    if (_consecutiveFailures >= MaxFailuresBeforeAutoDisable)
                    {
                        StingLog.Error($"StingAutoTagger: auto-disabling after {_consecutiveFailures} " +
                            "consecutive failures");
                        WasAutoDisabled = true;
                        try { Toggle(); } catch (Exception tEx) { _enabled = false; StingLog.Warn($"AutoTagger toggle on restore: {tEx.Message}"); }

                        try
                        {
                            UI.StingDockPanel.UpdateComplianceStatus(
                                "Auto-Tagger DISABLED (errors — re-enable via toggle)", "RED");
                        }
                        catch (Exception uiEx) { StingLog.Warn($"Auto-tagger status bar update failed: {uiEx.Message}"); }
                    }
                }
                return processed;
            }

            /// <summary>
            /// Phase 175 — gate symbol overlay auto-placement on
            /// project_config.json &quot;symbol_auto_place&quot; (default false to
            /// avoid surprising existing projects).
            /// </summary>
            private static bool IsSymbolAutoPlaceEnabled(Document doc)
            {
                try
                {
                    if (doc == null || string.IsNullOrEmpty(doc.PathName)) return false;
                    string p = System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(doc.PathName), "project_config.json");
                    if (!System.IO.File.Exists(p)) return false;
                    var root = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(p));
                    return (bool)(root["symbol_auto_place"] ?? false);
                }
                catch (Exception ex) { StingLog.Warn($"IsSymbolAutoPlaceEnabled: {ex.Message}"); return false; }
            }

            /// <summary>
            /// C-7: returns the discipline string of the active view's stamped
            /// DrawingType, or null when the view is not stamped, the type
            /// can't be resolved, or its discipline is "*" / empty (all
            /// disciplines welcome). The caller treats null as "no filter".
            /// </summary>
            private static string ResolveActiveViewDrawingTypeDiscipline(UIApplication app, Document doc)
            {
                try
                {
                    View view = doc?.ActiveView ?? app?.ActiveUIDocument?.ActiveView;
                    if (view == null || view is ViewSheet) return null;
                    // GAP-N: route through Stamper.Read so a template-controlled
                    // pack=…|cs=… stamp doesn't leak into the registry lookup.
                    string dtId = Drawing.DrawingTypeStamper.Read(view);
                    if (string.IsNullOrWhiteSpace(dtId)) return null;
                    var dt = Drawing.DrawingTypeRegistry.Get(doc, dtId);
                    if (dt == null) return null;
                    if (string.IsNullOrWhiteSpace(dt.Discipline) || dt.Discipline == "*") return null;
                    return dt.Discipline;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ResolveActiveViewDrawingTypeDiscipline: {ex.Message}");
                    return null;
                }
            }

            /// <summary>
            /// C-7: returns true when the element's derived discipline matches
            /// the requested filter. Falls back to TagConfig.DiscMap by category
            /// when DISC isn't yet populated; returns true (i.e. don't drop) when
            /// the element's discipline can't be inferred.
            /// </summary>
            private static bool DoesElementMatchDiscipline(Element el, string catName, string requiredDisc)
            {
                if (string.IsNullOrEmpty(requiredDisc)) return true;
                string elDisc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                if (string.IsNullOrEmpty(elDisc))
                {
                    if (!TagConfig.DiscMap.TryGetValue(catName, out elDisc) || string.IsNullOrEmpty(elDisc))
                        return true;
                }
                return string.Equals(elDisc, requiredDisc, StringComparison.OrdinalIgnoreCase);
            }
        }

        // ── DocumentChanged stale-marking ──────────────────────────────────────

        /// <summary>LOG-09: Track element version hashes to avoid redundant stale-marks.
        /// Key = element ID, Value = hash of tag + location tokens. Only re-mark when hash changes.</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, string> _elementVersionHash = new System.Collections.Concurrent.ConcurrentDictionary<long, string>();

        /// <summary>A2: Debounce — minimum interval (ms) between stale-mark transactions.
        /// Prevents thundering-herd on bulk operations (group moves, workset assigns, filter applies).</summary>
        private static DateTime _lastStaleMarkTime = DateTime.MinValue;
        private const int STALE_DEBOUNCE_MS = 500;

        /// <summary>
        /// DocumentChanged event handler that marks modified tagged elements as stale.
        /// For each element with a non-empty ASS_TAG_1_TXT, sets STING_STALE_BOOL = 1
        /// to indicate that spatial tokens (LOC, ZONE, LVL) may need re-derivation.
        /// Throttled: skips elements stale-marked in the last 5 seconds.
        /// Subscribe via application.ControlledApplication.DocumentChanged.
        /// </summary>
        public static void OnDocumentChanged(object sender,
            Autodesk.Revit.DB.Events.DocumentChangedEventArgs args)
        {
            if (!StingStaleMarker.IsEnabled) return;

            try
            {
                // A2: Debounce — skip if called within 500ms of last stale-mark transaction
                // Prevents thundering-herd on bulk operations (group moves, workset assigns, filter applies)
                if ((DateTime.UtcNow - _lastStaleMarkTime).TotalMilliseconds < STALE_DEBOUNCE_MS) return;

                Document doc = args.GetDocument();
                if (doc == null || !doc.IsValidObject || doc.IsFamilyDocument) return;

                var modifiedIds = args.GetModifiedElementIds();
                if (modifiedIds == null || modifiedIds.Count == 0) return;

                // R4-C AL-GAP-01: Process first 100, enqueue overflow for deferred processing.
                // PERF-009 FIX: Queue overflow instead of dropping — previously 400+ elements silently lost.
                ICollection<ElementId> processIds = modifiedIds;
                if (modifiedIds.Count > 100)
                {
                    var allIds = modifiedIds.ToList();
                    processIds = allIds.Take(100).ToList();
                    // Enqueue overflow for processing on next sync/idle
                    int enqueued = 0;
                    foreach (var overflow in allIds.Skip(100))
                    {
                        EnqueueDeferred(overflow);
                        enqueued++;
                    }
                    StingLog.Warn($"StingStaleMarker: {modifiedIds.Count} elements modified — processing 100, {enqueued} enqueued for deferred stale check.");
                }

                var idsToMark = new List<ElementId>();

                foreach (ElementId id in processIds)
                {
                    Element el = doc.GetElement(id);
                    if (el == null || !el.IsValidObject) continue;

                    // Only mark elements that already have a tag
                    string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                    if (string.IsNullOrEmpty(tag1)) continue;

                    // LOG-09: Compute version hash from tag + spatial tokens; skip if unchanged
                    string loc = ParameterHelpers.GetString(el, ParamRegistry.LOC);
                    string zone = ParameterHelpers.GetString(el, ParamRegistry.ZONE);
                    string lvl = ParameterHelpers.GetString(el, ParamRegistry.LVL);
                    string hash = $"{tag1}|{loc}|{zone}|{lvl}";
                    if (_elementVersionHash.TryGetValue(id.Value, out string prevHash) && prevHash == hash)
                        continue;

                    idsToMark.Add(id);
                }

                if (idsToMark.Count == 0) return;

                // PERF: Pre-compute hashes BEFORE transaction to minimize time inside tx.
                // Previously read 4 params per element AGAIN inside the transaction (redundant).
                var hashUpdates = new Dictionary<long, string>();
                foreach (ElementId id in idsToMark)
                {
                    Element el = doc.GetElement(id);
                    if (el == null) continue;
                    string t = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                    string l = ParameterHelpers.GetString(el, ParamRegistry.LOC);
                    string z = ParameterHelpers.GetString(el, ParamRegistry.ZONE);
                    string v = ParameterHelpers.GetString(el, ParamRegistry.LVL);
                    hashUpdates[id.Value] = $"{t}|{l}|{z}|{v}";
                }

                using (Transaction tx = new Transaction(doc, "STING Mark Stale"))
                {
                    tx.Start();
                    foreach (ElementId id in idsToMark)
                    {
                        try
                        {
                            Element el = doc.GetElement(id);
                            if (el == null || !el.IsValidObject) continue;
                            Parameter p = el.LookupParameter(ParamRegistry.STALE);
                            if (p != null && !p.IsReadOnly)
                            {
                                p.Set(1);
                                // Update hash cache with pre-computed value
                                if (hashUpdates.TryGetValue(id.Value, out string newHash))
                                    _elementVersionHash[id.Value] = newHash;
                            }
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"OnDocumentChanged stale-mark {id.Value}: {ex.Message}");
                        }
                    }
                    tx.Commit();
                }

                // A2: Update last stale-mark timestamp for debounce
                _lastStaleMarkTime = DateTime.UtcNow;

                // PERF: Partial 20% eviction without allocating ToList() copy of all keys
                if (_elementVersionHash.Count > 10000)
                {
                    int originalCount = _elementVersionHash.Count;
                    int evictCount = originalCount / 5;
                    int evicted = 0;
                    // Use enumerator directly — ConcurrentDictionary supports concurrent removal during iteration
                    foreach (var kvp in _elementVersionHash)
                    {
                        if (evicted >= evictCount) break;
                        _elementVersionHash.TryRemove(kvp.Key, out _);
                        evicted++;
                    }
                    StingLog.Info($"StingStaleMarker: evicted {evicted} of {originalCount} cached hashes");
                }

                StingLog.Info($"OnDocumentChanged: marked {idsToMark.Count} elements stale");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"OnDocumentChanged stale-marking: {ex.Message}");
            }
        }

        /// <summary>Public accessor for the multi-category filter (used by StingStaleMarker).</summary>
        public static ElementMulticategoryFilter CreateMultiCategoryFilterStatic()
        {
            return CreateMultiCategoryFilter();
        }

        /// <summary>Build a multi-category filter covering all tagged categories.</summary>
        private static ElementMulticategoryFilter CreateMultiCategoryFilter()
        {
            // Built-in default list. Mutated in place when the Categories sub-tab
            // has pushed TagCategoryFilter / TagCategoryExclusions ExtraParams.
            var cats = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_ElectricalEquipment,
                BuiltInCategory.OST_ElectricalFixtures,
                BuiltInCategory.OST_LightingFixtures,
                BuiltInCategory.OST_LightingDevices,
                BuiltInCategory.OST_PlumbingFixtures,
                BuiltInCategory.OST_Sprinklers,
                BuiltInCategory.OST_FireAlarmDevices,
                BuiltInCategory.OST_DataDevices,
                BuiltInCategory.OST_CommunicationDevices,
                BuiltInCategory.OST_SecurityDevices,
                BuiltInCategory.OST_NurseCallDevices,
                BuiltInCategory.OST_DuctAccessory,
                BuiltInCategory.OST_DuctFitting,
                BuiltInCategory.OST_DuctTerminal,
                BuiltInCategory.OST_PipeAccessory,
                BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_Furniture,
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_CableTray,
                BuiltInCategory.OST_Conduit,
            };

            // ORPHAN-FIX: honour the Categories sub-tab selection. Include list
            // replaces the default set; exclude list is subtracted from whatever
            // remains. Unknown tokens are logged once and skipped.
            try
            {
                string includeCsv = StingTools.UI.StingCommandHandler.GetExtraParam("TagCategoryFilter");
                string excludeCsv = StingTools.UI.StingCommandHandler.GetExtraParam("TagCategoryExclusions");
                if (!string.IsNullOrWhiteSpace(includeCsv))
                {
                    var parsed = ParseBuiltInCategoryCsv(includeCsv);
                    if (parsed.Count > 0) cats = parsed;
                }
                if (!string.IsNullOrWhiteSpace(excludeCsv))
                {
                    var excl = ParseBuiltInCategoryCsv(excludeCsv);
                    if (excl.Count > 0)
                        cats = cats.Where(c => !excl.Contains(c)).ToList();
                }
                if (cats.Count == 0)
                {
                    // Safety net: never hand Revit an empty filter, which would match nothing.
                    StingLog.Warn("CreateMultiCategoryFilter: user selection produced empty list — falling back to single-category placeholder.");
                    cats.Add(BuiltInCategory.OST_GenericModel);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CreateMultiCategoryFilter: applying Categories sub-tab selection failed ({ex.Message}) — using built-in list.");
            }

            return new ElementMulticategoryFilter(cats);
        }

        /// <summary>
        /// Parse a comma-separated list of BuiltInCategory names (e.g.
        /// "OST_PlumbingFixtures,OST_Doors") to a deduplicated list. Invalid
        /// tokens are silently skipped — callers warn when the result is empty.
        /// </summary>
        private static List<BuiltInCategory> ParseBuiltInCategoryCsv(string csv)
        {
            var seen = new HashSet<BuiltInCategory>();
            var result = new List<BuiltInCategory>();
            foreach (string raw in csv.Split(','))
            {
                string tok = raw?.Trim();
                if (string.IsNullOrEmpty(tok)) continue;
                if (Enum.TryParse<BuiltInCategory>(tok, ignoreCase: true, out var bic) && seen.Add(bic))
                    result.Add(bic);
            }
            return result;
        }
    }

    /// <summary>
    /// Toggle the real-time auto-tagger on/off.
    /// When enabled, newly placed elements are automatically tagged with ISO 19650 codes.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoTaggerToggleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            bool wasAutoDisabled = StingAutoTagger.WasAutoDisabled;
            bool nowEnabled = StingAutoTagger.Toggle();

            string msg;
            if (nowEnabled && wasAutoDisabled)
            {
                msg = "Real-time auto-tagging RE-ENABLED.\n\n" +
                      "Note: Auto-tagger was previously disabled automatically due to errors.\n" +
                      "If problems persist, check the log file for details.\n\n" +
                      $"Elements auto-tagged so far: {StingAutoTagger.ProcessedCount}";
            }
            else if (nowEnabled)
            {
                msg = "Real-time auto-tagging ENABLED.\n\n" +
                      "Newly placed elements will be automatically tagged\n" +
                      "with ISO 19650 codes (DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ).\n\n" +
                      $"Elements auto-tagged so far: {StingAutoTagger.ProcessedCount}";
            }
            else
            {
                msg = "Real-time auto-tagging DISABLED.\n\n" +
                      $"Total elements auto-tagged this session: {StingAutoTagger.ProcessedCount}";
            }
            // FIX-B10: Persist auto-tagger state to project_config.json
            TagConfig.AutoTaggerEnabled = nowEnabled;
            PersistAutoTaggerConfig(commandData);

            TaskDialog.Show("Auto-Tagger", msg);
            return Result.Succeeded;
        }

        /// <summary>FIX-B10: Save auto-tagger config keys to project_config.json adjacent to the .rvt file.</summary>
        internal static void PersistAutoTaggerConfig(ExternalCommandData commandData)
        {
            try
            {
                var doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (doc == null || string.IsNullOrEmpty(doc.PathName)) return;
                string dir = System.IO.Path.GetDirectoryName(doc.PathName);
                if (string.IsNullOrEmpty(dir)) return;
                string cfgPath = System.IO.Path.Combine(dir, "project_config.json");
                TagConfig.SaveToFile(cfgPath);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"AutoTagger config persist: {ex.Message}");
            }
        }
    }

    /// <summary>Toggle visual tag placement during auto-tagging.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoTaggerToggleVisualCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            StingAutoTagger.SetVisualTagging(!StingAutoTagger.IsVisualTaggingEnabled);

            // FIX-B10: Persist visual tagging state
            TagConfig.AutoTaggerVisual = StingAutoTagger.IsVisualTaggingEnabled;
            AutoTaggerToggleCommand.PersistAutoTaggerConfig(commandData);

            TaskDialog.Show("STING Auto-Tagger",
                $"Visual tag placement: {(StingAutoTagger.IsVisualTaggingEnabled ? "ENABLED" : "DISABLED")}\n\n" +
                "When enabled, the auto-tagger will also place visual annotation tags on newly placed elements.");
            return Result.Succeeded;
        }
    }

    /// <summary>Configure auto-tagger discipline filter and workset/visual options.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoTaggerConfigCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var td = new TaskDialog("STING Auto-Tagger Configuration");
            td.MainInstruction = "Auto-Tagger Settings";
            var sb = new StringBuilder();
            sb.AppendLine($"Auto-Tagger: {(StingAutoTagger.IsEnabled ? "ENABLED" : "DISABLED")}");
            sb.AppendLine($"Visual Tags: {(StingAutoTagger.IsVisualTaggingEnabled ? "ENABLED" : "DISABLED")}");
            sb.AppendLine($"Stale Marker: {(StingStaleMarker.IsEnabled ? "ENABLED" : "DISABLED")}");
            sb.AppendLine();
            sb.AppendLine("Discipline Filter:");
            sb.AppendLine("  M = Mechanical, E = Electrical, P = Plumbing");
            sb.AppendLine("  A = Architectural, S = Structural, FP = Fire");
            td.MainContent = sb.ToString();
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Toggle Visual Tags", "Enable/disable visual annotation placement");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Toggle Stale Marker", "Enable/disable geometry change detection");
            td.CommonButtons = TaskDialogCommonButtons.Close;
            var result = td.Show();
            if (result == TaskDialogResult.CommandLink1)
            {
                StingAutoTagger.SetVisualTagging(!StingAutoTagger.IsVisualTaggingEnabled);
                TagConfig.AutoTaggerVisual = StingAutoTagger.IsVisualTaggingEnabled;
                AutoTaggerToggleCommand.PersistAutoTaggerConfig(commandData);
            }
            else if (result == TaskDialogResult.CommandLink2)
            {
                StingStaleMarker.SetEnabled(!StingStaleMarker.IsEnabled);
                TagConfig.AutoTaggerStaleMarker = StingStaleMarker.IsEnabled;
                AutoTaggerToggleCommand.PersistAutoTaggerConfig(commandData);
            }
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  STALE TOKEN MARKER (IUpdater)
    //
    //  Marks elements as stale when their geometry changes (indicating a move
    //  that may invalidate spatial tokens like LOC, ZONE, LVL).
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// IUpdater that detects geometry changes on tagged elements and sets
    /// STING_STALE_BOOL = 1 when the element's spatial context may have changed.
    /// </summary>
    public class StingStaleMarker : IUpdater
    {
        private static StingStaleMarker _instance;
        private static UpdaterId _updaterId;
        private static bool _enabled = false;

        private StingStaleMarker(AddInId addInId)
        {
            _updaterId = new UpdaterId(addInId, new Guid("F1A2B3C4-D5E6-4F7A-8B9C-0D1E2F3A4B5C"));
        }

        public UpdaterId GetUpdaterId() => _updaterId;
        public ChangePriority GetChangePriority() => ChangePriority.FloorsRoofsStructuralWalls;
        public string GetUpdaterName() => "STING Stale Token Marker";
        public string GetAdditionalInformation() => "Marks elements as stale when geometry changes.";

        /// <summary>Register the stale marker updater at startup.</summary>
        public static void Register(UIControlledApplication app)
        {
            try
            {
                _instance = new StingStaleMarker(app.ActiveAddInId);
                UpdaterRegistry.RegisterUpdater(_instance, true);
                StingLog.Info("StingStaleMarker registered (disabled).");
            }
            catch (Exception ex)
            {
                StingLog.Error("StingStaleMarker.Register", ex);
            }
        }

        /// <summary>Enable or disable the stale marker.</summary>
        public static void SetEnabled(bool enabled)
        {
            if (_instance == null || _updaterId == null) return;
            try
            {
                if (enabled && !_enabled)
                {
                    var filter = StingAutoTagger.CreateMultiCategoryFilterStatic();
                    UpdaterRegistry.AddTrigger(_updaterId, filter,
                        Element.GetChangeTypeGeometry());
                    _enabled = true;
                    StingLog.Info("StingStaleMarker enabled.");
                }
                else if (!enabled && _enabled)
                {
                    UpdaterRegistry.RemoveAllTriggers(_updaterId);
                    _enabled = false;
                    StingLog.Info("StingStaleMarker disabled.");
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("StingStaleMarker.SetEnabled", ex);
            }
        }

        public static bool IsEnabled => _enabled;

        /// <summary>Unregister on shutdown.</summary>
        public static void Unregister()
        {
            try
            {
                if (_instance != null)
                    UpdaterRegistry.UnregisterUpdater(_updaterId);
            }
            catch (Exception ex) { StingLog.Warn($"StingStaleMarker unregister failed: {ex.Message}"); }
        }

        private const int MaxElementsPerTrigger = 20;

        // A-2: Per-document project-LOC cache, complementary to
        // SpatialAutoDetect.BuildRoomIndex (which already caches the room index
        // per-document with a 30 s TTL). The full room index is no longer
        // duplicated here — calling SpatialAutoDetect.BuildRoomIndex is cheap
        // when the cache is warm. Only the project LOC is stored locally
        // because DetectProjectLoc is currently uncached.
        private static string _cachedProjectLoc;
        private static string _cachedProjectLocDocKey;
        private static DateTime _projectLocCacheTime = DateTime.MinValue;
        private static readonly object _projectLocCacheLock = new object();

        /// <summary>Clear cached project LOC (call on document close/switch).</summary>
        internal static void ClearRoomIndexCache()
        {
            lock (_projectLocCacheLock)
            {
                _cachedProjectLoc = null;
                _cachedProjectLocDocKey = null;
                _projectLocCacheTime = DateTime.MinValue;
            }
            // A-2: also nudge the shared SpatialAutoDetect cache so a stale
            // index from a closed document never resurfaces.
            try { SpatialAutoDetect.InvalidateRoomIndex(); }
            catch (Exception ex) { StingLog.Warn($"ClearRoomIndexCache: {ex.Message}"); }
        }

        public void Execute(UpdaterData data)
        {
            if (!_enabled) return;

            try
            {
                Document doc = data.GetDocument();
                if (doc == null || !doc.IsValidObject) return;

                var modifiedIds = data.GetModifiedElementIds();
                if (modifiedIds == null || modifiedIds.Count == 0) return;
                if (modifiedIds.Count > MaxElementsPerTrigger)
                {
                    // STALE-01: Log when dropping elements due to batch limit so users
                    // know stale detection was skipped (previously silently returned)
                    StingLog.Info($"StingStaleMarker: skipping batch of {modifiedIds.Count} " +
                        $"elements (exceeds limit of {MaxElementsPerTrigger}). " +
                        "Run 'Retag Stale' manually after bulk operations.");
                    return;
                }

                // A-2: defer to the shared SpatialAutoDetect.BuildRoomIndex
                // (already TTL-cached per-document); only project LOC is
                // memoised locally to avoid repeating DetectProjectLoc on
                // every IUpdater trigger.
                Dictionary<ElementId, Autodesk.Revit.DB.Architecture.Room> roomIndex = null;
                string projectLoc = null;
                try
                {
                    roomIndex = SpatialAutoDetect.BuildRoomIndex(doc);

                    string docKey = doc?.PathName ?? doc?.Title ?? "";
                    lock (_projectLocCacheLock)
                    {
                        if (string.Equals(_cachedProjectLocDocKey, docKey, StringComparison.Ordinal)
                            && (DateTime.UtcNow - _projectLocCacheTime).TotalSeconds < 30)
                        {
                            projectLoc = _cachedProjectLoc;
                        }
                    }
                    if (projectLoc == null)
                    {
                        projectLoc = SpatialAutoDetect.DetectProjectLoc(doc);
                        lock (_projectLocCacheLock)
                        {
                            _cachedProjectLoc = projectLoc;
                            _cachedProjectLocDocKey = docKey;
                            _projectLocCacheTime = DateTime.UtcNow;
                        }
                    }
                }
                catch (Exception riEx)
                {
                    StingLog.Warn($"StingStaleMarker room index build: {riEx.Message}");
                }

                // TAG-STALE-WARN-01: count the elements newly flagged as stale this batch
                // so we can decide whether to schedule a stale-warning promotion job.
                int staleMarkedThisBatch = 0;

                foreach (ElementId id in modifiedIds)
                {
                    try
                    {
                        Element el = doc.GetElement(id);
                        if (el == null || !el.IsValidObject) continue;

                        // Only mark elements that already have a tag
                        string tag1 = ParameterHelpers.GetString(el, ParamRegistry.AllTokenParams[0]);
                        if (string.IsNullOrEmpty(tag1)) continue;

                        // Check if spatial context changed (LVL, LOC, ZONE)
                        bool isStale = false;

                        string currentLvl = ParameterHelpers.GetLevelCode(doc, el);
                        string storedLvl = ParameterHelpers.GetString(el, ParamRegistry.LVL);
                        if (!string.IsNullOrEmpty(storedLvl) && currentLvl != storedLvl)
                            isStale = true;

                        // FIX-07/FIX-N07: Detect LOC/ZONE changes using pre-built roomIndex
                        if (!isStale && roomIndex != null)
                        {
                            try
                            {
                                string storedLoc = ParameterHelpers.GetString(el, ParamRegistry.LOC);
                                if (!string.IsNullOrEmpty(storedLoc) && storedLoc != "XX")
                                {
                                    string currentLoc = SpatialAutoDetect.DetectLoc(doc, el, roomIndex, projectLoc);
                                    if (!string.IsNullOrEmpty(currentLoc) && currentLoc != "XX" && currentLoc != storedLoc)
                                        isStale = true;
                                }

                                if (!isStale)
                                {
                                    string storedZone = ParameterHelpers.GetString(el, ParamRegistry.ZONE);
                                    if (!string.IsNullOrEmpty(storedZone) && storedZone != "XX" && storedZone != "ZZ")
                                    {
                                        string currentZone = SpatialAutoDetect.DetectZone(doc, el, roomIndex);
                                        if (!string.IsNullOrEmpty(currentZone) && currentZone != "XX"
                                            && currentZone != "ZZ" && currentZone != storedZone)
                                            isStale = true;
                                    }
                                }
                            }
                            catch (Exception spEx) { StingLog.Warn($"StaleMarker spatial detection: {spEx.Message}"); }
                        }

                        // R-06: MEP system change detection — SYS/FUNC become stale on system reassignment
                        if (!isStale)
                        {
                            try
                            {
                                string categoryName = ParameterHelpers.GetCategoryName(el);
                                if (IsMepCategory(categoryName))
                                {
                                    string storedSys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                                    if (!string.IsNullOrEmpty(storedSys) && storedSys != "GEN" && storedSys != "XX")
                                    {
                                        string currentSys = TagConfig.GetMepSystemAwareSysCode(el, categoryName);
                                        if (!string.IsNullOrEmpty(currentSys) && currentSys != storedSys)
                                        {
                                            isStale = true;
                                            StingLog.Info($"StaleMarker: MEP system change on {id.Value} — stored SYS={storedSys}, current={currentSys}");
                                        }
                                    }
                                }
                            }
                            catch (Exception mepEx) { StingLog.Warn($"StaleMarker MEP detection: {mepEx.Message}"); }
                        }

                        if (isStale)
                        {
                            Parameter p = el.LookupParameter(ParamRegistry.STALE);
                            if (p != null && !p.IsReadOnly)
                                p.Set(1);
                            staleMarkedThisBatch++;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"StingStaleMarker element {id.Value}: {ex.Message}");
                    }
                }

                // TAG-STALE-WARN-01: When this batch flags any element stale, schedule a
                // single-shot idle job to promote the stale flag into a BIM issue once the
                // total stale count crosses TagConfig.StaleWarningThreshold. Enqueueing is
                // safe inside the IUpdater callback (the job runs later on idle, after the
                // transaction has committed and the document is no longer write-locked).
                if (staleMarkedThisBatch > 0 && TagConfig.StaleWarningThreshold > 0)
                {
                    try { StingIdlingScheduler.Enqueue(new StaleWarningPromotionJob()); }
                    catch (Exception schEx) { StingLog.Warn($"StaleMarker enqueue stale-warning job: {schEx.Message}"); }
                    // Also invalidate the compliance scan so the dashboard reflects the
                    // new stale count without waiting for the 30 s cache TTL to expire.
                    try { ComplianceScan.InvalidateCache(); }
                    catch (Exception ciEx) { StingLog.Warn($"StaleMarker invalidate compliance cache: {ciEx.Message}"); }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingStaleMarker.Execute: {ex.Message}");
            }
        }

        /// <summary>R-06: Check if category is MEP-related for system change detection.</summary>
        private static bool IsMepCategory(string categoryName)
        {
            return categoryName == "Ducts" || categoryName == "Duct Fittings" ||
                   categoryName == "Duct Accessories" || categoryName == "Air Terminals" ||
                   categoryName == "Mechanical Equipment" ||
                   categoryName == "Pipes" || categoryName == "Pipe Fittings" ||
                   categoryName == "Pipe Accessories" || categoryName == "Plumbing Fixtures" ||
                   categoryName == "Cable Trays" || categoryName == "Conduits" ||
                   categoryName == "Electrical Equipment" || categoryName == "Electrical Fixtures" ||
                   categoryName == "Fire Protection";
        }
    }
}
