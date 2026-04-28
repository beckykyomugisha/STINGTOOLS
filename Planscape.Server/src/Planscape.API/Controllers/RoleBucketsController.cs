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
        // Phase 155 — surface the role-block-vs-keyword-block
        // asymmetry the JS validator needs to honour.
        //
        //   keywordBlockBuckets — what a tenant's `"keywords"` block
        //     can declare. Six canonical buckets, no "none". Used by
        //     the dashboard's keyword-extension validator.
        //
        //   rolesBlockKeys — what a custom-machine `"roles"` block
        //     can map a state to. Six canonical buckets PLUS the
        //     "none" sentinel meaning "this state has no semantic
        //     role; skip metadata side-effects on transition".
        //
        // The legacy `buckets` / `priorityOrder` aliases are kept for
        // backward compat with Phase 154 dashboards that read the
        // canonical list under those keys.
        keywordBlockBuckets = RoleBuckets.Canonical,
        rolesBlockKeys = new[]
        {
            "rejecting", "accepting", "submitting", "terminal",
            "working", "initial", "none",
        },
        buckets = RoleBuckets.Canonical,        // legacy alias (Phase 154)
        priorityOrder = RoleBuckets.Canonical,  // legacy alias (Phase 154)
    });
}
