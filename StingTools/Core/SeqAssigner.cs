using System;
using System.Collections.Generic;

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
        SafetyExhausted
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
        /// Empty/placeholder LOC normalises to BLD1 to match PopulateAll.
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
                if (string.IsNullOrEmpty(locPart) || locPart == "XX") locPart = "BLD1";
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
                case SeqScheme.ZonePrefix:
                    string zPrefix = !string.IsNullOrEmpty(zoneOrDisc) && zoneOrDisc.Length >= 2
                        ? zoneOrDisc.Substring(0, 2)
                        : "Z1";
                    return $"{zPrefix}-{n.ToString().PadLeft(pad, '0')}";
                case SeqScheme.DiscPrefix:
                    string dPrefix = !string.IsNullOrEmpty(zoneOrDisc) ? zoneOrDisc : "X";
                    return $"{dPrefix}-{n.ToString().PadLeft(pad, '0')}";
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
            HashSet<string> existingTags)
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
            counters[seqKey]++;

            int maxSeq = MaxSeqForPad(pad);
            if (counters[seqKey] > maxSeq)
            {
                counters[seqKey] = preIncrementValue;          // rollback on overflow
                return SeqResult.Fail(SeqFailureReason.InitialOverflow, 0);
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
                        return SeqResult.Fail(SeqFailureReason.CollisionOverflow, collisionCount);
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
