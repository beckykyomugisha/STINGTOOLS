using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Stub connector for Autodesk Construction Cloud (BIM 360 / ACC).
/// Replace with real OAuth2 + Forge/APS API calls when ACC integration is configured.
/// </summary>
public class AccConnector : IPlatformConnector
{
    public PlatformType Platform => PlatformType.ACC;

    public Task<PlatformTestResult> TestConnectionAsync(PlatformConnection connection, CancellationToken ct = default)
        => Task.FromResult(new PlatformTestResult(false, "ACC connector not yet configured — provide OAuth credentials in appsettings."));

    public Task<PlatformTokenResult> RefreshTokenAsync(PlatformConnection connection, CancellationToken ct = default)
        => Task.FromResult(new PlatformTokenResult(false, Error: "ACC token refresh not implemented."));

    public Task<PlatformSyncResult> SyncAsync(PlatformConnection connection, IReadOnlyList<TaggedElement> elements, CancellationToken ct = default)
        => Task.FromResult(new PlatformSyncResult(false, Error: "ACC sync not implemented."));

    public Task<PlatformWebhookResult> HandleWebhookAsync(PlatformConnection connection, string payload, string? signature, CancellationToken ct = default)
        => Task.FromResult(new PlatformWebhookResult(false, Error: "ACC webhook handling not implemented."));
}

/// <summary>
/// Stub connector for Procore.
/// Replace with real Procore REST API v1.1 calls when integration is configured.
/// </summary>
public class ProcoreConnector : IPlatformConnector
{
    public PlatformType Platform => PlatformType.Procore;

    public Task<PlatformTestResult> TestConnectionAsync(PlatformConnection connection, CancellationToken ct = default)
        => Task.FromResult(new PlatformTestResult(false, "Procore connector not yet configured."));

    public Task<PlatformTokenResult> RefreshTokenAsync(PlatformConnection connection, CancellationToken ct = default)
        => Task.FromResult(new PlatformTokenResult(false, Error: "Procore token refresh not implemented."));

    public Task<PlatformSyncResult> SyncAsync(PlatformConnection connection, IReadOnlyList<TaggedElement> elements, CancellationToken ct = default)
        => Task.FromResult(new PlatformSyncResult(false, Error: "Procore sync not implemented."));

    public Task<PlatformWebhookResult> HandleWebhookAsync(PlatformConnection connection, string payload, string? signature, CancellationToken ct = default)
        => Task.FromResult(new PlatformWebhookResult(false, Error: "Procore webhook handling not implemented."));
}

/// <summary>
/// Stub connector for Oracle Aconex.
/// Replace with real Aconex REST API calls when integration is configured.
/// </summary>
public class AconexConnector : IPlatformConnector
{
    public PlatformType Platform => PlatformType.Aconex;

    public Task<PlatformTestResult> TestConnectionAsync(PlatformConnection connection, CancellationToken ct = default)
        => Task.FromResult(new PlatformTestResult(false, "Aconex connector not yet configured."));

    public Task<PlatformTokenResult> RefreshTokenAsync(PlatformConnection connection, CancellationToken ct = default)
        => Task.FromResult(new PlatformTokenResult(false, Error: "Aconex token refresh not implemented."));

    public Task<PlatformSyncResult> SyncAsync(PlatformConnection connection, IReadOnlyList<TaggedElement> elements, CancellationToken ct = default)
        => Task.FromResult(new PlatformSyncResult(false, Error: "Aconex sync not implemented."));

    public Task<PlatformWebhookResult> HandleWebhookAsync(PlatformConnection connection, string payload, string? signature, CancellationToken ct = default)
        => Task.FromResult(new PlatformWebhookResult(false, Error: "Aconex webhook handling not implemented."));
}

/// <summary>
/// Stub connector for Trimble Connect.
/// Replace with real Trimble Connect API calls when integration is configured.
/// </summary>
public class TrimbleConnector : IPlatformConnector
{
    public PlatformType Platform => PlatformType.Trimble;

    public Task<PlatformTestResult> TestConnectionAsync(PlatformConnection connection, CancellationToken ct = default)
        => Task.FromResult(new PlatformTestResult(false, "Trimble Connect connector not yet configured."));

    public Task<PlatformTokenResult> RefreshTokenAsync(PlatformConnection connection, CancellationToken ct = default)
        => Task.FromResult(new PlatformTokenResult(false, Error: "Trimble token refresh not implemented."));

    public Task<PlatformSyncResult> SyncAsync(PlatformConnection connection, IReadOnlyList<TaggedElement> elements, CancellationToken ct = default)
        => Task.FromResult(new PlatformSyncResult(false, Error: "Trimble sync not implemented."));

    public Task<PlatformWebhookResult> HandleWebhookAsync(PlatformConnection connection, string payload, string? signature, CancellationToken ct = default)
        => Task.FromResult(new PlatformWebhookResult(false, Error: "Trimble webhook handling not implemented."));
}

/// <summary>
/// Resolves the correct IPlatformConnector for a given PlatformType.
/// Falls back to a no-op connector if the platform is unknown.
/// </summary>
public class PlatformConnectorFactory : IPlatformConnectorFactory
{
    private readonly Dictionary<PlatformType, IPlatformConnector> _connectors;
    private readonly ILogger<PlatformConnectorFactory> _logger;

    public PlatformConnectorFactory(IEnumerable<IPlatformConnector> connectors, ILogger<PlatformConnectorFactory> logger)
    {
        _connectors = connectors.ToDictionary(c => c.Platform);
        _logger = logger;
    }

    public IPlatformConnector GetConnector(PlatformType platform)
    {
        if (_connectors.TryGetValue(platform, out var connector))
            return connector;

        _logger.LogWarning("No connector registered for platform {Platform}", platform);
        throw new NotSupportedException($"Platform {platform} is not supported.");
    }
}
