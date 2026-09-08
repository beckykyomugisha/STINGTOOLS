// ══════════════════════════════════════════════════════════════════════════
//  IssueStatusNormalizer.cs — one canonical issue-status vocabulary. PM-1 helper.
//
//  The audit found the issue-status string diverging across four subsystems:
//    "OPEN"  (BIMManager)            "Open" (Clash / ClashSlaIntegration)
//    "open"  (ACC / AccIssueSync)    "Resolved" / "Void" (KPI / KutKpiDashboard)
//  …and the workflow gate `has_open_issues` matched only "OPEN", so it never saw
//  clash / ACC issues. Every status read now normalises through here.
//
//  Pure (no Revit / no I/O) — unit-tested in StingTools.Cost.Tests.
// ══════════════════════════════════════════════════════════════════════════
using System;

namespace StingTools.Core
{
    /// <summary>
    /// The canonical issue states.
    ///
    /// <para><b>IM-9 added <see cref="Responded"/> and <see cref="Accepted"/> as their own
    /// kinds rather than folding them into existing ones.</b> The row proposed folding
    /// RESPONDED into Resolved; that would have been wrong. `Resolved` makes
    /// <see cref="IssueStatusNormalizer.IsOpen"/> false, and the codebase treats RESPONDED as
    /// STILL OPEN — `BIMManagerCommands` counts an issue open when its status is
    /// `OPEN || IN_PROGRESS || RESPONDED`, and the platform bridge maps it to "Active".
    /// Folding it into Resolved would have flipped `has_open_issues` and hidden every issue
    /// awaiting acceptance, which is the population the status exists to name.</para>
    ///
    /// <para>They also need round-tripping, not just classifying: both are exact-match
    /// filtered in several places (`status == "RESPONDED"` drives a "Bulk: Close All
    /// RESPONDED" action, and ACCEPTED appears in three terminal-state skip lists), so
    /// <see cref="IssueStatusNormalizer.Canonical"/> has to give each its own spelling back.
    /// Mapping them to an existing kind would have made canonicalising a stored row rewrite
    /// it into a value those filters no longer match.</para>
    /// </summary>
    public enum IssueStatusKind
    {
        Open,
        InProgress,
        /// <summary>A response has been provided and is awaiting acceptance. STILL OPEN.</summary>
        Responded,
        Resolved,
        /// <summary>The response was accepted; the issue is terminal. Grouped with CLOSED and
        /// VOID by every skip list in the codebase, and mapped to BCF/ACC "Resolved".</summary>
        Accepted,
        Closed,
        Void,
        Unknown,
    }

    public static class IssueStatusNormalizer
    {
        /// <summary>Map any of the four+ historical spellings to one canonical kind.</summary>
        public static IssueStatusKind Normalize(string raw)
        {
            string s = (raw ?? "").Trim().ToLowerInvariant().Replace("_", " ").Replace("-", " ");
            switch (s)
            {
                case "":            return IssueStatusKind.Unknown;
                case "open":
                case "new":
                case "reopened":
                case "re opened":   return IssueStatusKind.Open;
                case "in progress":
                case "inprogress":
                case "active":
                case "in review":
                case "review":
                case "assigned":    return IssueStatusKind.InProgress;
                // IM-9. "responded" is NOT "answered": answered/resolved means the work is
                // done, responded means a reply is on the table and somebody still has to
                // accept it. Keeping them apart is the whole point of the new kind.
                case "responded":   return IssueStatusKind.Responded;
                case "accepted":    return IssueStatusKind.Accepted;
                case "resolved":
                case "fixed":
                case "answered":    return IssueStatusKind.Resolved;
                case "closed":
                case "done":
                case "completed":
                case "verified":    return IssueStatusKind.Closed;
                case "void":
                case "cancelled":
                case "canceled":
                case "rejected":
                case "not an issue":
                case "wontfix":
                case "won t fix":   return IssueStatusKind.Void;
                default:
                    // Unknown spellings: treat anything containing "open" as Open so
                    // the gate fails safe (sees a possible open issue) rather than
                    // silently ignoring it.
                    return s.Contains("open") ? IssueStatusKind.Open : IssueStatusKind.Unknown;
            }
        }

        /// <summary>True when the issue still needs attention.
        ///
        /// <para>Open, In-progress and <b>Responded</b>. Responded is open because a reply
        /// awaiting acceptance is not finished work — which is exactly how
        /// `BIMManagerCommands` already counted it (`OPEN || IN_PROGRESS || RESPONDED`) while
        /// this normalizer was answering Unknown. The result was right and the reason was
        /// wrong: Unknown is also treated as open, so the gate happened to fail safe.</para>
        ///
        /// <para>Accepted is NOT open. Three separate skip lists in the codebase already
        /// group it with CLOSED and VOID, and both the BCF and platform bridges map it to
        /// "Resolved".</para>
        ///
        /// <para>Unknown stays open so the `has_open_issues` gate fails safe on a spelling
        /// nobody has taught this class yet.</para></summary>
        public static bool IsOpen(string raw)
        {
            var k = Normalize(raw);
            return k == IssueStatusKind.Open
                || k == IssueStatusKind.InProgress
                || k == IssueStatusKind.Responded
                || k == IssueStatusKind.Unknown;
        }

        /// <summary>True when the issue is in a TERMINAL state — no further action is
        /// expected. Closed, Void and Accepted.
        ///
        /// <para>Added with IM-9 because the codebase had this concept written out by hand in
        /// three places as `status == "CLOSED" || status == "VOID" || status == "ACCEPTED"`,
        /// and a fourth as `Closed || Void` that predates ACCEPTED and therefore misses it.
        /// One fact in four places is how ACCEPTED came to be terminal in some code paths and
        /// not others.</para></summary>
        public static bool IsTerminal(string raw) => IsTerminal(Normalize(raw));

        public static bool IsTerminal(IssueStatusKind kind)
            => kind == IssueStatusKind.Closed
            || kind == IssueStatusKind.Void
            || kind == IssueStatusKind.Accepted;

        /// <summary>The canonical UPPER_SNAKE spelling for persistence / display.</summary>
        public static string Canonical(string raw) => Canonical(Normalize(raw));

        public static string Canonical(IssueStatusKind kind)
        {
            switch (kind)
            {
                case IssueStatusKind.Open:        return "OPEN";
                case IssueStatusKind.InProgress:  return "IN_PROGRESS";
                case IssueStatusKind.Responded:   return "RESPONDED";
                case IssueStatusKind.Accepted:    return "ACCEPTED";
                case IssueStatusKind.Resolved:    return "RESOLVED";
                case IssueStatusKind.Closed:      return "CLOSED";
                case IssueStatusKind.Void:        return "VOID";
                default:                          return "UNKNOWN";
            }
        }
    }
}
