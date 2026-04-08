namespace StingBIM.API.Services;

public interface IEmailService
{
    Task SendAsync(string to, string subject, string htmlBody);
    Task SendInviteAsync(string to, string displayName, string organisationName, string tempPassword, string serverUrl);
    Task SendPasswordResetAsync(string to, string displayName, string resetToken, string serverUrl);
    Task SendIssueAssignedAsync(string to, string displayName, string issueCode, string title, string projectName, string serverUrl);
    Task SendSlaAlertAsync(string to, string displayName, string issueCode, string title, string priority, int hoursOverdue);
    Task SendComplianceDigestAsync(string to, string displayName, string projectName, double compliancePct, string ragStatus, int openIssues, int overdueActions);
}
