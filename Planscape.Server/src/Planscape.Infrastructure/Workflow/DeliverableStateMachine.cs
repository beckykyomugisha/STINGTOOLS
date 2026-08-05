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

    /// <summary>
    /// Phase 149 — tenant-supplied keyword extensions of the role-
    /// inference vocabulary. Shape: <c>{ "working": ["WAITING_ON_X", "PARKED"], … }</c>.
    /// Caller-defined keywords take priority over the built-ins (so
    /// <c>"working": ["LOCKED"]</c> can override the canonical
    /// <c>LOCKED → terminal</c> mapping for a project that uses LOCKED
    /// to mean "engineer has reserved this row for editing"). Built-in
    /// vocabularies stay as the universal fallback.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> CustomKeywords { get; init; } =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True when the source JSON supplied an explicit <c>"roles"</c> block.
    /// <see cref="RoleOf"/> then returns "none" for any state that block did
    /// not name, instead of inferring one. See the comment in RoleOf.
    /// </summary>
    public bool RoleInferenceDisabled { get; init; }

    /// <summary>
    /// Resolve the semantic role for a state name. O(1) for any state
    /// declared in the machine (loader pre-populates roles for every
    /// state in <c>states[]</c> plus every endpoint of
    /// <c>transitions[]</c>). Falls back to memoised inference for
    /// unknown states.
    /// </summary>
    public string RoleOf(string state)
    {
        var key = state?.ToUpperInvariant() ?? "";
        if (SemanticRoles.TryGetValue(key, out var role)) return role;

        // An explicit "roles" block is a closed statement of intent: the author
        // enumerated the states that carry a semantic role, so anything absent
        // is deliberately role-less. Falling through to inference here would
        // silently re-add roles they left out — e.g. "REJECTED_BY_QA" inferring
        // "rejecting" and firing the rejection-reason side-effect on a flow that
        // never asked for it. The loader disables inference in that case; only
        // machines built WITHOUT a roles block infer.
        if (RoleInferenceDisabled) return "none";
        // Phase 149 — memoised on-demand inference for state names the
        // caller didn't pre-declare. Keeps RoleOf O(1) amortised even
        // when the controller queries an unknown state. Each machine
        // instance carries its own cache so tenant CustomKeywords don't
        // cross-pollinate across projects.
        return _runtimeRoleCache.GetOrAdd(key, k => InferRoleWithExtensions(k) ?? "none");
    }

    // Phase 149 introduced runtime memoisation; Phase 150 made it
    // bounded. Phase 151 stripes it: 8 stripes × 32 entries each = 256
    // total entries (matches the prior cap), but now concurrent
    // GetOrAdd calls on different keys don't contend on the same lock.
    // The cap is generous — state-machine use cases rarely see more
    // than a few dozen unique state names per project.
    private const int RoleCacheCapacity = 256;
    private const int RoleCacheStripes = 8;
    private readonly StripedBoundedLruCache<string, string> _runtimeRoleCache =
        new(RoleCacheCapacity, RoleCacheStripes, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Phase 149 — instance-level inference that consults caller-supplied
    /// <see cref="CustomKeywords"/> first, then falls through to the
    /// built-in priority-ordered vocabulary.
    /// </summary>
    private string? InferRoleWithExtensions(string state)
    {
        if (string.IsNullOrWhiteSpace(state)) return null;
        var s = state.ToUpperInvariant();
        // Custom keywords win — apply them in canonical priority order
        // (rejecting > accepting > submitting > terminal > working >
        // initial) so a tenant's bespoke vocab inherits the same
        // outcome-beats-in-flight semantics as the built-in path.
        foreach (var role in RolePriority)
        {
            if (CustomKeywords.TryGetValue(role, out var keywords))
            {
                foreach (var kw in keywords)
                {
                    if (string.IsNullOrWhiteSpace(kw)) continue;
                    if (s.Contains(kw, StringComparison.OrdinalIgnoreCase)) return role;
                }
            }
        }
        return InferRoleByKeyword(s);
    }

    private static readonly string[] RolePriority =
        { "rejecting", "accepting", "submitting", "terminal", "working", "initial" };

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
    public static DeliverableStateMachine LoadOrDefault(string? json) =>
        LoadOrDefault(json, platformKeywords: null);

    /// <summary>
    /// Phase 150 — overload accepting platform-wide keyword extensions.
    /// Project-level <c>"keywords"</c> wins; platform sits below it but
    /// above the built-in vocabulary. <paramref name="platformKeywords"/>
    /// can be null to skip the layer (matches the legacy single-arg
    /// behaviour).
    /// </summary>
    public static DeliverableStateMachine LoadOrDefault(
        string? json,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? platformKeywords)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            // Even the default machine can benefit from platform-wide
            // keyword extensions (e.g. "FROZEN" common across tenants
            // but absent from the canonical machine). When platform
            // keywords are present, return a copy of Default with that
            // layer attached so RoleOf can consult it on cache miss.
            return platformKeywords == null || platformKeywords.Count == 0
                ? Default
                : new DeliverableStateMachine
                {
                    Name = Default.Name,
                    States = Default.States,
                    InitialState = Default.InitialState,
                    Transitions = Default.Transitions,
                    TerminalStates = Default.TerminalStates,
                    SemanticRoles = Default.SemanticRoles,
                    CustomKeywords = platformKeywords,
                };
        }

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

            // Phase 149 — optional `keywords` block: tenant extensions of
            // the built-in inference vocabulary. Shape:
            //   { "keywords": { "working": ["WAITING_ON_X", "PARKED"], … } }
            // Only the six canonical role buckets are recognised; anything
            // else is silently ignored (so a typo doesn't 500). Declared
            // states ARE re-resolved against these keywords below — see the
            // hasRolesBlock guard there. This comment previously said they were
            // not, and told tenants to duplicate the mapping into an explicit
            // `roles` block; that workaround is no longer needed.
            // Phase 150 — merge platform-wide keywords below the
            // project's own. Project entries win on key collisions so
            // a tenant can still override a platform default.
            var customKeywords = MergeKeywordLayers(
                ParseCustomKeywords(root),  // project — highest priority
                platformKeywords);          // platform — fallback
            // Resolve declared states against the tenant keywords now, so RoleOf
            // stays a cheap dict lookup for them.
            //
            // The guard is `hasRolesBlock`, NOT `roles.ContainsKey`. Both the
            // explicit "roles" block and the built-in inference above write into
            // the same dictionary, so ContainsKey could not tell them apart —
            // and because the inference pass had already claimed every declared
            // state, a tenant keyword could never take effect on one. That made
            // the documented promise on CustomKeywords ("working": ["LOCKED"]
            // overrides the canonical LOCKED → terminal) false for exactly the
            // states a project declares, which is all of them in practice.
            //
            // Priority is explicit roles > tenant keywords > built-in inference.
            // hasRolesBlock distinguishes the top tier: when it is set, the
            // inference branch never ran, so every entry present is explicit and
            // must be left alone. When it is not set, every entry came from
            // inference and a tenant keyword outranks it.
            if (customKeywords.Count > 0)
            {
                var allDeclared = new HashSet<string>(states, StringComparer.OrdinalIgnoreCase);
                foreach (var (from, to) in transitions) { allDeclared.Add(from); allDeclared.Add(to); }
                foreach (var declared in allDeclared)
                {
                    if (hasRolesBlock && roles.ContainsKey(declared)) continue;
                    var inferred = InferFromCustomKeywords(declared, customKeywords);
                    if (inferred != null) roles[declared] = inferred;
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
                CustomKeywords = customKeywords,
                IsCustom = true,
                RoleInferenceDisabled = hasRolesBlock,
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

    // Phase 154 — single source of truth on RoleBuckets.WithNone.
    // Includes "none" because the role-block validator on the loader
    // accepts "none" to explicitly suppress side-effects.
    private static IReadOnlySet<string> KnownRoles => RoleBuckets.WithNone;

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

        // 2. "Awaiting an outcome" phrases, BEFORE the outcome buckets.
        //
        // "ISSUED_FOR_APPROVAL" describes something sent out and waiting, not
        // something approved. But AcceptKeywords contains "APPROV", which is a
        // substring of it, so the accept scan below claimed the state first and
        // returned "accepting" — asserting the deliverable had been signed off
        // when it was still in someone's review queue. Same for
        // "ISSUED_FOR_CONSTRUCTION" and friends.
        //
        // SubmitKeywords already lists these phrases; they were simply
        // unreachable behind step 3. Hoisting the "FOR_"-style phrases (and
        // only those — bare "SUBMIT"/"REVIEW" stay at step 3) fixes the
        // precedence without weakening "APPROVED" → accepting.
        foreach (var kw in AwaitingPhrases) if (s.Contains(kw)) return "submitting";

        // 3. Accepting — strongest "positive outcome" signal.
        foreach (var kw in AcceptKeywords) if (s.Contains(kw)) return "accepting";

        // 3. Submitting — "in review" / "for review" precedes acceptance.
        foreach (var kw in SubmitKeywords) if (s.Contains(kw)) return "submitting";

        // 4. Blocked-but-live, BEFORE terminal.
        //
        // "BLOCKED" contains "LOCKED", which is a terminal keyword, so
        // "BLOCKED_BY_RFI" — a deliverable someone is actively chasing — was
        // classified "terminal" and dropped out of live reporting. Substring
        // scanning has no word boundaries, so the only fix is precedence.
        foreach (var kw in BlockedPhrases) if (s.Contains(kw)) return "working";

        // 5. Terminal — archival / closure verbs.
        foreach (var kw in TerminalKeywords) if (s.Contains(kw)) return "terminal";

        // 6. Working — broad "in progress" signal. Last among the
        // "actively-doing-something" buckets so it doesn't shadow more
        // specific outcomes.
        foreach (var kw in WorkingKeywords) if (s.Contains(kw)) return "working";

        // 7. Initial — broadest "queued / not started" signal. Last
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
    //
    // Phase 149 — added the "states the team puts a deliverable into when
    // they can't progress it but it's not finished" vocabulary
    // (ON_HOLD / BLOCKED / WAITING / ESCALATED) to working/submitting,
    // plus the "frozen / abandoned / withdrawn" vocabulary to terminal.
    // These don't appear on canonical ISO 19650 flows but turn up
    // routinely in JCT / NEC / corporate-bespoke vocabularies.
    private static readonly string[] RejectKeywords =
        { "REJECT", "DECLIN", "RETURN", "REWORK", "FAIL", "VOID" };
    private static readonly string[] AcceptKeywords =
        { "ACCEPT", "APPROV", "PUBLISH", "SIGNED_OFF", "SIGNOFF", "PASSED" };
    /// <summary>
    /// "Sent out and waiting" phrases. Scanned before the accept/reject buckets
    /// because each one contains an outcome word as a substring
    /// ("...FOR_APPROVAL" contains "APPROV") and would otherwise be mistaken for
    /// the outcome itself. Keep this list to phrases only — a bare verb here
    /// would shadow the genuine outcome states.
    /// </summary>
    private static readonly string[] AwaitingPhrases =
        { "ISSUED_FOR", "FOR_APPROVAL", "FOR_INFORMATION", "FOR_COMMENT", "FOR_REVIEW",
          "AWAITING", "PENDING_APPROVAL", "PENDING_REVIEW" };
    private static readonly string[] SubmitKeywords =
        { "SUBMIT", "REVIEW", "ISSUED_FOR", "FOR_INFORMATION", "FOR_APPROVAL", "FOR_COMMENT", "ESCALAT" };
    /// <summary>
    /// Still-live states whose text collides with a terminal keyword.
    /// "BLOCKED" contains "LOCKED"; scanned first so a blocked-but-active
    /// deliverable is not reported as closed.
    /// </summary>
    private static readonly string[] BlockedPhrases =
        { "BLOCKED", "UNBLOCKED" };
    private static readonly string[] TerminalKeywords =
        { "ARCHIV", "CLOSED", "CANCELL", "WAIVE", "SUPERSED", "COMPLETE", "FINAL", "DONE",
          "LOCKED", "FROZEN", "ABANDON", "WITHDRAW", "HANDED_OVER", "HANDOVER" };
    private static readonly string[] WorkingKeywords =
        { "PROGRESS", "WIP", "DRAFT", "ACTIVE", "BUILD", "ONGOING",
          "ON_HOLD", "ONHOLD", "BLOCKED", "WAITING", "PAUSED" };
    private static readonly string[] InitialKeywords =
        { "PENDING", "BACKLOG", "TODO", "NEW", "QUEUED", "OPEN", "BRIEF" };

    /// <summary>
    /// Phase 149 — parse the optional <c>"keywords"</c> block. Filters
    /// to the six canonical role bucket names; anything else is dropped
    /// silently (typo doesn't 500). Each bucket's value must be a JSON
    /// array of strings; non-strings within a bucket are skipped.
    /// </summary>
    /// <summary>
    /// Phase 150 — n-ary merge of keyword-extension layers in priority
    /// order. Layers are taken highest-priority-first; later layers
    /// fill in roles the earlier layers didn't claim. Within a single
    /// role bucket the entries from all layers concatenate then dedupe
    /// (case-insensitive). Null / empty layers are skipped silently.
    ///
    /// Phase 151 — used to merge three layers (project > tenant >
    /// platform) instead of two. Pass them in priority order; the
    /// helper handles any non-collision intersection rules.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyCollection<string>> MergeKeywordLayers(
        params IReadOnlyDictionary<string, IReadOnlyCollection<string>>?[] layersInPriorityOrder)
    {
        if (layersInPriorityOrder == null || layersInPriorityOrder.Length == 0)
            return new Dictionary<string, IReadOnlyCollection<string>>();

        var nonEmpty = new List<IReadOnlyDictionary<string, IReadOnlyCollection<string>>>();
        foreach (var layer in layersInPriorityOrder)
        {
            if (layer != null && layer.Count > 0) nonEmpty.Add(layer);
        }
        if (nonEmpty.Count == 0)
            return new Dictionary<string, IReadOnlyCollection<string>>();
        if (nonEmpty.Count == 1)
            return nonEmpty[0];

        var merged = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
        var allRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in nonEmpty)
            foreach (var role in layer.Keys) allRoles.Add(role);

        // A keyword may appear in DIFFERENT buckets in different layers — a
        // project saying working:["LOCKED"] over a platform saying
        // terminal:["LOCKED"]. Concatenating per-bucket keeps both, and the
        // conflict is then settled by RolePriority at inference time, where
        // terminal outranks working — so the platform silently won and layer
        // precedence meant nothing for exactly the case it exists to handle.
        //
        // Claim each keyword for the highest-priority layer that mentions it,
        // and let no later layer re-add it under a different role.
        var claimedBy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in nonEmpty)                 // already priority-ordered
            foreach (var (role, entries) in layer)
                foreach (var kw in entries)
                {
                    if (string.IsNullOrWhiteSpace(kw)) continue;
                    var norm = kw.Trim().ToUpperInvariant();
                    if (!claimedBy.ContainsKey(norm)) claimedBy[norm] = role;
                }

        foreach (var role in allRoles)
        {
            var combined = new List<string>();
            // Higher-priority layers contribute first so their entries
            // sit at the head of the deduped list (priority is
            // observable through enumeration order even though dedupe
            // collapses equal strings).
            foreach (var layer in nonEmpty)
            {
                if (!layer.TryGetValue(role, out var entries)) continue;
                foreach (var kw in entries)
                {
                    if (string.IsNullOrWhiteSpace(kw)) continue;
                    // Skip keywords a higher-priority layer claimed for another role.
                    if (claimedBy.TryGetValue(kw.Trim().ToUpperInvariant(), out var owner)
                        && !string.Equals(owner, role, StringComparison.OrdinalIgnoreCase))
                        continue;
                    combined.Add(kw);
                }
            }
            var deduped = combined
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (deduped.Count > 0) merged[role] = deduped.AsReadOnly();
        }
        return merged;
    }

    private static IReadOnlyDictionary<string, IReadOnlyCollection<string>> ParseCustomKeywords(JsonElement root)
    {
        var result = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("keywords", out var kwEl) || kwEl.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var prop in kwEl.EnumerateObject())
        {
            var role = prop.Name.Trim().ToLowerInvariant();
            if (!KnownRoles.Contains(role) || role == "none") continue;
            if (prop.Value.ValueKind != JsonValueKind.Array) continue;

            var entries = new List<string>();
            foreach (var item in prop.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) continue;
                var token = item.GetString();
                if (!string.IsNullOrWhiteSpace(token))
                    entries.Add(token!.Trim().ToUpperInvariant());
            }
            if (entries.Count > 0) result[role] = entries.AsReadOnly();
        }
        return result;
    }

    /// <summary>
    /// Used during loader pre-resolution. Walks the role-priority order
    /// against the tenant-supplied keyword map. Mirrors the runtime
    /// <see cref="InferRoleWithExtensions"/> path but operates on a
    /// caller-supplied dictionary so it can run before the
    /// <see cref="DeliverableStateMachine"/> instance exists.
    /// </summary>
    private static string? InferFromCustomKeywords(
        string state,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> kw)
    {
        if (string.IsNullOrWhiteSpace(state)) return null;
        var s = state.ToUpperInvariant();
        foreach (var role in RolePriority)
        {
            if (!kw.TryGetValue(role, out var list)) continue;
            foreach (var token in list)
            {
                if (string.IsNullOrWhiteSpace(token)) continue;
                if (s.Contains(token, StringComparison.OrdinalIgnoreCase)) return role;
            }
        }
        return null;
    }

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
