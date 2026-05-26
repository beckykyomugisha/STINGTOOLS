namespace Planscape.Infrastructure.Services;

/// <summary>
/// Phase 180 — Tiny EXIF DateTimeOriginal sniffer for site-photo
/// capture. Avoids pulling in a full EXIF library; just walks the
/// JPEG APP1 segment looking for the DateTimeOriginal (0x9003) tag
/// and parses it as a local-time "yyyy:MM:dd HH:mm:ss" string.
/// Returns null when EXIF is absent / unreadable (the common case
/// for re-encoded mobile uploads), so the caller falls back to
/// client-supplied or server-now timestamps.
///
/// This is a defensive read — never throws, never partial-decodes
/// the image. Cost: a single linear scan over the first ~64 KB.
/// </summary>
public static class PhotoExifSniffer
{
    public static DateTime? TryReadDateTimeOriginal(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 32) return null;
        try
        {
            // JPEG SOI: 0xFFD8
            if (bytes[0] != 0xFF || bytes[1] != 0xD8) return null;
            int i = 2;
            while (i + 4 < bytes.Length)
            {
                if (bytes[i] != 0xFF) return null;
                byte marker = bytes[i + 1];
                int segLen = (bytes[i + 2] << 8) | bytes[i + 3];
                if (segLen < 2 || i + 2 + segLen > bytes.Length) return null;

                // APP1 = 0xE1, EXIF header "Exif\0\0"
                if (marker == 0xE1 && segLen > 8 &&
                    bytes[i + 4] == 'E' && bytes[i + 5] == 'x' && bytes[i + 6] == 'i' && bytes[i + 7] == 'f')
                {
                    // TIFF header begins at i+10. Endianness flag.
                    int tiff = i + 10;
                    if (tiff + 8 > bytes.Length) return null;
                    bool little = bytes[tiff] == 'I' && bytes[tiff + 1] == 'I';
                    bool big    = bytes[tiff] == 'M' && bytes[tiff + 1] == 'M';
                    if (!little && !big) return null;

                    int ifd0Offset = ReadInt32(bytes, tiff + 4, little);
                    int ifd0       = tiff + ifd0Offset;
                    if (ifd0 + 2 > bytes.Length) return null;
                    int entries = ReadInt16(bytes, ifd0, little);

                    // Walk IFD0 looking for ExifIFDPointer (0x8769).
                    int exifIfd = -1;
                    for (int e = 0; e < entries; e++)
                    {
                        int entry = ifd0 + 2 + (e * 12);
                        if (entry + 12 > bytes.Length) break;
                        int tag = ReadInt16(bytes, entry, little);
                        if (tag == 0x8769)
                        {
                            int valOffset = ReadInt32(bytes, entry + 8, little);
                            exifIfd = tiff + valOffset;
                            break;
                        }
                    }
                    if (exifIfd <= 0 || exifIfd + 2 > bytes.Length) return null;
                    int exifEntries = ReadInt16(bytes, exifIfd, little);
                    for (int e = 0; e < exifEntries; e++)
                    {
                        int entry = exifIfd + 2 + (e * 12);
                        if (entry + 12 > bytes.Length) break;
                        int tag = ReadInt16(bytes, entry, little);
                        // 0x9003 = DateTimeOriginal
                        if (tag == 0x9003)
                        {
                            int len = ReadInt32(bytes, entry + 4, little);
                            int valOffset = ReadInt32(bytes, entry + 8, little);
                            int strStart = tiff + valOffset;
                            if (len <= 0 || strStart + len > bytes.Length) return null;
                            var s = System.Text.Encoding.ASCII.GetString(bytes, strStart, Math.Min(19, len));
                            // EXIF format: "yyyy:MM:dd HH:mm:ss"
                            if (DateTime.TryParseExact(s,
                                "yyyy:MM:dd HH:mm:ss",
                                System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.AssumeLocal,
                                out var dt))
                            {
                                return dt.ToUniversalTime();
                            }
                            return null;
                        }
                    }
                    return null;
                }
                i += 2 + segLen;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static int ReadInt16(byte[] b, int i, bool little) =>
        little ? (b[i] | (b[i + 1] << 8))
               : ((b[i] << 8) | b[i + 1]);

    private static int ReadInt32(byte[] b, int i, bool little) =>
        little ? (b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24))
               : ((b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3]);
}
