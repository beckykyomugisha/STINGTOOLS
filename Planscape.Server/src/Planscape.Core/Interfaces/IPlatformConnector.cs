using Planscape.Core.Entities;

namespace Planscape.Core.Interfaces;

/// <summary>
/// Abstraction for external BIM platform connectors (ACC, Procore, Aconex, Trimble Connect).
/// Each platform implements this interface for OAuth, sync, and webhook handling.
/// </summary>
public interface IPlatformConnector
{
    PlatformType Platform { get; }

    /// <summary>Test connectivity and token validity for a platform connection.</summary>
    Task<PlatformTestResult> TestConnectionAsync(PlatformConnection connection, CancellationToken ct = default);

    /// <summary>Refresh OAuth2 tokens using the stored refresh token.</summary>
    Task<PlatformTokenResult> RefreshTokenAsync(PlatformConnection connection, CancellationToken ct = default);

    /// <summary>Sync tagged elements and compliance data to/from the external platform.</summary>
    Task<PlatformSyncResult> SyncAsync(PlatformConnection connection, IReadOnlyList<TaggedElement> elements, CancellationToken ct = default);

    /// <summary>Process an incoming webhook callback from the platform.</summary>
    Task<PlatformWebhookResult> HandleWebhookAsync(PlatformConnection connection, string payload, string? signature, CancellationToken ct = default);
}

public record PlatformTestResult(bool Success, string? Message = null);
public record PlatformTokenResult(bool Success, string? AccessToken = null, string? RefreshToken = null, DateTime? ExpiresAt = null, string? Error = null);
public record PlatformSyncResult(bool Success, int PushedCount = 0, int PulledCount = 0, string? Error = null);
public record PlatformWebhookResult(bool Handled, string? Action = null, string? Error = null);

/// <summary>
/// Resolves the correct IPlatformConnector implementation for a given PlatformType.
/// </summary>
public interface IPlatformConnectorFactory
{
    IPlatformConnector GetConnector(PlatformType platform);
}
