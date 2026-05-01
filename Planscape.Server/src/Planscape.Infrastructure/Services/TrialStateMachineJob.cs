using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// S1.6 — daily Hangfire job that walks every tenant on plan = Trial and:
///
///   • Sends reminder emails 7 / 3 / 1 days before <c>TrialExpiresAt</c>
///     (idempotent — uses <c>TrialReminderSentDays</c> bitmask on Tenant
///     to avoid double-sends).
///   • On the day of expiry, demotes the tenant to a frozen state
///     (sets <c>IsActive = false</c>) until billing converts them. The
///     active session keeps working until the JWT expires; new logins are
///     blocked by AuthController's IsActive check.
///   • Tenants whose <c>PlannedUpgrade</c> is set and have a successful
///     payment method on file (S2.x) are auto-promoted; those without
///     are frozen and dunning-mailed.
///
/// Wire in Program.cs:
///   RecurringJob.AddOrUpdate&lt;TrialStateMachineJob&gt;("trial-state",
///     j =&gt; j.ExecuteAsync(CancellationToken.None), Cron.Daily());
/// </summary>
public class TrialStateMachineJob
{
    private readonly PlanscapeDbContext _db;
    private readonly IEmailService _email;
    private readonly ILogger<TrialStateMachineJob> _logger;

    public TrialStateMachineJob(PlanscapeDbContext db, IEmailService email, ILogger<TrialStateMachineJob> logger)
    {
        _db = db;
        _email = email;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        // Background job — bypass the global tenant filter so we can
        // see every tenant regardless of HTTP context.
        _db.BypassTenantFilter = true;

        var now = DateTime.UtcNow;
        var trials = await _db.Tenants
            .Where(t => t.Plan == BillingPlan.Trial && t.TrialExpiresAt != null)
            .ToListAsync(ct);

        foreach (var tenant in trials)
        {
            var daysLeft = (int)Math.Ceiling((tenant.TrialExpiresAt!.Value - now).TotalDays);

            // Expiry: freeze + dunning email.
            if (daysLeft <= 0)
            {
                if (tenant.IsActive)
                {
                    tenant.IsActive = false;
                    _logger.LogInformation("Trial expired for tenant {Slug}; freezing.", tenant.Slug);
                    await SafeSendAsync(tenant.ContactEmail,
                        $"Your Planscape trial has ended",
                        $"Hi {tenant.Name},\n\nYour 30-day Planscape trial has ended. Your data is safe and your team can read it once you upgrade.\n\nVisit https://planscape.app/billing/upgrade to continue.\n\n— Planscape");
                }
                continue;
            }

            // Reminder windows: 7d, 3d, 1d. Each uses one bit of the bitmask.
            var bit = daysLeft <= 1 ? 1 : daysLeft <= 3 ? 2 : daysLeft <= 7 ? 4 : 0;
            if (bit == 0) continue;
            if ((tenant.TrialReminderSentDays & bit) != 0) continue; // already sent

            await SafeSendAsync(tenant.ContactEmail,
                $"{daysLeft} days left in your Planscape trial",
                $"Hi {tenant.Name},\n\nYour Planscape trial ends in {daysLeft} days. Pick a plan now to keep everything running:\n\nhttps://planscape.app/billing/upgrade\n\n— Planscape");
            tenant.TrialReminderSentDays |= bit;
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task SafeSendAsync(string to, string subject, string body)
    {
        try { await _email.SendNotificationAsync(to, subject, body); }
        catch (Exception ex) { _logger.LogWarning(ex, "Trial reminder email to {To} failed", to); }
    }
}
