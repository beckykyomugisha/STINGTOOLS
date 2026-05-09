namespace Planscape.Core.Entities;

/// <summary>
/// On-site photograph captured by a coordinator / inspector / contractor.
/// Distinct from <see cref="IssueAttachment"/> — a SitePhoto is the
/// top-level concept, and an IssueAttachment row is created alongside
/// it when the photo's <see cref="Reason"/> is one of the issue-bearing
/// kinds. The image bytes are owned by the related <see cref="DocumentRecord"/>;
/// SitePhoto stores the workflow metadata (audience, blur, watermark,
/// approval) that issue attachments don't need.
///
/// Lifecycle (Audience):
///   Internal      → never blurred, never watermarked, never client-visible
///   PendingReview → awaiting PM/Admin/Owner approval
///   Approved      → enqueued for blur+watermark worker
///   ClientPortal  → derivative is published; original stays internal
///   Withdrawn     → retracted from client portal but still audited
/// </summary>
public class SitePhoto : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>The DocumentRecord that owns the original file bytes.</summary>
    public Guid DocumentId { get; set; }

    // ── Reason ────────────────────────────────────────────────────────
    /// <summary>
    /// Six-value taxonomy. Drives default audience + auto-routing
    /// (e.g. <c>Defect</c> auto-creates an NCR issue, <c>Safety</c>
    /// pages on-call).
    /// Values: Progress | Issue | Defect | Safety | AsBuilt | Reference.
    /// </summary>
    public string Reason { get; set; } = "Reference";

    // ── Audience state machine ────────────────────────────────────────
    /// <summary>
    /// Internal | PendingReview | Approved | ClientPortal | Withdrawn.
    /// Default flips to <c>PendingReview</c> for Progress + AsBuilt
    /// captures, <c>Internal</c> for everything else, unless the
    /// classifier or the user overrides.
    /// </summary>
    public string Audience { get; set; } = "Internal";

    // ── Blur + watermark pipeline ─────────────────────────────────────
    /// <summary>
    /// NotRequired | Pending | Done | Failed.
    /// Only photos transitioning Approved → ClientPortal go through the
    /// worker. Failure leaves the row at <c>Approved</c> until an
    /// admin retries (fail-closed: better to wait than to publish a
    /// face).
    /// </summary>
    public string BlurStatus { get; set; } = "NotRequired";
    public bool   WatermarkApplied { get; set; } = false;
    /// <summary>Storage path of the redacted (blurred + watermarked) derivative,
    /// served to client-portal users. Null until the worker writes it.</summary>
    public string? RedactedFilePath { get; set; }

    // ── Caption — required to publish ─────────────────────────────────
    /// <summary>
    /// Free-text caption. Must be non-empty (≥ 3 chars after trim) for
    /// approval to succeed. Surfaces on the client portal + daily digest.
    /// </summary>
    public string? Caption { get; set; }

    // ── Capture metadata ──────────────────────────────────────────────
    public DateTime  CapturedAt        { get; set; } = DateTime.UtcNow;
    public Guid?     CapturedByUserId  { get; set; }
    public string?   DeviceId          { get; set; }
    public string?   Source            { get; set; }   // mobile | web | bcc

    public double?   Latitude          { get; set; }
    public double?   Longitude         { get; set; }
    public double?   AccuracyM         { get; set; }

    // ── Anchors (any/all may be null) ────────────────────────────────
    /// <summary>ISO 19650 level code (L01, GF, B1, RF, …).</summary>
    public string?   LevelCode         { get; set; }
    /// <summary>Project zone code (Z01, Z02, …).</summary>
    public string?   ZoneCode          { get; set; }
    public Guid?     WorkPackageId     { get; set; }
    /// <summary>Set when Reason ∈ {Issue, Defect, Safety} — links to the
    /// issue that was auto-created or chosen at capture time.</summary>
    public Guid?     AnchorIssueId     { get; set; }
    /// <summary>Set when Reason = AsBuilt (or model-snapped) — element GUID
    /// for FM / COBie hand-off.</summary>
    public string?   AnchorElementGuid { get; set; }
    /// <summary>3D anchor (model id + xyz) for viewer pin placement.</summary>
    public Guid?     ModelId           { get; set; }
    public double?   ModelX            { get; set; }
    public double?   ModelY            { get; set; }
    public double?   ModelZ            { get; set; }

    // ── Auto-classification audit ─────────────────────────────────────
    /// <summary>0..1 confidence the on-device classifier had in the
    /// chosen Reason. 0 = user-picked, no classifier ran.</summary>
    public double    ClassifierConfidence { get; set; } = 0;
    /// <summary>JSON dump of the signals that drove the classifier
    /// (geofence hit, time of day, recent action, work package). Useful
    /// for debugging mis-classifications.</summary>
    public string?   ClassifierSignals    { get; set; }

    // ── Repeat-position grouping ─────────────────────────────────────
    /// <summary>perceptual-hash-derived bucket key. Photos sharing a
    /// PairKey get auto-grouped into before/after timelines.</summary>
    public string?   PairKey { get; set; }

    // ── Lifecycle audit ──────────────────────────────────────────────
    public DateTime? ApprovedAt        { get; set; }
    public Guid?     ApprovedByUserId  { get; set; }
    public DateTime? RejectedAt        { get; set; }
    public string?   RejectedReason    { get; set; }
    public Guid?     RejectedByUserId  { get; set; }
    public DateTime? WithdrawnAt       { get; set; }
    public Guid?     WithdrawnByUserId { get; set; }

    // ── Navigation ───────────────────────────────────────────────────
    public Project?         Project        { get; set; }
    public DocumentRecord?  Document       { get; set; }
    public AppUser?         CapturedByUser { get; set; }
    public AppUser?         ApprovedByUser { get; set; }
    public BimIssue?        AnchorIssue    { get; set; }

    // ── Helpers ──────────────────────────────────────────────────────
    /// <summary>Return true when this audience is reachable to client-
    /// portal users (after blur+watermark has run).</summary>
    public bool IsClientVisible() => Audience == "ClientPortal";

    /// <summary>Return true when this Reason category should default to
    /// the review queue rather than going straight to Internal.</summary>
    public static bool DefaultToReview(string reason) =>
        reason is "Progress" or "AsBuilt";

    /// <summary>Reasons that auto-create an issue at capture time.</summary>
    public static bool CreatesIssue(string reason) =>
        reason is "Issue" or "Defect" or "Safety";

    public static readonly string[] ValidReasons     = { "Progress", "Issue", "Defect", "Safety", "AsBuilt", "Reference" };
    public static readonly string[] ValidAudiences   = { "Internal", "PendingReview", "Approved", "ClientPortal", "Withdrawn" };
    public static readonly string[] ValidBlurStates  = { "NotRequired", "Pending", "Done", "Failed" };
}
