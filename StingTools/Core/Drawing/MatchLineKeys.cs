// StingTools — Drawing Template Manager · match-line keys (DTW-56)
//
// A match line is a detail curve that carries three keys: the pair key that
// lets the next run find it (STING_MATCH_LINE_GUID), the sheet it points to
// (STING_MATCH_REF) and its direction (STING_MATCH_DIR).
//
// Those keys were shared parameters. A real Revit 2025 run showed that the
// 'Lines' category reports AllowsBoundParameters = False, so they can never be
// bound to a detail line: 0 of 3 resolved on a new detail line, and Generate /
// Sync / Validate could not identify their own lines. The keys now live in
// Extensible Storage (Core/Storage/StingMatchLineSchema.cs). The parameters are
// still read as a fallback, for any element that carries them (a model line,
// say), and an unbound parameter is simply "no value".
//
// This file is the Revit-free half: which store wins, and how a stamped key
// is parsed and compared. It is <Compile Include>d by StingTools.Tags.Tests.

using System;
using System.Collections.Generic;

namespace StingTools.Core.Drawing
{
    /// <summary>Where a match line's keys were read from.</summary>
    public enum MatchLineKeySource
    {
        /// <summary>Nothing found: not a match line.</summary>
        None = 0,
        /// <summary>The Extensible Storage entity (the primary store).</summary>
        ExtensibleStorage = 1,
        /// <summary>The legacy STING_MATCH_* shared parameters.</summary>
        SharedParameters = 2,
    }

    /// <summary>The three keys a match-line curve carries, and where they came from.</summary>
    public sealed class MatchLineKeys
    {
        public static readonly MatchLineKeys Empty = new MatchLineKeys(null, null, null, MatchLineKeySource.None);

        public string PairGuid { get; }
        public string Ref { get; }
        public string Direction { get; }
        public MatchLineKeySource Source { get; }

        public MatchLineKeys(string pairGuid, string matchRef, string direction,
            MatchLineKeySource source = MatchLineKeySource.ExtensibleStorage)
        {
            PairGuid = Clean(pairGuid);
            Ref = Clean(matchRef);
            Direction = Clean(direction);
            Source = source;
        }

        /// <summary>True when the curve carries a pair key — the one thing that makes it
        /// a match line the engine can find again.</summary>
        public bool IsMatchLine => !string.IsNullOrEmpty(PairGuid);

        /// <summary>True when no key holds a value.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(PairGuid) && string.IsNullOrEmpty(Ref) && string.IsNullOrEmpty(Direction);

        /// <summary>The pair key with any dog-leg ":segN" suffix removed.</summary>
        public string BasePairKey => MatchLineKeyRules.BasePairKey(PairGuid);

        private static string Clean(string s)
        {
            if (s == null) return "";
            return s.Trim();
        }

        public override string ToString() => $"{Source}: guid='{PairGuid}' ref='{Ref}' dir='{Direction}'";
    }

    /// <summary>The decision rules for match-line keys.</summary>
    public static class MatchLineKeyRules
    {
        /// <summary>
        /// Which store describes a curve. The Extensible Storage record wins whenever it
        /// exists and is not empty — wholesale, not field by field: the writer stores all
        /// three keys at once, so an empty field there is the writer's answer (e.g.
        /// stamping.writeDirection off), and a stale parameter value from an older run
        /// must not fill it in. Otherwise the parameters are used when they hold anything.
        /// Either argument may be null (no entity / no bound parameters).
        /// </summary>
        public static MatchLineKeys Resolve(MatchLineKeys fromStorage, MatchLineKeys fromParameters)
        {
            if (fromStorage != null && !fromStorage.IsEmpty)
                return new MatchLineKeys(fromStorage.PairGuid, fromStorage.Ref, fromStorage.Direction,
                                         MatchLineKeySource.ExtensibleStorage);
            if (fromParameters != null && !fromParameters.IsEmpty)
                return new MatchLineKeys(fromParameters.PairGuid, fromParameters.Ref, fromParameters.Direction,
                                         MatchLineKeySource.SharedParameters);
            return MatchLineKeys.Empty;
        }

        /// <summary>
        /// The view-pair key a stamped key belongs to. Dog-leg segments are stamped
        /// "&lt;pair&gt;:segN"; only a trailing ":segN" (N an integer) is stripped,
        /// because the pair key itself contains colons.
        /// </summary>
        public static string BasePairKey(string stampedKey)
        {
            if (string.IsNullOrEmpty(stampedKey)) return stampedKey;
            int i = stampedKey.LastIndexOf(":seg", StringComparison.OrdinalIgnoreCase);
            if (i > 0 && int.TryParse(stampedKey.Substring(i + 4), out _))
                return stampedKey.Substring(0, i);
            return stampedKey;
        }

        /// <summary>The scope-pair GUID: the first colon-delimited field of a key.</summary>
        public static string ScopePairGuid(string key)
        {
            key = key ?? "";
            int sep = key.IndexOf(':');
            return sep > 0 ? key.Substring(0, sep) : key;
        }

        /// <summary>Two keys name the same pair (case-insensitive, segment suffix ignored).</summary>
        public static bool SamePair(string a, string b)
        {
            var x = BasePairKey(a);
            var y = BasePairKey(b);
            if (string.IsNullOrEmpty(x) || string.IsNullOrEmpty(y)) return false;
            return string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A two-sided pair is current when its curves' refs are exactly the two sheets
        /// it joins: each side's curve carries the OPPOSITE sheet's ref.
        /// </summary>
        public static bool RefsMatch(IEnumerable<string> curveRefs, string refA, string refB)
        {
            if (curveRefs == null) return false;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in curveRefs) seen.Add((r ?? "").Trim());
            if (seen.Count == 0) return false;
            return seen.Contains((refA ?? "").Trim()) && seen.Contains((refB ?? "").Trim());
        }
    }
}
