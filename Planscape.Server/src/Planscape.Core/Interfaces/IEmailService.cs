namespace Planscape.Core.Interfaces;

public interface IEmailService
{
    // Item 8 — true when a real SMTP host is configured, so callers can tell the user
    // "invite emailed" vs "email not configured — link copied instead" rather than silently no-op.
    bool IsConfigured { get; }

    // resetToken (optional): when supplied, the invite link carries it so the
    // invited user can set their password directly from the email — required
    // because invited users start IsActive=false and forgot-password skips them.
    // projectId (optional): carried into the one-click deep link so the
    // recipient lands on the right project after accepting.
    Task SendInviteEmailAsync(string toEmail, string displayName, string inviterName, string projectName, string serverUrl, string? resetToken = null, Guid projectId = default, CancellationToken ct = default);
    Task SendPasswordResetEmailAsync(string toEmail, string resetToken, string serverUrl, CancellationToken ct = default);
    Task SendNotificationAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
}
