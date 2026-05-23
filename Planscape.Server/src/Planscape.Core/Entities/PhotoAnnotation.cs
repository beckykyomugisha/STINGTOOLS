namespace Planscape.Core.Entities;

/// <summary>
/// Phase 179 — Vector markup overlay on a <see cref="SitePhoto"/>
/// (arrows, circles, polygons, text). Stored as a JSON shape array so
/// the desktop, mobile, and PDF renderers all share a single source of
/// truth for what to draw on top of the image.
///
/// One annotation row per author per photo — multiple annotators
/// (PM markup vs. inspector markup vs. surveyor markup) compose by
/// stacking rows. Order is by <see cref="CreatedAt"/>.
///
/// ShapesJson schema (informal):
///   [
///     {"kind":"arrow","x1":0.12,"y1":0.34,"x2":0.45,"y2":0.50,"color":"#E65C00","width":2},
///     {"kind":"circle","cx":0.6,"cy":0.4,"r":0.05,"color":"#C62828","width":2},
///     {"kind":"text","x":0.2,"y":0.8,"text":"Crack 0.4mm","color":"#000","size":12}
///   ]
/// All coordinates are normalised 0..1 over the photo's pixel extent so
/// the same JSON renders correctly at any resolution.
/// </summary>
public class PhotoAnnotation
{
    public Guid Id      { get; set; } = Guid.NewGuid();
    public Guid PhotoId { get; set; }

    public string ShapesJson { get; set; } = "[]";
    public string? Summary   { get; set; }

    public DateTime CreatedAt        { get; set; } = DateTime.UtcNow;
    public Guid?    CreatedByUserId  { get; set; }
    public string?  CreatedByName    { get; set; }
    public DateTime? UpdatedAt       { get; set; }

    public SitePhoto? Photo { get; set; }
    public AppUser?   CreatedByUser { get; set; }
}
