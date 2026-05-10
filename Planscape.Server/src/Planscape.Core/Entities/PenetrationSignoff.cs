namespace Planscape.Core.Entities;

/// <summary>
/// Phase 178f — penetration commissioning sign-off captured by the
/// mobile app on-site. Each row maps 1:1 to a placed FRP / fire-
/// damper / acoustic-seal instance via the deterministic
/// <see cref="PenetrationControlNumber"/> stamped on
/// <c>PEN_CONTROL_NUMBER_TXT</c>. Combines installer + inspector
/// metadata with photo / GPS evidence so the BS 9999 / Building
/// Safety Act golden-thread record closes at site sign-off.
/// </summary>
public class PenetrationSignoff : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>FRP-NNNN — minted by FrpPenetrationPlacer.MintControlNumber.</summary>
    public string PenetrationControlNumber { get; set; } = "";

    /// <summary>Mirror of PEN_PFV_UUID_TXT — UUIDv5(host, member). Cross-pipeline pairing key with sleeve schema.</summary>
    public string PfvUuid { get; set; } = "";

    /// <summary>FLOOR / WALL / BEAM / CEILING / ROOF.</summary>
    public string HostType { get; set; } = "";

    /// <summary>FR30 / FR60 / FR90 / FR120 / blank for non-rated.</summary>
    public string FireRating { get; set; } = "";

    /// <summary>UL system / EN 1366-3 reference written at swap time.</summary>
    public string Certification { get; set; } = "";

    /// <summary>FIRESTOP / FIRE_DAMPER / ACOUSTIC_SEAL / SLEEVE_GENERIC.</summary>
    public string ProductKind { get; set; } = "FIRESTOP";

    public string InstallerName { get; set; } = "";
    public string InstallerCompany { get; set; } = "";
    public DateTime? InstalledAt { get; set; }

    public string InspectorName { get; set; } = "";
    public DateTime? InspectedAt { get; set; }

    /// <summary>DRAFT / INSTALLED / INSPECTED / SIGNED-OFF / REWORK.</summary>
    public string Status { get; set; } = "INSTALLED";

    /// <summary>Free-text notes (defects, rework reason, vendor advice).</summary>
    public string Notes { get; set; } = "";

    /// <summary>Photo blob id stored in MinIO. Null when no photo captured.</summary>
    public string? PhotoBlobId { get; set; }

    public double? GpsLat { get; set; }
    public double? GpsLon { get; set; }

    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public string CapturedBy { get; set; } = "";
}
