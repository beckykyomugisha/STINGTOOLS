using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    //
    //  Fixes from MASTER_191_GAPS:
    //    CORE-05: Dynamic category filter from DiscMap (not hardcoded 22)
    //    CORE-06: Persist enabled state to StingPrefs file across sessions
    //    CORE-07: SubTransaction modifiability check before writes
    //    CORE-08: Skip elements that already have complete tags (copy/paste)
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
        private static int _skippedComplete; // CORE-08: count of skipped pre-tagged elements

        private readonly AddInId _addinId;

        /// <summary>Prefs file for persisting auto-tagger state (CORE-06).</summary>
        private static string PrefsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StingTools", "StingPrefs.json");

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
        public static int SkippedComplete => _skippedComplete;

        /// <summary>
        /// Register the updater with Revit. Called from OnStartup.
        /// Starts in disabled state unless persisted preference says otherwise (CORE-06).
        /// </summary>
        public static void Register(UIControlledApplication application)
        {
            try
            {
                _instance = new StingAutoTagger(application.ActiveAddInId);
                UpdaterRegistry.RegisterUpdater(_instance, true);

                // CORE-05: Build dynamic category filter from DiscMap keys
                var multiCatFilter = CreateMultiCategoryFilter();

                UpdaterRegistry.AddTrigger(_updaterId, multiCatFilter,
                    Element.GetChangeTypeElementAddition());

                // CORE-06: Check persisted preference
                bool savedState = LoadPersistedState();

                if (savedState)
                {
                    UpdaterRegistry.EnableUpdater(_updaterId);
                    _enabled = true;
                    StingLog.Info("StingAutoTagger: registered (enabled from persisted preference)");
                }
                else
                {
                    UpdaterRegistry.DisableUpdater(_updaterId);
                    _enabled = false;
                    StingLog.Info("StingAutoTagger: registered (disabled by default)");
                }

                // UI-04: Set initial LED state
                try { UI.StingDockPanel.UpdateAutoTaggerLed(_enabled); }
                catch { /* Panel may not be initialized yet */ }
            }
            catch (Exception ex)
            {
                StingLog.Error("StingAutoTagger: registration failed", ex);
            }
        }

        /// <summary>Unregister on shutdown. Persist current state (CORE-06).</summary>
        public static void Unregister()
        {
            try
            {
                SavePersistedState(_enabled);
                if (_updaterId != null)
                    UpdaterRegistry.UnregisterUpdater(_updaterId);
                StingLog.Info($"StingAutoTagger: unregistered (state={_enabled}, processed={_processedCount})");
            }
            catch { }
        }

        /// <summary>Toggle the auto-tagger on/off.</summary>
        public static bool Toggle()
        {
            if (_updaterId == null) return false;

            if (_enabled)
            {
                UpdaterRegistry.DisableUpdater(_updaterId);
                _enabled = false;
                StingLog.Info("StingAutoTagger: disabled");
            }
            else
            {
                UpdaterRegistry.EnableUpdater(_updaterId);
                _enabled = true;
                _recentlyProcessed.Clear();
                StingLog.Info("StingAutoTagger: enabled");
            }

            // CORE-06: Persist state for next session
            SavePersistedState(_enabled);

            // UI-04: Update AutoTagger LED indicator in dockable panel
            try { UI.StingDockPanel.UpdateAutoTaggerLed(_enabled); }
            catch { /* Non-critical UI update */ }

            return _enabled;
        }

        /// <summary>
        /// Called by Revit when elements are added to tagged categories.
        /// Auto-populates tokens and builds ISO 19650 tag.
        /// CORE-07: Uses SubTransaction for modifiability safety.
        /// CORE-08: Skips elements that already have complete tags (copy/paste).
        /// </summary>
        public void Execute(UpdaterData data)
        {
            if (!_enabled) return;

            Document doc = data.GetDocument();
            if (doc == null) return;

            var addedIds = data.GetAddedElementIds();
            if (addedIds == null || addedIds.Count == 0) return;

            try
            {
                // Build context once for the batch
                var ctx = TokenAutoPopulator.PopulationContext.Build(doc);
                var existingTags = TagConfig.BuildExistingTagIndex(doc);
                var seqCounters = TagConfig.GetExistingSequenceCounters(doc);

                // Throttle: if too many elements added at once (e.g. linked model),
                // limit to first 500 to prevent UI freeze
                int limit = Math.Min(addedIds.Count, 500);
                int processed = 0;

                foreach (ElementId id in addedIds)
                {
                    if (processed >= limit) break;

                    // Skip recently processed (avoid re-trigger loops)
                    if (_recentlyProcessed.Contains(id.Value)) continue;

                    Element el = doc.GetElement(id);
                    if (el == null) continue;

                    string catName = ParameterHelpers.GetCategoryName(el);
                    if (string.IsNullOrEmpty(catName)) continue;
                    if (!TagConfig.DiscMap.ContainsKey(catName)) continue;

                    // CORE-08: Skip elements that already have complete tags
                    // (e.g. copy/paste — preserve original tag assignments)
                    string existingTag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                    if (!string.IsNullOrEmpty(existingTag) && TagConfig.TagIsComplete(existingTag))
                    {
                        _recentlyProcessed.Add(id.Value);
                        _skippedComplete++;
                        processed++;
                        continue;
                    }

                    // CORE-07: Use SubTransaction for modifiability safety
                    using (SubTransaction st = new SubTransaction(doc))
                    {
                        if (st.Start() != TransactionStatus.Started)
                        {
                            StingLog.Warn($"StingAutoTagger: SubTransaction failed to start for element {id}");
                            continue;
                        }

                        try
                        {
                            // Populate all tokens
                            TokenAutoPopulator.PopulateAll(doc, el, ctx, overwrite: false);

                            // Build and write the tag
                            TagConfig.BuildAndWriteTag(doc, el, seqCounters,
                                skipComplete: true, existingTags: existingTags,
                                collisionMode: TagCollisionMode.AutoIncrement);

                            st.Commit();
                            _recentlyProcessed.Add(id.Value);
                            _processedCount++;
                        }
                        catch (Exception ex)
                        {
                            st.RollBack();
                            StingLog.Warn($"StingAutoTagger: failed on element {id}: {ex.Message}");
                        }
                    }

                    processed++;
                }

                // Trim processed cache to prevent unbounded growth
                if (_recentlyProcessed.Count > 10000)
                    _recentlyProcessed.Clear();

                // Log batch summary if >10 elements
                if (processed > 10)
                    StingLog.Info($"StingAutoTagger: batch processed {processed} elements " +
                        $"({_skippedComplete} skipped with complete tags)");
            }
            catch (Exception ex)
            {
                StingLog.Error("StingAutoTagger.Execute", ex);
            }
        }

        /// <summary>
        /// CORE-05: Build a dynamic multi-category filter from DiscMap keys.
        /// Falls back to hardcoded list if DiscMap is not yet loaded.
        /// </summary>
        private static ElementMulticategoryFilter CreateMultiCategoryFilter()
        {
            var cats = new HashSet<BuiltInCategory>();

            // Try to build dynamically from DiscMap category names
            try
            {
                foreach (string catName in TagConfig.DiscMap.Keys)
                {
                    if (ParamRegistry.CategoryEnumMap.TryGetValue(catName, out string enumName))
                    {
                        if (Enum.TryParse(enumName, out BuiltInCategory bic))
                            cats.Add(bic);
                    }
                }
            }
            catch
            {
                // DiscMap not loaded yet — use defaults
            }

            // Ensure minimum set is always present (fallback)
            if (cats.Count < 10)
            {
                cats.Add(BuiltInCategory.OST_MechanicalEquipment);
                cats.Add(BuiltInCategory.OST_ElectricalEquipment);
                cats.Add(BuiltInCategory.OST_ElectricalFixtures);
                cats.Add(BuiltInCategory.OST_LightingFixtures);
                cats.Add(BuiltInCategory.OST_LightingDevices);
                cats.Add(BuiltInCategory.OST_PlumbingFixtures);
                cats.Add(BuiltInCategory.OST_Sprinklers);
                cats.Add(BuiltInCategory.OST_FireAlarmDevices);
                cats.Add(BuiltInCategory.OST_DataDevices);
                cats.Add(BuiltInCategory.OST_CommunicationDevices);
                cats.Add(BuiltInCategory.OST_SecurityDevices);
                cats.Add(BuiltInCategory.OST_NurseCallDevices);
                cats.Add(BuiltInCategory.OST_DuctAccessory);
                cats.Add(BuiltInCategory.OST_DuctFitting);
                cats.Add(BuiltInCategory.OST_DuctTerminal);
                cats.Add(BuiltInCategory.OST_PipeAccessory);
                cats.Add(BuiltInCategory.OST_PipeFitting);
                cats.Add(BuiltInCategory.OST_Furniture);
                cats.Add(BuiltInCategory.OST_Doors);
                cats.Add(BuiltInCategory.OST_Windows);
                cats.Add(BuiltInCategory.OST_CableTray);
                cats.Add(BuiltInCategory.OST_Conduit);
                // CORE-05: Additional categories previously missing
                cats.Add(BuiltInCategory.OST_Walls);
                cats.Add(BuiltInCategory.OST_Floors);
                cats.Add(BuiltInCategory.OST_StructuralColumns);
                cats.Add(BuiltInCategory.OST_StructuralFoundation);
                cats.Add(BuiltInCategory.OST_StructuralFraming);
                cats.Add(BuiltInCategory.OST_Rooms);
                cats.Add(BuiltInCategory.OST_Roofs);
                cats.Add(BuiltInCategory.OST_Ceilings);
                cats.Add(BuiltInCategory.OST_FlexDucts);
                cats.Add(BuiltInCategory.OST_FlexPipes);
                cats.Add(BuiltInCategory.OST_ConduitFitting);
                cats.Add(BuiltInCategory.OST_CableTrayFitting);
                cats.Add(BuiltInCategory.OST_DuctCurves);
                cats.Add(BuiltInCategory.OST_PipeCurves);
                cats.Add(BuiltInCategory.OST_ElectricalCircuit);
            }

            return new ElementMulticategoryFilter(cats.ToList());
        }

        // ── CORE-06: State persistence ──────────────────────────────────

        private static bool LoadPersistedState()
        {
            try
            {
                if (File.Exists(PrefsPath))
                {
                    string json = File.ReadAllText(PrefsPath);
                    return json.Contains("\"autoTaggerEnabled\":true") ||
                           json.Contains("\"autoTaggerEnabled\": true");
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingAutoTagger: failed to load prefs: {ex.Message}");
            }
            return false;
        }

        private static void SavePersistedState(bool enabled)
        {
            try
            {
                string dir = Path.GetDirectoryName(PrefsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(PrefsPath,
                    $"{{\n  \"autoTaggerEnabled\": {(enabled ? "true" : "false")}\n}}");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingAutoTagger: failed to save prefs: {ex.Message}");
            }
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
            bool nowEnabled = StingAutoTagger.Toggle();
            TaskDialog.Show("Auto-Tagger",
                nowEnabled
                    ? "Real-time auto-tagging ENABLED.\n\n" +
                      "Newly placed elements will be automatically tagged\n" +
                      "with ISO 19650 codes (DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ).\n\n" +
                      $"Elements auto-tagged so far: {StingAutoTagger.ProcessedCount}\n" +
                      $"Elements skipped (already tagged): {StingAutoTagger.SkippedComplete}\n\n" +
                      "State will persist across Revit sessions."
                    : "Real-time auto-tagging DISABLED.\n\n" +
                      $"Total elements auto-tagged this session: {StingAutoTagger.ProcessedCount}\n" +
                      $"Elements skipped (already tagged): {StingAutoTagger.SkippedComplete}\n\n" +
                      "State will persist across Revit sessions.");
            return Result.Succeeded;
        }
    }
}
