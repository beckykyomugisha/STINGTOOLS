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
            if (lower.StartsWith("roof") || lower == "rf")
                return "RF";
            if (lower.StartsWith("penthouse") || lower == "ph" || lower == "pent")
                return "PH";
            if (lower.StartsWith("attic") || lower == "at" || lower == "att")
                return "AT";
            if (lower.StartsWith("terrace") || lower == "tr")
                return "TR";
            if (lower.StartsWith("podium") || lower == "pod")
                return "POD";
            if (lower.StartsWith("mezzanine") || lower == "mezz")
                return "MZ";
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
        public static string ZoneFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string upper = text.ToUpperInvariant();

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

        private static string ExtractDigits(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "")
                if (c >= '0' && c <= '9') sb.Append(c);
            return sb.ToString();
        }
    }
}
