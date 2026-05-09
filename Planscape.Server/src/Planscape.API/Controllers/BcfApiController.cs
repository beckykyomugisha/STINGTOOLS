using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Authorization;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Phase 178c (T3-21) — BCF 2.1 REST API skeleton (RFC 8259 JSON).
///
/// <para>
/// Implements the buildingSMART BCF API 2.1 specification, scoped to
/// federation read-only endpoints for the first ship. Covers:
/// </para>
///
/// <list type="bullet">
///   <item><c>GET  /bcf/2.1/projects/{pid}/topics</c></item>
///   <item><c>GET  /bcf/2.1/projects/{pid}/topics/{topicGuid}</c></item>
///   <item><c>GET  /bcf/2.1/projects/{pid}/topics/{topicGuid}/comments</c></item>
///   <item><c>GET  /bcf/2.1/projects/{pid}/topics/{topicGuid}/viewpoints</c></item>
/// </list>
///
/// <para>
/// <b>Mapping:</b>
/// </para>
/// <list type="bullet">
///   <item><see cref="BimIssue"/> → BCF topic (BcfGuid is the topic guid).</item>
///   <item><see cref="IssueComment"/> → BCF comment.</item>
///   <item>BimIssue.ModelX/Y/Z → minimal viewpoint with orthogonal camera.</item>
/// </list>
///
/// <para>
/// <b>Authentication:</b> the BCF API spec recommends a separate OAuth2
/// client registration. For Phase 178c we re-use the existing
/// Bearer-JWT-with-X-Tenant scheme (same auth as the rest of the API).
/// A dedicated <c>/bcf/2.1/auth</c> registry / token endpoint is a
/// Phase 178c follow-up.
/// </para>
///
/// <para>
/// <b>Out of scope (deliberately) — Phase 178c follow-up:</b>
/// </para>
/// <list type="bullet">
///   <item>Write endpoints (POST / PUT / DELETE) — return 501.</item>
///   <item>Viewpoint binary endpoints (snapshot.png / viewpoint.bcfv) — return 501.</item>
///   <item>Topic files / related-topics / projects.json registration.</item>
///   <item>Document references / labels / ext-projects.</item>
/// </list>
/// </summary>
[ApiController]
[Route("bcf/2.1/projects/{projectId:guid}/topics")]
[Authorize]
[ProjectAccess]
[Produces("application/json")]
public class BcfApiController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    public BcfApiController(PlanscapeDbContext db) => _db = db;

    /// <summary>BCF 2.1 — list all topics in a project.</summary>
    [HttpGet]
    public async Task<ActionResult> ListTopics(Guid projectId, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();

        var topics = await _db.Issues.AsNoTracking()
            .Where(i => i.ProjectId == projectId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

        return Ok(topics.Select(MapTopic).ToList());
    }

    /// <summary>BCF 2.1 — single topic by guid.</summary>
    [HttpGet("{topicGuid:guid}")]
    public async Task<ActionResult> GetTopic(Guid projectId, Guid topicGuid, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();

        var issue = await ResolveByGuid(projectId, topicGuid, ct);
        if (issue == null) return NotFound();
        return Ok(MapTopic(issue));
    }

    /// <summary>BCF 2.1 — comments on a topic.</summary>
    [HttpGet("{topicGuid:guid}/comments")]
    public async Task<ActionResult> ListComments(Guid projectId, Guid topicGuid, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();

        var issue = await ResolveByGuid(projectId, topicGuid, ct);
        if (issue == null) return NotFound();

        var comments = await _db.IssueComments.AsNoTracking()
            .Where(c => c.IssueId == issue.Id && c.DeletedAt == null)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        return Ok(comments.Select(c => new
        {
            guid = c.Id,
            date = c.CreatedAt.ToString("o"),
            author = c.AuthorName,
            comment = c.Body,
            topic_guid = topicGuid,
            modified_date = c.EditedAt?.ToString("o"),
            modified_author = (string?)null
        }).ToList());
    }

    /// <summary>
    /// BCF 2.1 — viewpoints on a topic. Currently emits at most one
    /// minimal orthogonal-camera viewpoint when the issue has anchored
    /// model coordinates. Snapshot binary endpoints are 501.
    /// </summary>
    [HttpGet("{topicGuid:guid}/viewpoints")]
    public async Task<ActionResult> ListViewpoints(Guid projectId, Guid topicGuid, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();

        var issue = await ResolveByGuid(projectId, topicGuid, ct);
        if (issue == null) return NotFound();
        if (!issue.ModelX.HasValue || !issue.ModelY.HasValue || !issue.ModelZ.HasValue)
            return Ok(Array.Empty<object>());

        return Ok(new[]
        {
            new
            {
                guid = issue.Id,
                topic_guid = topicGuid,
                index = 0,
                orthogonal_camera = new
                {
                    camera_view_point = new { x = issue.ModelX.Value, y = issue.ModelY.Value, z = issue.ModelZ.Value },
                    camera_direction  = new { x = 0.0, y = 0.0, z = -1.0 },
                    camera_up_vector  = new { x = 0.0, y = 1.0, z = 0.0 },
                    view_to_world_scale = 10.0
                }
            }
        });
    }

    // ── Write endpoints — Phase 178c follow-up. Return 501 deliberately. ──

    [HttpPost]
    public IActionResult CreateTopic() =>
        StatusCode(StatusCodes.Status501NotImplemented,
            new { error = "not_implemented", phase = "178c_followup", endpoint = "POST /topics" });

    [HttpPut("{topicGuid:guid}")]
    public IActionResult UpdateTopic(Guid topicGuid) =>
        StatusCode(StatusCodes.Status501NotImplemented,
            new { error = "not_implemented", phase = "178c_followup", endpoint = "PUT /topics/{guid}" });

    [HttpDelete("{topicGuid:guid}")]
    public IActionResult DeleteTopic(Guid topicGuid) =>
        StatusCode(StatusCodes.Status501NotImplemented,
            new { error = "not_implemented", phase = "178c_followup", endpoint = "DELETE /topics/{guid}" });

    [HttpPost("{topicGuid:guid}/comments")]
    public IActionResult AddComment(Guid topicGuid) =>
        StatusCode(StatusCodes.Status501NotImplemented,
            new { error = "not_implemented", phase = "178c_followup", endpoint = "POST /topics/{guid}/comments" });

    [HttpGet("{topicGuid:guid}/viewpoints/{viewpointGuid:guid}/snapshot")]
    public IActionResult GetSnapshot(Guid topicGuid, Guid viewpointGuid) =>
        StatusCode(StatusCodes.Status501NotImplemented,
            new { error = "not_implemented", phase = "178c_followup", endpoint = "viewpoint snapshot binary" });

    // ── helpers ──

    private async Task<bool> ProjectInTenant(Guid projectId, CancellationToken ct)
    {
        var tenantId = Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
        if (tenantId == Guid.Empty) return false;
        return await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == tenantId, ct);
    }

    /// <summary>
    /// Resolve a topic by its BCF guid OR by the underlying BimIssue.Id —
    /// older issues created before BcfGuid was added still need to be
    /// queryable via the API.
    /// </summary>
    private async Task<BimIssue?> ResolveByGuid(Guid projectId, Guid topicGuid, CancellationToken ct)
    {
        var guidStr = topicGuid.ToString();
        return await _db.Issues.AsNoTracking()
            .FirstOrDefaultAsync(i => i.ProjectId == projectId
                && (i.BcfGuid == guidStr || i.Id == topicGuid), ct);
    }

    private static object MapTopic(BimIssue i) => new
    {
        guid = string.IsNullOrEmpty(i.BcfGuid) ? i.Id.ToString() : i.BcfGuid!,
        topic_type = i.Type ?? "RFI",
        topic_status = i.Status ?? "OPEN",
        priority = i.Priority ?? "MEDIUM",
        title = i.Title,
        labels = Array.Empty<string>(),
        description = i.Description,
        creation_date = i.CreatedAt.ToString("o"),
        creation_author = i.CreatedBy ?? "",
        modified_date = i.ResolvedAt?.ToString("o"),
        assigned_to = i.AssigneeEmail ?? i.Assignee,
        due_date = i.DueDate?.ToString("o"),
        // BCF API extras Planscape exposes
        planscape = new
        {
            issue_code = i.IssueCode,
            project_id = i.ProjectId,
            discipline = i.Discipline,
            source = i.Source
        }
    };
}
