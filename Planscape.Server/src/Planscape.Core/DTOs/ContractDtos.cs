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
    string Severity)
{
    private static readonly System.Text.Json.JsonSerializerOptions ParseOpts =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Tolerant parse of the JSON-encoded <c>ValidationErrors</c> blob into the
    /// canonical contract shape. Returns <c>true</c> (with an empty list) for a
    /// null/blank blob — nothing to validate. Returns <c>false</c> only when the
    /// blob is present but is NOT a JSON array of objects (a plain string, a bare
    /// scalar, or malformed JSON) — i.e. a producer that hasn't converged on the
    /// <c>[{code,message,severity}]</c> shape. Behaviour-preserving: it never
    /// rewrites the blob, it only inspects it, so the wire stays untouched while
    /// the shape becomes part of the contract (full retype of the wire field is
    /// the deferred breaking cutover documented above).
    /// </summary>
    public static bool TryParse(string? json, out IReadOnlyList<ValidationErrorDto> errors)
    {
        errors = System.Array.Empty<ValidationErrorDto>();
        if (string.IsNullOrWhiteSpace(json)) return true;
        try
        {
            var parsed = System.Text.Json.JsonSerializer
                .Deserialize<List<ValidationErrorDto>>(json, ParseOpts);
            if (parsed is null) return false;
            errors = parsed;
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}

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

// ---------------------------------------------------------------------------
// Dashboard / list response DTOs for the remaining client-consumed endpoints
// where wire-shape drift occurred (Prompt 13 / Option 1). Each mirrors the
// CURRENT serialized JSON exactly — these pin the wire so OpenAPI / codegen
// has a real schema instead of an anonymous object or raw EF entity. They do
// NOT change the wire: for endpoints that returned an anonymous object the DTO
// fields are byte-identical; for endpoints that returned a raw EF entity the
// only delta is that the never-populated navigation artifacts (`project: null`,
// `documents: []`) are no longer emitted — no client reads them (the mobile
// types omit them) and the entity nav is always null/empty on these reads.
// ---------------------------------------------------------------------------

/// <summary>GET /api/projects/{id}/healthcare/dashboard — Healthcare Pack RAG roll-up.</summary>
public record HealthcareDashboardDto(
    HealthcarePressureRagDto Pressure,
    HealthcareMgasRagDto Mgas,
    HealthcareAntiLigatureRagDto AntiLigature,
    int RdsCount);

public record HealthcarePressureRagDto(int TotalLast7d, int BreachLast7d, string Rag);
public record HealthcareMgasRagDto(DateTime? Latest, bool Pass, string Rag);
public record HealthcareAntiLigatureRagDto(int TotalAudits, int Failed, string Rag);

/// <summary>GET /api/projects/{id}/penetrations/dashboard — install-progress counts.</summary>
public record PenetrationDashboardDto(
    List<PenetrationStatusCountDto> ByStatus,
    List<PenetrationHostCountDto> ByHost);

public record PenetrationStatusCountDto(string? Status, int Count);
public record PenetrationHostCountDto(string? HostType, int Count);

/// <summary>
/// GET /api/projects/{id}/compliance and /compliance/latest — wire projection of
/// the <see cref="ComplianceSnapshot"/> entity (drops only the <c>Project</c>
/// navigation). Mirrors every serialized scalar verbatim, so the JSON is
/// unchanged for existing consumers. NB: the mobile <c>ComplianceSnapshot</c>
/// type (Planscape/src/types/api.ts) is currently OUT OF SYNC with this real
/// wire shape (it uses compliancePercent/taggedElements/timestamp/byDiscipline
/// rather than tagPercent/taggedComplete/capturedAt/byDisciplineJson); that is a
/// pre-existing client drift to reconcile separately — this DTO pins the SERVER
/// truth, it does not adopt the client's (incorrect) field names.
/// </summary>
public record ComplianceSnapshotDto
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid ProjectId { get; init; }
    public string CapturedBy { get; init; } = "";
    public DateTime CapturedAt { get; init; }

    public int TotalElements { get; init; }
    public int TaggedComplete { get; init; }
    public int TaggedIncomplete { get; init; }
    public int Untagged { get; init; }
    public int FullyResolved { get; init; }
    public int StaleCount { get; init; }
    public int PlaceholderCount { get; init; }
    public int WarningCount { get; init; }
    public int WarningHealthScore { get; init; }

    public double TagPercent { get; init; }
    public double StrictPercent { get; init; }
    public double ContainerPercent { get; init; }
    public string RagStatus { get; init; } = "RED";

    public string? ByDisciplineJson { get; init; }
    public string? ByPhaseJson { get; init; }
    public string? EmptyTokenCountsJson { get; init; }

    public static ComplianceSnapshotDto From(ComplianceSnapshot s) => new()
    {
        Id = s.Id,
        TenantId = s.TenantId,
        ProjectId = s.ProjectId,
        CapturedBy = s.CapturedBy,
        CapturedAt = s.CapturedAt,
        TotalElements = s.TotalElements,
        TaggedComplete = s.TaggedComplete,
        TaggedIncomplete = s.TaggedIncomplete,
        Untagged = s.Untagged,
        FullyResolved = s.FullyResolved,
        StaleCount = s.StaleCount,
        PlaceholderCount = s.PlaceholderCount,
        WarningCount = s.WarningCount,
        WarningHealthScore = s.WarningHealthScore,
        TagPercent = s.TagPercent,
        StrictPercent = s.StrictPercent,
        ContainerPercent = s.ContainerPercent,
        RagStatus = s.RagStatus,
        ByDisciplineJson = s.ByDisciplineJson,
        ByPhaseJson = s.ByPhaseJson,
        EmptyTokenCountsJson = s.EmptyTokenCountsJson,
    };
}

/// <summary>
/// GET /api/projects/{id}/transmittals — paged envelope. The wire is the
/// envelope object <c>{ transmittals, total, page, pageSize }</c>; this DTO pins
/// it. NB: the mobile <c>listTransmittals</c> wrapper currently types the
/// response as <c>Transmittal[]</c> (a bare array) — a pre-existing client drift
/// against this envelope; matched here to the SERVER truth, not "fixed".
/// </summary>
public record TransmittalsPageDto(
    List<TransmittalDto> Transmittals,
    int Total,
    int Page,
    int PageSize);

/// <summary>Wire projection of <see cref="Transmittal"/> (drops the <c>Project</c>
/// and <c>Documents</c> navigations — never populated on the list read).</summary>
public record TransmittalDto
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid ProjectId { get; init; }
    public string TransmittalCode { get; init; } = "";
    public string Recipient { get; init; } = "";
    public string Status { get; init; } = "DRAFT";
    public string? Notes { get; init; }
    public string? DocumentIdsJson { get; init; }
    public string CreatedBy { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime? SentAt { get; init; }
    public Guid? RecipientUserId { get; init; }
    public Guid? SenderUserId { get; init; }
    public DateTime? SlaDeadline { get; init; }
    public DateTime? AcknowledgedAt { get; init; }
    public string? AcknowledgedBy { get; init; }
    public DateTime? RespondedAt { get; init; }
    public string? RespondedBy { get; init; }
    public string? ResponseNotes { get; init; }

    public static TransmittalDto From(Transmittal t) => new()
    {
        Id = t.Id,
        TenantId = t.TenantId,
        ProjectId = t.ProjectId,
        TransmittalCode = t.TransmittalCode,
        Recipient = t.Recipient,
        Status = t.Status,
        Notes = t.Notes,
        DocumentIdsJson = t.DocumentIdsJson,
        CreatedBy = t.CreatedBy,
        CreatedAt = t.CreatedAt,
        SentAt = t.SentAt,
        RecipientUserId = t.RecipientUserId,
        SenderUserId = t.SenderUserId,
        SlaDeadline = t.SlaDeadline,
        AcknowledgedAt = t.AcknowledgedAt,
        AcknowledgedBy = t.AcknowledgedBy,
        RespondedAt = t.RespondedAt,
        RespondedBy = t.RespondedBy,
        ResponseNotes = t.ResponseNotes,
    };
}
