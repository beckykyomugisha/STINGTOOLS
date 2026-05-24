namespace Planscape.Core.Entities;

/// <summary>
/// One Template Manager operation result captured by the Revit plugin and
/// pushed to the server via /api/projects/{projectId}/template-ops.
/// Sister entity to ComplianceSnapshot — the dashboard publishes here when
/// a STING user runs anything in the Template Manager v2.
/// </summary>
public class TemplateOpRecord : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    public string Operation { get; set; } = "";       // op tag, e.g. "CreateFillPatterns"
    public string OperationLabel { get; set; } = "";
    public string Severity { get; set; } = "Info";    // Info / Success / Warning / Error
    public string Headline { get; set; } = "";
    public string? SubHeadline { get; set; }
    public DateTime CompletedUtc { get; set; } = DateTime.UtcNow;
    public double DurationMs { get; set; }
    public string CapturedBy { get; set; } = "";
    public string? DocumentPath { get; set; }
    public string? DocumentTitle { get; set; }

    public int CreatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public int SectionCount { get; set; }

    // Free-form bag (JSON-serialised dictionary)
    public string? CountersJson { get; set; }

    // Navigation
    public Project? Project { get; set; }
}
