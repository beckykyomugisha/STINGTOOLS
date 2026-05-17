using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

public interface ISuitabilityStateMachine
{
    Task<TransitionOutcome> TransitionAsync(Guid tenantId, Guid projectId,
        Guid documentId, SuitabilityCode toCode,
        string triggeredBy, string? notes, string triggerSource = "User",
        CancellationToken ct = default);
}

public record TransitionOutcome(bool Success, string? Error, SuitabilityTransition? Transition);

public class SuitabilityStateMachine : ISuitabilityStateMachine
{
    private readonly PlanscapeDbContext _db;

    public SuitabilityStateMachine(PlanscapeDbContext db) => _db = db;

    public async Task<TransitionOutcome> TransitionAsync(
        Guid tenantId, Guid projectId, Guid documentId,
        SuitabilityCode toCode, string triggeredBy, string? notes,
        string triggerSource = "User", CancellationToken ct = default)
    {
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null)
            return new TransitionOutcome(false, "Document not found.", null);

        if (!Enum.TryParse<SuitabilityCode>(doc.SuitabilityCode ?? "S0", out var fromCode))
            fromCode = SuitabilityCode.S0;

        // Find the best applicable rule (project-specific first, then tenant-wide)
        var rule = await _db.SuitabilityTransitionRules
            .Where(r => r.TenantId == tenantId && r.Enabled
                     && r.FromCode == fromCode && r.ToCode == toCode
                     && (r.ProjectId == projectId || r.ProjectId == null))
            .OrderBy(r => r.ProjectId == null ? 1 : 0)
            .ThenBy(r => r.Priority)
            .FirstOrDefaultAsync(ct);

        if (rule is null)
            return new TransitionOutcome(false,
                $"No enabled transition rule from {fromCode} to {toCode}.", null);

        // Precondition checks (bitmask)
        if ((rule.PreconditionMask & 2) != 0 && string.IsNullOrEmpty(doc.FileName))
            return new TransitionOutcome(false, "Precondition failed: naming not validated.", null);

        var transition = new SuitabilityTransition
        {
            TenantId         = tenantId,
            ProjectId        = projectId,
            DocumentRecordId = documentId,
            RuleId           = rule.Id,
            FromCode         = fromCode,
            ToCode           = toCode,
            TriggeredBy      = triggeredBy,
            TriggerSource    = triggerSource,
            Notes            = notes,
        };

        _db.SuitabilityTransitions.Add(transition);

        doc.SuitabilityCode = toCode.ToString();
        doc.UpdatedAt       = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return new TransitionOutcome(true, null, transition);
    }
}
