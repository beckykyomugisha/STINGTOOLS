using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Planscape.API.Controllers;

/// <summary>
/// Phase 159 — recommended SOC2 / ISO 27001 audit categories for the
/// security-action audit-log entries that <see cref="SecurityController"/>
/// emits. The list is operator-configurable via appsettings:
///
///   "Audit": {
///     "Categories": [
///       "suspected_credential_leak",
///       "employee_offboarding",
///       "scheduled_rotation",
///       "suspicious_activity",
///       "regulatory_request"
///     ]
///   }
///
/// Falls back to a built-in list when the appsettings section is
/// absent so a fresh deployment has sensible defaults out of the box.
/// The category field on revoke-tokens stays free-form (no
/// server-side enum constraint) so taxonomies can evolve, but
/// dashboard / mobile clients can fetch this endpoint once per
/// session and render a dropdown that nudges operators toward the
/// recommended values.
/// </summary>
[ApiController]
[Route("api/audit/categories")]
[Authorize]
public class AuditCategoriesController : ControllerBase
{
    private static readonly string[] DefaultCategories =
    {
        "suspected_credential_leak",
        "employee_offboarding",
        "scheduled_rotation",
        "suspicious_activity",
        "policy_change",
        "regulatory_request",
        "unspecified",
    };

    private readonly IConfiguration _config;

    public AuditCategoriesController(IConfiguration config) => _config = config;

    [HttpGet]
    public ActionResult Get()
    {
        // Phase 159 — read the operator-configured list and merge with
        // built-ins. Operator-supplied entries win on case-insensitive
        // dedupe so a tenant can rename or reorder without losing the
        // canonical list. Empty / missing section → built-ins only.
        var configured = _config.GetSection("Audit:Categories")
            .GetChildren()
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim().ToLowerInvariant())
            .ToList();

        var merged = configured.Count > 0
            ? configured
                .Concat(DefaultCategories)
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToList()
            : DefaultCategories.ToList();

        // ETag is content-stable — same list across the deployment
        // unless appsettings changed. Cache-Control matches the
        // role-buckets endpoint so dashboards revalidate at most once
        // per hour.
        var etag = "\"" + System.Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(string.Join(",", merged))
            )).Substring(0, 16) + "\"";

        Response.Headers.ETag = etag;
        Response.Headers.CacheControl = "private, max-age=3600";

        if (Request.Headers.IfNoneMatch.Count > 0
            && Request.Headers.IfNoneMatch.Contains(etag))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return Ok(new
        {
            categories = merged,
            // The endpoint is advisory — clients can submit any
            // string. Surface that explicitly so operators don't
            // assume the list is enforced.
            note = "Advisory only. The revoke-tokens endpoint accepts any string in the category field.",
        });
    }
}
