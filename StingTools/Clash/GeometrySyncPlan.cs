// GeometrySyncPlan.cs — the Revit-free decisions inside geometry sync.
//
// Extracted so they can be tested. GeometrySyncHandler itself is an
// IExternalEventHandler that needs a UIApplication and a Document, so nothing in
// it is reachable from the pure-logic test projects; but the two decisions that
// actually govern whether a change reaches the server — how the queue encoding
// is read, and which ids are worth retrying — are plain arithmetic over 64-bit
// element ids.
using System.Collections.Generic;

namespace StingTools.Core.Clash
{
    /// <summary>
    /// The queue encoding and the retry rule for Planscape geometry sync.
    ///
    /// <para><b>The encoding.</b> <c>LiveClashUpdater.GeometrySyncQueue</c>
    /// carries a single 64-bit id per change: positive means "this element was
    /// added or modified", negative means "this element was deleted". A 64-bit id
    /// is deliberate — Revit 2024+ element ids can exceed <c>int.MaxValue</c>, and
    /// truncating one to <c>int</c> could wrap it negative and flip an edit into a
    /// deletion. Packing a
    /// deletion as a negated id keeps one queue and one drain, but it means the
    /// sign IS the semantics — read it backwards and every deletion becomes an
    /// attempt to tessellate an element that no longer exists, while every edit
    /// becomes a tombstone that erases live geometry from the server.</para>
    /// </summary>
    public static class GeometrySyncPlan
    {
        /// <summary>
        /// Split drained queue entries into element ids to re-export and element
        /// ids to tombstone. Both come back POSITIVE — the sign is queue
        /// encoding, not data.
        /// </summary>
        public static (List<long> Changed, List<long> Deleted) Partition(IEnumerable<long> drained)
        {
            var changed = new List<long>();
            var deleted = new List<long>();
            if (drained == null) return (changed, deleted);

            foreach (long id in drained)
            {
                if (id < 0) deleted.Add(-id);
                else if (id > 0) changed.Add(id);
                // 0 is not a valid Revit element id; drop it rather than
                // tessellating nothing or tombstoning everything.
            }
            return (changed, deleted);
        }

        /// <summary>
        /// The ids to put back on the queue when an upload fails, in queue
        /// encoding (negative = deletion).
        ///
        /// <para><b>Only what was actually sendable.</b> A changed element whose
        /// geometry could not be extracted — deleted since the edit, no solid,
        /// view-specific — is deliberately NOT retried. It would fail extraction
        /// again on every subsequent save, so re-queueing it converts a lost
        /// delta into an infinite one that also drags the genuinely retryable
        /// ids around with it forever.</para>
        ///
        /// <para>Deletions are always retryable: a tombstone needs no geometry,
        /// so there is nothing that can fail to extract.</para>
        /// </summary>
        public static List<long> BuildRetrySet(
            IEnumerable<long> extractedChangedIds, IEnumerable<long> deletedIds)
        {
            var retry = new List<long>();
            if (extractedChangedIds != null)
                foreach (long id in extractedChangedIds) if (id > 0) retry.Add(id);
            if (deletedIds != null)
                foreach (long id in deletedIds) if (id > 0) retry.Add(-id);
            return retry;
        }
    }
}
