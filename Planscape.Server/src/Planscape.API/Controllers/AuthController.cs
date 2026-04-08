using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Planscape.API.Services;
using Microsoft.IdentityModel.Tokens;
using Planscape.Core;
using Planscape.Core.DTOs;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Planscape.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IConfiguration _config;

    public AuthController(PlanscapeDbContext db, IConfiguration config)
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

        // Create tenant — Starter trial: limits derived from TierLimits (MaxUsers/MaxProjects = 0 = use tier defaults)
        var tenant = new Tenant
        {
            Name           = req.OrganisationName,
            Slug           = req.TenantSlug.ToLowerInvariant().Trim(),
            ContactEmail   = req.Email,
            Tier           = LicenseTier.Starter,
            MaxUsers       = 0,   // 0 = use TierLimits.MaxUsers(Starter) = 20
            MaxProjects    = 0,   // 0 = use TierLimits.MaxProjects(Starter) = 10
            StorageLimitBytes = 0, // 0 = use TierLimits.StorageLimitBytes(Starter) = 2 GB
            MimEnabled     = false,
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
            MaxActivations = TierLimits.MaxActivations(LicenseTier.Starter),  // 5
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

    // ── Forgot Password ────────────────────────────────────────────────────────────
    [EnableRateLimiting("auth")]
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<ActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req,
        [FromServices] IEmailService emailService,
        [FromServices] IConfiguration config)
    {
        // Always return 200 — never reveal whether email exists (security)
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email && u.IsActive);
        if (user != null)
        {
            // Generate 6-digit numeric token (user-friendly)
            string token = new Random().Next(100000, 999999).ToString();
            user.PasswordResetToken = BCrypt.Net.BCrypt.HashPassword(token, workFactor: 6);
            user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddHours(1);
            await _db.SaveChangesAsync();

            string serverUrl = config["App:ServerUrl"] ?? "https://planscape-api.onrender.com";
            await emailService.SendPasswordResetAsync(user.Email, user.DisplayName, token, serverUrl);
        }
        return Ok(new { message = "If that email address exists, a reset code has been sent." });
    }

    // ── Reset Password ─────────────────────────────────────────────────────────────
    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<ActionResult> ResetPassword([FromBody] ResetPasswordRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email && u.IsActive);
        if (user == null || string.IsNullOrEmpty(user.PasswordResetToken)
            || user.PasswordResetTokenExpiresAt < DateTime.UtcNow)
            return BadRequest(new { message = "Invalid or expired reset code." });

        if (!BCrypt.Net.BCrypt.Verify(req.Token, user.PasswordResetToken))
            return BadRequest(new { message = "Invalid or expired reset code." });

        if (req.NewPassword.Length < 8)
            return BadRequest(new { message = "Password must be at least 8 characters." });

        user.PasswordHash              = HashPassword(req.NewPassword);
        user.PasswordResetToken        = null;
        user.PasswordResetTokenExpiresAt = null;
        // Invalidate all sessions
        user.RefreshToken              = null;
        user.RefreshTokenExpiresAt     = null;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Password reset successfully. Please log in again." });
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
            ServerUrl = $"https://{key.Tenant?.Slug}.planscape.io",
            ExpiresAt = key.ExpiresAt
        });
    }

    // ── Forgot Password (request reset) ──────────────────────────────────────

    [EnableRateLimiting("auth")]
    [HttpPost("forgot-password")]
    public async Task<ActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req)
    {
        // Always return success to prevent email enumeration
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email && u.IsActive);
        if (user == null)
            return Ok(new { message = "If that email exists, a reset link has been sent." });

        // Generate a time-limited reset token (stored as refresh token with short expiry)
        var resetToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        user.RefreshToken = $"RESET:{resetToken}";
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddHours(1);
        await _db.SaveChangesAsync();

        // Send reset email
        var emailService = HttpContext.RequestServices.GetService<Planscape.Core.Interfaces.IEmailService>();
        if (emailService != null)
        {
            await emailService.SendAsync(user.Email, "Planscape Password Reset",
                $"Use this token to reset your password (expires in 1 hour):\n\n{resetToken}\n\n" +
                $"POST /api/auth/reset-password with {{ \"token\": \"{resetToken}\", \"newPassword\": \"...\" }}");
        }

        return Ok(new { message = "If that email exists, a reset link has been sent." });
    }

    // ── Reset Password (confirm reset) ────────────────────────────────────────

    [EnableRateLimiting("auth")]
    [HttpPost("reset-password")]
    public async Task<ActionResult> ResetPassword([FromBody] ResetPasswordRequest req)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.RefreshToken == $"RESET:{req.Token}"
                && u.RefreshTokenExpiresAt > DateTime.UtcNow
                && u.IsActive);

        if (user == null)
            return BadRequest(new { message = "Invalid or expired reset token" });

        if (req.NewPassword.Length < 8)
            return BadRequest(new { message = "Password must be at least 8 characters" });

        user.PasswordHash = HashPassword(req.NewPassword);
        user.RefreshToken = null;
        user.RefreshTokenExpiresAt = null;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Password has been reset. Please log in." });
    }

    private string GenerateJwt(Core.Entities.AppUser user)
    {
        var jwtKey = _config["Jwt:Key"] ?? "Planscape-Dev-Secret-Key-Min32Chars!!";
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
            issuer: _config["Jwt:Issuer"] ?? "Planscape",
            audience: _config["Jwt:Audience"] ?? "Planscape.Client",
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
