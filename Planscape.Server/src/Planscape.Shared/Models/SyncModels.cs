namespace Planscape.Shared.Models;

/// <summary>
/// Lightweight sync payload for plugin→server communication.
/// Matches the plugin's existing data structures for zero-friction integration.
/// </summary>
public class PluginSyncPayload
{
    public Guid ProjectId { get; set; }
    public string PluginVersion { get; set; } = "";
    public string RevitVersion { get; set; } = "";
    public string UserName { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Tag data (delta)
    public List<TagElementSync>? TagElements { get; set; }

    // SEQ counters (max-per-key merge)
    public Dictionary<string, int>? SeqCounters { get; set; }

    // Compliance snapshot
    public ComplianceSync? Compliance { get; set; }

    // Issues (created/updated since last sync)
    public List<IssueSync>? Issues { get; set; }

    // Workflow runs (new since last sync)
    public List<WorkflowRunSync>? WorkflowRuns { get; set; }
}

public class TagElementSync
{
    public long RevitElementId { get; set; }
    public string UniqueId { get; set; } = "";
    public string Disc { get; set; } = "";
    public string Loc { get; set; } = "";
    public string Zone { get; set; } = "";
    public string Lvl { get; set; } = "";
    public string Sys { get; set; } = "";
    public string Func { get; set; } = "";
    public string Prod { get; set; } = "";
    public string Seq { get; set; } = "";
    public string Tag1 { get; set; } = "";
    public string? Tag7 { get; set; }
    public string CategoryName { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public string? Status { get; set; }
    public string? Rev { get; set; }
    public bool IsComplete { get; set; }
    public bool IsFullyResolved { get; set; }
    public bool IsStale { get; set; }

    /// <summary>
    /// Client-supplied wall-clock timestamp of the most recent modification to
    /// this element's STING tokens. Serialised as <c>lastModifiedUtc</c> to
    /// match <c>Planscape.Core.DTOs.TagElementDto.LastModifiedUtc</c>; the
    /// server uses it for last-write-wins conflict detection and to return
    /// only true deltas on <c>GET /api/tagsync/elements/{projectId}</c>.
    /// Nullable so pre-INT-03 plugin builds that never populate it still
    /// deserialise cleanly (the server treats <c>null</c> as "legacy client —
    /// always accept").
    /// </summary>
    public DateTime? LastModifiedUtc { get; set; }
}

public class ComplianceSync
{
    public int TotalElements { get; set; }
    public int TaggedComplete { get; set; }
    public int TaggedIncomplete { get; set; }
    public int Untagged { get; set; }
    public int FullyResolved { get; set; }
    public int StaleCount { get; set; }
    public int PlaceholderCount { get; set; }
    public int WarningCount { get; set; }
    public double TagPercent { get; set; }
    public double StrictPercent { get; set; }
    public double ContainerPercent { get; set; }
    public string RagStatus { get; set; } = "RED";
    public Dictionary<string, int>? ByDiscipline { get; set; }
    public Dictionary<string, int>? EmptyTokenCounts { get; set; }
}

public class IssueSync
{
    public string IssueCode { get; set; } = "";
    public string Type { get; set; } = "RFI";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Priority { get; set; } = "MEDIUM";
    public string Status { get; set; } = "OPEN";
    public string? Assignee { get; set; }
    public string? Discipline { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class WorkflowRunSync
{
    public string PresetName { get; set; } = "";
    public int StepsPassed { get; set; }
    public int StepsFailed { get; set; }
    public int StepsSkipped { get; set; }
    public double DurationMs { get; set; }
    public double ComplianceBefore { get; set; }
    public double ComplianceAfter { get; set; }
    public DateTime ExecutedAt { get; set; }
}

public class SyncResult
{
    public bool Success { get; set; }
    public int TagsCreated { get; set; }
    public int TagsUpdated { get; set; }
    public int SeqCountersMerged { get; set; }
    public double ServerCompliancePercent { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// P4 — HTTP status code from the server when available (0 for
    /// network failures, -1 for non-HTTP errors). The offline-queue
    /// drain uses this to distinguish:
    ///   • 5xx / 0 (transient) → break the drain loop, keep retrying
    ///   • 4xx (fatal — bad payload, revoked auth) → skip this payload,
    ///     log + continue draining
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// P7 — server-side conflicts the client should surface. Each entry
    /// is a short human-readable string; the dock-panel sync chip shows
    /// "Sync: N conflicts" so users can open a conflict inspector.
    /// </summary>
    public List<string> Conflicts { get; set; } = new();

    public bool IsTransient => StatusCode == 0 || StatusCode >= 500;
    public bool IsFatalRequestError => StatusCode >= 400 && StatusCode < 500;
}
