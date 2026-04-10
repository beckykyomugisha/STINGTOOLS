namespace Planscape.API.Services;

/// <summary>HTML email templates for all Planscape system emails.</summary>
internal static class EmailTemplates
{
    private const string Brand = "#1A237E";

    private static string Wrap(string title, string content) => $@"<!DOCTYPE html>
<html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width'></head>
<body style='font-family:Segoe UI,Arial,sans-serif;background:#F4F5F7;margin:0;padding:20px'>
<div style='max-width:600px;margin:0 auto;background:white;border-radius:8px;overflow:hidden;border:1px solid #E0E0E8'>
<div style='background:{Brand};color:white;padding:20px 24px'>
  <h1 style='margin:0;font-size:18px'>🏗 Planscape</h1>
  <p style='margin:4px 0 0;font-size:13px;opacity:.8'>{title}</p>
</div>
<div style='padding:24px'>{content}</div>
<div style='background:#F4F5F7;padding:12px 24px;font-size:11px;color:#888;text-align:center'>
  Planscape · ISO 19650 BIM Coordination Platform · This email was sent automatically.
</div>
</div></body></html>";

    public static string Invite(string name, string org, string tempPw, string serverUrl) => Wrap(
        "You've been invited",
        $@"<p>Hi <strong>{name}</strong>,</p>
<p>You've been invited to join <strong>{org}</strong> on Planscape — your team's ISO 19650 BIM coordination platform.</p>
<table style='border-collapse:collapse;width:100%;margin:16px 0'>
<tr><td style='padding:8px;background:#F8F8FC;border:1px solid #E0E0E8;font-weight:600'>Login URL</td>
    <td style='padding:8px;border:1px solid #E0E0E8'><a href='{serverUrl}'>{serverUrl}</a></td></tr>
<tr><td style='padding:8px;background:#F8F8FC;border:1px solid #E0E0E8;font-weight:600'>Temporary Password</td>
    <td style='padding:8px;border:1px solid #E0E0E8;font-family:monospace;font-size:16px'>{tempPw}</td></tr>
</table>
<p>Please change your password after your first login.</p>
<a href='{serverUrl}' style='display:inline-block;background:{Brand};color:white;padding:10px 20px;border-radius:6px;text-decoration:none;font-weight:600'>Open Planscape →</a>");

    public static string PasswordReset(string name, string token, string serverUrl) => Wrap(
        "Password Reset Request",
        $@"<p>Hi <strong>{name}</strong>,</p>
<p>We received a request to reset your Planscape password. Use the token below (valid for 1 hour):</p>
<div style='background:#F0F4FF;border:1px solid #C5CEFF;border-radius:6px;padding:16px;text-align:center;margin:16px 0'>
  <span style='font-family:monospace;font-size:22px;font-weight:700;letter-spacing:2px;color:{Brand}'>{token}</span>
</div>
<p>Or use this link: <a href='{serverUrl}/reset-password?token={token}'>Reset Password</a></p>
<p style='color:#888;font-size:12px'>If you didn't request this, you can safely ignore this email.</p>");

    public static string IssueAssigned(string name, string code, string title, string project, string serverUrl) => Wrap(
        $"Issue Assigned: {code}",
        $@"<p>Hi <strong>{name}</strong>,</p>
<p>You have been assigned issue <strong>{code}</strong> in project <strong>{project}</strong>:</p>
<div style='background:#FFF8E1;border-left:4px solid #F57F17;padding:12px 16px;margin:16px 0;border-radius:0 6px 6px 0'>
  <strong>{code}</strong> — {title}
</div>
<a href='{serverUrl}' style='display:inline-block;background:{Brand};color:white;padding:10px 20px;border-radius:6px;text-decoration:none;font-weight:600'>View Issue →</a>");

    public static string SlaAlert(string name, string code, string title, string priority, int hours) => Wrap(
        $"SLA Breach: {code}",
        $@"<p>Hi <strong>{name}</strong>,</p>
<div style='background:#FFEBEE;border-left:4px solid #C62828;padding:12px 16px;margin:16px 0;border-radius:0 6px 6px 0'>
  <strong>⚠️ SLA BREACH</strong><br>
  Issue <strong>{code}</strong> [{priority}] has exceeded its SLA by <strong>{hours} hours</strong>.<br>
  <em>{title}</em>
</div>
<p>Please review and update the issue status immediately.</p>");

    public static string ComplianceDigest(string name, string project, double pct, string rag, int issues, int actions) => Wrap(
        $"{project} — Weekly Digest",
        $@"<p>Hi <strong>{name}</strong>,</p>
<p>Here is your weekly BIM compliance digest for <strong>{project}</strong>:</p>
<table style='border-collapse:collapse;width:100%;margin:16px 0'>
<tr><td style='padding:10px;background:#F8F8FC;border:1px solid #E0E0E8;font-weight:600'>Tag Compliance</td>
    <td style='padding:10px;border:1px solid #E0E0E8;font-size:20px;font-weight:700'>{pct:F1}% <span style='font-size:14px;color:{(rag=="GREEN"?"#2E7D32":rag=="AMBER"?"#F57F17":"#C62828")}'>{rag}</span></td></tr>
<tr><td style='padding:10px;background:#F8F8FC;border:1px solid #E0E0E8;font-weight:600'>Open Issues</td>
    <td style='padding:10px;border:1px solid #E0E0E8'>{issues}</td></tr>
<tr><td style='padding:10px;background:#F8F8FC;border:1px solid #E0E0E8;font-weight:600'>Overdue Actions</td>
    <td style='padding:10px;border:1px solid #E0E0E8{(actions>0?";color:#C62828":"")}'>{actions}</td></tr>
</table>");
}
