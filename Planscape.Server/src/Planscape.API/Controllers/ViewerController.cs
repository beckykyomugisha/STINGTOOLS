using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Planscape.API.Controllers;

/// <summary>
/// PHASE 93 — xeokit-based model viewer.
///
/// Serves XKT files from {Storage:Path}/xkt/ so the wwwroot/viewer/index.html
/// page can load federated model snapshots without Revit or a Speckle account.
///
/// Auth guard pattern copied from ProjectsController: the controller is
/// [Authorize] at class level and resolves the tenant via the JWT claim.
/// File-serving uses PhysicalFile(...) which returns a PhysicalFileResult —
/// the same underlying primitive DocumentsController ends up at through its
/// IFileStorageService abstraction, but applied directly here because XKT
/// files are stored in a flat on-disk directory rather than per-tenant.
///
/// Endpoints (both JWT-protected):
///   GET /api/viewer/models                — list available XKT filenames
///   GET /api/viewer/models/{filename}     — download a specific XKT file
/// </summary>
[ApiController]
[Route("api/viewer")]
[Authorize]
public class ViewerController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<ViewerController> _logger;

    public ViewerController(IConfiguration config, ILogger<ViewerController> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Returns the directory that holds XKT files ({Storage:Path}/xkt/).
    /// Uses the same Storage:Path config key that LocalFileStorageService
    /// consumes, so both the viewer and the document pipeline share roots.
    /// </summary>
    private string GetXktDirectory()
    {
        var root = _config["Storage:Path"]
                   ?? Path.Combine(Directory.GetCurrentDirectory(), "storage");
        return Path.Combine(root, "xkt");
    }

    /// <summary>List XKT filenames available on disk.</summary>
    /// <response code="200">JSON array of filenames (no path, no extension stripping).</response>
    [HttpGet("models")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<string>> ListModels()
    {
        var dir = GetXktDirectory();
        if (!Directory.Exists(dir))
        {
            // Empty list (not 404) — makes the viewer page render a clean
            // "no models yet" state instead of an auth-looking error.
            return Ok(Array.Empty<string>());
        }

        var files = Directory.EnumerateFiles(dir, "*.xkt", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Ok(files);
    }

    /// <summary>Stream a single XKT file back to the viewer page.</summary>
    /// <response code="200">Binary XKT payload (application/octet-stream).</response>
    /// <response code="400">Filename contains path separators.</response>
    /// <response code="404">File not found on disk.</response>
    [HttpGet("models/{filename}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetModel(string filename)
    {
        // Path traversal guard — reject anything that could escape the xkt dir.
        if (string.IsNullOrWhiteSpace(filename)
            || filename.Contains('/')
            || filename.Contains('\\')
            || filename.Contains("..", StringComparison.Ordinal))
        {
            return BadRequest(new { error = "Invalid filename." });
        }

        var path = Path.Combine(GetXktDirectory(), filename);
        if (!System.IO.File.Exists(path)) return NotFound();

        // PhysicalFileResult — the download pattern the spec calls out.
        // enableRangeProcessing so xeokit can use HTTP Range requests when
        // streaming large XKT payloads.
        return PhysicalFile(path, "application/octet-stream", filename, enableRangeProcessing: true);
    }
}
