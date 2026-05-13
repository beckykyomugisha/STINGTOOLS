using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
// Disambiguate System.Drawing.Color from Autodesk.Revit.DB.Color (CS0104).
using DrawingColor = System.Drawing.Color;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using Newtonsoft.Json.Linq;
using StingTools.UI;
using StingTools.BIMManager;
using StingTools.Core.Clash;
using Planscape.PluginSync;

namespace StingTools.Core
{
    /// <summary>
    /// Main Revit external application. Registers the STING Tools dockable panel
    /// as the single unified UI for all 160+ commands across 6 tabs:
    /// SELECT, ORGANISE, DOCS, TEMP, CREATE, VIEW.
    /// The ribbon tab contains only a single toggle button to show/hide the panel.
    /// </summary>
    // Note: CA1416 coverage is provided assembly-wide by
    // [assembly: SupportedOSPlatform("windows")] in Properties/AssemblyInfo.cs.
    public class StingToolsApp : IExternalApplication
    {
        public static string AssemblyPath { get; private set; }
        public static string DataPath { get; private set; }
        private static UpdaterId _sldUpdaterId;

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                AssemblyPath = Assembly.GetExecutingAssembly().Location;
                DataPath = Path.Combine(
                    Path.GetDirectoryName(AssemblyPath) ?? string.Empty,
                    "data");

                // Validate data directory and critical files at startup
                ValidateDataFiles();

                // Pre-flight: log assembly environment for crash diagnostics
                LogAssemblyEnvironment();

                // Pack 0 — establish offline-first defaults. Per-project config
                // loads later in OnDocumentOpened and can flip the flag off.
                StingOfflineConfig.ApplyDefaults();

                // Wire the Standards library's pluggable log sink to StingLog so
                // StandardsComplianceEngine messages land in the same log file as
                // the rest of the plugin (replaces the old NLog dependency).
                StingTools.Standards.Compliance.StandardsLog.Sink = (lvl, msg, ex) =>
                {
                    if (lvl == StingTools.Standards.Compliance.StandardsLogLevel.Info) StingLog.Info(msg);
                    else if (lvl == StingTools.Standards.Compliance.StandardsLogLevel.Warn) StingLog.Warn(msg);
                    else StingLog.Error(msg, ex);
                };

                // Pack 7 — wire the DocumentChanged cascade handler (room
                // renumbers, level changes, sheet ISO violations). Gated by
                // StingOfflineConfig.RealtimeCascadesEnabled at callback time.
                StingDocumentChangedHandler.Register(application);

                // Pack 8 — wire the Idling scheduler. Commands enqueue jobs
                // via StingIdlingScheduler.Enqueue(job).
                StingIdlingScheduler.Register(application);

                // Register the dockable panel — the single unified UI
                RegisterDockablePanel(application);
                RegisterElectricalPanel(application);
                RegisterPlumbingPanel(application);

                // Register the real-time auto-tagger (IUpdater) — starts disabled
                StingAutoTagger.Register(application);

                // Register the Tag 7 narrative auto-updater (IUpdater) — starts disabled.
                // Keeps ASS_TAG_7_TXT in sync with the active paragraph preset when
                // source parameters change. Users enable it from Tag Studio.
                StingTag7NarrativeUpdater.Register(application);

                // Phase 175 — SLD sync updater. Registered unconditionally; it
                // self-gates on project_config "sld_sync_enabled" inside Execute.
                try
                {
                    var sldUpdater = new StingTools.Core.SLD.SLDSyncUpdater(application.ActiveAddInId);
                    Autodesk.Revit.DB.UpdaterRegistry.RegisterUpdater(sldUpdater);
                    var elecFilter = new Autodesk.Revit.DB.ElementMulticategoryFilter(
                        new System.Collections.Generic.List<Autodesk.Revit.DB.BuiltInCategory>
                        {
                            Autodesk.Revit.DB.BuiltInCategory.OST_ElectricalEquipment,
                            Autodesk.Revit.DB.BuiltInCategory.OST_ElectricalFixtures,
                            Autodesk.Revit.DB.BuiltInCategory.OST_LightingFixtures,
                        });
                    Autodesk.Revit.DB.UpdaterRegistry.AddTrigger(
                        sldUpdater.GetUpdaterId(), elecFilter,
                        Autodesk.Revit.DB.Element.GetChangeTypeAny());
                    _sldUpdaterId = sldUpdater.GetUpdaterId();
                }
                catch (Exception sldEx)
                {
                    StingLog.Warn($"SLDSyncUpdater register failed: {sldEx.Message}");
                }

                // Register CableManifestUpdater (conduit/tray change → manifest sync)
                try
                {
                    StingTools.Core.Routing.CableManifestUpdater.Register(application);
                    StingLog.Info("CableManifestUpdater registered.");
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"CableManifestUpdater.Register failed: {ex.Message}");
                }

                // Register the real Core.Clash live updater (9-category geometry/
                // addition/deletion triggers). Replaces the prior Phase-106 stub
                // call which resolved via `using StingTools.Clash;` to a no-op
                // updater that never populated DirtyQueue.
                LiveClashUpdater.Register(application);

                // Subscribe DocumentChanged → drain LiveClashUpdater.DirtyQueue
                // by raising LiveClashHandler.Event. Without this, edits queue
                // forever and the live + scheduled clash paths never run after
                // the first scheduler tick (the scheduler's dirty gate reads
                // ClashSession.LastDirtyAtUtc, which is only advanced inside
                // RefreshElement/RemoveElement on the live path).
                LiveClashWireup.Subscribe(application);

                // CRASH FIX: Eagerly load ParamRegistry at startup instead of lazy-loading
                // on first command. This ensures:
                //   1. JSON parsing errors surface at startup where they're diagnosable
                //   2. Newtonsoft.Json assembly conflicts are caught before any command runs
                //   3. All subsequent commands get pre-loaded data with zero crash risk
                //   4. The data file path is validated once, not on every button click
                try
                {
                    ParamRegistry.EnsureLoaded();
                    StingLog.Info("ParamRegistry pre-loaded at startup successfully");
                }
                catch (Exception ex)
                {
                    StingLog.Error("ParamRegistry pre-load failed (commands will use defaults)", ex);
                }

                // Load user-preferred output directory from project_config.json
                try { OutputLocationHelper.LoadFromConfig(); }
                catch (Exception ex) { StingLog.Warn($"OutputLocationHelper config load: {ex.Message}"); }

                // Load project folder root from project_config.json
                try { ProjectFolderEngine.LoadRootFromConfig(); }
                catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine config load: {ex.Message}"); }

                // Route BcfEngine warnings into StingLog. BcfEngine.cs lives in
                // Planscape.Shared.dll (server-compatible, no Revit dependency),
                // so it can't reference StingLog directly — the hook bridges the
                // two assemblies without creating a namespace dependency.
                Planscape.Shared.BCF.BcfEngine.Warn = msg => StingLog.Warn(msg);

                // CRASH FIX: Subscribe to DocumentClosing to clear stale static caches.
                // ElementId-based caches and Definition caches become invalid when a
                // document closes. Using them against a new document causes native crashes.
                application.ControlledApplication.DocumentClosing += OnDocumentClosing;
                // BUG-05: Also clear param cache on document open to prevent cross-document
                // cache collisions when switching between documents.
                application.ControlledApplication.DocumentOpened += OnDocumentOpened;
                // FIX-15: Removed duplicate DocumentOpened subscription (was ENH-06)

                // FIX-06: Invalidate auto-tagger cache when switching between open documents
                application.ViewActivated += OnViewActivated;

                // R-02: Retry deferred auto-tag elements after sync-to-central
                application.ControlledApplication.DocumentSynchronizedWithCentral += OnDocumentSynchronizedWithCentral;

                // INT-03: Auto-sync to Planscape server after every successful STC.
                // Separate handler from OnDocumentSynchronizedWithCentral so the
                // deferred auto-tag retry stays isolated from the server sync concern.
                application.ControlledApplication.DocumentSynchronizedWithCentral += OnPlanscapeSyncAfterSTC;

                // AUTO-SYNC: Queue lightweight compliance sync on document save
                application.ControlledApplication.DocumentSaved += OnDocumentSaved;

                // S03b / Phase 91 — Start the Planscape sync scheduler if the plugin has
                // already authenticated with the server (persisted from a previous session).
                // Runs on a 5-min timer in-process: on each tick PluginSyncTickBridge builds
                // a payload from the active document and enqueues it for drain. Safe no-op
                // if not configured — in that case PlanscapeConnectCommand will lazy-start
                // the scheduler after LoginAsync succeeds.
                try
                {
                    // Always wire the tick bridge so whichever path starts the scheduler
                    // (OnStartup persisted creds, PlanscapeConnect, or SyncToPlanscapeServer
                    // lazy-start) gets the OnTick callback marshalled to the Revit thread.
                    StingTools.BIMManager.PluginSyncTickBridge.EnsureWired();

                    var serverUrl = PlanscapeServerClient.Instance?.ServerUrl;
                    var authToken = PlanscapeServerClient.Instance?.AuthToken;
                    if (!string.IsNullOrEmpty(serverUrl) && !string.IsNullOrEmpty(authToken))
                    {
                        if (SyncScheduler.Instance == null)
                        {
                            SyncScheduler.Start(serverUrl, authToken);
                            StingLog.Info($"SyncScheduler started against {serverUrl} (5-min tick, offline queue enabled)");
                        }

                        // INT-07 — keep the dock-panel sync chip in step with each sync attempt.
                        if (SyncScheduler.Instance != null)
                        {
                            SyncScheduler.Instance.OnSyncComplete += _ =>
                            {
                                StingDockPanel.LastInstance?.RefreshSyncIndicator();
                            };
                        }
                    }
                    else
                    {
                        StingLog.Info("SyncScheduler not started — no server URL / auth token yet; PlanscapeConnect will start it after login");
                    }
                }
                catch (Exception syncEx) { StingLog.Warn($"SyncScheduler start failed: {syncEx.Message}"); }

                StingLog.Info("STING Tools dockable panel loaded successfully");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("STING Tools",
                    "Failed to initialise STING Tools:\n" + ex.Message);
                StingLog.Error("Startup failed", ex);
                return Result.Failed;
            }
        }

        /// <summary>
        /// CRASH FIX: Clear all static caches that hold ElementId or Definition references.
        /// These become invalid when a document closes and cause native crashes if used
        /// against a different document.
        /// </summary>
        private static void OnDocumentClosing(object sender,
            Autodesk.Revit.DB.Events.DocumentClosingEventArgs e)
        {
            try
            {
                // GAP-FIX: Auto-save warning baseline on document close
                if (TagConfig.AutoSaveWarningBaseline)
                {
                    try
                    {
                        var doc = e.Document;
                        if (doc != null && !doc.IsFamilyDocument)
                        {
                            WarningsEngine.SaveBaseline(doc);
                            StingLog.Info("DocumentClosing: auto-saved warning baseline");
                        }
                    }
                    catch (Exception wex) { StingLog.Warn($"Auto-save warning baseline: {wex.Message}"); }
                }

                ParameterHelpers.ClearParamCache();
                ParameterHelpers.InvalidateSessionCaches();
                ComplianceScan.InvalidateCache();
                Temp.FormulaEngine.InvalidateFormulaCache();
                UI.StingCommandHandler.ClearStaticState();
                // Phase 167: Drop the per-doc ProjectSetup cache so reopens re-detect.
                try { ProjectFolderEngine.InvalidateSetupCache(e.Document?.PathName); }
                catch (Exception cEx) { StingLog.Warn($"Setup cache invalidate: {cEx.Message}"); }
                // Phase 78: Save dropped element IDs to sidecar before clearing queue
                StingAutoTagger.SaveDroppedElementsSidecar(e.Document);
                // R-02: Clear deferred elements on document close
                StingAutoTagger.ClearDeferredQueue();
                // PERF-CRIT: Clear stale marker room index cache
                StingStaleMarker.ClearRoomIndexCache();
                // GAP-STP-02: Clear cached tag types on document close
                Tags.TagPlacementEngine.ClearTagTypeCache();
                // ME-HIGH-01: Clear per-document workset ID cache to prevent stale workset IDs
                // from the closed document being applied to subsequently opened documents.
                Model.ModelWorksetAssigner.ClearCache();
                // TAG-SORT-LEVEL-01: Clear cached level elevations to prevent stale data
                Tags.BatchTagCommand.ClearLevelElevationCache();
                // C-6 / D-3: cascade through Drawing-Type subsystem caches so
                // a stale ElementId from this document never resolves to an
                // element in the next.
                try { Drawing.DrawingTypeRegistry.Reload(e.Document); }
                catch (Exception ex) { StingLog.Warn($"DocumentClosing DrawingTypeRegistry.Reload: {ex.Message}"); }
                StingLog.Info("DocumentClosing: cleared parameter, compliance, formula, selection, deferred, workset, level, and drawing-type caches");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DocumentClosing cleanup: {ex.Message}");
            }
        }

        /// <summary>R-02: Retry deferred auto-tag elements after sync-to-central completes.
        /// Elements skipped during auto-tagging due to workset ownership are retried here.</summary>
        private static void OnDocumentSynchronizedWithCentral(object sender,
            Autodesk.Revit.DB.Events.DocumentSynchronizedWithCentralEventArgs e)
        {
            try
            {
                var deferredIds = StingAutoTagger.DrainDeferredQueue();
                if (deferredIds.Count == 0) return;

                Document doc = e.Document;
                if (doc == null || !doc.IsValidObject) return;

                var known = new HashSet<string>(TagConfig.DiscMap.Keys);
                var popCtx = TokenAutoPopulator.PopulationContext.Build(doc);
                var (tagIndex, seqCounters) = TagConfig.BuildTagIndexAndCounters(doc);
                var formulas = TagPipelineHelper.LoadFormulas();
                var gridLines = TagPipelineHelper.LoadGridLines(doc);
                var stats = new TaggingStats();
                int processed = 0;

                using (var tx = new Transaction(doc, "STING AutoTag deferred retry"))
                {
                    tx.Start();
                    foreach (var id in deferredIds)
                    {
                        try
                        {
                            Element el = doc.GetElement(id);
                            if (el == null || !el.IsValidObject) continue;
                            string cat = ParameterHelpers.GetCategoryName(el);
                            if (!known.Contains(cat)) continue;

                            bool ok = TagPipelineHelper.RunFullPipeline(
                                doc, el, popCtx, tagIndex, seqCounters,
                                formulas, gridLines,
                                overwrite: false,
                                skipComplete: true,
                                collisionMode: TagCollisionMode.AutoIncrement,
                                stats: stats);
                            if (ok) processed++;
                        }
                        catch (Exception elEx) { StingLog.Warn($"AutoTagger deferred retry element {id.Value}: {elEx.Message}"); }
                    }
                    tx.Commit();
                }

                if (processed > 0)
                {
                    try { TagConfig.SaveSeqSidecar(doc, seqCounters); } catch (Exception ex) { StingLog.Warn($"Deferred retry SEQ sidecar: {ex.Message}"); }
                    ComplianceScan.InvalidateCache();
                    StingAutoTagger.InvalidateContext();
                }

                StingLog.Info($"AutoTagger deferred retry: processed {processed}/{deferredIds.Count} elements after sync-to-central");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"OnDocumentSynchronizedWithCentral deferred retry: {ex.Message}");
            }
        }

        // GAP 1-B (INT-02) — debounce state for OnPlanscapeSyncAfterSTC.
        // STC can fire multiple times in quick succession (user hitting
        // Sync-to-Central twice, or worksharing-initiated re-syncs); we
        // throttle to at most one full payload every DebounceSeconds per
        // document path. The _pendingSyncDoc field tracks the document
        // whose sync is being held so a subsequent same-doc STC during
        // the window is a no-op, but a different doc's STC still fires.
        private static readonly object _planscapeSyncLock = new object();
        private static Document _pendingSyncDoc;
        private static DateTime _lastPlanscapeSync = DateTime.MinValue;
        private const int PlanscapeSyncDebounceSeconds = 60;

        /// <summary>INT-03: After a successful STC, trigger a Planscape server sync.
        /// Exits silently if the plugin isn't authenticated; otherwise delegates to the
        /// existing PlatformSyncCommand.SyncToPlanscapeServer() path which collects tags,
        /// builds the payload, and hands off to Planscape.PluginSync.SyncScheduler
        /// (queueing for retry on network failure).</summary>
        private static void OnPlanscapeSyncAfterSTC(object sender,
            Autodesk.Revit.DB.Events.DocumentSynchronizedWithCentralEventArgs e)
        {
            try
            {
                var client = PlanscapeServerClient.Instance;
                if (client == null || !client.IsConnected) return; // silent — not authenticated

                Document doc = e.Document;
                if (doc == null || !doc.IsValidObject || doc.IsFamilyDocument) return;

                // GAP 1-B — 60s debounce. Multiple STCs in the same window collapse
                // into a single sync. Doc-scoped so swapping documents still syncs.
                lock (_planscapeSyncLock)
                {
                    var sincePrev = DateTime.UtcNow - _lastPlanscapeSync;
                    bool sameDocPending = _pendingSyncDoc != null
                        && _pendingSyncDoc.IsValidObject
                        && string.Equals(_pendingSyncDoc.PathName, doc.PathName, StringComparison.OrdinalIgnoreCase);
                    if (sameDocPending && sincePrev.TotalSeconds < PlanscapeSyncDebounceSeconds)
                    {
                        StingLog.Info($"Planscape STC sync debounced ({sincePrev.TotalSeconds:F0}s < {PlanscapeSyncDebounceSeconds}s) for {doc.Title}");
                        return;
                    }
                    _pendingSyncDoc = doc;
                    _lastPlanscapeSync = DateTime.UtcNow;
                }

                StingLog.Info("Planscape: auto-sync triggered by STC");

                // UIApplication fallback chain:
                //   1. StingCommandHandler.CurrentApp (set during any prior command)
                //   2. Construct from the document's Application (available in event args)
                UIApplication uiApp = UI.StingCommandHandler.CurrentApp;
                if (uiApp == null)
                {
                    var revitApp = doc.Application;
                    if (revitApp != null) uiApp = new UIApplication(revitApp);
                }
                if (uiApp == null) { StingLog.Warn("Planscape STC auto-sync: no UIApplication available"); return; }

                PlatformSyncCommand.SyncToPlanscapeServer(uiApp);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"OnPlanscapeSyncAfterSTC: {ex.Message}");
            }
        }

        /// <summary>BUG-05: Clear param cache on document open to prevent cross-document collisions.</summary>
        private static void OnDocumentOpened(object sender,
            Autodesk.Revit.DB.Events.DocumentOpenedEventArgs e)
        {
            try
            {
                Temp.FormulaEngine.ClearCache();
                ParameterHelpers.ClearParamCache();
                StingAutoTagger.InvalidateContext();
                ComplianceScan.InvalidateCache();

                // FIX-C01: Reset selection scope to view-only on document switch
                // Prevents stale project-wide scope from carrying over between projects
                Select.SelectionScopeHelper.SetScope(false);

                // Standards: sync ProjectStandardsManager from this document.
                // The manager persists to %APPDATA% (per-user, not per-project)
                // so without a per-project hint, opening a different .rvt keeps
                // the previous region's electrical / fire / structural bindings
                // active. Read order: PROJECT_REGION param → sidecar JSON next
                // to the .rvt → leave singleton unchanged.
                try
                {
                    var pi = e.Document?.ProjectInformation;
                    string projectRegion = pi?.LookupParameter("PROJECT_REGION")?.AsString();
                    string source = "PROJECT_REGION";
                    if (string.IsNullOrWhiteSpace(projectRegion))
                    {
                        projectRegion = ProjectRegionSidecar.Read(e.Document);
                        source = "sting_region.json";
                    }
                    if (!string.IsNullOrWhiteSpace(projectRegion))
                    {
                        var mgr = StingTools.Standards.ProjectStandardsManager.Instance;
                        if (!string.Equals(mgr.Region, projectRegion, StringComparison.OrdinalIgnoreCase))
                        {
                            mgr.ApplyRegionalPreset(projectRegion);
                            StingLog.Info($"Standards: synced active region → {projectRegion} (from {source})");
                        }
                    }
                }
                catch (Exception regEx) { StingLog.Warn($"Standards region sync skipped: {regEx.Message}"); }

                // C4 / G1.3: Reload TagConfig on document open — prefer project-adjacent config
                // to prevent config bleed between projects
                try
                {
                    string configPath = null;
                    // First: look alongside the .rvt file for project-specific config
                    string docPath = e.Document?.PathName;
                    if (!string.IsNullOrEmpty(docPath))
                    {
                        string projectDir = System.IO.Path.GetDirectoryName(docPath);
                        if (!string.IsNullOrEmpty(projectDir))
                        {
                            string adjacent = System.IO.Path.Combine(projectDir, "project_config.json");
                            if (System.IO.File.Exists(adjacent))
                                configPath = adjacent;
                        }
                    }
                    // Fallback: look in plugin data directory
                    if (configPath == null)
                        configPath = FindDataFile("project_config.json");

                    if (configPath != null)
                        TagConfig.LoadFromFile(configPath);
                    else
                        TagConfig.LoadDefaults();
                }
                catch (Exception cfgEx)
                {
                    StingLog.Warn($"DocumentOpened TagConfig reload: {cfgEx.Message}");
                    TagConfig.LoadDefaults();
                }

                Temp.FormulaEngine.InvalidateFormulaCache();
                StingLog.Info("DocumentOpened: cleared formula, param, auto-tagger, compliance caches; reloaded TagConfig");

                // FUT-19: Pre-warm ONLY non-Revit-API caches (file I/O) on background thread.
                // PERF-CRIT: Revit API is NOT thread-safe — ComplianceScan.Scan() and
                // LoadGridLines() use FilteredElementCollector which MUST run on the UI thread.
                // Previously this caused native Revit instability and slowdowns.
                try
                {
                    System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            // Pre-load formulas from CSV — pure file I/O, safe on background thread
                            TagPipelineHelper.LoadFormulas();
                            StingLog.Info("FUT-19: Background pre-warming completed (formulas only — Revit API deferred)");
                        }
                        catch (Exception prEx) { StingLog.Warn($"FUT-19 pre-warm: {prEx.Message}"); }
                    });
                }
                catch (Exception pwEx) { StingLog.Warn($"FUT-19 pre-warm launch: {pwEx.Message}"); }

                // GAP 1-C (INT-01) — Lazy-start SyncScheduler on document open.
                // OnStartup only succeeds if credentials were in memory before any
                // document was loaded. When the user connects AFTER plugin start and
                // THEN opens the project document, OnStartup's check already missed.
                // This hook retries: if the client is authenticated, the project is
                // linked (planscape_connection.json present with a projectId), and
                // the scheduler is still idle, start it now.
                try
                {
                    if (Planscape.PluginSync.SyncScheduler.Instance == null)
                    {
                        var pClient = PlanscapeServerClient.Instance;
                        bool connected = pClient != null && pClient.IsConnected
                            && !string.IsNullOrEmpty(pClient.ServerUrl)
                            && !string.IsNullOrEmpty(pClient.AuthToken);
                        if (connected)
                        {
                            string docPath = e.Document?.PathName;
                            string projectDir = !string.IsNullOrEmpty(docPath)
                                ? System.IO.Path.GetDirectoryName(docPath)
                                : null;
                            string cfgPath = null;
                            if (!string.IsNullOrEmpty(projectDir))
                            {
                                // Preferred: _bim_manager/planscape_connection.json
                                string bimDir = System.IO.Path.Combine(projectDir, "_bim_manager");
                                cfgPath = System.IO.Path.Combine(bimDir, "planscape_connection.json");
                                if (!System.IO.File.Exists(cfgPath)) cfgPath = null;
                            }
                            bool hasProjectLink = false;
                            if (cfgPath != null)
                            {
                                try
                                {
                                    var jc = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(cfgPath));
                                    string pid = jc["projectId"]?.Value<string>();
                                    hasProjectLink = !string.IsNullOrWhiteSpace(pid)
                                        && Guid.TryParse(pid, out var _g) && _g != Guid.Empty;
                                }
                                catch (Exception cfgReadEx) { StingLog.Warn($"GAP 1-C cfg read: {cfgReadEx.Message}"); }
                            }
                            if (hasProjectLink)
                            {
                                Planscape.PluginSync.SyncScheduler.Start(pClient.ServerUrl, pClient.AuthToken);
                                StingTools.BIMManager.PluginSyncTickBridge.EnsureWired();
                                StingLog.Info($"GAP 1-C: SyncScheduler lazy-started on DocumentOpened against {pClient.ServerUrl}");
                                try
                                {
                                    if (Planscape.PluginSync.SyncScheduler.Instance != null)
                                    {
                                        Planscape.PluginSync.SyncScheduler.Instance.OnSyncComplete += _ =>
                                        {
                                            StingTools.UI.StingDockPanel.LastInstance?.RefreshSyncIndicator();
                                        };
                                    }
                                }
                                catch (Exception icEx) { StingLog.Warn($"GAP 1-C OnSyncComplete wire: {icEx.Message}"); }
                            }
                        }
                    }
                }
                catch (Exception lzEx) { StingLog.Warn($"GAP 1-C SyncScheduler lazy-start: {lzEx.Message}"); }

                // FIX-B10: Restore auto-tagger state from persisted config
                try
                {
                    if (TagConfig.AutoTaggerEnabled.HasValue)
                    {
                        bool want = TagConfig.AutoTaggerEnabled.Value;
                        if (want != StingAutoTagger.IsEnabled) StingAutoTagger.Toggle();
                    }
                    if (TagConfig.AutoTaggerVisual.HasValue)
                        StingAutoTagger.SetVisualTagging(TagConfig.AutoTaggerVisual.Value);
                    if (TagConfig.AutoTaggerStaleMarker.HasValue)
                        StingStaleMarker.SetEnabled(TagConfig.AutoTaggerStaleMarker.Value);
                    // GAP-AT-03: Restore discipline filter from project config
                    StingAutoTagger.RestoreDisciplineFilter();
                }
                catch (Exception atEx)
                {
                    StingLog.Warn($"AutoTagger state restore: {atEx.Message}");
                }

                // TAG-DEFERRED-OVERFLOW-01: Restore previously-dropped auto-tag elements
                // from sidecar so a session that overflowed the deferred queue does not
                // permanently lose those elements. The sidecar is rotated to .consumed
                // after a successful load so we never replay it twice.
                try
                {
                    int restored = StingAutoTagger.LoadDroppedElementsSidecar(e.Document);
                    if (restored > 0)
                        StingLog.Info($"DocumentOpened: re-queued {restored} previously-dropped auto-tag elements; will drain on next sync-to-central.");
                }
                catch (Exception drEx) { StingLog.Warn($"DocumentOpened deferred sidecar load: {drEx.Message}"); }

                // AL-07: Notify user of auto-run workflow on open
                try
                {
                    string autoWorkflow = TagConfig.AutoRunWorkflowOnOpen;
                    if (!string.IsNullOrEmpty(autoWorkflow))
                    {
                        StingLog.Info($"OnDocumentOpened: AUTO_RUN_WORKFLOW_ON_OPEN configured: '{autoWorkflow}'. " +
                            "Use 'Workflow Preset' command to execute manually.");
                    }
                }
                catch (Exception arwEx)
                {
                    StingLog.Warn($"AUTO_RUN_WORKFLOW_ON_OPEN check failed: {arwEx.Message}");
                }

                // Phase 77: Consume any pending workflow presets from WorkflowScheduler triggers
                // (document-open, compliance-fall, SLA-violation, warning-threshold triggers)
                try
                {
                    WorkflowScheduler.CheckDocumentOpenTriggers(e.Document);
                    while (WorkflowScheduler.HasPendingPresets)
                    {
                        string presetName = WorkflowScheduler.DequeuePendingPreset();
                        if (!string.IsNullOrEmpty(presetName))
                        {
                            StingLog.Info($"WorkflowScheduler: executing queued preset '{presetName}'");
                            UI.StingCommandHandler.SetExtraParam("WorkflowPresetName", presetName);
                            // Actual execution happens via ExternalEvent in StingCommandHandler
                            break; // Execute one at a time; remaining will execute on next idle
                        }
                    }
                }
                catch (Exception wsEx)
                {
                    StingLog.Warn($"WorkflowScheduler document-open execution: {wsEx.Message}");
                }

                // PERF-CRIT: Morning briefing DEFERRED to Idling event.
                // Previously ran ComplianceScan.Scan() (iterates ALL elements), GetWarnings(),
                // CheckSLAViolations (disk I/O), ComplianceTrendTracker (disk I/O), and showed
                // a BLOCKING TaskDialog — all inside the DocumentOpened event handler.
                // This blocked the Revit UI thread for 5-30+ seconds on large models, making
                // even native Revit buttons unresponsive.
                //
                // Now deferred to Revit's Idling event which fires after the UI is fully ready.
                // The briefing runs ONCE on first idle after document open, then unsubscribes.
                try
                {
                    _pendingBriefingDoc = e.Document;
                    // Idling event fires repeatedly when Revit is idle — we use a flag to run once
                    if (!_briefingSubscribed)
                    {
                        _briefingSubscribed = true;
                        // We can't subscribe to Idling from ControlledApplication — use static flag
                        // and check in StingCommandHandler's first Execute() call instead
                        _briefingPending = true;
                        StingLog.Info("Morning briefing deferred to first idle/command");
                    }
                }
                catch (Exception mbEx)
                {
                    StingLog.Warn($"Morning briefing defer: {mbEx.Message}");
                }

                // Template engine v1.1 (S11/S15): extract default templates,
                // workflows, and manifest on first open per project.
                try
                {
                    Planscape.Docs.Templates.EmbeddedTemplates.ExtractIfMissing(e.Document);
                }
                catch (Exception tEx)
                {
                    StingLog.Warn($"DocumentOpened template extraction: {tEx.Message}");
                }

                // Pack 0 — project-scoped offline config override. File is at
                // <project>/_BIM_COORD/sting_config.json. Missing file keeps defaults.
                try
                {
                    string bimDir = BIMManager.BIMManagerEngine.GetBIMManagerDir(e.Document);
                    StingOfflineConfig.LoadFromProject(bimDir);
                    UI.StingDockPanel.UpdateOfflineStatus(StingOfflineConfig.IsOffline, StingOfflineConfig.Source);
                }
                catch (Exception ocEx)
                {
                    StingLog.Warn($"DocumentOpened offline-config reload: {ocEx.Message}");
                }

                // Phase 165 (NEW-02 / Clash wiring) — lazy-start the ClashScheduler
                // when a project is opened. Pulls the cadence from the project's
                // default_clash_matrix.json (SchedulerIntervalMinutes) and falls
                // back to 60 minutes. Idempotent: re-opens hit Instance.Stop()
                // first so we don't stack timers across documents.
                try
                {
                    if (TagConfig.AutoStartClashScheduler && e.Document != null && !e.Document.IsFamilyDocument)
                    {
                        UIApplication uiAppForClash = UI.StingCommandHandler.CurrentApp;
                        if (uiAppForClash == null)
                        {
                            var revitApp = e.Document.Application;
                            if (revitApp != null) uiAppForClash = new UIApplication(revitApp);
                        }
                        if (uiAppForClash != null)
                        {
                            try { Clash.ClashScheduler.Instance.Stop(); } catch { /* first-run no-op */ }
                            Clash.ClashScheduler.Instance.Start(uiAppForClash, intervalMinutes: 0);
                            StingLog.Info("ClashScheduler started for active document");
                        }
                    }
                }
                catch (Exception csEx) { StingLog.Warn($"DocumentOpened ClashScheduler start: {csEx.Message}"); }

                // Phase 167 + folder consolidation: auto-bootstrap a default
                // ProjectSetup so every subsystem writes into ONE project root,
                // then silently sweep any legacy sibling folders (_BIM_COORD,
                // _bim_manager, STING_BIM_MANAGER, STING_Exports, STING_Project,
                // .bimmanager, *_Briefcase_*, STING_BOQ_RateHeatMap,
                // STING_WORKFLOW_LOG.json) into the unified container.
                try
                {
                    if (e.Document != null && !e.Document.IsFamilyDocument && !string.IsNullOrEmpty(e.Document.PathName))
                    {
                        var setup = ProjectFolderEngine.LoadOrBootstrapSetup(e.Document);
                        if (setup != null)
                        {
                            string root = setup.ResolveRootPath(e.Document.PathName);
                            StingLog.Info($"DocumentOpened: project setup ready — root={root}, mode={setup.Mode}");

                            // Idempotent silent migration. Only reports if it
                            // actually moved something — no UI prompt, never
                            // blocks the open path.
                            try
                            {
                                var rep = ProjectFolderEngine.MigrateFromLegacy(e.Document);
                                if (rep != null && (rep.FilesMoved > 0 || rep.FoldersRemoved > 0))
                                {
                                    StingLog.Info($"DocumentOpened auto-migration: {rep.FilesMoved} files moved, {rep.FoldersRemoved} legacy folders removed.");
                                }
                            }
                            catch (Exception mEx) { StingLog.Warn($"DocumentOpened auto-migration: {mEx.Message}"); }
                        }
                    }
                }
                catch (Exception cfEx) { StingLog.Warn($"DocumentOpened CDE folder bootstrap: {cfEx.Message}"); }

                // Pack 8 — drip-feed a compliance refresh through the Idling
                // scheduler so the dashboard is live within a second of open.
                try { StingIdlingScheduler.Enqueue(new ComplianceRefreshJob()); }
                catch (Exception schEx) { StingLog.Warn($"DocumentOpened Idling enqueue: {schEx.Message}"); }

                // TAG-STALE-WARN-01: After the compliance refresh populates the cache,
                // promote any pre-existing stale elements that exceed the threshold into
                // a BIM issue so coordinators see the work outstanding from a previous
                // session immediately on open. The job is single-shot and dedupes against
                // any existing OPEN stale issue, so re-opening a model is a no-op.
                try
                {
                    if (TagConfig.StaleWarningThreshold > 0)
                        StingIdlingScheduler.Enqueue(new StaleWarningPromotionJob());
                }
                catch (Exception swEx) { StingLog.Warn($"DocumentOpened stale-warning enqueue: {swEx.Message}"); }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DocumentOpened cleanup: {ex.Message}");
            }
        }

        // PERF-CRIT: Deferred morning briefing state — runs on first command after document open
        private static Document _pendingBriefingDoc;
        internal static volatile bool _briefingPending;
        private static bool _briefingSubscribed;

        /// <summary>PERF-CRIT: Run the morning briefing on demand (called from StingCommandHandler
        /// on first command execution after document open, NOT from DocumentOpened event).
        /// This prevents blocking the Revit UI thread during document load.</summary>
        internal static void RunDeferredMorningBriefing(Document doc)
        {
            if (!_briefingPending) return;
            _briefingPending = false;

            if (doc == null || !doc.IsValidObject) return;

            try
            {
                var briefing = new System.Text.StringBuilder();
                bool hasAlerts = false;

                // 1. Compliance scan (now runs on demand, not during document open)
                var comp = ComplianceScan.Scan(doc);
                if (comp != null)
                {
                    try { ComplianceTrendTracker.RecordSnapshot(doc, comp); }
                    catch (Exception ex) { StingLog.Warn($"Trend snapshot: {ex.Message}"); }

                    string trendDir = "unknown"; double trendDelta = 0;
                    try { (trendDir, trendDelta) = ComplianceTrendTracker.GetTrend(doc); }
                    catch (Exception ex) { StingLog.Warn($"Trend read: {ex.Message}"); }

                    briefing.AppendLine($"Tag Compliance: {comp.CompliancePercent:F0}% ({comp.RAGStatus})" +
                        (trendDir != "unknown" && trendDir != "insufficient data" ?
                        $"  [{trendDir} {trendDelta:+0.0;-0.0}% over 7 days]" : ""));
                    briefing.AppendLine($"  Tagged: {comp.TaggedComplete}/{comp.TotalElements}  |  " +
                        $"Untagged: {comp.Untagged}  |  Stale: {comp.StaleCount}");
                    if (comp.PlaceholderCount > 0)
                        briefing.AppendLine($"  Placeholders (GEN/XX/ZZ): {comp.PlaceholderCount}");
                    if (comp.StaleCount > 0) hasAlerts = true;
                    if (comp.CompliancePercent < 60) hasAlerts = true;
                    if (trendDir == "declining") hasAlerts = true;
                }

                // 2. Warnings — lightweight count only (no full classification)
                try
                {
                    int warnCount = doc.GetWarnings()?.Count ?? 0;
                    briefing.AppendLine($"\nModel Warnings: {warnCount}");
                    if (warnCount > 100) { briefing.AppendLine("  (HIGH warning count — run Warnings Auto-Fix)"); hasAlerts = true; }
                }
                catch (Exception wEx) { StingLog.Warn($"Morning briefing warnings: {wEx.Message}"); }

                // 3. SLA violations (disk I/O — acceptable here since we're not in event handler)
                try
                {
                    var overdue = BIMManager.BIMManagerEngine.CheckSLAViolations(doc);
                    if (overdue.Count > 0)
                    {
                        hasAlerts = true;
                        int critCount = overdue.Count(o => o.priority == "CRITICAL");
                        int highCount = overdue.Count(o => o.priority == "HIGH");
                        briefing.AppendLine($"\nOverdue Issues: {overdue.Count}");
                        if (critCount > 0) briefing.AppendLine($"  CRITICAL: {critCount} (SLA: 4 hrs)");
                        if (highCount > 0) briefing.AppendLine($"  HIGH: {highCount} (SLA: 24 hrs)");
                        briefing.AppendLine($"  Most overdue: {overdue[0].issueId} ({overdue[0].hoursOverdue:F0}h)");
                    }
                }
                catch (Exception slaEx) { StingLog.Warn($"Morning briefing SLA: {slaEx.Message}"); }

                // Healthcare Pack HC-07 — surface healthcare facility-type and run
                // a quick gated healthcare validator sweep. Hits the cache built
                // by RunAllHealthcareValidators so the cost is bounded.
                bool isHealthcareProject = false;
                int healthcareErrors = 0, healthcareWarnings = 0;
                try
                {
                    var p = doc.ProjectInformation?.LookupParameter("PRJ_ORG_HEALTH_FACILITY_TYPE_TXT");
                    string facType = "";
                    if (p != null && p.HasValue && p.StorageType == StorageType.String)
                        facType = (p.AsString() ?? "").Trim();
                    if (!string.IsNullOrEmpty(facType))
                    {
                        isHealthcareProject = true;
                        var hcResults = Core.Validation.Healthcare.RunAllHealthcareValidators.Validate(doc);
                        healthcareErrors   = hcResults.Count(r => r.Severity == Core.Validation.ValidationSeverity.Error);
                        healthcareWarnings = hcResults.Count(r => r.Severity == Core.Validation.ValidationSeverity.Warning);
                        briefing.AppendLine($"Healthcare ({facType}): {hcResults.Count} findings — {healthcareErrors} errors, {healthcareWarnings} warnings");
                        if (healthcareErrors > 0) hasAlerts = true;
                    }
                }
                catch (Exception hcEx) { StingLog.Warn($"Morning briefing healthcare: {hcEx.Message}"); }

                // 4. Show briefing only if alerts — now safe to show TaskDialog (not in event handler)
                if (hasAlerts)
                {
                    briefing.AppendLine("\n────────────────────────────────────");
                    briefing.AppendLine("Open BIM Coordination Center for full details.");
                    var dlg = new TaskDialog("STING Morning Briefing");
                    dlg.MainInstruction = "Model Status Summary";
                    dlg.MainContent = briefing.ToString();
                    dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        "Run Morning Health Check workflow", "Auto-fix stale elements, warnings, and validate tags");
                    if (isHealthcareProject)
                        dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                            "Run Healthcare Commissioning workflow", "Healthcare validators + Room Data Sheets + COBie export");
                    dlg.CommonButtons = TaskDialogCommonButtons.Close;
                    var dlgResult = dlg.Show();

                    if (dlgResult == TaskDialogResult.CommandLink1)
                    {
                        UI.StingCommandHandler.SetExtraParam("WorkflowPresetName", "MorningHealthCheck");
                        StingLog.Info("Morning briefing: user requested MorningHealthCheck workflow");
                    }
                    else if (dlgResult == TaskDialogResult.CommandLink2 && isHealthcareProject)
                    {
                        UI.StingCommandHandler.SetExtraParam("WorkflowPresetName", "HealthcareCommissioning");
                        StingLog.Info("Morning briefing: user requested HealthcareCommissioning workflow");
                    }
                }
                else
                {
                    StingLog.Info($"Morning briefing: model healthy — {comp?.CompliancePercent:F0}% compliance, no alerts");
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Deferred morning briefing: {ex.Message}");
            }
        }

        /// <summary>
        /// FIX-06: Invalidate auto-tagger cache when switching between documents.
        /// ViewActivated fires when user switches active view or document — clear cached
        /// context so the auto-tagger picks up the correct document's data.
        /// </summary>
        private static Document _lastActiveDoc;
        private static void OnViewActivated(object sender,
            Autodesk.Revit.UI.Events.ViewActivatedEventArgs e)
        {
            try
            {
                Autodesk.Revit.DB.View view = e.CurrentActiveView;
                Document currentDoc = view?.Document;
                if (currentDoc != null && currentDoc != _lastActiveDoc)
                {
                    _lastActiveDoc = currentDoc;
                    StingAutoTagger.InvalidateContext();
                    ComplianceScan.InvalidateCache();
                    // GAP-05: Clear parameter lookup cache on document switch to prevent
                    // stale Definition objects from a different document being reused
                    ParameterHelpers.ClearParamCache();
                    StingLog.Info("ViewActivated: document switch detected — caches invalidated");
                }

                if (view != null) UpdateScaleTabInfo(view);
                if (view != null && currentDoc != null) MaybeAutoApplyScaleSize(currentDoc, view);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ViewActivated handler: {ex.Message}");
            }
        }

        /// <summary>
        /// Push the active view's scale, resolved tier, and offset to the
        /// three Scale-tab info labels. Safe to call from any thread — marshals
        /// onto the Dispatcher and tolerates a closed / detached panel.
        /// </summary>
        private static void UpdateScaleTabInfo(Autodesk.Revit.DB.View view)
        {
            try
            {
                var panel = UI.StingDockPanel.LastInstance;
                if (panel == null || !panel.IsLoaded) return;
                ScaleTiers.Tier tier = ScaleTiers.ForView(view);
                int scale = view.Scale > 0 ? view.Scale : 100;
                double offsetFt = (tier.OffsetMm / 304.8) * scale;
                double cappedFt = System.Math.Min(offsetFt, ScaleTiers.OffsetCapFt);

                string scaleTxt  = $"Scale: 1:{scale}";
                string tierTxt   = $"Tier: {tier.Label}  (size {tier.TextSizeMm}mm)";
                string offsetTxt = $"Offset: {tier.OffsetMm:F1} mm ({cappedFt:F2} ft)";

                panel.Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    try { panel.UpdateScaleInfoLabels(scaleTxt, tierTxt, offsetTxt); }
                    catch (Exception ex) { StingLog.Warn($"UpdateScaleTabInfo dispatch: {ex.Message}"); }
                }));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"UpdateScaleTabInfo: {ex.Message}");
            }
        }

        /// <summary>
        /// When <c>TAG_SCALE_TIER_AUTO_BOOL</c> is set on Project Information,
        /// auto-apply <see cref="Tags.SetScaleAwareTagSizeCommand"/> to the
        /// activated view. Defaults to Instance mode so the switch is
        /// side-effect free across views. Suppresses the task dialog.
        /// </summary>
        private static void MaybeAutoApplyScaleSize(Document doc, Autodesk.Revit.DB.View view)
        {
            try
            {
                Element projInfo = doc.ProjectInformation;
                if (projInfo == null) return;
                Parameter flag = projInfo.LookupParameter(ParamRegistry.TAG_SCALE_TIER_AUTO);
                if (flag == null || flag.StorageType != StorageType.Integer) return;
                if (flag.AsInteger() == 0) return;

                ScaleTiers.Tier tier = ScaleTiers.ForView(view);
                if (!ParamRegistry.TagStyleSizes.Contains(tier.TextSizeMm)) return;

                var result = Tags.SetScaleAwareTagSizeCommand.ApplyToView(
                    doc, view, tier.TextSizeMm, "Auto");
                int total = result.InstanceSwitches + result.TypeMatrixFlips;
                if (total > 0)
                    StingLog.Info($"AutoScaleTagSize on view activation: view='{view.Name}' " +
                                  $"changed={total} (instances={result.InstanceSwitches}, " +
                                  $"typeFlips={result.TypeMatrixFlips})");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MaybeAutoApplyScaleSize: {ex.Message}");
            }
        }

        // ── Auto-Sync on DocumentSaved ─────────────────────────────
        private static volatile bool _isSyncing;

        /// <summary>
        /// AUTO-SYNC: Collect a lightweight compliance summary when the user saves
        /// and hand it to the Planscape OfflineQueue — the SyncScheduler drains the
        /// queue on its own timer. HTTP calls must NEVER happen inside Revit event
        /// handlers (S03c/d: replaced the old _pendingSyncDoc / _pendingSyncTime
        /// dead-code fields with a proper enqueue).
        /// </summary>
        private static void OnDocumentSaved(object sender,
            Autodesk.Revit.DB.Events.DocumentSavedEventArgs e)
        {
            if (_isSyncing) return;
            _isSyncing = true;
            try
            {
                var doc = e.Document;
                if (doc == null || doc.IsFamilyDocument) return;

                StingLog.Info($"DocumentSaved: {doc.Title} — queuing server sync");

                // Collect lightweight compliance summary (cached scan — fast path)
                int totalElements = 0;
                int taggedCount = 0;
                int staleCount = 0;
                int placeholderCount = 0;
                int warningCount = 0;
                double tagPct = 0;
                double strictPct = 0;
                double containerPct = 0;
                string ragStatus = "RED";
                try
                {
                    var comp = ComplianceScan.Scan(doc);
                    if (comp != null)
                    {
                        totalElements    = comp.TotalElements;
                        taggedCount      = comp.TaggedComplete;
                        staleCount       = comp.StaleCount;
                        placeholderCount = comp.PlaceholderCount;
                        tagPct           = comp.CompliancePercent;
                        ragStatus        = comp.RAGStatus ?? "RED";
                    }
                }
                catch (Exception compEx)
                {
                    StingLog.Warn($"DocumentSaved compliance scan: {compEx.Message}");
                }

                // C3 — also populate TagElements so SyncNow has something to push
                // besides the compliance summary. Capped at 5000 elements to keep
                // the save → queue latency in the sub-second range.
                List<Planscape.Shared.Models.TagElementSync> tagElements = null;
                try { tagElements = CollectTagElements(doc, max: 5000); }
                catch (Exception tagEx) { StingLog.Warn($"DocumentSaved tag collect: {tagEx.Message}"); }

                // Build the sync payload and hand it to the offline queue.
                // If SyncScheduler hasn't been started yet (user isn't logged in),
                // OfflineQueue.Shared is null and we log+skip.
                try
                {
                    var client = PlanscapeServerClient.Instance;
                    var payload = new Planscape.Shared.Models.PluginSyncPayload
                    {
                        ProjectId     = Guid.Empty, // server resolves via auth/tenant scope
                        UserName      = client?.ConnectedUser ?? Environment.UserName ?? "Unknown",
                        RevitVersion  = Assembly.GetAssembly(typeof(Autodesk.Revit.DB.Document))?
                                          .GetName().Version?.ToString() ?? "",
                        PluginVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "",
                        Timestamp     = DateTime.UtcNow,
                        TagElements   = tagElements,
                        Compliance    = new Planscape.Shared.Models.ComplianceSync
                        {
                            TotalElements     = totalElements,
                            TaggedComplete    = taggedCount,
                            StaleCount        = staleCount,
                            PlaceholderCount  = placeholderCount,
                            WarningCount      = warningCount,
                            TagPercent        = tagPct,
                            StrictPercent     = strictPct,
                            ContainerPercent  = containerPct,
                            RagStatus         = ragStatus
                        }
                    };

                    var queue = OfflineQueue.Shared;
                    if (queue != null)
                    {
                        queue.Enqueue(payload);
                        StingLog.Info($"DocumentSaved: {doc.Title} — compliance {tagPct:F1}% " +
                            $"({taggedCount}/{totalElements}) + {tagElements?.Count ?? 0} tag elements enqueued " +
                            $"(queue depth: {queue.Count})");

                        // C3 — drain immediately instead of waiting for the 5-min timer.
                        // Fire-and-forget; the scheduler handles retry on failure.
                        if (SyncScheduler.Instance != null)
                        {
                            _ = Task.Run(async () =>
                            {
                                try { await SyncScheduler.Instance.SyncNowAsync(); }
                                catch (Exception dEx) { StingLog.Warn($"DocumentSaved immediate drain: {dEx.Message}"); }
                            });
                        }
                    }
                    else
                    {
                        StingLog.Info($"DocumentSaved: {doc.Title} — SyncScheduler not running, sync skipped");
                    }
                }
                catch (Exception qEx)
                {
                    StingLog.Warn($"DocumentSaved enqueue: {qEx.Message}");
                }
            }
            catch (Exception ex)
            {
                StingLog.Error($"DocumentSaved handler error: {ex.Message}");
            }
            finally
            {
                _isSyncing = false;
            }
        }

        /// <summary>
        /// C3 — Collect lightweight tag element records for the sync payload.
        /// Includes only elements with ASS_TAG_1_TXT populated (tagged elements)
        /// and caps at <paramref name="max"/> to keep the save path fast.
        /// </summary>
        private static List<Planscape.Shared.Models.TagElementSync> CollectTagElements(
            Autodesk.Revit.DB.Document doc, int max = 5000)
        {
            var results = new List<Planscape.Shared.Models.TagElementSync>();
            if (doc == null) return results;

            var collector = new Autodesk.Revit.DB.FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WhereElementIsViewIndependent();

            foreach (var el in collector)
            {
                if (results.Count >= max) break;

                string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                if (string.IsNullOrEmpty(tag1)) continue;

                string FromReg(string p) { try { return ParameterHelpers.GetString(el, p); } catch { return ""; } }

                results.Add(new Planscape.Shared.Models.TagElementSync
                {
                    RevitElementId = el.Id.Value,
                    UniqueId       = el.UniqueId,
                    Disc           = FromReg(ParamRegistry.DISC),
                    Loc            = FromReg(ParamRegistry.LOC),
                    Zone           = FromReg(ParamRegistry.ZONE),
                    Lvl            = FromReg(ParamRegistry.LVL),
                    Sys            = FromReg(ParamRegistry.SYS),
                    Func           = FromReg(ParamRegistry.FUNC),
                    Prod           = FromReg(ParamRegistry.PROD),
                    Seq            = FromReg(ParamRegistry.SEQ),
                    Tag1           = tag1,
                    CategoryName   = el.Category?.Name ?? "",
                    FamilyName     = ParameterHelpers.GetFamilyName(el) ?? "",
                });
            }
            return results;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            StingLog.Info("STING Tools shutting down");

            // Unhook DocumentSaved sync handler
            try { application.ControlledApplication.DocumentSaved -= OnDocumentSaved; }
            catch (Exception ex) { StingLog.Warn($"DocumentSaved unhook: {ex.Message}"); }

            // S03 / Phase 91 — Tear down the sync scheduler and its background timer.
            // Acceptance criterion 2: call SyncScheduler.Stop() if SyncScheduler.Instance != null.
            // StopShared() is the correct static facade (there's no static Stop()); the explicit
            // guard is belt-and-braces since StopShared() is already null-safe internally.
            try
            {
                if (SyncScheduler.Instance != null)
                {
                    SyncScheduler.StopShared();
                    StingLog.Info("SyncScheduler stopped (Phase 91)");
                }
            }
            catch (Exception ex) { StingLog.Warn($"SyncScheduler stop: {ex.Message}"); }

            StingPluginHooks.ClearAll();
            StingAutoTagger.Unregister();
            StingTag7NarrativeUpdater.Unregister();
            try { StingTools.Core.Routing.CableManifestUpdater.Unregister(); } catch { }

            // Phase 175 — unregister the SLD sync updater.
            try
            {
                if (_sldUpdaterId != null)
                    Autodesk.Revit.DB.UpdaterRegistry.UnregisterUpdater(_sldUpdaterId);
            }
            catch (Exception ex) { StingLog.Warn($"SLDSyncUpdater unregister: {ex.Message}"); }

            // Clash rec-2: Unregister the live clash IUpdater. Safe against re-entry
            // and no-op if never registered.
            try
            {
                Autodesk.Revit.DB.UpdaterRegistry.UnregisterUpdater(
                    StingTools.Core.Clash.LiveClashUpdater.UpdaterGuid);
                StingLog.Info("LiveClashUpdater unregistered");
            }
            catch (Exception ex) { StingLog.Warn($"LiveClashUpdater unregister: {ex.Message}"); }

            UI.ThemeManager.ClearTarget(); // H-02: Prevent memory leak from static WPF reference
            try { Planscape.Docs.Workflow.AuditLog.Shutdown(); }
            catch (Exception ex) { StingLog.Warn($"AuditLog shutdown: {ex.Message}"); }
            StingLog.Shutdown();
            return Result.Succeeded;
        }

        // ── Assembly Pre-Flight Check ────────────────────────────────

        /// <summary>
        /// Logs the assembly environment at startup for crash diagnostics.
        /// Detects known conflict patterns (version mismatches for key assemblies)
        /// that have been observed to cause native Revit crashes.
        /// </summary>
        private static void LogAssemblyEnvironment()
        {
            try
            {
                var stingAsm = Assembly.GetExecutingAssembly().GetName();
                StingLog.Info($"STING Tools v{stingAsm.Version} loaded from {AssemblyPath}");

                // Check for assemblies we depend on that may conflict with other addins
                var criticalAssemblies = new[] {
                    "Newtonsoft.Json", "ClosedXML", "DocumentFormat.OpenXml",
                    "System.IO.Packaging", "WindowsBase", "RevitAPI", "RevitAPIUI"
                };

                var loaded = AppDomain.CurrentDomain.GetAssemblies();
                var conflicts = new List<string>();

                // Group loaded assemblies by short name to detect version conflicts
                var grouped = loaded
                    .Where(a => !a.IsDynamic)
                    .GroupBy(a => a.GetName().Name, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1 || criticalAssemblies.Contains(g.Key));

                foreach (var g in grouped)
                {
                    var versions = g.Select(a => a.GetName().Version?.ToString() ?? "?").Distinct().ToList();
                    if (versions.Count > 1)
                    {
                        string msg = $"CONFLICT: {g.Key} loaded with {versions.Count} versions: " +
                            string.Join(", ", versions);
                        StingLog.Warn(msg);
                        conflicts.Add(msg);
                    }
                    else if (criticalAssemblies.Contains(g.Key))
                    {
                        StingLog.Info($"Assembly: {g.Key} v{versions[0]}");
                    }
                }

                if (conflicts.Count > 0)
                {
                    StingLog.Warn($"Assembly pre-flight: {conflicts.Count} version conflict(s) detected. " +
                        "These may cause intermittent crashes. Check log for details.");
                }
                else
                {
                    StingLog.Info("Assembly pre-flight: no version conflicts detected.");
                }
            }
            catch (Exception ex)
            {
                // Pre-flight check is diagnostic only — never block startup
                StingLog.Warn($"Assembly pre-flight check failed: {ex.Message}");
            }
        }

        // ── Data File Validation ─────────────────────────────────────

        /// <summary>
        /// Validates that the data directory and critical data files exist at startup.
        /// Logs warnings for missing files and shows a one-time dialog if the data
        /// directory is completely missing — this is the #1 cause of command crashes.
        /// </summary>
        private static void ValidateDataFiles()
        {
            if (!Directory.Exists(DataPath))
            {
                StingLog.Warn($"Data directory not found: {DataPath}");
                StingLog.Warn("Data-dependent commands will use fallback defaults. " +
                    "Deploy the data/ folder alongside StingTools.dll.");
                // Don't block startup with a dialog — log is sufficient.
                // Commands that need data files already show their own error dialogs.
                return;
            }

            // Critical files that many commands depend on
            string[] criticalFiles = new[]
            {
                "PARAMETER_REGISTRY.json",
                "MR_PARAMETERS.txt",
                "MR_PARAMETERS.csv",
                "BLE_MATERIALS.csv",
                "MEP_MATERIALS.csv",
                "MR_SCHEDULES.csv",
                "FORMULAS_WITH_DEPENDENCIES.csv",
                "CATEGORY_BINDINGS.csv",
                "LABEL_DEFINITIONS.json",
            };

            var missing = new List<string>();
            foreach (string file in criticalFiles)
            {
                string path = FindDataFile(file);
                if (path == null)
                    missing.Add(file);
            }

            if (missing.Count > 0)
            {
                string list = string.Join(", ", missing);
                StingLog.Warn($"Missing {missing.Count} critical data files: {list}");
                StingLog.Warn($"Data path: {DataPath}");
                StingLog.Warn("Commands that depend on these files will show individual error messages.");
            }
            else
            {
                StingLog.Info($"Data validation passed: all {criticalFiles.Length} critical files found in {DataPath}");
            }

            // IG-04: Verify pyRevit manifest
            string manifestPath = FindDataFile("PYREVIT_SCRIPT_MANIFEST.csv");
            if (manifestPath != null)
            {
                try
                {
                    var mLines = File.ReadAllLines(manifestPath).Skip(1).ToList();
                    int missingScripts = mLines.Count(l => {
                        var p = ParseCsvLine(l);
                        return p.Length >= 2 && !string.IsNullOrEmpty(p[1].Trim()) && !File.Exists(p[1].Trim());
                    });
                    if (missingScripts > 0)
                        StingLog.Warn($"PYREVIT_SCRIPT_MANIFEST: {missingScripts} script path(s) not found on disk.");
                }
                catch (Exception ex) { StingLog.Warn($"PyRevit manifest check: {ex.Message}"); }
            }

            // DATA-01: Validate schema version headers on TAG_CONFIG CSVs.
            // Routed through HandoverModeHelper so the active preset's CSVs
            // (Handover default, or DesignConstruction variant) are the ones
            // checked at startup.
            // doc is null at startup; helper falls back to PARAGRAPH_PRESETS.json active_preset.
            string[] versionedCsvs = HandoverModeHelper.GetAllTagConfigCsvs(null);
            foreach (string csv in versionedCsvs)
            {
                string path = FindDataFile(csv);
                if (path != null)
                    GetCsvSchemaVersion(path);
            }
        }

        // ── Dockable Panel Registration ──────────────────────────────

        private void RegisterDockablePanel(UIControlledApplication application)
        {
            try
            {
                // Initialise the external event handler for panel button dispatching
                StingDockPanel.Initialise(application);

                // Register the dockable pane with Revit
                var provider = new StingDockPanelProvider();
                application.RegisterDockablePane(
                    StingDockPanelProvider.PaneId,
                    "STING Tools",
                    provider);

                // Create a minimal ribbon tab with just a toggle button
                const string tabName = "STING Tools";
                application.CreateRibbonTab(tabName);
                string asmPath = AssemblyPath;

                // STING Hub — quick-launch panel (added FIRST so it sits at the
                // left end of the tab). 9 small stacked buttons with runtime-
                // drawn letter icons; no image files on disk.
                RibbonPanel hubPanel = application.CreateRibbonPanel(tabName, "STING Hub");
                BuildHubPanel(hubPanel);

                var togglePanel = application.CreateRibbonPanel(tabName, "Panel");
                AddButton(togglePanel, "btnTogglePanel", "STING\nPanel",
                    asmPath, typeof(ToggleDockPanelCommand).FullName,
                    "Show/hide the STING Tools dockable panel");

                StingLog.Info("Dockable panel registered successfully");
            }
            catch (Exception ex)
            {
                StingLog.Error("Failed to register dockable panel", ex);
            }
        }

        // ── Phase 177 — STING Electrical Center registration ────────────

        /// <summary>
        /// Register the STING Electrical Center as a second dockable panel
        /// (tabbed behind the Properties palette so it sits alongside the
        /// main panel) plus a ribbon button to toggle it.
        /// </summary>
        private void RegisterElectricalPanel(UIControlledApplication application)
        {
            try
            {
                var provider = new StingTools.UI.StingElectricalPanelProvider();
                application.RegisterDockablePane(
                    StingTools.UI.StingElectricalPanelProvider.PaneId,
                    "⚡ STING Electrical",
                    provider);

                const string tabName = "STING Tools";
                string asmPath = AssemblyPath;
                var elecPanel = application.CreateRibbonPanel(tabName, "⚡ Electrical");
                AddButton(elecPanel, "btnToggleElectrical", "STING\nElectrical",
                    asmPath, typeof(ToggleElectricalPanelCommand).FullName,
                    "Show/hide the STING Electrical Center dockable panel.");
                StingLog.Info("Electrical dockable panel registered successfully");
            }
            catch (Exception ex)
            {
                StingLog.Error("Failed to register Electrical dockable panel", ex);
            }
        }

        // ── Phase 178c — STING Plumbing Center registration ─────────────
        private void RegisterPlumbingPanel(UIControlledApplication application)
        {
            try
            {
                StingTools.UI.Plumbing.StingPlumbingCommandHandler.Initialise(application);
                var provider = new StingTools.UI.Plumbing.StingPlumbingPanelProvider();
                application.RegisterDockablePane(
                    StingTools.UI.Plumbing.StingPlumbingPanelProvider.PaneId,
                    "💧 STING Plumbing",
                    provider);

                const string tabName = "STING Tools";
                string asmPath = AssemblyPath;
                var plumbPanel = application.CreateRibbonPanel(tabName, "💧 Plumbing");
                AddButton(plumbPanel, "btnTogglePlumbing", "STING\nPlumbing",
                    asmPath, typeof(TogglePlumbingPanelCommand).FullName,
                    "Show/hide the STING Plumbing Center dockable panel.");
                StingLog.Info("Plumbing dockable panel registered successfully");
            }
            catch (Exception ex)
            {
                StingLog.Error("Failed to register Plumbing dockable panel", ex);
            }
        }

        /* Ribbon panels removed — all commands now accessible via the dockable panel.
           Original panels: Select (22 cmds), Docs (17 cmds), Tags (28 cmds),
           Organise (32 cmds), Temp (64 cmds). */

        // ── Data file utilities ───────────────────────────────────────

        /// <summary>Find a data file by name, searching DataPath, subdirectories,
        /// and common alternative locations relative to the DLL.</summary>
        public static string FindDataFile(string fileName)
        {
            if (string.IsNullOrEmpty(DataPath) && string.IsNullOrEmpty(AssemblyPath))
                return null;

            // 1. Primary: DataPath/fileName (e.g. .../CompiledPlugin/data/BLE_MATERIALS.csv)
            if (!string.IsNullOrEmpty(DataPath))
            {
                try
                {
                    string direct = Path.Combine(DataPath, fileName);
                    if (File.Exists(direct)) return direct;
                }
                catch (Exception ex) { StingLog.Warn($"Path.Combine or File.Exists can fail on invalid paths: {ex.Message}"); }
            }

            // 2. Search DataPath subdirectories (only if directory actually exists)
            if (!string.IsNullOrEmpty(DataPath))
            {
                try
                {
                    if (Directory.Exists(DataPath))
                    {
                        foreach (string f in Directory.GetFiles(
                            DataPath, fileName, SearchOption.AllDirectories))
                        {
                            return f;
                        }
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"FindDataFile '{fileName}': {ex.Message}");
                }
            }

            // 3. Fallback: search alternative paths relative to DLL location
            //    Handles deployment where data/ is at a sibling or parent level
            string dllDir = Path.GetDirectoryName(AssemblyPath) ?? "";
            string[] alternativePaths = new[]
            {
                Path.Combine(dllDir, "Data", fileName),          // Data/ (capital D, source layout)
                Path.Combine(dllDir, "..", "data", fileName),    // ../data/ (parent)
                Path.Combine(dllDir, "..", "Data", fileName),    // ../Data/ (parent, capital)
                Path.Combine(dllDir, "..", "StingTools", "Data", fileName), // sibling project
            };

            foreach (string alt in alternativePaths)
            {
                try
                {
                    string resolved = Path.GetFullPath(alt);
                    if (File.Exists(resolved))
                    {
                        StingLog.Info($"FindDataFile '{fileName}' found at fallback: {resolved}");
                        return resolved;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"path resolution failed, skip: {ex.Message}"); }
            }

            return null;
        }

        /// <summary>
        /// DATA-01: Parse the schema version from a CSV file's first line.
        /// Supports both `#SCHEMA_VERSION=X.Y` and `#!SCHEMA_VERSION=X.Y` formats.
        /// Returns null if not present or unreadable.
        /// </summary>
        public static string GetCsvSchemaVersion(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;
                using (var reader = new StreamReader(filePath))
                {
                    string firstLine = reader.ReadLine();
                    if (firstLine == null) return null;
                    if (firstLine.StartsWith("#!SCHEMA_VERSION="))
                        return firstLine.Substring("#!SCHEMA_VERSION=".Length).Trim();
                    if (firstLine.StartsWith("#SCHEMA_VERSION="))
                    {
                        string versionPart = firstLine.Substring("#SCHEMA_VERSION=".Length);
                        int comma = versionPart.IndexOf(',');
                        return (comma >= 0 ? versionPart.Substring(0, comma) : versionPart).Trim();
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Schema version read failed for {filePath}: {ex.Message}"); }
            return null;
        }

        /// <summary>Parse a CSV line respecting quoted fields.</summary>
        public static string[] ParseCsvLine(string line)
        {
            var result = new System.Collections.Generic.List<string>();
            bool inQuote = false;
            var current = new System.Text.StringBuilder();

            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuote = !inQuote;
                }
                else if (c == ',' && !inQuote)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString());
            return result.ToArray();
        }

        // ── Ribbon helpers ───────────────────────────────────────────────

        private static void AddButton(RibbonPanel panel, string name,
            string text, string asmPath, string className,
            string tooltip)
        {
            var data = new PushButtonData(name, text, asmPath, className);
            data.ToolTip = tooltip;
            panel.AddItem(data);
        }

        private static void AddPulldownItem(PulldownButton pulldown,
            string name, string text, string asmPath, string className,
            string tooltip)
        {
            var data = new PushButtonData(name, text, asmPath, className);
            data.ToolTip = tooltip;
            pulldown.AddPushButton(data);
        }

        // ── STING Hub ribbon panel ──────────────────────────────────────────

        /// <summary>
        /// Render a square letter-tile icon at runtime: rounded rectangle in
        /// <paramref name="bgColor"/> with white bold Arial letters centred
        /// on top. Returned as a frozen <see cref="BitmapImage"/> suitable
        /// for <c>PushButtonData.Image</c> / <c>LargeImage</c>.
        /// </summary>
        private static BitmapImage MakeLetterIcon(string letters, DrawingColor bgColor, int size = 32)
        {
            try
            {
                using (var bmp = new Bitmap(size, size))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = TextRenderingHint.AntiAlias;
                        g.Clear(DrawingColor.Transparent);

                        int radius = Math.Max(2, size / 5);
                        int d = radius * 2;
                        using (var path = new GraphicsPath())
                        {
                            path.AddArc(0, 0, d, d, 180, 90);
                            path.AddArc(size - d - 1, 0, d, d, 270, 90);
                            path.AddArc(size - d - 1, size - d - 1, d, d, 0, 90);
                            path.AddArc(0, size - d - 1, d, d, 90, 90);
                            path.CloseFigure();
                            using (var brush = new SolidBrush(bgColor))
                                g.FillPath(brush, path);
                        }

                        // Scale font to letter count so 2-character labels fill
                        // the tile without clipping.
                        float fontSize = letters != null && letters.Length >= 2
                            ? size * 0.42f
                            : size * 0.55f;
                        using (var font = new Font("Arial", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var sf = new StringFormat
                        {
                            Alignment = StringAlignment.Center,
                            LineAlignment = StringAlignment.Center
                        })
                        using (var textBrush = new SolidBrush(DrawingColor.White))
                        {
                            g.DrawString(letters ?? string.Empty, font, textBrush,
                                new RectangleF(0, 0, size, size), sf);
                        }
                    }

                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        ms.Position = 0;
                        var img = new BitmapImage();
                        img.BeginInit();
                        img.CacheOption = BitmapCacheOption.OnLoad;
                        img.StreamSource = ms;
                        img.EndInit();
                        img.Freeze();
                        return img;
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Info($"MakeLetterIcon('{letters}') failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Build the STING Hub ribbon panel: 9 quick-launch small buttons laid
        /// out as three vertical stacks of three. Each button's icon is drawn
        /// at runtime via <see cref="MakeLetterIcon"/> — no image files on disk.
        ///
        /// Tag wiring (via the wrapper IExternalCommand classes below) routes
        /// through <see cref="StingDockPanel.DispatchCommand"/>, so each tag
        /// must be handled in <c>StingCommandHandler.Execute</c>.
        ///
        /// TODO — verify these tag strings are wired in StingCommandHandler.cs;
        /// the closest existing dispatchers use slightly different names
        /// (e.g. "BIMCoordinationCenter", "DocumentManager", "BOQCostManager",
        /// "Fabrication_OpenWorkspace", "Placement_OpenCentre",
        /// "DrawingTypes_Editor"). Add aliases or switch the hub tag strings
        /// when wiring up the handler:
        ///   BIMCoordCenter_Open, SheetManager_Open, DrawingTypes_Edit,
        ///   DocumentMgmt_Open, BOQ_ExportCost, Fabrication_Open,
        ///   Placement_Open, StructuralDWGWizard, Scheduling_Dashboard.
        /// </summary>
        private static void BuildHubPanel(RibbonPanel panel)
        {
            string asm = AssemblyPath;

            var specs = new (string tag, string label, string letters, DrawingColor color, string cls)[]
            {
                ("BIMCoordCenter_Open",  "Coord Center",  "CC", DrawingColor.SteelBlue,    typeof(HubBIMCoordCenterCommand).FullName),
                ("SheetManager_Open",    "Sheet Manager", "SM", DrawingColor.Teal,         typeof(HubSheetManagerCommand).FullName),
                ("DrawingTypes_Edit",    "Drawing Types", "DT", DrawingColor.MediumPurple, typeof(HubDrawingTypesCommand).FullName),
                ("DocumentMgmt_Open",    "Doc Manager",   "DM", DrawingColor.DarkOrange,   typeof(HubDocumentMgmtCommand).FullName),
                ("BOQ_ExportCost",       "BOQ / Cost",    "BQ", DrawingColor.SeaGreen,     typeof(HubBoqExportCostCommand).FullName),
                ("Fabrication_Open",     "Fabrication",   "FW", DrawingColor.Firebrick,    typeof(HubFabricationCommand).FullName),
                ("Placement_Open",       "Placement",     "PC", DrawingColor.Goldenrod,    typeof(HubPlacementCommand).FullName),
                ("StructuralDWGWizard",  "Struct Wizard", "SW", DrawingColor.SlateGray,    typeof(HubStructuralDwgWizardCommand).FullName),
                ("Scheduling_Dashboard", "Scheduling",    "SD", DrawingColor.MidnightBlue, typeof(HubSchedulingDashboardCommand).FullName),
                ("Tag3D",                "3D Tag",        "T3", DrawingColor.Crimson,      typeof(HubTag3DCommand).FullName),
                ("CreateTagFamilies",    "Tag Families",  "TF", DrawingColor.DarkCyan,     typeof(HubCreateTagFamiliesCommand).FullName),
                ("AutoTag",              "Auto Tag",      "AT", DrawingColor.DarkGreen,    typeof(HubAutoTagCommand).FullName),
            };

            var buttons = new List<PushButtonData>(12);
            foreach (var s in specs)
            {
                var data = new PushButtonData("Hub_" + s.tag, s.label, asm, s.cls)
                {
                    ToolTip = $"{s.label} — Right-click to pin to Quick Access Toolbar"
                };
                try
                {
                    data.Image      = MakeLetterIcon(s.letters, s.color, 16);
                    data.LargeImage = MakeLetterIcon(s.letters, s.color, 32);
                }
                catch (Exception ex)
                {
                    StingLog.Info($"Hub icon '{s.letters}' failed: {ex.Message}");
                }
                buttons.Add(data);
            }

            try
            {
                panel.AddStackedItems(buttons[0], buttons[1], buttons[2]);
                panel.AddStackedItems(buttons[3], buttons[4], buttons[5]);
                panel.AddStackedItems(buttons[6], buttons[7], buttons[8]);
                panel.AddStackedItems(buttons[9], buttons[10], buttons[11]);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"BuildHubPanel: AddStackedItems failed, falling back to AddItem: {ex.Message}");
                foreach (var b in buttons)
                {
                    try { panel.AddItem(b); }
                    catch (Exception innerEx) { StingLog.Warn($"BuildHubPanel AddItem '{b.Name}': {innerEx.Message}"); }
                }
            }
        }
    }

    /// <summary>
    /// Toggle the STING Tools dockable panel visibility.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ToggleDockPanelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                DockablePane pane = ParameterHelpers.GetApp(commandData)
                    .GetDockablePane(StingDockPanelProvider.PaneId);

                if (pane == null)
                {
                    TaskDialog.Show("STING Panel",
                        "Dockable panel not found. Restart Revit to register it.");
                    return Result.Failed;
                }

                if (pane.IsShown())
                    pane.Hide();
                else
                    pane.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Toggle dockable panel failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Phase 177 — toggle the STING Electrical Center dockable panel from the
    /// "⚡ Electrical" ribbon button. Mirrors <see cref="ToggleDockPanelCommand"/>
    /// but targets the electrical pane GUID.
    /// </summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.ReadOnly)]
    public class ToggleElectricalPanelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var pane = ParameterHelpers.GetApp(commandData)
                    .GetDockablePane(StingTools.UI.StingElectricalPanelProvider.PaneId);
                if (pane == null)
                {
                    TaskDialog.Show("STING Electrical",
                        "Electrical panel not found. Restart Revit to register it.");
                    return Result.Failed;
                }
                if (pane.IsShown()) pane.Hide(); else pane.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Toggle Electrical panel failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Phase 178c — toggle the STING Plumbing Center dockable panel.
    /// </summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.ReadOnly)]
    public class TogglePlumbingPanelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var pane = ParameterHelpers.GetApp(commandData)
                    .GetDockablePane(StingTools.UI.Plumbing.StingPlumbingPanelProvider.PaneId);
                if (pane == null)
                {
                    TaskDialog.Show("STING Plumbing",
                        "Plumbing panel not found. Restart Revit to register it.");
                    return Result.Failed;
                }
                if (pane.IsShown()) pane.Hide(); else pane.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Toggle Plumbing panel failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ── STING Hub button dispatchers ────────────────────────────────────────
    // Each ribbon button on the STING Hub panel is bound to one of these thin
    // wrappers. They delegate to StingDockPanel.DispatchCommand, which raises
    // the shared ExternalEvent so the request is processed by
    // StingCommandHandler.Execute on the Revit API thread — same dispatch
    // path the dockable panel buttons use.

    internal static class HubDispatcher
    {
        public static Result Run(string tag, ref string message)
        {
            try
            {
                StingTools.UI.StingDockPanel.DispatchCommand(tag);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error($"Hub dispatch '{tag}' failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HubBIMCoordCenterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => HubDispatcher.Run("BIMCoordinationCenter", ref message);
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HubSheetManagerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => HubDispatcher.Run("SheetManager", ref message);
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HubDrawingTypesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => HubDispatcher.Run("DrawingTypes_Editor", ref message);
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HubDocumentMgmtCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => HubDispatcher.Run("DocumentManager", ref message);
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HubBoqExportCostCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => HubDispatcher.Run("BOQCostManager", ref message);
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HubFabricationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => HubDispatcher.Run("Fabrication_OpenWorkspace", ref message);
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HubPlacementCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => HubDispatcher.Run("Placement_OpenCentre", ref message);
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HubStructuralDwgWizardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => HubDispatcher.Run("StrCADWizard", ref message);
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HubSchedulingDashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => HubDispatcher.Run("AutoSchedule4D", ref message);
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HubTag3DCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => HubDispatcher.Run("Tag3D", ref message);
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HubCreateTagFamiliesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => HubDispatcher.Run("CreateTagFamilies", ref message);
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HubAutoTagCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => HubDispatcher.Run("AutoTag", ref message);
    }
}
