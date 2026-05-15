namespace Planscape.Core.Entities;

/// <summary>
/// A point-in-time BOQ (Bill of Quantities) cost snapshot pushed from the Revit plugin
/// or auto-seeded from an IFC upload. Stores total estimated/actual costs plus a
/// per-discipline breakdown serialised as JSON.
/// </summary>
public class BoqSnapshot : ITenantScoped
{
    public int  Id        { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public Guid TenantId  { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>
    /// The user who pushed this snapshot, or "system-ifc-import" for auto-seeded snapshots.
    /// </summary>
    public string CreatedByUserId { get; set; } = "";
    /// <summary>Serialised <see cref="BoqSnapshotDto"/> JSON blob.</summary>
    public string SnapshotJson { get; set; } = "{}";
}

// ── DTOs ────────────────────────────────────────────────────────────────────

/// <summary>Full snapshot DTO exchanged between plugin/mobile and the server.</summary>
public class BoqSnapshotDto
{
    public double TotalEstimated { get; set; }
    public double TotalActual    { get; set; }
    public List<BoqDisciplineRow> Disciplines { get; set; } = new();
}

public class BoqDisciplineRow
{
    public string Discipline { get; set; } = "";
    public int    Items      { get; set; }
    public double Estimated  { get; set; }
    public double Actual     { get; set; }
}

/// <summary>Compact trend point returned in GET /boq/snapshot.</summary>
public class BoqTrendPoint
{
    public DateTime Date           { get; set; }
    public double   TotalEstimated { get; set; }
    public double   TotalActual    { get; set; }
}
