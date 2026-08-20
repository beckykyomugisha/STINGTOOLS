using Microsoft.Extensions.Logging;
using Planscape.Core.Interfaces;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Sends a NOTIFICATION without letting its failure fail the operation that
/// triggered it.
///
/// <para><b>Why this exists.</b> Providers throw on a hard failure by contract —
/// <c>ResendEmailService</c> throws on a missing key, an unreachable API and any
/// non-2xx from Resend. Controllers called them bare, so a rejected recipient took
/// down an entire request that had ALREADY COMMITTED its database work. Measured
/// against production on 2026-08-20: <c>POST /api/projects/{id}/members/invite</c>
/// answered <b>500 with an empty body</b> because Resend returned 422 for the
/// recipient domain — after the invitee's <c>AppUser</c> row and its invite token
/// were saved, and before the <c>ProjectMember</c> row was added. The invitation was
/// half-created and the caller was told it had failed.</para>
///
/// <para>The same throw defeated <c>ForgotPassword</c>'s entire purpose. That
/// endpoint returns an identical 200 for every address specifically "to prevent
/// email enumeration" — but an unknown address returns before sending, so it 200s,
/// while a REAL one reaches the send and 500d. Two different answers is an
/// enumeration oracle, and it was demonstrated against production.</para>
///
/// <para><b>This is not a silent catch.</b> The failure is logged with its reason
/// and returned to the caller as <c>false</c>, so the response can say
/// <c>emailSent: false</c> and hand back a copyable link — which is precisely what
/// the invite endpoint already promised to do and could not reach. A caller that
/// treats the email as the operation itself (a "resend verification" button, the
/// <c>/notifications/test-email</c> diagnostic) must NOT use this — there, a failure
/// is the answer the user asked for.</para>
/// </summary>
public static class EmailDispatch
{
    /// <summary>
    /// Runs <paramref name="send"/> and reports whether mail actually went out.
    ///
    /// <para>Returns false — never throws — when the provider is unconfigured or the
    /// send fails. <paramref name="what"/> and <paramref name="toEmail"/> go in the
    /// log line so an operator can tell which message was lost and to whom, which is
    /// the thing that was impossible while the failure surfaced as a bare 500.</para>
    /// </summary>
    public static async Task<bool> TrySendAsync(
        IEmailService? email, ILogger logger, string what, string toEmail, Func<Task> send)
    {
        if (email == null)
        {
            logger.LogWarning("[email] {What} to {To} not sent — no email service registered.", what, toEmail);
            return false;
        }

        // Asked before sending, not after: a provider that no-ops when unconfigured
        // (SmtpEmailService with no Host, NullEmailService) would otherwise report
        // success for a message that never left the building.
        if (!email.IsConfigured)
        {
            logger.LogWarning(
                "[email] {What} to {To} NOT SENT — no email provider configured. "
              + "Set Email__Provider=resend + RESEND_API_KEY, or Smtp__Host.", what, toEmail);
            return false;
        }

        try
        {
            await send().ConfigureAwait(false);
            logger.LogInformation("[email] {What} sent to {To}.", what, toEmail);
            return true;
        }
        catch (Exception ex)
        {
            // Deliberately broad. Every provider failure mode belongs here — a bad key,
            // a rejected recipient, a rate limit, a network drop — and none of them is a
            // reason to fail the caller's actual operation. The reason is logged rather
            // than swallowed; that distinction is the whole point.
            logger.LogError(ex,
                "[email] {What} to {To} FAILED — the operation continued. Reason: {Reason}",
                what, toEmail, ex.Message);
            return false;
        }
    }
}
