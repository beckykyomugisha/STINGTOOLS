using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Planscape.API.Services;

/// <summary>
/// Phase A4 (multi-host integration) — server half of the substrate
/// drift-check. Computes the SHA-256 over the corporate IFC enum/pset
/// manifest (<c>shared/ifc/enums/_manifest.json</c>) that every host
/// compares against on login. A host whose hash differs is reading a
/// stale or forked copy of the shared vocabulary — surfaced as a warning
/// so coordination doesn't silently run on divergent enums.
///
/// The hash is computed over <b>LF-normalised</b> bytes so it agrees with
/// the host-side hash (<c>stingtools_core.substrate.substrate_manifest_sha256</c>)
/// regardless of whether either side's working tree checked the file out
/// with CRLF (Windows) or LF (Linux). Hashing raw bytes would make a
/// Windows host and a Linux server drift forever on identical content.
///
/// The substrate is immutable per deployment, so the value is computed
/// once and cached for the process lifetime (registered as a singleton).
/// </summary>
public interface ISubstrateManifestProvider
{
    /// <summary>The cached substrate manifest descriptor for this deployment.</summary>
    SubstrateManifestResponse Get();
}

/// <summary>Wire shape for <c>GET /api/substrate/manifest</c>.</summary>
public sealed record SubstrateManifestResponse
{
    /// <summary>SHA-256 (lowercase hex) over the LF-normalised manifest bytes.</summary>
    public string Sha256 { get; init; } = "";

    /// <summary>The manifest schema version (<c>schema_version</c>).</summary>
    public int SchemaVersion { get; init; }

    /// <summary>Total enum count declared in the manifest (<c>total_enums</c>).</summary>
    public int TotalEnums { get; init; }
}

/// <inheritdoc cref="ISubstrateManifestProvider"/>
public sealed class SubstrateManifestProvider : ISubstrateManifestProvider
{
    private readonly Lazy<SubstrateManifestResponse> _cached;
    private readonly ILogger<SubstrateManifestProvider> _logger;

    public SubstrateManifestProvider(
        IConfiguration config,
        IHostEnvironment env,
        ILogger<SubstrateManifestProvider> logger)
    {
        _logger = logger;
        _cached = new Lazy<SubstrateManifestResponse>(() =>
        {
            var path = ResolveManifestPath(config, env);
            if (path is null)
            {
                _logger.LogWarning(
                    "Substrate manifest not found — /api/substrate/manifest will report an empty hash, " +
                    "so hosts cannot drift-check. Set Substrate:ManifestPath or ship Data/IFC/Substrate/_manifest.json.");
                return new SubstrateManifestResponse();
            }

            try
            {
                var resp = ComputeFromFile(path);
                _logger.LogInformation(
                    "Substrate manifest loaded from {Path}: sha256={Sha} schemaVersion={Schema} totalEnums={Total}",
                    path, resp.Sha256, resp.SchemaVersion, resp.TotalEnums);
                return resp;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read substrate manifest at {Path}", path);
                return new SubstrateManifestResponse();
            }
        });
    }

    public SubstrateManifestResponse Get() => _cached.Value;

    /// <summary>
    /// Compute the substrate descriptor from a manifest file. Public + static
    /// so the unit test can exercise the hashing/parsing without a DI graph.
    /// </summary>
    public static SubstrateManifestResponse ComputeFromFile(string path)
    {
        var raw = File.ReadAllBytes(path);
        var normalized = NormalizeNewlines(raw);
        var sha = Convert.ToHexString(SHA256.HashData(normalized)).ToLowerInvariant();

        int schemaVersion = 0, totalEnums = 0;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("schema_version", out var sv) && sv.TryGetInt32(out var s)) schemaVersion = s;
            if (root.TryGetProperty("total_enums", out var te) && te.TryGetInt32(out var t)) totalEnums = t;
        }
        catch (JsonException)
        {
            // A malformed manifest still yields a stable hash; counts stay 0.
        }

        return new SubstrateManifestResponse
        {
            Sha256 = sha,
            SchemaVersion = schemaVersion,
            TotalEnums = totalEnums,
        };
    }

    /// <summary>Collapse CRLF/CR → LF so the hash is checkout-OS-independent.</summary>
    internal static byte[] NormalizeNewlines(byte[] raw)
    {
        // Operate on bytes directly: the manifest is ASCII/UTF-8 JSON, so a
        // byte-level newline collapse matches the host's
        // raw.replace(b"\r\n", b"\n").replace(b"\r", b"\n") exactly.
        var text = Encoding.UTF8.GetString(raw).Replace("\r\n", "\n").Replace("\r", "\n");
        return Encoding.UTF8.GetBytes(text);
    }

    /// <summary>
    /// Resolve the manifest path. Priority:
    ///   1. config <c>Substrate:ManifestPath</c> (absolute or relative to CWD),
    ///   2. <c>{ContentRoot|BaseDirectory}/Data/IFC/Substrate/_manifest.json</c>
    ///      (the build-copied artifact),
    ///   3. dev fallback: walk up from the base/content root looking for the
    ///      repo's <c>shared/ifc/enums/_manifest.json</c>.
    /// Returns null if none resolve.
    /// </summary>
    private static string? ResolveManifestPath(IConfiguration config, IHostEnvironment env)
    {
        var configured = config["Substrate:ManifestPath"];
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return Path.GetFullPath(configured);

        var rel = Path.Combine("Data", "IFC", "Substrate", "_manifest.json");
        foreach (var root in new[] { AppContext.BaseDirectory, env.ContentRootPath })
        {
            if (string.IsNullOrEmpty(root)) continue;
            var candidate = Path.Combine(root, rel);
            if (File.Exists(candidate)) return candidate;
        }

        // Dev fallback: locate the repo copy by walking parents.
        var sentinel = Path.Combine("shared", "ifc", "enums", "_manifest.json");
        foreach (var start in new[] { AppContext.BaseDirectory, env.ContentRootPath, Directory.GetCurrentDirectory() })
        {
            var dir = string.IsNullOrEmpty(start) ? null : new DirectoryInfo(start);
            for (int i = 0; dir is not null && i < 12; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, sentinel);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }
}
