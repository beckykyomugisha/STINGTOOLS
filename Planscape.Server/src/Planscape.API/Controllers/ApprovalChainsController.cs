using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Authorization;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Phase 178c (T3-12) — Multi-step / parallel approval chains for CDE
/// state transitions on a <see cref="DocumentRecord"/>.
///
/// <para>
/// Endpoints:
/// <list type="bullet">
///   <item><c>POST   /api/projects/{pid}/documents/{did}/approval-chain</c> — create chain</item>
///   <item><c>GET    /api/projects/{pid}/documents/{did}/approval-chain</c> — list active chains</item>
///   <item><c>POST   /api/projects/{pid}/documents/{did}/approval-chain/decisions</c> — submit a decision</item>
///   <item><c>DELETE /api/projects/{pid}/documents/{did}/approval-chain/{chainId}</c> — cancel chain</item>
/// </list>
/// </para>
///
/// <para>
/// Semantics:
/// <list type="bullet">
///   <item><b>PARALLEL</b> stage — every required approver must approve in any order.</item>
///   <item><b>SEQUENTIAL</b> stage — approvers act in declared order; later approvers can't decide first.</item>
///   <item>Any REJECT immediately fails the whole chain (Status = REJECTED, Document does NOT transition).</item>
///   <item>When the last stage completes, Chain.Status = COMPLETED — the existing CDE transition gate accepts it.</item>
///   <item>The legacy single-approver <see cref="DocumentApproval"/> path remains as back-compat.</item>
/// </list>
/// </para>
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/documents/{documentId:guid}/approval-chain")]
[Authorize]
[ProjectAccess]
public class ApprovalChainsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;
    private readonly ILogger<ApprovalChainsController> _logger;

    public ApprovalChainsController(
        PlanscapeDbContext db, ITenantContext tenant, IAuditService audit,
        ILogger<ApprovalChainsController> logger)
    {
        _db = db; _tenant = tenant; _audit = audit; _logger = logger;
    }

    /// <summary>List approval chains for a document (newest first).</summary>
    [HttpGet]
    public async Task<ActionResult> List(Guid projectId, Guid documentId, CancellationToken ct)
    {
        var chains = await _db.ApprovalChains.AsNoTracking()
            .Where(c => c.DocumentId == documentId && c.ProjectId == projectId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new
            {
                c.Id, c.Transition, c.Status, c.CreatedBy, c.CreatedAt, c.CompletedAt, c.Description,
                stages = _db.ApprovalStages.AsNoTracking()
                    .Where(s => s.ChainId == c.Id)
                    .OrderBy(s => s.Order)
                    .Select(s => new
                    {
                        s.Id, s.Order, s.Mode, s.Status, s.Label, s.StartedAt, s.CompletedAt,
                        requiredApprovers = s.RequiredApproversJson,
                        decisions = s.DecisionsJson
                    }).ToList()
            })
            .ToListAsync(ct);
        return Ok(chains);
    }

    /// <summary>Create a new approval chain definition for a document.</summary>
    [HttpPost]
    public async Task<ActionResult> Create(
        Guid projectId, Guid documentId,
        [FromBody] CreateApprovalChainRequest req,
        CancellationToken ct)
    {
        if (req.Stages == null || req.Stages.Count == 0)
            return BadRequest(new { error = "at_least_one_stage_required" });

        var doc = await _db.Documents.FirstOrDefaultAsync(
            d => d.Id == documentId && d.ProjectId == projectId, ct);
        if (doc == null) return NotFound(new { error = "document_not_found" });

        // Reject duplicate OPEN chain for the same transition.
        var hasOpen = await _db.ApprovalChains
            .AnyAsync(c => c.DocumentId == documentId
                        && c.Transition == req.Transition
                        && c.Status == "OPEN", ct);
        if (hasOpen)
            return Conflict(new { error = "open_chain_already_exists", transition = req.Transition });

        var displayName = User.FindFirst("display_name")?.Value ?? "Unknown";

        var chain = new ApprovalChain
        {
            TenantId    = _tenant.TenantId,
            ProjectId   = projectId,
            DocumentId  = documentId,
            Transition  = req.Transition,
            Status      = "OPEN",
            CreatedBy   = displayName,
            Description = req.Description,
        };
        _db.ApprovalChains.Add(chain);

        for (int i = 0; i < req.Stages.Count; i++)
        {
            var stageReq = req.Stages[i];
            var mode = (stageReq.Mode ?? "PARALLEL").ToUpperInvariant();
            if (mode != "PARALLEL" && mode != "SEQUENTIAL")
                return BadRequest(new { error = "invalid_mode_must_be_parallel_or_sequential", stageOrder = i });
            if (stageReq.RequiredApprovers == null || stageReq.RequiredApprovers.Count == 0)
                return BadRequest(new { error = "stage_requires_at_least_one_approver", stageOrder = i });

            _db.ApprovalStages.Add(new ApprovalStage
            {
                TenantId              = _tenant.TenantId,
                ChainId               = chain.Id,
                Order                 = i,
                Mode                  = mode,
                RequiredApproversJson = ApprovalStage.SerializeApprovers(stageReq.RequiredApprovers),
                Status                = i == 0 ? "PENDING" : "PENDING",  // all start PENDING; stage 0 is "active"
                StartedAt             = i == 0 ? DateTime.UtcNow : null,
                Label                 = stageReq.Label,
            });
        }

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("CREATE", "ApprovalChain", chain.Id.ToString(),
            JsonSerializer.Serialize(new { chain.DocumentId, chain.Transition, stages = req.Stages.Count }));

        return CreatedAtAction(nameof(List), new { projectId, documentId },
            new { chain.Id, chain.Status, chain.Transition, stages = req.Stages.Count });
    }

    /// <summary>
    /// Submit a decision against the active stage of an OPEN chain. Decision is
    /// "APPROVE" / "REJECT" / "ABSTAIN". Body identifies the chain by id.
    /// </summary>
    [HttpPost("decisions")]
    public async Task<ActionResult> SubmitDecision(
        Guid projectId, Guid documentId,
        [FromBody] SubmitApprovalDecisionRequest req,
        CancellationToken ct)
    {
        if (req.ChainId == Guid.Empty)
            return BadRequest(new { error = "chainId_required" });

        var decision = (req.Decision ?? "").ToUpperInvariant();
        if (decision != "APPROVE" && decision != "REJECT" && decision != "ABSTAIN")
            return BadRequest(new { error = "decision_must_be_APPROVE_REJECT_or_ABSTAIN" });

        var userIdClaim = User.FindFirst("user_id")?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized(new { error = "no_user_id_claim" });

        var chain = await _db.ApprovalChains
            .FirstOrDefaultAsync(c => c.Id == req.ChainId
                                   && c.DocumentId == documentId
                                   && c.ProjectId == projectId, ct);
        if (chain == null) return NotFound(new { error = "chain_not_found" });
        if (chain.Status != "OPEN")
            return BadRequest(new { error = "chain_not_open", currentStatus = chain.Status });

        var stages = await _db.ApprovalStages
            .Where(s => s.ChainId == chain.Id)
            .OrderBy(s => s.Order)
            .ToListAsync(ct);

        // Find the first stage that's not yet completed → this is the active one.
        var activeStage = stages.FirstOrDefault(s => s.Status == "PENDING");
        if (activeStage == null)
            return BadRequest(new { error = "no_active_stage" });

        var required = ApprovalStage.ParseRequiredApprovers(activeStage.RequiredApproversJson);
        if (!required.Contains(userId))
            return Forbid();

        // SEQUENTIAL ordering — within a stage, approvers must act in list order.
        if (activeStage.Mode == "SEQUENTIAL")
        {
            var existingDecisions = ParseDecisionRows(activeStage.DecisionsJson);
            int decidedCount = existingDecisions.Count;
            if (decidedCount >= required.Count)
                return BadRequest(new { error = "stage_already_decided" });
            var expectedNext = required[decidedCount];
            if (expectedNext != userId)
                return BadRequest(new
                {
                    error = "out_of_order_sequential_decision",
                    expectedNextUserId = expectedNext
                });
        }

        // Append the decision to the stage's decisions list.
        var rows = ParseDecisionRows(activeStage.DecisionsJson);
        // Reject duplicate decisions from the same approver in a parallel stage.
        if (rows.Any(r => r.UserId == userId))
            return BadRequest(new { error = "approver_already_decided" });

        rows.Add(new DecisionRow(userId, decision, req.Reason, DateTime.UtcNow));
        activeStage.DecisionsJson = JsonSerializer.Serialize(rows);
        if (activeStage.StartedAt == null) activeStage.StartedAt = DateTime.UtcNow;

        // Reject — fail the whole chain immediately.
        if (decision == "REJECT")
        {
            activeStage.Status = "REJECTED";
            activeStage.CompletedAt = DateTime.UtcNow;
            chain.Status = "REJECTED";
            chain.CompletedAt = DateTime.UtcNow;
        }
        else
        {
            // Has every required approver decided non-REJECT?
            var approvalSet = rows
                .Where(r => r.Decision == "APPROVE" || r.Decision == "ABSTAIN")
                .Select(r => r.UserId).ToHashSet();
            bool stageComplete = required.All(r => approvalSet.Contains(r));
            if (stageComplete)
            {
                activeStage.Status = "APPROVED";
                activeStage.CompletedAt = DateTime.UtcNow;
                // Activate the next stage if any.
                var next = stages.FirstOrDefault(s => s.Order == activeStage.Order + 1);
                if (next != null)
                {
                    next.StartedAt = DateTime.UtcNow;
                }
                else
                {
                    chain.Status = "COMPLETED";
                    chain.CompletedAt = DateTime.UtcNow;
                }
            }
        }

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("UPDATE", "ApprovalChain", chain.Id.ToString(),
            JsonSerializer.Serialize(new
            {
                stageId = activeStage.Id,
                stageOrder = activeStage.Order,
                userId,
                decision,
                chainStatus = chain.Status,
                stageStatus = activeStage.Status
            }));

        return Ok(new
        {
            chain.Id,
            chainStatus = chain.Status,
            stageStatus = activeStage.Status,
            activeStageOrder = stages.FirstOrDefault(s => s.Status == "PENDING")?.Order
        });
    }

    /// <summary>Cancel an OPEN chain (e.g. requestor changed mind).</summary>
    [HttpDelete("{chainId:guid}")]
    public async Task<ActionResult> Cancel(
        Guid projectId, Guid documentId, Guid chainId, CancellationToken ct)
    {
        var chain = await _db.ApprovalChains
            .FirstOrDefaultAsync(c => c.Id == chainId
                                   && c.DocumentId == documentId
                                   && c.ProjectId == projectId, ct);
        if (chain == null) return NotFound();
        if (chain.Status != "OPEN")
            return BadRequest(new { error = "chain_not_open", currentStatus = chain.Status });

        chain.Status = "CANCELLED";
        chain.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("UPDATE", "ApprovalChain", chain.Id.ToString(), "{\"status\":\"CANCELLED\"}");
        return NoContent();
    }

    // ── helpers ──

    private static List<DecisionRow> ParseDecisionRows(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<DecisionRow>>(raw) ?? new();
        }
        catch { return new(); }
    }

    public record DecisionRow(Guid UserId, string Decision, string? Reason, DateTime DecidedAt);
}

public record CreateApprovalChainRequest(
    string Transition,
    List<ApprovalStageRequest> Stages,
    string? Description = null);

public record ApprovalStageRequest(
    string? Mode,                    // "PARALLEL" | "SEQUENTIAL"
    List<Guid> RequiredApprovers,
    string? Label = null);

public record SubmitApprovalDecisionRequest(
    Guid ChainId,
    string Decision,                 // "APPROVE" | "REJECT" | "ABSTAIN"
    string? Reason = null);
