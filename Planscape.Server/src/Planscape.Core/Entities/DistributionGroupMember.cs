namespace Planscape.Core.Entities;

/// <summary>
/// Member of a <see cref="DistributionGroup"/>. Either a registered
/// AppUser (<see cref="UserId"/> set) or an external email recipient
/// (<see cref="ExternalEmail"/> set). Mutually exclusive — exactly one
/// must be non-null at the controller level.
/// </summary>
public class DistributionGroupMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DistributionGroupId { get; set; }

    public Guid?   UserId        { get; set; }
    public string? ExternalEmail { get; set; }
    public string? DisplayName   { get; set; }

    /// <summary>Optional discipline filter — member only receives photos matching this discipline.</summary>
    public string? DisciplineFilter { get; set; }

    public DateTime AddedAt        { get; set; } = DateTime.UtcNow;
    public Guid?    AddedByUserId  { get; set; }

    public DistributionGroup? DistributionGroup { get; set; }
    public AppUser? User { get; set; }
}
