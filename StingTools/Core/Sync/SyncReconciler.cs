// ══════════════════════════════════════════════════════════════════════════
//  SyncReconciler.cs — the reconciliation floor under the live delta channel.
//
//  WHY THIS EXISTS
//  Delta alone drifts. The live updater misses undo edge cases and link
//  reloads, and it sees nothing at all that happened while the user was logged
//  out or the plugin was unloaded. The 5-minute SyncScheduler tick therefore
//  stays, but instead of re-sending the entire model every five minutes it now
//  sweeps, hashes each element's content, and sends only the rows whose hash
//  moved since we last handed them to the queue.
//
//  WHICH HASH, AND WHY
//  Two patterns already existed in the tree:
//
//    • StingAutoTagger._elementVersionHash — a cheap delimited concatenation
//      of the fields that matter, compared against the previous value.
//    • BOQ/Sync/BoqSnapshotHasher — a canonical projection serialised to JSON
//      and SHA-256'd.
//
//  This uses the CHEAP CONCATENATION, for two reasons:
//
//    1. Cost. This runs over every element in the model every five minutes on
//       the Revit API thread. BoqSnapshotHasher allocates a JSON string and
//       runs SHA-256 per subject; at model scale that is a per-sweep stall for
//       no benefit.
//    2. The hash never leaves this process. It is a local "did this change
//       since I last sent it" memo compared against an in-process previous
//       value. BoqSnapshotHasher needs SHA-256 precisely because it does NOT
//       stay local — the server recomputes and compares the same checksum, so
//       it needs canonical cross-process agreement. We need none of that.
//
//  BoqSnapshotHasher's hard-won lesson still applies and is honoured by
//  construction: DefaultValueHandling.Include exists there because zero/empty
//  values were invisible to the checksum, so a change that only zeroed a field
//  slipped past drift detection. A length-prefixed concatenation cannot have
//  that bug — an empty field still occupies its slot (encoded "0:"), so a
//  field going empty changes the string.
//
//  The concatenation is folded into a 64-bit FNV-1a rather than stored as a
//  string, purely to bound memory: a large model is hundreds of thousands of
//  elements, and 8 bytes per element instead of a ~150-byte string is the
//  difference between a few MB and tens of MB held for the whole session. A
//  64-bit collision would mean one element's change is missed until it changes
//  again, and the live delta channel would catch it in the meantime.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Text;
using Planscape.Shared.Models;

namespace StingTools.Core.Sync
{
    internal static class SyncReconciler
    {
        // docKey -> (elementId -> content hash last handed to the queue)
        private static readonly Dictionary<string, Dictionary<long, ulong>> _sent =
            new Dictionary<string, Dictionary<long, ulong>>(StringComparer.Ordinal);
        private static readonly object _lock = new object();

        /// <summary>
        /// Reduce a full sweep to the rows that actually changed since the last
        /// sweep, plus IsDeleted rows for elements we have sent before that are
        /// no longer in the model.
        /// <para>
        /// The first sweep for a document returns everything, which is correct:
        /// the memo is in-process only, so a Revit restart re-establishes the
        /// baseline from scratch rather than trusting stale state.
        /// </para>
        /// </summary>
        internal static List<TagElementSync> FilterChanged(string docKey, List<TagElementSync> rows)
        {
            var changed = new List<TagElementSync>();
            if (rows == null) return changed;
            if (string.IsNullOrEmpty(docKey)) return rows;

            lock (_lock)
            {
                if (!_sent.TryGetValue(docKey, out var memo))
                {
                    memo = new Dictionary<long, ulong>();
                    _sent[docKey] = memo;
                }

                var seen = new HashSet<long>();
                foreach (var row in rows)
                {
                    if (row == null) continue;
                    seen.Add(row.RevitElementId);
                    ulong hash = ComputeHash(row);
                    if (memo.TryGetValue(row.RevitElementId, out var prev) && prev == hash) continue;

                    // Recorded as sent at filter time rather than on server ack,
                    // because the payload is about to be written to the durable
                    // offline queue and will be retried from there. The queue can
                    // still drop a payload (500-file cap, or a fatal 4xx), which
                    // it counts in DroppedSinceLastDrain; such a row is then not
                    // re-sent until it changes again or Revit restarts.
                    memo[row.RevitElementId] = hash;
                    changed.Add(row);
                }

                // Elements we have sent before that this sweep did not see are
                // gone — deleted while logged out, or a deletion the live updater
                // missed. This is the only path that can notice those.
                if (memo.Count > seen.Count)
                {
                    var vanished = new List<long>();
                    foreach (var id in memo.Keys) if (!seen.Contains(id)) vanished.Add(id);
                    foreach (var id in vanished)
                    {
                        memo.Remove(id);
                        changed.Add(TagElementSyncMapper.MapDeleted(id));
                    }
                }
            }
            return changed;
        }

        /// <summary>Forget a document's baseline (e.g. on close).</summary>
        internal static void Forget(string docKey)
        {
            if (string.IsNullOrEmpty(docKey)) return;
            lock (_lock) { _sent.Remove(docKey); }
        }

        /// <summary>
        /// Content hash over every field that the server stores. Field order is
        /// fixed and every field contributes a slot, so a value going empty is a
        /// change rather than an invisible no-op.
        /// </summary>
        internal static ulong ComputeHash(TagElementSync r)
        {
            var sb = new StringBuilder(256);
            // Length-prefixed rather than delimiter-separated, so no value can
            // ever contain the separator and forge a different field split:
            // "ab"+"" encodes as 2:ab0: and "a"+"b" as 1:a1:b.
            void F(string v) { sb.Append(v == null ? 0 : v.Length).Append(':').Append(v); }

            F(r.UniqueId); F(r.IfcGlobalId);
            F(r.Disc); F(r.Loc); F(r.Zone); F(r.Lvl);
            F(r.Sys); F(r.Func); F(r.Prod); F(r.Seq);
            F(r.Tag1); F(r.Tag7);
            F(r.CategoryName); F(r.FamilyName);
            F(r.Status); F(r.Rev);
            F(r.IsComplete ? "1" : "0");
            F(r.IsFullyResolved ? "1" : "0");
            F(r.IsStale ? "1" : "0");
            F(r.IsDeleted ? "1" : "0");
            F(r.Tag7A); F(r.Tag7B); F(r.Tag7C);
            F(r.Tag7D); F(r.Tag7E); F(r.Tag7F);
            F(r.T4Commissioning); F(r.T5Cost); F(r.T6Carbon); F(r.T7Fabrication);
            F(r.T8ClashTriage); F(r.T9AsBuilt); F(r.T10Compliance);
            F(r.ParaDepth.ToString(System.Globalization.CultureInfo.InvariantCulture));
            F(r.PatternMode);

            // LastModifiedUtc is deliberately EXCLUDED. It falls back to
            // DateTime.UtcNow whenever ASS_TAG_MODIFIED_DT is absent, so
            // including it would make every element's hash differ on every
            // sweep and defeat the entire point of this filter.

            return Fnv1a64(sb.ToString());
        }

        private static ulong Fnv1a64(string s)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime  = 1099511628211UL;
            ulong hash = offset;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                hash ^= (byte)(c & 0xFF);
                hash *= prime;
                hash ^= (byte)(c >> 8);
                hash *= prime;
            }
            return hash;
        }
    }
}
