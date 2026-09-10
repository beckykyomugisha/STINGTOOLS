// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccCommandOutcome.cs
//
// The last mile of the "a failure is not an empty result" fix: given an
// AccFetchResult, what does the operator see and does the command succeed?
//
// That decision used to live inside AccPullClashesCommand, which imports the
// Revit API and can therefore be linked into no test project. The whole point
// of the outcome split is that a workflow step FAILS instead of passing, and
// that branch was verified by reading only - the same class of gap the exercise
// is about. So the decision lives here: Revit-free and log-free, linked into
// StingTools.Acc.Tests, with the Revit commands keeping only their shell.
//
// One function, two callers (AccPullClashesCommand + AccSyncIssueStatusCommand),
// so the two cannot drift into describing the same failure differently.

using System;

namespace StingTools.V6
{
    /// <summary>What a command should do about a completed ACC read. Stands in for
    /// Autodesk.Revit.UI.Result, which cannot be named from a Revit-free file.</summary>
    public enum AccCommandVerdict
    {
        /// <summary>The read succeeded and returned data - carry on.</summary>
        Proceed = 0,
        /// <summary>The read succeeded and there was genuinely nothing. Report it plainly
        /// and return Result.Succeeded: an empty container, a clash-clean model set and a
        /// station with nothing commissioned are all real, expected states.</summary>
        SucceededEmpty = 1,
        /// <summary>The read did not succeed. Return Result.Failed so a workflow step
        /// cannot record work it never did.</summary>
        Failed = 2,
    }

    public static class AccCommandOutcome
    {
        /// <summary>Map a fetch status to what the command should do.
        ///
        /// The discard arm THROWS rather than defaulting. A permissive `_ => Failed`
        /// would compile, pass every test, and silently decide the behaviour of a status
        /// nobody thought about - which is how a new member ends up with an unconsidered
        /// meaning. Throwing means AccCommandOutcomeTests, which enumerates
        /// Enum.GetValues, goes red the moment a member is added without a decision.
        /// (An arm-less switch expression would do the same, but warns CS8524 about
        /// out-of-range casts, and this build is 0-warning.)</summary>
        public static AccCommandVerdict Verdict(AccFetchStatus status) => status switch
        {
            AccFetchStatus.Ok => AccCommandVerdict.Proceed,
            AccFetchStatus.EmptyOk => AccCommandVerdict.SucceededEmpty,
            AccFetchStatus.AuthFailed => AccCommandVerdict.Failed,
            AccFetchStatus.NotFound => AccCommandVerdict.Failed,
            AccFetchStatus.TransportFailed => AccCommandVerdict.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status,
                "Unhandled AccFetchStatus: every status needs an explicit command verdict. " +
                "Add one here rather than letting it take a default meaning."),
        };

        /// <summary>True when the command must stop and report a failure.</summary>
        public static bool IsFailure(AccFetchStatus status) => Verdict(status) == AccCommandVerdict.Failed;

        /// <summary>The operator-facing message for a read that did NOT succeed.
        ///
        /// It names the failure kind, the reason and the container that was used, and it
        /// opens by saying nothing was checked. What it must NEVER do is read like an
        /// empty result: the words "clash-clean", "no clashes" and "not found" are absent
        /// by construction and asserted absent by test, because each of them turns a
        /// failed read into a coordinator's belief that ACC lost the data.</summary>
        public static string FailureMessage(string what, AccFetchStatus status, int httpStatus,
            string detail, string containerId)
        {
            if (!IsFailure(status))
                throw new ArgumentException(
                    $"FailureMessage called for {status}, which is not a failure. " +
                    "A succeeding read must be reported by its own branch, not by this one.",
                    nameof(status));

            string reason = string.IsNullOrWhiteSpace(detail)
                ? AccFetchOutcome.Describe(status, httpStatus)
                : detail;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Could not read {what} from ACC. NOTHING WAS CHECKED — this is not a clean result.");
            sb.AppendLine();
            sb.AppendLine($"Failure: {status}");
            sb.AppendLine($"Reason:  {reason}");
            sb.AppendLine($"Container id used: {(string.IsNullOrEmpty(containerId) ? "(none)" : containerId)}");
            sb.AppendLine();
            sb.AppendLine(Remedy(status));
            return sb.ToString();
        }

        /// <summary>What to do about each failure kind. Separate from the message so a test
        /// can assert every failure kind has advice, not just a label.</summary>
        public static string Remedy(AccFetchStatus status) => status switch
        {
            AccFetchStatus.AuthFailed =>
                "Sign in to Autodesk again (BIM Coordination Center → Platforms → ACC), then confirm " +
                "the app's Client ID/Secret and that the account can see this project.",
            AccFetchStatus.NotFound =>
                "Check the container id. ProjectId is the Issues container; set CoordContainerId when " +
                "the Model Coordination container differs. If both are correct, the service sub-path " +
                "has changed and needs confirming against APS.",
            AccFetchStatus.TransportFailed =>
                "Check network access to developer.api.autodesk.com and retry. If the payload shape " +
                "has changed, the service sub-path needs confirming against APS.",
            AccFetchStatus.Ok => "No action — the request succeeded.",
            AccFetchStatus.EmptyOk => "No action — the request succeeded and there was nothing to return.",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status,
                "Unhandled AccFetchStatus: every status needs remedy text, or an operator is " +
                "told what failed and not what to do."),
        };
    }
}
