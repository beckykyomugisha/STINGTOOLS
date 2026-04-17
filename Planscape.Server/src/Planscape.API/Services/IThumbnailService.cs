using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Planscape.API.Services;

/// <summary>
/// S04: image thumbnailing + EXIF GPS extraction for on-site photo
/// attachments. Injected into Issues / Documents controllers via DI.
/// </summary>
public interface IThumbnailService
{
    /// <summary>
    /// Generates multiple JPEG thumbnails (keyed by longest-edge size)
    /// from a source image stream. Does not dispose the input stream.
    /// </summary>
    Task<Dictionary<int, byte[]>> GenerateThumbnailsAsync(Stream imageStream);

    /// <summary>
    /// Reads GPS coordinates from the image's EXIF profile, if present.
    /// Returns (null, null) when there is no profile or no GPS tags.
    /// </summary>
    (double? Latitude, double? Longitude) ExtractGpsFromExif(Stream imageStream);
}
