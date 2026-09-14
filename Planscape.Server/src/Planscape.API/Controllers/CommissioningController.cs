using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Shared.Commissioning;

namespace Planscape.API.Controllers;

/// <summary>
/// QR-3 — the commissioning ladder over HTTP, so a scan on a phone can advance an
/// asset the way the desktop always could.
///
/// WHY THIS DID NOT EXIST
/// ----------------------
/// "QR commissioning" shipped as a desktop feature whose only entry point read the
/// Revit SELECTION — there was no scan path anywhere, and there could not be one
/// over the wire because this server had no commissioning controller and no
/// commissioning entity. A phone could parse the QR and had nothing to POST to.
///
/// THE RULES ARE NOT RESTATED HERE
/// -------------------------------
/// Every decision goes through <see cref="CommissioningStateMachine"/> in
/// Planscape.Shared, which the Revit plugin also calls. Writing the ladder twice is
/// how a desktop that refuses a skip-state transition and an API that allows it come
/// to produce a handover record nobody can reconcile — silently, months later.
///
/// APPEND-ONLY
/// -----------
/// Every advance INSERTS. Current state is the newest row's ToState. Nothing here
/// updates or deletes: commissioning records what happened, and the refusal to
/// regress exists so the history cannot be rewritten.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/commissioning")]
[Authorize]
public class CommissioningController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    public CommissioningController(PlanscapeDbContext db) => _db = db;

    // ── Read ──────────────────────────────────────────────────────────────────

    /// <summary>Current state of one element, plus its full history.
    ///
    /// An element with no rows is NOT_STARTED with an empty history — that is a
    /// real answer, not a 404. A 404 would make "never commissioned" look like
    /// "no such element", and only one of those is a problem.</summary>
    [HttpGet("{elementUniqueId}")]
    public async Task<ActionResult> GetState(Guid projectId, string elementUniqueId)
    {
        if (string.IsNullOrWhiteSpace(elementUniqueId))
            return BadRequest(new { error = "elementUniqueId is required." });

        var tenantId = GetTenantId();
        var history = await _db.CommissioningRecords
            .Where(r => r.ProjectId == projectId && r.Project!.TenantId == tenantId
                     && r.ElementUniqueId == elementUniqueId)
            .OrderBy(r => r.RecordedAt)
            .ToListAsync();

        var current = history.Count > 0 ? history[^1].ToState : CommissioningStates.NotStarted;

        return Ok(new
        {
            elementUniqueId,
            currentState = current,
            nextState = CommissioningStates.Next(current),
            isTerminal = CommissioningStates.IsTerminal(current),
            // What the CALLER would still have to supply. Lets a phone render the
            // right form before it tries, rather than learning from a 409.
            witnessRequiredNext = string.Equals(
                CommissioningStates.Next(current), CommissioningStates.Commissioned,
                StringComparison.OrdinalIgnoreCase),
            history,
        });
    }

    /// <summary>The project's commissioning register: the CURRENT state of every
    /// element that has ever had a step, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult> GetRegister(Guid projectId, [FromQuery] string? state = null)
    {
        var tenantId = GetTenantId();
        var rows = await _db.CommissioningRecords
            .Where(r => r.ProjectId == projectId && r.Project!.TenantId == tenantId)
            .ToListAsync();

        var latest = rows
            .GroupBy(r => r.ElementUniqueId)
            .Select(g => g.OrderBy(r => r.RecordedAt).Last())
            .Where(r => state == null || string.Equals(r.ToState, state, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.RecordedAt)
            .ToList();

        // Counts across the whole ladder, INCLUDING the zeroes. A dashboard that
        // renders only the states present cannot show that nothing has reached
        // COMMISSIONED — an absent bar and a zero bar mean different things.
        var byState = CommissioningStates.All.ToDictionary(
            s => s,
            s => latest.Count(r => string.Equals(r.ToState, s, StringComparison.OrdinalIgnoreCase)));

        return Ok(new { total = latest.Count, byState, items = latest });
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    public sealed class AdvanceRequest
    {
        public string ElementUniqueId { get; set; } = "";
        /// <summary>Null or empty advances one step. A named state must still be the
        /// very next one — the state machine refuses a skip either way.</summary>
        public string? RequestedState { get; set; }
        public string? Operative { get; set; }
        public string? Witness { get; set; }
        public string? Notes { get; set; }
        public string? ElementTag { get; set; }
        public string? ElementName { get; set; }
        /// <summary>"mobile-scan" / "desktop" / "api". Recorded so an audit can tell
        /// a scan on site from something typed at a desk.</summary>
        public string? Source { get; set; }
        /// <summary>When the step actually happened, for a queued offline scan.
        /// Advisory: RecordedAt is always server time, because a phone's clock is
        /// not evidence.</summary>
        public DateTime? OccurredAt { get; set; }
        /// <summary>The state the CLIENT believed the element was in when it built
        /// this request. When supplied and wrong, the write is refused with 409 —
        /// see the concurrency note in <see cref="Advance"/>.</summary>
        public string? ExpectedCurrentState { get; set; }
    }

    [HttpPost("advance")]
    public async Task<ActionResult> Advance(Guid projectId, [FromBody] AdvanceRequest req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.ElementUniqueId))
            return BadRequest(new { error = "elementUniqueId is required." });

        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound(new { error = "Project not found." });

        var last = await _db.CommissioningRecords
            .Where(r => r.ProjectId == projectId && r.ElementUniqueId == req.ElementUniqueId)
            .OrderByDescending(r => r.RecordedAt)
            .FirstOrDefaultAsync();

        var current = last?.ToState ?? CommissioningStates.NotStarted;

        // Optimistic concurrency, opt-in. Two fitters scanning the same asset within
        // seconds would otherwise both read INSTALLED and both advance — one step
        // would be recorded twice under two names. A client that tells us what it
        // believed gets protected; one that does not is unchanged.
        if (!string.IsNullOrWhiteSpace(req.ExpectedCurrentState)
            && !string.Equals(req.ExpectedCurrentState, current, StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new
            {
                error = "state_moved",
                detail = $"You were looking at {req.ExpectedCurrentState}; it is now {current}. " +
                         "Someone else advanced it. Re-read before signing off.",
                currentState = current,
            });
        }

        var decision = CommissioningStateMachine.Decide(current, new CommissioningRequest
        {
            RequestedState = req.RequestedState,
            Operative = req.Operative,
            Witness = req.Witness,
            Notes = req.Notes,
        });

        if (!decision.Ok)
        {
            // The refusal ENUM is what a client should branch on; `reason` is the
            // sentence for a human. A client matching on message text stops working
            // the moment someone rewords it.
            return UnprocessableEntity(new
            {
                error = "transition_refused",
                refusal = decision.Refusal.ToString(),
                reason = decision.Reason,
                currentState = decision.FromState,
                attemptedState = decision.ToState,
            });
        }

        var row = new CommissioningRecord
        {
            TenantId = tenantId,
            ProjectId = projectId,
            ElementUniqueId = req.ElementUniqueId.Trim(),
            ElementTag = req.ElementTag,
            ElementName = req.ElementName,
            FromState = decision.FromState,
            ToState = decision.ToState!,
            Operative = req.Operative!.Trim(),
            Witness = string.IsNullOrWhiteSpace(req.Witness) ? null : req.Witness.Trim(),
            Notes = req.Notes,
            Source = string.IsNullOrWhiteSpace(req.Source) ? "api" : req.Source.Trim(),
            RecordedAt = DateTime.UtcNow,
            OccurredAt = req.OccurredAt,
            RecordedByUserId = User.FindFirst("sub")?.Value ?? User.Identity?.Name,
        };

        _db.CommissioningRecords.Add(row);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            record = row,
            currentState = row.ToState,
            nextState = CommissioningStates.Next(row.ToState),
            isTerminal = CommissioningStates.IsTerminal(row.ToState),
        });
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

/// <summary>
/// The ladder itself — a global constant, so it gets a route that does not pretend
/// to be project-scoped.
///
/// It exists so a client renders the REAL states instead of keeping its own copy of
/// the list. A client-side copy is the same drift risk as a second state machine,
/// one layer out: the app offers a state the server will refuse, and the user finds
/// out by being rejected.
/// </summary>
[ApiController]
[Route("api/commissioning")]
[Authorize]
public class CommissioningStatesController : ControllerBase
{
    [HttpGet("states")]
    public ActionResult GetStates() => Ok(new
    {
        states = CommissioningStates.All,
        witnessRequiredFor = CommissioningStates.Commissioned,
        terminal = CommissioningStates.Handover,
    });
}
