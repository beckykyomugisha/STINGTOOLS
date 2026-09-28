// CableCircuitIdentity — Revit-free rules for tying a cable-manifest record
// to the Revit ElectricalSystem (circuit) it belongs to.
//
// Why this exists (ROADMAP ELEC-7): consumers matched a manifest cable to a
// circuit by comparing StingCable.CircuitId with RBS_ELEC_CIRCUIT_NUMBER, but
// AddCableCommand writes CircuitId as "<SourceName>-<DestElementId>". The two
// never matched, so conduit auto-route routed 0 cables. Circuit numbers are
// also not unique across panels ("1" exists on every board), so even a
// number-shaped CircuitId picked an arbitrary panel's circuit 1.
//
// Resolution order (first unambiguous hit wins):
//   1. StoredElementId        — StingCable.CircuitElementId written by
//                               AddCable / a previous auto-route run.
//   2. SourceAndDestination   — the circuit whose loads include the cable's
//                               destination and whose base equipment is the
//                               cable's source. Destination comes from
//                               DestEquipmentId (UniqueId) or, for manifests
//                               written before that field was used, from the
//                               trailing element id of a legacy CircuitId.
//      DestinationOnly        — as above when the manifest names no source.
//   3. PanelAndCircuitNumber  — CircuitId equals a circuit number and
//                               PanelName matches one of the circuit's panel
//                               names.
//      CircuitNumberOnly      — CircuitId equals exactly ONE circuit number in
//                               the model and the manifest names no panel.
// More than one candidate at any step is reported as Ambiguous — never
// resolved by picking the first.
//
// This file must stay free of Autodesk.Revit.* so StingTools.Routing.Tests
// can compile it directly.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Electrical
{
    /// <summary>A circuit reduced to the facts identity resolution needs.</summary>
    public sealed class CircuitCandidate
    {
        public long SystemId { get; set; }
        public string CircuitNumber { get; set; } = "";
        /// <summary>Every name the feeding panel is known by (panel name, element name).</summary>
        public List<string> PanelNames { get; set; } = new List<string>();
        public string BaseEquipmentUniqueId { get; set; } = "";
        public List<string> MemberUniqueIds { get; set; } = new List<string>();
        public List<long> MemberElementIds { get; set; } = new List<long>();
    }

    /// <summary>The identity facts a manifest cable carries.</summary>
    public sealed class CableIdentityKey
    {
        public long CircuitElementId { get; set; }
        public string CircuitId { get; set; } = "";
        public string PanelName { get; set; } = "";
        public string SourceUniqueId { get; set; } = "";
        public string DestUniqueId { get; set; } = "";
    }

    public enum CircuitMatchMethod
    {
        None,
        StoredElementId,
        SourceAndDestination,
        DestinationOnly,
        PanelAndCircuitNumber,
        CircuitNumberOnly,
    }

    public sealed class CircuitMatch
    {
        public long SystemId { get; set; }
        public CircuitMatchMethod Method { get; set; } = CircuitMatchMethod.None;
        public bool Ambiguous { get; set; }
        public string Reason { get; set; } = "";
        public bool Found => Method != CircuitMatchMethod.None && !Ambiguous && SystemId > 0;

        internal static CircuitMatch Hit(long id, CircuitMatchMethod m) =>
            new CircuitMatch { SystemId = id, Method = m };
        internal static CircuitMatch Miss(string reason) =>
            new CircuitMatch { Reason = reason };
        internal static CircuitMatch Many(int n, string what) =>
            new CircuitMatch { Ambiguous = true, Reason = $"{n} circuits match {what} — refusing to guess" };
    }

    public static class CableCircuitIdentity
    {
        /// <summary>
        /// The display CircuitId AddCableCommand has always written:
        /// "&lt;source name with spaces as underscores&gt;-&lt;destination element id&gt;".
        /// Kept byte-for-byte so exports and existing manifests stay stable.
        /// </summary>
        public static string BuildLegacyCircuitId(string sourceName, long destElementId) =>
            $"{(sourceName ?? "").Replace(' ', '_')}-{destElementId}";

        /// <summary>
        /// Recovers the destination element id from a legacy CircuitId
        /// ("Panel_A-123456" → 123456). The id is the digits after the LAST
        /// '-', so source names containing '-' still parse. A bare number ("12")
        /// or a numeric multi-pole number ("1-3") is a circuit number, not a
        /// legacy id, and returns false.
        /// </summary>
        public static bool TryParseLegacyDestinationId(string circuitId, out long destElementId)
        {
            destElementId = 0;
            if (string.IsNullOrWhiteSpace(circuitId)) return false;
            int dash = circuitId.LastIndexOf('-');
            if (dash <= 0 || dash == circuitId.Length - 1) return false;
            string tail = circuitId.Substring(dash + 1);
            if (!tail.All(char.IsDigit)) return false;
            // A purely numeric prefix ("1-3") is a multi-pole circuit number,
            // not "<source name>-<element id>".
            if (circuitId.Substring(0, dash).All(ch => char.IsDigit(ch) || ch == '-' || ch == ',' || ch == ' '))
                return false;
            return long.TryParse(tail, out destElementId) && destElementId > 0;
        }

        public static CircuitMatch Resolve(CableIdentityKey key, IEnumerable<CircuitCandidate> candidates)
        {
            if (key == null) return CircuitMatch.Miss("no cable record");
            var all = (candidates ?? Enumerable.Empty<CircuitCandidate>()).Where(c => c != null).ToList();
            if (all.Count == 0) return CircuitMatch.Miss("the model has no electrical circuits");

            // 1. Stored element id.
            if (key.CircuitElementId > 0)
            {
                var hit = all.FirstOrDefault(c => c.SystemId == key.CircuitElementId);
                if (hit != null) return CircuitMatch.Hit(hit.SystemId, CircuitMatchMethod.StoredElementId);
                // Stale id (circuit deleted / renumbered into a new system):
                // fall through to the structural keys rather than failing.
            }

            // 2. Destination (+ source).
            bool hasDestUid = !string.IsNullOrWhiteSpace(key.DestUniqueId);
            bool hasLegacyDest = TryParseLegacyDestinationId(key.CircuitId, out long legacyDestId);
            if (hasDestUid || hasLegacyDest)
            {
                var byDest = all.Where(c =>
                        (hasDestUid && c.MemberUniqueIds.Any(u => string.Equals(u, key.DestUniqueId, StringComparison.Ordinal)))
                        || (hasLegacyDest && c.MemberElementIds.Contains(legacyDestId)))
                    .ToList();
                if (byDest.Count > 0)
                {
                    if (!string.IsNullOrWhiteSpace(key.SourceUniqueId))
                    {
                        var bySrc = byDest.Where(c => string.Equals(c.BaseEquipmentUniqueId,
                            key.SourceUniqueId, StringComparison.Ordinal)).ToList();
                        if (bySrc.Count == 1) return CircuitMatch.Hit(bySrc[0].SystemId, CircuitMatchMethod.SourceAndDestination);
                        if (bySrc.Count > 1) return CircuitMatch.Many(bySrc.Count, "this source and destination");
                        return CircuitMatch.Miss(
                            "the destination is on " + byDest.Count + " circuit(s), none fed from the manifest's source equipment " +
                            "(re-circuited since the cable was added?)");
                    }
                    if (byDest.Count == 1) return CircuitMatch.Hit(byDest[0].SystemId, CircuitMatchMethod.DestinationOnly);
                    return CircuitMatch.Many(byDest.Count, "this destination");
                }
                // Destination on no circuit: a number-shaped CircuitId can still
                // be tried below; a legacy "<name>-<id>" cannot be a number.
                if (hasLegacyDest)
                    return CircuitMatch.Miss("the destination equipment is not on any circuit");
            }

            // 3. Circuit number (+ panel).
            string num = (key.CircuitId ?? "").Trim();
            if (num.Length == 0) return CircuitMatch.Miss("the cable record carries no circuit reference");
            var byNum = all.Where(c => string.Equals((c.CircuitNumber ?? "").Trim(), num,
                StringComparison.OrdinalIgnoreCase)).ToList();
            if (byNum.Count == 0) return CircuitMatch.Miss($"no circuit numbered '{num}'");

            string panel = (key.PanelName ?? "").Trim();
            if (panel.Length > 0)
            {
                var byPanel = byNum.Where(c => c.PanelNames.Any(p =>
                    string.Equals((p ?? "").Trim(), panel, StringComparison.OrdinalIgnoreCase))).ToList();
                if (byPanel.Count == 1) return CircuitMatch.Hit(byPanel[0].SystemId, CircuitMatchMethod.PanelAndCircuitNumber);
                if (byPanel.Count > 1) return CircuitMatch.Many(byPanel.Count, $"panel '{panel}' circuit '{num}'");
                return CircuitMatch.Miss($"circuit '{num}' exists but not on panel '{panel}'");
            }
            if (byNum.Count == 1) return CircuitMatch.Hit(byNum[0].SystemId, CircuitMatchMethod.CircuitNumberOnly);
            return CircuitMatch.Many(byNum.Count, $"circuit number '{num}' across different panels");
        }

        /// <summary>Human wording for a match method, for command result text.</summary>
        public static string Describe(CircuitMatchMethod m)
        {
            switch (m)
            {
                case CircuitMatchMethod.StoredElementId:       return "stored circuit element id";
                case CircuitMatchMethod.SourceAndDestination:  return "source panel + destination equipment";
                case CircuitMatchMethod.DestinationOnly:       return "destination equipment";
                case CircuitMatchMethod.PanelAndCircuitNumber: return "panel + circuit number";
                case CircuitMatchMethod.CircuitNumberOnly:     return "circuit number (unique in model)";
                default:                                       return "unresolved";
            }
        }
    }
}
