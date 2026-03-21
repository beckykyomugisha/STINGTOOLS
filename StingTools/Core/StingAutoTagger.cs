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
        // G2.3: Cached formulas and grid lines for pipeline helper
        private static List<Temp.FormulaEngine.FormulaDefinition> _formulas;
        private static List<Grid> _gridLines;

        // LOG-04: Token hash cache to skip redundant TAG7 rebuilds
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, string>
            _tag7HashCache = new System.Collections.Concurrent.ConcurrentDictionary<long, string>();

        // BUG-04: ExternalEvent queue for deferred tag processing
        private static readonly ConcurrentQueue<ElementId> _pendingQueue = new ConcurrentQueue<ElementId>();
        private static ExternalEvent _autoTagEvent;
        private static AutoTagQueueHandler _autoTagHandler;

        /// <summary>Invalidate cached context (call after external tagging operations).</summary>
        public static void InvalidateContext()
        {
            _contextInvalid = true;
            _tag7HashCache.Clear();
            _elementVersionHash.Clear();
            // FIX-N04: Reset failure counter so auto-tagger can recover after external fixes
            _consecutiveFailures = 0;
            WasAutoDisabled = false;
            // A3: Clear processed cache on context invalidation to prevent stale-skip on document reload
            lock (_recentlyProcessed)
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
                _recentlyProcessed.Clear();
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

        /// <summary>Set the allowed discipline filter. Empty set = all disciplines.</summary>
        public static void SetDisciplineFilter(IEnumerable<string> discs)
        {
            _allowedDiscs = discs != null
                ? new HashSet<string>(discs, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                if (addedIds.Count > MaxElementsPerTrigger)
                {
                    StingLog.Info($"StingAutoTagger: skipping batch of {addedIds.Count} elements " +
                        $"(exceeds limit of {MaxElementsPerTrigger})");
                    return;
                }

                // D1: Use cached context; rebuild only when invalidated
                if (_contextInvalid || _cachedCtx == null)
                {
                    _cachedCtx = TokenAutoPopulator.PopulationContext.Build(doc);
                    var built = TagConfig.BuildTagIndexAndCounters(doc);
                    _cachedExistingTags = built.Item1;
                    _cachedSeqCounters = built.Item2;
                    _formulas = TagPipelineHelper.LoadFormulas();
                    _gridLines = TagPipelineHelper.LoadGridLines(doc);
                    _contextInvalid = false;
                    StingLog.Info($"AutoTagger: context rebuilt ({_cachedExistingTags.Count} existing tags, {_formulas.Count} formulas, {_gridLines.Count} grids)");
                }
                var ctx = _cachedCtx;
                var existingTags = _cachedExistingTags;
                var seqCounters = _cachedSeqCounters;

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
                    if (!TagConfig.DiscMap.ContainsKey(catName)) continue;

                    // Discipline filter
                    if (_allowedDiscs.Count > 0)
                    {
                        string elemDisc = TagConfig.DiscMap.TryGetValue(catName, out string dv) ? dv : "";
                        if (!_allowedDiscs.Contains(elemDisc)) continue;
                    }

                    // Workset filter
                    if (doc.IsWorkshared)
                    {
                        try
                        {
                            WorksetId wsId = el.WorksetId;
                            if (wsId != null && wsId != WorksetId.InvalidWorksetId)
                            {
                                var wsInfo = WorksharingUtils.GetWorksharingTooltipInfo(doc, el.Id);
                                if (!string.IsNullOrEmpty(wsInfo.Owner)
                                    && wsInfo.Owner != doc.Application.Username
                                    && wsInfo.Owner != "")
                                    continue;
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
                    // Rebuild context if needed
                    if (_contextInvalid || _cachedCtx == null)
                    {
                        _cachedCtx = TokenAutoPopulator.PopulationContext.Build(doc);
                        var built = TagConfig.BuildTagIndexAndCounters(doc);
                        _cachedExistingTags = built.Item1;
                        _cachedSeqCounters = built.Item2;
                        // FIX-02: Also reload formulas and grid lines on context rebuild
                        _formulas = TagPipelineHelper.LoadFormulas();
                        _gridLines = TagPipelineHelper.LoadGridLines(doc);
                        _contextInvalid = false;
                        StingLog.Info($"AutoTagger: context rebuilt ({_cachedExistingTags.Count} existing tags, {_formulas.Count} formulas)");
                    }
                    var ctx = _cachedCtx;
                    var existingTags = _cachedExistingTags;
                    var seqCounters = _cachedSeqCounters;

                    // Dequeue up to MaxPerBatch elements
                    var batch = new List<ElementId>();
                    while (batch.Count < MaxPerBatch && _pendingQueue.TryDequeue(out ElementId eid))
                        batch.Add(eid);

                    if (batch.Count == 0) return;

                    int processed = 0;
                    using (var trans = new Transaction(doc, "STING Auto-Tag"))
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

                                                IndependentTag.Create(
                                                    doc, tagType.Id, view.Id, new Reference(el),
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
                                        int toRemove = 1000;
                                        while (toRemove-- > 0 && _recentlyProcessedQueue.Count > 0)
                                        {
                                            long oldest = _recentlyProcessedQueue.Dequeue();
                                            _recentlyProcessed.Remove(oldest);
                                        }
                                    }
                                }

                                processed++;
                            }
                            catch (Exception elEx)
                            {
                                StingLog.Warn($"AutoTagger queue element {id.Value}: {elEx.Message}");
                            }
                        }
                        trans.Commit();

                        // FIX-02: Save SEQ sidecar after commit for session continuity
                        try { TagConfig.SaveSeqSidecar(doc, seqCounters); }
                        catch (Exception ssEx) { StingLog.Warn($"AutoTagger SaveSeqSidecar: {ssEx.Message}"); }
                    }

                    ComplianceScan.InvalidateCache();
                    _consecutiveFailures = 0;

                    if (processed > 0)
                        StingLog.Info($"AutoTagger queue: processed {processed}/{batch.Count} elements");

                    // If more items remain in queue, raise event again
                    if (!_pendingQueue.IsEmpty && _autoTagEvent != null)
                        _autoTagEvent.Raise();
                }
                catch (Exception ex)
                {
                    StingLog.Error("AutoTagQueueHandler.Execute", ex);
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

                // Limit batch size to prevent performance issues
                if (modifiedIds.Count > 100) return;

                var idsToMark = new List<ElementId>();

                foreach (ElementId id in modifiedIds)
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
                                // LG-04: Record which tokens changed for targeted re-derivation
                                string curLoc = ParameterHelpers.GetString(el, ParamRegistry.LOC);
                                string curZone = ParameterHelpers.GetString(el, ParamRegistry.ZONE);
                                string curLvl = ParameterHelpers.GetString(el, ParamRegistry.LVL);
                                string curTag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                                string newHash = $"{curTag}|{curLoc}|{curZone}|{curLvl}";

                                // Determine which tokens changed
                                if (_elementVersionHash.TryGetValue(id.Value, out string prevHash) && prevHash != newHash)
                                {
                                    var changedTokens = new List<string>();
                                    string[] prevParts = prevHash.Split('|');
                                    string[] curParts = newHash.Split('|');
                                    string[] tokenNames = { "TAG1", "LOC", "ZONE", "LVL" };
                                    for (int ti = 0; ti < tokenNames.Length && ti < prevParts.Length && ti < curParts.Length; ti++)
                                    {
                                        if (prevParts[ti] != curParts[ti])
                                            changedTokens.Add(tokenNames[ti]);
                                    }
                                    if (changedTokens.Count > 0)
                                    {
                                        try
                                        {
                                            Parameter staleTokens = el.LookupParameter("ASS_STALE_TOKENS_TXT");
                                            if (staleTokens != null && !staleTokens.IsReadOnly)
                                                staleTokens.Set(string.Join(",", changedTokens));
                                        }
                                        catch (Exception stEx) { StingLog.Warn($"StaleTokens write for {id.Value}: {stEx.Message}"); }
                                    }
                                }
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

                // LOG-09: Prune version hash cache when it grows too large
                if (_elementVersionHash.Count > 10000)
                    _elementVersionHash.Clear();

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
            return new ElementMulticategoryFilter(cats);
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

        public void Execute(UpdaterData data)
        {
            if (!_enabled) return;

            try
            {
                Document doc = data.GetDocument();
                if (doc == null || !doc.IsValidObject) return;

                var modifiedIds = data.GetModifiedElementIds();
                if (modifiedIds == null || modifiedIds.Count == 0) return;
                if (modifiedIds.Count > MaxElementsPerTrigger) return;

                // FIX-N07: Build roomIndex ONCE before the loop to avoid NullReferenceException
                // and prevent per-element room collector rebuilds inside an IUpdater
                Dictionary<ElementId, Autodesk.Revit.DB.Architecture.Room> roomIndex = null;
                string projectLoc = null;
                try
                {
                    roomIndex = SpatialAutoDetect.BuildRoomIndex(doc);
                    projectLoc = SpatialAutoDetect.DetectProjectLoc(doc);
                }
                catch (Exception riEx)
                {
                    StingLog.Warn($"StingStaleMarker room index build: {riEx.Message}");
                }

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

                        if (isStale)
                        {
                            Parameter p = el.LookupParameter(ParamRegistry.STALE);
                            if (p != null && !p.IsReadOnly)
                                p.Set(1);
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"StingStaleMarker element {id.Value}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingStaleMarker.Execute: {ex.Message}");
            }
        }
    }
}
