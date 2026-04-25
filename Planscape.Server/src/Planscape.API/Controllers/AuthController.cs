using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
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
/// <summary>
/// Authentication — login, registration, password management, and licence activation.
/// </summary>
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

    /// <summary>Authenticate with email and password to obtain a JWT.</summary>
    /// <response code="200">JWT access token, refresh token, and user info.</response>
    /// <response code="401">Invalid email or password.</response>
    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
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

    /// <summary>Exchange a refresh token for a new access token.</summary>
    /// <response code="200">New JWT access token and refresh token.</response>
    /// <response code="401">Invalid or expired refresh token.</response>
    [HttpPost("refresh")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
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

    /// <summary>Register a new tenant organisation and owner account (30-day Starter trial).</summary>
    /// <response code="201">Account created with access token and trial licence key.</response>
    /// <response code="400">Password too short.</response>
    /// <response code="409">Organisation slug or email already taken.</response>
    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
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
            Key            = $"STING-TRIAL-{Guid.NewGuid():N}".ToUpper()[..32],
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

    /// <summary>Change the current user's password (requires authentication).</summary>
    /// <response code="200">Password changed — all refresh tokens invalidated.</response>
    /// <response code="400">New password too short.</response>
    /// <response code="401">Current password is incorrect.</response>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
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

    /// <summary>Return the authenticated user's profile and tenant info.</summary>
    /// <response code="200">User profile object.</response>
    /// <response code="404">User not found (token valid but account deleted).</response>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Me()
    {
        // JWT middleware maps "sub" to ClaimTypes.NameIdentifier by default, so check both
        var subClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        var userId = Guid.TryParse(subClaim, out var id) ? id : Guid.Empty;
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

    // ── Accept invitation (P10) ────────────────────────────────────────────────

    /// <summary>
    /// Exchange an invitation token for a fully-activated account + JWT.
    ///
    /// Flow:
    ///   1. Admin calls POST /api/projects/{id}/members/invite → creates a
    ///      pending AppUser (IsActive=false) with an INV: token in
    ///      RefreshToken (expires in 14 days).
    ///   2. User receives an email with /accept-invitation?token=…&amp;email=…
    ///   3. Mobile/web POSTs here with token + email + password.
    ///   4. Server sets password, IsActive=true, clears token, returns JWT.
    /// </summary>
    /// <response code="200">Account activated — access + refresh tokens returned.</response>
    /// <response code="400">Invalid / expired token.</response>
    [EnableRateLimiting("auth")]
    [HttpPost("accept-invitation")]
    [ProducesResponseType(typeof(AuthLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthLoginResponse>> AcceptInvitation([FromBody] AcceptInvitationRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Token) || string.IsNullOrWhiteSpace(req.Email))
            return BadRequest(new { message = "Token and email are required." });
        if (req.Password == null || req.Password.Length < 8)
            return BadRequest(new { message = "Password must be at least 8 characters." });

        var email = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u =>
                u.Email == email &&
                u.RefreshToken == $"INV:{req.Token}" &&
                u.RefreshTokenExpiresAt > DateTime.UtcNow);

        if (user == null)
            return BadRequest(new { message = "Invalid or expired invitation." });

        user.PasswordHash = HashPassword(req.Password);
        user.IsActive = true;

        // Issue a fresh access + refresh token.
        var refresh = Guid.NewGuid().ToString("N");
        user.RefreshToken = refresh;
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(30);
        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new AuthLoginResponse
        {
            AccessToken  = GenerateJwt(user),
            RefreshToken = refresh,
            ExpiresAt    = DateTime.UtcNow.AddHours(8),
            UserName     = user.DisplayName,
            Role         = user.Role.ToString(),
            Tier         = user.Tenant?.Tier.ToString() ?? "Starter",
            MimEnabled   = user.Tenant?.MimEnabled ?? false,
        });
    }

    // ── Tenant switcher (TENANT-SWITCH) ────────────────────────────────────────

    /// <summary>List all tenants the authenticated user's email is a member of.</summary>
    /// <remarks>
    /// Used by the mobile header badge + picker. An email can have multiple <see cref="AppUser"/>
    /// rows (one per tenant) — this endpoint surfaces all of them so the UI can let the
    /// consultant switch organisations without logging out.
    /// </remarks>
    [HttpGet("tenants")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> GetMemberships()
    {
        var subClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        var activeUserId = Guid.TryParse(subClaim, out var id) ? id : Guid.Empty;

        var active = await _db.Users.AsNoTracking()
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Id == activeUserId);
        if (active == null) return NotFound();

        // Same email across tenants — common when consultants are invited to
        // multiple client orgs.
        var memberships = await _db.Users.AsNoTracking()
            .Include(u => u.Tenant)
            .Where(u => u.Email == active.Email && u.IsActive)
            .OrderBy(u => u.Tenant!.Name)
            .Select(u => new
            {
                userId = u.Id,
                tenantId = u.TenantId,
                tenantName = u.Tenant!.Name,
                tenantSlug = u.Tenant.Slug,
                tenantTier = u.Tenant.Tier.ToString(),
                mimEnabled = u.Tenant.MimEnabled,
                role = u.Role.ToString(),
                isActiveTenant = u.Id == activeUserId,
            })
            .ToListAsync();

        return Ok(memberships);
    }

    /// <summary>Re-issue a JWT for a different tenant the user belongs to.</summary>
    /// <response code="200">New JWT + refresh token — apply in SecureStore under a per-tenant key.</response>
    /// <response code="403">User is not a member of the requested tenant.</response>
    [HttpPost("switch-tenant")]
    [Authorize]
    [ProducesResponseType(typeof(AuthLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthLoginResponse>> SwitchTenant([FromBody] SwitchTenantRequest req)
    {
        var subClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        var activeUserId = Guid.TryParse(subClaim, out var id) ? id : Guid.Empty;

        var active = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == activeUserId);
        if (active == null) return Unauthorized();

        var target = await _db.Users
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u =>
                u.Email == active.Email &&
                u.TenantId == req.TenantId &&
                u.IsActive);
        if (target == null)
        {
            // Do not reveal whether the tenant exists — return 403.
            return Forbid();
        }

        var token = GenerateJwt(target);
        var refreshToken = Guid.NewGuid().ToString("N");
        target.RefreshToken = refreshToken;
        target.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(30);
        target.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new AuthLoginResponse
        {
            AccessToken = token,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddHours(8),
            UserName = target.DisplayName,
            Role = target.Role.ToString(),
            Tier = target.Tenant?.Tier.ToString() ?? "Starter",
            MimEnabled = target.Tenant?.MimEnabled ?? false
        });
    }

    // ── Licence activation ─────────────────────────────────────────────────────

    /// <summary>Activate a licence key to unlock a tier (Professional / Premium / Enterprise).</summary>
    /// <response code="200">Activation result with tier, MIM flag, and server URL.</response>
    [HttpPost("license/activate")]
    [ProducesResponseType(typeof(LicenseActivationResponse), StatusCodes.Status200OK)]
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

    /// <summary>Request a password-reset token (sent via email). Always returns 200 to prevent email enumeration.</summary>
    /// <response code="200">Reset link sent (if email exists).</response>
    [EnableRateLimiting("auth")]
    [HttpPost("forgot-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
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

    /// <summary>Reset password using a token from the forgot-password email.</summary>
    /// <response code="200">Password reset — user can log in with new password.</response>
    /// <response code="400">Invalid/expired token or password too short.</response>
    [EnableRateLimiting("auth")]
    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
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
        // P1 — tag newly issued tokens with kid=current so future rotations can
        // tell our tokens apart. KeyId matches the SecurityKey registered in
        // Program.cs ("current" vs "previous").
        var jwtKey = _config["Jwt:Key"] ?? "Planscape-Dev-Secret-Key-Min32Chars!!";
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)) { KeyId = "current" };
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

public record SwitchTenantRequest(Guid TenantId);
public record AcceptInvitationRequest(string Token, string Email, string Password);
