using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planscape.API.Services;

namespace Planscape.API.Controllers;

/// <summary>
/// Phase A4 (multi-host integration) — substrate drift-check endpoint.
///
/// Every host (Revit, Bonsai, ArchiCAD-IFC, Tekla-IFC, StingBridge) computes
/// a SHA-256 over its copy of the corporate IFC enum/pset manifest
/// (<c>shared/ifc/enums/_manifest.json</c>) and, on login, compares it against
/// the value this endpoint reports. A mismatch means the host is reading a
/// stale or forked substrate — surfaced to the user as a warning so a
/// federation of four tools can't silently coordinate on divergent enums.
///
/// The substrate is GLOBAL (one corporate vocabulary across every tenant and
/// project), so this endpoint is intentionally NOT project-scoped. It requires
/// authentication only.
/// </summary>
[ApiController]
[Route("api/substrate")]
[Authorize]
public class SubstrateController : ControllerBase
{
    private readonly ISubstrateManifestProvider _provider;

    public SubstrateController(ISubstrateManifestProvider provider)
    {
        _provider = provider;
    }

    /// <summary>
    /// GET /api/substrate/manifest
    /// Returns the server's substrate descriptor:
    /// <c>{ "sha256": "&lt;64-hex&gt;", "schemaVersion": 2, "totalEnums": 52 }</c>.
    /// The value is computed once per deployment and cached.
    /// </summary>
    [HttpGet("manifest")]
    [ProducesResponseType(typeof(SubstrateManifestResponse), 200)]
    public ActionResult<SubstrateManifestResponse> GetManifest()
    {
        return Ok(_provider.Get());
    }
}
