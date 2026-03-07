using System;
using System.Collections.Generic;
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
        private static readonly HashSet<long> _recentlyProcessed = new HashSet<long>();
        private static int _processedCount;

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

                // Build context once for the batch
                var ctx = TokenAutoPopulator.PopulationContext.Build(doc);
                var existingTags = TagConfig.BuildExistingTagIndex(doc);
                var seqCounters = TagConfig.GetExistingSequenceCounters(doc);

                foreach (ElementId id in addedIds)
                {
                    // Skip recently processed (avoid re-trigger loops)
                    if (_recentlyProcessed.Contains(id.Value)) continue;

                    Element el = doc.GetElement(id);
                    if (el == null || !el.IsValidObject) continue;

                    string catName = ParameterHelpers.GetCategoryName(el);
                    if (string.IsNullOrEmpty(catName)) continue;
                    if (!TagConfig.DiscMap.ContainsKey(catName)) continue;

                    // Populate all tokens
                    TokenAutoPopulator.PopulateAll(doc, el, ctx, overwrite: false);

                    // Build and write the tag
                    TagConfig.BuildAndWriteTag(doc, el, seqCounters,
                        skipComplete: true, existingTags: existingTags,
                        collisionMode: TagCollisionMode.AutoIncrement);

                    _recentlyProcessed.Add(id.Value);
                    _processedCount++;
                }

                // Trim processed cache to prevent unbounded growth
                if (_recentlyProcessed.Count > 10000)
                    _recentlyProcessed.Clear();

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
}
