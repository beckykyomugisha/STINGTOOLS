namespace Planscape.Core.Entities;

/// <summary>
/// Phase 178b — T2-5. A saved 3D viewer state — camera + visibility +
/// section plane + active discipline filter — that any user can re-
/// open later to drop into the exact same coordination context.
///
/// Created two ways:
///   1. User clicks "Save view" in the viewer (manual capture).
///   2. User creates a meeting action item and the viewer state is
///      automatically attached so participants can re-open the exact
///      view later. <see cref="LinkedMeetingId"/> + <see cref="LinkedActionItemId"/>
///      record the back-link.
///
/// The <see cref="StateJson"/> blob is the viewer's own serialised
/// state — opaque to the server. Schema is owned by the viewer
/// (`coordination-viewer.js` `captureViewState()`).
/// </summary>
public class SavedView : ITenantScoped
{
    public Guid   Id        { get; set; } = Guid.NewGuid();
    public Guid   TenantId  { get; set; }
    public Guid   ProjectId { get; set; }
    public Guid?  ModelId   { get; set; }

    /// <summary>Human-readable name shown in the saved-views list.</summary>
    public string Name      { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>Opaque viewer state — camera position + target, visibility
    /// set, section planes, active disciplines, render mode, ghost flag.</summary>
    public string StateJson { get; set; } = "{}";

    /// <summary>Optional thumbnail (data: URL or storage path) the viewer
    /// captures when saving — surfaces in saved-views list previews.</summary>
    public string? ThumbnailB64 { get; set; }

    public Guid?    CapturedByUserId { get; set; }
    public string   CapturedByName   { get; set; } = "";
    public DateTime CreatedAt        { get; set; } = DateTime.UtcNow;

    /// <summary>When set, this view was auto-captured at meeting-action-
    /// item creation time so participants can re-open the discussion
    /// context.</summary>
    public Guid?  LinkedMeetingId    { get; set; }
    public Guid?  LinkedActionItemId { get; set; }

    public Project? Project       { get; set; }
    public AppUser? CapturedByUser { get; set; }
    public Meeting? LinkedMeeting  { get; set; }
}
