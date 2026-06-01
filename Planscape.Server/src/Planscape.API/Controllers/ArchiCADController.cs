// Planscape Server — ArchiCAD live push endpoint.
//
// StingBridge calls POST /api/archicad/{projectId}/push with a batch of
// element change events. The controller fans them out via SignalR so every
// connected Planscape Web/Desktop/Mobile client in that project group
// receives them in real time — no polling needed.
//
// Authentication: X-StingBridge-Key header validated against the project's
// stored BCrypt bridge key hash (set when the project is first connected
// from StingBridge via GET /api/archicad/{id}/keygen by an authorised user).
//
// Payload (array of ArchiCADEvent):
//   { "kind": "Changed"|"Added"|"Deleted",
//     "elementId": "uuid",
//     "elementType": "Wall"|"Slab"|"Column"|...,
//     "properties": { "Classification.Code": "...", ... },
//     "boundingBox": { "min": [x,y,z], "max": [x,y,z] },   // optional
//     "timestampUtc": "2026-05-17T10:30:00Z" }

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;
using BCrypt.Net;
using System.Security.Cryptography;

namespace Planscape.API.Controllers
{
    /// <summary>
    /// In-memory ring buffer of recent ArchiCAD events per project.
    /// Capped at 200 events per project to bound memory usage.
    /// Populated by Push; consumed by GetRecentEvents for late-join clients.
    /// </summary>
    internal static class ArchiCADEventBuffer
    {
        private const int MaxPerProject = 200;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, System.Collections.Generic.Queue<ArchiCADEvent>>
            _store = new();

        public static void Add(Guid projectId, System.Collections.Generic.IEnumerable<ArchiCADEvent> events)
        {
            var q = _store.GetOrAdd(projectId, _ => new System.Collections.Generic.Queue<ArchiCADEvent>());
            lock (q)
            {
                foreach (var ev in events)
                {
                    q.Enqueue(ev);
                    if (q.Count > MaxPerProject) q.Dequeue();
                }
            }
        }

        public static System.Collections.Generic.List<ArchiCADEvent> Get(Guid projectId, int last = 200)
        {
            if (!_store.TryGetValue(projectId, out var q)) return new();
            lock (q) return q.TakeLast(last).ToList();
        }

        public static void Clear(Guid projectId) => _store.TryRemove(projectId, out _);
    }

    [ApiController]
    [Route("api/archicad")]
    public class ArchiCADController : ControllerBase
    {
        private readonly IHubContext<ArchiCADHub>          _hub;
        private readonly IHubContext<NotificationHub>      _notificationHub;
        private readonly PresenceTracker                   _presence;
        private readonly PlanscapeDbContext                _db;
        private readonly IServiceScopeFactory              _scopeFactory;

        public ArchiCADController(
            IHubContext<ArchiCADHub> hub,
            IHubContext<NotificationHub> notificationHub,
            PresenceTracker presence,
            PlanscapeDbContext db,
            IServiceScopeFactory scopeFactory)
        {
            _hub             = hub;
            _notificationHub = notificationHub;
            _presence        = presence;
            _db              = db;
            _scopeFactory    = scopeFactory;
        }

        /// <summary>
        /// Property-dict keys (case-insensitive) under which StingBridge may
        /// carry the element's IFC GlobalId when the dedicated
        /// <see cref="ArchiCADEvent.IfcGlobalId"/> field isn't populated.
        /// </summary>
        private static readonly string[] IfcGuidPropertyKeys =
        {
            "IfcGlobalId", "IfcGuid", "GlobalId", "IFC_GlobalId",
            "General.IfcGlobalId", "IFC GUID",
        };

        /// <summary>
        /// Resolve an IFC GlobalId for an event: prefer the dedicated field,
        /// fall back to any of the known property-dict keys. Returns null when
        /// no GlobalId is available (the event then can't seed identity).
        /// </summary>
        private static string? ExtractIfcGlobalId(ArchiCADEvent ev)
        {
            if (!string.IsNullOrWhiteSpace(ev.IfcGlobalId)) return ev.IfcGlobalId.Trim();
            if (ev.Properties is null) return null;
            foreach (var key in IfcGuidPropertyKeys)
            {
                if (ev.Properties.TryGetValue(key, out var raw) && raw is not null)
                {
                    var s = raw.ToString();
                    if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
                }
            }
            return null;
        }

        /// <summary>
        /// Rebuild an event's property dict from the persisted JSON. Best-effort:
        /// malformed / null JSON yields null rather than throwing the GET.
        /// </summary>
        private static Dictionary<string, object>? DeserialiseProps(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json); }
            catch { return null; }
        }

        // ── POST /api/archicad/{projectId}/push ──────────────────────────────

        [HttpPost("{projectId:guid}/push")]
        public async Task<IActionResult> Push(Guid projectId, [FromBody] ArchiCADPushPayload payload)
        {
            var project = await AuthenticateBridge(projectId);
            if (project == null)
                return Unauthorized(new { error = "Invalid or missing X-StingBridge-Key" });

            if (payload?.Events == null || payload.Events.Count == 0)
                return BadRequest(new { error = "No events in payload" });

            if (payload.Events.Count > 5_000)
                return BadRequest(new { error = "Maximum 5,000 events per push" });

            string group = $"archicad:{projectId}";

            // Persist events in ring buffer for late-join clients.
            ArchiCADEventBuffer.Add(projectId, payload.Events);

            // Durable persistence — the ring buffer above is in-memory only and
            // is lost on restart. Snapshot the events into ArchiCADEventLog rows
            // and insert fire-and-forget in a fresh DI scope so the recent-events
            // endpoint can fall back to the DB after a cold start. Tenant comes
            // from the bridge-authenticated project (no JWT claim on this path),
            // so TenantId is stamped explicitly.
            // MIGRATION REQUIRED: dotnet ef migrations add ArchiCADEventLogPersistence
            var logTenantId = project.TenantId;
            var nowUtc = DateTime.UtcNow;
            var logRows = payload.Events.Select(ev => new Planscape.Core.Entities.ArchiCADEventLog
            {
                TenantId          = logTenantId,
                ProjectId         = projectId,
                Kind              = ev.Kind ?? "Changed",
                ElementId         = ev.ElementId ?? "",
                ElementType       = ev.ElementType ?? "",
                IfcGlobalId       = ExtractIfcGlobalId(ev),
                PropertiesJson    = (ev.Properties != null && ev.Properties.Count > 0)
                                        ? System.Text.Json.JsonSerializer.Serialize(ev.Properties)
                                        : null,
                EventTimestampUtc = ev.TimestampUtc == default
                                        ? nowUtc
                                        : (ev.TimestampUtc.Kind == DateTimeKind.Utc
                                            ? ev.TimestampUtc
                                            : DateTime.SpecifyKind(ev.TimestampUtc, DateTimeKind.Utc)),
                CreatedAt         = nowUtc,
            }).ToList();
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
                    db.ArchiCADEventLogs.AddRange(logRows);
                    await db.SaveChangesAsync();
                }
                catch { /* event persistence is best-effort; never fails the push */ }
            });

            // Gap M — register the ArchiCAD author in the presence tracker so the
            // BCC "N people viewing" chip includes ArchiCAD authors alongside web/mobile
            // users. We use a synthetic stable connection-id and a deterministic userId
            // derived from the author's email so the presence entry survives multiple
            // pushes from the same author without creating duplicate entries.
            if (payload.AuthorInfo != null && !string.IsNullOrWhiteSpace(payload.AuthorInfo.Name))
            {
                var connId = $"archicad:{projectId}:{payload.AuthorInfo.Email}";
                var emailBytes = System.Text.Encoding.UTF8.GetBytes(
                    payload.AuthorInfo.Email?.ToLowerInvariant() ?? payload.AuthorInfo.Name);
                var userId = new Guid(MD5.HashData(emailBytes).Take(16).ToArray());
                _presence.Join(projectId, connId, new PresentUser(userId, payload.AuthorInfo.Name, "archicad"));

                await _notificationHub.Clients.Group($"project-{projectId}").SendAsync("PresenceChanged", new
                {
                    projectId,
                    users = _presence.ProjectUsers(projectId).Select(u => new
                    {
                        u.UserId,
                        u.DisplayName,
                        u.Source
                    })
                });
            }

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

            // Push a status heartbeat so clients know the author is live.
            await _hub.Clients.Group(group).SendAsync("ModelStatus", new
            {
                projectId,
                eventCount   = payload.Events.Count,
                lastPushUtc  = DateTime.UtcNow,
                authorInfo   = payload.AuthorInfo,
                isLive       = true
            });

            // Seed the cross-host identity table. Each event that carries an
            // IFC GlobalId (dedicated field or Properties dict) maps to its
            // ArchiCAD ElementId. Deleted events are skipped — the element no
            // longer exists to map. Fire-and-forget in a fresh DI scope so it
            // never blocks (or fails) the push response; tenant comes from the
            // bridge-authenticated project, not an HTTP claim.
            var tenantId = project.TenantId;
            var mappings = payload.Events
                .Where(ev => ev.Kind != "Deleted")
                .Select(ev => new { Guid = ExtractIfcGlobalId(ev), ev.ElementId, ev.ElementType })
                .Where(x => !string.IsNullOrWhiteSpace(x.Guid) && !string.IsNullOrWhiteSpace(x.ElementId))
                .Select(x => new Planscape.Core.Interfaces.ElementMappingDto(x.Guid!, x.ElementId, x.ElementType))
                .ToList();

            if (mappings.Count > 0)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var ingest = scope.ServiceProvider.GetRequiredService<IIfcIngestService>();
                        await ingest.UpsertMappingsAsync(
                            tenantId, projectId, Planscape.Core.Constants.MappingHosts.ArchiCad,
                            hostDocumentGuid: null, mappings);
                    }
                    catch { /* identity ingest is best-effort; never fails the push */ }
                });
            }

            return Ok(new { received = payload.Events.Count });
        }

        // ── POST /api/archicad/{projectId}/status ────────────────────────────
        // Heartbeat — StingBridge calls this every 30 s to signal ArchiCAD is live.

        [HttpPost("{projectId:guid}/status")]
        public async Task<IActionResult> Status(Guid projectId,
            [FromBody] ArchiCADStatusPayload payload)
        {
            var project = await AuthenticateBridge(projectId);
            if (project == null) return Unauthorized();

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

        // ── GET /api/archicad/{projectId}/events/recent ──────────────────────────
        // Returns up to the last 200 ArchiCAD events for this project.
        // Used by web/mobile clients on connect to catch up on model state.
        // Also accessible by authenticated users (not just StingBridge).

        [HttpGet("{projectId:guid}/events/recent")]
        [Authorize]
        public async Task<IActionResult> RecentEvents(
            Guid projectId,
            [FromQuery] int count = 200,
            CancellationToken ct = default)
        {
            // Verify the caller is a project member (standard auth check).
            var tenantClaim = User.FindFirst("tenant_id")?.Value;
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Unauthorized(new { error = "Missing tenant claim" });

            var isMember = await _db.ProjectMembers
                .AnyAsync(m => m.ProjectId == projectId && m.UserId == GetUserId(), ct);
            if (!isMember)
            {
                // Also accept bridge-key auth so StingBridge can poll its own buffer.
                var bridgeProject = await AuthenticateBridge(projectId);
                if (bridgeProject == null) return Forbid();
            }

            int safeCount = Math.Clamp(count, 1, 200);
            var events = ArchiCADEventBuffer.Get(projectId, safeCount);

            // Cold-start fallback: the in-memory ring buffer is empty after a
            // restart, so read the most recent persisted rows and rebuild the
            // ArchiCADEvent shape. Tenant scoping is automatic (ITenantScoped
            // query filter); order oldest→newest to match the ring buffer.
            bool fromDb = false;
            if (events.Count == 0)
            {
                var rows = await _db.ArchiCADEventLogs
                    .Where(x => x.ProjectId == projectId)
                    .OrderByDescending(x => x.CreatedAt)
                    .Take(safeCount)
                    .ToListAsync(ct);
                rows.Reverse();
                events = rows.Select(r => new ArchiCADEvent
                {
                    Kind         = r.Kind,
                    ElementId    = r.ElementId,
                    ElementType  = r.ElementType,
                    IfcGlobalId  = r.IfcGlobalId,
                    Properties   = DeserialiseProps(r.PropertiesJson),
                    TimestampUtc = r.EventTimestampUtc,
                }).ToList();
                fromDb = events.Count > 0;
            }

            return Ok(new
            {
                projectId,
                eventCount  = events.Count,
                events,
                bufferCap   = 200,
                source      = fromDb ? "db" : "memory",
                note        = events.Count == 0
                    ? "No recent events. StingBridge has not pushed any changes yet."
                    : fromDb
                        ? $"Last {events.Count} events (restored from durable log after a server restart)."
                        : $"Last {events.Count} events (in-memory ring buffer).",
            });
        }

        // ── GET /api/archicad/{projectId}/keygen ─────────────────────────────
        // One-time setup: generate + store a BCrypt-hashed bridge key.
        // Returns the plaintext key once — it cannot be recovered later.
        // Restricted to authenticated users who are members of the project.

        [HttpGet("{projectId:guid}/keygen")]
        [Authorize]
        public async Task<IActionResult> GenerateKey(Guid projectId)
        {
            // Only a project member may generate a bridge key.
            var tenantClaim = User.FindFirst("tenant_id")?.Value;
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Unauthorized(new { error = "Missing tenant claim" });

            var project = await _db.Projects
                .FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
            if (project == null) return NotFound();

            var isMember = await _db.ProjectMembers
                .AnyAsync(m => m.ProjectId == projectId && m.UserId == GetUserId());
            if (!isMember) return Forbid();

            // Generate 32 random bytes → base64 plaintext key.
            string newKey  = Convert.ToBase64String(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

            // Store only the BCrypt hash — plaintext is never persisted.
            project.BridgeKeyHash = BCrypt.Net.BCrypt.HashPassword(newKey, workFactor: 11);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                projectId,
                bridgeKey = newKey,
                note = "Store this in StingBridge settings. It will not be shown again."
            });
        }

        // ── helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Validates X-StingBridge-Key against the project's stored BCrypt hash.
        /// Returns the project on success, null on auth failure.
        /// </summary>
        private async Task<Planscape.Core.Entities.Project?> AuthenticateBridge(Guid projectId)
        {
            string? key = Request.Headers["X-StingBridge-Key"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(key)) return null;

            var project = await _db.Projects.FindAsync(projectId);
            if (project == null) return null;

            // No bridge key registered yet — reject all bridge calls.
            if (string.IsNullOrWhiteSpace(project.BridgeKeyHash)) return null;

            // BCrypt verify — constant-time, safe against timing attacks.
            if (!BCrypt.Net.BCrypt.Verify(key, project.BridgeKeyHash)) return null;

            return project;
        }

        private Guid GetUserId()
        {
            var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
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

        /// <summary>
        /// Optional dedicated IFC GlobalId for the element. When StingBridge
        /// can resolve it, populating this is preferred over burying the id in
        /// <see cref="Properties"/>; the server falls back to the Properties
        /// dict (see ArchiCADController.IfcGuidPropertyKeys) when this is empty.
        /// Drives the cross-host ExternalElementMapping row.
        /// </summary>
        public string?                     IfcGlobalId  { get; set; }

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
