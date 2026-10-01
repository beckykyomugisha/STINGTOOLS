using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planscape.API.Services;
using Planscape.Infrastructure.Data;
using Xunit;

namespace Planscape.Tests;

// C3: the plugin now sends X-Idempotency-Key on every issue create, so a create retried
// after a client timeout resolves to the first issue instead of minting a second one that
// AccSyncService would also push to ACC. This pins the server half the plugin relies on.
public class IssueCreateIdempotencyTests
{
    // The real host runs as the request's tenant; without the tenant double the global filter
    // reads Guid.Empty and hides every record (proved: both tests fail without it).
    private static (ServiceProvider sp, PlanscapeDbContext db) NewDb(Guid tenant)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenantContextDouble();
        services.AddDbContext<PlanscapeDbContext>(o => o.UseInMemoryDatabase("idem-" + Guid.NewGuid()));
        var sp = services.BuildServiceProvider();
        sp.UseTenant(tenant);
        return (sp, sp.GetRequiredService<PlanscapeDbContext>());
    }

    [Fact]
    public async Task ARecordedKey_ResolvesToTheFirstIssue_ForThatTenantOnly()
    {
        var tenant = Guid.NewGuid();
        var (sp, db) = NewDb(tenant);
        var issue = Guid.NewGuid();
        const string key = "sting-issue-create:11111111222233334444555555555555:ISS-0042";

        Assert.Null(await IdempotencyGuard.SeenResultAsync(db, tenant, "issue.create", key));   // new request
        await IdempotencyGuard.RecordAsync(db, tenant, "issue.create", key, issue);

        Assert.Equal(issue, await IdempotencyGuard.SeenResultAsync(db, tenant, "issue.create", key));   // the replay
        Assert.Null(await IdempotencyGuard.SeenResultAsync(db, tenant, "issue.create", key + "x"));
        Assert.Null(await IdempotencyGuard.SeenResultAsync(db, tenant, "issue.update", key));          // other scope
        var other = Guid.NewGuid();
        sp.UseTenant(other);
        Assert.Null(await IdempotencyGuard.SeenResultAsync(db, other, "issue.create", key));           // other tenant
    }

    [Fact]
    public async Task RecordingTheSameKeyTwice_IsBenign()
    {
        var tenant = Guid.NewGuid();
        var (_, db) = NewDb(tenant);
        var first = Guid.NewGuid();
        await IdempotencyGuard.RecordAsync(db, tenant, "issue.create", "k", first);
        await IdempotencyGuard.RecordAsync(db, tenant, "issue.create", "k", Guid.NewGuid());
        Assert.Equal(first, await IdempotencyGuard.SeenResultAsync(db, tenant, "issue.create", "k"));
    }
}
