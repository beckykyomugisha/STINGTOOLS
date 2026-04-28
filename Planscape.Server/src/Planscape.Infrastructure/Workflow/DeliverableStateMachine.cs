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
            // ignored (no-op rather than 500). If the JSON omits "roles"
            // entirely, the machine still works — every state just gets
            // the default "none" role and side-effects are skipped.
            var roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("roles", out var rolesEl) && rolesEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in rolesEl.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.String) continue;
                    var role = (prop.Value.GetString() ?? "").Trim().ToLowerInvariant();
                    if (KnownRoles.Contains(role))
                        roles[prop.Name.ToUpperInvariant()] = role;
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
