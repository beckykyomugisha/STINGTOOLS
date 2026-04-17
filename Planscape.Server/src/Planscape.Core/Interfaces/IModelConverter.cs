namespace Planscape.Core.Interfaces;

/// <summary>
/// P7 — Pluggable IFC → glTF converter. Production implementations:
///
///   • IfcConvertConverter          — wraps the `IfcConvert` CLI (IfcOpenShell).
///                                    Free, local, slow on large models.
///   • ApsModelDerivativeConverter  — Autodesk Platform Services. Faster +
///                                    higher fidelity, paid.
///
/// Default binding is <see cref="NullModelConverter"/> which returns "not
/// implemented" — tenants that upload IFC get a "conversion pending" banner
/// in the viewer until a converter is registered.
/// </summary>
public interface IModelConverter
{
    /// <summary>Short name for audit logs ("ifcconvert", "aps", "null").</summary>
    string ProviderName { get; }

    /// <summary>
    /// Convert <paramref name="inputPath"/> (IFC / RVT / whatever the
    /// implementation supports) to a glTF/GLB written to <paramref name="outputPath"/>.
    /// Returns a <see cref="ConversionResult"/> with metrics + error when failed.
    /// </summary>
    Task<ConversionResult> ConvertToGlbAsync(
        string inputPath, string outputPath, CancellationToken ct = default);
}

public sealed record ConversionResult(
    bool   Success,
    string ProviderName,
    long   ElapsedMs,
    long   OutputSizeBytes,
    int?   ElementCount,
    string? Error);

/// <summary>
/// P8 — Headless thumbnail renderer. Generates a 512×512 PNG preview of a
/// glTF/GLB so the mobile model list has a thumb without downloading the
/// whole file.
///
/// Production: run three.js headless under Node via `@napi-rs/canvas` +
/// `node-three-renderer` (or call a separate micro-service). For v1 the
/// <see cref="NullThumbnailGenerator"/> no-ops — the mobile list falls back
/// to an emoji.
/// </summary>
public interface IModelThumbnailGenerator
{
    string ProviderName { get; }

    Task<ThumbnailResult> GenerateAsync(
        string modelPath, string outputPngPath, CancellationToken ct = default);
}

public sealed record ThumbnailResult(
    bool   Success,
    string ProviderName,
    long   ElapsedMs,
    string? Error);
