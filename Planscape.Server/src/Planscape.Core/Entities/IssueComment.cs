namespace Planscape.Core.Entities;

/// <summary>
/// P2 — Comment / discussion thread on a <see cref="BimIssue"/>. Designed for
/// lightweight back-and-forth between site + office without a new domain
/// (e.g. "Got a photo from the other side?" / "Checking — hang on").
///
/// v1 stores plain text + optional attachment reference. Reactions + rich
/// formatting can be added later without a schema break (the Attachments
/// join gets the shared IssueAttachment model).
/// </summary>
public class IssueComment : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid IssueId { get; set; }
    public Guid? AuthorUserId { get; set; }

    /// <summary>Freeform text. Max length enforced at 4000 chars in DbContext.</summary>
    public string Body { get; set; } = "";

    /// <summary>Display name captured at write time — avoids the N+1 join for read.</summary>
    public string AuthorName { get; set; } = "";

    /// <summary>Auto-set to "mobile" / "plugin" / "web" for audit filters.</summary>
    public string? Source { get; set; }

    /// <summary>
    /// Optional "mention" — when populated we push the mentioned user a push +
    /// mark the comment with a chip. Separate from the issue's assignee since
    /// comments can tag people who aren't the current assignee.
    /// </summary>
    public Guid? MentionedUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EditedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public BimIssue? Issue { get; set; }
    public AppUser? AuthorUser { get; set; }
}
