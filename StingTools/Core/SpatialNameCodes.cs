// SpatialNameCodes — the LVL / ZONE / LOC codes read out of names (level names,
// room names, room numbers, workset names). Revit-free so the rules are unit-tested
// (StingTools.Tags.Tests/SpatialNameCodesTests.cs); SpatialAutoDetect and
// ParameterHelpers.GetLevelCodeForLevel call it.
//
// WHAT WAS WRONG (tagging accuracy review, 2026-09-29)
//
// Every rule here used String.Contains, so a code matched inside a longer one:
//   "Zone 12" / "Z012"      → Z01      "Building 12" / "BLD10" → BLD1
//   "Wing Annex"            → Z01      "Block AB"              → BLD1
//   "21st Floor"            → L01      "22nd" → L02, "23rd" → L03, "Twenty-First" → L01
// and two level rules produced a code that could not be used at all:
//   "Level -1"              → L01, the same code as Level 1
//   "Ring beam"             → "RING-BEAM". The hyphen is the tag separator, so the
//     stored LVL read back as "RING" (the token sanitiser cuts at the separator) and
//     an Overwrite run built a nine-segment tag and refused to write it.
//
// Codes now match only as whole tokens: the characters either side of a match must
// not be a letter or a digit.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace StingTools.Core
{
    public static class SpatialNameCodes
    {
        /// <summary>Longest passthrough level code. Unchanged from the original rule.</summary>
        public const int MaxPassthroughLength = 12;

        /// <summary>The level code for a level name. "XX" when the name carries nothing usable.</summary>
        public static string LevelCodeFromName(string levelName)
            => LevelCodeFromName(levelName, out _);

        /// <summary>
        /// The level code for a level name. <paramref name="passthrough"/> is true when no
        /// rule recognised the name and the code is the sanitised name itself.
        /// </summary>
        public static string LevelCodeFromName(string levelName, out bool passthrough)
        {
            passthrough = false;
            if (string.IsNullOrWhiteSpace(levelName)) return "XX";

            string name = levelName.Trim();
            string lower = name.ToLowerInvariant();

            if (lower.StartsWith("level ") && name.Length > 6)
            {
                string rest = name.Substring(6).Trim();

                // "Level -1", "Level −2" (U+2212): below ground. It used to lose the sign
                // and collide with Level 1. Same code the basement rule below gives.
                var below = Regex.Match(rest, @"^[-−]\s*(\d{1,2})(?!\d)");
                if (below.Success)
                    return "B" + int.Parse(below.Groups[1].Value);

                // TAGACC-10: "Level 2 Mezzanine" is its own level, not Level 2.
                if (rest.ToLowerInvariant().Contains("mezz"))
                    return WithNumber("MZ", rest);

                // TAGACC-10: "Level 1a" / "Level 1 B" are distinct from Level 1.
                var lettered = Regex.Match(rest, @"^(\d{1,3})\s*([A-Za-z])$");
                if (lettered.Success)
                    return "L" + lettered.Groups[1].Value.PadLeft(2, '0') + lettered.Groups[2].Value.ToUpperInvariant();

                string suffix = ExtractDigits(rest);
                if (suffix.Length > 0 && suffix.Length <= 3)
                    return "L" + suffix.PadLeft(2, '0');
            }
            if (lower == "ground" || lower == "ground floor" || lower == "ground level")
                return "GF";
            if (lower.StartsWith("lower ground") || lower == "lg")
                return "LG";
            if (lower.StartsWith("upper ground") || lower == "ug")
                return "UG";
            if (lower.StartsWith("sub-basement") || lower.StartsWith("sub basement") || lower == "sb")
            {
                string sbDigits = ExtractDigits(name);
                return "SB" + sbDigits;
            }
            if (lower.StartsWith("basement") ||
                (lower.Length >= 2 && lower[0] == 'b' && char.IsDigit(lower[1])))
            {
                string bDigits = ExtractDigits(name);
                return "B" + (bDigits.Length > 0 ? bDigits : "1");
            }
            // Keyword levels keep a standalone number ("Roof 2" → RF2, "Mezzanine 2" → MZ2),
            // so two such levels no longer share one code (TAGACC-10). A roof plant deck is
            // RFP, not the roof. A number glued to punctuation ("Roof +45.000") is an
            // elevation, not a level number, and is ignored.
            if (lower.StartsWith("roof") || lower == "rf")
                return WithNumber(lower.Contains("plant") ? "RFP" : "RF", name);
            if (lower.StartsWith("penthouse") || lower == "ph" || lower == "pent")
                return WithNumber("PH", name);
            if (lower.StartsWith("attic") || lower == "at" || lower == "att")
                return WithNumber("AT", name);
            if (lower.StartsWith("terrace") || lower == "tr")
                return WithNumber("TR", name);
            if (lower.StartsWith("podium") || lower == "pod")
                return WithNumber("POD", name);
            if (lower.StartsWith("mezzanine") || lower == "mezz")
                return WithNumber("MZ", name);
            if (lower.StartsWith("plant") && lower.Contains("room"))
                return "PL";

            // Numeric ordinal as a whole number: "21st" is L21, not L01 because it
            // contains "1st".
            var ordinal = Regex.Match(lower, @"(?<!\d)(\d{1,3})\s*(st|nd|rd|th)(?![a-z])");
            if (ordinal.Success)
                return "L" + ordinal.Groups[1].Value.TrimStart('0').PadLeft(2, '0');

            // Ordinal words, only when not the tail of a compound ("twenty-first").
            string[] words = { "first", "second", "third", "fourth", "fifth" };
            for (int i = 0; i < words.Length; i++)
                if (Regex.IsMatch(lower, @"(?<![a-z\-])" + words[i]))
                    return "L" + (i + 1).ToString("00");

            // "L01", "L1", "Floor 3".
            string digits = ExtractDigits(name);
            if (digits.Length > 0 && digits.Length <= 3)
                return "L" + digits.PadLeft(2, '0');

            // Unrecognised but real: pass the name through as letters and digits only.
            // Anything else (a space became "-" here) can be the tag separator.
            string sane = Regex.Replace(name, @"[^A-Za-z0-9]", "").ToUpperInvariant();
            if (sane.Length > MaxPassthroughLength) sane = sane.Substring(0, MaxPassthroughLength);
            if (sane.Length == 0) return "XX";
            passthrough = true;
            return sane;
        }

        /// <summary>
        /// ZONE code named in free text (room department, room name, room number, workset
        /// name), or null. Recognises Z01–Z04, "Zone 1–4", "Zone A–D", "Wing A–D" and the
        /// four compass words, each as a whole token.
        /// </summary>
        public static string ZoneFromText(string text) => ZoneFromText(text, null);

        /// <summary>
        /// As <see cref="ZoneFromText(string)"/>, but a code the project declares
        /// (ZONE_CODES in project_config.json, e.g. "WARD1", "ZA") is tried first, as a
        /// whole token, longest first. XX and ZZ are placeholders and never matched.
        /// TAGACC-9: only Z01–Z04 and their aliases were ever recognised.
        /// </summary>
        public static string ZoneFromText(string text, IEnumerable<string> declaredCodes)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string upper = text.ToUpperInvariant();

            if (declaredCodes != null)
            {
                foreach (string code in declaredCodes
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(c => c.Trim().ToUpperInvariant())
                    // One-letter codes ("A") would match "Store A"; they need a room department.
                    .Where(c => c.Length >= 2 && c != "XX" && c != "ZZ")
                    .Distinct()
                    .OrderByDescending(c => c.Length))
                {
                    if (ContainsToken(upper, code)) return code;
                }
            }

            string[] letters = { "A", "B", "C", "D" };
            for (int i = 0; i < 4; i++)
            {
                string n = (i + 1).ToString();
                if (ContainsToken(upper, "Z0" + n) || ContainsToken(upper, "ZONE " + n)
                    || ContainsToken(upper, "ZONE " + letters[i]) || ContainsToken(upper, "WING " + letters[i]))
                    return "Z0" + n;
            }

            if (ContainsToken(upper, "NORTH")) return "Z01";
            if (ContainsToken(upper, "SOUTH")) return "Z02";
            if (ContainsToken(upper, "EAST"))  return "Z03";
            if (ContainsToken(upper, "WEST"))  return "Z04";
            return null;
        }

        /// <summary>
        /// TAGACC-15: ZONE from a room's Department field. The field exists to hold a zone, so
        /// when its whole value IS a declared code it is that zone — at any length, so a
        /// project using one-letter zones ("A", "B") is recognised here. Anything else falls
        /// back to <see cref="ZoneFromText(string, IEnumerable{string})"/>, where one-letter
        /// codes are not matched inside free text ("Store A" is not zone A).
        /// </summary>
        public static string ZoneFromDepartment(string department, IEnumerable<string> declaredCodes)
        {
            if (string.IsNullOrWhiteSpace(department)) return null;
            string whole = department.Trim().ToUpperInvariant();
            if (declaredCodes != null && whole != "XX" && whole != "ZZ")
                foreach (string c in declaredCodes)
                    if (!string.IsNullOrWhiteSpace(c) && string.Equals(c.Trim(), whole, StringComparison.OrdinalIgnoreCase))
                        return c.Trim().ToUpperInvariant();
            return ZoneFromText(department, declaredCodes);
        }

        /// <summary>
        /// The built-in LOC aliases (BLD1–BLD3, "Building 1–3", "Block A–C", EXT), each as
        /// a whole token, or null. The project/corporate SpatialCodeRegistry is consulted
        /// before this and wins.
        /// </summary>
        public static string LocFromTextFallback(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string upper = text.ToUpperInvariant();

            string[] letters = { "A", "B", "C" };
            for (int i = 0; i < 3; i++)
            {
                string n = (i + 1).ToString();
                if (ContainsToken(upper, "BLD" + n) || ContainsToken(upper, "BUILDING " + n)
                    || ContainsToken(upper, "BLOCK " + letters[i]))
                    return "BLD" + n;
            }

            // Whole-token EXT so "NEXT", "TEXTILE", "EXTENSION" never match.
            if (ContainsToken(upper, "EXT") || upper.Contains("EXTERNAL") || upper.Contains("EXTERIOR"))
                return "EXT";
            return null;
        }

        /// <summary>
        /// True when <paramref name="token"/> occurs in <paramref name="text"/> with no
        /// letter or digit immediately either side. Both are expected upper-case.
        /// </summary>
        public static bool ContainsToken(string text, string token)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token)) return false;
            int idx = text.IndexOf(token, StringComparison.Ordinal);
            while (idx >= 0)
            {
                int end = idx + token.Length;
                bool startOk = idx == 0 || !char.IsLetterOrDigit(text[idx - 1]);
                bool endOk = end >= text.Length || !char.IsLetterOrDigit(text[end]);
                if (startOk && endOk) return true;
                idx = text.IndexOf(token, idx + 1, StringComparison.Ordinal);
            }
            return false;
        }

        /// <summary><paramref name="code"/> plus a standalone one- or two-digit number in
        /// <paramref name="text"/> ("2", "L2"), or the bare code when there is none.</summary>
        private static string WithNumber(string code, string text)
        {
            var m = Regex.Match(text ?? "", @"(?:^|\s)[Ll]?(\d{1,2})(?=\s|$)");
            return m.Success ? code + int.Parse(m.Groups[1].Value) : code;
        }

        private static string ExtractDigits(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "")
                if (c >= '0' && c <= '9') sb.Append(c);
            return sb.ToString();
        }
    }
}
