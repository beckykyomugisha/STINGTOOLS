using System;
using System.Globalization;
using System.Linq;

namespace StingTools.IfcResults
{
    /// <summary>
    /// IFC GlobalId encoding per the buildingSMART specification. Converts a
    /// 128-bit .NET <see cref="Guid"/> to the 22-character base64-ish string over
    /// the alphabet <c>0-9 A-Z a-z _ $</c>, grouping the 16 bytes as
    /// <c>1 / 3 / 3 / 3 / 3 / 3</c> bytes → <c>2 / 4 / 4 / 4 / 4 / 4</c> chars.
    /// The base64 compression (<see cref="FromGuid"/>) is the canonical
    /// algorithm — identical to Autodesk RevitIFC <c>GUIDUtil.ConvertToIFCGuid</c>,
    /// IfcOpenShell, and xbim — and is pinned by an independently-verified
    /// reference vector in <c>StingTools.Boq.Tests/IfcGuidEncoderTests.cs</c>.
    ///
    /// <para><b>Revit UniqueId → IFC GlobalId.</b> A Revit
    /// <c>Element.UniqueId</c> is a 45-char string:
    /// <c>&lt;36-char episode GUID&gt;-&lt;8-hex suffix&gt;</c>. It is NOT a valid
    /// <c>IfcGloballyUniqueId</c>. <see cref="FromRevitUniqueId"/> reconstructs the
    /// export GUID per the documented relationship — the suffix is XOR-folded into
    /// the GUID's low 4 bytes (because <c>suffix = origLow4 XOR elementId</c>, the
    /// fold places the true element id in those bytes), then base64-compressed. The
    /// fold MUST happen: two elements created in the same episode share the GUID
    /// part and differ only in the suffix.</para>
    ///
    /// <para><b>Verification scope (read before trusting for external interop).</b>
    /// The compression is proven canonical. The UniqueId→GUID *reconstruction*
    /// follows the documented episode+XOR-suffix algorithm and is element-id
    /// sensitive, but is NOT pinned against a real Revit IFC export, so exact
    /// equality with what Revit's exporter writes is asserted only at the
    /// compression layer. For a guaranteed match where a live <c>Element</c> is
    /// available, use <see cref="FromElementGoldStandard"/>, which calls
    /// <c>ExporterIFCUtils.CreateGUID(el)</c> at runtime via reflection. This
    /// string encoder is the equivalent for the BOQ snapshot path (UniqueId string
    /// only) and is the single resolver so BOQ rows and COBie Components for the
    /// same element produce identical GlobalIds.</para>
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

            // Parse the trailing suffix hex (chars after the '-' at index 36).
            // NOTE: this value is the UniqueId *suffix*, not the element id itself
            // (suffix = original-low-4-bytes XOR elementId). XOR-folding the suffix
            // into the GUID's low 4 bytes therefore yields the true element id in
            // those bytes — exactly the export-GUID reconstruction.
            uint suffix = 0;
            if (uniqueId.Length > 37 && uniqueId[36] == '-')
            {
                string idHex = uniqueId.Substring(37);
                // 64-bit element ids (Revit 2024+) can give a longer suffix; fold
                // the low 32 bits, matching the documented 32-bit XOR.
                // TODO-VERIFY-API: confirm >32-bit suffix folding against a real
                // Revit 2024+ export; the live-element gold standard is
                // FromElementGoldStandard (ExporterIFCUtils.CreateGUID).
                if (long.TryParse(idHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long parsed))
                    suffix = unchecked((uint)parsed);
            }

            return FromGuid(FoldSuffix(g, suffix));
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
        /// XOR the UniqueId suffix into the low 4 bytes of a GUID (big-endian),
        /// the fold Revit's exporter applies before compressing. Because
        /// <c>suffix = origLow4 XOR elementId</c>, the result carries the true
        /// element id in those bytes. Returns the GUID unchanged for suffix 0.
        /// </summary>
        private static Guid FoldSuffix(Guid g, uint suffix)
        {
            if (suffix == 0) return g;
            byte[] b = g.ToByteArray();
            // The GUID's logical last 4 bytes are Data4[4..7] = b[12..15],
            // stored big-endian in .NET's array.
            b[12] ^= (byte)((suffix >> 24) & 0xFF);
            b[13] ^= (byte)((suffix >> 16) & 0xFF);
            b[14] ^= (byte)((suffix >> 8) & 0xFF);
            b[15] ^= (byte)(suffix & 0xFF);
            return new Guid(b);
        }

        /// <summary>
        /// Gold-standard IFC GlobalId for a live Revit <c>Element</c>: calls
        /// <c>Autodesk.Revit.DB.IFC.ExporterIFCUtils.CreateGUID(Element)</c> at
        /// runtime via reflection, returning the EXACT GlobalId Revit's IFC
        /// exporter assigns. <c>RevitAPIIFC.dll</c> is loaded inside Revit but is
        /// intentionally NOT a compile-time reference of StingTools (mirrors the
        /// policy in <c>ClashExportContext.TryGetIfcGuid</c>), so this resolves the
        /// type by name. Falls back to <see cref="FromRevitUniqueId"/> (the
        /// canonical string reconstruction) when RevitAPIIFC is unavailable — e.g.
        /// in unit tests or a non-Revit host.
        /// <para><paramref name="element"/> is typed <see cref="object"/> so this
        /// assembly needs no IFC reference and the fallback is unit-testable with a
        /// stub exposing a string <c>UniqueId</c> property.</para>
        /// </summary>
        public static string FromElementGoldStandard(object element)
        {
            if (element == null) return "";
            string uniqueId = element.GetType().GetProperty("UniqueId")?.GetValue(element) as string ?? "";
            try
            {
                // TODO-VERIFY-API: confirm the ExporterIFCUtils.CreateGUID(Element)
                // overload + the "RevitAPIIFC" assembly name on the target Revit
                // (2025/2026/2027) — both have been stable but are version-sensitive.
                var t = Type.GetType("Autodesk.Revit.DB.IFC.ExporterIFCUtils, RevitAPIIFC");
                var m = t?.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    .FirstOrDefault(x => x.Name == "CreateGUID"
                        && x.GetParameters().Length == 1
                        && x.GetParameters()[0].ParameterType.Name == "Element");
                if (m != null && m.Invoke(null, new[] { element }) is string s && !string.IsNullOrEmpty(s))
                    return s;
            }
            catch (Exception ex)
            {
                WarnGoldStandardOnce(ex.Message);
            }
            // Canonical string reconstruction (compression verified; fold per the
            // documented episode + XOR-suffix algorithm).
            return FromRevitUniqueId(uniqueId);
        }

        private static bool _goldStandardWarned;

        private static void WarnGoldStandardOnce(string msg)
        {
            if (_goldStandardWarned) return;
            _goldStandardWarned = true;
            // Log via reflection so this file stays dependency-free — it is linked
            // standalone into StingTools.Boq.Tests (no StingTools.Core reference).
            // Resolves StingLog inside the real plugin; silently no-ops in tests.
            // Logging must never break a COBie/handover export.
            try
            {
                Type.GetType("StingTools.Core.StingLog, StingTools")?
                    .GetMethod("Warn", new[] { typeof(string) })?
                    .Invoke(null, new object[]
                    {
                        "IfcGuidEncoder.FromElementGoldStandard: ExporterIFCUtils.CreateGUID " +
                        "unavailable; using canonical string encoder. " + msg
                    });
            }
            catch { }
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
