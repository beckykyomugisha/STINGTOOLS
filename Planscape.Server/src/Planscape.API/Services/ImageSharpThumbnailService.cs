using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Planscape.API.Services;

/// <summary>
/// S04: ImageSharp-backed thumbnail + EXIF GPS service.
/// Generates 150 / 300 / 600 px JPEG thumbnails and reads GPS DMS
/// coordinates from EXIF metadata for geofence validation.
/// </summary>
public class ImageSharpThumbnailService : IThumbnailService
{
    private static readonly int[] Sizes = { 150, 300, 600 };

    public async Task<Dictionary<int, byte[]>> GenerateThumbnailsAsync(Stream imageStream)
    {
        var results = new Dictionary<int, byte[]>();
        imageStream.Position = 0;
        using var image = await Image.LoadAsync(imageStream);

        foreach (var size in Sizes)
        {
            using var clone = image.Clone(ctx =>
            {
                var longest = Math.Max(image.Width, image.Height);
                if (longest == 0) return;
                var ratio = (double)size / longest;
                var newW = Math.Max(1, (int)(image.Width * ratio));
                var newH = Math.Max(1, (int)(image.Height * ratio));
                ctx.Resize(newW, newH);
            });
            using var ms = new MemoryStream();
            await clone.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 80 });
            results[size] = ms.ToArray();
        }
        return results;
    }

    public (double? Latitude, double? Longitude) ExtractGpsFromExif(Stream imageStream)
    {
        try
        {
            imageStream.Position = 0;
            using var image = Image.Load(imageStream);
            var exif = image.Metadata.ExifProfile;
            if (exif == null) return (null, null);

            // ImageSharp 3.x: TryGetValue<T>(tag, out var value) is the supported API.
            if (!exif.TryGetValue(ExifTag.GPSLatitude, out var latValue) ||
                !exif.TryGetValue(ExifTag.GPSLongitude, out var lonValue) ||
                latValue?.Value == null || lonValue?.Value == null)
            {
                return (null, null);
            }

            var lat = ConvertDmsToDecimal(latValue.Value);
            var lon = ConvertDmsToDecimal(lonValue.Value);

            if (exif.TryGetValue(ExifTag.GPSLatitudeRef, out var latRef)
                && latRef?.Value == "S")
                lat = -lat;
            if (exif.TryGetValue(ExifTag.GPSLongitudeRef, out var lonRef)
                && lonRef?.Value == "W")
                lon = -lon;

            return (lat, lon);
        }
        catch (Exception)
        {
            // Corrupt or unsupported EXIF — degrade to no-GPS rather than failing the upload.
            return (null, null);
        }
    }

    private static double ConvertDmsToDecimal(Rational[] dms)
    {
        if (dms == null || dms.Length < 3) return 0;
        return ToDouble(dms[0]) + ToDouble(dms[1]) / 60.0 + ToDouble(dms[2]) / 3600.0;
    }

    private static double ToDouble(Rational r)
    {
        return r.Denominator == 0 ? 0 : (double)r.Numerator / r.Denominator;
    }
}
