using Microsoft.AspNetCore.Authorization;

namespace Planscape.Infrastructure.Authorization;

/// <summary>
/// Phase 152 — authorisation requirement satisfied when EITHER the
/// caller has the tenant-level <c>Admin</c> / <c>Owner</c> role
/// (existing controller-level guard) OR the caller has at least one
/// active <see cref="Planscape.Core.Entities.ProjectMember"/> row with
/// ISO 19650 role <c>K</c> (BIM Manager). Used by tenant-keywords
/// admin endpoints so a BIM Manager can edit deliverable-state-machine
/// vocabulary without having to be promoted to a tenant Owner.
///
/// Failure modes:
///   • No <c>tenant_id</c> claim → unauthenticated request, denied.
///   • Tenant Admin / Owner role claim → granted, no DB hit.
///   • Otherwise → DB lookup against ProjectMembers; granted iff at
///     least one row has Iso19650Role == "K" (BIM Manager).
/// </summary>
public sealed class BimManagerOrAdminRequirement : IAuthorizationRequirement { }
