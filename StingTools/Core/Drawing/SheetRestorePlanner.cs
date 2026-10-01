// StingTools — Drawing Template Manager · what a sheet-number restore may safely move (DTW-200)
//
// Restore Sheet Numbers reads the last recorded renumber and puts every sheet back. The
// record is a file; the model is live, and the two part company: after Ctrl+Z of the
// renumber, the record still lists renames that are no longer there, and a sheet now
// carrying a recorded "to" number may be a different sheet that always had it. Moving
// it back onto its "from" number collides with the sheet that already has that number
// again. This decides, against the live numbers, which recorded changes can still be
// reversed and says why each other one cannot. Revit-free; StingTools.Tags.Tests
// compiles it.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    public static class SheetRestorePlanner
    {
        public sealed class Move
        {
            /// <summary>The sheet's key (its element id) in the caller's terms.</summary>
            public string SheetKey;
            /// <summary>Its number now — the recorded "to".</summary>
            public string Current;
            /// <summary>The number it goes back to — the recorded "from".</summary>
            public string Restore;
        }

        public sealed class Plan
        {
            public List<Move> Moves { get; } = new List<Move>();
            /// <summary>Recorded changes left alone, each with its reason.</summary>
            public List<string> Skipped { get; } = new List<string>();
            /// <summary>Changes whose old number is held again by a sheet this restore does
            /// not move — the signature of a renumber already undone with Ctrl+Z.</summary>
            public int LooksUndone { get; set; }
        }

        /// <param name="changes">The recorded (from, to) pairs, in record order.</param>
        /// <param name="sheetByNumber">Every live sheet number → that sheet's key.</param>
        public static Plan Build(IEnumerable<KeyValuePair<string, string>> changes,
            IReadOnlyDictionary<string, string> sheetByNumber)
        {
            var plan = new Plan();
            var live = sheetByNumber ?? new Dictionary<string, string>();
            var candidates = new List<Move>();
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in changes ?? Enumerable.Empty<KeyValuePair<string, string>>())
            {
                string from = c.Key?.Trim(), to = c.Value?.Trim();
                if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) continue;
                if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) continue;
                if (!TryGet(live, to, out var key))
                {
                    plan.Skipped.Add($"{to} (was {from}): no sheet carries '{to}' now — it was renumbered again, or the renumber was undone.");
                    continue;
                }
                if (!claimed.Add(key))
                {
                    plan.Skipped.Add($"{to} (was {from}): the record names this sheet twice; only the first is restored.");
                    continue;
                }
                candidates.Add(new Move { SheetKey = key, Current = to, Restore = from });
            }

            // A restore target held by a sheet that is NOT moving would collide. Dropping a
            // candidate frees nothing and may strand another, so repeat until stable.
            bool changed = true;
            while (changed)
            {
                changed = false;
                var moving = new HashSet<string>(candidates.Select(m => m.SheetKey), StringComparer.Ordinal);
                var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in candidates.ToList())
                {
                    string reason = null;
                    if (TryGet(live, m.Restore, out var holder) && !moving.Contains(holder))
                    {
                        reason = $"{m.Current} -> {m.Restore}: '{m.Restore}' is held by another sheet this restore does not move — "
                               + "the renumber looks already undone (Ctrl+Z), or that number was reused.";
                        plan.LooksUndone++;
                    }
                    else if (!targets.Add(m.Restore))
                        reason = $"{m.Current} -> {m.Restore}: another recorded sheet goes back to '{m.Restore}' too; left alone.";
                    if (reason == null) continue;
                    plan.Skipped.Add(reason);
                    candidates.Remove(m);
                    changed = true;
                    break;
                }
            }
            plan.Moves.AddRange(candidates);
            return plan;
        }

        private static bool TryGet(IReadOnlyDictionary<string, string> live, string number, out string key)
        {
            if (live.TryGetValue(number, out key)) return true;
            foreach (var kv in live)
                if (string.Equals(kv.Key, number, StringComparison.OrdinalIgnoreCase)) { key = kv.Value; return true; }
            key = null;
            return false;
        }
    }
}
