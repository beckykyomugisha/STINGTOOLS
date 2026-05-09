using SkiaSharp;

namespace Planscape.Infrastructure.Services.PhotoPipeline;

/// <summary>
/// Face detector contract. Default binding in the worker container is
/// <see cref="OnnxFaceDetector"/> (RetinaFace / YuNet 320×320 ONNX
/// model bundled with the worker image). API process binds the no-op
/// <see cref="NullFaceDetector"/> so the API can build without the
/// model file in its image.
/// </summary>
public interface IFaceDetector
{
    Task<IReadOnlyList<SKRectI>> DetectAsync(SKBitmap source, CancellationToken ct);
}

/// <summary>
/// Number-plate detector contract. Default binding is the heuristic
/// <see cref="HeuristicNumberPlateDetector"/> — adequate for European
/// plates seen on a typical building site; commercial ALPR can be
/// dropped in by re-binding the interface in the worker container.
/// </summary>
public interface INumberPlateDetector
{
    Task<IReadOnlyList<SKRectI>> DetectAsync(SKBitmap source, CancellationToken ct);
}

/// <summary>
/// API-process binding — returns no boxes. The API must NEVER call the
/// pipeline directly (the controller only enqueues the job; the worker
/// container resolves the real detector). This null impl keeps the DI
/// graph satisfied if an integration test resolves the pipeline in-
/// proc.
/// </summary>
public sealed class NullFaceDetector : IFaceDetector
{
    public Task<IReadOnlyList<SKRectI>> DetectAsync(SKBitmap source, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SKRectI>>(Array.Empty<SKRectI>());
}

/// <summary>API-process binding for the plate detector — same rationale.</summary>
public sealed class NullNumberPlateDetector : INumberPlateDetector
{
    public Task<IReadOnlyList<SKRectI>> DetectAsync(SKBitmap source, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SKRectI>>(Array.Empty<SKRectI>());
}
