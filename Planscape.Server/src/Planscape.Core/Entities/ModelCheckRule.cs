namespace Planscape.Core.Entities;

/// <summary>
/// A rule-set is a named, versioned collection of model-check rules
/// that runs together. Equivalent to a Solibri Ruleset (.cset). Common
/// sets: "Phase Gate S3", "Fire Door Compliance", "Healthcare HTM
/// audit", "Pre-handover".
/// </summary>
public class ModelCheckRuleSet : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Null = firm-wide; set = project-specific.</summary>
    public Guid? ProjectId { get; set; }

    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    public string Version { get; set; } = "1.0";

    /// <summary>Cron expression for scheduled execution (Hangfire RecurringJob).</summary>
    public string? Schedule { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>SHA-256 of all included rule definitions — tamper evidence.</summary>
    public string? Checksum { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
}

/// <summary>
/// A single model-check rule — the executable unit of a Solibri-grade
/// rule-based checker. Rules carry a discriminator (Kind) that picks
/// the executor on the server side: existence checks, property checks,
/// clearance checks, redundancy checks, fire-rating checks, spatial
/// containment, accessibility (BS 8300), and free-form expression checks.
///
/// Rule parameters are stored as opaque JSON to keep the schema stable
/// as new rule types are added — the executor knows how to interpret
/// the payload for its Kind.
/// </summary>
public class ModelCheckRule : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid RuleSetId { get; set; }

    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>
    /// Rule kind — drives executor dispatch:
    /// PropertyRequired / PropertyEquals / PropertyInRange /
    /// ElementExists / ElementCount / Clearance / Containment /
    /// FireRatingPropagation / Accessibility / DuplicateGuid /
    /// NamingConvention / IfcClassValidator / SpatialOverlap /
    /// MissingMaterial / GeometryQuality / Expression.
    /// </summary>
    public string Kind { get; set; } = "PropertyRequired";

    /// <summary>"Critical" / "Major" / "Minor" / "Info" — drives reporting severity.</summary>
    public string Severity { get; set; } = "Major";

    /// <summary>
    /// IFC type filter — "IfcDoor", "IfcWall", or "*" for all elements.
    /// Comma-separated for multi-type rules.
    /// </summary>
    public string? AppliesToIfcTypes { get; set; }

    /// <summary>STING discipline filter (M/E/P/A/S).</summary>
    public string? AppliesToDiscipline { get; set; }

    /// <summary>JSON parameter blob — schema per Kind. Examples:
    /// <list type="bullet">
    /// <item>PropertyRequired: <c>{"pset":"Pset_DoorCommon","prop":"FireRating"}</c></item>
    /// <item>PropertyInRange: <c>{"pset":"Pset_Door","prop":"Width","min":900,"max":1200}</c></item>
    /// <item>Clearance: <c>{"otherIfcType":"IfcSlab","direction":"above","minMm":2400}</c></item>
    /// <item>FireRatingPropagation: <c>{"requiredRating":"FD60","wallProp":"FireRating"}</c></item>
    /// </list>
    /// </summary>
    public string ParamsJson { get; set; } = "{}";

    /// <summary>
    /// Optional auto-fix action — when the rule fires, can the engine
    /// propose a remediation? "None" / "SuggestPropertyValue" /
    /// "CreateIssue" / "AssignToCoordinator".
    /// </summary>
    public string AutoAction { get; set; } = "None";

    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }

    public ModelCheckRuleSet? RuleSet { get; set; }
}

/// <summary>
/// One execution of a <see cref="ModelCheckRuleSet"/> against a project
/// model. Holds the run-level summary (counts, durations, status)
/// and is the parent of N <see cref="ModelCheckResult"/> rows.
/// </summary>
public class ModelCheckRun : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    public Guid RuleSetId { get; set; }

    /// <summary>Optional — null = federation-wide run.</summary>
    public Guid? ProjectModelId { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    /// <summary>"Queued" / "Running" / "Completed" / "Failed" / "Cancelled".</summary>
    public string Status { get; set; } = "Queued";

    public int TotalRulesEvaluated { get; set; }
    public int TotalElementsChecked { get; set; }
    public int FindingsCount { get; set; }
    public int CriticalCount { get; set; }
    public int MajorCount { get; set; }
    public int MinorCount { get; set; }
    public int InfoCount { get; set; }

    public string? ErrorMessage { get; set; }
    public string TriggeredBy { get; set; } = "manual";

    public ModelCheckRuleSet? RuleSet { get; set; }
}

/// <summary>
/// A single rule firing — one row per element that failed a rule.
/// Mirrors the granularity of clash records so the dashboard can fold
/// the two streams into a single "issues to resolve" list.
/// </summary>
public class ModelCheckResult : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    public Guid RunId { get; set; }
    public Guid RuleId { get; set; }

    public Guid? ProjectModelId { get; set; }
    public string? IfcGlobalId { get; set; }
    public string? IfcType { get; set; }
    public string? ElementName { get; set; }
    public string? Level { get; set; }

    /// <summary>"Critical" / "Major" / "Minor" / "Info".</summary>
    public string Severity { get; set; } = "Major";

    /// <summary>Human description — "Door D-042 missing FireRating property".</summary>
    public string Message { get; set; } = "";

    /// <summary>Suggested remediation if the rule has an AutoAction.</summary>
    public string? Suggestion { get; set; }

    /// <summary>Status: Open / Acknowledged / Resolved / Ignored / FalsePositive.</summary>
    public string Status { get; set; } = "Open";

    /// <summary>Link to <see cref="BimIssue"/> when one was raised from this finding.</summary>
    public Guid? BimIssueId { get; set; }

    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }

    public ModelCheckRule? Rule { get; set; }
    public ModelCheckRun? Run { get; set; }
}
