using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.DTOs;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Replays a batch of offline-queued operations in a single request.
/// Designed for mobile clients that queue CREATE_ISSUE, UPDATE_ISSUE,
/// and TRANSITION_CDE actions while offline.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BatchController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private const int MaxOperations = 100;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public BatchController(PlanscapeDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Execute a batch of operations. Each operation is processed independently;
    /// individual failures do not roll back other operations.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<BatchResponse>> Execute([FromBody] BatchRequest req)
    {
        if (req.Operations.Count > MaxOperations)
            return BadRequest($"Batch limited to {MaxOperations} operations (received {req.Operations.Count})");

        var tenantId = GetTenantId();
        var results = new List<BatchOperationResult>(req.Operations.Count);
        int succeeded = 0, failed = 0;

        for (int i = 0; i < req.Operations.Count; i++)
        {
            var op = req.Operations[i];
            try
            {
                var result = op.Type switch
                {
                    "CREATE_ISSUE"   => await HandleCreateIssue(op.Payload, tenantId),
                    "UPDATE_ISSUE"   => await HandleUpdateIssue(op.Payload, tenantId),
                    "TRANSITION_CDE" => await HandleTransitionCde(op.Payload, tenantId),
                    _ => new BatchOperationResult { Index = i, Type = op.Type, Success = false, Error = $"Unknown operation type: {op.Type}" }
                };

                result = result with { Index = i, Type = op.Type };
                results.Add(result);

                if (result.Success) succeeded++; else failed++;
            }
            catch (Exception ex)
            {
                results.Add(new BatchOperationResult
                {
                    Index = i, Type = op.Type, Success = false, Error = ex.Message
                });
                failed++;
            }
        }

        return Ok(new BatchResponse
        {
            Total = req.Operations.Count,
            Succeeded = succeeded,
            Failed = failed,
            Results = results
        });
    }

    private async Task<BatchOperationResult> HandleCreateIssue(JsonElement payload, Guid tenantId)
    {
        var projectId = payload.GetProperty("projectId").GetGuid();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null)
            return new BatchOperationResult { Success = false, Error = "Project not found" };

        var title = payload.GetProperty("title").GetString() ?? "";
        var type = payload.TryGetProperty("type", out var t) ? t.GetString() ?? "RFI" : "RFI";
        var priority = payload.TryGetProperty("priority", out var pr) ? pr.GetString() : "MEDIUM";
        var description = payload.TryGetProperty("description", out var d) ? d.GetString() : null;
        var assignee = payload.TryGetProperty("assignee", out var a) ? a.GetString() : null;
        var discipline = payload.TryGetProperty("discipline", out var disc) ? disc.GetString() : null;

        var issue = new BimIssue
        {
            ProjectId = projectId,
            IssueCode = $"{type}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            Type = type,
            Title = title,
            Description = description,
            Priority = priority ?? "MEDIUM",
            Assignee = assignee,
            Discipline = discipline,
            CreatedBy = User.FindFirst("display_name")?.Value ?? "Unknown"
        };

        _db.Issues.Add(issue);
        await _db.SaveChangesAsync();

        return new BatchOperationResult
        {
            Success = true,
            Data = new { issue.Id, issue.IssueCode }
        };
    }

    private async Task<BatchOperationResult> HandleUpdateIssue(JsonElement payload, Guid tenantId)
    {
        var issueId = payload.GetProperty("issueId").GetGuid();
        var issue = await _db.Issues
            .FirstOrDefaultAsync(i => i.Id == issueId && i.Project!.TenantId == tenantId);
        if (issue == null)
            return new BatchOperationResult { Success = false, Error = "Issue not found" };

        if (payload.TryGetProperty("status", out var s) && s.GetString() is string status)
        {
            issue.Status = status;
            if (status is "RESOLVED" or "CLOSED")
                issue.ResolvedAt = DateTime.UtcNow;
        }
        if (payload.TryGetProperty("priority", out var p) && p.GetString() is string pri)
            issue.Priority = pri;
        if (payload.TryGetProperty("assignee", out var a) && a.GetString() is string assignee)
            issue.Assignee = assignee;

        await _db.SaveChangesAsync();

        return new BatchOperationResult
        {
            Success = true,
            Data = new { issue.Id, issue.Status }
        };
    }

    private async Task<BatchOperationResult> HandleTransitionCde(JsonElement payload, Guid tenantId)
    {
        var documentId = payload.GetProperty("documentId").GetGuid();
        var newState = payload.GetProperty("newState").GetString() ?? "";

        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId && d.Project!.TenantId == tenantId);
        if (doc == null)
            return new BatchOperationResult { Success = false, Error = "Document not found" };

        var oldState = doc.CdeStatus;
        doc.CdeStatus = newState;

        if (payload.TryGetProperty("suitabilityCode", out var sc) && sc.GetString() is string suit)
            doc.SuitabilityCode = suit;

        await _db.SaveChangesAsync();

        return new BatchOperationResult
        {
            Success = true,
            Data = new { doc.Id, oldState, newState = doc.CdeStatus }
        };
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}
