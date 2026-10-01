// StingTools — scope-box names that mean to be STING-LOC / ZONE / AREA boxes and do not parse
//
// DTW-93 made one strict grammar for scope-box names (ScopeBoxNames). A name a person
// typed by hand — "STING-LOC::Block A" (a space), "STING-LOCATION::X", "STING-LOC:X" —
// is refused, and the elements inside it quietly take the fallback LOC / ZONE. The
// refusal was only a log line (and for a near-miss prefix, which classifies as a plain
// box, not even that).
//
// DTW-144 makes it visible without relaxing the grammar: this names every box whose
// name claims (or nearly claims) a STING-LOC / STING-ZONE / STING-AREA prefix and does
// not parse, with the reason. Tagging reports them beside its result; the Drawing
// Doctor lists them.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;
using System.Text.RegularExpressions;

namespace StingTools.Core.Drawing
{
    public static class ScopeBoxNameAudit
    {
        // "STING", optional separator, a word that names a LOC / ZONE / AREA box, then a colon.
        private static readonly Regex _nearMiss = new Regex(
            @"^\s*STING[\s_\-]*(?<word>LOC|LOCS|LOCATION|LOCATIONS|BUILDING|BLD|ZONE|ZONES|AREA|AREAS)\s*:",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Why <paramref name="name"/> — a scope box that claims to be a STING-LOC, STING-ZONE
        /// or STING-AREA box — is not one; null when it parses, or when it does not claim
        /// any of those (a plain box, a seed box, a STING:: drawing-type box).
        /// </summary>
        public static string Problem(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            switch (ScopeBoxNames.Classify(name))
            {
                case ScopeBoxKind.Building:
                    return ScopeBoxNames.TryParseLoc(name, out _, out var lr) ? null : lr ?? ScopeBoxNames.LocPatternReason;
                case ScopeBoxKind.Zone:
                    return ScopeBoxNames.TryParseZone(name, out _, out var zr) ? null : zr ?? ScopeBoxNames.ZonePatternReason;
                case ScopeBoxKind.Area:
                    return ScopeBoxNames.TryParseArea(name, out _, out _, out var ar) ? null : ar ?? ScopeBoxNames.AreaPatternReason;
                case ScopeBoxKind.Plain:
                    var m = _nearMiss.Match(name);
                    if (!m.Success) return null;
                    string want = Intended(m.Groups["word"].Value);
                    return $"the prefix is not {want} — write {want}<code> (one code; allowed chars: A-Z 0-9 . _ -)";
                default:
                    return null;
            }
        }

        /// <summary>The prefix a near-miss word means.</summary>
        private static string Intended(string word)
        {
            var w = (word ?? "").ToUpperInvariant();
            if (w.StartsWith("ZONE", StringComparison.Ordinal)) return ScopeBoxNames.ZonePrefix;
            if (w.StartsWith("AREA", StringComparison.Ordinal)) return ScopeBoxNames.AreaPrefix;
            return ScopeBoxNames.LocPrefix;
        }
    }
}
