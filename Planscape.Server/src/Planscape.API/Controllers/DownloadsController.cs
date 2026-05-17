using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Planscape.API.Controllers;

/// <summary>
/// Serves Planscape Desktop installer downloads and version metadata.
/// All endpoints are publicly accessible — no authentication required.
/// Installers are stored in wwwroot/downloads/ and are placed there by
/// the desktop build pipeline (npm run package:win/mac/linux).
/// </summary>
[ApiController]
[Route("api/downloads")]
public class DownloadsController : ControllerBase
{
    private static readonly Dictionary<string, string> PlatformExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["windows"] = ".exe",
        ["mac"]     = ".dmg",
        ["linux"]   = ".AppImage",
    };

    private readonly IWebHostEnvironment _env;
    private readonly ILogger<DownloadsController> _logger;

    public DownloadsController(IWebHostEnvironment env, ILogger<DownloadsController> logger)
    {
        _env    = env;
        _logger = logger;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string DownloadsRoot => Path.Combine(_env.WebRootPath, "downloads");

    private string LatestJsonPath => Path.Combine(DownloadsRoot, "latest.json");

    private async Task<JsonDocument?> ReadLatestJsonAsync()
    {
        if (!System.IO.File.Exists(LatestJsonPath))
            return null;
        try
        {
            await using var stream = System.IO.File.OpenRead(LatestJsonPath);
            return await JsonDocument.ParseAsync(stream);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse {Path}", LatestJsonPath);
            return null;
        }
    }

    // ── Endpoints ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns current version metadata for all platforms.
    /// Response shape:
    /// <code>
    /// {
    ///   "version": "1.0.0",
    ///   "releaseDate": "2026-05-15",
    ///   "releaseNotes": "...",
    ///   "windows": { "filename": "...", "size": "0", "available": false },
    ///   "mac":     { ... },
    ///   "linux":   { ... }
    /// }
    /// </code>
    /// When no latest.json exists on disk a safe default is returned with
    /// all platforms marked unavailable.
    /// </summary>
    [HttpGet("latest")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLatest()
    {
        var doc = await ReadLatestJsonAsync();
        if (doc is null)
        {
            // Return a safe default so the downloads page renders gracefully
            // even before the first desktop build has been published.
            return Ok(new
            {
                version      = "1.0.0",
                releaseDate  = "2026-05-15",
                releaseNotes = "No release published yet.",
                windows = new { filename = string.Empty, size = "0", available = false },
                mac     = new { filename = string.Empty, size = "0", available = false },
                linux   = new { filename = string.Empty, size = "0", available = false },
            });
        }

        // Return the raw JSON from disk so the build pipeline controls the
        // exact shape (including any extra fields) without code changes.
        return new ContentResult
        {
            Content     = doc.RootElement.GetRawText(),
            ContentType = "application/json; charset=utf-8",
            StatusCode  = StatusCodes.Status200OK,
        };
    }

    /// <summary>
    /// Streams the installer file for the specified platform.
    /// Platform values: windows | mac | linux (case-insensitive).
    /// Returns 404 with a JSON error body when the file has not been published yet.
    /// </summary>
    [HttpGet("file/{platform}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFile(string platform)
    {
        if (!PlatformExtensions.TryGetValue(platform, out var ext))
        {
            return BadRequest(new
            {
                error   = "unknown_platform",
                message = "Valid values are: windows, mac, linux",
            });
        }

        // Resolve the filename from latest.json so the URL stays stable
        // even when the version number changes (the page always links to
        // /api/downloads/file/windows rather than a versioned filename).
        string? filename = null;
        var doc = await ReadLatestJsonAsync();
        if (doc is not null
            && doc.RootElement.TryGetProperty(platform.ToLowerInvariant(), out var platEl)
            && platEl.TryGetProperty("filename", out var fnEl))
        {
            filename = fnEl.GetString();
        }

        // Fall back to scanning for any file with the expected extension.
        if (string.IsNullOrWhiteSpace(filename) || !System.IO.File.Exists(Path.Combine(DownloadsRoot, filename)))
        {
            filename = Directory
                .EnumerateFiles(DownloadsRoot, $"*{ext}", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => System.IO.File.GetLastWriteTimeUtc(f))
                .Select(Path.GetFileName)
                .FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(filename))
        {
            return NotFound(new
            {
                error   = "not_available",
                message = $"The {platform} installer has not been published yet. Check back soon.",
            });
        }

        var filePath = Path.Combine(DownloadsRoot, filename);
        if (!System.IO.File.Exists(filePath))
        {
            return NotFound(new
            {
                error   = "not_available",
                message = $"The {platform} installer file was not found on this server.",
            });
        }

        var mimeType = platform.ToLowerInvariant() switch
        {
            "windows" => "application/octet-stream",
            "mac"     => "application/x-apple-diskimage",
            "linux"   => "application/x-executable",
            _         => "application/octet-stream",
        };

        _logger.LogInformation("Serving installer {Filename} for platform {Platform}", filename, platform);

        // Stream directly from disk — avoids loading the whole file into memory.
        var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 65536, useAsync: true);
        return File(stream, mimeType, filename, enableRangeProcessing: true);
    }

    /// <summary>
    /// Returns the electron-updater auto-update manifest (latest.yml) if present.
    /// Electron Builder publishes this file alongside the installer.
    /// Returns 404 when not yet published.
    /// </summary>
    [HttpGet("latest.yml")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetLatestYml()
    {
        var ymlPath = Path.Combine(DownloadsRoot, "latest.yml");
        if (!System.IO.File.Exists(ymlPath))
            return NotFound();

        return PhysicalFile(ymlPath, "text/yaml; charset=utf-8");
    }

    /// <summary>
    /// Returns the macOS electron-updater auto-update manifest (latest-mac.yml) if present.
    /// </summary>
    [HttpGet("latest-mac.yml")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetLatestMacYml()
    {
        var ymlPath = Path.Combine(DownloadsRoot, "latest-mac.yml");
        if (!System.IO.File.Exists(ymlPath))
            return NotFound();

        return PhysicalFile(ymlPath, "text/yaml; charset=utf-8");
    }

    /// <summary>
    /// Returns the Linux electron-updater auto-update manifest (latest-linux.yml) if present.
    /// </summary>
    [HttpGet("latest-linux.yml")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetLatestLinuxYml()
    {
        var ymlPath = Path.Combine(DownloadsRoot, "latest-linux.yml");
        if (!System.IO.File.Exists(ymlPath))
            return NotFound();

        return PhysicalFile(ymlPath, "text/yaml; charset=utf-8");
    }
}
