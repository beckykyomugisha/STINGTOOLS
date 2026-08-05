using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Authorization;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Phase 178c (T3-24) — Document revision history.
///
/// <list type="bullet">
///   <item><c>GET  /api/projects/{pid}/documents/{did}/revisions</c></item>
///   <item><c>POST /api/projects/{pid}/documents/{did}/revisions</c> — manual snapshot</item>
/// </list>
///
/// <para>Auto-created revisions land here from <c>DocumentsController</c>'s
/// CDE state transition path.</para>
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/documents/{documentId:guid}/revisions")]
[Authorize]
[ProjectAccess]
public class DocumentRevisionsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;

    public DocumentRevisionsController(PlanscapeDbContext db, ITenantContext tenant, IAuditService audit)
    {
        _db = db; _tenant = tenant; _audit = audit;
    }

    /// <summary>List revisions for a document, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult> List(Guid projectId, Guid documentId, CancellationToken ct)
    {
        var doc = await _db.Documents.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId && d.ProjectId == projectId, ct);
        if (doc == null) return NotFound();

        var rows = await _db.DocumentRevisions.AsNoTracking()
            .Where(r => r.DocumentId == documentId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id, r.Revision, r.CdeStateAtRevision, r.SuitabilityAtRevision,
                r.FilePath, r.FileSizeBytes, r.ContentHash,
                r.CreatedBy, r.CreatedAt, r.CommentSummary, r.Source
            })
            .ToListAsync(ct);

        return Ok(new { documentId, currentRevision = doc.Revision, revisions = rows });
    }

    /// <summary>Manually mint a revision snapshot for the current state of the document.</summary>
    [HttpPost]
    public async Task<ActionResult> Create(
        Guid projectId, Guid documentId,
        [FromBody] CreateRevisionRequest req,
        CancellationToken ct)
    {
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId && d.ProjectId == projectId, ct);
        if (doc == null) return NotFound();

        var revision = string.IsNullOrWhiteSpace(req.Revision) ? (doc.Revision ?? string.Empty) : req.Revision!;
        var displayName = User.FindFirst("display_name")?.Value ?? "Unknown";

        var rev = new DocumentRevision
        {
            TenantId              = _tenant.TenantId,
            DocumentId            = documentId,
            Revision              = revision,
            CdeStateAtRevision    = doc.CdeStatus,
            SuitabilityAtRevision = doc.SuitabilityCode,
            FilePath              = doc.FilePath,
            FileSizeBytes         = doc.FileSizeBytes,
            ContentHash           = doc.ContentHash,
            CreatedBy             = displayName,
            CommentSummary        = req.CommentSummary,
            Source                = "manual",
        };
        _db.DocumentRevisions.Add(rev);

        // Bump the document's revision label if the caller asked for a new one.
        if (!string.IsNullOrWhiteSpace(req.Revision) && req.Revision != doc.Revision)
        {
            doc.Revision = req.Revision!;
            doc.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("CREATE", "DocumentRevision", rev.Id.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { rev.Revision, rev.Source }));

        return CreatedAtAction(nameof(List), new { projectId, documentId },
            new { rev.Id, rev.Revision, rev.CdeStateAtRevision, rev.CreatedAt });
    }
}

public record CreateRevisionRequest(string? Revision, string? CommentSummary = null);
