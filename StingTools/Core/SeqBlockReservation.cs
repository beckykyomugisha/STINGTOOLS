using System;
using System.Collections.Generic;

namespace StingTools.Core
{
    /// <summary>
    /// A set of sequence-number blocks reserved from the Planscape server, one
    /// per counter key, that <see cref="SeqAssigner.AssignNext"/> draws from
    /// instead of allocating locally.
    ///
    /// WHY THIS EXISTS
    /// ---------------
    /// <see cref="SeqAssigner"/> allocates optimistically against an in-memory
    /// counter dictionary and the results are reconciled later by the
    /// max-per-key merge at <c>POST /seq/sync</c>. That is safe for a single
    /// Revit instance, but it is a *read-then-write* across hosts: Revit and
    /// StingBridge can both read the same high-water mark, both mint the same
    /// number, and the max-merge then accepts the higher of two identical
    /// values without noticing the collision. Server-side reservation closes
    /// that window by bumping and reading the counter in one indivisible step
    /// (<c>POST /seq/reserve</c>, which is backed by
    /// <c>INSERT … ON CONFLICT … RETURNING</c>).
    ///
    /// OFFLINE WINDOW — A KNOWN, DELIBERATE LIMITATION
    /// -----------------------------------------------
    /// When no server connection is configured, or the reserve call fails, the
    /// caller passes <c>null</c> here and <see cref="SeqAssigner"/> falls back to
    /// today's purely local allocation. In that mode the cross-host duplicate
    /// window described above **remains open**. This is intentional: refusing to
    /// number elements offline would be worse than numbering them optimistically
    /// and reconciling on the next sync, which is the behaviour that has shipped
    /// to date. It is recorded in docs/ROADMAP.md (SB-2) so it is not mistaken
    /// for a closed gap.
    ///
    /// This type is deliberately pure — no HTTP, no Revit API — so the
    /// allocation logic is unit-testable without a server or a running Revit.
    /// The transport lives in
    /// <c>PlanscapeServerClient.ReserveSeqBlocksAsync</c>.
    /// </summary>
    public sealed class SeqBlockReservation
    {
        private sealed class Block
        {
            public int Next;   // next unissued number
            public int End;    // inclusive last number of the reserved block
        }

        private readonly Dictionary<string, Block> _blocks =
            new Dictionary<string, Block>(StringComparer.Ordinal);

        /// <summary>Keys that carry at least one still-unissued number.</summary>
        public IEnumerable<string> Keys => _blocks.Keys;

        /// <summary>
        /// Record a reserved inclusive block <paramref name="start"/>..<paramref name="end"/>
        /// for <paramref name="key"/>. A later call for the same key replaces the
        /// earlier block — the server hands out disjoint ranges, so the newer one
        /// is always the live grant and the remainder of the old one is simply
        /// forfeited (gaps are acceptable; duplicates are not).
        /// </summary>
        public void Add(string key, int start, int end)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            if (end < start) return;               // empty grant — nothing to record
            _blocks[key] = new Block { Next = start, End = end };
        }

        /// <summary>How many numbers remain unissued for <paramref name="key"/>.</summary>
        public int Remaining(string key)
            => _blocks.TryGetValue(key, out var b) ? Math.Max(0, b.End - b.Next + 1) : 0;

        /// <summary>
        /// Take the next reserved number for <paramref name="key"/>.
        /// Returns false when the key was never reserved or its block is spent —
        /// callers MUST then fall back to local allocation rather than inventing
        /// a number, because an invented number is exactly the duplicate this
        /// class exists to prevent.
        /// </summary>
        public bool TryTake(string key, out int number)
        {
            number = 0;
            if (key == null || !_blocks.TryGetValue(key, out var b)) return false;
            if (b.Next > b.End) return false;      // exhausted
            number = b.Next;
            b.Next++;
            return true;
        }
    }
}
