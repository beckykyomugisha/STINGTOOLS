using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;

namespace Planscape.API.Controllers;

/// <summary>
/// Pillar B (5A) — device-twin query + binding. Powers the mobile Live tab
/// (RAG list) and the viewer's twin overlay (K3). Binding records the K1 iot
/// mapping so telemetry resolves to the model element.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/twins")]
[Authorize]
public class DeviceTwinsController : ControllerBase
{
    private readonly IDeviceTwinService _twins;
    private readonly ITwinBindingService _binding;
    private readonly PlanscapeDbContext _db;

    public DeviceTwinsController(
        IDeviceTwinService twins, ITwinBindingService binding, PlanscapeDbContext db)
    {
        _twins = twins;
        _binding = binding;
        _db = db;
    }

    /// <summary>GET — all twins with health (the RAG list).</summary>
    [HttpGet]
    public async Task<ActionResult<object>> List(Guid projectId, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return NotFound();
        var twins = await _twins.ListAsync(projectId, ct);
        return Ok(twins.Select(t => new
        {
            t.DeviceId, t.IfcGlobalId, t.Protocol, t.AssetTag, t.Serial,
            t.Manufacturer, t.Model, t.HealthState, t.LastSeenAt, t.LastStateJson,
        }));
    }

    /// <summary>GET — K3 overlay profile coloured by twin health.</summary>
    [HttpGet("overlay")]
    public async Task<ActionResult<object>> Overlay(Guid projectId, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return NotFound();
        var twins = await _twins.ListAsync(projectId, ct);
        return Ok(TwinOverlayBuilder.Build(twins));
    }

    /// <summary>GET — one twin.</summary>
    [HttpGet("{deviceId}")]
    public async Task<ActionResult<object>> Get(Guid projectId, string deviceId, CancellationToken ct)
    {
        var t = await _twins.GetAsync(projectId, deviceId, ct);
        return t is null ? NotFound() : Ok(t);
    }

    /// <summary>GET — recent telemetry for one metric (sparkline).</summary>
    [HttpGet("{deviceId}/telemetry")]
    public async Task<ActionResult<object>> Telemetry(
        Guid projectId, string deviceId, [FromQuery] string metric, [FromQuery] int max = 200,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(metric)) return BadRequest("metric is required");
        var pts = await _twins.RecentAsync(projectId, deviceId, metric, max, ct);
        return Ok(pts.Select(p => new { p.Ts, p.Value, p.Unit }));
    }

    /// <summary>POST — bind a device to a model element (records K1 iot mapping).</summary>
    [HttpPost("bind")]
    public async Task<ActionResult<object>> Bind(
        Guid projectId, [FromBody] BindRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.DeviceId) || string.IsNullOrWhiteSpace(req.IfcGlobalId))
            return BadRequest("deviceId and ifcGlobalId are required");
        if (!await ProjectInTenant(projectId, ct)) return NotFound();

        var twin = await _binding.BindAsync(new TwinBindRequest(
            projectId, req.DeviceId, req.IfcGlobalId, req.Protocol ?? "mqtt",
            req.AssetTag, req.Serial, req.Manufacturer, req.Model, req.Label, req.MetadataJson), ct);

        return Ok(new { twin.DeviceId, twin.IfcGlobalId, twin.HealthState });
    }

    private async Task<bool> ProjectInTenant(Guid projectId, CancellationToken ct)
    {
        var c = User.FindFirst("tenant_id")?.Value;
        var tenantId = c != null && Guid.TryParse(c, out var id) ? id : Guid.Empty;
        return await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == tenantId, ct);
    }

    public class BindRequest
    {
        public string DeviceId { get; set; } = "";
        public string IfcGlobalId { get; set; } = "";
        public string? Protocol { get; set; }
        public string? AssetTag { get; set; }
        public string? Serial { get; set; }
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public string? Label { get; set; }
        public string? MetadataJson { get; set; }
    }
}
