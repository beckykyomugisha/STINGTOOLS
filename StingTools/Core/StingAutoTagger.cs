using System;
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

        /// <summary>Invalidate cached context (call after external tagging operations).</summary>
        public static void InvalidateContext() { _contextInvalid = true; }

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
            catch { }
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

        /// <summary>Enable or disable visual tag placement during auto-tagging.</summary>
        public static void SetVisualTagging(bool enabled) { _visualTaggingEnabled = enabled; }
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

        public void Execute(UpdaterData data)
        {
            if (!_enabled) return;

            try
            {
                Document doc = data.GetDocument();
                if (doc == null || !doc.IsValidObject) return;

                var addedIds = data.GetAddedElementIds();
                if (addedIds == null || addedIds.Count == 0) return;

                // Guard: limit elements per trigger to prevent performance issues
                // during large paste/import operations
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
                    _contextInvalid = false;
                    StingLog.Info($"AutoTagger: context rebuilt ({_cachedExistingTags.Count} existing tags)");
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

                    // Item 5: Discipline filter — skip elements not in allowed disciplines
                    if (_allowedDiscs.Count > 0)
                    {
                        string elemDisc = TagConfig.DiscMap.TryGetValue(catName, out string dv) ? dv : "";
                        if (!_allowedDiscs.Contains(elemDisc)) continue;
                    }

                    // Item 5: Workset filter — skip elements not owned by current user
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
                                {
                                    StingLog.Info($"AutoTagger: skipping el {id.Value} owned by '{wsInfo.Owner}'");
                                    continue;
                                }
                            }
                        }
                        catch { /* workset check is advisory — never block tagging */ }
                    }

                    // Item 1: Inherit tokens from family type before auto-detection
                    TokenAutoPopulator.TypeTokenInherit(doc, el);

                    // Populate all tokens
                    TokenAutoPopulator.PopulateAll(doc, el, ctx, overwrite: false);

                    // Build and write the tag
                    TagConfig.BuildAndWriteTag(doc, el, seqCounters,
                        skipComplete: true, existingTags: existingTags,
                        collisionMode: TagCollisionMode.AutoIncrement,
                        cachedRev: ctx.ProjectRev);

                    // D1: Incrementally track newly created tags to avoid rebuilding the full index
                    string newTag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                    if (!string.IsNullOrEmpty(newTag)) existingTags.Add(newTag);

                    // Write TAG7 + sub-sections (TAG7A-TAG7F) — rich descriptive narrative
                    try
                    {
                        string[] tokenVals = ParamRegistry.ReadTokenValues(el);
                        TagConfig.WriteTag7All(doc, el, catName, tokenVals, overwrite: false);
                    }
                    catch (Exception tag7Ex)
                    {
                        StingLog.Warn($"AutoTagger TAG7 for {id}: {tag7Ex.Message}");
                    }

                    // Auto-combine: propagate tag to discipline-specific containers
                    try
                    {
                        string[] combineTokens = ParamRegistry.ReadTokenValues(el);
                        ParamRegistry.WriteContainers(el, combineTokens, catName);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"AutoTagger combine failed for {el.Id.Value}: {ex.Message}");
                    }

                    // Item 4: Visual tag placement — only if user has enabled visual auto-tagging
                    if (_visualTaggingEnabled && doc.ActiveView != null
                        && !(doc.ActiveView is ViewSheet)
                        && doc.ActiveView.CanBePrinted)
                    {
                        try
                        {
                            View view = doc.ActiveView;
                            // F1: Skip visual tag if element is not visible in the active view
                            BoundingBoxXYZ elBb = el.get_BoundingBox(view);
                            if (elBb == null)
                            {
                                StingLog.Info($"AutoTagger: element {el.Id.Value} not visible in view '{view.Name}' — skipping visual tag");
                            }
                            else
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
                            StingLog.Warn($"AutoTagger visual tag placement: {vex.Message}");
                        }
                    }

                    lock (_processedLock)
                    {
                        _recentlyProcessed.Add(id.Value);
                        _recentlyProcessedQueue.Enqueue(id.Value);
                        _processedCount++;

                        // LRU eviction: remove oldest 1000 entries instead of clearing all.
                        // Full Clear() creates a window where recently processed elements
                        // can be re-tagged before the cache refills.
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
                }

                // Reset failure counter on success
                _consecutiveFailures = 0;
            }
            catch (Exception ex)
            {
                StingLog.Error("StingAutoTagger.Execute", ex);
                _consecutiveFailures++;

                // Auto-disable after repeated failures to prevent Revit instability
                if (_consecutiveFailures >= MaxFailuresBeforeAutoDisable)
                {
                    StingLog.Error($"StingAutoTagger: auto-disabling after {_consecutiveFailures} " +
                        "consecutive failures to prevent Revit instability");
                    WasAutoDisabled = true;
                    try { Toggle(); } catch { _enabled = false; }

                    // Notify the user via the dockable panel status bar
                    try
                    {
                        UI.StingDockPanel.UpdateComplianceStatus(
                            "Auto-Tagger DISABLED (errors — re-enable via toggle)", "RED");
                    }
                    catch { /* Panel may not be loaded yet */ }
                }
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
            TaskDialog.Show("Auto-Tagger", msg);
            return Result.Succeeded;
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
                StingAutoTagger.SetVisualTagging(!StingAutoTagger.IsVisualTaggingEnabled);
            else if (result == TaskDialogResult.CommandLink2)
                StingStaleMarker.SetEnabled(!StingStaleMarker.IsEnabled);
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
            catch { }
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

                foreach (ElementId id in modifiedIds)
                {
                    try
                    {
                        Element el = doc.GetElement(id);
                        if (el == null || !el.IsValidObject) continue;

                        // Only mark elements that already have a tag
                        string tag1 = ParameterHelpers.GetString(el, ParamRegistry.AllTokenParams[0]);
                        if (string.IsNullOrEmpty(tag1)) continue;

                        // Check if spatial context changed
                        string currentLvl = ParameterHelpers.GetLevelCode(doc, el);
                        string storedLvl = ParameterHelpers.GetString(el, ParamRegistry.LVL);

                        if (!string.IsNullOrEmpty(storedLvl) && currentLvl != storedLvl)
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
