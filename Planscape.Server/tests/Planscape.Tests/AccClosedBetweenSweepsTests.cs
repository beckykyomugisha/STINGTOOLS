using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Services;
using Xunit;

namespace Planscape.Tests;

/// <summary>ACC-SRV-11: an issue raised and closed in Planscape between two sweeps.</summary>
public partial class AccServerIntegrationTests
{
    private static string ConfigSince(DateTime since, string? mode = null)
    {
        var o = new JObject
        {
            ["accIssueSubtypeId"] = "sub-1",
            [AccSyncService.KeyIssueSyncSince] = since.ToString("o"),
        };
        if (mode != null) o[AccSyncService.KeyClosedBetweenSweeps] = mode;
        return o.ToString();
    }

    private static async Task AddClosedAsync(Fx fx, string code, DateTime created, string status = "CLOSED")
    {
        using var seed = fx.Db();
        seed.Issues.Add(new BimIssue
        {
            TenantId = fx.TenantId, ProjectId = fx.ProjectId, IssueCode = code, Title = code,
            Status = status, CreatedAt = created, UpdatedAt = created,
        });
        await seed.SaveChangesAsync();
    }

    private static int Posts(Fx fx) => fx.Http.Calls.Count(c => c.Method == HttpMethod.Post && c.Url.EndsWith("/issues"));

    [Fact]
    public async Task The_first_sync_records_a_baseline_and_never_backfills_closed_history()
    {
        var fx = new Fx();
        await fx.SeedAsync(configJson: "{\"accIssueSubtypeId\":\"sub-1\",\"accClosedBetweenSweeps\":\"create\"}", openIssues: 0);
        await AddClosedAsync(fx, "RFI-OLD", DateTime.UtcNow.AddDays(-30));
        StubAcc(fx.Http, okPosts: 10);
        using var db = fx.Db();
        var r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);

        Assert.Equal(0, Posts(fx));
        Assert.Equal(0, r.ClosedNotSent);
        var cfg = JObject.Parse((await fx.ReadConnAsync()).ConfigJson!);
        Assert.NotNull(cfg[AccSyncService.KeyIssueSyncSince]);
    }

    [Fact]
    public async Task By_default_a_closed_between_sweeps_issue_is_reported_once_and_not_sent()
    {
        var fx = new Fx();
        var since = DateTime.UtcNow.AddHours(-2);
        await fx.SeedAsync(configJson: ConfigSince(since), openIssues: 0);
        await AddClosedAsync(fx, "RFI-NEW", DateTime.UtcNow.AddHours(-1), status: "RESOLVED");
        await AddClosedAsync(fx, "RFI-OLDER", since.AddDays(-1));         // before the baseline: history
        StubAcc(fx.Http, okPosts: 10);

        AccSyncService.AccSyncReport r;
        using (var db = fx.Db()) r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);
        Assert.Equal(0, Posts(fx));
        Assert.Equal(1, r.ClosedNotSent);
        Assert.Equal(AccSyncService.StatusOk, r.Status);                 // nothing failed; it is said, not hidden
        Assert.Contains("RFI-NEW", r.Error);
        Assert.DoesNotContain("RFI-OLDER", r.Error);

        using (var db = fx.Db()) r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);
        Assert.Equal(0, r.ClosedNotSent);                                // reported once
        Assert.Equal(0, Posts(fx));
    }

    [Fact]
    public async Task In_create_mode_it_is_created_in_ACC_closed_and_mapped_once()
    {
        var fx = new Fx();
        await fx.SeedAsync(configJson: ConfigSince(DateTime.UtcNow.AddHours(-2), "create"), openIssues: 0);
        await AddClosedAsync(fx, "RFI-NEW", DateTime.UtcNow.AddHours(-1));
        StubAcc(fx.Http, okPosts: 10);

        AccSyncService.AccSyncReport r;
        using (var db = fx.Db()) r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);
        Assert.Equal(1, r.Pushed);
        var post = Assert.Single(fx.Http.Calls, c => c.Method == HttpMethod.Post && c.Url.EndsWith("/issues"));
        Assert.Equal("closed", (string?)JObject.Parse(post.Body!)["status"]);
        var map = (JObject)JObject.Parse((await fx.ReadConnAsync()).ConfigJson!)[AccSyncService.KeyIssueMap]!;
        Assert.Single(map.Properties());

        using (var db = fx.Db()) r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);
        Assert.Equal(1, Posts(fx));                                      // never created twice
    }

    [Fact]
    public async Task A_rejected_closed_create_is_a_failure_not_a_silent_skip()
    {
        var fx = new Fx();
        await fx.SeedAsync(configJson: ConfigSince(DateTime.UtcNow.AddHours(-2), "create"), openIssues: 0);
        await AddClosedAsync(fx, "RFI-NEW", DateTime.UtcNow.AddHours(-1));
        StubAcc(fx.Http, okPosts: 0);
        using var db = fx.Db();
        var r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);

        Assert.Equal(1, r.Failed);
        Assert.Equal(AccSyncService.StatusFailed, r.Status);
        Assert.Contains(r.Failures!, f => f.Contains("RFI-NEW") && f.Contains("between sweeps"));
    }

    [Theory]
    [InlineData(null, AccClosedSweepPolicy.Mode.Report, false)]
    [InlineData("", AccClosedSweepPolicy.Mode.Report, false)]
    [InlineData("report", AccClosedSweepPolicy.Mode.Report, false)]
    [InlineData(" Create ", AccClosedSweepPolicy.Mode.Create, false)]
    [InlineData("always", AccClosedSweepPolicy.Mode.Report, true)]       // unknown: the choice that sends nothing, and said
    public void The_policy_parses_and_an_unknown_value_is_reported(string? value, AccClosedSweepPolicy.Mode expected, bool hasError)
    {
        var mode = AccClosedSweepPolicy.Parse(value, out var error);
        Assert.Equal(expected, mode);
        Assert.Equal(hasError, error != null);
    }

    [Fact]
    public void The_baseline_and_the_reported_set_cannot_be_overwritten_by_a_client()
    {
        Assert.Contains(AccSyncService.KeyIssueSyncSince, AccSyncService.ServerOwnedConfigKeys);
        Assert.Contains(AccSyncService.KeyIssueClosedReported, AccSyncService.ServerOwnedConfigKeys);
        Assert.DoesNotContain(AccSyncService.KeyClosedBetweenSweeps, AccSyncService.ServerOwnedConfigKeys);
    }
}
