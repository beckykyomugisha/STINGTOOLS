namespace Planscape.Core.Interfaces;

/// <summary>
/// Thin client for the converter sidecar (Planscape.Server/src/converter-sidecar).
/// The sidecar exposes <c>POST /ifc-to-glb</c> which pulls the IFC from a
/// presigned <c>sourceUrl</c>, runs IfcConvert, and STREAMS the GLB back in the
/// response body (with an <c>X-Glb-Sha256</c> + <c>X-Glb-Bytes</c> header).
///
/// The caller (IfcToGlbConversionJob) then stores the GLB itself via
/// <see cref="IFileStorageService.SaveScopedAsync"/> and creates the
/// ProjectModel row with correct tenancy — so the sidecar needs NO callback
/// into the authed API and NO shared platform bearer with cross-tenant project
/// access. This is the sustainable shape: one credential surface, correct
/// tenant attribution, and the GLB bytes never transit the API web process
/// (the conversion job runs on the worker).
///
/// Configured via <c>Converter:BaseUrl</c> + <c>Converter:Token</c> (env
/// <c>Converter__BaseUrl</c> / <c>Converter__Token</c>). When unset,
/// <see cref="IsConfigured"/> is false and callers skip conversion gracefully.
/// </summary>
public interface IConverterClient
{
    /// <summary>True when Converter:BaseUrl is configured.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Ask the sidecar to convert an IFC (fetchable at <paramref name="sourceUrl"/>)
    /// to GLB and stream it back. The returned <see cref="ConverterGlbResult"/>
    /// owns the open response — the caller MUST dispose it. Never throws for an
    /// HTTP/transport error; failures surface via <see cref="ConverterGlbResult.Success"/>.
    /// </summary>
    Task<ConverterGlbResult> ConvertIfcToGlbAsync(
        string sourceUrl, string fileName, string? discipline, CancellationToken ct = default);
}

/// <summary>
/// Result of an IFC→GLB conversion. Holds the open GLB response stream when
/// <see cref="Success"/>; dispose to release the underlying HTTP response.
/// </summary>
public sealed class ConverterGlbResult : IDisposable
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    /// <summary>The GLB bytes (only when <see cref="Success"/>). Read once, then dispose.</summary>
    public Stream? Glb { get; init; }
    /// <summary>SHA-256 of the GLB (hex), supplied by the sidecar so the caller hashes in zero passes.</summary>
    public string? Sha256 { get; init; }
    /// <summary>GLB size in bytes.</summary>
    public long Bytes { get; init; }

    private IDisposable? _owner;

    public static ConverterGlbResult Fail(string error) => new() { Success = false, Error = error };

    public static ConverterGlbResult Ok(Stream glb, string? sha256, long bytes, IDisposable owner) =>
        new() { Success = true, Glb = glb, Sha256 = sha256, Bytes = bytes, _owner = owner };

    public void Dispose() => _owner?.Dispose();
}
