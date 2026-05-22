namespace Planscape.Core.Entities;

/// <summary>
/// Many-to-many join between <see cref="PhotoAlbum"/> and
/// <see cref="SitePhoto"/>. Carries a <see cref="SortOrder"/> so
/// curators can lay photos out in narrative sequence
/// (handover bundle, week-in-review, etc).
/// </summary>
public class PhotoAlbumPhoto
{
    public Guid AlbumId  { get; set; }
    public Guid PhotoId  { get; set; }

    /// <summary>Lower numbers render first. Defaults to insertion order × 100 so future inserts can slip between.</summary>
    public int  SortOrder { get; set; } = 0;

    public DateTime AddedAt        { get; set; } = DateTime.UtcNow;
    public Guid?    AddedByUserId  { get; set; }

    public PhotoAlbum? Album { get; set; }
    public SitePhoto?  Photo { get; set; }
}
