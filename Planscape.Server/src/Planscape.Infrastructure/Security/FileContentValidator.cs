using System.IO;
using System.Linq;

namespace Planscape.Infrastructure.Security;

/// <summary>
/// NEW-LOGIC-06/07 — Server-side validation for uploaded files.
///
/// Two independent checks:
///
/// <list type="bullet">
///   <item><c>SanitiseFileName</c> strips directory components and any
///   characters that are not ASCII letters, digits, dot, dash, or underscore.
///   Prevents path traversal ('../..') and NTFS alternate streams (':').</item>
///   <item><c>SniffImage</c> reads the first few bytes of a stream and checks
///   them against a whitelist of common image magic numbers. Callers pass a
///   seekable stream so it can be rewound after the check.</item>
/// </list>
/// </summary>
public static class FileContentValidator
{
    public static string SanitiseFileName(string? fileName, string fallback = "upload")
    {
        if (string.IsNullOrWhiteSpace(fileName)) return fallback;
        // Strip any directory component
        fileName = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(fileName)) return fallback;

        var allowed = new System.Text.StringBuilder();
        foreach (var ch in fileName)
        {
            if (char.IsLetterOrDigit(ch) || ch == '.' || ch == '-' || ch == '_')
                allowed.Append(ch);
            else if (ch == ' ') allowed.Append('_');
            // drop everything else (`:`, `/`, `\\`, `\0`, control, etc.)
        }
        var cleaned = allowed.ToString().Trim('.', '_', '-');
        if (string.IsNullOrWhiteSpace(cleaned)) return fallback;
        // Truncate aggressively (POSIX path-component limit is 255)
        return cleaned.Length > 200 ? cleaned.Substring(0, 200) : cleaned;
    }

    public enum DetectedImage { Unknown, Jpeg, Png, Gif, WebP, Heic, Avif, Bmp, Tiff }

    public static DetectedImage SniffImage(Stream s)
    {
        if (!s.CanSeek) return DetectedImage.Unknown;
        var orig = s.Position;
        try
        {
            var head = new byte[16];
            int read = s.Read(head, 0, 16);
            if (read < 12) return DetectedImage.Unknown;

            // JPEG: FF D8 FF
            if (head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return DetectedImage.Jpeg;
            // PNG: 89 50 4E 47 0D 0A 1A 0A
            if (head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47) return DetectedImage.Png;
            // GIF: "GIF87a" or "GIF89a"
            if (head[0] == 0x47 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x38) return DetectedImage.Gif;
            // BMP: 42 4D
            if (head[0] == 0x42 && head[1] == 0x4D) return DetectedImage.Bmp;
            // TIFF (little-endian or big-endian)
            if ((head[0] == 0x49 && head[1] == 0x49 && head[2] == 0x2A && head[3] == 0x00)
             || (head[0] == 0x4D && head[1] == 0x4D && head[2] == 0x00 && head[3] == 0x2A)) return DetectedImage.Tiff;
            // RIFF / WebP: "RIFF" ... "WEBP"
            if (head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F'
                && head[8] == 'W' && head[9] == 'E' && head[10] == 'B' && head[11] == 'P') return DetectedImage.WebP;
            // ISO BMFF (HEIC / AVIF). Major brand lives at bytes 8..11.
            if (head[4] == 'f' && head[5] == 't' && head[6] == 'y' && head[7] == 'p')
            {
                var brand = System.Text.Encoding.ASCII.GetString(head, 8, 4);
                if (brand.StartsWith("heic") || brand.StartsWith("heix") || brand.StartsWith("mif1")
                    || brand.StartsWith("hevc") || brand.StartsWith("msf1")) return DetectedImage.Heic;
                if (brand.StartsWith("avif") || brand.StartsWith("avis")) return DetectedImage.Avif;
            }

            return DetectedImage.Unknown;
        }
        finally
        {
            s.Position = orig;
        }
    }

    public static bool IsImage(Stream s, out DetectedImage kind)
    {
        kind = SniffImage(s);
        return kind != DetectedImage.Unknown;
    }

    /// <summary>
    /// S8 — content-type whitelist for Planscape document uploads.
    /// Accepts image / PDF / Office / common BIM-payload MIME types
    /// (IFC, RVT, DWG, BCF). Anything outside this list is rejected
    /// before the bytes are persisted. Case-insensitive.
    /// </summary>
    public static readonly System.Collections.Generic.HashSet<string> AllowedDocumentMimeTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // images (also covered by SniffImage)
            "image/jpeg", "image/png", "image/webp", "image/gif", "image/bmp",
            "image/tiff", "image/heic", "image/heif", "image/avif",
            // PDF
            "application/pdf",
            // Office
            "application/msword",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/vnd.ms-excel",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "application/vnd.ms-powerpoint",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            // text / data
            "text/plain", "text/csv", "application/json", "application/xml", "text/xml",
            // archives (BCF is zip-based)
            "application/zip", "application/x-zip-compressed",
            // BIM payloads — many browsers send octet-stream for these
            "application/octet-stream",
            "model/ifc", "application/x-step",          // IFC
            "application/acad", "image/vnd.dwg",         // DWG
            "application/x-bcf",                          // BCF
        };

    /// <summary>
    /// S8 — case-insensitive extension whitelist used as a second-line
    /// guard when the browser sends <c>application/octet-stream</c>
    /// (which is too permissive on its own to allow as a MIME type).
    /// </summary>
    public static readonly System.Collections.Generic.HashSet<string> AllowedDocumentExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tif", ".tiff",
            ".heic", ".heif", ".avif",
            ".pdf",
            ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
            ".txt", ".csv", ".json", ".xml",
            ".zip",
            ".ifc", ".rvt", ".rfa", ".dwg", ".dxf", ".bcf", ".bcfzip", ".nwd", ".nwc",
        };

    public static bool IsAllowedDocumentUpload(string? contentType, string? fileName)
    {
        var ct = (contentType ?? "").Trim();
        var ext = string.IsNullOrWhiteSpace(fileName) ? "" : Path.GetExtension(fileName);

        // Reject empty content-type unless the extension is on the
        // allow-list (some clients send "" for known BIM payloads).
        if (string.IsNullOrEmpty(ct))
            return AllowedDocumentExtensions.Contains(ext);

        if (!AllowedDocumentMimeTypes.Contains(ct))
            return false;

        // application/octet-stream alone is too broad — require a
        // recognisable extension before letting it through.
        if (ct.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
            return AllowedDocumentExtensions.Contains(ext);

        return true;
    }
}
