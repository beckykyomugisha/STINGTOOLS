using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Priority 7 — Activity feed. Last 50 material events in the running
    /// session, surfaced as a horizontal chip strip in the Hub status bar.
    /// Click a chip jumps to the affected material in the grid.
    /// </summary>
    public class ActivityEntry
    {
        public DateTime At { get; set; } = DateTime.Now;
        public string Kind { get; set; }      // MAT_AutoFill / MAT_EditCost / MAT_Merge / …
        public string Material { get; set; }
        public string Description { get; set; }
    }

    public static class MaterialActivityFeed
    {
        private static readonly ConcurrentQueue<ActivityEntry> _entries
            = new ConcurrentQueue<ActivityEntry>();
        private const int Cap = 50;
        public static event Action OnAdded;

        public static IEnumerable<ActivityEntry> Snapshot()
        {
            var arr = _entries.ToArray();
            Array.Reverse(arr);
            return arr;
        }

        public static void Add(string kind, string material, string description)
        {
            var e = new ActivityEntry { Kind = kind, Material = material, Description = description };
            _entries.Enqueue(e);
            while (_entries.Count > Cap && _entries.TryDequeue(out _)) { }
            try { OnAdded?.Invoke(); } catch (Exception ex) { StingLog.Warn($"ActivityFeed.OnAdded: {ex.Message}"); }
        }
    }
}
