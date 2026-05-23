namespace Planscape.Core.Entities;

/// <summary>
/// Lightning Protection System project record — one row per Revit project
/// per sync. Captures the headline numbers the BS EN 62305 compliance
/// audit produces so the Planscape dashboard, mobile inbox, and
/// management reports can show LPS status without re-running the
/// plugin engine.
///
/// Mirrors ComplianceSnapshot in shape — point-in-time, tenant-scoped,
/// project-bound, captured-by audit fields. Updated by LpsSyncToServerCommand
/// in the plugin and read by /api/projects/{id}/lps GET endpoints.
/// </summary>
public class LpsRecord : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string CapturedBy { get; set; } = "";
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    // ── Project-level LPS configuration (BS EN 62305-3) ────────────
    /// <summary>I / II / III / IV / NONE.</summary>
    public string LpsClass { get; set; } = "";
    public double RollingSphereRadiusM { get; set; }
    public double MeshSizeM { get; set; }
    public int    InspectionIntervalMonths { get; set; }
    public double EarthResistanceTargetOhm { get; set; }
    /// <summary>Effective Ng for the project (project override or region default).</summary>
    public double GroundFlashDensity { get; set; }

    // ── Component counts ───────────────────────────────────────────
    public int AirTerminalCount { get; set; }
    public int DownConductorCount { get; set; }
    public int EarthElectrodeCount { get; set; }
    public int BondingCount { get; set; }
    public int SpdCount { get; set; }

    // ── Separation distance ────────────────────────────────────────
    /// <summary>BS EN 62305-3 §6.3 kc factor — partitioning across DCs.</summary>
    public double KcFactor { get; set; }
    /// <summary>Count of MEP conflicts flagged by Sep Distance check.</summary>
    public int SepDistanceViolations { get; set; }

    // ── Risk assessment (BS EN 62305-2) ────────────────────────────
    public double AnnualStrikeFrequencyNd { get; set; }
    public double CollectionAreaM2 { get; set; }
    public double RiskR1 { get; set; }
    public double RiskR2 { get; set; }
    public double RiskR3 { get; set; }
    public double RiskR4 { get; set; }
    public double TolerableR1 { get; set; }
    public double TolerableR2 { get; set; }
    public double TolerableR3 { get; set; }
    public double TolerableR4 { get; set; }
    public string RecommendedClass { get; set; } = "";

    // ── Compliance verdict + audit ─────────────────────────────────
    /// <summary>PASS / WARN / FAIL.</summary>
    public string ComplianceVerdict { get; set; } = "";
    public int ComplianceChecksPass { get; set; }
    public int ComplianceChecksWarn { get; set; }
    public int ComplianceChecksFail { get; set; }

    /// <summary>YYYY-MM-DD of the last earth-resistance / continuity test.</summary>
    public string LastTestDate { get; set; } = "";
    public string CertReference { get; set; } = "";

    // ── SPD coordination summary (IEC 62305-4 / IEC 61643) ─────────
    public int SpdCoordinationPass { get; set; }
    public int SpdCoordinationWarn { get; set; }
    public int SpdCoordinationFail { get; set; }

    // Navigation
    public Project? Project { get; set; }
}
