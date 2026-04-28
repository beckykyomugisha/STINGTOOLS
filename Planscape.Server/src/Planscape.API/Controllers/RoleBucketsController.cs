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
    [HttpGet]
    public ActionResult Get() => Ok(new
    {
        // Both forms — the lower-case canonical names that both the
        // server validator and the JS validator accept, and the
        // priority-ordered list used by the loader's inference path
        // (most-specific outcome first).
        buckets = RoleBuckets.Canonical,
        priorityOrder = RoleBuckets.Canonical, // alias for clients
    });
}
