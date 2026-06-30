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
    public enum IssueStatusKind { Open, InProgress, Resolved, Closed, Void, Unknown }

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

        /// <summary>True when the issue still needs attention (Open or In-progress).
        /// Unknown is treated as open so the `has_open_issues` gate fails safe.</summary>
        public static bool IsOpen(string raw)
        {
            var k = Normalize(raw);
            return k == IssueStatusKind.Open || k == IssueStatusKind.InProgress || k == IssueStatusKind.Unknown;
        }

        /// <summary>The canonical UPPER_SNAKE spelling for persistence / display.</summary>
        public static string Canonical(string raw) => Canonical(Normalize(raw));

        public static string Canonical(IssueStatusKind kind)
        {
            switch (kind)
            {
                case IssueStatusKind.Open:        return "OPEN";
                case IssueStatusKind.InProgress:  return "IN_PROGRESS";
                case IssueStatusKind.Resolved:    return "RESOLVED";
                case IssueStatusKind.Closed:      return "CLOSED";
                case IssueStatusKind.Void:        return "VOID";
                default:                          return "UNKNOWN";
            }
        }
    }
}
