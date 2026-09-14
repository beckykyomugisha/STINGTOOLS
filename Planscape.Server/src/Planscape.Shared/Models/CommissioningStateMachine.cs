namespace Planscape.Shared.Commissioning;

// ══════════════════════════════════════════════════════════════════════════
//  The commissioning state machine — ONE copy, compiled into both the Revit
//  plugin and the server.
//
//  WHY IT LIVES HERE
//  -----------------
//  The rules were written once, in StingTools/V6/QRCommissioningWorkflow.cs, and
//  the phone had no way to reach them: Planscape.Server had no commissioning
//  controller and no commissioning entity, so a scan on site could not advance
//  anything. Building the server half meant either sharing these rules or writing
//  them twice.
//
//  Twice is how they drift, and the drift would be invisible: a desktop that
//  refuses a skip-state transition and an API that allows it produce a handover
//  record nobody can reconcile, months later, with no error anywhere. So the rules
//  live in Planscape.Shared, which the plugin already ProjectReferences — no
//  cross-project <Compile Include> hack, unlike BcfEngine.
//
//  Everything here is Revit-free and storage-free on purpose. It decides; callers
//  persist. The plugin writes COMM_* parameters onto an Element; the server writes
//  a CommissioningRecord row. Neither decision differs.
// ══════════════════════════════════════════════════════════════════════════

/// <summary>Why a transition was refused. Callers branch on this rather than on
/// message text — a string comparison is how a refusal quietly stops being
/// detected when someone rewords it.</summary>
public enum CommissioningRefusal
{
    None = 0,
    /// <summary>The target state is not one of <see cref="CommissioningStates.All"/>.</summary>
    UnknownState,
    /// <summary>Going backwards. Commissioning is a ratchet.</summary>
    Regression,
    /// <summary>The element is ALREADY in the requested state.
    ///
    /// Separated from <see cref="Regression"/> because it means something different
    /// and needs a different sentence. The case that matters is two operatives
    /// recording the same step offline: the second drain is not someone trying to
    /// rewind the record, it is a duplicate of work already captured, and telling
    /// them "refusing to regress" is both wrong and alarming.</summary>
    AlreadyInState,
    /// <summary>Skipping a step. Each state is evidence the previous one happened.</summary>
    SkippedState,
    /// <summary>No operative named. An unattributed sign-off is not a sign-off.</summary>
    MissingOperative,
    /// <summary>COMMISSIONED needs a witness.</summary>
    MissingWitness,
}

public static class CommissioningStates
{
    /// <summary>The ladder, in order. Index IS the rank — do not reorder, and add
    /// only at the end: a stored state is compared against this list by name, so a
    /// reorder silently changes what "advance" means for every existing record.</summary>
    public static readonly string[] All =
    {
        "NOT_STARTED", "RECEIVED", "INSTALLED", "TESTED", "COMMISSIONED", "HANDOVER"
    };

    public const string NotStarted = "NOT_STARTED";
    public const string Commissioned = "COMMISSIONED";
    public const string Handover = "HANDOVER";

    /// <summary>Rank of a state, or -1 when it is not one of ours.
    ///
    /// Null/empty means NOT_STARTED (rank 0) — an element that has never been
    /// touched has no parameter value. An UNRECOGNISED string is -1, NOT 0: the old
    /// implementation mapped anything unknown to 0, so a typo or a value written by
    /// a future version read as "not started" and could be silently advanced over.
    /// </summary>
    public static int RankOf(string? state)
    {
        if (string.IsNullOrWhiteSpace(state)) return 0;
        for (int i = 0; i < All.Length; i++)
            if (string.Equals(All[i], state, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    public static bool IsKnown(string? state) => RankOf(state) >= 0;

    /// <summary>The state after <paramref name="current"/>, clamped at HANDOVER.
    /// Returns null for an unrecognised current state — there is no sensible "next"
    /// from a value we do not understand, and guessing one is how bad data spreads.</summary>
    public static string? Next(string? current)
    {
        int i = RankOf(current);
        if (i < 0) return null;
        return All[Math.Min(i + 1, All.Length - 1)];
    }

    /// <summary>True when the element has reached the end of the ladder.</summary>
    public static bool IsTerminal(string? state) => RankOf(state) == All.Length - 1;
}

/// <summary>What a caller is asking for.</summary>
public sealed class CommissioningRequest
{
    /// <summary>Target state. Null or empty means "advance one step".</summary>
    public string? RequestedState { get; set; }
    public string? Operative { get; set; }
    public string? Witness { get; set; }
    public string? Notes { get; set; }
}

public sealed class CommissioningDecision
{
    public bool Ok { get; init; }
    public string FromState { get; init; } = CommissioningStates.NotStarted;
    public string? ToState { get; init; }
    public CommissioningRefusal Refusal { get; init; }
    /// <summary>A sentence for a human. Never the thing to branch on.</summary>
    public string? Reason { get; init; }

    public static CommissioningDecision Allow(string from, string to) =>
        new() { Ok = true, FromState = from, ToState = to };

    public static CommissioningDecision Refuse(
        string from, string? to, CommissioningRefusal why, string reason) =>
        new() { Ok = false, FromState = from, ToState = to, Refusal = why, Reason = reason };
}

public static class CommissioningStateMachine
{
    /// <summary>Decide whether a transition is allowed. Writes nothing.
    ///
    /// The rules, and why each exists:
    ///   - no regression   : commissioning records what HAPPENED. Rewinding it
    ///                       destroys the only evidence that it did.
    ///   - no skipping     : each state is evidence the previous one occurred.
    ///                       TESTED without INSTALLED is a claim about an asset
    ///                       nobody has fitted.
    ///   - operative named : an unattributed sign-off is not a sign-off.
    ///   - witness at      : the point where the asset is declared fit for use is
    ///     COMMISSIONED     the one a second person must have seen.
    /// </summary>
    public static CommissioningDecision Decide(string? currentState, CommissioningRequest request)
    {
        request ??= new CommissioningRequest();

        int ci = CommissioningStates.RankOf(currentState);
        if (ci < 0)
        {
            // Unrecognised STORED state. Refuse rather than treat it as NOT_STARTED,
            // which would let a record written by a newer version be advanced over.
            return CommissioningDecision.Refuse(
                currentState ?? "", null, CommissioningRefusal.UnknownState,
                $"'{currentState}' is not a commissioning state this version knows. " +
                "It may have been written by a newer build; refusing to act on it.");
        }

        string from = CommissioningStates.All[ci];

        string? target = string.IsNullOrWhiteSpace(request.RequestedState)
            ? CommissioningStates.Next(from)
            : request.RequestedState.Trim().ToUpperInvariant();

        int ti = CommissioningStates.RankOf(target);
        if (ti < 0)
            return CommissioningDecision.Refuse(from, target, CommissioningRefusal.UnknownState,
                $"'{target}' is not a commissioning state. Valid: {string.Join(", ", CommissioningStates.All)}.");

        // QR-13 — asked for the state it is already in. This is the offline
        // double-sign-off: two operatives both scanned at INSTALLED, both recorded
        // TESTED, and the second one drains after the first has landed. It is a
        // duplicate, NOT an attempt to rewind, and it gets its own refusal so the
        // client can say "already recorded" rather than "refusing to regress".
        //
        // Still a refusal, not a silent success: a caller must never report
        // "advanced" for a step that changed nothing.
        if (ti == ci && ci > 0)
            return CommissioningDecision.Refuse(from, target, CommissioningRefusal.AlreadyInState,
                $"Already {from}. Someone may have recorded this step already — "
                + "commissioning does not repeat a state.");

        if (ti < ci)
            return CommissioningDecision.Refuse(from, target, CommissioningRefusal.Regression,
                $"Refusing to regress from {from} to {target}.");

        if (ti - ci > 1)
            return CommissioningDecision.Refuse(from, target, CommissioningRefusal.SkippedState,
                $"Cannot go {from} to {target} in one step — each state is evidence the previous one happened. " +
                $"Next is {CommissioningStates.Next(from)}.");

        if (string.IsNullOrWhiteSpace(request.Operative))
            return CommissioningDecision.Refuse(from, target, CommissioningRefusal.MissingOperative,
                "An operative must be named — an unattributed sign-off is not a sign-off.");

        if (string.Equals(target, CommissioningStates.Commissioned, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(request.Witness))
            return CommissioningDecision.Refuse(from, target, CommissioningRefusal.MissingWitness,
                "COMMISSIONED requires a witness — this is the point the asset is declared fit for use.");

        return CommissioningDecision.Allow(from, target!);
    }
}
