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

// ── Full sync payload (plugin → server) ──────────────────────────────────────

/// <summary>
/// Enhanced sync request carrying elements + compliance snapshot + warning summary
/// + SEQ counters in a single atomic call. Supersedes TagSyncRequest for plugin v2.2+.
/// </summary>
public record FullSyncRequest
{
    public Guid    ProjectId     { get; init; }
    /// <summary>Used to auto-create the project if ProjectId == Guid.Empty.</summary>
    public string  ProjectName   { get; init; } = "";
    /// <summary>Short project code, e.g. "MP01". Used on auto-create.</summary>
    public string  ProjectCode   { get; init; } = "";
    public string  UserName      { get; init; } = "";
    public string  RevitVersion  { get; init; } = "";
    public string  PluginVersion { get; init; } = "";

    /// <summary>Tagged elements delta (all elements that have ASS_TAG_1 set).</summary>
    public List<TagElementDto> Elements { get; init; } = new();

    /// <summary>Latest compliance snapshot from ComplianceScan.Scan().</summary>
    public FullSyncComplianceDto? Compliance { get; init; }

    /// <summary>Warning summary from WarningsEngine.ScanWarnings().</summary>
    public FullSyncWarningDto? Warnings { get; init; }

    /// <summary>SEQ counter maximums (CounterKey → MaxValue). Server merges with max().</summary>
    public Dictionary<string, int> SeqCounters { get; init; } = new();
}

/// <summary>Compliance snapshot embedded in a FullSyncRequest.</summary>
public record FullSyncComplianceDto
{
    public int    TotalElements      { get; init; }
    public int    TaggedComplete     { get; init; }
    public int    TaggedIncomplete   { get; init; }
    public int    Untagged           { get; init; }
    public int    FullyResolved      { get; init; }
    public int    StaleCount         { get; init; }
    public int    PlaceholderCount   { get; init; }
    public double TagPercent         { get; init; }
    public double StrictPercent      { get; init; }
    public double ContainerPercent   { get; init; }
    public string RagStatus          { get; init; } = "RED";
    /// <summary>Discipline → tagged element count.</summary>
    public Dictionary<string, int>    ByDiscipline    { get; init; } = new();
    /// <summary>Token name → empty count (e.g. "Sys" → 42).</summary>
    public Dictionary<string, int>    EmptyTokenCounts { get; init; } = new();
    /// <summary>Discipline → (Total, Tagged, Pct) as a simple object.</summary>
    public Dictionary<string, DiscSummaryDto> ByDiscDetail { get; init; } = new();
}

public record DiscSummaryDto
{
    public int    Total  { get; init; }
    public int    Tagged { get; init; }
    public double Pct    { get; init; }
}

/// <summary>Warning summary embedded in a FullSyncRequest.</summary>
public record FullSyncWarningDto
{
    public int Total        { get; init; }
    public int Critical     { get; init; }
    public int High         { get; init; }
    public int AutoFixable  { get; init; }
    public int HealthScore  { get; init; }
}

/// <summary>Response to FullSyncRequest.</summary>
public record FullSyncResponse
{
    /// <summary>The project ID used (echoed back, or the newly-created project ID).</summary>
    public Guid   ProjectId        { get; init; }
    public bool   ProjectCreated   { get; init; }
    public int    Received         { get; init; }
    public int    Created          { get; init; }
    public int    Updated          { get; init; }
    public int    SeqCountersSaved { get; init; }
    public double CompliancePercent{ get; init; }
    public string RagStatus        { get; init; } = "";
    public DateTime SyncedAt       { get; init; } = DateTime.UtcNow;
}

// ── Project management DTOs ───────────────────────────────────────────────────

public record CreateProjectRequest
{
    public string Name        { get; init; } = "";
    public string Code        { get; init; } = "";
    public string? Description{ get; init; }
    public string? Phase      { get; init; }
}

// ── Issue DTOs ────────────────────────────────────────────────────────────────

public record CreateIssueRequest
{
    public string  Type        { get; init; } = "RFI";
    public string  Title       { get; init; } = "";
    public string? Description { get; init; }
    public string  Priority    { get; init; } = "MEDIUM";
    public string? Assignee    { get; init; }
    public string? Discipline  { get; init; }
    public string? Revision    { get; init; }
    public List<long> LinkedElementIds { get; init; } = new();
}

public record ForgotPasswordRequest { public string Email { get; init; } = ""; }
public record ResetPasswordRequest
{
    public string Email       { get; init; } = "";
    public string Token       { get; init; } = "";
    public string NewPassword { get; init; } = "";
}

public record ProjectSettingsRequest
{
    public string? Name               { get; init; }
    public string? Description        { get; init; }
    public string? Phase              { get; init; }
    public string? ConfigJson         { get; init; }
    public string? TagSeparator       { get; init; }
    public double? RagGreenThreshold  { get; init; }
    public double? RagAmberThreshold  { get; init; }
}

public record IssueDto
{
    public Guid    Id         { get; init; }
    public string  IssueCode  { get; init; } = "";
    public string  Type       { get; init; } = "";
    public string  Title      { get; init; } = "";
    public string  Priority   { get; init; } = "";
    public string  Status     { get; init; } = "";
    public string? Assignee   { get; init; }
    public string? Discipline { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? DueDate  { get; init; }
    public bool    IsOverdue  { get; init; }
}
