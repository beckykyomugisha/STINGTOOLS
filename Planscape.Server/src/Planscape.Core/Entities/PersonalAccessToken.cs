namespace Planscape.Core.Entities;

/// <summary>
/// A long-lived credential a user can mint for a headless client
/// (StingBridge, CI, scripts) that cannot complete an interactive login.
///
/// WHY THIS EXISTS: accounts provisioned through the planscape.build identity
/// handoff (<c>POST /api/auth/handoff/exchange</c>) are deliberately created with an
/// unusable random password hash — they can only be entered via a fresh
/// 120-second ticket. That is correct for a browser, but it left headless
/// clients with no credential at all: StingBridge authenticates with
/// email + password, and such a user has no password to give it.
///
/// SCOPE: a PAT is NOT a session and is NOT accepted as a bearer token by the
/// API. It is exchanged at <c>POST /api/auth/token/exchange</c> for an ordinary
/// short-lived JWT, exactly as a password would be at <c>/api/auth/login</c>.
/// Keeping the API JWT-only means no endpoint's authorisation behaviour
/// changes and there is no second auth scheme to keep in sync.
///
/// STORAGE: only the SHA-256 of the token is persisted. The plaintext is
/// returned once, at mint time, and is unrecoverable afterwards. SHA-256
/// (not BCrypt) is appropriate here for the same reason it is used for refresh
/// tokens in this codebase: the secret is a full-entropy 256-bit random value,
/// so there is no dictionary or rainbow table to defend against, and the
/// verification path must stay cheap enough to run on every exchange.
/// </summary>
public class PersonalAccessToken : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>The user this token acts as. Exchanged sessions carry this user's role.</summary>
    public Guid UserId { get; set; }

    /// <summary>Human label so a user can tell two tokens apart when revoking.</summary>
    public string Name { get; set; } = "";

    /// <summary>SHA-256 (hex) of the plaintext token. The plaintext is never stored.</summary>
    public string TokenHash { get; set; } = "";

    /// <summary>
    /// First few characters of the plaintext (e.g. <c>psat_a1b2c3</c>), stored so the
    /// UI can show which token a row refers to. Not a secret and not sufficient
    /// to authenticate — it is a display aid only.
    /// </summary>
    public string TokenPrefix { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Stamped on each successful exchange so unused tokens can be found and pruned.</summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>Null means "never expires". Set by the caller at mint time.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Set when revoked. Revocation is a soft delete so the audit trail survives.</summary>
    public DateTime? RevokedAt { get; set; }

    public Tenant? Tenant { get; set; }
    public AppUser? User { get; set; }

    /// <summary>True when this token may still be exchanged for a session.</summary>
    public bool IsUsable(DateTime nowUtc) =>
        RevokedAt == null && (ExpiresAt == null || ExpiresAt > nowUtc);
}
