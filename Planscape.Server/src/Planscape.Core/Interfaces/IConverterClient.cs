namespace Planscape.Core.Interfaces;

/// <summary>
/// Thin client for the converter sidecar (Planscape.Server/src/converter-sidecar).
/// The sidecar exposes <c>POST /ifc-to-glb</c> which pulls the IFC from a
/// presigned <c>sourceUrl</c>, runs IfcConvert, and publishes the GLB back via
/// <c>POST /api/projects/{id}/models</c> (so the GLB lands as a normal,
/// renderable ProjectModel — no extra controller work on the callback).
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
    /// to GLB and publish it to the given project. Returns the sidecar's result
    /// (ok + optional new GLB model id) — never throws for an HTTP error, so a
    /// failed conversion doesn't crash the enqueuing job.
    /// </summary>
    Task<ConverterResult> ConvertIfcToGlbAsync(
        string sourceUrl, Guid projectId, string fileName, string? discipline, CancellationToken ct = default);
}

public record ConverterResult(bool Success, Guid? GlbModelId = null, string? Error = null);
