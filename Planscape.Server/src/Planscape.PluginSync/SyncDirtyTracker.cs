// ══════════════════════════════════════════════════════════════════════════
//  SyncDirtyTracker.cs — per-document dirty SET for delta element sync.
//
//  WHY A SET AND NOT A QUEUE
//  The sibling LiveClashUpdater.GeometrySyncQueue is a ConcurrentQueue, so it
//  accumulates one entry per change notification: dragging a wall thirty times
//  enqueues that wall thirty times, and the consumer re-maps and re-sends the
//  same element thirty times. Set semantics collapse that to one, which is the
//  whole point of a delta channel — "I moved one wall" should send one wall.
//
//  DELETIONS
//  Follow the negative-element-id sentinel convention already established at
//  LiveClashUpdater.cs:72-76: a deleted element is recorded as -id. The element
//  object is gone by the time we hear about it, so the id is all that survives.
//
//  DEBOUNCE
//  Two clocks, following the StingAutoTagger._lastStaleMarkTime idiom:
//    • quiet period  — push ~3s after editing stops,
//    • hard ceiling  — push at most ~30s after the FIRST dirty element, so a
//      long continuous edit session still checkpoints.
//  A bulk operation (group move, workset assign, filter apply) touches
//  thousands of elements in a burst and must produce ONE send, not thousands.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace Planscape.PluginSync;

public static class SyncDirtyTracker
{
    /// <summary>Push this long after the last edit in a burst.</summary>
    public const int QuietPeriodMs = 3_000;

    /// <summary>Push at most this long after the first dirty element.</summary>
    public const int MaxHoldMs = 30_000;

    private sealed class DocState
    {
        internal readonly HashSet<long> Dirty = new HashSet<long>();
        internal DateTime FirstDirtyUtc = DateTime.MinValue;
        internal DateTime LastDirtyUtc  = DateTime.MinValue;
    }

    private static readonly object _lock = new object();
    private static readonly Dictionary<string, DocState> _byDoc =
        new Dictionary<string, DocState>(StringComparer.Ordinal);

    /// <summary>
    /// Record changed/added element ids (positive) and deleted ids
    /// (negative sentinel) for a document. Safe to call from the Revit
    /// updater thread; never throws.
    /// </summary>
    public static void Mark(string docGuid, IEnumerable<long> elementIds)
    {
        if (string.IsNullOrEmpty(docGuid) || elementIds == null) return;
        lock (_lock)
        {
            if (!_byDoc.TryGetValue(docGuid, out var state))
            {
                state = new DocState();
                _byDoc[docGuid] = state;
            }
            var now = DateTime.UtcNow;
            foreach (var id in elementIds)
            {
                // A deletion supersedes any pending add/modify for the same
                // element — otherwise we would send a row for an element that
                // no longer exists and then a delete for it in the same batch.
                if (id < 0) state.Dirty.Remove(-id);
                else if (state.Dirty.Contains(-id)) continue; // already deleted
                state.Dirty.Add(id);
            }
            if (state.Dirty.Count > 0)
            {
                if (state.FirstDirtyUtc == DateTime.MinValue) state.FirstDirtyUtc = now;
                state.LastDirtyUtc = now;
            }
        }
    }

    /// <summary>True when any document has dirty elements waiting.</summary>
    public static bool HasPending
    {
        get { lock (_lock) { return _byDoc.Values.Any(s => s.Dirty.Count > 0); } }
    }

    /// <summary>
    /// True when <paramref name="docGuid"/> has dirty elements AND either the
    /// quiet period has elapsed since the last edit or the hard ceiling has
    /// elapsed since the first.
    /// </summary>
    public static bool IsDue(string docGuid)
    {
        if (string.IsNullOrEmpty(docGuid)) return false;
        lock (_lock)
        {
            if (!_byDoc.TryGetValue(docGuid, out var state) || state.Dirty.Count == 0) return false;
            var now = DateTime.UtcNow;
            return (now - state.LastDirtyUtc).TotalMilliseconds >= QuietPeriodMs
                || (now - state.FirstDirtyUtc).TotalMilliseconds >= MaxHoldMs;
        }
    }

    /// <summary>True when ANY tracked document is due for a push.</summary>
    public static bool AnyDue()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            foreach (var state in _byDoc.Values)
            {
                if (state.Dirty.Count == 0) continue;
                if ((now - state.LastDirtyUtc).TotalMilliseconds >= QuietPeriodMs
                    || (now - state.FirstDirtyUtc).TotalMilliseconds >= MaxHoldMs)
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Atomically take and clear the dirty set for a document. Callers that
    /// fail to deliver should hand the ids back via <see cref="Mark"/> so the
    /// delta isn't lost — the 5-minute reconciliation sweep is the backstop
    /// either way.
    /// </summary>
    public static List<long> Drain(string docGuid)
    {
        if (string.IsNullOrEmpty(docGuid)) return new List<long>();
        lock (_lock)
        {
            if (!_byDoc.TryGetValue(docGuid, out var state)) return new List<long>();
            var ids = state.Dirty.ToList();
            state.Dirty.Clear();
            state.FirstDirtyUtc = DateTime.MinValue;
            state.LastDirtyUtc  = DateTime.MinValue;
            return ids;
        }
    }

    /// <summary>Discard everything (used when live sync is switched off).</summary>
    public static void Clear()
    {
        lock (_lock) { _byDoc.Clear(); }
    }

    public static int PendingCount(string docGuid)
    {
        if (string.IsNullOrEmpty(docGuid)) return 0;
        lock (_lock)
        {
            return _byDoc.TryGetValue(docGuid, out var state) ? state.Dirty.Count : 0;
        }
    }
}
