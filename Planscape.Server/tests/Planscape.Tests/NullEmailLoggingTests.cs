using Microsoft.Extensions.Logging;
using Planscape.Infrastructure.Services;

namespace Planscape.Tests;

/// <summary>
/// Pins the rule that a credential never reaches the log, even on the failure path.
///
/// <para><see cref="NullEmailService"/> is selected whenever no email provider is
/// configured. On a production host that means two things at once: email is broken,
/// AND — until this change — every password-reset token was being written verbatim
/// into the platform's log store at Warning level, which is above the
/// <c>Serilog__MinimumLevel__Default=Warning</c> floor render.yaml sets, so it was
/// retained rather than dropped.</para>
///
/// <para>A single-use reset token grants a password change on the named account, so a
/// log line carrying both the address and the token is a complete account takeover for
/// anyone who can read logs. This is the same defect class as #711, where a
/// secret-shaped <c>EMAIL_FROM</c> was interpolated into Cloudflare's Function logs on
/// every failed send.</para>
///
/// <para>The invite path in the same class already reported presence rather than value
/// (<c>hasToken={HasToken}</c>), which is the evidence this was an oversight rather
/// than a deliberate development affordance.</para>
/// </summary>
public class NullEmailLoggingTests
{
    private const string Token = "PLNS-RESET-3f9a1c7e5b2d48a6b0c1d2e3f4a5b6c7";

    [Fact]
    public async Task A_password_reset_token_is_never_written_to_the_log()
    {
        var log = new CapturingLogger<NullEmailService>();
        var svc = new NullEmailService(log);

        await svc.SendPasswordResetEmailAsync(
            "victim@example.com", Token, "https://app.planscape.build");

        // Assert on the WHOLE rendered line plus every structured argument: the token
        // must not survive in either, because a Serilog sink writes both.
        Assert.NotEmpty(log.Entries);                       // it really did log — an empty
                                                            // log would pass this test
                                                            // vacuously and hide a
                                                            // silent no-op.
        Assert.DoesNotContain(Token, log.Everything);
    }

    [Fact]
    public async Task The_reset_line_still_names_the_account_and_that_nothing_was_sent()
    {
        // Redaction must not become silence. The operator still has to be able to tell
        // "a reset was requested for this address and no mail left the building" —
        // otherwise the fix trades a credential leak for the invisibility that let the
        // #711 outage run unnoticed.
        var log = new CapturingLogger<NullEmailService>();
        var svc = new NullEmailService(log);

        await svc.SendPasswordResetEmailAsync(
            "victim@example.com", Token, "https://app.planscape.build");

        var line = log.Entries.Single(e => e.Level == LogLevel.Warning
                                        && e.Rendered.Contains("PASSWORD-RESET"));
        Assert.Contains("victim@example.com", line.Rendered);
        Assert.Contains("NOT SENT", line.Rendered);
        Assert.True(line.Level >= LogLevel.Warning,
            "must clear the production Serilog floor (Warning) or it is invisible where it matters");
    }

    [Fact]
    public async Task An_invite_token_is_not_logged_either()
    {
        // Guards the path that was already correct, so a later edit cannot regress it
        // back to printing the value.
        var log = new CapturingLogger<NullEmailService>();
        var svc = new NullEmailService(log);

        await svc.SendInviteEmailAsync(
            "invitee@example.com", "Invitee", "Inviter", "Project X",
            "https://app.planscape.build", Token, Guid.NewGuid());

        Assert.NotEmpty(log.Entries);
        Assert.DoesNotContain(Token, log.Everything);
    }

    // ── capture ──────────────────────────────────────────────────────────────

    private sealed record Entry(LogLevel Level, string Rendered, string State);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public readonly List<Entry> Entries = new();

        /// <summary>Rendered message AND raw structured state, concatenated — a secret
        /// that escapes only as a structured property is still in the sink.</summary>
        public string Everything => string.Join("\n",
            Entries.Select(e => e.Rendered + "\n" + e.State));

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(new Entry(logLevel, formatter(state, exception), state?.ToString() ?? ""));
    }
}
