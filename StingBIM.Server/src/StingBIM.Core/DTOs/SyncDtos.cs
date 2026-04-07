namespace StingBIM.Core.DTOs;

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
}

public record TagSyncResponse
{
    public int Received { get; init; }
    public int Created { get; init; }
    public int Updated { get; init; }
    public double CompliancePercent { get; init; }
    public string RagStatus { get; init; } = "";
    public DateTime SyncedAt { get; init; } = DateTime.UtcNow;
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
