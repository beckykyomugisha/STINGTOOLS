// dm.version.added and docs.approval.completed: what the handlers match and decide.
//
// Before: FindByUrn substring-matched the event's VERSION urn against what was recorded
// at upload (an earlier version or the lineage), so a new version never matched and every
// event was logged as "no DocumentRecord" and skipped. And every approval completion
// published the document — including a rejected one.

using System.Linq;
using System.Text.Json;
using Planscape.API.Controllers;
using Xunit;

namespace Planscape.Tests;

public class AccWebhookMatchingTests
{
    [Fact]
    public void A_new_version_urn_yields_the_lineage_of_the_same_file()
    {
        var c = AutodeskWebhooksController.UrnCandidates("urn:adsk.wipprod:fs.file:vf.AbC123?version=3", "");
        Assert.Contains("urn:adsk.wipprod:dm.lineage:AbC123", c);   // what an upload records for the item
        Assert.Contains("urn:adsk.wipprod:fs.file:vf.AbC123", c);   // any version of the same file
        Assert.Contains("urn:adsk.wipprod:fs.file:vf.AbC123?version=3", c);
    }

    [Fact]
    public void The_payload_lineage_is_tried_first()
    {
        var c = AutodeskWebhooksController.UrnCandidates("urn:x?version=2", "urn:adsk.wipprod:dm.lineage:L1");
        Assert.Equal("urn:adsk.wipprod:dm.lineage:L1", c.First());
    }

    [Theory]
    [InlineData("{\"payload\":{\"approvalStatus\":\"APPROVED\"}}", "approved")]
    [InlineData("{\"payload\":{\"status\":\"approved_with_comments\"}}", "approved")]
    [InlineData("{\"payload\":{\"approvalStatus\":\"REJECTED\"}}", "rejected")]
    [InlineData("{\"payload\":{\"result\":\"not_approved\"}}", "rejected")]
    [InlineData("{\"payload\":{\"approvalStatus\":{\"label\":\"Approved\"}}}", "approved")]
    [InlineData("{\"payload\":{}}", "unknown")]
    [InlineData("{\"payload\":{\"status\":\"completed\"}}", "unknown")]
    public void Only_an_explicit_approval_publishes(string json, string expected)
    {
        using var d = JsonDocument.Parse(json);
        Assert.Equal(expected, AutodeskWebhooksController.ApprovalOutcome(d.RootElement));
    }
}
