using Planscape.Core.Entities;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// Tests for entity model defaults and business rules that don't require a database.
/// </summary>
public class EntityModelTests
{
    [Fact]
    public void AppUser_DefaultRole_IsViewer()
    {
        var user = new AppUser();
        Assert.Equal(UserRole.Viewer, user.Role);
    }

    [Fact]
    public void AppUser_IsActiveByDefault()
    {
        var user = new AppUser();
        Assert.True(user.IsActive);
    }

    [Fact]
    public void AppUser_DefaultIso19650Role_IsZ()
    {
        // Z = Unassigned — default until an admin assigns a role
        var user = new AppUser();
        Assert.Equal("Z", user.Iso19650Role);
    }

    [Fact]
    public void Tenant_DefaultTier_IsStarter()
    {
        var tenant = new Tenant();
        Assert.Equal(LicenseTier.Starter, tenant.Tier);
    }

    [Fact]
    public void Tenant_MimDisabledByDefault()
    {
        var tenant = new Tenant();
        Assert.False(tenant.MimEnabled);
    }

    [Fact]
    public void Project_DefaultStatus_IsActive()
    {
        var project = new Project();
        Assert.Equal(ProjectStatus.Active, project.Status);
    }

    [Fact]
    public void Project_DefaultRagStatus_IsRed()
    {
        var project = new Project();
        Assert.Equal("RED", project.RagStatus);
    }

    [Fact]
    public void Project_DefaultTagSeparator_IsDash()
    {
        var project = new Project();
        Assert.Equal("-", project.TagSeparator);
    }

    [Fact]
    public void BimIssue_DefaultStatus_IsOpen()
    {
        var issue = new BimIssue();
        Assert.Equal("OPEN", issue.Status);
    }

    [Fact]
    public void BimIssue_DefaultPriority_IsMedium()
    {
        var issue = new BimIssue();
        Assert.Equal("MEDIUM", issue.Priority);
    }

    [Fact]
    public void LicenseKey_DefaultMaxActivations_IsOne()
    {
        var key = new LicenseKey();
        Assert.Equal(1, key.MaxActivations);
    }

    [Fact]
    public void LicenseKey_DefaultCurrentActivations_IsZero()
    {
        var key = new LicenseKey();
        Assert.Equal(0, key.CurrentActivations);
    }

    [Fact]
    public void ProjectMember_DefaultRole_IsContributor()
    {
        var member = new ProjectMember();
        Assert.Equal("Contributor", member.ProjectRole);
        Assert.Equal("M", member.Iso19650Role);
        Assert.True(member.IsActive);
    }

    [Fact]
    public void MeetingActionItem_DefaultStatus_IsOpen()
    {
        var action = new MeetingActionItem();
        Assert.Equal("OPEN", action.Status);
    }

    [Fact]
    public void TaggedElement_DefaultCompliance_IsFalse()
    {
        var el = new TaggedElement();
        Assert.False(el.IsComplete);
        Assert.False(el.IsFullyResolved);
        Assert.False(el.IsStale);
    }
}
