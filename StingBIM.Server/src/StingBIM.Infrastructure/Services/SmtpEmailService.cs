using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using StingBIM.Core.Interfaces;

namespace StingBIM.Infrastructure.Services;

/// <summary>
/// MailKit-based SMTP email service for sending invite, password-reset, and notification emails.
/// Reads configuration from the "Email" section in appsettings.
/// </summary>
public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailService> _logger;

    private string Host        => _config["Email:Host"]!;
    private int    Port        => int.TryParse(_config["Email:Port"], out var p) ? p : 587;
    private string Username    => _config["Email:Username"] ?? "";
    private string Password    => _config["Email:Password"] ?? "";
    private string FromAddress => _config["Email:FromAddress"] ?? "noreply@stingbim.com";
    private string FromName    => _config["Email:FromName"] ?? "StingBIM";
    private bool   UseSsl      => bool.TryParse(_config["Email:UseSsl"], out var v) && v;

    public SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendInviteEmailAsync(
        string toEmail, string displayName, string inviterName,
        string projectName, string serverUrl, CancellationToken ct = default)
    {
        var subject = $"You've been invited to {projectName} on StingBIM";
        var html = WrapInLayout($@"
            <h2>Welcome to StingBIM</h2>
            <p>Hi {Escape(displayName)},</p>
            <p><strong>{Escape(inviterName)}</strong> has invited you to collaborate on
               <strong>{Escape(projectName)}</strong>.</p>
            <p>To get started, visit the server and set your password:</p>
            <p style=""text-align:center; margin:32px 0;"">
              <a href=""{Escape(serverUrl)}/reset-password?email={Uri.EscapeDataString(toEmail)}""
                 style=""background:#0066cc; color:#ffffff; padding:12px 32px;
                        border-radius:6px; text-decoration:none; font-weight:600;"">
                Set Password &amp; Join
              </a>
            </p>
            <p style=""color:#666; font-size:13px;"">
              If you did not expect this invitation you can safely ignore this email.
            </p>");

        await SendAsync(toEmail, subject, html, ct);
    }

    public async Task SendPasswordResetEmailAsync(
        string toEmail, string resetToken, string serverUrl, CancellationToken ct = default)
    {
        var subject = "StingBIM — Password Reset";
        var html = WrapInLayout($@"
            <h2>Password Reset</h2>
            <p>A password reset was requested for your StingBIM account.</p>
            <p style=""text-align:center; margin:32px 0;"">
              <a href=""{Escape(serverUrl)}/reset-password?token={Uri.EscapeDataString(resetToken)}&amp;email={Uri.EscapeDataString(toEmail)}""
                 style=""background:#0066cc; color:#ffffff; padding:12px 32px;
                        border-radius:6px; text-decoration:none; font-weight:600;"">
                Reset Password
              </a>
            </p>
            <p style=""color:#666; font-size:13px;"">
              This link expires in 24 hours. If you did not request a password reset, ignore this email.
            </p>");

        await SendAsync(toEmail, subject, html, ct);
    }

    public async Task SendNotificationAsync(
        string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        var html = WrapInLayout(htmlBody);
        await SendAsync(toEmail, subject, html, ct);
    }

    // ── Private helpers ──────────────────────────────────────────────────────────

    private async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(FromName, FromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        using var client = new SmtpClient();
        try
        {
            var secureOption = UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await client.ConnectAsync(Host, Port, secureOption, ct);

            if (!string.IsNullOrEmpty(Username))
                await client.AuthenticateAsync(Username, Password, ct);

            await client.SendAsync(message, ct);
            _logger.LogInformation("Email sent to {ToEmail}: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {ToEmail}: {Subject}", toEmail, subject);
            throw;
        }
        finally
        {
            await client.DisconnectAsync(quit: true, ct);
        }
    }

    private static string WrapInLayout(string bodyContent) => $@"
<!DOCTYPE html>
<html lang=""en"">
<head><meta charset=""utf-8"" /></head>
<body style=""margin:0; padding:0; font-family:'Segoe UI',Roboto,Helvetica,Arial,sans-serif; background:#f4f5f7;"">
  <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f4f5f7; padding:40px 0;"">
    <tr><td align=""center"">
      <table width=""600"" cellpadding=""0"" cellspacing=""0""
             style=""background:#ffffff; border-radius:8px; overflow:hidden; box-shadow:0 2px 8px rgba(0,0,0,0.08);"">
        <!-- Header -->
        <tr>
          <td style=""background:#0066cc; padding:24px 32px;"">
            <span style=""color:#ffffff; font-size:22px; font-weight:700; letter-spacing:0.5px;"">StingBIM</span>
          </td>
        </tr>
        <!-- Body -->
        <tr>
          <td style=""padding:32px;"">
            {bodyContent}
          </td>
        </tr>
        <!-- Footer -->
        <tr>
          <td style=""padding:16px 32px; background:#f9fafb; border-top:1px solid #e5e7eb; font-size:12px; color:#9ca3af;"">
            &copy; {DateTime.UtcNow.Year} StingBIM &mdash; ISO 19650 compliant BIM collaboration
          </td>
        </tr>
      </table>
    </td></tr>
  </table>
</body>
</html>";

    private static string Escape(string text) =>
        System.Net.WebUtility.HtmlEncode(text);
}

/// <summary>
/// No-op email service used in development or when SMTP is not configured.
/// Logs the email details instead of sending.
/// </summary>
public class NullEmailService : IEmailService
{
    private readonly ILogger<NullEmailService> _logger;

    public NullEmailService(ILogger<NullEmailService> logger) => _logger = logger;

    public Task SendInviteEmailAsync(
        string toEmail, string displayName, string inviterName,
        string projectName, string serverUrl, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[NullEmail] Invite: to={ToEmail}, displayName={DisplayName}, inviter={Inviter}, project={Project}, url={Url}",
            toEmail, displayName, inviterName, projectName, serverUrl);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetEmailAsync(
        string toEmail, string resetToken, string serverUrl, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[NullEmail] PasswordReset: to={ToEmail}, token={Token}, url={Url}",
            toEmail, resetToken, serverUrl);
        return Task.CompletedTask;
    }

    public Task SendNotificationAsync(
        string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[NullEmail] Notification: to={ToEmail}, subject={Subject}",
            toEmail, subject);
        return Task.CompletedTask;
    }
}
