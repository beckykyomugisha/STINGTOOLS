// ══════════════════════════════════════════════════════════════════════════
//  LiveSyncUpdater.cs — IUpdater feeding the Planscape delta element sync.
//
//  CRITICAL INVARIANTS
//  1. Execute() NEVER throws. An exception out of an IUpdater takes Revit down
//     with it, so the whole body is wrapped (as LiveClashUpdater.cs:54-82 does).
//  2. Execute() never starts a transaction and never touches the network. It
//     only records element ids in Planscape.PluginSync.SyncDirtyTracker.
//  3. Triggers are NOT attached at registration. Revit evaluates an updater's
//     trigger filter on every element change even while the updater is
//     disabled, so an always-on trigger taxes every user who never touches
//     Planscape. Triggers go on at connect and come off at disconnect — the
//     same discipline StingAutoTagger.Register/Toggle uses, and for the reason
//     stated in that file.
//  4. HTTP never runs on the Revit API thread. The debounce timer (a plain
//     ThreadPool timer) only calls ExternalEvent.Raise(); the handler runs on
//     the API thread and only maps elements and writes a queue file; the
//     network call happens off-thread in Task.Run. Same shape as
//     PluginSyncTickBridge.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace StingTools.Core.Sync
{
    public sealed class LiveSyncUpdater : IUpdater
    {
        private static UpdaterId _updaterId;
        private static LiveSyncUpdater _instance;
        private static ExternalEvent _pushEvent;
        private static Timer _debounceTimer;
        private static bool _triggersActive;
        private static readonly object _lifecycleLock = new object();

        /// <summary>How often the debounce timer checks whether a push is due.</summary>
        private const int PollIntervalMs = 1_000;

        public static bool IsLive { get { lock (_lifecycleLock) { return _triggersActive; } } }

        internal static string DocKey(Document doc) =>
            doc?.ProjectInformation?.UniqueId ?? doc?.PathName ?? "host";

        public UpdaterId GetUpdaterId() => _updaterId;
        public string GetUpdaterName() => "STING Planscape Live Sync";
        public string GetAdditionalInformation() => "Records edited elements for delta sync to Planscape.";
        public ChangePriority GetChangePriority() => ChangePriority.MEPAccessoriesFittingsSegmentsWires;

        public void Execute(UpdaterData data)
        {
            // MUST NOT THROW — see invariant 1.
            try
            {
                var doc = data?.GetDocument();
                if (doc == null) return;
                string key = DocKey(doc);

                var ids = new List<long>();
                foreach (var id in data.GetModifiedElementIds()) ids.Add(id.Value);
                foreach (var id in data.GetAddedElementIds())    ids.Add(id.Value);
                // Deleted: negative sentinel, per the LiveClashUpdater convention.
                foreach (var id in data.GetDeletedElementIds())  ids.Add(-id.Value);

                if (ids.Count > 0) Planscape.PluginSync.SyncDirtyTracker.Mark(key, ids);
            }
            catch (Exception ex)
            {
                StingLog.Error("LiveSyncUpdater.Execute swallowed", ex);
            }
        }

        /// <summary>
        /// Register at startup with NO triggers. Call from OnStartup.
        /// </summary>
        public static void Register(UIControlledApplication application)
        {
            try
            {
                lock (_lifecycleLock)
                {
                    if (_updaterId != null) return;
                    _updaterId = new UpdaterId(application.ActiveAddInId,
                        new Guid("6E1C7A94-2B58-4D33-9A17-8F5C0D2E4B61"));
                    _instance = new LiveSyncUpdater();
                    UpdaterRegistry.RegisterUpdater(_instance, true);
                    UpdaterRegistry.DisableUpdater(_updaterId);

                    _pushEvent = ExternalEvent.Create(new LiveSyncPushHandler());
                    StingLog.Info("LiveSyncUpdater: registered (disabled, no triggers)");
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("LiveSyncUpdater.Register failed", ex);
            }
        }

        /// <summary>
        /// Attach triggers and start the debounce timer. Call on a successful
        /// Planscape connect. Idempotent.
        /// </summary>
        public static void StartLive()
        {
            try
            {
                lock (_lifecycleLock)
                {
                    if (_updaterId == null)
                    {
                        StingLog.Warn("LiveSyncUpdater.StartLive: not registered, ignoring");
                        return;
                    }
                    if (_triggersActive) return;

                    // All model element instances — element sync is not restricted
                    // to a category list.
                    var filter = new ElementIsElementTypeFilter(true);
                    UpdaterRegistry.AddTrigger(_updaterId, filter, Element.GetChangeTypeAny());
                    UpdaterRegistry.AddTrigger(_updaterId, filter, Element.GetChangeTypeElementAddition());
                    UpdaterRegistry.AddTrigger(_updaterId, filter, Element.GetChangeTypeElementDeletion());
                    UpdaterRegistry.EnableUpdater(_updaterId);

                    _debounceTimer = new Timer(OnDebounceTick, null, PollIntervalMs, PollIntervalMs);
                    _triggersActive = true;
                    StingLog.Info("LiveSyncUpdater: live (triggers attached, debounce timer started)");
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("LiveSyncUpdater.StartLive failed", ex);
            }
        }

        /// <summary>
        /// Remove triggers and stop the debounce timer. Call on disconnect /
        /// shutdown so users who aren't using Planscape pay nothing.
        /// </summary>
        public static void StopLive()
        {
            try
            {
                lock (_lifecycleLock)
                {
                    if (_updaterId == null || !_triggersActive) return;
                    try { UpdaterRegistry.RemoveAllTriggers(_updaterId); }
                    catch (Exception ex) { StingLog.Warn($"LiveSyncUpdater: RemoveAllTriggers: {ex.Message}"); }
                    UpdaterRegistry.DisableUpdater(_updaterId);

                    _debounceTimer?.Dispose();
                    _debounceTimer = null;
                    _triggersActive = false;
                    Planscape.PluginSync.SyncDirtyTracker.Clear();
                    StingLog.Info("LiveSyncUpdater: stopped (triggers removed)");
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("LiveSyncUpdater.StopLive failed", ex);
            }
        }

        public static void Unregister()
        {
            try
            {
                StopLive();
                lock (_lifecycleLock)
                {
                    if (_updaterId != null) UpdaterRegistry.UnregisterUpdater(_updaterId);
                    _updaterId = null;
                }
            }
            catch (Exception ex) { StingLog.Warn($"LiveSyncUpdater.Unregister: {ex.Message}"); }
        }

        /// <summary>
        /// ThreadPool timer callback. MUST NOT touch the Revit API — it only
        /// raises the external event so the push runs on the API thread.
        /// </summary>
        private static void OnDebounceTick(object state)
        {
            try
            {
                if (!Planscape.PluginSync.SyncDirtyTracker.AnyDue()) return;
                _pushEvent?.Raise();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LiveSyncUpdater.OnDebounceTick: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Runs on the Revit API thread. Coalesces the whole dirty set into ONE
    /// payload and hands the offline queue a single file, then kicks the drain
    /// off-thread.
    /// </summary>
    internal sealed class LiveSyncPushHandler : IExternalEventHandler
    {
        public string GetName() => "STING Planscape Live Sync Push";

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app?.ActiveUIDocument?.Document;
                if (doc == null || doc.IsFamilyDocument) return;

                string key = LiveSyncUpdater.DocKey(doc);
                if (!Planscape.PluginSync.SyncDirtyTracker.IsDue(key)) return;

                var client = BIMManager.PlanscapeServerClient.Instance;
                if (client == null || !client.IsConnected) return;

                string bimDir = BIMManager.BIMManagerEngine.GetBIMManagerDir(doc);
                Guid projectId = BIMManager.PlatformSyncCommand.LoadPlanscapeProjectId(
                    System.IO.Path.Combine(bimDir, "planscape_connection.json"));
                var queue = Planscape.PluginSync.OfflineQueue.Shared;

                // Undeliverable: no linked project, or the scheduler isn't
                // running. Discard the dirty set rather than leaving it pending
                // — it stays "due" forever otherwise, and the debounce timer
                // would raise this external event once a second for the rest of
                // the session. Nothing is lost that matters: the 5-minute
                // reconciliation sweep re-derives state from the model by
                // content hash, so whatever we drop here is picked up as soon
                // as delivery becomes possible.
                if (projectId == Guid.Empty || queue == null)
                {
                    int discarded = Planscape.PluginSync.SyncDirtyTracker.Drain(key).Count;
                    if (discarded > 0)
                        StingLog.Info($"LiveSyncPush: {discarded} dirty element(s) discarded for {doc.Title} — " +
                                      (projectId == Guid.Empty ? "no Planscape project linked" : "sync scheduler not running") +
                                      "; the 5-minute reconciliation sweep will re-derive them");
                    return;
                }

                var ids = Planscape.PluginSync.SyncDirtyTracker.Drain(key);
                if (ids.Count == 0) return;

                var rows = new List<Planscape.Shared.Models.TagElementSync>(ids.Count);
                foreach (long raw in ids)
                {
                    if (raw < 0)
                    {
                        rows.Add(TagElementSyncMapper.MapDeleted(-raw));
                        continue;
                    }
                    Element el = null;
                    try { el = doc.GetElement(new ElementId(raw)); }
                    catch (Exception ex) { StingLog.Warn($"LiveSyncPush: GetElement({raw}): {ex.Message}"); }

                    // Gone without a deletion notification (undo, link reload) —
                    // record it as deleted rather than dropping it silently.
                    if (el == null || !el.IsValidObject)
                    {
                        rows.Add(TagElementSyncMapper.MapDeleted(raw));
                        continue;
                    }
                    if (el is ElementType) continue;

                    rows.Add(TagElementSyncMapper.MapElement(
                        doc, el, hydrateTiers: TagElementSyncMapper.ShouldHydrateTiers(el)));
                }

                if (rows.Count == 0) return;

                var payload = new Planscape.Shared.Models.PluginSyncPayload
                {
                    ProjectId     = projectId,
                    UserName      = client.ConnectedUser ?? Environment.UserName,
                    RevitVersion  = app.Application?.VersionNumber ?? "",
                    PluginVersion = typeof(LiveSyncUpdater).Assembly.GetName().Version?.ToString() ?? "2.2.0",
                    Timestamp     = DateTime.UtcNow,
                    TagElements   = rows,
                };

                // ONE payload for N dirty elements — never one per element.
                // ChunkForTransport is a no-op below MaxElementsPerPayload, so a
                // normal delta is exactly one queue file; only a very large bulk
                // edit splits, and the queue's file names now carry a monotonic
                // suffix so multiple files in the same millisecond can't collide.
                var chunks = BIMManager.PlatformSyncCommand.ChunkForTransport(payload);
                foreach (var chunk in chunks) queue.Enqueue(chunk);
                StingLog.Info($"LiveSyncPush: {rows.Count:N0} dirty element(s) coalesced into " +
                              $"{chunks.Count} payload(s) for {doc.Title} (queue depth: {queue.Count})");

                // Network off the Revit API thread.
                if (Planscape.PluginSync.SyncScheduler.Instance != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try { await Planscape.PluginSync.SyncScheduler.Instance.SyncNowAsync(); }
                        catch (Exception ex) { StingLog.Warn($"LiveSyncPush drain: {ex.Message}"); }
                    });
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("LiveSyncPushHandler.Execute swallowed", ex);
            }
        }
    }
}
