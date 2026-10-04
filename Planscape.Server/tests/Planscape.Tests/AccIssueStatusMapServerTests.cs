using Planscape.Infrastructure.Services;
using Planscape.Shared.Helpers;

namespace Planscape.Tests;

/// <summary>
/// KUT deep review INT-12 — the status the server creates an ACC issue with. It used to send
/// "answered" for RESOLVED (a BIM 360 value, not in the Issues v1 enum) and "open" for any
/// status it did not recognise. Source of the enum: components.schemas.status in
/// https://github.com/autodesk-platform-services/aps-sdk-openapi/blob/main/construction/issues/Issues.yaml
/// </summary>
public class AccIssueStatusMapServerTests
{
    private static readonly string[] IssuesV1 =
        { "draft", "open", "pending", "in_progress", "in_review", "completed", "not_approved", "in_dispute", "closed" };

    [Theory]
    [InlineData("OPEN")]
    [InlineData("IN_PROGRESS")]
    [InlineData("RESOLVED")]
    [InlineData("CLOSED")]
    [InlineData("VOID")]
    [InlineData("SOMETHING_NEW")]
    public void The_server_only_ever_sends_an_Issues_v1_status(string planscapeStatus)
    {
        string? s = AccSyncService.MapStatus(planscapeStatus);
        Assert.True(s == null || IssuesV1.Contains(s), $"'{planscapeStatus}' -> '{s}'");
    }

    [Theory]
    [InlineData("RESOLVED")]
    [InlineData("CLOSED")]
    [InlineData("VOID")]
    [InlineData("SOMETHING_NEW")]
    public void A_status_that_is_not_open_is_not_pushed(string planscapeStatus)
        => Assert.Null(AccSyncService.MapStatus(planscapeStatus));

    [Theory]
    [InlineData("OPEN")]
    [InlineData("IN_PROGRESS")]
    public void An_open_issue_is_created_open(string planscapeStatus)
        => Assert.Equal("open", AccSyncService.MapStatus(planscapeStatus));

    [Fact]
    public void The_server_and_the_plugin_share_one_vocabulary()
        => Assert.Equal(IssuesV1, AccIssueStatusMap.AccStatuses.ToArray());
}
