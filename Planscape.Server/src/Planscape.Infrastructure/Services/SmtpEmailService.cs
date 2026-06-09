using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MimeKit;
using Planscape.Core.Interfaces;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// MailKit-based SMTP transport. Composition (HTML + plain-text rendering,
/// branding) lives in <see cref="EmailServiceBase"/>; this class only owns the
/// SMTP wire-up so it can't drift from the Resend provider.
///
/// Works against Mailpit (:1025, no TLS), Gmail (:587 STARTTLS / :465 SSL) and
/// Resend's SMTP endpoint (smtp.resend.com, user "resend", password = API key) —
/// the zero-code Resend option. For the production HTTP path see
/// <see cref="ResendEmailService"/>.
/// </summary>
public class SmtpEmailService : EmailServiceBase
{
    private string Host        => Cfg("Host");
    public  override bool IsConfigured => !string.IsNullOrWhiteSpace(Host);   // item 8
    private int    Port        => int.TryParse(Cfg("Port", "587"), out var p) ? p : 587;
    private string Username    => Cfg("Username");
    private string Password    => Cfg("Password");
    private bool   UseSsl      => bool.TryParse(Cfg("UseSsl", "true"), out var v) && v;

    public SmtpEmailService(
        IConfiguration config,
        ILogger<SmtpEmailService> logger,
        IServiceScopeFactory scopeFactory)
        : base(config, logger, scopeFactory)
    {
    }

    protected override async Task SendTransportAsync(string toEmail, RenderedEmail email, CancellationToken ct)
    {
        var (fromName, fromAddress) = await ResolveFromAsync(ct);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = email.Subject;

        // BodyBuilder ⇒ proper multipart/alternative (HTML + plain-text fallback).
        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = email.Html,
            TextBody = email.Text,
        };
        message.Body = bodyBuilder.ToMessageBody();

        if (string.IsNullOrWhiteSpace(Host))
        {
            _logger.LogWarning("[Smtp:NoHost] ⚠ EMAIL NOT SENT (Smtp:Host empty) to={ToEmail} subject={Subject}", toEmail, email.Subject);
            return;
        }

        using var client = new SmtpClient();
        try
        {
            // UseSsl=true ⇒ implicit TLS (Gmail/Resend :465). Otherwise StartTlsWhenAvailable:
            // upgrades when the server advertises STARTTLS (Gmail/Resend :587) and stays plain
            // for a local Mailpit/MailHog capture on :1025 — so the SAME transport proves the
            // path against a dev catcher and sends real mail in production.
            var secureOption = UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
            await client.ConnectAsync(Host, Port, secureOption, ct);
            if (!string.IsNullOrEmpty(Username))
                await client.AuthenticateAsync(Username, Password, ct);
            await client.SendAsync(message, ct);
            _logger.LogInformation("[email] smtp sent to {ToEmail}: {Subject}", toEmail, email.Subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[email] smtp send failed to {ToEmail}: {Subject}", toEmail, email.Subject);
            throw;
        }
        finally
        {
            await client.DisconnectAsync(quit: true, ct);
        }
    }
}

/// <summary>
/// No-op email service used in development or when no provider is configured.
/// Logs the email details instead of sending.
/// </summary>
public class NullEmailService : IEmailService
{
    public bool IsConfigured => false;   // item 8 — no provider wired
    private readonly ILogger<NullEmailService> _logger;

    public NullEmailService(ILogger<NullEmailService> logger)
    {
        _logger = logger;
        _logger.LogWarning(
            "[NullEmail] ⚠ NO EMAIL PROVIDER CONFIGURED — emails (invites, password resets, notifications) "
          + "will NOT be delivered, only logged. Set Email__Provider=smtp + Smtp__Host (Gmail/Mailpit) or "
          + "Email__Provider=resend + RESEND_API_KEY to send real mail.");
    }

    public Task SendInviteEmailAsync(
        string toEmail, string displayName, string inviterName,
        string projectName, string serverUrl, string? resetToken = null, Guid projectId = default, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "[NullEmail] ⚠ INVITE NOT SENT (no provider) to={ToEmail}, displayName={DisplayName}, inviter={Inviter}, project={Project}, url={Url}, hasToken={HasToken}",
            toEmail, displayName, inviterName, projectName, serverUrl, !string.IsNullOrWhiteSpace(resetToken));
        return Task.CompletedTask;
    }

    public Task SendPasswordResetEmailAsync(
        string toEmail, string resetToken, string serverUrl, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "[NullEmail] ⚠ PASSWORD-RESET NOT SENT (no provider) to={ToEmail}, token={Token}, url={Url}",
            toEmail, resetToken, serverUrl);
        return Task.CompletedTask;
    }

    public Task SendNotificationAsync(
        string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "[NullEmail] ⚠ NOTIFICATION NOT SENT (no provider) to={ToEmail}, subject={Subject}",
            toEmail, subject);
        return Task.CompletedTask;
    }

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        => SendNotificationAsync(toEmail, subject, htmlBody, ct);
}
