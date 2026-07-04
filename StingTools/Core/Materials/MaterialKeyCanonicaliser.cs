// ══════════════════════════════════════════════════════════════════════════
//  MaterialKeyCanonicaliser.cs — RC-1 param-value normalisation.
//
//  The compound take-off composed lookup keys RAW, so BLE_BLOCK_SIZE_TXT variants
//  (440X215 / 440×215 / 440 x 215 / 215x440) and BLE_MORTAR_MIX_TXT "1 : 6" all
//  missed the table and SILENTLY fell to DEFAULT — a plausible-but-wrong quantity
//  (a 1:3 structural mortar priced at half cement; a 25% block-count error). This
//  canonicaliser upper-cases, collapses whitespace, maps × → x, orders block
//  sizes largest-first, and maps common aliases, so a value that denotes a
//  catalogued type actually resolves. Document-free + unit-tested.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace StingTools.Core.Materials
{
    public static class MaterialKeyCanonicaliser
    {
        /// <summary>Block size → "LxH" largest-first (e.g. 215x440 / 440×215 /
        /// "440 x 215" → "440x215"). Empty in → "".</summary>
        public static string BlockSize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string s = raw.Trim().ToUpperInvariant()
                .Replace('×', 'x').Replace('X', 'x').Replace('*', 'x');
            s = Regex.Replace(s, @"\s+", "");           // strip all whitespace
            // Extract the two leading integers around the first 'x'.
            var m = Regex.Match(s, @"(\d+)x(\d+)");
            if (m.Success
                && int.TryParse(m.Groups[1].Value, out int a)
                && int.TryParse(m.Groups[2].Value, out int b))
            {
                int hi = Math.Max(a, b), lo = Math.Min(a, b);
                return $"{hi}x{lo}";
            }
            return s;
        }

        /// <summary>Mortar mix → "1:N" with whitespace around ':' collapsed
        /// ("1 : 6" → "1:6"). cement:lime:sand designations pass through upper-cased.</summary>
        public static string MortarMix(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string s = raw.Trim().ToUpperInvariant();
            s = Regex.Replace(s, @"\s*:\s*", ":");      // collapse space around ':'
            s = Regex.Replace(s, @"\s+", "");           // strip remaining whitespace
            // Alias designations (i)-(iv) → the MORTAR_CL keys.
            switch (s)
            {
                case "M(I)": case "(I)": case "M-I": case "MI": return "M_I";
                case "M(II)": case "(II)": case "M-II": case "MII": return "M_II";
                case "M(III)": case "(III)": case "M-III": case "MIII": return "M_III";
                case "M(IV)": case "(IV)": case "M-IV": case "MIV": return "M_IV";
            }
            return s;
        }

        /// <summary>Brick bond → the BRICK_BOND TypeKey form (UPPER, spaces →
        /// underscore). "english garden wall" → "ENGLISH_GARDEN_WALL".</summary>
        public static string BrickBond(string raw)
        {
            string s = Normalise(raw);
            if (s.Length == 0) return "";
            switch (s)
            {
                case "RUNNING": case "HALF_BRICK": return "STRETCHER";
                case "ENGLISH_GARDEN": case "GARDEN_WALL": return "ENGLISH_GARDEN_WALL";
            }
            return s;
        }

        /// <summary>Plaster type → the PLASTER TypeKey form (UPPER, spaces →
        /// underscore). "thin coat" → "THIN_COAT".</summary>
        public static string PlasterType(string raw)
        {
            string s = Normalise(raw);
            if (s.Length == 0) return "";
            switch (s)
            {
                case "RENDER": case "EXTERNAL": return "THIN_COAT";
                case "TWO_COAT": case "INTERNAL": return "STANDARD";
                case "ROUGH_CAST": case "ROUGHCAST": return "THICK";
            }
            return s;
        }

        // UPPER, trim, collapse internal whitespace → single underscore.
        private static string Normalise(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string s = raw.Trim().ToUpperInvariant();
            s = Regex.Replace(s, @"[\s\-]+", "_");
            s = Regex.Replace(s, @"_+", "_").Trim('_');
            return s;
        }
    }
}
