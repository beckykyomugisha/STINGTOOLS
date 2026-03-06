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

        /// <summary>
        /// Register the updater with Revit. Called from OnStartup.
        /// Starts in disabled state — user must toggle on explicitly.
        /// </summary>
        public static void Register(UIControlledApplication application)
        {
            try
            {
                _instance = new StingAutoTagger(application.ActiveAddInId);
                UpdaterRegistry.RegisterUpdater(_instance, true);

                // Add triggers for element addition in tagged categories
                var catFilter = new ElementCategoryFilter(BuiltInCategory.OST_MechanicalEquipment);
                var multiCatFilter = CreateMultiCategoryFilter();

                UpdaterRegistry.AddTrigger(_updaterId, multiCatFilter,
                    Element.GetChangeTypeElementAddition());

                // Start disabled
                UpdaterRegistry.DisableUpdater(_updaterId);
                _enabled = false;
                StingLog.Info("StingAutoTagger: registered (disabled by default)");
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
            return _enabled;
        }

        /// <summary>
        /// Called by Revit when elements are added to tagged categories.
        /// Auto-populates tokens and builds ISO 19650 tag.
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
                // Build context once for the batch (single-pass for both indexes)
                var ctx = TokenAutoPopulator.PopulationContext.Build(doc);
                var (existingTags, seqCounters) = TagConfig.BuildTagIndexAndCounters(doc);

                foreach (ElementId id in addedIds)
                {
                    // Skip recently processed (avoid re-trigger loops)
                    if (_recentlyProcessed.Contains(id.Value)) continue;

                    Element el = doc.GetElement(id);
                    if (el == null) continue;

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
            }
            catch (Exception ex)
            {
                StingLog.Error("StingAutoTagger.Execute", ex);
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
            bool nowEnabled = StingAutoTagger.Toggle();
            TaskDialog.Show("Auto-Tagger",
                nowEnabled
                    ? "Real-time auto-tagging ENABLED.\n\n" +
                      "Newly placed elements will be automatically tagged\n" +
                      "with ISO 19650 codes (DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ).\n\n" +
                      $"Elements auto-tagged so far: {StingAutoTagger.ProcessedCount}"
                    : "Real-time auto-tagging DISABLED.\n\n" +
                      $"Total elements auto-tagged this session: {StingAutoTagger.ProcessedCount}");
            return Result.Succeeded;
        }
    }
}
