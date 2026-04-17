using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.API.Controllers;

/// <summary>
/// FLEX-03 — tenant branding CRUD.
///
///   GET    /api/tenant/branding           → resolved branding for current user (any authenticated)
///   PUT    /api/tenant/branding           → persist branding (Admin / Owner only)
///   DELETE /api/tenant/branding           → revert to config defaults
///   POST   /api/tenant/branding/templates/reload → bust the template cache (Admin)
///
/// Mobile should call GET on cold start and cache the result locally. Web dashboard
/// can expose PUT in a settings drawer.
/// </summary>
[ApiController]
[Route("api/tenant/branding")]
[Authorize]
public class TenantBrandingController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ITenantBrandingService _branding;
    private readonly IEmailTemplateRenderer _renderer;

    public TenantBrandingController(
        PlanscapeDbContext db,
        ITenantBrandingService branding,
        IEmailTemplateRenderer renderer)
    {
        _db = db;
        _branding = branding;
        _renderer = renderer;
    }

    [HttpGet]
    public async Task<ActionResult<ResolvedBranding>> Get(CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty) return Unauthorized();
        var resolved = await _branding.GetAsync(tenantId, ct);
        return Ok(resolved);
    }

    /// <summary>Returns the raw (unmerged) row — null when the tenant uses defaults.</summary>
    [HttpGet("raw")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<ActionResult<TenantBranding?>> GetRaw(CancellationToken ct)
    {
        var tenantId = GetTenantId();
        var row = await _db.TenantBrandings.AsNoTracking()
                           .FirstOrDefaultAsync(b => b.TenantId == tenantId, ct);
        return Ok(row);
    }

    [HttpPut]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<ActionResult<ResolvedBranding>> Update(
        [FromBody] UpdateBrandingRequest req, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty) return Unauthorized();

        // Validate colors (belt-and-braces — DB + client should also enforce).
        if (!string.IsNullOrEmpty(req.AccentColor) && !IsHexColor(req.AccentColor!))
            return BadRequest(new { error = "AccentColor must be a 3/6-digit hex value beginning with '#'." });
        if (!string.IsNullOrEmpty(req.HeaderColor) && !IsHexColor(req.HeaderColor!))
            return BadRequest(new { error = "HeaderColor must be a 3/6-digit hex value beginning with '#'." });
        if (!string.IsNullOrEmpty(req.LogoUrl) && !Uri.TryCreate(req.LogoUrl, UriKind.Absolute, out _))
            return BadRequest(new { error = "LogoUrl must be an absolute https:// URL." });

        var userId = GetUserId();
        var branding = new TenantBranding
        {
            ProductName      = req.ProductName,
            AccentColor      = req.AccentColor,
            HeaderColor      = req.HeaderColor,
            LogoUrl          = req.LogoUrl,
            SupportEmail     = req.SupportEmail,
            EmailFromName    = req.EmailFromName,
            EmailFromAddress = req.EmailFromAddress,
            EmailSignature   = req.EmailSignature,
            DefaultLanguage  = req.DefaultLanguage,
            UpdatedByUserId  = userId,
        };

        await _branding.SetAsync(tenantId, branding, ct);
        return Ok(await _branding.GetAsync(tenantId, ct));
    }

    [HttpDelete]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> Reset(CancellationToken ct)
    {
        var tenantId = GetTenantId();
        var row = await _db.TenantBrandings.FirstOrDefaultAsync(b => b.TenantId == tenantId, ct);
        if (row != null)
        {
            _db.TenantBrandings.Remove(row);
            await _db.SaveChangesAsync(ct);
        }
        _branding.InvalidateCache(tenantId);
        return NoContent();
    }

    [HttpPost("templates/reload")]
    [Authorize(Roles = "Admin,Owner")]
    public IActionResult ReloadTemplates()
    {
        _renderer.Reload();
        return Ok(new { reloaded = true });
    }

    // ── helpers ──────────────────────────────────────────────────────

    private static bool IsHexColor(string s) =>
        System.Text.RegularExpressions.Regex.IsMatch(s, @"^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$");

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;

    private Guid? GetUserId() =>
        Guid.TryParse(User.FindFirst("sub")?.Value, out var id) ? id : null;
}

public record UpdateBrandingRequest(
    string? ProductName,
    string? AccentColor,
    string? HeaderColor,
    string? LogoUrl,
    string? SupportEmail,
    string? EmailFromName,
    string? EmailFromAddress,
    string? EmailSignature,
    string? DefaultLanguage);
