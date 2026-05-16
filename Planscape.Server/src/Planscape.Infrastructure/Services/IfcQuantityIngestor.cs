using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

public interface IIfcQuantityIngestor
{
    Task<IngestResult> IngestAsync(Guid tenantId, Guid projectId,
        Guid projectModelId, IReadOnlyList<IfcElementRow> elements,
        CancellationToken ct = default);
}

public record IfcElementRow(
    string GlobalId,
    string IfcType,
    string? Level,
    string? Zone,
    double NetQuantity,
    IReadOnlyDictionary<string, string> Properties);

public record IngestResult(int Created, int Updated, int Skipped, int RulesMissed);

public class IfcQuantityIngestor : IIfcQuantityIngestor
{
    private readonly PlanscapeDbContext _db;
    private readonly Nrm2DescriptionBuilder _descBuilder;

    public IfcQuantityIngestor(PlanscapeDbContext db, Nrm2DescriptionBuilder descBuilder)
    {
        _db = db;
        _descBuilder = descBuilder;
    }

    public async Task<IngestResult> IngestAsync(Guid tenantId, Guid projectId,
        Guid projectModelId, IReadOnlyList<IfcElementRow> elements,
        CancellationToken ct = default)
    {
        // Load rules with their classification code
        var rules = await _db.TakeoffRules
            .Include(r => r.ClassificationCode)
            .Where(r => r.TenantId == tenantId && r.Enabled)
            .ToListAsync(ct);

        int created = 0, updated = 0, skipped = 0, missed = 0;

        foreach (var el in elements)
        {
            var matched = rules.Where(r => MatchesRule(r, el)).ToList();
            if (matched.Count == 0) { missed++; continue; }

            foreach (var rule in matched)
            {
                if (rule.ClassificationCode is null) { skipped++; continue; }

                var wastePercent = rule.WastePercent;
                var grossQty     = el.NetQuantity * (1 + wastePercent / 100.0);
                var description  = _descBuilder.BuildFromRule(rule, el.Properties);
                var sectionCode  = rule.ClassificationCode.Code;

                var existing = await _db.QuantityLines.FirstOrDefaultAsync(
                    q => q.ProjectModelId == projectModelId
                      && q.IfcGlobalId == el.GlobalId
                      && q.TakeoffRuleId == rule.Id
                      && q.BaselineId == null, ct);

                if (existing is not null)
                {
                    existing.NetQuantity     = el.NetQuantity;
                    existing.WastePercent    = wastePercent;
                    existing.Quantity        = grossQty;
                    existing.ItemDescription = description;
                    existing.UpdatedAt       = DateTime.UtcNow;
                    updated++;
                }
                else
                {
                    _db.QuantityLines.Add(new QuantityLine
                    {
                        TenantId             = tenantId,
                        ProjectId            = projectId,
                        ClassificationCodeId = rule.ClassificationCodeId,
                        TakeoffRuleId        = rule.Id,
                        ProjectModelId       = projectModelId,
                        IfcGlobalId          = el.GlobalId,
                        IfcType              = el.IfcType,
                        Level                = el.Level ?? "",
                        Zone                 = el.Zone  ?? "",
                        SectionCode          = sectionCode,
                        ItemDescription      = description,
                        Unit                 = rule.Unit,
                        NetQuantity          = el.NetQuantity,
                        WastePercent         = wastePercent,
                        Quantity             = grossQty,
                        Currency             = "GBP",
                        LineKind             = "Measured",
                        PricingBasis         = "Remeasure",
                    });
                    created++;
                }
            }
        }

        await _db.SaveChangesAsync(ct);
        return new IngestResult(created, updated, skipped, missed);
    }

    private static bool MatchesRule(TakeoffRule rule, IfcElementRow el)
    {
        if (!string.IsNullOrEmpty(rule.IfcType)
            && !string.Equals(rule.IfcType, el.IfcType, StringComparison.OrdinalIgnoreCase))
            return false;

        // PropertyFiltersJson evaluation deferred — require exact IfcType match for now
        return true;
    }
}
