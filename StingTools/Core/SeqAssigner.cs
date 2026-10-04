using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core
{
    /// <summary>Sequence numbering scheme variants.</summary>
    public enum SeqScheme
    {
        /// <summary>Zero-padded numeric: 0001, 0042</summary>
        Numeric,
        /// <summary>Alphabetic: A, B, ... Z, AA, AB</summary>
        Alpha,
        /// <summary>Zone-prefixed: Z1-0042</summary>
        ZonePrefix,
        /// <summary>Discipline-prefixed: M-0042</summary>
        DiscPrefix
    }

    /// <summary>Why an AssignNext call failed to produce a unique sequence number.</summary>
    public enum SeqFailureReason
    {
        None,
        /// <summary>The very first increment already exceeded the pad capacity.</summary>
        InitialOverflow,
        /// <summary>An increment inside the collision loop exceeded the pad capacity.</summary>
        CollisionOverflow,
        /// <summary>The collision safety limit was exhausted and the tag is still a duplicate.</summary>
        SafetyExhausted,
        /// <summary>DSCH-39: the next number would pass the SEQ_RANGE_ALLOCATION maximum
        /// for the element's DISC. Refused, never wrapped and never written.</summary>
        RangeExhausted,
        /// <summary>DSCH-39: a server-reserved number lies outside the DISC's allocated
        /// range. Refused rather than written out of range.</summary>
        ReservationOutsideRange
    }

    /// <summary>Outcome of <see cref="SeqAssigner.AssignNext"/>.</summary>
    public readonly struct SeqResult
    {
        public bool Success { get; }
        public string Tag { get; }
        public string Seq { get; }
        public int CollisionCount { get; }
        public SeqFailureReason Failure { get; }

        private SeqResult(bool success, string tag, string seq, int collisions, SeqFailureReason failure)
        {
            Success = success; Tag = tag; Seq = seq; CollisionCount = collisions; Failure = failure;
        }

        public static SeqResult Ok(string tag, string seq, int collisions)
            => new SeqResult(true, tag, seq, collisions, SeqFailureReason.None);

        public static SeqResult Fail(SeqFailureReason reason, int collisions)
            => new SeqResult(false, null, null, collisions, reason);
    }

    /// <summary>
    /// Pure (Revit-free) sequence-number assignment used by the ISO 19650
    /// tagging pipeline. Encapsulates the SEQ counter key, the SEQ string
    /// formatting, the overflow cap, and the collision auto-increment loop so
    /// the logic can be unit-tested without a Revit document.
    ///
    /// <see cref="TagConfig.BuildAndWriteTag"/> delegates here for the
    /// counter/collision arithmetic; it keeps the Revit-side concerns
    /// (parameter writes, logging, <c>TaggingStats</c>) around the call.
    /// </summary>
    public static class SeqAssigner
    {
        /// <summary>
        /// Why a numeric SEQ token is not a valid sequence at <paramref name="pad"/> digits,
        /// or null when it is. A sequence is exactly <paramref name="pad"/> digits and at
        /// least 1: BuildSeqString always pads and counters start at 1. "12" at pad 4 or
        /// "00012" was written by hand or under an older pad setting, and "0000" is the
        /// unassigned placeholder at any pad width (IsUnassignedSeq). The check
        /// used to accept pad + 1 digits, any width below it, and 0.
        /// </summary>
        public static string ValidateNumericSeq(string value, int pad)
        {
            if (pad <= 0) pad = 4;
            string v = value ?? "";
            if (v.Length == 0 || !v.All(c => c >= '0' && c <= '9'))
                return $"SEQ '{value}' is not a valid number";
            if (v.Length != pad)
                return $"SEQ '{value}' is not {pad} digits (zero-padded, e.g. {1.ToString().PadLeft(pad, '0')})";
            if (IsUnassignedSeq(v))
                return $"SEQ '{value}' is zero; sequences start at {1.ToString().PadLeft(pad, '0')}";
            return null;
        }

        /// <summary>The unassigned-SEQ placeholder at <paramref name="pad"/> digits ("0000"
        /// at the default 4). It was the literal "0000" everywhere, so at pad 3 or 5 the
        /// placeholder no longer looked like a sequence and was never recognised.</summary>
        public static string UnassignedSeq(int pad) => new string('0', pad > 0 ? pad : 4);

        /// <summary>True for an unassigned SEQ at any pad width: one or more zeros and
        /// nothing else.</summary>
        public static bool IsUnassignedSeq(string value)
            => !string.IsNullOrEmpty(value) && value.All(c => c == '0');

        /// <summary>True for a token that was never resolved: XX, ZZ or an all-zero SEQ.
        /// GEN is an assumed value, not an unresolved one (TagConfig.TagIsComplete).</summary>
        public static bool IsUnresolvedToken(string value)
            => value == "XX" || value == "ZZ" || IsUnassignedSeq(value);

        /// <summary>Highest value representable in <paramref name="pad"/> digits.</summary>
        public static int MaxSeqForPad(int pad)
            => pad switch { 1 => 9, 2 => 99, 3 => 999, 4 => 9999, 5 => 99999, _ => (int)Math.Pow(10, pad) - 1 };

        /// <summary>
        /// Canonical SEQ counter key. Format: <c>DISC_SYS_LVL</c>, or
        /// <c>DISC_ZONE_SYS_LVL</c> when <paramref name="includeZone"/> is set.
        /// Normalises empty / placeholder tokens so the key never drifts between
        /// sessions (empty DISC→A, SYS→GEN, LVL/XX→L00, ZONE/XX/ZZ→Z01).
        /// </summary>
        public static string BuildSeqKey(string disc, string sys, string lvl, string zone, bool includeZone)
            => BuildSeqKey(disc, sys, lvl, zone, null, includeZone, includeLoc: false);

        /// <summary>
        /// Phase 191 — LOC-aware overload. When <paramref name="includeLoc"/> is
        /// set the location (building/volume) code joins the counter key so each
        /// building numbers independently — multi-building campuses get
        /// per-volume sequences (Temple AHU-0001 and Meetinghouse AHU-0001
        /// coexist). Key shapes: <c>DISC_SYS_LVL</c> · <c>DISC_ZONE_SYS_LVL</c>
        /// · <c>DISC_LOC_SYS_LVL</c> · <c>DISC_LOC_ZONE_SYS_LVL</c>.
        /// Empty LOC normalises to XX — "location not established", the value
        /// BuildAndWriteTag writes (F-2). It used to normalise to BLD1, which put
        /// every unplaced element in building 1's counter group.
        /// </summary>
        public static string BuildSeqKey(string disc, string sys, string lvl, string zone, string loc, bool includeZone, bool includeLoc)
        {
            if (string.IsNullOrEmpty(disc)) disc = "A";
            if (string.IsNullOrEmpty(sys))  sys  = "GEN";
            if (string.IsNullOrEmpty(lvl) || lvl == "XX") lvl = "L00";

            string locPart = null;
            if (includeLoc)
            {
                locPart = loc;
                if (string.IsNullOrEmpty(locPart)) locPart = "XX";
            }

            if (includeZone)
            {
                if (string.IsNullOrEmpty(zone) || zone == "XX" || zone == "ZZ") zone = "Z01";
                return includeLoc
                    ? $"{disc}_{locPart}_{zone}_{sys}_{lvl}"
                    : $"{disc}_{zone}_{sys}_{lvl}";
            }
            return includeLoc
                ? $"{disc}_{locPart}_{sys}_{lvl}"
                : $"{disc}_{sys}_{lvl}";
        }

        /// <summary>Format a sequence number for the given scheme and pad width.</summary>
        public static string BuildSeqString(int n, SeqScheme scheme, int pad, string zoneOrDisc = "")
        {
            if (pad <= 0) pad = 4;
            switch (scheme)
            {
                case SeqScheme.Alpha:
                    return ToAlpha(n);
                // ZonePrefix / DiscPrefix are DEPRECATED. They emitted a SEQ
                // string like "Z1-0042" / "M-0042" — which (1) injects the tag
                // separator '-' into the SEQ segment, turning the fixed
                // 8-segment ISO 19650 tag into 9 segments, and (2) duplicates
                // the ZONE (seg 3) and DISC (seg 1) tokens that already have
                // their own segments. The net effect on the assembled tag was
                // "DISC shows twice / SEQ replaced by the discipline code".
                // They now fall through to Numeric so the canonical tag can
                // never be corrupted and any project that persisted one of
                // these schemes self-heals on the next re-sequence.
                case SeqScheme.ZonePrefix:
                case SeqScheme.DiscPrefix:
                case SeqScheme.Numeric:
                default:
                    return n.ToString().PadLeft(pad, '0');
            }
        }

        /// <summary>Convert an integer to alphabetic (A=1, B=2 … Z=26, AA=27 …).</summary>
        public static string ToAlpha(int n)
        {
            if (n <= 0) return "A";
            string result = "";
            while (n > 0)
            {
                n--;
                result = (char)('A' + (n % 26)) + result;
                n /= 26;
            }
            return result;
        }

        /// <summary>
        /// DSCH-39: true when <paramref name="n"/> lies inside <paramref name="range"/>
        /// (always true with no range). A held number outside the range must not move a
        /// counter: one above the maximum would block the whole group, and one below the
        /// minimum is superseded by the start-at-minimum rule.
        /// </summary>
        public static bool InRange(int n, (int Min, int Max)? range)
            => range == null || (n >= range.Value.Min && n <= range.Value.Max);

        /// <summary>
        /// DSCH-39: the counter value from which the next allocation is made. With a
        /// range, a counter below <c>Min - 1</c> (a new counter is 0) is lifted so the
        /// next number is <c>Min</c>. With no range the counter is returned unchanged.
        /// </summary>
        public static int FloorForRange(int counter, (int Min, int Max)? range)
            => range != null && counter < range.Value.Min - 1 ? range.Value.Min - 1 : counter;

        /// <summary>
        /// Allocate the next unique sequence number for <paramref name="seqKey"/>.
        ///
        /// Tentatively increments <paramref name="counters"/>[seqKey]; on overflow
        /// past the pad capacity it rolls the counter back and fails. When
        /// <paramref name="existingTags"/> is supplied, it auto-increments past any
        /// already-present tag (up to <paramref name="maxCollisionDepth"/> tries),
        /// failing (and rolling back) on overflow or exhaustion. The full tag is
        /// composed as <c>tagBody + seq + tagSuffix</c> so the collision check sees
        /// the same string the model stores.
        ///
        /// On success the counter is left at the allocated value; on any failure it
        /// is restored to its pre-increment value so the slot can be reused.
        /// <paramref name="existingTags"/> is only read, never mutated.
        ///
        /// DSCH-39: <paramref name="range"/> is the SEQ_RANGE_ALLOCATION entry for the
        /// element's DISC (TagConfig.SeqRangeFor). It applies to every counter of that
        /// DISC (the key is DISC/SYS/LVL[/ZONE/LOC]): a counter below the minimum starts
        /// at the minimum, and a number past the maximum fails with
        /// <see cref="SeqFailureReason.RangeExhausted"/> — not wrapped, not written. With
        /// no range (null) behaviour is unchanged: 1 up to the pad capacity.
        /// </summary>
        public static SeqResult AssignNext(
            string seqKey,
            Dictionary<string, int> counters,
            string tagBody,
            string tagSuffix,
            SeqScheme scheme,
            int pad,
            string seqSchemeContext,
            int maxCollisionDepth,
            HashSet<string> existingTags,
            SeqBlockReservation reservation = null,
            (int Min, int Max)? range = null)
        {
            if (counters == null) throw new ArgumentNullException(nameof(counters));
            tagBody ??= string.Empty;
            tagSuffix ??= string.Empty;

            if (!counters.TryGetValue(seqKey, out int currentSeqVal))
            {
                currentSeqVal = 0;
                counters[seqKey] = 0;
            }

            int preIncrementValue = currentSeqVal;
            counters[seqKey] = FloorForRange(currentSeqVal, range);

            // Server-reserved block, when one was granted for this key. Taking the
            // number from the reservation is what makes Revit and StingBridge
            // safe to run against the same key concurrently: the server bumped
            // and returned the counter in one indivisible step, so no other host
            // can be holding the same value. We still advance the local counter
            // to the taken number so the existing rollback, overflow and
            // collision logic below is unchanged, and so the later /seq/sync
            // max-merge reports a high-water mark that reflects what we used.
            //
            // reservation == null (no server configured, or the reserve call
            // failed) falls through to purely local allocation — today's exact
            // behaviour, including its cross-host duplicate window. See the
            // remarks on SeqBlockReservation.
            if (reservation != null && reservation.TryTake(seqKey, out int reservedSeq))
            {
                if (!InRange(reservedSeq, range))
                {
                    counters[seqKey] = preIncrementValue;
                    return SeqResult.Fail(SeqFailureReason.ReservationOutsideRange, 0);
                }
                counters[seqKey] = reservedSeq;
            }
            else
            {
                counters[seqKey]++;
            }

            int padMax = MaxSeqForPad(pad);
            int maxSeq = range != null ? Math.Min(padMax, range.Value.Max) : padMax;
            bool rangeBinds = range != null && range.Value.Max < padMax;
            if (counters[seqKey] > maxSeq)
            {
                counters[seqKey] = preIncrementValue;          // rollback on overflow
                return SeqResult.Fail(rangeBinds ? SeqFailureReason.RangeExhausted : SeqFailureReason.InitialOverflow, 0);
            }

            string seq = BuildSeqString(counters[seqKey], scheme, pad, seqSchemeContext);
            string tag = tagBody + seq + tagSuffix;
            int collisionCount = 0;

            if (existingTags != null)
            {
                int safetyLimit = maxCollisionDepth;
                while (existingTags.Contains(tag) && safetyLimit-- > 0)
                {
                    collisionCount++;
                    counters[seqKey]++;
                    if (counters[seqKey] > maxSeq)
                    {
                        counters[seqKey] = preIncrementValue;  // rollback to pre-collision value
                        return SeqResult.Fail(rangeBinds ? SeqFailureReason.RangeExhausted : SeqFailureReason.CollisionOverflow, collisionCount);
                    }
                    seq = BuildSeqString(counters[seqKey], scheme, pad, seqSchemeContext);
                    tag = tagBody + seq + tagSuffix;
                }

                // Only a true exhaustion (limit spent AND still colliding) is a failure;
                // a tag that resolved on the final iteration is a success.
                if (safetyLimit <= 0 && existingTags.Contains(tag))
                {
                    counters[seqKey] = preIncrementValue;      // rollback counter
                    return SeqResult.Fail(SeqFailureReason.SafetyExhausted, collisionCount);
                }
            }

            return SeqResult.Ok(tag, seq, collisionCount);
        }
    }
}
