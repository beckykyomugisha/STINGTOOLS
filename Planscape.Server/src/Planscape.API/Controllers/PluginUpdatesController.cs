using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Planscape.API.Controllers;

/// <summary>
/// S8.4 — plugin auto-update endpoint. The Revit plugin polls this on
/// startup; if the manifest reports a newer version + a download URL,
/// the plugin offers the author a one-click upgrade.
///
/// Not pushed automatically — Revit add-ins can't hot-reload, so the
/// plugin queues the upgrade for the next Revit launch + shows a
/// 'restart Revit to upgrade' nag. Users on enterprise lockdown who
/// can't update freely see the nag forever; the operator can flip
/// the kill-switch in appsettings to silence it for those tenants.
///
/// Manifest is config-driven so a hotfix doesn't require an API
/// redeploy; flip the version + URL in appsettings.
/// </summary>
[ApiController]
[Route("api/plugin/updates")]
[AllowAnonymous]
public class PluginUpdatesController : ControllerBase
{
    private readonly IConfiguration _config;

    public PluginUpdatesController(IConfiguration config) { _config = config; }

    [HttpGet("manifest")]
    public ActionResult Manifest([FromQuery] string? channel)
    {
        // Channels: 'stable' (default), 'beta', 'enterprise'. Beta is
        // opt-in for friendly customers; enterprise is the slow ring
        // for tenants on procurement-controlled IT.
        var ch = (channel ?? "stable").ToLowerInvariant();
        var version    = _config[$"PluginUpdates:{ch}:Version"]   ?? "1.0.0.0";
        var sha256     = _config[$"PluginUpdates:{ch}:Sha256"]    ?? "";
        var downloadUrl= _config[$"PluginUpdates:{ch}:DownloadUrl"] ?? "";
        var minRevit   = _config[$"PluginUpdates:{ch}:MinRevit"]  ?? "2025";
        var notes      = _config[$"PluginUpdates:{ch}:ReleaseNotes"] ?? "";

        return Ok(new
        {
            channel = ch,
            version,
            sha256,
            downloadUrl,
            minRevit,
            releaseNotes = notes,
            killSwitch  = bool.TryParse(_config["PluginUpdates:KillSwitch"], out var k) && k,
            polledAt   = DateTime.UtcNow,
        });
    }
}
