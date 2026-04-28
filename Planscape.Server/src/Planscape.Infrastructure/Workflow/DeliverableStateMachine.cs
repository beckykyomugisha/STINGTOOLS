using System.Text.Json;

namespace Planscape.Infrastructure.Workflow;

/// <summary>
/// Phase 145 — per-project deliverable state machine. Phase 146 added
/// <see cref="SemanticRole"/> mapping so custom flows that rename
/// SUBMITTED / ACCEPTED / REJECTED still get the metadata side-effects
/// (timestamp, actor name, rejection reason).
/// The canonical ISO 19650 flow is hard-coded in <see cref="Default"/>; a
/// tenant can override it by setting <c>Project.CustomDeliverableStateMachineJson</c>
/// to a JSON object of the shape:
///
///   {
///     "states":      ["DRAFT", "REVIEW", "APPROVED", "ARCHIVED"],
///     "initial":     "DRAFT",
///     "transitions": [
///       { "from": "DRAFT",    "to": "REVIEW" },
///       { "from": "REVIEW",   "to": "APPROVED" },
///       { "from": "REVIEW",   "to": "DRAFT" },
///       { "from": "APPROVED", "to": "ARCHIVED" }
///     ],
///     "terminal":    ["ARCHIVED"]
///   }
///
/// <see cref="LoadOrDefault"/> is forgiving: a malformed JSON, a missing key,
/// or an empty <c>transitions</c> array all fall back to the canonical flow
/// rather than locking the project out of transitioning anything. The
/// default flow's name is "ISO_19650_v1".
/// </summary>
public sealed class DeliverableStateMachine
{
    public string Name { get; init; } = "ISO_19650_v1";
    public IReadOnlyCollection<string> States { get; init; } = Array.Empty<string>();
    public string? InitialState { get; init; }
    public IReadOnlySet<(string From, string To)> Transitions { get; init; } = new HashSet<(string, string)>();
    public IReadOnlyCollection<string> TerminalStates { get; init; } = Array.Empty<string>();
    public bool IsCustom { get; init; }

    /// <summary>
    /// Phase 146 — semantic role assigned to each state. Lets custom
    /// machines that rename the canonical states (e.g. <c>UNDER_REVIEW</c>
    /// instead of <c>SUBMITTED</c>) still trigger the metadata side-effects
    /// inside <c>DeliverablesController.Transition</c>:
    ///   • <c>submitting</c> → stamp SubmittedAt / SubmittedBy / DocumentId
    ///   • <c>accepting</c>  → stamp AcceptedAt / AcceptedBy + clear rejection reason
    ///   • <c>rejecting</c>  → capture rejection reason
    ///   • <c>working</c> / <c>terminal</c> / <c>initial</c> / <c>none</c> → no side-effect
    /// Roles are case-insensitive and read from the state name in
    /// <see cref="Default"/>; in custom JSON they live under
    /// <c>"roles": { "STATE_NAME": "submitting", … }</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> SemanticRoles { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolve the semantic role for a state name, defaulting to "none".</summary>
    public string RoleOf(string state) =>
        SemanticRoles.TryGetValue(state?.ToUpperInvariant() ?? "", out var role) ? role : "none";

    public bool IsValidTransition(string current, string target) =>
        Transitions.Contains((current?.ToUpperInvariant() ?? "", target?.ToUpperInvariant() ?? ""));

    public bool IsTerminal(string state) =>
        TerminalStates.Contains(state?.ToUpperInvariant() ?? "");

    public IReadOnlyCollection<string> NextStates(string current) =>
        Transitions
            .Where(t => string.Equals(t.From, current, StringComparison.OrdinalIgnoreCase))
            .Select(t => t.To)
            .ToArray();

    /// <summary>Canonical ISO 19650 6-state flow (matches the original DeliverablesController logic).</summary>
    public static readonly DeliverableStateMachine Default = new()
    {
        Name = "ISO_19650_v1",
        States = new[] { "PENDING", "IN_PROGRESS", "SUBMITTED", "ACCEPTED", "REJECTED", "WAIVED" },
        InitialState = "PENDING",
        TerminalStates = new[] { "ACCEPTED", "WAIVED" },
        Transitions = new HashSet<(string, string)>
        {
            ("PENDING", "IN_PROGRESS"),
            ("PENDING", "WAIVED"),
            ("IN_PROGRESS", "SUBMITTED"),
            ("IN_PROGRESS", "WAIVED"),
            ("IN_PROGRESS", "PENDING"),
            ("SUBMITTED", "ACCEPTED"),
            ("SUBMITTED", "REJECTED"),
            ("REJECTED", "IN_PROGRESS"),
            ("REJECTED", "WAIVED"),
            ("WAIVED", "PENDING"),
        },
        SemanticRoles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PENDING"]     = "initial",
            ["IN_PROGRESS"] = "working",
            ["SUBMITTED"]   = "submitting",
            ["ACCEPTED"]    = "accepting",
            ["REJECTED"]    = "rejecting",
            ["WAIVED"]      = "terminal",
        },
    };

    /// <summary>
    /// Parse the JSONB blob from <c>Project.CustomDeliverableStateMachineJson</c>.
    /// Falls back to <see cref="Default"/> on null / empty / malformed input.
    /// All comparisons are case-insensitive — the parser uppercases incoming
    /// state names.
    /// </summary>
    public static DeliverableStateMachine LoadOrDefault(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Default;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Default;

            var states = ReadStringArray(root, "states");
            var transitions = new HashSet<(string, string)>();
            if (root.TryGetProperty("transitions", out var ts) && ts.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in ts.EnumerateArray())
                {
                    if (t.ValueKind != JsonValueKind.Object) continue;
                    var from = (t.TryGetProperty("from", out var fEl) ? fEl.GetString() : null)?.ToUpperInvariant();
                    var to = (t.TryGetProperty("to", out var tEl) ? tEl.GetString() : null)?.ToUpperInvariant();
                    if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) continue;
                    transitions.Add((from, to));
                }
            }

            // No transitions → unusable, fall back. A custom machine with no
            // arcs would lock every deliverable in its initial state.
            if (transitions.Count == 0) return Default;

            var terminal = ReadStringArray(root, "terminal");
            var initial = (root.TryGetProperty("initial", out var iEl) ? iEl.GetString() : null)?.ToUpperInvariant();
            var name = (root.TryGetProperty("name", out var nEl) ? nEl.GetString() : null) ?? "custom";

            // Phase 146 — per-state semantic roles. Object form
            // `{ "STATE_NAME": "submitting" }` maps custom names to the
            // canonical side-effect categories. Unknown role values are
            // ignored (no-op rather than 500).
            //
            // Phase 147 — when the JSON omits "roles" entirely, infer
            // them from canonical state names case-insensitively. A
            // tenant who only wants different transitions (not different
            // names) gets the canonical metadata side-effects for free.
            // States that aren't a canonical name keep "none". Explicit
            // "roles" always wins over inference.
            var roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var hasRolesBlock = root.TryGetProperty("roles", out var rolesEl)
                                && rolesEl.ValueKind == JsonValueKind.Object;
            if (hasRolesBlock)
            {
                foreach (var prop in rolesEl.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.String) continue;
                    var role = (prop.Value.GetString() ?? "").Trim().ToLowerInvariant();
                    if (KnownRoles.Contains(role))
                        roles[prop.Name.ToUpperInvariant()] = role;
                }
            }
            else
            {
                // Phase 147 — exact-match against CanonicalRoles for known
                // ISO 19650 names + common synonyms.
                // Phase 148 — substring keyword match as a fallback so
                // truly bespoke vocabularies (e.g. "ARCH_HAS_REVIEWED",
                // "ME_FINAL_APPROVAL") still get inferred roles. The
                // substring path runs only when the exact lookup misses.
                foreach (var s in states)
                {
                    if (CanonicalRoles.TryGetValue(s, out var canonical))
                    {
                        roles[s] = canonical;
                        continue;
                    }
                    var fuzzy = InferRoleByKeyword(s);
                    if (fuzzy != null) roles[s] = fuzzy;
                }
                // Inferred initial / terminal roles too — the canonical
                // dictionary covers them, but make sure we don't skip
                // states that only appear in transitions[] not states[].
                foreach (var (from, to) in transitions)
                {
                    if (!roles.ContainsKey(from))
                    {
                        if (CanonicalRoles.TryGetValue(from, out var rf)) roles[from] = rf;
                        else { var f = InferRoleByKeyword(from); if (f != null) roles[from] = f; }
                    }
                    if (!roles.ContainsKey(to))
                    {
                        if (CanonicalRoles.TryGetValue(to, out var rt)) roles[to] = rt;
                        else { var f = InferRoleByKeyword(to); if (f != null) roles[to] = f; }
                    }
                }
            }

            return new DeliverableStateMachine
            {
                Name = name,
                States = states,
                InitialState = initial,
                Transitions = transitions,
                TerminalStates = terminal,
                SemanticRoles = roles,
                IsCustom = true,
            };
        }
        catch
        {
            // Any parse failure → fall back. Caller surfaces the JSON
            // problem to the user via the GET /state-machine endpoint
            // which calls Default-vs-custom comparison.
            return Default;
        }
    }

    private static readonly HashSet<string> KnownRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "initial", "working", "submitting", "accepting", "rejecting", "terminal", "none",
    };

    /// <summary>
    /// Phase 147 — canonical state-name to role lookup used when a custom
    /// JSON omits a <c>"roles"</c> block. Lets a project that just
    /// reorders transitions inherit the canonical metadata side-effects
    /// without having to enumerate every role explicitly. Custom state
    /// names that aren't on this list stay role-less unless the JSON
    /// declares them.
    /// </summary>
    private static readonly Dictionary<string, string> CanonicalRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PENDING"]     = "initial",
        ["IN_PROGRESS"] = "working",
        ["IN-PROGRESS"] = "working",
        ["INPROGRESS"]  = "working",
        ["WIP"]         = "working",
        ["DRAFT"]       = "working",
        ["SUBMITTED"]   = "submitting",
        ["FOR_REVIEW"]  = "submitting",
        ["UNDER_REVIEW"] = "submitting",
        ["IN_REVIEW"]   = "submitting",
        ["ACCEPTED"]    = "accepting",
        ["APPROVED"]    = "accepting",
        ["PUBLISHED"]   = "accepting",
        ["REJECTED"]    = "rejecting",
        ["DECLINED"]    = "rejecting",
        ["RETURNED"]    = "rejecting",
        ["WAIVED"]      = "terminal",
        ["ARCHIVED"]    = "terminal",
        ["CLOSED"]      = "terminal",
    };

    /// <summary>
    /// Phase 148 — substring-keyword role inference for state names that
    /// don't appear in <see cref="CanonicalRoles"/>. Used only when the
    /// custom JSON omits a <c>"roles"</c> block AND the state name has no
    /// canonical alias, so it never overrides explicit caller intent.
    ///
    /// Priority order is action-specific → generic. The "outcome" roles
    /// (rejecting / accepting) win over "in-flight" roles (submitting /
    /// working) so a name like <c>FINAL_REVIEW_REJECTED</c> resolves to
    /// rejecting rather than submitting. Returns null when no keyword
    /// matches — caller leaves the role unset and <see cref="RoleOf"/>
    /// will report "none" for that state.
    /// </summary>
    internal static string? InferRoleByKeyword(string state)
    {
        if (string.IsNullOrWhiteSpace(state)) return null;
        var s = state.ToUpperInvariant();

        // 1. Rejecting — strongest "negative outcome" signal first.
        foreach (var kw in RejectKeywords) if (s.Contains(kw)) return "rejecting";

        // 2. Accepting — strongest "positive outcome" signal.
        foreach (var kw in AcceptKeywords) if (s.Contains(kw)) return "accepting";

        // 3. Submitting — "in review" / "for review" precedes acceptance.
        foreach (var kw in SubmitKeywords) if (s.Contains(kw)) return "submitting";

        // 4. Terminal — archival / closure verbs.
        foreach (var kw in TerminalKeywords) if (s.Contains(kw)) return "terminal";

        // 5. Working — broad "in progress" signal. Last among the
        // "actively-doing-something" buckets so it doesn't shadow more
        // specific outcomes.
        foreach (var kw in WorkingKeywords) if (s.Contains(kw)) return "working";

        // 6. Initial — broadest "queued / not started" signal. Last
        // overall because words like "OPEN" can appear in compound names
        // (e.g. RE-OPENED) where another bucket is more accurate.
        foreach (var kw in InitialKeywords) if (s.Contains(kw)) return "initial";

        return null;
    }

    // Phase 148 — keyword vocabularies tuned to common ISO 19650 / NEC /
    // JCT terminology. Order within each list is most-specific → most-
    // generic so the substring match doesn't fire on a less-meaningful
    // prefix. All entries are uppercase; <see cref="InferRoleByKeyword"/>
    // upper-cases the input once before the scan.
    private static readonly string[] RejectKeywords =
        { "REJECT", "DECLIN", "RETURN", "REWORK", "FAIL", "VOID" };
    private static readonly string[] AcceptKeywords =
        { "ACCEPT", "APPROV", "PUBLISH", "SIGNED_OFF", "SIGNOFF", "PASSED" };
    private static readonly string[] SubmitKeywords =
        { "SUBMIT", "REVIEW", "ISSUED_FOR", "FOR_INFORMATION", "FOR_APPROVAL", "FOR_COMMENT" };
    private static readonly string[] TerminalKeywords =
        { "ARCHIV", "CLOSED", "CANCELL", "WAIVE", "SUPERSED", "COMPLETE", "FINAL", "DONE" };
    private static readonly string[] WorkingKeywords =
        { "PROGRESS", "WIP", "DRAFT", "ACTIVE", "BUILD", "ONGOING" };
    private static readonly string[] InitialKeywords =
        { "PENDING", "BACKLOG", "TODO", "NEW", "QUEUED", "OPEN", "BRIEF" };

    private static string[] ReadStringArray(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        return arr.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!.ToUpperInvariant())
            .ToArray();
    }
}
