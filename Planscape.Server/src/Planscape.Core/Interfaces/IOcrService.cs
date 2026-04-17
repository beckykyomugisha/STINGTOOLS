namespace Planscape.Core.Interfaces;

/// <summary>
/// T3 — Server-side OCR. On-device OCR (Apple Vision / Android ML Kit) is
/// the default path; callers only reach this when device OCR is either
/// unavailable (Expo Go, desktop upload) or returns sub-threshold confidence.
/// </summary>
public interface IOcrService
{
    string ProviderName { get; }

    /// <summary>
    /// Run OCR on a JPEG / PNG / HEIC byte stream. Returns recognised text
    /// plus an overall confidence 0-1. Implementations should treat a
    /// failure as an empty result rather than throwing.
    /// </summary>
    Task<OcrServerResult> RecognizeAsync(Stream image, string mimeType, CancellationToken ct = default);
}

public sealed record OcrServerResult(
    bool Success,
    string ProviderName,
    string Text,
    double Confidence,
    string? Error);
