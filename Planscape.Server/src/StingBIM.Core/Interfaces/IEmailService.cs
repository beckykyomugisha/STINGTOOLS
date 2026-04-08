namespace StingBIM.Core.Interfaces;

public interface IEmailService
{
    Task SendInviteEmailAsync(string toEmail, string displayName, string inviterName, string projectName, string serverUrl, CancellationToken ct = default);
    Task SendPasswordResetEmailAsync(string toEmail, string resetToken, string serverUrl, CancellationToken ct = default);
    Task SendNotificationAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
}
