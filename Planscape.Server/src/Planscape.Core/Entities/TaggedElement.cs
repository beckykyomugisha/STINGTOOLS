namespace Planscape.Core.Entities;

/// <summary>
/// A tagged BIM element synced from the Revit plugin.
/// Stores ISO 19650 8-segment tag data + TAG7 narrative + compliance state.
/// </summary>
public class TaggedElement : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public long RevitElementId { get; set; }
    public string UniqueId { get; set; } = ""; // Revit UniqueId for cross-session tracking

    // 8 source tokens
    public string Disc { get; set; } = "";
    public string Loc { get; set; } = "";
    public string Zone { get; set; } = "";
    public string Lvl { get; set; } = "";
    public string Sys { get; set; } = "";
    public string Func { get; set; } = "";
    public string Prod { get; set; } = "";
    public string Seq { get; set; } = "";

    // Assembled tags
    public string Tag1 { get; set; } = ""; // Full 8-segment ISO 19650 tag
    public string? Tag7 { get; set; }       // Rich descriptive narrative
    public string? Tag7A { get; set; }      // Identity Header
    public string? Tag7B { get; set; }      // System & Function
    public string? Tag7C { get; set; }      // Spatial Context
    public string? Tag7D { get; set; }      // Lifecycle & Status
    public string? Tag7E { get; set; }      // Technical Specs
    public string? Tag7F { get; set; }      // Classification

    // Context
    public string CategoryName { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string? Status { get; set; }  // NEW/EXISTING/DEMOLISHED/TEMPORARY
    public string? Rev { get; set; }
    public string? GridRef { get; set; }
    public string? RoomName { get; set; }
    public string? Level { get; set; }

    // Compliance
    public bool IsStale { get; set; }
    public bool IsComplete { get; set; }
    public bool IsFullyResolved { get; set; }
    public string? ValidationErrors { get; set; } // JSON array of errors

    // Audit
    public string? PreviousTag { get; set; }
    public DateTime? TagModifiedAt { get; set; }
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
    public string SyncedBy { get; set; } = "";

    // Source identification — set by the IFC ingester to track the authoring tool.
    // Added for ArchiCAD source identification.
    public string? Source { get; set; } // "archicad" | "ifc" | "revit" | null

    // Optimistic-concurrency / last-write-wins support for bidirectional sync.
    // LastModifiedUtc is the client-supplied wall-clock modification time; the
    // server uses it to detect stale updates from out-of-date clients.
    public DateTime? LastModifiedUtc { get; set; }
    public int Version { get; set; } = 1;

    // P6 live-link fields — populated by P6LiveLinkService when a matching activity is found.
    public string?  P6ActivityId    { get; set; }
    public double?  PercentComplete { get; set; }
    public string?  ActualStart     { get; set; }
    public string?  ActualFinish    { get; set; }

    // Navigation
    public Project? Project { get; set; }
}
