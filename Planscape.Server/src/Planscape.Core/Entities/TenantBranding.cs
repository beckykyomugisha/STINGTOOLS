namespace Planscape.Core.Entities;

/// <summary>
/// FLEX-03 — Per-tenant branding that customises emails, mobile theme, and web dashboard
/// chrome without a code change. One row per tenant; falls back to Tenant:DefaultBranding
/// config when no row exists.
///
/// Values that are null or empty are resolved from config at render time, so a tenant
/// can override just the accent color and inherit the rest.
/// </summary>
public class TenantBranding : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    /// <summary>Display name shown in email subjects and mobile header. Null = inherit.</summary>
    public string? ProductName { get; set; }

    /// <summary>Hex color with leading # — accent/CTA. Default: #E8912D (Planscape orange).</summary>
    public string? AccentColor { get; set; }

    /// <summary>Hex color — email header banner and mobile status bar. Default: #1A237E.</summary>
    public string? HeaderColor { get; set; }

    /// <summary>Public HTTPS URL to a 400×120 PNG/SVG logo. Null = product-name text only.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>Per-tenant support address (footer contact). Null = inherit.</summary>
    public string? SupportEmail { get; set; }

    /// <summary>Override SMTP FROM name for outbound mail. Null = inherit.</summary>
    public string? EmailFromName { get; set; }

    /// <summary>Override SMTP FROM address. Rarely set — requires DKIM alignment on the chosen domain.</summary>
    public string? EmailFromAddress { get; set; }

    /// <summary>Plain-text signature appended to outbound emails. Null = default signature.</summary>
    public string? EmailSignature { get; set; }

    /// <summary>Two-letter ISO language code. Used when the user has no locale preference.</summary>
    public string? DefaultLanguage { get; set; } = "en";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedByUserId { get; set; }
}
