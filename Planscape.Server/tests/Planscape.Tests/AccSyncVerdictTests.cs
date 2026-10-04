using Planscape.Infrastructure.Services;

namespace Planscape.Tests;

/// <summary>
/// KUT deep review INT-8 — the server's ACC issue-push verdict. Every run used to report
/// Success = true, so a run in which every push failed recorded LastSyncStatus "OK" and the
/// scheduled sweep counted the connection healthy; and a failed open-issue count read as 0.
/// </summary>
public class AccSyncVerdictTests
{
    [Fact]
    public void A_run_with_failed_pushes_is_not_a_success()
    {
        var r = AccSyncService.Summarise(pushed: 0, skipped: 3, pulledOpen: 12, failed: 5);
        Assert.False(r.Success);
        Assert.Equal("FAILED", AccSyncService.StatusFor(r));
        Assert.Contains("5 issue push(es) failed", r.Error);
    }

    [Fact]
    public void Some_pushed_some_failed_is_partial()
    {
        var r = AccSyncService.Summarise(pushed: 2, skipped: 0, pulledOpen: null, failed: 1);
        Assert.Equal("PARTIAL", AccSyncService.StatusFor(r));
    }

    [Fact]
    public void A_clean_run_is_ok()
    {
        var r = AccSyncService.Summarise(pushed: 4, skipped: 1, pulledOpen: 7, failed: 0);
        Assert.True(r.Success);
        Assert.Equal("OK", AccSyncService.StatusFor(r));
        Assert.Null(r.Error);
    }

    [Fact]
    public void An_unknown_open_count_is_not_zero()
    {
        var r = AccSyncService.Summarise(0, 0, pulledOpen: null, failed: 0);
        Assert.Null(r.PulledOpen);
    }
}
