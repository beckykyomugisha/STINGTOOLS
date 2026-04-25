using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Planscape.Core.Interfaces;

namespace Planscape.API.Controllers;

/// <summary>
/// FLEX-15 — Internationalisation endpoints.
///
///   GET /api/i18n                → list of supported language codes + resolved language for the caller
///   GET /api/i18n/{language}     → full translation map for a language (mobile prefetch)
///   POST /api/i18n/reload        → hot-reload resource files (Admin only)
/// </summary>
[ApiController]
[Route("api/i18n")]
public class I18nController : ControllerBase
{
    private readonly II18nService _i18n;
    private readonly IHostEnvironment _env;

    public I18nController(II18nService i18n, IHostEnvironment env)
    {
        _i18n = i18n;
        _env = env;
    }

    [HttpGet]
    [AllowAnonymous]
    public ActionResult GetSupported()
    {
        var resolved = (HttpContext.Items["Language"] as string) ?? "en";
        return Ok(new
        {
            supported = _i18n.SupportedLanguages,
            resolved,
            fallback = "en",
        });
    }

    [HttpGet("{language}")]
    [AllowAnonymous]
    public ActionResult GetBundle(string language, CancellationToken ct)
    {
        // Return the raw JSON file so the mobile app can use i18next with HTTP backend
        // or cache it under AsyncStorage. We only serve files the service has already
        // validated at startup (prevents path traversal on arbitrary filenames).
        if (!_i18n.SupportedLanguages.Contains(language.ToLowerInvariant()))
            return NotFound(new { error = "unsupported_language", requested = language });

        var root = Path.Combine(_env.ContentRootPath, "I18n");
        var file = Path.Combine(root, $"{language}.json");
        if (!System.IO.File.Exists(file)) return NotFound();
        var content = System.IO.File.ReadAllText(file);
        return Content(content, "application/json");
    }

    [HttpPost("reload")]
    [Authorize(Roles = "Admin,Owner")]
    public IActionResult Reload()
    {
        _i18n.Reload();
        return Ok(new { reloaded = true, languages = _i18n.SupportedLanguages });
    }
}
