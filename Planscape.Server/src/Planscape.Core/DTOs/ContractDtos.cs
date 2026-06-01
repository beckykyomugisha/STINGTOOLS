using Planscape.Core.Entities;

namespace Planscape.Core.DTOs;

/// <summary>
/// Declared shape of one entry inside <see cref="TaggedElementDto.ValidationErrors"/>
/// (which is transported as a JSON-encoded array of these on the wire).
///
/// Drift-5/contract note: hosts currently write divergent shapes into the
/// underlying <c>TaggedElement.ValidationErrors</c> string (Bonsai emits
/// rule/segment/message objects; the Revit plugin emits plain strings). This
/// record is the CANONICAL target every producer should converge on so the
/// blob stops being convention-only. Fully retyping the wire field from
/// <c>string</c> to <c>ValidationErrorDto[]</c> is a coordinated breaking
/// change (the mobile client currently types it as a string — see
/// Planscape/src/types/api.ts) and is intentionally deferred; this declares
/// the shape in the generated contract without forcing that cutover yet.
/// </summary>
public record ValidationErrorDto(
    string Code,
    string Message,
    string Severity);

/// <summary>
/// Wire projection of <see cref="TaggedElement"/> — the precise, generation-
/// friendly response type for the element-search / list endpoints
/// (GET /api/tagsync/elements/search). Mirrors every serialized scalar of the
/// entity so the JSON is unchanged for existing consumers, while giving OpenAPI
/// a clean schema instead of the raw EF entity (which leaks the <c>Project</c>
/// navigation and internal columns).
///
/// CROSS-HOST KEY CONTRACT (Drift 4/5): for cross-host element identity the
/// canonical key is the IFC GlobalId, exposed by the ifc endpoints as
/// <c>ifcGlobalId</c> (see <see cref="IfcElementDto"/> + ExternalElementMapping).
/// The <see cref="UniqueId"/> here is the Revit-side host id and MUST NOT be
/// treated as the cross-host key. See docs/element-ingest-paths.md.
///
/// <c>Lvl</c> is the ISO 19650 level CODE; <c>Level</c> is the human-readable
/// level NAME — distinct fields, do not conflate (the mobile drift fixed in
/// Planscape/src/types/api.ts hinged on this).
/// </summary>
public record TaggedElementDto
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid ProjectId { get; init; }
    public long RevitElementId { get; init; }
    public string UniqueId { get; init; } = "";

    // 8 source tokens
    public string Disc { get; init; } = "";
    public string Loc { get; init; } = "";
    public string Zone { get; init; } = "";
    public string Lvl { get; init; } = "";   // level CODE
    public string Sys { get; init; } = "";
    public string Func { get; init; } = "";
    public string Prod { get; init; } = "";
    public string Seq { get; init; } = "";

    // Assembled tags
    public string Tag1 { get; init; } = "";
    public string? Tag7 { get; init; }
    public string? Tag7A { get; init; }
    public string? Tag7B { get; init; }
    public string? Tag7C { get; init; }
    public string? Tag7D { get; init; }
    public string? Tag7E { get; init; }
    public string? Tag7F { get; init; }

    // Context
    public string CategoryName { get; init; } = "";
    public string FamilyName { get; init; } = "";
    public string TypeName { get; init; } = "";
    public string? Status { get; init; }
    public string? Rev { get; init; }
    public string? GridRef { get; init; }
    public string? RoomName { get; init; }
    public string? Level { get; init; }   // level NAME (distinct from Lvl)

    // Compliance
    public bool IsStale { get; init; }
    public bool IsComplete { get; init; }
    public bool IsFullyResolved { get; init; }

    /// <summary>JSON-encoded array of <see cref="ValidationErrorDto"/>.</summary>
    public string? ValidationErrors { get; init; }

    // Audit / sync
    public string? PreviousTag { get; init; }
    public DateTime? TagModifiedAt { get; init; }
    public DateTime SyncedAt { get; init; }
    public string SyncedBy { get; init; } = "";
    public string? Source { get; init; }
    public DateTime? LastModifiedUtc { get; init; }
    public int Version { get; init; }

    // P6 live-link
    public string? P6ActivityId { get; init; }
    public double? PercentComplete { get; init; }
    public string? ActualStart { get; init; }
    public string? ActualFinish { get; init; }

    /// <summary>Project the EF entity to the wire DTO (drops only the
    /// <c>Project</c> navigation — every serialized scalar is preserved, so
    /// the JSON is byte-equivalent for existing consumers).</summary>
    public static TaggedElementDto From(TaggedElement e) => new()
    {
        Id = e.Id,
        TenantId = e.TenantId,
        ProjectId = e.ProjectId,
        RevitElementId = e.RevitElementId,
        UniqueId = e.UniqueId,
        Disc = e.Disc,
        Loc = e.Loc,
        Zone = e.Zone,
        Lvl = e.Lvl,
        Sys = e.Sys,
        Func = e.Func,
        Prod = e.Prod,
        Seq = e.Seq,
        Tag1 = e.Tag1,
        Tag7 = e.Tag7,
        Tag7A = e.Tag7A,
        Tag7B = e.Tag7B,
        Tag7C = e.Tag7C,
        Tag7D = e.Tag7D,
        Tag7E = e.Tag7E,
        Tag7F = e.Tag7F,
        CategoryName = e.CategoryName,
        FamilyName = e.FamilyName,
        TypeName = e.TypeName,
        Status = e.Status,
        Rev = e.Rev,
        GridRef = e.GridRef,
        RoomName = e.RoomName,
        Level = e.Level,
        IsStale = e.IsStale,
        IsComplete = e.IsComplete,
        IsFullyResolved = e.IsFullyResolved,
        ValidationErrors = e.ValidationErrors,
        PreviousTag = e.PreviousTag,
        TagModifiedAt = e.TagModifiedAt,
        SyncedAt = e.SyncedAt,
        SyncedBy = e.SyncedBy,
        Source = e.Source,
        LastModifiedUtc = e.LastModifiedUtc,
        Version = e.Version,
        P6ActivityId = e.P6ActivityId,
        PercentComplete = e.PercentComplete,
        ActualStart = e.ActualStart,
        ActualFinish = e.ActualFinish,
    };
}
