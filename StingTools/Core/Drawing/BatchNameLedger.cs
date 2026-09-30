// StingTools — Drawing Template Manager · names taken during a production batch
//
// DrawingProducer primes the sheet numbers and view names in use once per batch, then
// adds each one it assigns so a later sheet in the same batch sees it. But a batch is
// one transaction per item, and an item can roll back — an area view that could not
// be cropped, a failure handler, a thrown exception. The sheet and view it made are
// gone; their number and name stayed in the set. The next item asking for the same
// number was then told it was taken and given "A-101-A" for a sheet that has no twin.
//
// The ledger remembers who took each name during the batch. A name whose owner no
// longer exists — rolled back — is released the next time it is asked about. Names
// that were already in the model when the batch was primed have no owner here and are
// never released: they were not made by this batch, so no rollback of it removes them.
//
// Checking on read rather than snapshotting per transaction covers every caller: a
// command that forgets to restore a snapshot after its rollback cannot reintroduce the
// false duplicate.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    public sealed class BatchNameLedger
    {
        private readonly Dictionary<string, long> _owners;

        /// <summary>Every name in use as far as this batch knows — what a uniqueness rule consults.</summary>
        public HashSet<string> Names { get; }

        public BatchNameLedger(IEnumerable<string> existing, StringComparer comparer)
        {
            comparer = comparer ?? StringComparer.Ordinal;
            Names = new HashSet<string>(existing ?? Enumerable.Empty<string>(), comparer);
            _owners = new Dictionary<string, long>(comparer);
        }

        /// <summary>Record that element <paramref name="ownerId"/> took <paramref name="name"/> in this batch.</summary>
        public void Record(string name, long ownerId)
        {
            if (string.IsNullOrEmpty(name)) return;
            Names.Add(name);
            _owners[name] = ownerId;
        }

        /// <summary>
        /// True when <paramref name="name"/> is taken. A name this batch gave to an element
        /// that <paramref name="alive"/> says no longer exists is released first.
        /// </summary>
        public bool Contains(string name, Func<long, bool> alive)
        {
            if (string.IsNullOrEmpty(name) || !Names.Contains(name)) return false;
            if (alive != null && _owners.TryGetValue(name, out var owner) && !alive(owner))
            {
                Names.Remove(name);
                _owners.Remove(name);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Release every name starting with <paramref name="prefix"/> whose owner is gone —
        /// run before a rule that walks suffixes of a base name (A-101, A-101-A, …).
        /// Returns how many were released.
        /// </summary>
        public int Heal(string prefix, Func<long, bool> alive)
        {
            if (alive == null || _owners.Count == 0) return 0;
            var gone = _owners
                .Where(kv => (prefix == null || kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) && !alive(kv.Value))
                .Select(kv => kv.Key).ToList();
            foreach (var n in gone) { Names.Remove(n); _owners.Remove(n); }
            return gone.Count;
        }
    }
}
