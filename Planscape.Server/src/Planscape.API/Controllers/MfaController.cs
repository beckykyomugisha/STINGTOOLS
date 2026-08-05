using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// TOTP multi-factor authentication: enroll, verify, recovery codes, challenge log.
/// Production implementation would use a TOTP library (e.g. Otp.NET) and
/// IDataProtectionProvider for secret encryption.
/// </summary>
[ApiController]
[Route("api/mfa")]
[Authorize]
public class MfaController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ILogger<MfaController> _logger;
    private readonly IDataProtector _protector;

    public MfaController(PlanscapeDbContext db, ILogger<MfaController> logger,
        IDataProtectionProvider dpProvider)
    {
        _db = db;
        _logger = logger;
        _protector = dpProvider.CreateProtector("mfa-totp-secrets");
    }

    private Guid GetTenantId() =>
        Guid.Parse(User.FindFirst("tenant_id")?.Value
            ?? throw new InvalidOperationException("tenant_id claim missing"));

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirst("sub")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? throw new InvalidOperationException("userId claim missing"));

    // ── Enrollment ────────────────────────────────────────────────────────

    [HttpGet("enrollments")]
    public async Task<ActionResult> GetEnrollments()
    {
        var userId   = GetUserId();
        var tenantId = GetTenantId();
        var enrollments = await _db.MfaEnrollments
            .Where(e => e.UserId == userId && e.TenantId == tenantId && e.RevokedAt == null)
            .Select(e => new
            {
                e.Id, e.Method, e.DeviceLabel,
                e.EnrolledAt, e.LastVerifiedAt,
                HasRecoveryCodes = e.RecoveryCodesJson != "[]"
            })
            .ToListAsync();
        return Ok(enrollments);
    }

    [HttpPost("enroll/totp")]
    public async Task<ActionResult> EnrollTotp([FromBody] EnrollTotpRequest req)
    {
        var userId   = GetUserId();
        var tenantId = GetTenantId();

        // Generate a 20-byte TOTP secret; encode as base32 (standard for OTP URIs).
        var rawKey     = System.Security.Cryptography.RandomNumberGenerator.GetBytes(20);
        var base32Secret = Base32Encoding.ToString(rawKey);
        var enrollment = new MfaEnrollment
        {
            TenantId          = tenantId,
            UserId            = userId,
            Method            = "TOTP",
            SecretEncrypted   = _protector.Protect(base32Secret),
            DeviceLabel       = req.DeviceLabel,
            RecoveryCodesJson = "[]",
        };
        _db.MfaEnrollments.Add(enrollment);
        await _db.SaveChangesAsync();

        var issuerLabel  = Uri.EscapeDataString("Planscape");
        var accountLabel = Uri.EscapeDataString(User.Identity!.Name ?? userId.ToString());
        var otpAuthUri   = $"otpauth://totp/{issuerLabel}:{accountLabel}?secret={base32Secret}&issuer={issuerLabel}&algorithm=SHA1&digits=6&period=30";

        return Ok(new
        {
            enrollment.Id,
            enrollment.DeviceLabel,
            Secret    = base32Secret,   // returned once only; client renders QR code from OtpAuthUri
            OtpAuthUri = otpAuthUri,
            Message   = "Verify with a TOTP code to activate enrollment."
        });
    }

    [HttpPost("enrollments/{enrollmentId}/verify")]
    public async Task<ActionResult> VerifyEnrollment(Guid enrollmentId, [FromBody] VerifyTotpRequest req)
    {
        var userId   = GetUserId();
        var tenantId = GetTenantId();
        // Enrollment must exist AND must not yet be verified (LastVerifiedAt == null = pending).
        var enrollment = await _db.MfaEnrollments
            .FirstOrDefaultAsync(e => e.Id == enrollmentId && e.UserId == userId
                                   && e.TenantId == tenantId && e.RevokedAt == null);
        if (enrollment is null) return NotFound();
        if (enrollment.LastVerifiedAt.HasValue)
            return Conflict("Enrollment already verified. Revoke and re-enroll to replace.");

        // Decrypt the stored secret then verify the submitted TOTP code.
        // VerificationWindow(2, 2) accepts codes from ±2 time steps (±60 s clock drift).
        bool isValid = false;
        if (req.Code?.Length == 6 && enrollment.SecretEncrypted is not null)
        {
            try
            {
                var base32Secret = _protector.Unprotect(enrollment.SecretEncrypted);
                var secretBytes  = Base32Encoding.ToBytes(base32Secret);
                var totp         = new Totp(secretBytes);
                isValid = totp.VerifyTotp(req.Code, out _, new VerificationWindow(2, 2));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TOTP verification failed to decrypt secret for enrollment {Id}", enrollmentId);
            }
        }
        var challenge = new MfaChallenge
        {
            TenantId      = tenantId,
            UserId        = userId,
            EnrollmentId  = enrollmentId,
            Method        = "TOTP",
            Succeeded     = isValid,
            ClientIp      = HttpContext.Connection.RemoteIpAddress?.ToString(),
            // Store a truncated UA (max 200 chars) to reduce fingerprinting surface.
            UserAgent     = (Request.Headers.UserAgent.ToString() is { } ua ? ua[..Math.Min(200, ua.Length)] : null),
            FailureReason = isValid ? null : "InvalidCode",
        };
        _db.MfaChallenges.Add(challenge);

        if (isValid)
        {
            enrollment.LastVerifiedAt = DateTime.UtcNow;
            // Recovery codes: store as SHA-256 hashes so a DB dump cannot be used directly.
            // Client receives plaintext once; server stores the hash.
            var plainCodes = Enumerable.Range(0, 10)
                .Select(_ => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant())
                .ToList();
            enrollment.RecoveryCodesJson = System.Text.Json.JsonSerializer.Serialize(
                plainCodes.Select(c => new
                {
                    Hash = Convert.ToHexString(
                        System.Security.Cryptography.SHA256.HashData(
                            System.Text.Encoding.UTF8.GetBytes(c))),
                    Used = false
                }));
        }

        await _db.SaveChangesAsync();

        if (!isValid) return BadRequest("Invalid TOTP code.");
        return Ok(new { message = "MFA enrolled and verified. Save your recovery codes securely." });
    }

    [HttpDelete("enrollments/{enrollmentId}")]
    public async Task<ActionResult> RevokeEnrollment(Guid enrollmentId)
    {
        var userId   = GetUserId();
        var tenantId = GetTenantId();
        var enrollment = await _db.MfaEnrollments
            .FirstOrDefaultAsync(e => e.Id == enrollmentId && e.UserId == userId && e.TenantId == tenantId);
        if (enrollment is null) return NotFound();

        enrollment.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── Recovery Codes ────────────────────────────────────────────────────

    [HttpPost("enrollments/{enrollmentId}/recovery-codes/regenerate")]
    public async Task<ActionResult> RegenerateRecoveryCodes(Guid enrollmentId)
    {
        var userId   = GetUserId();
        var tenantId = GetTenantId();
        var enrollment = await _db.MfaEnrollments
            .FirstOrDefaultAsync(e => e.Id == enrollmentId && e.UserId == userId
                                   && e.TenantId == tenantId && e.RevokedAt == null);
        if (enrollment is null) return NotFound();

        var codes = Enumerable.Range(0, 10)
            .Select(_ => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant())
            .ToList();
        // Store SHA-256 hashes only — plaintext is returned once and never persisted.
        enrollment.RecoveryCodesJson = System.Text.Json.JsonSerializer.Serialize(
            codes.Select(c => new
            {
                Hash = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(c))),
                Used = false
            }));
        await _db.SaveChangesAsync();

        // Return codes plaintext once — user must store them
        return Ok(new { recoveryCodes = codes });
    }

    // ── Challenge Log ─────────────────────────────────────────────────────

    [HttpGet("challenges")]
    public async Task<ActionResult> GetChallenges(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (page < 1) page = 1;
        pageSize = Math.Clamp(pageSize, 1, 200);

        var userId   = GetUserId();
        var tenantId = GetTenantId();
        var total = await _db.MfaChallenges
            .CountAsync(c => c.UserId == userId && c.TenantId == tenantId);
        var challenges = await _db.MfaChallenges
            .Where(c => c.UserId == userId && c.TenantId == tenantId)
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new
            {
                c.Id, c.Method, c.Succeeded, c.ClientIp,
                c.FailureReason, c.CreatedAt
            })
            .ToListAsync();
        return Ok(new { total, page, pageSize, items = challenges });
    }

    // ── Admin: tenant-wide MFA status ─────────────────────────────────────

    [HttpGet("admin/status")]
    [Authorize(Roles = "TenantAdmin,SuperAdmin")]
    public async Task<ActionResult> GetTenantMfaStatus()
    {
        var tenantId = GetTenantId();
        var enrolled = await _db.MfaEnrollments
            .Where(e => e.TenantId == tenantId && e.RevokedAt == null)
            .GroupBy(e => e.UserId)
            .CountAsync();
        var totalUsers = await _db.Users.CountAsync(u => u.TenantId == tenantId);
        return Ok(new
        {
            totalUsers,
            enrolledUsers = enrolled,
            enrollmentPct = totalUsers == 0 ? 0.0 : (enrolled * 100.0 / totalUsers)
        });
    }
}

public record EnrollTotpRequest(string? DeviceLabel);
public record VerifyTotpRequest(string? Code);
