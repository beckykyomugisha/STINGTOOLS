using Microsoft.AspNetCore.Authorization;

namespace Planscape.Infrastructure.Authorization;

/// <summary>
/// Phase 158 — authorisation requirement satisfied when the caller
/// holds the <c>Admin</c> / <c>Owner</c> tenant role (existing
/// operator superset) OR the new <c>SecurityOfficer</c> role
/// (Phase 158, separation-of-duties for SOC2 / ISO 27001).
///
/// SecurityOfficer is intentionally narrow: it grants session-
/// termination + audit-read privileges but NOT user / project /
/// vocabulary edit. Use this requirement on endpoints that should
/// be reachable from a security persona without giving them tenant
/// admin powers.
/// </summary>
public sealed class SecurityOfficerOrAdminRequirement : IAuthorizationRequirement { }
