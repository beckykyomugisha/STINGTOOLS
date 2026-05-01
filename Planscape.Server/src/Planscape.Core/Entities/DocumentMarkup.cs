namespace Planscape.Core.Entities;

/// <summary>
/// P3 — Drawing / document markup overlay. v1 stores a vector layer as JSON
/// (array of shape objects — rectangles, circles, freehand paths, text
/// anchors, clouds). The mobile renderer + web canvas both understand the
/// same JSON shape so round-trip is lossless.
///
/// v1 supports one markup layer per document per reviewer; subsequent versions
/// chain via <see cref="PreviousMarkupId"/> so we can render a blame-style
/// history ("who drew this cloud?").
/// </summary>
public class DocumentMarkup : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid? PreviousMarkupId { get; set; }

    /// <summary>JSON array of shapes — see `DocumentMarkup.schema.json` for the shape catalogue.</summary>
    public string ShapesJson { get; set; } = "[]";

    /// <summary>Which page/sheet this markup sits on (1-based; single-page docs use 1).</summary>
    public int PageNumber { get; set; } = 1;

    /// <summary>Free-text reviewer note that accompanies the markup.</summary>
    public string? Summary { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public DocumentRecord? Document { get; set; }
    public AppUser? CreatedByUser { get; set; }
}
