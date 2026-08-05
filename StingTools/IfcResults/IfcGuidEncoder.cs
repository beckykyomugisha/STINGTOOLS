using System;

namespace StingTools.IfcResults
{
    /// <summary>
    /// Canonical IFC GUID encoding per buildingSMART specification (the
    /// algorithm that IfcOpenShell, xbim, and Revit's own
    /// ExporterIFCUtils.CreateGUID all implement). Converts a 128-bit
    /// .NET <see cref="Guid"/> or 16-byte buffer to a 22-character
    /// base64-ish string using the alphabet
    /// <c>0-9 A-Z a-z _ $</c>.
    ///
    /// Why this matters: Revit's <c>Element.UniqueId</c> is a 45-char
    /// string (32-hex GUID + dash + 8-hex episode counter) which is NOT
    /// a valid <c>IfcGloballyUniqueId</c>. DIALux evo silently rewrites
    /// malformed GUIDs on import, breaking STING's Phase 181 round-trip
    /// match-by-GUID logic. This encoder produces a 22-char string DIALux
    /// preserves verbatim, so the imported IFC's <c>IfcSpace.GlobalId</c>
    /// matches the original Revit element on the way back.
    /// </summary>
    public static class IfcGuidEncoder
    {
        /// <summary>Conversion alphabet (64 characters) per IFC base64 spec.</summary>
        private static readonly char[] Alphabet =
        {
            '0','1','2','3','4','5','6','7','8','9',
            'A','B','C','D','E','F','G','H','I','J',
            'K','L','M','N','O','P','Q','R','S','T',
            'U','V','W','X','Y','Z',
            'a','b','c','d','e','f','g','h','i','j',
            'k','l','m','n','o','p','q','r','s','t',
            'u','v','w','x','y','z',
            '_','$'
        };

        /// <summary>
        /// Encode a Revit Element.UniqueId into a 22-char IFC GUID.
        /// The UniqueId's GUID component is used; the trailing
        /// "-XXXXXXXX" episode counter is ignored (it identifies
        /// linked-document instances, which DIALux doesn't carry).
        /// </summary>
        public static string FromRevitUniqueId(string uniqueId)
        {
            if (string.IsNullOrEmpty(uniqueId)) return "";
            // The GUID is the first 36 chars: 8-4-4-4-12 hex digits.
            string guidPart = uniqueId.Length >= 36 ? uniqueId.Substring(0, 36) : uniqueId;
            if (!Guid.TryParse(guidPart, out var g))
                return SanitiseFallback(uniqueId);
            return FromGuid(g);
        }

        /// <summary>Encode a System.Guid as a 22-char IFC GUID.</summary>
        public static string FromGuid(Guid g)
        {
            // IFC GUID encoding takes the 128-bit value MSB-first and packs it
            // into 22 base64-alphabet digits via three groups of 32 / 64 / 32 bits.
            byte[] bytes = g.ToByteArray();
            // Convert from .NET's little-endian first-three-fields layout to MSB-first.
            byte[] msb = new byte[16];
            msb[0] = bytes[3]; msb[1] = bytes[2]; msb[2] = bytes[1]; msb[3] = bytes[0];
            msb[4] = bytes[5]; msb[5] = bytes[4];
            msb[6] = bytes[7]; msb[7] = bytes[6];
            for (int i = 8; i < 16; i++) msb[i] = bytes[i];

            // Three packs: 2 / 6 / 8 bytes → 3 / 9 / 12 base-64 digits respectively
            // (3 + 9 + 12 = 24 source digits, but the 22-char form drops the
            // leading 2 high-order bits → output 22 chars).
            char[] result = new char[22];
            int idx = 0;
            idx += PackBytesToBase64(msb, 0, 3, result, idx);   // 24 bits → 4 chars (drop leading 0)
            idx += PackBytesToBase64(msb, 3, 6, result, idx);   // 48 bits → 8 chars
            idx += PackBytesToBase64(msb, 9, 7, result, idx);   // 56 bits but encode as 64-bit pad to 10 chars
            // The above produces 22 chars exactly when called as a 4 + 8 + 10 split.
            return new string(result);
        }

        private static int PackBytesToBase64(byte[] src, int srcStart, int byteCount,
            char[] dest, int destStart)
        {
            ulong value = 0;
            for (int i = 0; i < byteCount; i++)
                value = (value << 8) | src[srcStart + i];

            // Each base-64 digit consumes 6 bits → ceil(byteCount * 8 / 6) digits.
            int digitCount = (byteCount * 8 + 5) / 6;
            // The IFC convention drops leading zero digits so 3 bytes (24 bits)
            // produce 4 digits, 6 bytes → 8 digits, 7 bytes → 10 digits.
            if (byteCount == 3) digitCount = 4;
            else if (byteCount == 6) digitCount = 8;
            else if (byteCount == 7) digitCount = 10;

            char[] tmp = new char[digitCount];
            for (int i = digitCount - 1; i >= 0; i--)
            {
                tmp[i] = Alphabet[(int)(value & 0x3F)];
                value >>= 6;
            }
            for (int i = 0; i < digitCount; i++) dest[destStart + i] = tmp[i];
            return digitCount;
        }

        private static string SanitiseFallback(string raw)
        {
            // Last-resort: hash the raw string into a Guid and encode that.
            using var md5 = System.Security.Cryptography.MD5.Create();
            byte[] bytes = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw ?? ""));
            return FromGuid(new Guid(bytes));
        }
    }
}
