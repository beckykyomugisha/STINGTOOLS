using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

public interface IModelCheckerService
{
    Task<ModelCheckRunSummary> RunAsync(Guid tenantId, Guid projectId,
        Guid ruleSetId, Guid? projectModelId, string triggeredBy,
        CancellationToken ct = default);
}

public record ModelCheckRunSummary(Guid RunId, int TotalChecked,
    int FindingsOpen, int FindingsCritical, TimeSpan Duration);

public class ModelCheckerService : IModelCheckerService
{
    private readonly PlanscapeDbContext _db;

    public ModelCheckerService(PlanscapeDbContext db) => _db = db;

    public async Task<ModelCheckRunSummary> RunAsync(Guid tenantId, Guid projectId,
        Guid ruleSetId, Guid? projectModelId, string triggeredBy,
        CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        _ = await _db.ModelCheckRuleSets
            .FirstOrDefaultAsync(rs => rs.Id == ruleSetId && rs.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"RuleSet {ruleSetId} not found.");

        var rules = await _db.ModelCheckRules
            .Where(r => r.RuleSetId == ruleSetId && r.Enabled)
            .ToListAsync(ct);

        var run = new ModelCheckRun
        {
            TenantId       = tenantId,
            ProjectId      = projectId,
            RuleSetId      = ruleSetId,
            ProjectModelId = projectModelId,
            TriggeredBy    = triggeredBy,
            Status         = "Running",
            StartedAt      = started,
        };
        _db.ModelCheckRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        var elements = await _db.TaggedElements
            .Where(e => e.ProjectId == projectId)
            .ToListAsync(ct);

        var findings = new List<ModelCheckResult>();
        foreach (var rule in rules)
            findings.AddRange(EvaluateRule(rule, elements, run.Id, tenantId, projectId));

        _db.ModelCheckResults.AddRange(findings);

        var open     = findings.Count(f => f.Status == "Open");
        var critical = findings.Count(f => f.Severity == "Critical" && f.Status == "Open");

        run.Status               = "Completed";
        run.CompletedAt          = DateTime.UtcNow;
        run.FindingsCount        = findings.Count;
        run.CriticalCount        = critical;
        run.MajorCount           = findings.Count(f => f.Severity == "Major");
        run.MinorCount           = findings.Count(f => f.Severity == "Minor");
        run.InfoCount            = findings.Count(f => f.Severity == "Info");
        run.TotalElementsChecked = elements.Count;
        run.TotalRulesEvaluated  = rules.Count;

        await _db.SaveChangesAsync(ct);

        return new ModelCheckRunSummary(run.Id, elements.Count, open, critical,
            DateTime.UtcNow - started);
    }

    private IEnumerable<ModelCheckResult> EvaluateRule(
        ModelCheckRule rule, IList<TaggedElement> elements,
        Guid runId, Guid tenantId, Guid projectId)
        => rule.Kind switch
        {
            "PropertyRequired" => CheckPropertyRequired(rule, elements, runId, tenantId, projectId),
            _ => Enumerable.Empty<ModelCheckResult>()
        };

    private IEnumerable<ModelCheckResult> CheckPropertyRequired(
        ModelCheckRule rule, IList<TaggedElement> elements,
        Guid runId, Guid tenantId, Guid projectId)
    {
        foreach (var el in elements.Where(e => string.IsNullOrEmpty(e.Tag1)))
        {
            yield return new ModelCheckResult
            {
                TenantId     = tenantId,
                ProjectId    = projectId,
                RunId        = runId,
                RuleId       = rule.Id,
                IfcGlobalId  = el.UniqueId,
                IfcType      = null,
                ElementName  = el.FamilyName ?? el.CategoryName,
                Severity     = rule.Severity,
                Status       = "Open",
                Message      = $"Rule '{rule.Name}': required property missing.",
            };
        }
    }
}
