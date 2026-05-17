#nullable enable
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.API.Authorization;
using Planscape.API.Services;

namespace Planscape.API.Controllers;

[ApiController]
[Route("api/projects/{projectId}/offline-queue")]
[Authorize]
[ProjectAccess]
public class OfflineQueueController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IAuditService _audit;
    private readonly ILogger<OfflineQueueController> _logger;

    public OfflineQueueController(PlanscapeDbContext db, IAuditService audit,
        ILogger<OfflineQueueController> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>
    /// Batch-replay offline actions captured while the mobile client had no connectivity.
    /// Each action is processed independently so a single failure never blocks the rest.
    /// </summary>
    [HttpPost("replay")]
    public async Task<ActionResult> Replay(Guid projectId, [FromBody] ReplayRequest req)
    {
        if (req.Actions == null || req.Actions.Count == 0)
            return BadRequest("actions array is required");

        var tenantId = GetTenantId();
        var userName = User.FindFirst("display_name")?.Value ?? "offline";

        var processed = 0;
        var conflicts = new List<object>();
        var errors = new List<object>();

        foreach (var action in req.Actions)
        {
            try
            {
                switch (action.Type)
                {
                    case "CREATE_ISSUE":
                        await ProcessCreateIssue(projectId, tenantId, userName, action, errors);
                        processed++;
                        break;

                    case "UPDATE_ISSUE":
                    {
                        var conflict = await ProcessUpdateIssue(projectId, tenantId, userName, action);
                        if (conflict != null) conflicts.Add(conflict);
                        else processed++;
                        break;
                    }

                    case "TRANSITION_CDE":
                    {
                        var conflict = await ProcessTransitionCde(projectId, tenantId, userName, action);
                        if (conflict != null) conflicts.Add(conflict);
                        else processed++;
                        break;
                    }

                    default:
                        errors.Add(new { action.ClientId, reason = $"Unknown action type: {action.Type}" });
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "offline-queue replay: action {ClientId} ({Type}) failed", action.ClientId, action.Type);
                errors.Add(new { action.ClientId, action.Type, reason = ex.Message });
            }
        }

        return Ok(new { processed, conflicts, errors });
    }

    // ── Action processors ────────────────────────────────────────────────────────

    private static string? Str(JsonElement p, string key) =>
        p.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetString() : null;

    private async Task ProcessCreateIssue(Guid projectId, Guid tenantId, string userName,
        OfflineAction action, List<object> errors)
    {
        var p = action.Payload;
        var issue = new BimIssue
        {
            TenantId   = tenantId,
            ProjectId  = projectId,
            Type       = Str(p, "type")       ?? "RFI",
            Title      = Str(p, "title")      ?? "(offline issue)",
            Description = Str(p, "description"),
            Priority   = Str(p, "priority")   ?? "MEDIUM",
            Assignee   = Str(p, "assignee"),
            Discipline = Str(p, "discipline"),
            CreatedBy  = userName,
            Source     = "offline",
            // Use the client timestamp so the timeline reflects when the action was captured.
            CreatedAt  = action.ClientTimestamp ?? DateTime.UtcNow,
            UpdatedAt  = action.ClientTimestamp ?? DateTime.UtcNow,
        };

        // Stable IssueCode — use clientId prefix if provided so re-plays are idempotent.
        // Server-side uniqueness constraint on IssueCode prevents double-insert.
        if (!string.IsNullOrEmpty(action.ClientId))
            issue.IssueCode = $"OFF-{action.ClientId[..Math.Min(8, action.ClientId.Length)]}";

        _db.Issues.Add(issue);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("CREATE_OFFLINE", "Issue", issue.Id.ToString(), $"{{\"clientId\":\"{action.ClientId}\"}}");
    }

    private async Task<object?> ProcessUpdateIssue(Guid projectId, Guid tenantId, string userName,
        OfflineAction action)
    {
        var p = action.Payload;
        var idStr = Str(p, "id");
        if (!Guid.TryParse(idStr, out var issueId))
            return new { action.ClientId, reason = "Invalid issue id in payload" };

        var issue = await _db.Issues
            .FirstOrDefaultAsync(i => i.Id == issueId && i.ProjectId == projectId && i.Project!.TenantId == tenantId);
        if (issue == null) return new { action.ClientId, reason = $"Issue {issueId} not found" };

        // Last-write-wins: skip if the server copy is already newer.
        var clientTs = action.ClientTimestamp ?? DateTime.MinValue;
        if (issue.UpdatedAt > clientTs)
            return new { action.ClientId, reason = "conflict", serverUpdatedAt = issue.UpdatedAt, clientTimestamp = clientTs };

        if (Str(p, "status")      is string status)   issue.Status      = status;
        if (Str(p, "priority")    is string priority)  issue.Priority    = priority;
        if (Str(p, "description") is string desc)      issue.Description = desc;
        if (Str(p, "assignee")    is string assignee)  issue.Assignee    = assignee;
        issue.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("UPDATE_OFFLINE", "Issue", issue.Id.ToString(), $"{{\"clientId\":\"{action.ClientId}\"}}");
        return null;
    }

    private async Task<object?> ProcessTransitionCde(Guid projectId, Guid tenantId, string userName,
        OfflineAction action)
    {
        var p = action.Payload;
        var idStr = Str(p, "documentId");
        if (!Guid.TryParse(idStr, out var docId))
            return new { action.ClientId, reason = "Invalid documentId in payload" };

        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == docId && d.ProjectId == projectId && d.Project!.TenantId == tenantId);
        if (doc == null) return new { action.ClientId, reason = $"Document {docId} not found" };

        var newState = Str(p, "newState");
        if (string.IsNullOrEmpty(newState))
            return new { action.ClientId, reason = "newState is required" };

        // Last-write-wins: skip if the server already moved past this transition.
        var clientTs = action.ClientTimestamp ?? DateTime.MinValue;
        if ((doc.UpdatedAt ?? DateTime.MinValue) > clientTs)
            return new { action.ClientId, reason = "conflict", serverUpdatedAt = doc.UpdatedAt, clientTimestamp = clientTs };

        // Validate using the same transition map as DocumentsController.
        var validTargets = GetValidTransitions(doc.CdeStatus);
        if (!validTargets.Contains(newState))
            return new { action.ClientId, reason = $"Invalid CDE transition: {doc.CdeStatus} → {newState}" };

        var oldState = doc.CdeStatus;
        doc.CdeStatus = newState;
        doc.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("TRANSITION_OFFLINE", "Document", doc.Id.ToString(),
            $"{{\"oldState\":\"{oldState}\",\"newState\":\"{newState}\",\"clientId\":\"{action.ClientId}\"}}");
        return null;
    }

    // Mirror of DocumentsController.ValidTransitions — kept local to avoid coupling.
    private static string[] GetValidTransitions(string current) => current switch
    {
        "WIP"       => new[] { "SHARED" },
        "SHARED"    => new[] { "PUBLISHED", "WIP" },
        "PUBLISHED" => new[] { "ARCHIVE", "SUPERSEDED" },
        _           => Array.Empty<string>()
    };

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

// ── Request / response models ────────────────────────────────────────────────

public record ReplayRequest(List<OfflineAction> Actions);

public record OfflineAction(
    string Type,
    JsonElement Payload,
    DateTime? ClientTimestamp,
    string ClientId);
