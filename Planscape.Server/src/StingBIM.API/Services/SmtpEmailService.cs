using System.Net;
using System.Net.Mail;

namespace StingBIM.API.Services;

/// <summary>
/// SMTP email service. Configure via appsettings:
///   Email:SmtpHost, Email:SmtpPort, Email:SmtpUser, Email:SmtpPass,
///   Email:FromAddress, Email:FromName, Email:ServerUrl (for links in emails)
/// When Email:SmtpHost is empty, emails are logged to console only (dev mode).
/// </summary>
public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailService> _log;

    public SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger)
    {
        _config = config;
        _log = logger;
    }

    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        string host = _config["Email:SmtpHost"] ?? "";
        if (string.IsNullOrEmpty(host))
        {
            _log.LogInformation("EMAIL (dev/no-SMTP) To={To} Subject={Subject}", to, subject);
            return;
        }

        try
        {
            int port = int.TryParse(_config["Email:SmtpPort"], out int p) ? p : 587;
            string user = _config["Email:SmtpUser"] ?? "";
            string pass = _config["Email:SmtpPass"] ?? "";
            string from = _config["Email:FromAddress"] ?? "noreply@stingbim.io";
            string fromName = _config["Email:FromName"] ?? "StingBIM";

            using var client = new SmtpClient(host, port)
            {
                Credentials = new NetworkCredential(user, pass),
                EnableSsl    = true
            };

            var msg = new MailMessage
            {
                From       = new MailAddress(from, fromName),
                Subject    = subject,
                Body       = htmlBody,
                IsBodyHtml = true
            };
            msg.To.Add(to);
            await client.SendMailAsync(msg);
            _log.LogInformation("Email sent To={To} Subject={Subject}", to, subject);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Email send failed To={To}", to);
        }
    }

    public async Task SendInviteAsync(string to, string displayName, string organisationName, string tempPassword, string serverUrl)
    {
        string subject = $"You've been invited to {organisationName} on StingBIM";
        string body = EmailTemplates.Invite(displayName, organisationName, tempPassword, serverUrl);
        await SendAsync(to, subject, body);
    }

    public async Task SendPasswordResetAsync(string to, string displayName, string resetToken, string serverUrl)
    {
        string subject = "Reset your StingBIM password";
        string body = EmailTemplates.PasswordReset(displayName, resetToken, serverUrl);
        await SendAsync(to, subject, body);
    }

    public async Task SendIssueAssignedAsync(string to, string displayName, string issueCode, string title, string projectName, string serverUrl)
    {
        string subject = $"[{projectName}] Issue assigned to you: {issueCode}";
        string body = EmailTemplates.IssueAssigned(displayName, issueCode, title, projectName, serverUrl);
        await SendAsync(to, subject, body);
    }

    public async Task SendSlaAlertAsync(string to, string displayName, string issueCode, string title, string priority, int hoursOverdue)
    {
        string subject = $"⚠️ SLA breach: {issueCode} is {hoursOverdue}h overdue";
        string body = EmailTemplates.SlaAlert(displayName, issueCode, title, priority, hoursOverdue);
        await SendAsync(to, subject, body);
    }

    public async Task SendComplianceDigestAsync(string to, string displayName, string projectName, double compliancePct, string ragStatus, int openIssues, int overdueActions)
    {
        string subject = $"[{projectName}] Weekly BIM Compliance Digest";
        string body = EmailTemplates.ComplianceDigest(displayName, projectName, compliancePct, ragStatus, openIssues, overdueActions);
        await SendAsync(to, subject, body);
    }
}
