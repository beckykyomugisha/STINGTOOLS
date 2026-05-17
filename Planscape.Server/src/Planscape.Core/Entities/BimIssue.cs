namespace Planscape.Core.Entities;

/// <summary>
/// ISO 19650 BIM issue / RFI / NCR tracked across project lifecycle.
/// </summary>
public class BimIssue : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string IssueCode { get; set; } = ""; // e.g., RFI-0001, NCR-0003, SI-0012
    // Phase 96 — WORKFLOW_REQ added for mobile "Request workflow run" flow.
    // The Revit plugin's BCC Issues tab filters on this type and surfaces a
    // "Run Preset" action instead of the normal issue detail panel.
    public string Type { get; set; } = "RFI"; // RFI, NCR, SI, TQ, DESIGN, SAFETY, CLASH, WORKFLOW_REQ
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Priority { get; set; } = "MEDIUM"; // CRITICAL, HIGH, MEDIUM, LOW
    public string Status { get; set; } = "OPEN"; // OPEN, IN_PROGRESS, RESOLVED, CLOSED
    public string? Assignee { get; set; } // Display name (legacy / human-readable)
    public string? AssigneeEmail { get; set; } // NEW-MOB-17: stable identifier for routing
    public Guid? AssigneeUserId { get; set; } // NEW-SRV-23: FK for project member enforcement
    public string CreatedBy { get; set; } = "";
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DueDate { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? Discipline { get; set; }
    public string? Revision { get; set; }
    public string? LinkedElementIds { get; set; } // JSON array of Revit element IDs
    public string? BcfGuid { get; set; } // BCF 2.1 topic GUID

    // WATCHERS — comma-or-JSON list of AppUser ids who receive every status
    // change + comment for this issue (in addition to the assignee). Stored
    // as a JSON array of GUID strings to mirror the LinkedElementIds shape
    // and avoid a join table; downstream code uses
    // <see cref="ParseWatcherIds"/> / <see cref="SerializeWatcherIds"/>.
    // Migration: add a nullable text column WatcherUserIds when bringing
    // the production database forward (dotnet ef migrations add AddBimIssueWatchers).
    public string? WatcherUserIds { get; set; }

    // CO-ASSIGNEES — JSON array of additional AppUser ids who share
    // responsibility for resolving this issue alongside the primary assignee.
    // Common in BIM: an RFI may involve the architect, structural engineer,
    // and MEP coordinator simultaneously. Each co-assignee gets the same push
    // notifications as the primary assignee (assignment + status changes +
    // attachments). Stored identically to WatcherUserIds; use
    // <see cref="ParseWatcherIds"/> for deserialisation (same schema).
    // Migration: dotnet ef migrations add AddBimIssueCoAssignees
    public string? CoAssigneeUserIds { get; set; }

    public static IReadOnlyList<Guid> ParseWatcherIds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<Guid>();
        var trimmed = raw.Trim();
        try
        {
            if (trimmed.StartsWith("["))
            {
                var arr = System.Text.Json.JsonSerializer.Deserialize<string[]>(trimmed) ?? Array.Empty<string>();
                return arr.Where(s => Guid.TryParse(s, out _))
                          .Select(Guid.Parse)
                          .Distinct()
                          .ToList();
            }
        }
        catch { /* fall through to comma form */ }
        return trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                      .Where(s => Guid.TryParse(s, out _))
                      .Select(Guid.Parse)
                      .Distinct()
                      .ToList();
    }

    public static string? SerializeWatcherIds(IEnumerable<Guid>? ids)
    {
        if (ids == null) return null;
        var list = ids.Where(g => g != Guid.Empty).Distinct().Select(g => g.ToString()).ToArray();
        return list.Length == 0 ? null : System.Text.Json.JsonSerializer.Serialize(list);
    }

    // SRV-03 — site location captured at the moment of issue creation
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? LocationAccuracy { get; set; }
    public string? DeviceId { get; set; } // device that raised the issue (mobile audit)
    public string? Source { get; set; } // "mobile" | "plugin" | "web"

    // MODEL-VIEWER — 3D model anchor. When an issue is created from the 3D
    // viewer ("create issue here"), we record which model, which element
    // inside it, and the exact XYZ the user tapped. Viewer then renders pins
    // in-model. All nullable so non-viewer flows are untouched.
    public Guid? ModelId { get; set; }
    public string? ModelElementGuid { get; set; }
    public double? ModelX { get; set; }
    public double? ModelY { get; set; }
    public double? ModelZ { get; set; }

    // CUSTOM-FIELDS (FLEX-13) — tenant/project-defined schema values live here
    // as a JSON object ({ "field_key": any }). Schema definitions are stored
    // separately in <see cref="IssueCustomFieldSchema"/>. Storage is JSONB on
    // Postgres (with a GIN index for searched keys); empty object when unset.
    public string? CustomFields { get; set; }

    // Phase 175 — design option binding. When an issue is raised against
    // a specific Revit design option (e.g. RFI on the VE façade study
    // alternative), the plugin attaches the host project's option-set
    // and option name. The mobile inbox + cross-project SearchController
    // filter on these so site queries are answered against the right
    // alternative. Both nullable for legacy / main-model issues.
    public string? OptionSetName { get; set; }
    public string? OptionName { get; set; }

    // Navigation
    public Project? Project { get; set; }
    public AppUser? AssigneeUser { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public List<IssueAttachment> Attachments { get; set; } = new();
}
