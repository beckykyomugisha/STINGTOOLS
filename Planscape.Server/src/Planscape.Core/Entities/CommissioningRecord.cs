namespace Planscape.Core.Entities;

/// <summary>
/// One commissioning STEP for one element — an append-only event, not a mutable
/// current-state row.
///
/// WHY APPEND-ONLY
/// ---------------
/// Commissioning is a record of what happened on site. The desktop already treats
/// it that way: <c>QRCommissioningWorkflow</c> writes the current state onto the
/// element AND appends to <c>STING_Commissioning_Audit.json</c>, and the state
/// machine refuses to regress precisely so the history cannot be rewritten.
///
/// A single mutable row would keep the answer to "what state is it in" and destroy
/// the answer to "who signed it off, when, and who witnessed" — which is the half
/// that gets asked in a dispute, years later. So every advance inserts a row, and
/// the current state is the newest row's <see cref="ToState"/>.
///
/// KEYED BY UniqueId, DELIBERATELY
/// -------------------------------
/// <see cref="ElementUniqueId"/> is the Revit UniqueId, which is what the QR
/// payload carries (<c>?u=</c>) and what the desktop resolves by. It is a HOST id,
/// not the cross-host IFC GlobalId — see ContractDtos.cs. <see cref="ElementTag"/>
/// is recorded alongside it because a printed label carries the tag, and a person
/// reading this table later needs to recognise the asset without a Revit session.
/// </summary>
public class CommissioningRecord : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>Revit UniqueId of the element. The key the ladder advances on.</summary>
    public string ElementUniqueId { get; set; } = "";

    /// <summary>ISO 19650 tag (ASS_TAG_1_TXT) at the time of the step, when the
    /// caller knew it. Denormalised on purpose: the tag is what is printed on the
    /// asset, and this row has to stay readable after the model has moved on.</summary>
    public string? ElementTag { get; set; }

    /// <summary>Human label for the element at the time of the step.</summary>
    public string? ElementName { get; set; }

    /// <summary>State before this step. "NOT_STARTED" for the first.</summary>
    public string FromState { get; set; } = "NOT_STARTED";

    /// <summary>State after this step. The newest row's value IS the current state.</summary>
    public string ToState { get; set; } = "";

    /// <summary>Who performed it. Never empty — the state machine refuses an
    /// unattributed step.</summary>
    public string Operative { get; set; } = "";

    /// <summary>Who witnessed it. Required by the state machine for COMMISSIONED.</summary>
    public string? Witness { get; set; }

    public string? Notes { get; set; }

    /// <summary>Where the step came from: "mobile-scan", "desktop", "api". Recorded
    /// so a later audit can tell a scanned sign-off on site from one typed at a
    /// desk — they carry different weight.</summary>
    public string Source { get; set; } = "api";

    /// <summary>Server time the row was written. NOT client time: a phone's clock is
    /// not evidence, and an offline queue can replay hours later.</summary>
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Client-reported time of the step, when supplied. May differ from
    /// <see cref="RecordedAt"/> for a queued offline scan. Advisory only.</summary>
    public DateTime? OccurredAt { get; set; }

    /// <summary>The user account that made the call, as opposed to the operative
    /// NAME typed into the form. The two are usually the same person and are not
    /// required to be — a supervisor may record a fitter's step.</summary>
    public string? RecordedByUserId { get; set; }

    public Project? Project { get; set; }
}
