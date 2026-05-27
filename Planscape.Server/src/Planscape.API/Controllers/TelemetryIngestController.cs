using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;

namespace Planscape.API.Controllers;

/// <summary>
/// Pillar B (5A) — HTTP telemetry ingest. The Planscape.Edge agent / protocol
/// adapters (5C) POST decoded batches here; the same IDeviceTwinService path
/// updates last-known-state and the TwinHub pushes a K3 overlay + live state.
/// (Rule evaluation + work-order automation wire into this path in 6A.)
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/telemetry")]
[Authorize]
public class TelemetryIngestController : ControllerBase
{
    private readonly IDeviceTwinService _twins;
    private readonly ITwinRuleEvaluator _rules;
    private readonly IHubContext<TwinHub> _hub;
    private readonly PlanscapeDbContext _db;

    public TelemetryIngestController(
        IDeviceTwinService twins,
        IHubContext<TwinHub> hub,
        PlanscapeDbContext db,
        ITwinRuleEvaluator rules)
    {
        _twins = twins;
        _hub = hub;
        _db = db;
        _rules = rules;
    }

    /// <summary>POST .../ingest — a batch of readings.</summary>
    [HttpPost("ingest")]
    public async Task<ActionResult<object>> Ingest(
        Guid projectId, [FromBody] IngestRequest req, CancellationToken ct)
    {
        if (req?.Readings is null || req.Readings.Count == 0) return BadRequest("readings is empty");
        if (!await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == GetTenantId(), ct))
            return NotFound();

        var readings = req.Readings
            .Where(r => !string.IsNullOrWhiteSpace(r.DeviceId) && !string.IsNullOrWhiteSpace(r.Metric))
            .Select(r => new TelemetryReading(r.DeviceId, r.Metric, r.Value, r.Unit, r.Ts))
            .ToList();
        if (readings.Count == 0) return BadRequest("no valid readings");

        var touched = await _twins.IngestAsync(projectId, readings, ct);

        // 6A — evaluate rules (no-op evaluator in 5A; real engine in 6A).
        await _rules.EvaluateAsync(projectId, readings, ct);

        // Live push: K3 overlay + state for the viewer + mobile Live tab.
        var refreshed = await _twins.ListAsync(projectId, ct);
        await TwinHub.NotifyOverlay(_hub, projectId, TwinOverlayBuilder.Build(refreshed));
        await TwinHub.NotifyState(_hub, projectId, refreshed.Select(t => new
        {
            t.DeviceId, t.IfcGlobalId, t.HealthState, t.LastSeenAt, t.LastStateJson,
        }));

        return Ok(new { ingested = readings.Count, devices = touched.Count });
    }

    private Guid GetTenantId()
    {
        var c = User.FindFirst("tenant_id")?.Value;
        return c != null && Guid.TryParse(c, out var id) ? id : Guid.Empty;
    }

    public class IngestRequest { public List<ReadingDto> Readings { get; set; } = new(); }
    public class ReadingDto
    {
        public string DeviceId { get; set; } = "";
        public string Metric { get; set; } = "";
        public double Value { get; set; }
        public string? Unit { get; set; }
        public DateTime? Ts { get; set; }
    }
}
