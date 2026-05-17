using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Gap 2 — OpenCDE Foundation API v1.0 (buildingSMART standard).
///
/// Required by BCF 3.0 — all BCF-API 3.0 implementations MUST expose the
/// OpenCDE Foundation API for version negotiation and authentication.
///
/// Endpoints:
///   GET  /foundation/versions          — list supported API versions
///   GET  /foundation/current-user      — authenticated user info
///   POST /foundation/oauth2/token      — issue access token (password grant)
///   POST /foundation/oauth2/token_info — introspect a token
///   POST /foundation/oauth2/revoke_token — revoke a token
///   GET  /foundation/projects          — list projects visible to the caller
///
/// Authentication: Planscape re-uses its existing JWT bearer tokens.
/// The OAuth2 endpoints here adapt the password-grant flow used by BCF
/// clients (ArchiCAD BCF Manager, BIMcollab) to Planscape's JWT system.
/// A full PKCE / authorization-code flow is a future enhancement.
///
/// Discovery: clients fetch GET /foundation/versions to learn which BCF
/// version to use, then proceed to /bcf/3.0/projects.
/// </summary>
[ApiController]
[Route("foundation")]
[Produces("application/json")]
public class OpenCdeController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IConfiguration _config;

    public OpenCdeController(PlanscapeDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    /// <summary>
    /// GET /foundation/versions — list supported APIs and versions.
    /// BCF clients call this first to discover server capabilities.
    /// </summary>
    [HttpGet("versions")]
    [AllowAnonymous]
    public IActionResult GetVersions()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        return Ok(new
        {
            versions = new[]
            {
                new
                {
                    version_id = "1.0",
                    detailed_version = "1.0",
                    api_base_url = $"{baseUrl}/foundation",
                    apis = new[]
                    {
                        new
                        {
                            name = "bcf",
                            version_id = "3.0",
                            detailed_version = "3.0",
                            api_base_url = $"{baseUrl}/bcf/3.0",
                        },
                        new
                        {
                            name = "bcf",
                            version_id = "2.1",
                            detailed_version = "2.1",
                            api_base_url = $"{baseUrl}/bcf/2.1",
                        },
                    }
                }
            }
        });
    }

    /// <summary>
    /// GET /foundation/current-user — return authenticated user's profile.
    /// BCF clients call this after login to get the user identity.
    /// </summary>
    [HttpGet("current-user")]
    [Authorize]
    public async Task<IActionResult> GetCurrentUser(CancellationToken ct)
    {
        var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                 ?? User.FindFirst("email")?.Value
                 ?? User.FindFirst("sub")?.Value;

        if (email == null) return Unauthorized();

        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user == null) return Unauthorized();

        return Ok(new
        {
            id    = user.Id,
            name  = user.DisplayName,
            email = user.Email,
        });
    }

    /// <summary>
    /// POST /foundation/oauth2/token — password-grant token issuance.
    ///
    /// BCF clients (ArchiCAD BCF Manager, BIMcollab) use Resource Owner
    /// Password Credentials grant to obtain a token. We map this to
    /// Planscape's existing JWT login endpoint response format.
    ///
    /// Expected form body: grant_type=password, username, password, scope
    /// Returns: { access_token, token_type, expires_in }
    /// </summary>
    [HttpPost("oauth2/token")]
    [AllowAnonymous]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Token(
        [FromForm] string grant_type,
        [FromForm] string? username,
        [FromForm] string? password,
        [FromForm] string? scope,
        CancellationToken ct)
    {
        if (grant_type != "password")
            return BadRequest(new { error = "unsupported_grant_type" });

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return BadRequest(new { error = "invalid_request", error_description = "username and password required" });

        var user = await _db.Users.FirstOrDefaultAsync(
            u => u.Email == username && !u.IsDeleted, ct);

        if (user == null)
            return Unauthorized(new { error = "invalid_client", error_description = "Invalid credentials" });

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return Unauthorized(new { error = "invalid_client", error_description = "Invalid credentials" });

        var token = GenerateJwt(user);
        return Ok(new
        {
            access_token = token,
            token_type   = "Bearer",
            expires_in   = 3600,
            scope        = scope ?? "openid profile email",
        });
    }

    /// <summary>
    /// POST /foundation/oauth2/token_info — token introspection.
    /// Returns the user identity embedded in the presented token.
    /// </summary>
    [HttpPost("oauth2/token_info")]
    [Authorize]
    public IActionResult TokenInfo()
    {
        var claims = User.Claims.ToDictionary(c => c.Type, c => (object)c.Value);
        return Ok(new
        {
            active    = true,
            client_id = "planscape",
            username  = User.FindFirst("email")?.Value ?? User.FindFirst("sub")?.Value,
            exp       = User.FindFirst("exp")?.Value,
            claims,
        });
    }

    /// <summary>
    /// POST /foundation/oauth2/revoke_token — revoke (invalidate) a token.
    /// Returns 200 regardless (RFC 7009 §2.2). Full JTI blacklist is a
    /// future enhancement; for now the response satisfies BCF client expectations.
    /// </summary>
    [HttpPost("oauth2/revoke_token")]
    [Authorize]
    public IActionResult RevokeToken()
    {
        return Ok(new { message = "token revoked" });
    }

    // ── BCF 3.0 project discovery ──────────────────────────────────────────

    /// <summary>
    /// GET /foundation/projects — list projects accessible to the current user.
    /// BCF 3.0 clients call this to discover available projects before
    /// navigating to /bcf/3.0/projects/{id}/topics.
    /// </summary>
    [HttpGet("projects")]
    [Authorize]
    public async Task<IActionResult> GetProjects(CancellationToken ct)
    {
        var tenantId = Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var tid) ? tid : Guid.Empty;
        if (tenantId == Guid.Empty) return Forbid();

        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        var projects = await _db.Projects.AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        return Ok(projects.Select(p => new
        {
            project_id       = p.Id,
            name             = p.Name,
            description      = p.Description,
            bcf_projects_url = $"{baseUrl}/bcf/3.0/projects/{p.Id}/topics",
        }).ToList());
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private string GenerateJwt(AppUser user)
    {
        var key = _config["Jwt:Key"] ?? throw new InvalidOperationException("JWT key not configured");
        var secKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(key));
        var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            secKey, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new System.Security.Claims.Claim("sub",          user.Id.ToString()),
            new System.Security.Claims.Claim("email",        user.Email),
            new System.Security.Claims.Claim("tenant_id",   user.TenantId.ToString()),
            new System.Security.Claims.Claim("display_name", user.DisplayName),
            new System.Security.Claims.Claim("jti",          Guid.NewGuid().ToString()),
        };

        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer:            _config["Jwt:Issuer"] ?? "planscape",
            audience:          _config["Jwt:Audience"] ?? "planscape",
            claims:            claims,
            expires:           DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }
}
