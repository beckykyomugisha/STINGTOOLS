using System;
using System.Globalization;

namespace StingTools.IfcResults
{
    /// <summary>
    /// Canonical IFC GlobalId encoding per the buildingSMART specification —
    /// the exact algorithm Autodesk's RevitIFC <c>GUIDUtil.ConvertToIFCGuid</c>,
    /// IfcOpenShell, and xbim all implement. Converts a 128-bit .NET
    /// <see cref="Guid"/> to the 22-character base64-ish string over the
    /// alphabet <c>0-9 A-Z a-z _ $</c>, grouping the 16 bytes as
    /// <c>1 / 3 / 3 / 3 / 3 / 3</c> bytes → <c>2 / 4 / 4 / 4 / 4 / 4</c> chars.
    ///
    /// <para><b>Revit UniqueId → IFC GlobalId.</b> A Revit
    /// <c>Element.UniqueId</c> is a 45-char string:
    /// <c>&lt;36-char episode GUID&gt;-&lt;8-hex element id&gt;</c>. It is NOT a
    /// valid <c>IfcGloballyUniqueId</c>. Revit's IFC exporter derives the
    /// GlobalId by XOR-folding the trailing element id into the low 4 bytes of
    /// the episode GUID and then base64-compressing the result — so the element
    /// id MUST be folded in (two elements created in the same episode share the
    /// GUID part and differ only in the suffix). <see cref="FromRevitUniqueId"/>
    /// reproduces that, so the value matches what an actual Revit IFC export
    /// writes for the same element (verified by a pinned test vector in
    /// <c>StingTools.Boq.Tests/IfcGuidEncoderTests.cs</c>).</para>
    ///
    /// <para>For a live <c>Element</c> the gold-standard is
    /// <c>Autodesk.Revit.DB.IFC.ExporterIFCUtils.CreateGUID(el)</c>; this
    /// string encoder is the equivalent for the BOQ snapshot path (which only
    /// carries a UniqueId string) and is intentionally the single resolver so
    /// BOQ rows and COBie Components for the same element produce identical
    /// GlobalIds.</para>
    /// </summary>
    public static class IfcGuidEncoder
    {
        /// <summary>Conversion alphabet (64 characters) per the IFC base64 spec.</summary>
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
        /// Encode a Revit <c>Element.UniqueId</c> into its canonical 22-char IFC
        /// GlobalId — the same value Revit's IFC exporter assigns. The 8-hex
        /// element-id suffix is XOR-folded into the GUID's low 4 bytes before
        /// compression (this is what makes per-element GlobalIds distinct within
        /// one episode).
        /// </summary>
        public static string FromRevitUniqueId(string uniqueId)
        {
            if (string.IsNullOrEmpty(uniqueId)) return "";

            // The episode GUID is the first 36 chars: 8-4-4-4-12 hex digits.
            string guidPart = uniqueId.Length >= 36 ? uniqueId.Substring(0, 36) : uniqueId;
            if (!Guid.TryParse(guidPart, out var g))
                return SanitiseFallback(uniqueId);

            // Parse the trailing element-id hex (chars after the '-' at index 36).
            uint elementId = 0;
            if (uniqueId.Length > 37 && uniqueId[36] == '-')
            {
                string idHex = uniqueId.Substring(37);
                // 64-bit element ids (Revit 2024+) can exceed 8 hex chars; fold
                // the low 32 bits, matching the documented 32-bit XOR.
                // TODO-VERIFY-API: confirm >32-bit element-id folding against a
                // real Revit 2024+ export; the live-element gold standard is
                // ExporterIFCUtils.CreateGUID(el).
                if (long.TryParse(idHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long parsed))
                    elementId = unchecked((uint)parsed);
            }

            return FromGuid(FoldElementId(g, elementId));
        }

        /// <summary>
        /// Encode a <see cref="Guid"/> as a 22-char IFC GlobalId using the
        /// canonical 2/4/4/4/4/4 grouping (Autodesk RevitIFC
        /// <c>ConvertToIFCGuid</c>).
        /// </summary>
        public static string FromGuid(Guid g)
        {
            // .NET stores Data1/2/3 little-endian and Data4 big-endian in the
            // 16-byte array. The index pattern below reconstructs the GUID's
            // logical big-endian value, identical to RevitIFC GUIDUtil and the
            // original buildingSMART CreateGuid_64.c.
            byte[] b = g.ToByteArray();
            ulong[] num = new ulong[6];
            num[0] = b[3];
            num[1] = (ulong)b[2] * 65536 + (ulong)b[1] * 256 + b[0];
            num[2] = (ulong)b[5] * 65536 + (ulong)b[4] * 256 + b[7];
            num[3] = (ulong)b[6] * 65536 + (ulong)b[8] * 256 + b[9];
            num[4] = (ulong)b[10] * 65536 + (ulong)b[11] * 256 + b[12];
            num[5] = (ulong)b[13] * 65536 + (ulong)b[14] * 256 + b[15];

            char[] buf = new char[22];
            int offset = 0;
            for (int i = 0; i < 6; i++)
            {
                int len = (i == 0) ? 2 : 4;   // 2 + 5×4 = 22
                ulong value = num[i];
                for (int jj = 0; jj < len; jj++)
                {
                    buf[offset + len - jj - 1] = Alphabet[(int)(value % 64)];
                    value /= 64;
                }
                offset += len;
            }
            return new string(buf);
        }

        /// <summary>
        /// XOR an element id into the low 4 bytes of a GUID (big-endian), the
        /// fold Revit's exporter applies before compressing. Returns the GUID
        /// unchanged for element id 0.
        /// </summary>
        private static Guid FoldElementId(Guid g, uint elementId)
        {
            if (elementId == 0) return g;
            byte[] b = g.ToByteArray();
            // The GUID's logical last 4 bytes are Data4[4..7] = b[12..15],
            // stored big-endian in .NET's array.
            b[12] ^= (byte)((elementId >> 24) & 0xFF);
            b[13] ^= (byte)((elementId >> 16) & 0xFF);
            b[14] ^= (byte)((elementId >> 8) & 0xFF);
            b[15] ^= (byte)(elementId & 0xFF);
            return new Guid(b);
        }

        private static string SanitiseFallback(string raw)
        {
            // Last-resort for malformed input: hash the raw string into a Guid
            // and encode that through the same canonical path.
            using var md5 = System.Security.Cryptography.MD5.Create();
            byte[] bytes = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw ?? ""));
            return FromGuid(new Guid(bytes));
        }
    }
}
