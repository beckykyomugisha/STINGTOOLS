using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Pillar D Seam 1 (T3) + T1 — make the asset register a live twin registry.
/// seed-from-model turns serviceable equipment in the model projection into
/// twins (the handover register becomes the operational source-of-truth);
/// provision-from-cx provisions a single device at commissioning sign-off.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/twins/provision")]
[Authorize]
public class TwinProvisioningController : ControllerBase
{
    private readonly ITwinProvisioningService _provision;
    private readonly PlanscapeDbContext _db;

    public TwinProvisioningController(ITwinProvisioningService provision, PlanscapeDbContext db)
    {
        _provision = provision;
        _db = db;
    }

    /// <summary>POST seed-from-model — create twins for serviceable equipment.</summary>
    [HttpPost("seed-from-model")]
    public async Task<ActionResult<object>> SeedFromModel(Guid projectId, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return NotFound();
        var added = await _provision.SeedFromModelAsync(projectId, ct);
        return Ok(new { added });
    }

    /// <summary>POST from-cx — provision + bind one device at CX sign-off.</summary>
    [HttpPost("from-cx")]
    public async Task<ActionResult<object>> FromCx(
        Guid projectId, [FromBody] CxProvisionRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.DeviceId) || string.IsNullOrWhiteSpace(req.IfcGlobalId))
            return BadRequest("deviceId and ifcGlobalId are required");
        if (!await ProjectInTenant(projectId, ct)) return NotFound();

        var twin = await _provision.ProvisionFromCxAsync(new TwinProvisionRequest(
            projectId, req.DeviceId, req.IfcGlobalId, req.Protocol ?? "mqtt",
            req.AssetTag, req.Serial, req.Manufacturer, req.Model,
            req.CommissioningRef, req.PressureRegime, req.DesignDeltaPa), ct);

        return Ok(new { twin.DeviceId, twin.IfcGlobalId, twin.HealthState });
    }

    private async Task<bool> ProjectInTenant(Guid projectId, CancellationToken ct)
    {
        var c = User.FindFirst("tenant_id")?.Value;
        var tenantId = c != null && Guid.TryParse(c, out var id) ? id : Guid.Empty;
        return await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == tenantId, ct);
    }

    public class CxProvisionRequest
    {
        public string DeviceId { get; set; } = "";
        public string IfcGlobalId { get; set; } = "";
        public string? Protocol { get; set; }
        public string? AssetTag { get; set; }
        public string? Serial { get; set; }
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public string? CommissioningRef { get; set; }
        public string? PressureRegime { get; set; }
        public double? DesignDeltaPa { get; set; }
    }
}
