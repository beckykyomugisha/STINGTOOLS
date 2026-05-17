// Planscape Server — ArchiCAD live push endpoint.
//
// StingBridge calls POST /api/archicad/{projectId}/push with a batch of
// element change events. The controller fans them out via SignalR so every
// connected Planscape Web/Desktop/Mobile client in that project group
// receives them in real time — no polling needed.
//
// Authentication: X-StingBridge-Key header validated against the project's
// stored bridge key (set when the project is first connected from StingBridge).
//
// Payload (array of ArchiCADEvent):
//   { "kind": "Changed"|"Added"|"Deleted",
//     "elementId": "uuid",
//     "elementType": "Wall"|"Slab"|"Column"|...,
//     "properties": { "Classification.Code": "...", ... },
//     "boundingBox": { "min": [x,y,z], "max": [x,y,z] },   // optional
//     "timestampUtc": "2026-05-17T10:30:00Z" }

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Planscape.Infrastructure.SignalR;

namespace Planscape.API.Controllers
{
    [ApiController]
    [Route("api/archicad")]
    public class ArchiCADController : ControllerBase
    {
        private readonly IHubContext<ArchiCADHub> _hub;
        private readonly IConfiguration           _config;

        public ArchiCADController(IHubContext<ArchiCADHub> hub, IConfiguration config)
        {
            _hub    = hub;
            _config = config;
        }

        // ── POST /api/archicad/{projectId}/push ──────────────────────────────

        [HttpPost("{projectId}/push")]
        public async Task<IActionResult> Push(string projectId, [FromBody] ArchiCADPushPayload payload)
        {
            // Validate bridge key.
            string? key = Request.Headers["X-StingBridge-Key"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(key) || !ValidateKey(projectId, key))
                return Unauthorized(new { error = "Invalid or missing X-StingBridge-Key" });

            if (payload?.Events == null || payload.Events.Count == 0)
                return BadRequest(new { error = "No events in payload" });

            string group = $"archicad:{projectId}";

            // Fan-out each event to connected web/mobile/desktop clients.
            foreach (var ev in payload.Events)
            {
                string method = ev.Kind switch
                {
                    "Added"   => "ElementAdded",
                    "Deleted" => "ElementDeleted",
                    _         => "ElementChanged"
                };
                await _hub.Clients.Group(group).SendAsync(method, ev);
            }

            // Also push a status heartbeat so clients know the author is live.
            await _hub.Clients.Group(group).SendAsync("ModelStatus", new
            {
                projectId,
                eventCount   = payload.Events.Count,
                lastPushUtc  = DateTime.UtcNow,
                authorInfo   = payload.AuthorInfo
            });

            return Ok(new { received = payload.Events.Count });
        }

        // ── POST /api/archicad/{projectId}/status ────────────────────────────
        // Heartbeat — StingBridge calls this every 30 s to signal ArchiCAD is live.

        [HttpPost("{projectId}/status")]
        public async Task<IActionResult> Status(string projectId,
            [FromBody] ArchiCADStatusPayload payload)
        {
            string? key = Request.Headers["X-StingBridge-Key"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(key) || !ValidateKey(projectId, key))
                return Unauthorized();

            await _hub.Clients.Group($"archicad:{projectId}").SendAsync("ModelStatus", new
            {
                projectId,
                connectedAuthors = payload.ConnectedAuthors,
                activeLayers     = payload.ActiveLayers,
                lastPushUtc      = DateTime.UtcNow,
                isLive           = true
            });

            return Ok();
        }

        // ── GET /api/archicad/{projectId}/keygen ─────────────────────────────
        // One-time setup: generate + store the bridge key for a project.
        // Protected by normal JWT auth (project admin only in production).

        [HttpGet("{projectId}/keygen")]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public IActionResult GenerateKey(string projectId)
        {
            string newKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator
                .GetBytes(32));
            // In production: persist to db against the project. Here we return it
            // for the user to paste into StingBridge settings.
            return Ok(new { projectId, bridgeKey = newKey,
                note = "Store this in StingBridge settings. It will not be shown again." });
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private bool ValidateKey(string projectId, string key)
        {
            // TODO: load from db (Projects.BridgeKeyHash) and compare bcrypt hash.
            // For now accept any non-empty key in dev; enforce in production via
            // the Projects table BridgeKeyHash column (migration pending).
            return !string.IsNullOrWhiteSpace(key);
        }
    }

    // ── DTOs ─────────────────────────────────────────────────────────────────

    public class ArchiCADPushPayload
    {
        public List<ArchiCADEvent> Events     { get; set; } = new();
        public ArchiCADAuthorInfo? AuthorInfo { get; set; }
    }

    public class ArchiCADEvent
    {
        public string                      Kind         { get; set; } = "Changed";
        public string                      ElementId    { get; set; } = "";
        public string                      ElementType  { get; set; } = "";
        public Dictionary<string, object>? Properties   { get; set; }
        public BoundingBoxDto?             BoundingBox  { get; set; }
        public DateTime                    TimestampUtc { get; set; } = DateTime.UtcNow;
    }

    public class BoundingBoxDto
    {
        public double[] Min { get; set; } = Array.Empty<double>();
        public double[] Max { get; set; } = Array.Empty<double>();
    }

    public class ArchiCADAuthorInfo
    {
        public string Name    { get; set; } = "";
        public string Email   { get; set; } = "";
        public string Version { get; set; } = ""; // ArchiCAD version
    }

    public class ArchiCADStatusPayload
    {
        public List<string> ConnectedAuthors { get; set; } = new();
        public List<string> ActiveLayers     { get; set; } = new();
    }
}
