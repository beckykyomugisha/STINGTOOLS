using System.Text.Json;
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

        // Batch-load existing draft lines for this model to avoid per-element round-trips.
        var incomingGlobalIds = elements.Select(e => e.GlobalId).Distinct().ToHashSet();
        var existingLines = await _db.QuantityLines
            .Where(q => q.ProjectModelId == projectModelId && q.BaselineId == null
                     && q.IfcGlobalId != null && incomingGlobalIds.Contains(q.IfcGlobalId!))
            .ToListAsync(ct);
        var existingLookup = existingLines
            .GroupBy(q => (q.IfcGlobalId!, q.TakeoffRuleId ?? Guid.Empty))
            .ToDictionary(g => g.Key, g => g.First());

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

                existingLookup.TryGetValue((el.GlobalId, rule.Id), out var existing);

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

        if (!string.IsNullOrWhiteSpace(rule.PropertyFiltersJson))
        {
            try
            {
                var filters = JsonSerializer.Deserialize<PropertyFilter[]>(
                    rule.PropertyFiltersJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (filters is not null)
                    foreach (var f in filters)
                        if (!EvaluateFilter(f, el.Properties))
                            return false;
            }
            catch (JsonException)
            {
                // Malformed JSON in the rule — treat as no filter (don't silently drop elements).
            }
        }

        return true;
    }

    private static bool EvaluateFilter(PropertyFilter f, IReadOnlyDictionary<string, string> props)
    {
        if (string.IsNullOrEmpty(f.Prop)) return true;

        // Resolve value: prefer "Pset.Prop" fully-qualified key, then bare "Prop" key.
        string? rawValue = null;
        if (!string.IsNullOrEmpty(f.Pset))
            props.TryGetValue($"{f.Pset}.{f.Prop}", out rawValue);
        if (rawValue is null)
            props.TryGetValue(f.Prop, out rawValue);

        // JsonElement.ToString() returns the value without JSON delimiters for strings/numbers/bools.
        var filterValue = f.Value is System.Text.Json.JsonElement je
            ? je.ValueKind == System.Text.Json.JsonValueKind.String
                ? je.GetString() ?? ""
                : je.GetRawText()
            : f.Value?.ToString() ?? "";

        var op = (f.Op ?? "eq").ToLowerInvariant() switch
        {
            "=" or "==" => "eq",
            "!=" or "<>" => "neq",
            ">" => "gt",
            "<" => "lt",
            ">=" => "gte",
            "<=" => "lte",
            var x => x,
        };

        // Numeric comparison when both sides parse as doubles.
        if ((op == "gt" || op == "lt" || op == "gte" || op == "lte")
            && double.TryParse(rawValue, out var dActual)
            && double.TryParse(filterValue, out var dFilter))
        {
            return op switch
            {
                "gt"  => dActual >  dFilter,
                "lt"  => dActual <  dFilter,
                "gte" => dActual >= dFilter,
                "lte" => dActual <= dFilter,
                _     => true,
            };
        }

        // String comparisons (case-insensitive).
        return op switch
        {
            "eq"         => string.Equals(rawValue, filterValue, StringComparison.OrdinalIgnoreCase),
            "neq"        => !string.Equals(rawValue, filterValue, StringComparison.OrdinalIgnoreCase),
            "contains"   => rawValue?.Contains(filterValue, StringComparison.OrdinalIgnoreCase) ?? false,
            "startswith" => rawValue?.StartsWith(filterValue, StringComparison.OrdinalIgnoreCase) ?? false,
            "endswith"   => rawValue?.EndsWith(filterValue, StringComparison.OrdinalIgnoreCase) ?? false,
            "exists"     => rawValue is not null,
            _            => true,   // unknown operator — don't reject the element
        };
    }

    private sealed record PropertyFilter(
        string? Pset,
        string Prop,
        string? Op,
        object? Value);
}
