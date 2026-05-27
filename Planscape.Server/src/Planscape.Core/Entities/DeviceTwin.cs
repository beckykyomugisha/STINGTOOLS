namespace Planscape.Core.Entities;

/// <summary>
/// Pillar B (5A) — a digital twin of a physical device, bound to a model
/// element through K1 (the device is an ExternalElementMapping row with
/// Host="iot", so its telemetry resolves to the very IFC GlobalId the viewer
/// and Revit already use — no separate device↔element identity system).
/// Holds last-known state for the live RAG view; the time series lives in
/// <see cref="TelemetryPoint"/>.
/// </summary>
public class DeviceTwin : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>Stable device identifier (the HostElementId of its iot mapping).</summary>
    public string DeviceId { get; set; } = "";

    /// <summary>Canonical model element this device monitors (resolved via K1).</summary>
    public string? IfcGlobalId { get; set; }

    /// <summary>mqtt | bacnet | modbus | opcua | manual.</summary>
    public string Protocol { get; set; } = "mqtt";

    public string? AssetTag { get; set; }
    public string? Serial { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }

    /// <summary>OK | WARNING | ALARM | OFFLINE | UNKNOWN — drives the RAG overlay colour.</summary>
    public string HealthState { get; set; } = "UNKNOWN";

    /// <summary>Last metric→value snapshot as JSON (the live tile feed).</summary>
    public string? LastStateJson { get; set; }

    public DateTime? LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Free-form provisioning metadata (commissioning ref, COBie key, …).</summary>
    public string? MetadataJson { get; set; }

    public Project? Project { get; set; }
}

/// <summary>
/// Pillar B (5A) — one telemetry reading. Stored in a TimescaleDB hypertable
/// (partitioned by <see cref="Ts"/>) so high-volume ingest + retention +
/// continuous aggregates stay performant on the existing Postgres 16.
///
/// Hypertable conversion is a post-migration step (Timescale DDL EF can't
/// emit): SELECT create_hypertable('"TelemetryPoints"','Ts'); plus a
/// retention/continuous-aggregate policy. Until run it behaves as a plain
/// table — correct, just unpartitioned.
/// </summary>
public class TelemetryPoint : ITenantScoped
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string DeviceId { get; set; } = "";
    public string Metric { get; set; } = "";
    public double Value { get; set; }
    public string? Unit { get; set; }
    public DateTime Ts { get; set; } = DateTime.UtcNow;
}
