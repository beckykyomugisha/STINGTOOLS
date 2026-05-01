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
    public List<TagElementDto> Elements { get; init; } = new();
}

public record TagElementDto
{
    public long RevitElementId { get; init; }
    public string UniqueId { get; init; } = "";
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
