using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using StingBIM.Core.Entities;
using StingBIM.Infrastructure.Data;

namespace StingBIM.API.Controllers;

/// <summary>
/// ISO 19650 CDE document management with validated state transitions.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/[controller]")]
[Authorize]
public class DocumentsController : ControllerBase
{
    private readonly StingBimDbContext _db;

    // ISO 19650-2 CDE state machine — valid one-way transitions
    private static readonly Dictionary<string, string[]> ValidTransitions = new()
    {
        ["WIP"] = new[] { "SHARED" },
        ["SHARED"] = new[] { "PUBLISHED", "WIP" }, // WIP = rework
        ["PUBLISHED"] = new[] { "ARCHIVE", "SUPERSEDED" },
        ["ARCHIVE"] = Array.Empty<string>(),
        ["SUPERSEDED"] = Array.Empty<string>(),
        ["WITHDRAWN"] = Array.Empty<string>(),
        ["OBSOLETE"] = Array.Empty<string>()
    };

    // Suitability code mapping per CDE state
    private static readonly Dictionary<string, string> DefaultSuitability = new()
    {
        ["WIP"] = "S0", ["SHARED"] = "S3", ["PUBLISHED"] = "S4", ["ARCHIVE"] = "S7"
    };

    public DocumentsController(StingBimDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult> GetDocuments(Guid projectId,
        [FromQuery] string? cdeStatus = null, [FromQuery] string? discipline = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var tenantId = GetTenantId();
        var query = _db.Documents.Where(d => d.ProjectId == projectId && d.Project!.TenantId == tenantId);

        if (!string.IsNullOrEmpty(cdeStatus)) query = query.Where(d => d.CdeStatus == cdeStatus);
        if (!string.IsNullOrEmpty(discipline)) query = query.Where(d => d.Discipline == discipline);

        var total = await query.CountAsync();
        var docs = await query.OrderByDescending(d => d.UploadedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new { docs, total, page, pageSize });
    }

    [HttpPost]
    public async Task<ActionResult> CreateDocument(Guid projectId, [FromBody] CreateDocumentRequest req)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");

        var doc = new DocumentRecord
        {
            ProjectId = projectId,
            FileName = req.FileName,
            DocumentType = req.DocumentType ?? "",
            CdeStatus = "WIP",
            SuitabilityCode = "S0",
            Discipline = req.Discipline,
            Revision = req.Revision,
            UploadedBy = User.FindFirst("display_name")?.Value ?? "Unknown",
            StatusHistoryJson = JsonConvert.SerializeObject(new[]
            {
                new { timestamp = DateTime.UtcNow, oldState = "", newState = "WIP", suitability = "S0",
                    user = User.FindFirst("display_name")?.Value ?? "Unknown" }
            })
        };

        _db.Documents.Add(doc);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetDocuments), new { projectId }, doc);
    }

    /// <summary>
    /// CDE state transition with ISO 19650 validation.
    /// </summary>
    [HttpPut("{docId}/state")]
    public async Task<ActionResult> TransitionState(Guid projectId, Guid docId, [FromBody] CdeTransitionRequest req)
    {
        var tenantId = GetTenantId();
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == docId && d.ProjectId == projectId && d.Project!.TenantId == tenantId);
        if (doc == null) return NotFound();

        // Validate transition
        if (!ValidTransitions.TryGetValue(doc.CdeStatus, out var validTargets))
            return BadRequest($"Unknown current state: {doc.CdeStatus}");

        if (!validTargets.Contains(req.NewState))
            return BadRequest($"Invalid CDE transition: {doc.CdeStatus} → {req.NewState}. Valid: {string.Join(", ", validTargets)}");

        var oldState = doc.CdeStatus;
        doc.CdeStatus = req.NewState;
        doc.SuitabilityCode = req.SuitabilityCode ?? DefaultSuitability.GetValueOrDefault(req.NewState, doc.SuitabilityCode);
        if (req.Revision != null) doc.Revision = req.Revision;

        // Append to status history
        var history = !string.IsNullOrEmpty(doc.StatusHistoryJson)
            ? JsonConvert.DeserializeObject<List<object>>(doc.StatusHistoryJson) ?? new()
            : new List<object>();
        history.Add(new
        {
            timestamp = DateTime.UtcNow, oldState, newState = req.NewState,
            suitability = doc.SuitabilityCode,
            user = User.FindFirst("display_name")?.Value ?? "Unknown"
        });
        doc.StatusHistoryJson = JsonConvert.SerializeObject(history);

        await _db.SaveChangesAsync();
        return Ok(doc);
    }

    [HttpGet("{docId}/history")]
    public async Task<ActionResult> GetHistory(Guid projectId, Guid docId)
    {
        var tenantId = GetTenantId();
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == docId && d.ProjectId == projectId && d.Project!.TenantId == tenantId);
        if (doc == null) return NotFound();

        var history = !string.IsNullOrEmpty(doc.StatusHistoryJson)
            ? JsonConvert.DeserializeObject<List<object>>(doc.StatusHistoryJson)
            : new List<object>();

        return Ok(new { doc.Id, doc.FileName, doc.CdeStatus, doc.SuitabilityCode, history });
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

public record CreateDocumentRequest(string FileName, string? DocumentType, string? Discipline, string? Revision);
public record CdeTransitionRequest(string NewState, string? SuitabilityCode, string? Revision);
