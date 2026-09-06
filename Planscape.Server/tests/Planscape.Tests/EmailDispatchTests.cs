using Microsoft.Extensions.Logging.Abstractions;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Services;

namespace Planscape.Tests;

/// <summary>
/// Pins the rule that a NOTIFICATION failure must not fail the operation that
/// triggered it.
///
/// <para>Measured against production on 2026-08-20:
/// <c>POST /api/projects/{id}/members/invite</c> answered <b>500 with an empty
/// body</b> because Resend returned 422 for the recipient domain — after the
/// invitee's user row and invite token were committed and before the
/// <c>ProjectMember</c> row was added. And <c>POST /api/auth/forgot-password</c>
/// answered 200 for an unknown address but 500 for a real one, which is the
/// enumeration oracle that endpoint exists to prevent.</para>
///
/// <para>Both came from the same shape: providers throw on hard failure by contract,
/// and controllers called them bare.</para>
/// </summary>
public class EmailDispatchTests
{
    // ── A failing provider is reported, not propagated ───────────────────────

    [Fact]
    public async Task A_throwing_provider_does_not_propagate()
    {
        // The exact production failure: Resend rejects the recipient and the provider
        // throws by contract. Nothing above it should ever see this exception.
        var email = new StubEmail(configured: true,
            onSend: () => throw new InvalidOperationException(
                "Resend send failed (422): Invalid `to` field."));

        var sent = await EmailDispatch.TrySendAsync(
            email, NullLogger.Instance, "invite", "x@example.com", email.SendAsync);

        Assert.False(sent);
        Assert.Equal(1, email.Attempts);   // it really did try
    }

    [Theory]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(OperationCanceledException))]
    public async Task Every_transport_failure_mode_is_contained(Type exceptionType)
    {
        // Enumerated rather than listing one: a bad key, a rate limit and a dropped
        // connection are all "the message did not go out", and none of them is a reason
        // to fail the caller's actual operation.
        var email = new StubEmail(configured: true,
            onSend: () => throw (Exception)Activator.CreateInstance(exceptionType)!);

        Assert.False(await EmailDispatch.TrySendAsync(
            email, NullLogger.Instance, "invite", "x@example.com", email.SendAsync));
    }

    // ── "Configured" is asked BEFORE sending ─────────────────────────────────

    [Fact]
    public async Task An_unconfigured_provider_reports_false_without_sending()
    {
        // SmtpEmailService with no Host and NullEmailService both no-op and return
        // normally. Reporting that as success would tell the plugin "emailed" for a
        // message that never left the building — and the plugin would then NOT copy
        // the invite link, which is the fallback the user needs.
        var email = new StubEmail(configured: false);

        Assert.False(await EmailDispatch.TrySendAsync(
            email, NullLogger.Instance, "invite", "x@example.com", email.SendAsync));
        Assert.Equal(0, email.Attempts);
    }

    [Fact]
    public async Task A_missing_service_is_not_a_crash()
    {
        // ForgotPassword resolves IEmailService with GetService, which returns null
        // when nothing is registered.
        Assert.False(await EmailDispatch.TrySendAsync(
            null, NullLogger.Instance, "password-reset", "x@example.com",
            () => Task.CompletedTask));
    }

    // ── Success still reports success ────────────────────────────────────────

    [Fact]
    public async Task A_successful_send_reports_true()
    {
        // The guard must not become "always false" — the invite response says
        // "emailed" vs "copy the link" based on this, and getting it wrong in the
        // safe-looking direction tells a user to chase a link that was already sent.
        var email = new StubEmail(configured: true);

        Assert.True(await EmailDispatch.TrySendAsync(
            email, NullLogger.Instance, "invite", "x@example.com", email.SendAsync));
        Assert.Equal(1, email.Attempts);
    }

    /// <summary>Minimal IEmailService that records attempts and can be told to throw.</summary>
    private sealed class StubEmail : IEmailService
    {
        private readonly Action? _onSend;
        public StubEmail(bool configured, Action? onSend = null)
        {
            IsConfigured = configured;
            _onSend = onSend;
        }

        public bool IsConfigured { get; }
        public int Attempts { get; private set; }

        public Task SendAsync()
        {
            Attempts++;
            _onSend?.Invoke();
            return Task.CompletedTask;
        }

        public Task SendInviteEmailAsync(string toEmail, string displayName, string inviterName,
            string projectName, string serverUrl, string? resetToken = null,
            Guid projectId = default, CancellationToken ct = default) => SendAsync();

        public Task SendPasswordResetEmailAsync(string toEmail, string resetToken,
            string serverUrl, CancellationToken ct = default) => SendAsync();

        public Task SendNotificationAsync(string toEmail, string subject, string htmlBody,
            CancellationToken ct = default) => SendAsync();

        public Task SendAsync(string toEmail, string subject, string htmlBody,
            CancellationToken ct = default) => SendAsync();
    }
}
