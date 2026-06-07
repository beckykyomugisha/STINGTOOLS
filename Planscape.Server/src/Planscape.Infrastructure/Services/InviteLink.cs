using System;
using System.Text;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Single source of truth for the one-click invite "accept" deep link, shared
/// by the controller (which returns it to the plugin) and the email service
/// (which renders it into the message) so the two never diverge.
///
/// The link points at the set-password / accept page carrying the invite
/// token, the recipient's email, and the target project. On success that page
/// redirects into the SPA project view, so a single click takes the recipient
/// from the email to accepting the invite and landing on the project — and a
/// recipient with no account yet sets their password there (which activates
/// them), rather than dead-ending on a login they can't pass.
/// </summary>
public static class InviteLink
{
    /// <summary>
    /// Build the accept-invite deep link.
    /// <paramref name="token"/> is the raw (un-hashed) invite/reset token; when
    /// absent the link only prefills the email and the recipient must request a
    /// reset. <paramref name="projectId"/> is carried through so the page can
    /// land the recipient on the right project after activation.
    /// </summary>
    public static string BuildAcceptUrl(string baseUrl, string email, string? token, Guid projectId)
    {
        var b = (baseUrl ?? "").TrimEnd('/');
        var sb = new StringBuilder($"{b}/reset-password.html?email={Uri.EscapeDataString(email ?? "")}");
        if (!string.IsNullOrWhiteSpace(token))
            sb.Append($"&token={Uri.EscapeDataString(token!)}");
        if (projectId != Guid.Empty)
            sb.Append($"&project={projectId}");
        return sb.ToString();
    }

    /// <summary>
    /// Cloudflare *quick* tunnels (<c>*.trycloudflare.com</c>) mint a fresh
    /// random hostname on every restart, so any link built against one dies
    /// when the tunnel cycles. Returns a warning when <paramref name="baseUrl"/>
    /// is such a tunnel (so the UI + logs can flag it), else null.
    /// </summary>
    public static string? UnstableBaseWarning(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;
        if (baseUrl.IndexOf("trycloudflare.com", StringComparison.OrdinalIgnoreCase) >= 0)
            return "PUBLIC_BASE_URL is a Cloudflare quick tunnel (trycloudflare.com), which rotates its "
                 + "hostname on restart — this invite link will stop working when the tunnel cycles. "
                 + "Use a named Cloudflare tunnel or a real domain for invites you expect to last.";
        return null;
    }
}
