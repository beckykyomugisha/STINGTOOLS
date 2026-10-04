// TaggingStatsSnapshot — undo a rolled-back batch's counts (ROADMAP ELEC-32).
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using StingTools.Core;

namespace StingTools.Tags
{

    /// <summary>
    /// ELEC-32: a copy of a <see cref="TaggingStats"/> taken before a batch, so a batch
    /// Revit rolled back can be taken back out of the counts. TaggingStats exposes only
    /// Record* methods (private setters, readonly collections), so every instance field is
    /// copied by reflection — a field added later is covered without anyone remembering.
    /// Collections are restored in place (same instance, same comparer).
    /// </summary>
    internal sealed class TaggingStatsSnapshot
    {
        private sealed class DictSnap { public System.Collections.IDictionary Target; public List<KeyValuePair<object, object>> Entries; }
        private sealed class ListSnap { public System.Collections.IList Target; public List<object> Items; }

        private readonly TaggingStats _stats;
        private readonly List<(System.Reflection.FieldInfo fi, object value)> _fields =
            new List<(System.Reflection.FieldInfo, object)>();

        private TaggingStatsSnapshot(TaggingStats stats) { _stats = stats; }

        internal static TaggingStatsSnapshot Take(TaggingStats stats)
        {
            if (stats == null) return null;
            var snap = new TaggingStatsSnapshot(stats);
            foreach (var f in typeof(TaggingStats).GetFields(System.Reflection.BindingFlags.Instance
                         | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                snap._fields.Add((f, Capture(f.GetValue(stats))));
            return snap;
        }

        /// <summary>Put the stats back to what they were when the snapshot was taken.</summary>
        internal void Restore()
        {
            foreach (var (fi, value) in _fields)
            {
                try
                {
                    object restored = Apply(value);
                    if (!fi.IsInitOnly) fi.SetValue(_stats, restored);
                }
                catch (Exception ex) { StingLog.Warn($"TaggingStats restore {fi.Name}: {ex.Message}"); }
            }
        }

        private static object Capture(object v)
        {
            if (v is System.Collections.IDictionary d)
            {
                var entries = new List<KeyValuePair<object, object>>();
                foreach (System.Collections.DictionaryEntry de in d)
                    entries.Add(new KeyValuePair<object, object>(de.Key, Capture(de.Value)));
                return new DictSnap { Target = d, Entries = entries };
            }
            if (v is System.Collections.IList l)
            {
                var items = new List<object>();
                foreach (object item in l) items.Add(Capture(item));
                return new ListSnap { Target = l, Items = items };
            }
            return v;
        }

        private static object Apply(object snap)
        {
            if (snap is DictSnap d)
            {
                d.Target.Clear();
                foreach (var e in d.Entries) d.Target[e.Key] = Apply(e.Value);
                return d.Target;
            }
            if (snap is ListSnap l)
            {
                if (l.Target.IsFixedSize)
                {
                    for (int i = 0; i < l.Items.Count && i < l.Target.Count; i++) l.Target[i] = Apply(l.Items[i]);
                }
                else
                {
                    l.Target.Clear();
                    foreach (object item in l.Items) l.Target.Add(Apply(item));
                }
                return l.Target;
            }
            return snap;
        }
    }
}
