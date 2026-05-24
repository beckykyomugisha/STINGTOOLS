namespace Planscape.Core.Entities;

/// <summary>
/// Single row of a <see cref="PhotoChecklist"/>. Each item names ONE shot
/// (e.g. "North elevation pre-pour"); the coordinator fulfils it by
/// linking a <see cref="SitePhoto"/> via <see cref="FulfilledByPhotoId"/>.
/// Items can be re-fulfilled (better shot taken) — old links audit-log
/// the supersede.
/// </summary>
public class PhotoChecklistItem
{
    public Guid Id          { get; set; } = Guid.NewGuid();
    public Guid ChecklistId { get; set; }

    public string Title       { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>Zero-based render order on mobile + BCC.</summary>
    public int    SortOrder   { get; set; } = 0;

    /// <summary>Six-reason taxonomy default (so an item that demands a Defect shot doesn't auto-tag Reference).</summary>
    public string DefaultReason { get; set; } = "Reference";

    public bool   IsRequired    { get; set; } = true;
    public bool   IsWaived      { get; set; } = false;
    public string? WaivedReason { get; set; }

    /// <summary>Linked photo that satisfies this item; null until fulfilment.</summary>
    public Guid?    FulfilledByPhotoId { get; set; }
    public DateTime? FulfilledAt        { get; set; }
    public Guid?     FulfilledByUserId  { get; set; }

    public PhotoChecklist? Checklist { get; set; }
    public SitePhoto?      FulfilledByPhoto { get; set; }
}
