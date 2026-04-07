using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using StingBIM.Core.DTOs;
using StingBIM.Core.Entities;
using StingBIM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace StingBIM.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly StingBimDbContext _db;
    private readonly IConfiguration _config;

    public AuthController(StingBimDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    // ── Login ──────────────────────────────────────────────────────────────────

    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public async Task<ActionResult<AuthLoginResponse>> Login([FromBody] AuthLoginRequest req)
    {
        var user = await _db.Users
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Email == req.Email && u.IsActive);

        if (user == null || !BCryptVerify(req.Password, user.PasswordHash))
            return Unauthorized(new { message = "Invalid email or password" });

        var token = GenerateJwt(user);
        var refreshToken = Guid.NewGuid().ToString("N");
        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(30);
        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new AuthLoginResponse
        {
            AccessToken = token,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddHours(8),
            UserName = user.DisplayName,
            Role = user.Role.ToString(),
            Tier = user.Tenant?.Tier.ToString() ?? "Starter",
            MimEnabled = user.Tenant?.MimEnabled ?? false
        });
    }

    // ── Refresh Token ──────────────────────────────────────────────────────────

    [HttpPost("refresh")]
    public async Task<ActionResult> RefreshToken([FromBody] RefreshTokenRequest req)
    {
        var user = await _db.Users
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.RefreshToken == req.RefreshToken && u.IsActive);

        if (user == null || user.RefreshTokenExpiresAt < DateTime.UtcNow)
            return Unauthorized(new { message = "Invalid or expired refresh token" });

        var newAccessToken  = GenerateJwt(user);
        var newRefreshToken = Guid.NewGuid().ToString("N");
        user.RefreshToken          = newRefreshToken;
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(30);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            accessToken  = newAccessToken,
            refreshToken = newRefreshToken,
            expiresAt    = DateTime.UtcNow.AddHours(8)
        });
    }

    // ── Self-Registration (first-time tenant setup only) ───────────────────────

    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    public async Task<ActionResult> Register([FromBody] RegisterRequest req)
    {
        // Only allow registration if the tenant slug is not yet taken
        if (await _db.Tenants.AnyAsync(t => t.Slug == req.TenantSlug))
            return Conflict(new { message = $"Organisation slug '{req.TenantSlug}' is already taken" });

        if (await _db.Users.AnyAsync(u => u.Email == req.Email))
            return Conflict(new { message = "Email already registered" });

        if (req.Password.Length < 8)
            return BadRequest(new { message = "Password must be at least 8 characters" });

        // Create tenant
        var tenant = new Tenant
        {
            Name          = req.OrganisationName,
            Slug          = req.TenantSlug.ToLowerInvariant().Trim(),
            ContactEmail  = req.Email,
            Tier          = LicenseTier.Starter,
            MaxUsers      = 5,
            MaxProjects   = 1,
            MimEnabled    = false,
            TrialExpiresAt = DateTime.UtcNow.AddDays(30)
        };
        _db.Tenants.Add(tenant);

        // Create owner account
        var owner = new AppUser
        {
            TenantId      = tenant.Id,
            Email         = req.Email.Trim().ToLowerInvariant(),
            DisplayName   = req.DisplayName,
            PasswordHash  = HashPassword(req.Password),
            Role          = UserRole.Owner,
            Iso19650Role  = "A"
        };
        _db.Users.Add(owner);

        // Seed a trial license key
        var licenseKey = new LicenseKey
        {
            TenantId       = tenant.Id,
            Key            = $"STING-TRIAL-{Guid.NewGuid():N}".ToUpper()[..28],
            Tier           = LicenseTier.Starter,
            MaxActivations = 3,
            MimEnabled     = false,
            ExpiresAt      = DateTime.UtcNow.AddDays(30)
        };
        _db.LicenseKeys.Add(licenseKey);

        await _db.SaveChangesAsync();

        var token = GenerateJwt(owner);
        return CreatedAtAction(null, null, new
        {
            accessToken    = token,
            refreshToken   = (string?)null,  // user must login to get refresh token
            expiresAt      = DateTime.UtcNow.AddHours(8),
            userName       = owner.DisplayName,
            tier           = "Starter",
            trialExpiresAt = tenant.TrialExpiresAt,
            licenseKey     = licenseKey.Key,
            message        = "Account created. 30-day Starter trial active. Add your licence key from the Admin panel to upgrade."
        });
    }

    // ── Change Password (authenticated) ───────────────────────────────────────

    [HttpPost("change-password")]
    [Authorize]
    public async Task<ActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var userId = Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value, out var id) ? id : Guid.Empty;
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return NotFound();

        if (!BCryptVerify(req.CurrentPassword, user.PasswordHash))
            return Unauthorized(new { message = "Current password is incorrect" });

        if (req.NewPassword.Length < 8)
            return BadRequest(new { message = "New password must be at least 8 characters" });

        user.PasswordHash = HashPassword(req.NewPassword);
        // Invalidate all existing refresh tokens
        user.RefreshToken          = null;
        user.RefreshTokenExpiresAt = null;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Password changed. Please log in again." });
    }

    // ── Me (current user info) ─────────────────────────────────────────────────

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult> Me()
    {
        var userId = Guid.TryParse(User.FindFirst("sub")?.Value, out var id) ? id : Guid.Empty;
        var user = await _db.Users.Include(u => u.Tenant).FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return NotFound();

        return Ok(new
        {
            user.Id, user.Email, user.DisplayName, user.Role, user.Iso19650Role,
            Tier = user.Tenant?.Tier.ToString() ?? "Starter",
            user.Tenant?.MimEnabled,
            user.LastLoginAt
        });
    }

    // ── Licence activation ─────────────────────────────────────────────────────

    [HttpPost("license/activate")]
    public async Task<ActionResult<LicenseActivationResponse>> ActivateLicense([FromBody] LicenseActivationRequest req)
    {
        var key = await _db.LicenseKeys
            .Include(k => k.Tenant)
            .FirstOrDefaultAsync(k => k.Key == req.LicenseKey && k.IsActive);

        if (key == null)
            return Ok(new LicenseActivationResponse { Valid = false, Message = "Invalid license key" });

        if (key.ExpiresAt.HasValue && key.ExpiresAt < DateTime.UtcNow)
            return Ok(new LicenseActivationResponse { Valid = false, Message = "License key has expired" });

        if (key.CurrentActivations >= key.MaxActivations)
            return Ok(new LicenseActivationResponse { Valid = false, Message = $"Maximum activations ({key.MaxActivations}) reached" });

        key.CurrentActivations++;
        key.LastActivatedBy = req.UserName;
        key.LastActivatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new LicenseActivationResponse
        {
            Valid = true,
            Tier = key.Tier.ToString(),
            MimEnabled = key.MimEnabled,
            ServerUrl = $"https://{key.Tenant?.Slug}.stingbim.io",
            ExpiresAt = key.ExpiresAt
        });
    }

    private string GenerateJwt(Core.Entities.AppUser user)
    {
        var jwtKey = _config["Jwt:Key"] ?? "StingBIM-Dev-Secret-Key-Min32Chars!!";
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("tenant_id", user.TenantId.ToString()),
            new Claim("tenant_slug", user.Tenant?.Slug ?? ""),
            new Claim("role", user.Role.ToString()),
            new Claim("iso_role", user.Iso19650Role),
            new Claim("display_name", user.DisplayName)
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"] ?? "StingBIM",
            audience: _config["Jwt:Audience"] ?? "StingBIM.Client",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static bool BCryptVerify(string password, string hash)
        => BCrypt.Net.BCrypt.Verify(password, hash);

    /// <summary>
    /// Hashes a password using BCrypt (work factor 12).
    /// </summary>
    public static string HashPassword(string password)
        => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
}
