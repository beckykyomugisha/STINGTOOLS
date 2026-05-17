using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Per-tenant SSO configuration management (OIDC + SAML2).
/// All secrets arrive over the wire as plaintext and should be encrypted
/// by the server before persisting — production implementation would call
/// IDataProtectionProvider. For this scaffold the field names use the
/// *Encrypted suffix to signal that intent.
/// </summary>
[ApiController]
[Route("api/tenant/sso")]
[Authorize(Roles = "TenantAdmin,SuperAdmin")]
public class SsoController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ILogger<SsoController> _logger;
    private readonly IDataProtector _protector;

    public SsoController(PlanscapeDbContext db, ILogger<SsoController> logger,
        IDataProtectionProvider dpProvider)
    {
        _db = db;
        _logger = logger;
        _protector = dpProvider.CreateProtector("sso-secrets");
    }

    private string? Protect(string? value) =>
        value is null ? null : _protector.Protect(value);

    private string? Unprotect(string? cipher) =>
        cipher is null ? null : _protector.Unprotect(cipher);

    private Guid GetTenantId() =>
        Guid.Parse(User.FindFirst("tenantId")?.Value
            ?? throw new InvalidOperationException("tenantId claim missing"));

    [HttpGet]
    public async Task<ActionResult> GetConfigs()
    {
        var tenantId = GetTenantId();
        var configs = await _db.SsoConfigs
            .Where(c => c.TenantId == tenantId)
            .Select(c => new
            {
                c.Id, c.Name, c.Protocol, c.Enabled, c.EmailDomains, c.RequireSso,
                c.OidcIssuer, c.OidcClientId, c.OidcAuthorizationEndpoint,
                c.OidcTokenEndpoint, c.OidcUserInfoEndpoint, c.OidcJwksUri, c.OidcScopes,
                c.SamlEntityId, c.SamlSsoUrl, c.SamlSloUrl,
                c.AttributeMapJson, c.GroupRoleMapJson,
                c.ScimEndpoint,
                c.CreatedAt, c.UpdatedAt, c.CreatedBy,
                c.LastSuccessfulLoginAt, c.LastFailedLoginAt, c.LastFailureReason
                // Encrypted fields deliberately omitted from GET response
            })
            .ToListAsync();
        return Ok(configs);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult> GetConfig(Guid id)
    {
        var tenantId = GetTenantId();
        var config = await _db.SsoConfigs
            .Where(c => c.Id == id && c.TenantId == tenantId)
            .Select(c => new
            {
                c.Id, c.Name, c.Protocol, c.Enabled, c.EmailDomains, c.RequireSso,
                c.OidcIssuer, c.OidcClientId,
                HasOidcSecret = c.OidcClientSecretEncrypted != null,
                c.OidcAuthorizationEndpoint, c.OidcTokenEndpoint,
                c.OidcUserInfoEndpoint, c.OidcJwksUri, c.OidcScopes,
                c.SamlEntityId, c.SamlSsoUrl, c.SamlSloUrl,
                HasSamlIdpCert   = c.SamlIdpCertificate != null,
                HasSamlSpCert    = c.SamlSpCertificateEncrypted != null,
                c.AttributeMapJson, c.GroupRoleMapJson,
                c.ScimEndpoint,
                HasScimToken  = c.ScimBearerTokenEncrypted != null,
                c.CreatedAt, c.UpdatedAt, c.CreatedBy,
                c.LastSuccessfulLoginAt, c.LastFailedLoginAt, c.LastFailureReason
            })
            .FirstOrDefaultAsync();
        return config is null ? NotFound() : Ok(config);
    }

    [HttpPost]
    public async Task<ActionResult> CreateConfig([FromBody] UpsertSsoConfigRequest req)
    {
        var tenantId = GetTenantId();

        // Must have at minimum either OIDC issuer or SAML entity ID; empty configs are rejected.
        var protocol = req.Protocol?.ToUpperInvariant() ?? "OIDC";
        if (protocol == "OIDC" && string.IsNullOrEmpty(req.OidcIssuer))
            return BadRequest("OIDC configuration requires OidcIssuer.");
        if (protocol == "SAML" && string.IsNullOrEmpty(req.SamlEntityId))
            return BadRequest("SAML configuration requires SamlEntityId.");

        var config = new SsoConfig
        {
            TenantId                       = tenantId,
            Name                           = req.Name,
            Protocol                       = protocol,
            Enabled                        = req.Enabled ?? true,
            EmailDomains                   = req.EmailDomains,
            RequireSso                     = req.RequireSso ?? false,
            OidcIssuer                     = req.OidcIssuer,
            OidcClientId                   = req.OidcClientId,
            OidcClientSecretEncrypted      = Protect(req.OidcClientSecret),
            OidcAuthorizationEndpoint      = req.OidcAuthorizationEndpoint,
            OidcTokenEndpoint              = req.OidcTokenEndpoint,
            OidcUserInfoEndpoint           = req.OidcUserInfoEndpoint,
            OidcJwksUri                    = req.OidcJwksUri,
            OidcScopes                     = req.OidcScopes ?? "openid profile email",
            SamlEntityId                   = req.SamlEntityId,
            SamlSsoUrl                     = req.SamlSsoUrl,
            SamlSloUrl                     = req.SamlSloUrl,
            SamlIdpCertificate             = req.SamlIdpCertificate,
            SamlSpCertificateEncrypted     = Protect(req.SamlSpCertificate),
            AttributeMapJson               = req.AttributeMapJson,
            GroupRoleMapJson               = req.GroupRoleMapJson,
            ScimEndpoint                   = req.ScimEndpoint,
            ScimBearerTokenEncrypted       = Protect(req.ScimBearerToken),
            CreatedBy                      = User.Identity?.Name,
        };
        _db.SsoConfigs.Add(config);
        await _db.SaveChangesAsync();
        _logger.LogInformation("SSO config created for tenant {TenantId}: {Name}", tenantId, config.Name);
        return Ok(new { config.Id, config.Name, config.Protocol });
    }

    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateConfig(Guid id, [FromBody] UpsertSsoConfigRequest req)
    {
        var tenantId = GetTenantId();
        var config = await _db.SsoConfigs.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId);
        if (config is null) return NotFound();

        config.Name         = req.Name;
        config.Enabled      = req.Enabled ?? config.Enabled;
        config.EmailDomains = req.EmailDomains ?? config.EmailDomains;
        config.RequireSso   = req.RequireSso ?? config.RequireSso;

        if (req.OidcClientSecret is not null)
            config.OidcClientSecretEncrypted = Protect(req.OidcClientSecret);

        config.OidcIssuer                = req.OidcIssuer ?? config.OidcIssuer;
        config.OidcClientId              = req.OidcClientId ?? config.OidcClientId;
        config.OidcAuthorizationEndpoint = req.OidcAuthorizationEndpoint ?? config.OidcAuthorizationEndpoint;
        config.OidcTokenEndpoint         = req.OidcTokenEndpoint ?? config.OidcTokenEndpoint;
        config.OidcUserInfoEndpoint      = req.OidcUserInfoEndpoint ?? config.OidcUserInfoEndpoint;
        config.OidcJwksUri               = req.OidcJwksUri ?? config.OidcJwksUri;
        config.OidcScopes                = req.OidcScopes ?? config.OidcScopes;

        config.SamlEntityId = req.SamlEntityId ?? config.SamlEntityId;
        config.SamlSsoUrl   = req.SamlSsoUrl   ?? config.SamlSsoUrl;
        config.SamlSloUrl   = req.SamlSloUrl   ?? config.SamlSloUrl;
        if (req.SamlIdpCertificate is not null) config.SamlIdpCertificate = req.SamlIdpCertificate;
        if (req.SamlSpCertificate  is not null) config.SamlSpCertificateEncrypted = Protect(req.SamlSpCertificate);

        config.AttributeMapJson = req.AttributeMapJson ?? config.AttributeMapJson;
        config.GroupRoleMapJson = req.GroupRoleMapJson ?? config.GroupRoleMapJson;
        config.ScimEndpoint     = req.ScimEndpoint     ?? config.ScimEndpoint;
        if (req.ScimBearerToken is not null) config.ScimBearerTokenEncrypted = Protect(req.ScimBearerToken);

        config.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { config.Id, config.Name, config.UpdatedAt });
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteConfig(Guid id)
    {
        var tenantId = GetTenantId();
        var config = await _db.SsoConfigs.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId);
        if (config is null) return NotFound();
        _db.SsoConfigs.Remove(config);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id}/test")]
    public async Task<ActionResult> TestConfig(Guid id)
    {
        var tenantId = GetTenantId();
        var config = await _db.SsoConfigs.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId);
        if (config is null) return NotFound();

        // Production: attempt OIDC discovery / SAML metadata fetch and return result
        return Ok(new { status = "Stub", message = "Test not yet wired — register OIDC provider first." });
    }
}

public record UpsertSsoConfigRequest(
    string Name,
    string? Protocol,
    bool? Enabled,
    string? EmailDomains,
    bool? RequireSso,
    string? OidcIssuer,
    string? OidcClientId,
    string? OidcClientSecret,
    string? OidcAuthorizationEndpoint,
    string? OidcTokenEndpoint,
    string? OidcUserInfoEndpoint,
    string? OidcJwksUri,
    string? OidcScopes,
    string? SamlEntityId,
    string? SamlSsoUrl,
    string? SamlSloUrl,
    string? SamlIdpCertificate,
    string? SamlSpCertificate,
    string? AttributeMapJson,
    string? GroupRoleMapJson,
    string? ScimEndpoint,
    string? ScimBearerToken);
