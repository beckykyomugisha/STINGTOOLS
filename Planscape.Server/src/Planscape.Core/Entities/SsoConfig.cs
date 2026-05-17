namespace Planscape.Core.Entities;

/// <summary>
/// Per-tenant identity provider configuration for SSO. One row per
/// configured IdP — most tenants have one, enterprise customers
/// occasionally have two (e.g. one for staff, one for external
/// consultants).
///
/// Supports OIDC (Microsoft Entra, Google Workspace, Okta) and SAML 2.0
/// (legacy ADFS, Ping, OneLogin). SCIM provisioning is optional and
/// configured separately via <see cref="ScimEndpoint"/>.
///
/// Secrets (client secrets, signing keys) are stored encrypted at rest
/// using the server's data-protection keyring — the columns here hold
/// the encrypted ciphertext, never plaintext.
/// </summary>
public class SsoConfig : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Display name shown on the login chooser ("Planscape Corp SSO").</summary>
    public string Name { get; set; } = "";

    /// <summary>"OIDC" or "SAML2".</summary>
    public string Protocol { get; set; } = "OIDC";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Auto-redirect users whose email matches one of these domains
    /// directly to this IdP (comma-separated list). When set, the
    /// password fallback is hidden for those domains.
    /// </summary>
    public string? EmailDomains { get; set; }

    /// <summary>If true, password auth is disabled for this tenant entirely.</summary>
    public bool RequireSso { get; set; } = false;

    // ── OIDC fields ──

    public string? OidcIssuer { get; set; }
    public string? OidcClientId { get; set; }
    public string? OidcClientSecretEncrypted { get; set; }
    public string? OidcAuthorizationEndpoint { get; set; }
    public string? OidcTokenEndpoint { get; set; }
    public string? OidcUserInfoEndpoint { get; set; }
    public string? OidcJwksUri { get; set; }
    public string OidcScopes { get; set; } = "openid profile email";

    // ── SAML fields ──

    public string? SamlEntityId { get; set; }
    public string? SamlSsoUrl { get; set; }
    public string? SamlSloUrl { get; set; }
    public string? SamlIdpCertificate { get; set; }
    public string? SamlSpCertificateEncrypted { get; set; }

    /// <summary>
    /// JSON map of {claimName → user property} for attribute mapping.
    /// Defaults: email→email, given_name→firstName, family_name→lastName,
    /// groups→roles.
    /// </summary>
    public string? AttributeMapJson { get; set; }

    /// <summary>
    /// JSON map of {idp_group → planscape_role}. When an SSO assertion
    /// arrives, the user's groups are translated to roles via this map.
    /// </summary>
    public string? GroupRoleMapJson { get; set; }

    // ── SCIM (System for Cross-domain Identity Management) ──

    /// <summary>SCIM 2.0 endpoint that Planscape exposes for this tenant.</summary>
    public string? ScimEndpoint { get; set; }

    /// <summary>SCIM bearer token issued to the IdP for provisioning.</summary>
    public string? ScimBearerTokenEncrypted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }

    public DateTime? LastSuccessfulLoginAt { get; set; }
    public DateTime? LastFailedLoginAt { get; set; }
    public string? LastFailureReason { get; set; }
}

/// <summary>
/// Per-user multi-factor enrollment. Currently TOTP only (Google
/// Authenticator, Authy, 1Password). WebAuthn / passkey support is a
/// straightforward extension when needed.
///
/// Recovery codes are one-time-use 8-character strings hashed at
/// rest — same pattern as refresh tokens. Each successful TOTP /
/// recovery code use is logged via <see cref="MfaChallenge"/>.
/// </summary>
public class MfaEnrollment : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>"TOTP" / "WebAuthn" (WebAuthn deferred to v2).</summary>
    public string Method { get; set; } = "TOTP";

    /// <summary>TOTP secret encrypted with the data-protection keyring.</summary>
    public string SecretEncrypted { get; set; } = "";

    /// <summary>JSON array of 10 SHA-256 hashed recovery codes, used flag.</summary>
    public string RecoveryCodesJson { get; set; } = "[]";

    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastVerifiedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    /// <summary>Friendly name for the enrolled device ("iPhone 15", "Yubikey 5").</summary>
    public string? DeviceLabel { get; set; }

    public AppUser? User { get; set; }
}

/// <summary>
/// One row per MFA challenge attempt (success or failure). Feeds the
/// audit log and rate-limiter (too many failures locks the account).
/// </summary>
public class MfaChallenge : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid? EnrollmentId { get; set; }

    /// <summary>"TOTP" / "RecoveryCode" / "Backup".</summary>
    public string Method { get; set; } = "TOTP";

    public bool Succeeded { get; set; }

    public string? ClientIp { get; set; }
    public string? UserAgent { get; set; }
    public string? FailureReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
