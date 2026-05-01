namespace Planscape.Core.Entities;

/// <summary>
/// S1.1 — marker interface for entities that are tenant-isolated. Every
/// entity carrying user-generated data MUST implement this; the EF
/// <c>HasQueryFilter</c> in <c>PlanscapeDbContext</c> filters every query by
/// <c>ITenantContext.TenantId</c> and <c>SaveChangesAsync</c> auto-stamps
/// the field on Add. Together they make tenant leaks structurally
/// impossible — adding a new entity without implementing this interface
/// will leak across tenants, but at least it's a deliberate omission
/// rather than a missing-Where forgotten in a controller.
///
/// TenantId is denormalised onto child entities (e.g. BimIssue carries
/// TenantId in addition to ProjectId) so the query filter is a single
/// indexed column lookup, no join required. This is standard practice
/// in mature multi-tenant systems and aligns with
/// docs/PLANSCAPE_GAPS.md Phase 1 P0 hardening.
/// </summary>
public interface ITenantScoped
{
    Guid TenantId { get; set; }
}
