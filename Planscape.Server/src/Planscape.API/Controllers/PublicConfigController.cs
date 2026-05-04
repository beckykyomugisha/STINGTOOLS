using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Planscape.API.Controllers;

/// <summary>
/// Phase 169 — single source of truth for client-side runtime config.
/// The dashboard's <c>js/dashboard.js</c> and the Next.js marketing
/// site's <c>AfricaSection</c> both call this endpoint to discover the
/// public Mapbox token rather than hard-coding it. Set the token via:
///   • appsettings.json:   <c>"Maps": { "MapboxToken": "pk.eyJ..." }</c>
///   • environment var:    <c>Maps__MapboxToken=pk.eyJ...</c>  (or <c>MAPBOX_TOKEN=pk.eyJ...</c>)
/// </summary>
[ApiController]
[Route("api/public-config")]
[AllowAnonymous]
public class PublicConfigController : ControllerBase
{
    private readonly IConfiguration _config;

    public PublicConfigController(IConfiguration config) => _config = config;

    /// <summary>Public, unauthenticated configuration for browser clients.</summary>
    /// <response code="200">Configuration object. Empty token means the
    /// server hasn't been configured yet — clients should render a fallback.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult Get()
    {
        var token =
            _config["Maps:MapboxToken"] ??
            Environment.GetEnvironmentVariable("MAPBOX_TOKEN") ??
            "";

        return Ok(new
        {
            mapboxToken = token,
        });
    }
}
