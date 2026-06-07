using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MimeKit;
using Planscape.Core.Interfaces;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// MailKit-based SMTP email service. FLEX-03/07 — renders templates from the
/// on-disk <c>EmailTemplates/</c> directory with per-tenant branding resolved via
/// <see cref="ITenantBrandingService"/>. Falls back to a hard-coded plain template
/// when the renderer cannot find a file (never silently fails to send).
/// </summary>
public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    // Config keys use either "Smtp:*" (matches the Production template) or "Email:*" (legacy).
    private string Cfg(string key, string fallback = "") =>
        _config[$"Smtp:{key}"] ?? _config[$"Email:{key}"] ?? fallback;

    private string Host        => Cfg("Host");
    public  bool   IsConfigured => !string.IsNullOrWhiteSpace(Host);   // item 8
    private int    Port        => int.TryParse(Cfg("Port", "587"), out var p) ? p : 587;
    private string Username    => Cfg("Username");
    private string Password    => Cfg("Password");
    private string FromAddress => Cfg("FromAddress", "noreply@planscape.io");
    private string FromName    => Cfg("FromName", "Planscape");
    private bool   UseSsl      => bool.TryParse(Cfg("UseSsl", "true"), out var v) && v;

    public SmtpEmailService(
        IConfiguration config,
        ILogger<SmtpEmailService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _config = config;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task SendInviteEmailAsync(
        string toEmail, string displayName, string inviterName,
        string projectName, string serverUrl, string? resetToken = null, Guid projectId = default, CancellationToken ct = default)
    {
        // One source of truth for the deep link (see InviteLink) so the email
        // and the controller's response carry the identical URL. With a reset
        // token the invitee sets their password straight from the email and is
        // then redirected to the project; without one the link only prefills
        // the email and they must request a reset.
        var acceptUrl = InviteLink.BuildAcceptUrl(serverUrl, toEmail, resetToken, projectId);
        var model = new Dictionary<string, string?>
        {
            ["DisplayName"]  = displayName,
            ["InviterName"]  = inviterName,
            ["ProjectName"]  = projectName,
            ["AcceptUrl"]    = acceptUrl,
            ["ServerUrl"]    = serverUrl,
        };
        var rendered = await RenderAsync("invite", model, ct);
        await SendAsync(toEmail, rendered, ct);
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
        await SendAsync(toEmail, rendered, ct);
    }

    public async Task SendNotificationAsync(
        string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        // Ad-hoc notification — wrap the caller's body in the branded layout but skip
        // template lookup (the caller has already composed the message body).
        var branding = await ResolveBrandingAsync(ct);
        var layoutModel = new Dictionary<string, string?>
        {
            ["Body"] = htmlBody,
        };
        // Use the layout template directly by invoking a synthetic template name.
        var renderer = GetRenderer();
        var html = renderer != null
            ? (await renderer.RenderAsync("_layout", branding.DefaultLanguage, layoutModel, branding, ct)).Html
            : WrapInLegacyLayout(htmlBody, branding);
        await SendAsync(toEmail, new RenderedEmail(subject, html, StripHtml(htmlBody)), ct);
    }

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        => SendNotificationAsync(toEmail, subject, htmlBody, ct);

    // ── Rendering helpers ─────────────────────────────────────────────────────

    private async Task<RenderedEmail> RenderAsync(
        string templateName,
        Dictionary<string, string?> model,
        CancellationToken ct)
    {
        var branding = await ResolveBrandingAsync(ct);
        var renderer = GetRenderer();
        if (renderer == null)
        {
            // Renderer not wired (DI failure) — emit a plain, branding-aware fallback.
            var subject = $"{branding.ProductName} — {templateName.Replace('-', ' ')}";
            var legacyBody = string.Join("\n", model.Select(kv => $"<p>{System.Net.WebUtility.HtmlEncode(kv.Key)}: {System.Net.WebUtility.HtmlEncode(kv.Value ?? "")}</p>"));
            var html = WrapInLegacyLayout(legacyBody, branding);
            return new RenderedEmail(subject, html, StripHtml(legacyBody));
        }
        // Render the per-template body, then wrap in the layout.
        var body = await renderer.RenderAsync(templateName, branding.DefaultLanguage, model, branding, ct);
        var layoutModel = new Dictionary<string, string?>(model, StringComparer.Ordinal)
        {
            ["Body"] = body.Html,
        };
        var wrapped = await renderer.RenderAsync("_layout", branding.DefaultLanguage, layoutModel, branding, ct);
        return new RenderedEmail(body.Subject, wrapped.Html, body.Text);
    }

    private async Task<ResolvedBranding> ResolveBrandingAsync(CancellationToken ct)
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
        // No tenant in scope (Hangfire job, seed task, etc.) — build defaults manually.
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

    private IEmailTemplateRenderer? GetRenderer()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            return scope.ServiceProvider.GetService<IEmailTemplateRenderer>();
        }
        catch { return null; }
    }

    // ── SMTP transport ────────────────────────────────────────────────────────

    private async Task SendAsync(string toEmail, RenderedEmail email, CancellationToken ct)
    {
        var branding = await ResolveBrandingAsync(ct);
        var fromAddress = !string.IsNullOrWhiteSpace(branding.EmailFromAddress)
            ? branding.EmailFromAddress
            : FromAddress;
        var fromName = !string.IsNullOrWhiteSpace(branding.EmailFromName)
            ? branding.EmailFromName
            : FromName;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = email.Subject;

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = email.Html,
            TextBody = email.Text,
        };
        message.Body = bodyBuilder.ToMessageBody();

        if (string.IsNullOrWhiteSpace(Host))
        {
            // No SMTP host configured — behave like NullEmailService to avoid a crash.
            _logger.LogWarning("[Smtp:NoHost] ⚠ EMAIL NOT SENT (Smtp:Host empty) to={ToEmail} subject={Subject}", toEmail, email.Subject);
            return;
        }

        using var client = new SmtpClient();
        try
        {
            // UseSsl=true ⇒ implicit TLS (Gmail :465). Otherwise StartTlsWhenAvailable:
            // upgrades to TLS when the server advertises STARTTLS (Gmail :587) and stays
            // plain when it doesn't (a local Mailpit/MailHog capture on :1025) — so the
            // SAME service proves the path against a dev catcher and sends real mail to Gmail.
            var secureOption = UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
            await client.ConnectAsync(Host, Port, secureOption, ct);
            if (!string.IsNullOrEmpty(Username))
                await client.AuthenticateAsync(Username, Password, ct);
            await client.SendAsync(message, ct);
            _logger.LogInformation("Email sent to {ToEmail}: {Subject}", toEmail, email.Subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {ToEmail}: {Subject}", toEmail, email.Subject);
            throw;
        }
        finally
        {
            await client.DisconnectAsync(quit: true, ct);
        }
    }

    // ── Legacy layout — used only if the template renderer is unavailable. ──

    private static string WrapInLegacyLayout(string bodyContent, ResolvedBranding branding) => $@"
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

    private static string StripHtml(string html)
    {
        var noTags = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "");
        return System.Net.WebUtility.HtmlDecode(noTags).Trim();
    }
}

/// <summary>
/// No-op email service used in development or when SMTP is not configured.
/// Logs the email details instead of sending.
/// </summary>
public class NullEmailService : IEmailService
{
    public bool IsConfigured => false;   // item 8 — no SMTP host wired
    private readonly ILogger<NullEmailService> _logger;

    public NullEmailService(ILogger<NullEmailService> logger)
    {
        _logger = logger;
        // LOUD on startup so a misconfigured deploy never silently drops mail.
        _logger.LogWarning(
            "[NullEmail] ⚠ NO SMTP CONFIGURED — emails (invites, password resets, notifications) "
          + "will NOT be delivered, only logged. Set Smtp__Host / Smtp__Port / Smtp__Username / "
          + "Smtp__Password / Smtp__FromAddress (e.g. Gmail smtp.gmail.com:587 + app password) to send real mail.");
    }

    public Task SendInviteEmailAsync(
        string toEmail, string displayName, string inviterName,
        string projectName, string serverUrl, string? resetToken = null, Guid projectId = default, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "[NullEmail] ⚠ INVITE NOT SENT (no SMTP) to={ToEmail}, displayName={DisplayName}, inviter={Inviter}, project={Project}, url={Url}, hasToken={HasToken}",
            toEmail, displayName, inviterName, projectName, serverUrl, !string.IsNullOrWhiteSpace(resetToken));
        return Task.CompletedTask;
    }

    public Task SendPasswordResetEmailAsync(
        string toEmail, string resetToken, string serverUrl, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "[NullEmail] ⚠ PASSWORD-RESET NOT SENT (no SMTP) to={ToEmail}, token={Token}, url={Url}",
            toEmail, resetToken, serverUrl);
        return Task.CompletedTask;
    }

    public Task SendNotificationAsync(
        string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "[NullEmail] ⚠ NOTIFICATION NOT SENT (no SMTP) to={ToEmail}, subject={Subject}",
            toEmail, subject);
        return Task.CompletedTask;
    }

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        => SendNotificationAsync(toEmail, subject, htmlBody, ct);
}
