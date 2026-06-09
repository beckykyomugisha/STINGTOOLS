using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Planscape.Core.Interfaces;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Shared base for every email provider. Owns the ONE message-composition path
/// — template rendering (HTML + plain-text), branding resolution and from-address
/// resolution — so SMTP and Resend (and any future provider) can't drift in what
/// the recipient sees. Subclasses implement ONLY the transport
/// (<see cref="SendTransportAsync"/>) and <see cref="IsConfigured"/>.
///
/// The HTML body is produced by <see cref="IEmailTemplateRenderer"/> (the
/// RawHtmlKeys/<c>{{Body}}</c> renderer) — identical bytes regardless of provider.
/// </summary>
public abstract class EmailServiceBase : IEmailService
{
    protected readonly IConfiguration _config;
    protected readonly ILogger _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    protected EmailServiceBase(IConfiguration config, ILogger logger, IServiceScopeFactory scopeFactory)
    {
        _config = config;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    // Config keys use either "Smtp:*" (matches the Production template) or "Email:*".
    // Both providers read the from-identity from the SAME keys so flipping
    // Email:Provider doesn't silently change the sender.
    protected string Cfg(string key, string fallback = "") =>
        _config[$"Smtp:{key}"] ?? _config[$"Email:{key}"] ?? fallback;

    protected string FromAddress => Cfg("FromAddress", "noreply@planscape.io");
    protected string FromName    => Cfg("FromName", "Planscape");

    /// <summary>True when this provider has the credentials it needs to send.</summary>
    public abstract bool IsConfigured { get; }

    /// <summary>The ONLY provider-specific step — push the already-composed
    /// <see cref="RenderedEmail"/> over the wire. Throw on a hard failure so the
    /// caller (controller / endpoint) can surface a clear error.</summary>
    protected abstract Task SendTransportAsync(string toEmail, RenderedEmail email, CancellationToken ct);

    // ── IEmailService surface (shared composition, then provider transport) ─────

    public async Task SendInviteEmailAsync(
        string toEmail, string displayName, string inviterName,
        string projectName, string serverUrl, string? resetToken = null, Guid projectId = default, CancellationToken ct = default)
    {
        var acceptUrl = InviteLink.BuildAcceptUrl(serverUrl, toEmail, resetToken, projectId);
        var model = new Dictionary<string, string?>
        {
            ["DisplayName"] = displayName,
            ["InviterName"] = inviterName,
            ["ProjectName"] = projectName,
            ["AcceptUrl"]   = acceptUrl,
            ["ServerUrl"]   = serverUrl,
        };
        var rendered = await RenderAsync("invite", model, ct);
        await SendTransportAsync(toEmail, rendered, ct);
    }

    public async Task SendPasswordResetEmailAsync(
        string toEmail, string resetToken, string serverUrl, CancellationToken ct = default)
    {
        var resetUrl = $"{serverUrl.TrimEnd('/')}/reset-password.html?token={Uri.EscapeDataString(resetToken)}&email={Uri.EscapeDataString(toEmail)}";
        var model = new Dictionary<string, string?>
        {
            ["ResetUrl"]  = resetUrl,
            ["ServerUrl"] = serverUrl,
        };
        var rendered = await RenderAsync("password-reset", model, ct);
        await SendTransportAsync(toEmail, rendered, ct);
    }

    public async Task SendNotificationAsync(
        string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        // Ad-hoc notification — wrap the caller's body in the branded layout but skip
        // template lookup (the caller has already composed the message body).
        var branding = await ResolveBrandingAsync(ct);
        var layoutModel = new Dictionary<string, string?> { ["Body"] = htmlBody };
        var renderer = GetRenderer();
        var html = renderer != null
            ? (await renderer.RenderAsync("_layout", branding.DefaultLanguage, layoutModel, branding, ct)).Html
            : WrapInLegacyLayout(htmlBody, branding);
        await SendTransportAsync(toEmail, new RenderedEmail(subject, html, StripHtml(htmlBody)), ct);
    }

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        => SendNotificationAsync(toEmail, subject, htmlBody, ct);

    // ── Rendering helpers (shared) ──────────────────────────────────────────────

    protected async Task<RenderedEmail> RenderAsync(
        string templateName, Dictionary<string, string?> model, CancellationToken ct)
    {
        var branding = await ResolveBrandingAsync(ct);
        var renderer = GetRenderer();
        if (renderer == null)
        {
            var subject = $"{branding.ProductName} — {templateName.Replace('-', ' ')}";
            var legacyBody = string.Join("\n", model.Select(kv => $"<p>{System.Net.WebUtility.HtmlEncode(kv.Key)}: {System.Net.WebUtility.HtmlEncode(kv.Value ?? "")}</p>"));
            var html = WrapInLegacyLayout(legacyBody, branding);
            return new RenderedEmail(subject, html, StripHtml(legacyBody));
        }
        var body = await renderer.RenderAsync(templateName, branding.DefaultLanguage, model, branding, ct);
        var layoutModel = new Dictionary<string, string?>(model, StringComparer.Ordinal) { ["Body"] = body.Html };
        var wrapped = await renderer.RenderAsync("_layout", branding.DefaultLanguage, layoutModel, branding, ct);
        return new RenderedEmail(body.Subject, wrapped.Html, body.Text);
    }

    protected async Task<ResolvedBranding> ResolveBrandingAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var brandingSvc = scope.ServiceProvider.GetService<ITenantBrandingService>();
        var tenantCtx   = scope.ServiceProvider.GetService<ITenantContext>();
        var tenantId    = tenantCtx?.TenantId;
        if (brandingSvc != null && tenantId.HasValue)
        {
            try { return await brandingSvc.GetAsync(tenantId.Value, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Branding lookup failed; using defaults"); }
        }
        return new ResolvedBranding(
            ProductName:      _config["Tenant:DefaultBranding:ProductName"]      ?? "Planscape",
            AccentColor:      _config["Tenant:DefaultBranding:AccentColor"]      ?? "#E8912D",
            HeaderColor:      _config["Tenant:DefaultBranding:HeaderColor"]      ?? "#1A237E",
            LogoUrl:          _config["Tenant:DefaultBranding:LogoUrl"],
            SupportEmail:     _config["Tenant:DefaultBranding:SupportEmail"]     ?? "support@planscape.io",
            EmailFromName:    FromName,
            EmailFromAddress: FromAddress,
            EmailSignature:   _config["Tenant:DefaultBranding:EmailSignature"],
            DefaultLanguage:  _config["Tenant:DefaultBranding:DefaultLanguage"]  ?? "en");
    }

    /// <summary>Resolve the sender identity: per-tenant branding override → config defaults.</summary>
    protected async Task<(string name, string address)> ResolveFromAsync(CancellationToken ct)
    {
        var branding = await ResolveBrandingAsync(ct);
        var address = !string.IsNullOrWhiteSpace(branding.EmailFromAddress) ? branding.EmailFromAddress : FromAddress;
        var name    = !string.IsNullOrWhiteSpace(branding.EmailFromName)    ? branding.EmailFromName    : FromName;
        return (name, address);
    }

    protected IEmailTemplateRenderer? GetRenderer()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            return scope.ServiceProvider.GetService<IEmailTemplateRenderer>();
        }
        catch { return null; }
    }

    // ── Legacy layout — used only if the template renderer is unavailable. ──

    protected static string WrapInLegacyLayout(string bodyContent, ResolvedBranding branding) => $@"
<!DOCTYPE html>
<html lang=""en"">
<head><meta charset=""utf-8"" /></head>
<body style=""margin:0; padding:0; font-family:'Segoe UI',Roboto,Helvetica,Arial,sans-serif; background:#f4f5f7;"">
  <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f4f5f7; padding:40px 0;"">
    <tr><td align=""center"">
      <table width=""600"" cellpadding=""0"" cellspacing=""0""
             style=""background:#ffffff; border-radius:8px; overflow:hidden; box-shadow:0 2px 8px rgba(0,0,0,0.08);"">
        <tr>
          <td style=""background:{branding.HeaderColor}; padding:24px 32px;"">
            <span style=""color:#ffffff; font-size:22px; font-weight:700; letter-spacing:0.5px;"">{System.Net.WebUtility.HtmlEncode(branding.ProductName)}</span>
          </td>
        </tr>
        <tr>
          <td style=""padding:32px;"">
            {bodyContent}
          </td>
        </tr>
        <tr>
          <td style=""padding:16px 32px; background:#f9fafb; border-top:1px solid #e5e7eb; font-size:12px; color:#9ca3af;"">
            &copy; {DateTime.UtcNow.Year} {System.Net.WebUtility.HtmlEncode(branding.ProductName)}
          </td>
        </tr>
      </table>
    </td></tr>
  </table>
</body>
</html>";

    protected static string StripHtml(string html)
    {
        var noTags = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "");
        return System.Net.WebUtility.HtmlDecode(noTags).Trim();
    }
}
