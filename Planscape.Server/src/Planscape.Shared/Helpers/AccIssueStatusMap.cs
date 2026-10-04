// AccIssueStatusMap.cs — the ONE mapping between STING / Planscape issue states and the
// Autodesk Construction Cloud Issues API (construction/issues/v1) status vocabulary.
//
// Compiled into Planscape.Shared (the server) and, through that project reference, used by
// the Revit plugin; StingTools.Acc.Tests links this file directly. Pure C#: no Revit, no I/O,
// no logging, so both sides run the same code and the tests exercise it.
//
// Source of the vocabulary (INT-12): the APS OpenAPI description of Issues v1,
//   https://github.com/autodesk-platform-services/aps-sdk-openapi/blob/main/construction/issues/Issues.yaml
//   (commit bbb74b00d650dd0807d349a1bce875f6065014ad, 2025-12-11), components.schemas.status:
//   enum draft, open, pending, in_progress, in_review, completed, not_approved, in_dispute, closed.
// The same file says the statuses AVAILABLE on a project come from GET users/me
// (issue.new.permittedStatuses) and, per issue, from permittedStatuses — so which of the nine a
// given container accepts is project configuration. Autodesk's 2023 status announcement
// (https://aps.autodesk.com/blog/acc-issues-status-ui-change) describes in_progress, completed,
// not_approved and in_dispute as optional statuses a project admin turns on.
//
// Values from the older BIM 360 issue workflow (answered, work_completed, ready_to_inspect,
// void, not_an_issue …) are NOT Issues v1 values. They are reported as Unknown, never guessed.
#nullable enable
using System;
using System.Collections.Generic;

namespace Planscape.Shared.Helpers
{
    /// <summary>What an ACC issue status means on the STING side.</summary>
    public enum AccIssueState
    {
        /// <summary>Needs action and nobody is on it yet: draft, open, not_approved, in_dispute.</summary>
        Open,
        /// <summary>Being worked or reviewed: pending, in_progress, in_review.</summary>
        InProgress,
        /// <summary>The assignee reports the work done; the issue is not closed until the
        /// creator closes it (completed). STILL OPEN.</summary>
        Responded,
        /// <summary>closed — the only terminal Issues v1 status.</summary>
        Closed,
        /// <summary>Absent, blank, or not an Issues v1 value. Never treated as open or closed.</summary>
        Unknown,
    }

    /// <summary>What a reconciliation should do with an escalation record, given its ACC status.</summary>
    public enum AccReconcileAction
    {
        /// <summary>Closed in ACC: stop tracking so a recurrence is raised again.</summary>
        Untrack,
        /// <summary>A known, still-open status: keep tracking.</summary>
        Keep,
        /// <summary>A status this map does not know: keep tracking AND report it.</summary>
        KeepUnknown,
    }

    public static class AccIssueStatusMap
    {
        /// <summary>The Issues v1 status enum, verbatim, in the order the API lists it.</summary>
        public static readonly IReadOnlyList<string> AccStatuses = new[]
        {
            "draft", "open", "pending", "in_progress", "in_review",
            "completed", "not_approved", "in_dispute", "closed",
        };

        /// <summary>The status STING sends when it creates an issue. "open" is the base
        /// status; the 2023 additions may be switched off on a project, and draft hides the
        /// issue from its assignee, so neither is sent.</summary>
        public const string CreateStatus = "open";

        /// <summary>True only for an exact Issues v1 value (case-insensitive, trimmed).</summary>
        public static bool IsAccStatus(string? accStatus)
        {
            string s = (accStatus ?? "").Trim().ToLowerInvariant();
            foreach (var v in AccStatuses) if (v == s) return true;
            return false;
        }

        /// <summary>ACC status → STING meaning. Every Issues v1 value is mapped on purpose;
        /// anything else is Unknown.</summary>
        public static AccIssueState FromAcc(string? accStatus)
        {
            switch ((accStatus ?? "").Trim().ToLowerInvariant())
            {
                case "draft":
                case "open":
                case "not_approved":   // the reported fix was rejected: back to the assignee
                case "in_dispute":     // the assignment is contested: unresolved
                    return AccIssueState.Open;
                case "pending":
                case "in_progress":
                case "in_review":
                    return AccIssueState.InProgress;
                case "completed":      // done per the assignee, not yet closed
                    return AccIssueState.Responded;
                case "closed":
                    return AccIssueState.Closed;
                default:
                    return AccIssueState.Unknown;
            }
        }

        /// <summary>True only when ACC says the issue is closed.</summary>
        public static bool IsClosed(string? accStatus) => FromAcc(accStatus) == AccIssueState.Closed;

        /// <summary>The reconciliation decision for one escalation whose ACC issue was found.</summary>
        public static AccReconcileAction ReconcileAction(string? accStatus)
        {
            var state = FromAcc(accStatus);
            if (state == AccIssueState.Closed) return AccReconcileAction.Untrack;
            if (state == AccIssueState.Unknown) return AccReconcileAction.KeepUnknown;
            return AccReconcileAction.Keep;
        }

        /// <summary>
        /// STING / Planscape status → the status to send when CREATING the issue in ACC, or
        /// null when the issue must not be pushed. Only a still-open issue is pushed, and it is
        /// created "open": an issue that is resolved, closed or void in STING has no reason to
        /// be raised in ACC, and a status STING does not recognise is refused rather than sent.
        /// Accepts the STING spellings (OPEN, IN_PROGRESS, RESPONDED, …) and the ACC ones.
        /// </summary>
        public static string? ToAccCreateStatus(string? stingStatus)
        {
            string s = (stingStatus ?? "").Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
            switch (s)
            {
                case "open":
                case "new":
                case "reopened":
                case "re_opened":
                case "in_progress":
                case "inprogress":
                case "active":
                case "assigned":
                case "in_review":
                case "review":
                case "pending":
                case "responded":
                    return CreateStatus;
                default:
                    return null;
            }
        }
    }
}
