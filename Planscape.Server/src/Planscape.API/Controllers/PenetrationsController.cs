using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Phase 178f — penetration commissioning endpoints. The mobile app
/// scans a QR encoding (controlNumber + pfvUuid + projectId), captures
/// installer / inspector + photo + GPS, and POSTs here. Reads cover
/// the audit / dashboard side so coordinators can see install
/// progress without opening Revit.
///
/// Routes are tenant-scoped via TenantResolutionMiddleware.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/penetrations")]
[Authorize]
public class PenetrationsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    public PenetrationsController(PlanscapeDbContext db) { _db = db; }

    public sealed class SignoffRequest
    {
        public string PenetrationControlNumber { get; set; } = "";
        public string PfvUuid { get; set; } = "";
        /// <summary>Optional IFC GlobalId of the penetrated host element (cross-host identity key).</summary>
        public string? ElementIfcGlobalId { get; set; }
        public string HostType { get; set; } = "";
        public string FireRating { get; set; } = "";
        public string Certification { get; set; } = "";
        public string ProductKind { get; set; } = "FIRESTOP";
        public string InstallerName { get; set; } = "";
        public string InstallerCompany { get; set; } = "";
        public DateTime? InstalledAt { get; set; }
        public string InspectorName { get; set; } = "";
        public DateTime? InspectedAt { get; set; }
        public string Status { get; set; } = "INSTALLED";
        public string Notes { get; set; } = "";
        public string? PhotoBlobId { get; set; }
        public double? GpsLat { get; set; }
        public double? GpsLon { get; set; }
    }

    /// <summary>
    /// Create or update the sign-off row for a control number. Mobile
    /// app calls this on submit. Idempotent on
    /// (projectId, controlNumber, pfvUuid).
    /// </summary>
    [HttpPut("{controlNumber}/signoff")]
    public async Task<IActionResult> Upsert(Guid projectId, string controlNumber,
        [FromBody] SignoffRequest req)
    {
        if (string.IsNullOrWhiteSpace(controlNumber)) return BadRequest("controlNumber required.");
        if (req == null) return BadRequest("body required.");

        var tenantId = ResolveTenantId();
        var existing = await _db.PenetrationSignoffs
            .Where(p => p.ProjectId == projectId
                     && p.PenetrationControlNumber == controlNumber
                     && (string.IsNullOrEmpty(req.PfvUuid) || p.PfvUuid == req.PfvUuid))
            .FirstOrDefaultAsync();

        if (existing == null)
        {
            existing = new PenetrationSignoff
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = projectId,
                PenetrationControlNumber = controlNumber,
                PfvUuid = req.PfvUuid ?? "",
                CapturedAt = DateTime.UtcNow,
                CapturedBy = User.Identity?.Name ?? "",
            };
            _db.PenetrationSignoffs.Add(existing);
        }

        if (req.ElementIfcGlobalId != null) existing.ElementIfcGlobalId = req.ElementIfcGlobalId;
        existing.HostType         = req.HostType ?? existing.HostType;
        existing.FireRating       = req.FireRating ?? existing.FireRating;
        existing.Certification    = req.Certification ?? existing.Certification;
        existing.ProductKind      = req.ProductKind ?? existing.ProductKind;
        existing.InstallerName    = req.InstallerName ?? existing.InstallerName;
        existing.InstallerCompany = req.InstallerCompany ?? existing.InstallerCompany;
        if (req.InstalledAt.HasValue) existing.InstalledAt = req.InstalledAt;
        existing.InspectorName    = req.InspectorName ?? existing.InspectorName;
        if (req.InspectedAt.HasValue) existing.InspectedAt = req.InspectedAt;
        existing.Status           = req.Status ?? existing.Status;
        existing.Notes            = req.Notes ?? existing.Notes;
        if (req.PhotoBlobId != null) existing.PhotoBlobId = req.PhotoBlobId;
        if (req.GpsLat.HasValue) existing.GpsLat = req.GpsLat;
        if (req.GpsLon.HasValue) existing.GpsLon = req.GpsLon;

        await _db.SaveChangesAsync();
        return Ok(existing);
    }

    [HttpGet("{controlNumber}/signoff")]
    public async Task<IActionResult> GetByControlNumber(Guid projectId, string controlNumber)
    {
        var row = await _db.PenetrationSignoffs
            .Where(p => p.ProjectId == projectId && p.PenetrationControlNumber == controlNumber)
            .OrderByDescending(p => p.CapturedAt)
            .FirstOrDefaultAsync();
        return row == null ? NotFound() : Ok(row);
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid projectId,
        [FromQuery] string? status = null, [FromQuery] string? hostType = null)
    {
        var q = _db.PenetrationSignoffs.Where(p => p.ProjectId == projectId);
        if (!string.IsNullOrEmpty(status)) q = q.Where(p => p.Status == status);
        if (!string.IsNullOrEmpty(hostType)) q = q.Where(p => p.HostType == hostType);
        var rows = await q.OrderByDescending(p => p.CapturedAt).Take(500).ToListAsync();
        return Ok(rows);
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(Guid projectId)
    {
        var rows = await _db.PenetrationSignoffs
            .Where(p => p.ProjectId == projectId)
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();
        var byHost = await _db.PenetrationSignoffs
            .Where(p => p.ProjectId == projectId)
            .GroupBy(p => p.HostType)
            .Select(g => new { HostType = g.Key, Count = g.Count() })
            .ToListAsync();
        return Ok(new { byStatus = rows, byHost });
    }

    private Guid ResolveTenantId()
    {
        // TenantResolutionMiddleware stamps the tenant id on
        // HttpContext.Items["TenantId"]. Falls back to Guid.Empty for
        // dev / single-tenant deployments — same pattern as
        // HealthcareController.
        if (HttpContext.Items.TryGetValue("TenantId", out var v) && v is Guid g) return g;
        return Guid.Empty;
    }
}
