namespace Planscape.Core.Interfaces;

public interface IEmailService
{
    // resetToken (optional): when supplied, the invite link carries it so the
    // invited user can set their password directly from the email — required
    // because invited users start IsActive=false and forgot-password skips them.
    Task SendInviteEmailAsync(string toEmail, string displayName, string inviterName, string projectName, string serverUrl, string? resetToken = null, CancellationToken ct = default);
    Task SendPasswordResetEmailAsync(string toEmail, string resetToken, string serverUrl, CancellationToken ct = default);
    Task SendNotificationAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
}
