namespace Planscape.Infrastructure.Services.PhotoPipeline;

/// <summary>
/// Phase 178 — Image redaction pipeline contract. The default
/// implementation runs ONNX face-detection + a heuristic plate-detector
/// + an SkiaSharp watermark composer; alternative implementations can
/// swap in commercial detectors (AWS Rekognition, Azure Face) by
/// re-binding this interface in Program.cs.
///
/// The pipeline is deliberately decoupled from <see cref="RedactPublishedPhotoJob"/>
/// so the worker container can host the heavy ONNX runtime without
/// dragging it into the API process — that's what makes the "split
/// worker now" decision actually save API CPU.
/// </summary>
public interface IPhotoRedactionPipeline
{
    /// <summary>
    /// Read JPEG / PNG bytes from <paramref name="source"/>, blur every
    /// detected face + number-plate, paint the watermark band in the
    /// bottom-right, and return the JPEG bytes of the derivative plus
    /// detection counts. Returns <c>null</c> on a fatal pipeline error
    /// (caller marks the photo as Failed).
    /// </summary>
    Task<RedactionResult?> RedactAsync(
        Stream source,
        string watermarkText,
        CancellationToken ct);
}

/// <summary>
/// Outcome of a single redaction pass. <see cref="Quarantined"/> wins
/// over <see cref="FacesBlurred"/> / <see cref="PlatesBlurred"/>: when
/// it's true the bytes are still produced but the caller must NOT
/// publish them — see <see cref="QuarantineReason"/>.
/// </summary>
public sealed record RedactionResult(
    byte[] Bytes,
    int    FacesBlurred,
    int    PlatesBlurred,
    bool   WatermarkApplied,
    bool   Quarantined,
    string? QuarantineReason);
