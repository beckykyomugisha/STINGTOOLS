namespace Planscape.Core.Entities;

/// <summary>
/// S5.1 — one row per chunk in a federated scene. The S5.2 sidecar
/// (gltf-transform) splits an uploaded ProjectModel into chunks keyed
/// by discipline / level / system; each chunk lands here with its own
/// storage path, AABB, and approximate vertex count.
///
/// The mobile / web viewer fetches the manifest first
/// (one HTTP per project, ~5 KB) and then streams chunks on demand
/// based on camera position + the user's discipline filter.
/// </summary>
public class SceneNode : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>The source ProjectModel this chunk was split from.</summary>
    public Guid SourceModelId { get; set; }

    /// <summary>"M" | "E" | "P" | "A" | "S" | "FP" | "LV" — ISO 19650 discipline code.</summary>
    public string Discipline { get; set; } = "";

    /// <summary>Optional level / floor code, e.g. "L01", "GF", "B1".</summary>
    public string? LevelCode { get; set; }

    /// <summary>Optional system code, e.g. "HVAC", "DCW", "STR".</summary>
    public string? SystemCode { get; set; }

    /// <summary>Storage path of the chunk's GLB, scoped under <c>t_{tenantId}/{projectId}/scenes/...</c>.</summary>
    public string StoragePath { get; set; } = "";

    /// <summary>SHA-256 of the chunk so the mobile cache can dedup across versions.</summary>
    public string ContentHash { get; set; } = "";

    public long FileSizeBytes { get; set; }

    /// <summary>Approximate vertex count — drives the auto-LOD heuristic.</summary>
    public int VertexCount { get; set; }

    /// <summary>AABB in millimetres — six doubles. Lets the viewer cull off-camera chunks.</summary>
    public double MinX { get; set; }
    public double MinY { get; set; }
    public double MinZ { get; set; }
    public double MaxX { get; set; }
    public double MaxY { get; set; }
    public double MaxZ { get; set; }

    /// <summary>Compression: "none" | "draco" | "meshopt".</summary>
    public string Compression { get; set; } = "none";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }
}
