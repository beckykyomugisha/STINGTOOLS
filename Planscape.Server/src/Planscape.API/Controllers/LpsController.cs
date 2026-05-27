using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Authorization;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;

namespace Planscape.API.Controllers;

/// <summary>
/// Lightning Protection System project records — point-in-time captures
/// of the BS EN 62305 compliance audit (class, component counts, risk
/// R1–R4, separation-distance violations, SPD coordination verdict).
///
/// Mirrors the ComplianceController / WarningsController shape. Push
/// from the Revit plugin via the LPS panel's "Sync to Planscape" button;
/// read from the management dashboard for trend tracking and
/// regulatory submissions.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/lps")]
[Authorize]
[ProjectAccess]
public class LpsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IHubContext<NotificationHub> _hub;

    public LpsController(PlanscapeDbContext db, IHubContext<NotificationHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    // ── Push (POST) ────────────────────────────────────────────────

    public class PushLpsRecordRequest
    {
        // Project-level
        public string LpsClass { get; set; } = "";
        public double RollingSphereRadiusM { get; set; }
        public double MeshSizeM { get; set; }
        public int    InspectionIntervalMonths { get; set; }
        public double EarthResistanceTargetOhm { get; set; }
        public double GroundFlashDensity { get; set; }

        // Counts
        public int AirTerminalCount    { get; set; }
        public int DownConductorCount  { get; set; }
        public int EarthElectrodeCount { get; set; }
        public int BondingCount        { get; set; }
        public int SpdCount            { get; set; }

        // Separation
        public double KcFactor { get; set; }
        public int    SepDistanceViolations { get; set; }

        // Risk
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

        // Verdict
        public string ComplianceVerdict { get; set; } = "";
        public int    ComplianceChecksPass { get; set; }
        public int    ComplianceChecksWarn { get; set; }
        public int    ComplianceChecksFail { get; set; }

        public string LastTestDate { get; set; } = "";
        public string CertReference { get; set; } = "";

        public int SpdCoordinationPass { get; set; }
        public int SpdCoordinationWarn { get; set; }
        public int SpdCoordinationFail { get; set; }
    }

    [HttpPost]
    public async Task<ActionResult> PushRecord(Guid projectId, [FromBody] PushLpsRecordRequest req)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        var rec = new LpsRecord
        {
            ProjectId  = projectId,
            TenantId   = tenantId,
            CapturedBy = User.FindFirst("display_name")?.Value ?? "Unknown",

            LpsClass                  = req.LpsClass ?? "",
            RollingSphereRadiusM      = req.RollingSphereRadiusM,
            MeshSizeM                 = req.MeshSizeM,
            InspectionIntervalMonths  = req.InspectionIntervalMonths,
            EarthResistanceTargetOhm  = req.EarthResistanceTargetOhm,
            GroundFlashDensity        = req.GroundFlashDensity,

            AirTerminalCount    = req.AirTerminalCount,
            DownConductorCount  = req.DownConductorCount,
            EarthElectrodeCount = req.EarthElectrodeCount,
            BondingCount        = req.BondingCount,
            SpdCount            = req.SpdCount,

            KcFactor              = req.KcFactor,
            SepDistanceViolations = req.SepDistanceViolations,

            AnnualStrikeFrequencyNd = req.AnnualStrikeFrequencyNd,
            CollectionAreaM2        = req.CollectionAreaM2,
            RiskR1 = req.RiskR1, RiskR2 = req.RiskR2, RiskR3 = req.RiskR3, RiskR4 = req.RiskR4,
            TolerableR1 = req.TolerableR1, TolerableR2 = req.TolerableR2,
            TolerableR3 = req.TolerableR3, TolerableR4 = req.TolerableR4,
            RecommendedClass = req.RecommendedClass ?? "",

            ComplianceVerdict    = req.ComplianceVerdict ?? "",
            ComplianceChecksPass = req.ComplianceChecksPass,
            ComplianceChecksWarn = req.ComplianceChecksWarn,
            ComplianceChecksFail = req.ComplianceChecksFail,

            LastTestDate  = req.LastTestDate ?? "",
            CertReference = req.CertReference ?? "",

            SpdCoordinationPass = req.SpdCoordinationPass,
            SpdCoordinationWarn = req.SpdCoordinationWarn,
            SpdCoordinationFail = req.SpdCoordinationFail
        };

        _db.LpsRecords.Add(rec);
        await _db.SaveChangesAsync();

        // Broadcast so dashboards / mobile inbox refresh without polling.
        _ = _hub.Clients.Group($"project-{projectId}").SendAsync("LpsRecordPushed", new
        {
            projectId,
            recordId = rec.Id,
            lpsClass = rec.LpsClass,
            verdict  = rec.ComplianceVerdict,
            capturedAt = rec.CapturedAt
        });

        return CreatedAtAction(nameof(GetLatest), new { projectId }, rec);
    }

    // ── Read (GET) ─────────────────────────────────────────────────

    [HttpGet("latest")]
    public async Task<ActionResult<LpsRecord>> GetLatest(Guid projectId)
    {
        var tenantId = GetTenantId();
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;
        var rec = await _db.LpsRecords
            .Where(r => r.TenantId == tenantId && r.ProjectId == projectId)
            .OrderByDescending(r => r.CapturedAt)
            .FirstOrDefaultAsync();
        if (rec == null) return NotFound("No LPS records for project");
        return rec;
    }

    [HttpGet("history")]
    public async Task<ActionResult<IEnumerable<LpsRecord>>> GetHistory(Guid projectId, int limit = 50)
    {
        var tenantId = GetTenantId();
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;
        var rows = await _db.LpsRecords
            .Where(r => r.TenantId == tenantId && r.ProjectId == projectId)
            .OrderByDescending(r => r.CapturedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync();
        return rows;
    }

    [HttpGet("trend")]
    public async Task<ActionResult> GetTrend(Guid projectId, int days = 90)
    {
        var tenantId = GetTenantId();
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;
        var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 7, 365));
        var rows = await _db.LpsRecords
            .Where(r => r.TenantId == tenantId && r.ProjectId == projectId && r.CapturedAt >= since)
            .OrderBy(r => r.CapturedAt)
            .Select(r => new {
                r.CapturedAt, r.LpsClass, r.ComplianceVerdict,
                r.RiskR1, r.RiskR2, r.RiskR3, r.RiskR4,
                r.SepDistanceViolations,
                r.SpdCoordinationFail
            })
            .ToListAsync();
        return Ok(new { projectId, days, samples = rows.Count, rows });
    }

    // ── Helpers ────────────────────────────────────────────────────

    private Guid GetTenantId()
    {
        var claim = User.FindFirst("tenant_id");
        return claim != null && Guid.TryParse(claim.Value, out var id) ? id : Guid.Empty;
    }
}
