namespace Planscape.Core.Entities;

/// <summary>
/// ISO 19650 BIM issue / RFI / NCR tracked across project lifecycle.
/// </summary>
public class BimIssue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string IssueCode { get; set; } = ""; // e.g., RFI-0001, NCR-0003, SI-0012
    public string Type { get; set; } = "RFI"; // RFI, NCR, SI, TQ, DESIGN, SAFETY, CLASH
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

    // Navigation
    public Project? Project { get; set; }
    public AppUser? AssigneeUser { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public List<IssueAttachment> Attachments { get; set; } = new();
}
