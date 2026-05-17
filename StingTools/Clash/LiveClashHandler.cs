// LiveClashHandler.cs — drains LiveClashUpdater.DirtyQueue on the Revit API thread,
// calls ClashSession.RefreshElement for each dirty element, and writes CLASH_LIVE_FLAG
// parameters for newly flagged and cleared elements.
// Budget: 200 ms total per Idling tick. Drops excess to next tick.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Core.Clash
{
    public sealed class LiveClashHandler : IExternalEventHandler
    {
        private static LiveClashHandler _inst;
        public static ExternalEvent Event { get; private set; }

        // Round-robin cursor across the watched-element set so a coordinator
        // pinning hundreds of clashes doesn't starve the 200 ms budget on a
        // single tick. We always start at _watchedCursor and advance through
        // the watched ids in stable sorted order; on subsequent ticks the
        // cursor resumes where it left off.
        private int _watchedCursor;

        private LiveClashHandler() { }

        public static LiveClashHandler Instance
        {
            get
            {
                if (_inst == null) { _inst = new LiveClashHandler(); Event = ExternalEvent.Create(_inst); }
                return _inst;
            }
        }

        public string GetName() => "STING Live Clash Handler";

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app?.ActiveUIDocument?.Document;
                if (doc == null) return;

                var session = ClashSession.ForDocument(doc);
                if (!session.Initialised)
                {
                    // Lazy-init from the active 3D view. If no 3D view, skip.
                    var v3d = GetActiveOrDefault3DView(doc);
                    if (v3d == null) return;
                    var swInit = Stopwatch.StartNew();
                    session.InitialiseFromView(v3d);
                    StingLog.Info($"ClashSession cold-init {swInit.ElapsedMilliseconds}ms");
                }

                var sw = Stopwatch.StartNew();
                var toFlag = new HashSet<int>();
                var toClear = new HashSet<int>();
                int processed = 0;

                while (LiveClashUpdater.DirtyQueue.TryDequeue(out var entry) && sw.ElapsedMilliseconds < 200)
                {
                    try
                    {
                        LiveClashResult r;
                        if (entry.ElementId < 0)
                        {
                            // Deletion sentinel.
                            r = session.RemoveElement(-entry.ElementId);
                        }
                        else
                        {
                            r = session.RefreshElement(entry.ElementId);
                        }
                        foreach (var id in r.NewlyFlagged) { toClear.Remove(id); toFlag.Add(id); }
                        foreach (var id in r.NewlyCleared) { toFlag.Remove(id); toClear.Add(id); }
                        processed++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"LiveClash processOne {entry.ElementId}: {ex.Message}");
                    }
                }

                // F9: Re-evaluate watched elements every tick even if they
                //     weren't in the dirty queue. Lets coordinators "pin" a
                //     hard-to-investigate clash so it always reflects current
                //     state without waiting for an unrelated edit nearby.
                //
                //     Round-robin across the watched set instead of restarting
                //     from index 0 every tick — with hundreds of pinned ids
                //     the prior pass would always re-check the first N within
                //     budget and never reach the tail. The cursor is preserved
                //     on the handler instance and resumes on the next raise.
                if (sw.ElapsedMilliseconds < 200)
                {
                    var watched = session.WatchedSnapshot();
                    if (watched.Count > 0)
                    {
                        // Stable order so the cursor maps to the same id across
                        // ticks even when the underlying HashSet enumerates
                        // differently between calls.
                        var ordered = watched.OrderBy(x => x).ToList();
                        if (_watchedCursor >= ordered.Count) _watchedCursor = 0;
                        int start = _watchedCursor;
                        int processedWatched = 0;
                        for (int i = 0; i < ordered.Count; i++)
                        {
                            if (sw.ElapsedMilliseconds >= 200) break;
                            int idx = (start + i) % ordered.Count;
                            int watchedId = ordered[idx];
                            try
                            {
                                var r = session.RefreshElement(watchedId);
                                foreach (var id in r.NewlyFlagged) { toClear.Remove(id); toFlag.Add(id); }
                                foreach (var id in r.NewlyCleared) { toFlag.Remove(id); toClear.Add(id); }
                            }
                            catch (Exception ex) { StingLog.Warn($"LiveClash watched {watchedId}: {ex.Message}"); }
                            processedWatched++;
                        }
                        _watchedCursor = (start + processedWatched) % ordered.Count;
                        // If we didn't finish in this tick's remaining budget,
                        // re-raise so the next tick continues from the cursor.
                        if (processedWatched < ordered.Count)
                            Event?.Raise();
                    }
                }

                if (toFlag.Count > 0 || toClear.Count > 0)
                {
                    LiveClashFlag.Apply(doc, toFlag, toClear);
                }

                if (processed > 0)
                    StingLog.Info($"LiveClashHandler: {processed} dirty, +{toFlag.Count} flagged, -{toClear.Count} cleared, {sw.ElapsedMilliseconds}ms");

                // If there is still work in the queue, re-raise immediately.
                if (!LiveClashUpdater.DirtyQueue.IsEmpty)
                    Event?.Raise();
            }
            catch (Exception ex) { StingLog.Error("LiveClashHandler.Execute", ex); }
        }

        private static View3D GetActiveOrDefault3DView(Document doc)
        {
            var active = doc.ActiveView as View3D;
            if (active != null && !active.IsTemplate) return active;
            var collector = new FilteredElementCollector(doc).OfClass(typeof(View3D));
            foreach (View3D v in collector)
            {
                if (!v.IsTemplate) return v;
            }
            return null;
        }
    }
}
