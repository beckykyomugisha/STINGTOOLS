using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

    public MfaController(PlanscapeDbContext db, ILogger<MfaController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private Guid GetTenantId() =>
        Guid.Parse(User.FindFirst("tenantId")?.Value
            ?? throw new InvalidOperationException("tenantId claim missing"));

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

        // Production: generate TOTP secret, return QR URI, don't persist until confirmed
        // For scaffold: create enrollment in "pending" state
        var secret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(20));
        var enrollment = new MfaEnrollment
        {
            TenantId        = tenantId,
            UserId          = userId,
            Method          = "TOTP",
            SecretEncrypted = secret, // TODO: encrypt with IDataProtectionProvider
            DeviceLabel     = req.DeviceLabel,
            RecoveryCodesJson = "[]",
        };
        _db.MfaEnrollments.Add(enrollment);
        await _db.SaveChangesAsync();

        // In production: derive otpauth:// URI from secret for QR display
        return Ok(new
        {
            enrollment.Id,
            enrollment.DeviceLabel,
            // Secret returned only during enrollment, never again
            Secret    = "base32-secret-stub",
            OtpAuthUri = $"otpauth://totp/Planscape:{User.Identity!.Name}?secret=STUB&issuer=Planscape",
            Message   = "Verify with a TOTP code to activate enrollment."
        });
    }

    [HttpPost("enrollments/{enrollmentId}/verify")]
    public async Task<ActionResult> VerifyEnrollment(Guid enrollmentId, [FromBody] VerifyTotpRequest req)
    {
        var userId   = GetUserId();
        var tenantId = GetTenantId();
        var enrollment = await _db.MfaEnrollments
            .FirstOrDefaultAsync(e => e.Id == enrollmentId && e.UserId == userId && e.TenantId == tenantId);
        if (enrollment is null) return NotFound();

        // Production: validate req.Code against enrollment.SecretEncrypted using Otp.NET
        var isValid = req.Code?.Length == 6; // stub
        var challenge = new MfaChallenge
        {
            TenantId      = tenantId,
            UserId        = userId,
            EnrollmentId  = enrollmentId,
            Method        = "TOTP",
            Succeeded     = isValid,
            ClientIp      = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent     = Request.Headers.UserAgent.ToString(),
            FailureReason = isValid ? null : "InvalidCode",
        };
        _db.MfaChallenges.Add(challenge);

        if (isValid)
        {
            enrollment.LastVerifiedAt = DateTime.UtcNow;
            // Generate recovery codes
            var codes = Enumerable.Range(0, 10)
                .Select(_ => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant())
                .ToList();
            enrollment.RecoveryCodesJson = System.Text.Json.JsonSerializer.Serialize(
                codes.Select(c => new { Code = c, Used = false }));
        }

        await _db.SaveChangesAsync();

        if (!isValid) return BadRequest("Invalid TOTP code.");
        return Ok(new { message = "MFA enrolled and verified." });
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
        enrollment.RecoveryCodesJson = System.Text.Json.JsonSerializer.Serialize(
            codes.Select(c => new { Code = c, Used = false }));
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
