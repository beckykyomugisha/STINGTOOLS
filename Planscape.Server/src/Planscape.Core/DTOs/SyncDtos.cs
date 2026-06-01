namespace Planscape.Core.DTOs;

/// <summary>
/// Payload sent by the Revit plugin to sync tagged elements to the server.
/// </summary>
public record TagSyncRequest
{
    public Guid ProjectId { get; init; }
    public string UserName { get; init; } = "";
    public string RevitVersion { get; init; } = "";
    public string PluginVersion { get; init; } = "";

    /// <summary>
    /// Host plugin identifier (revit | blender | archicad | tekla). Defaults
    /// to "revit" — this endpoint has historically only been called by the
    /// Revit plugin. Attributes the cross-host
    /// <see cref="Planscape.Core.Entities.ExternalElementMapping"/> rows the
    /// post-sync ingest writes.
    /// </summary>
    public string Host { get; init; } = "revit";

    /// <summary>
    /// Host-side document GUID (Revit RVT GUID). Distinguishes the same IFC
    /// GlobalId across federated documents in the identity table. Nullable —
    /// older plugin builds omit it.
    /// </summary>
    public string? HostDocumentGuid { get; init; }

    public List<TagElementDto> Elements { get; init; } = new();
}

public record TagElementDto
{
    public long RevitElementId { get; init; }
    public string UniqueId { get; init; } = "";

    /// <summary>
    /// True IFC GlobalId (22-char) of the element, from its IFC_GLOBAL_ID_TXT
    /// shared parameter (Revit's IfcGloballyUniqueId, stabilised by the plugin's
    /// StabilizeIfcGuidsCommand). This — NOT <see cref="UniqueId"/> (Revit's
    /// 45-char UniqueId) — is the canonical cross-host key, equal to the
    /// IfcGlobalId Bonsai/ArchiCAD send for the same element. The TagSync mapping
    /// upsert keys <see cref="Planscape.Core.Entities.ExternalElementMapping"/> on
    /// this. Nullable: empty until the element is stabilised + exported, in which
    /// case no mapping row is written.
    /// </summary>
    public string? IfcGlobalId { get; init; }

    public string Disc { get; init; } = "";
    public string Loc { get; init; } = "";
    public string Zone { get; init; } = "";
    public string Lvl { get; init; } = "";
    public string Sys { get; init; } = "";
    public string Func { get; init; } = "";
    public string Prod { get; init; } = "";
    public string Seq { get; init; } = "";
    public string Tag1 { get; init; } = "";
    public string? Tag7 { get; init; }
    public string CategoryName { get; init; } = "";
    public string FamilyName { get; init; } = "";
    public string? Status { get; init; }
    public string? Rev { get; init; }
    public bool IsComplete { get; init; }
    public bool IsFullyResolved { get; init; }
    /// <summary>
    /// Client-supplied wall-clock timestamp of the most recent modification.
    /// Used by the server to detect stale updates (older than the stored value).
    /// Nullable for backward compatibility with older plugin builds.
    /// </summary>
    public DateTime? LastModifiedUtc { get; init; }
}

public record TagSyncResponse
{
    public int Received { get; init; }
    public int Created { get; init; }
    public int Updated { get; init; }
    public double CompliancePercent { get; init; }
    public string RagStatus { get; init; } = "";
    public DateTime SyncedAt { get; init; } = DateTime.UtcNow;
    /// <summary>
    /// Conflicts detected where the client sent a LastModifiedUtc older than
    /// the server's stored value. The server retains its own copy
    /// (SERVER_WINS) and reports each rejected element here so the client can
    /// merge or prompt the user.
    /// </summary>
    public List<SyncConflictDto> Conflicts { get; init; } = new();
}

public record SyncConflictDto
{
    public string ElementId { get; init; } = "";
    public DateTime? ServerTimestamp { get; init; }
    public DateTime? ClientTimestamp { get; init; }
    public string Resolution { get; init; } = "";
    // S01 — additional fields populated by the GET /sync/conflicts list
    // endpoint. Optional on the inline bulk-sync path, which still only
    // sets ElementId/Server|ClientTimestamp/Resolution.
    public Guid? Id { get; init; }
    public string? ConflictType { get; init; }
    public DateTime? ResolvedAt { get; init; }
    public string? ResolvedBy { get; init; }
}

// ── S01 — sync watermark + conflict log REST DTOs ────────────────────
public record SyncWatermarkDto
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public string DeviceId { get; init; } = "";
    public DateTime LastSyncUtc { get; init; }
    public int ElementCount { get; init; }
}

public record UpsertWatermarkRequest
{
    public string DeviceId { get; init; } = "";
    public DateTime LastSyncUtc { get; init; }
    public int ElementCount { get; init; }
}

public record LogConflictRequest
{
    public string ElementId { get; init; } = "";
    public string ConflictType { get; init; } = "";
    /// <summary>
    /// Optional value-level diff fields. Currently ignored server-side —
    /// kept on the wire for forward compatibility so a future
    /// ConflictPayload column can be populated without a client change.
    /// </summary>
    public string? ServerValue { get; init; }
    public string? ClientValue { get; init; }
    public string Resolution { get; init; } = "";
    public string? ResolvedBy { get; init; }
}

public record DocumentVersionDto
{
    public Guid Id { get; init; }
    public int VersionNumber { get; init; }
    public string FilePath { get; init; } = "";
    public string? FileHash { get; init; }
    public long? FileSize { get; init; }
    public string? UploadedBy { get; init; }
    public DateTime UploadedAt { get; init; }
    public string? ChangeDescription { get; init; }
}

public record ComplianceSummaryDto
{
    public int TotalElements { get; init; }
    public int Tagged { get; init; }
    public int Untagged { get; init; }
    public int FullyResolved { get; init; }
    public int Stale { get; init; }
    public double CompliancePercent { get; init; }
    public double ContainerPercent { get; init; }
    public string RagStatus { get; init; } = "";
    public Dictionary<string, int> ByDiscipline { get; init; } = new();
    public Dictionary<string, int> EmptyTokenCounts { get; init; } = new();
}

public record AuthLoginRequest
{
    public string Email { get; init; } = "";
    public string Password { get; init; } = "";
}

public record AuthLoginResponse
{
    public string AccessToken { get; init; } = "";
    public string RefreshToken { get; init; } = "";
    public DateTime ExpiresAt { get; init; }
    public string UserName { get; init; } = "";
    public string Role { get; init; } = "";
    public string Tier { get; init; } = "";
    public bool MimEnabled { get; init; }
}

public record LicenseActivationRequest
{
    public string LicenseKey { get; init; } = "";
    public string MachineId { get; init; } = "";
    public string RevitVersion { get; init; } = "";
    public string UserName { get; init; } = "";
}

public record LicenseActivationResponse
{
    public bool Valid { get; init; }
    public string Tier { get; init; } = "";
    public bool MimEnabled { get; init; }
    public string? ServerUrl { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public string? Message { get; init; }
}

// ── Auth request DTOs ─────────────────────────────────────────────────────────

public record RefreshTokenRequest
{
    public string RefreshToken { get; init; } = "";
}

public record RegisterRequest
{
    public string OrganisationName { get; init; } = "";
    public string TenantSlug       { get; init; } = "";  // subdomain / identifier
    public string DisplayName      { get; init; } = "";
    public string Email            { get; init; } = "";
    public string Password         { get; init; } = "";

    // S1.5 — optional billing surface picked up at signup time. Plan defaults
    // to "Trial" (always 30 days regardless of the value the user picks);
    // Currency drives whether the future invoices route to Stripe (USD/EUR/GBP)
    // or Flutterwave (UGX/KES/TZS/RWF/NGN/ZAR/ZMW). Country is a 2-letter ISO
    // hint that the signup flow uses to default the currency client-side.
    public string? Plan        { get; init; }   // "Studio" | "Practice" | "Network"; null = "Network"
    public string? Currency    { get; init; }   // "USD" | "UGX" | "KES" | "TZS" | "RWF" | "NGN" | "ZAR" | "EUR" | "GBP"
    public string? CountryCode { get; init; }   // ISO 3166-1 alpha-2 hint
}

public record ChangePasswordRequest
{
    public string CurrentPassword { get; init; } = "";
    public string NewPassword     { get; init; } = "";
}

public record ForgotPasswordRequest
{
    public string Email { get; init; } = "";
}

public record ResetPasswordRequest
{
    public string Token       { get; init; } = "";
    public string NewPassword { get; init; } = "";
}

// ── Batch Operations ────────────────────────────────────────────────

public record BatchRequest
{
    public List<BatchOperation> Operations { get; init; } = new();
}

public record BatchOperation
{
    public string Type { get; init; } = "";  // CREATE_ISSUE, UPDATE_ISSUE, TRANSITION_CDE
    public System.Text.Json.JsonElement Payload { get; init; }
}

public record BatchResponse
{
    public int Total { get; init; }
    public int Succeeded { get; init; }
    public int Failed { get; init; }
    public List<BatchOperationResult> Results { get; init; } = new();
}

public record BatchOperationResult
{
    public int Index { get; init; }
    public string Type { get; init; } = "";
    public bool Success { get; init; }
    public string? Error { get; init; }
    public object? Data { get; init; }
}
