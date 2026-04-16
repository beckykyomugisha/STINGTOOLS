using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using StingTools.UI;
using StingTools.BIMManager;
using Planscape.PluginSync;

namespace StingTools.Core
{
    /// <summary>
    /// Main Revit external application. Registers the STING Tools dockable panel
    /// as the single unified UI for all 160+ commands across 6 tabs:
    /// SELECT, ORGANISE, DOCS, TEMP, CREATE, VIEW.
    /// The ribbon tab contains only a single toggle button to show/hide the panel.
    /// </summary>
    public class StingToolsApp : IExternalApplication
    {
        public static string AssemblyPath { get; private set; }
        public static string DataPath { get; private set; }

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

                // Register the dockable panel — the single unified UI
                RegisterDockablePanel(application);

                // Register the real-time auto-tagger (IUpdater) — starts disabled
                StingAutoTagger.Register(application);

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

                // AUTO-SYNC: Queue lightweight compliance sync on document save
                application.ControlledApplication.DocumentSaved += OnDocumentSaved;

                // S03b: Start the Planscape sync scheduler if the plugin has already
                // authenticated with the server. Runs on a 5-min timer in-process and
                // drains the offline queue; safe no-op if not configured.
                try
                {
                    var serverUrl = StingBIMServerClient.Instance?.ServerUrl;
                    var authToken = StingBIMServerClient.Instance?.AuthToken;
                    if (!string.IsNullOrEmpty(serverUrl) && !string.IsNullOrEmpty(authToken))
                    {
                        SyncScheduler.Start(serverUrl, authToken);
                        StingLog.Info($"SyncScheduler started against {serverUrl}");
                    }
                    else
                    {
                        StingLog.Info("SyncScheduler not started — no server URL / auth token yet (will run offline-queue only)");
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
                StingLog.Info("DocumentClosing: cleared parameter, compliance, formula, selection, deferred, workset, and level caches");
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
                    dlg.CommonButtons = TaskDialogCommonButtons.Close;
                    var dlgResult = dlg.Show();

                    if (dlgResult == TaskDialogResult.CommandLink1)
                    {
                        UI.StingCommandHandler.SetExtraParam("WorkflowPresetName", "MorningHealthCheck");
                        StingLog.Info("Morning briefing: user requested MorningHealthCheck workflow");
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
                Document currentDoc = e.CurrentActiveView?.Document;
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
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ViewActivated handler: {ex.Message}");
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

                // Build the sync payload and hand it to the offline queue.
                // If SyncScheduler hasn't been started yet (user isn't logged in),
                // OfflineQueue.Shared is null and we log+skip.
                try
                {
                    var client = StingBIMServerClient.Instance;
                    var payload = new Planscape.Shared.Models.PluginSyncPayload
                    {
                        ProjectId     = Guid.Empty, // server resolves via auth/tenant scope
                        UserName      = client?.ConnectedUser ?? Environment.UserName ?? "Unknown",
                        RevitVersion  = Assembly.GetAssembly(typeof(Autodesk.Revit.DB.Document))?
                                          .GetName().Version?.ToString() ?? "",
                        PluginVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "",
                        Timestamp     = DateTime.UtcNow,
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
                            $"({taggedCount}/{totalElements}) enqueued (queue depth: {queue.Count})");
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

        public Result OnShutdown(UIControlledApplication application)
        {
            StingLog.Info("STING Tools shutting down");

            // Unhook DocumentSaved sync handler
            try { application.ControlledApplication.DocumentSaved -= OnDocumentSaved; }
            catch (Exception ex) { StingLog.Warn($"DocumentSaved unhook: {ex.Message}"); }

            // S03: Tear down the sync scheduler and its background timer.
            try { SyncScheduler.StopShared(); }
            catch (Exception ex) { StingLog.Warn($"SyncScheduler stop: {ex.Message}"); }

            StingPluginHooks.ClearAll();
            StingAutoTagger.Unregister();
            UI.ThemeManager.ClearTarget(); // H-02: Prevent memory leak from static WPF reference
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

            // DATA-01: Validate schema version headers on TAG_CONFIG CSVs
            string[] versionedCsvs = new[]
            {
                "STING_TAG_CONFIG_v5_0_GEN.csv",
                "STING_TAG_CONFIG_v5_0_ARCH.csv",
                "STING_TAG_CONFIG_v5_0_STR.csv",
                "STING_TAG_CONFIG_v5_0_MEP.csv",
            };
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
}
