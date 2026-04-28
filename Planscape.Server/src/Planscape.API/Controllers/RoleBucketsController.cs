using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planscape.Infrastructure.Workflow;

namespace Planscape.API.Controllers;

/// <summary>
/// Phase 154 — single-source-of-truth endpoint for the canonical
/// deliverable state-machine role buckets. Was the dashboard JS
/// validator's manual mirror of the server set; now it fetches this
/// once on first render so adding a seventh bucket is a one-file
/// server change instead of a coupled client+server update.
///
/// Read by anyone authenticated — no tenant data leaks here, the
/// list is universal across deployments.
/// </summary>
[ApiController]
[Route("api/state-machine/role-buckets")]
[Authorize]
public class RoleBucketsController : ControllerBase
{
    /// <summary>Static body — same on every request, same across every
    /// deployment with the same code. We compute it once at type-init
    /// time so the controller path is allocation-free.</summary>
    private static readonly object PayloadObject = new
    {
        keywordBlockBuckets = RoleBuckets.Canonical,
        rolesBlockKeys = new[]
        {
            "rejecting", "accepting", "submitting", "terminal",
            "working", "initial", "none",
        },
        buckets = RoleBuckets.Canonical,        // legacy alias (Phase 154)
        priorityOrder = RoleBuckets.Canonical,  // legacy alias (Phase 154)
    };

    /// <summary>Phase 156 — strong ETag derived from the SHA-256 of the
    /// payload's JSON serialisation. Stable across processes (the
    /// canonical list is hardcoded), so any instance in a horizontal-
    /// scaled fleet returns the same tag for the same payload.</summary>
    private static readonly string ETag = ComputeETag(PayloadObject);

    private static string ComputeETag(object payload)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json));
        // Strong validator: surrounded by quotes per RFC 7232.
        return "\"" + System.Convert.ToHexString(hash).Substring(0, 16) + "\"";
    }

    [HttpGet]
    public ActionResult Get()
    {
        // Phase 156 — ETag / 304 negotiation. The body is constant
        // across the deployment so we can answer If-None-Match
        // matches with a body-less 304. Saves ~250 bytes per call
        // and lets browsers / mobile clients keep a long-lived
        // local cache. Cache-Control hints clients to reuse for
        // up to 1 hour without revalidating, but every request
        // that does revalidate hits the cheap match-and-304 path.
        Response.Headers.ETag = ETag;
        Response.Headers.CacheControl = "private, max-age=3600";

        if (Request.Headers.IfNoneMatch.Count > 0
            && Request.Headers.IfNoneMatch.Contains(ETag))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }
        return Ok(PayloadObject);
    }
}
