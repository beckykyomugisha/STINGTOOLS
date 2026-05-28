using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Pillar A (3B, G5) — model-diff visualisation. Projects a stored
/// <c>IfcElementSnapshot</c> generation (the per-upload Added/Modified/Deleted
/// classification computed by IfcDeltaService) into a K3 ViewerOverlayProfile
/// the viewer renders with one applyOverlay() call. No bespoke diff render
/// path — the diff IS just another overlay feed.
/// </summary>
[ApiController]
[Authorize]
public class ModelDiffController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    public ModelDiffController(PlanscapeDbContext db) => _db = db;

    /// <summary>
    /// GET /api/projects/{projectId}/models/{modelId}/diff?uploadSequence=N
    /// Defaults to the latest generation. Returns counts + a ready-to-render
    /// overlay profile (added=green, modified=amber, deleted=red).
    /// </summary>
    [HttpGet("/api/projects/{projectId:guid}/models/{modelId:guid}/diff")]
    public async Task<ActionResult<object>> Diff(
        Guid projectId, Guid modelId, [FromQuery] int? uploadSequence = null, CancellationToken ct = default)
    {
        var tenantId = GetTenantId();
        if (!await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == tenantId, ct))
            return NotFound();

        var seq = uploadSequence ?? await _db.IfcElementSnapshots
            .Where(s => s.ProjectModelId == modelId)
            .MaxAsync(s => (int?)s.UploadSequence, ct) ?? 0;
        if (seq == 0) return Ok(EmptyDiff(seq));

        var rows = await _db.IfcElementSnapshots
            .Where(s => s.ProjectModelId == modelId && s.UploadSequence == seq
                        && s.ChangeKind != "Unchanged")
            .Select(s => new { s.IfcGuid, s.ChangeKind })
            .ToListAsync(ct);

        const string Green = "#27ae60", Amber = "#f39c12", Red = "#e74c3c";
        var map = new Dictionary<string, string>();
        int added = 0, modified = 0, deleted = 0;
        foreach (var r in rows)
        {
            string? col = r.ChangeKind switch
            {
                "Added"    => Green,
                "Modified" => Amber,
                "Deleted"  => Red,
                _          => null,
            };
            if (col == null || string.IsNullOrWhiteSpace(r.IfcGuid)) continue;
            map[r.IfcGuid] = col;
            if (r.ChangeKind == "Added") added++;
            else if (r.ChangeKind == "Modified") modified++;
            else deleted++;
        }

        return Ok(new
        {
            modelId,
            uploadSequence = seq,
            added, modified, deleted,
            overlay = new
            {
                source = "diff",
                mode = "map",
                title = $"Changes (upload {seq})",
                guidColorMap = map,
                defaultColor = (string?)null,
                legend = new[]
                {
                    new { label = "Added", color = Green },
                    new { label = "Modified", color = Amber },
                    new { label = "Deleted", color = Red },
                },
            },
        });
    }

    private static object EmptyDiff(int seq) => new
    {
        uploadSequence = seq, added = 0, modified = 0, deleted = 0,
        overlay = new { source = "diff", mode = "map", guidColorMap = new Dictionary<string, string>() },
    };

    private Guid GetTenantId()
    {
        var c = User.FindFirst("tenant_id")?.Value;
        return c != null && Guid.TryParse(c, out var id) ? id : Guid.Empty;
    }
}
