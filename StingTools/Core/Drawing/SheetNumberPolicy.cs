// StingTools — Drawing Template Manager · Sheet-number policy
//
// THE PROBLEM THIS SOLVES
//
// All 90 drawing types that carry an isoNaming block declare the full
// ISO 19650-2 field set — volume, level, type, role, suitability,
// revision — yet only 9 of the 93 profiles NUMBER by it. The other 84
// use bespoke short codes: "A-RCP-{lvl}-{seq:D3}", "HO-{seq:D3}",
// "LG-{seq:D2}", "RFI-{seq:D4}", "P-DRN-SCH-{seq:D3}". So a project that
// has adopted ISO 19650 numbering gets it on nine drawings and legacy
// short numbers on eighty-four, while every profile carries the ISO
// metadata needed to do it properly. DT-096 only warns on the inverse
// case (ISO tokens with no isoNaming), so the mismatch was invisible.
//
// WHY NOT JUST REWRITE THE 84 PATTERNS
//
// Because both conventions are legitimate and projects in flight depend
// on their existing numbers. Rewriting the data would silently renumber
// every sheet in every open project. And a per-type second pattern field
// would mean 93 more strings to keep in sync — the same drift, doubled.
//
// THE APPROACH
//
// One project-level policy, and the ISO number is DERIVED rather than
// authored. Because isoNaming already holds every field, the ISO pattern
// is a pure function of the profile: no new per-type data, nothing to
// drift, and a project switches convention by setting one parameter.
//
//   PRJ_ORG_SHEET_NUMBER_POLICY_TXT = "short"  (default — unchanged)
//                                   = "iso"    (ISO 19650-2 numbering)
//                                   = "profile"(honour each profile's own
//                                               pattern verbatim, even if
//                                               it is already ISO)
//
// "short" and "profile" behave identically today; they are distinct so a
// project can say "deliberately per-profile" rather than "not decided".
//
// Default is "short", so this file changes NO existing behaviour until a
// project opts in. That is deliberate: a numbering change is a document
// -control event, not a side effect of a plugin update.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace StingTools.Core.Drawing
{
    /// <summary>How a project wants sheet numbers composed.</summary>
    public enum SheetNumberPolicyKind
    {
        /// <summary>Each profile's own sheetNumberPattern, verbatim. The default.</summary>
        Profile = 0,
        /// <summary>Full ISO 19650-2 field order, derived from the profile's isoNaming block.</summary>
        Iso = 1,
    }

    /// <summary>
    /// Resolves the sheet-number pattern a profile should use under the
    /// active project policy. Revit-free and side-effect-free, so the
    /// precedence rules are unit-testable without a document.
    /// </summary>
    public static class SheetNumberPolicy
    {
        /// <summary>
        /// ProjectInformation parameter that selects the policy. Sourced
        /// through ParamRegistry at the call site; named here so the string
        /// lives in one place.
        /// </summary>
        public const string PolicyParameterName = "PRJ_ORG_SHEET_NUMBER_POLICY_TXT";

        /// <summary>
        /// The canonical ISO 19650-2 container identifier: Project–Originator–
        /// Volume–Level–Type–Role–Number.
        /// <para>
        /// DTW-44: it used to end "-{suit}-{rev}". Those two are METADATA of the
        /// container in ISO 19650-2 (§5.1.7 / Annex A status and revision codes),
        /// not part of its identifier — and here they were frozen at the profile's
        /// values when the sheet was numbered (S2-P01), so a sheet issued at A1-C02
        /// still carried "S2-P01" in its number. Suitability and revision are
        /// stamped on the sheet and printed by the title block instead.
        /// Existing sheets keep their numbers: production finds a sheet by its
        /// context stamp, never by re-deriving its number, so only sheets created
        /// from now on take the shorter form.
        /// </para>
        /// </summary>
        public const string IsoPattern =
            "{project}-{originator}-{vol}-{lvl}-{type}-{role}-{seq:D4}";

        /// <summary>Parse the policy string. Anything unrecognised — including null — is Profile.</summary>
        public static SheetNumberPolicyKind Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return SheetNumberPolicyKind.Profile;
            switch (value.Trim().ToLowerInvariant())
            {
                case "iso":
                case "iso19650":
                case "iso-19650":
                    return SheetNumberPolicyKind.Iso;
                case "short":
                case "profile":
                case "legacy":
                    return SheetNumberPolicyKind.Profile;
                default:
                    return SheetNumberPolicyKind.Profile;
            }
        }

        /// <summary>
        /// True when <paramref name="value"/> is a spelling Parse knows. Parse reads
        /// anything else as Profile, so a typo ("iso 19650") quietly means per-drawing-type
        /// numbering; callers use this to say so instead.
        /// </summary>
        public static bool IsRecognised(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            switch (value.Trim().ToLowerInvariant())
            {
                case "iso": case "iso19650": case "iso-19650":
                case "short": case "profile": case "legacy":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The policy a wizard should record: null when the picker still shows what it was
        /// pre-populated with (nobody chose — leave the parameter alone), else the pick.
        /// Without this, opening and running the wizard on a fresh project wrote "profile"
        /// though no one had decided anything.
        /// </summary>
        public static SheetNumberPolicyKind? ChosenOrNull(string pickedTag, string prePopulatedTag)
        {
            if (string.IsNullOrEmpty(pickedTag)) return null;
            if (prePopulatedTag != null && string.Equals(pickedTag, prePopulatedTag, StringComparison.Ordinal)) return null;
            return Parse(pickedTag);
        }

        /// <summary>The value a UI writes for <paramref name="kind"/>. Parse reads it back as the same kind.</summary>
        public static string ToParameterValue(SheetNumberPolicyKind kind)
            => kind == SheetNumberPolicyKind.Iso ? "iso" : "profile";

        /// <summary>
        /// What to write to the policy parameter when a user picks
        /// <paramref name="chosen"/> and the project already holds
        /// <paramref name="stored"/>. Null means write nothing: the stored
        /// value already means that policy. A synonym such as "short" or
        /// "ISO19650" is left as the user typed it rather than rewritten to the
        /// canonical spelling for no change in behaviour. An empty value is
        /// written, so the choice is recorded as a decision, not a default; so is
        /// an unrecognised one, which Parse only reads as Profile by default.
        /// </summary>
        public static string ValueToWrite(string stored, SheetNumberPolicyKind chosen)
            => IsRecognised(stored) && Parse(stored) == chosen ? null : ToParameterValue(chosen);

        /// <summary>
        /// True when <paramref name="pattern"/> already composes an ISO
        /// number — it references the project / originator / volume tokens.
        /// Used so the ISO policy leaves the 9 profiles that already comply
        /// untouched rather than rewriting them to an identical string.
        /// </summary>
        public static bool IsAlreadyIso(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return false;
            return pattern.IndexOf("{project}", StringComparison.OrdinalIgnoreCase) >= 0
                && pattern.IndexOf("{originator}", StringComparison.OrdinalIgnoreCase) >= 0
                && pattern.IndexOf("{vol}", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// The pattern to use for <paramref name="dt"/> under
        /// <paramref name="policy"/>.
        ///
        /// Under Iso, a profile is switched to <see cref="IsoPattern"/> only
        /// when it actually has an isoNaming block to fill it — without one
        /// the ISO tokens would resolve to empty segments and produce
        /// "--ZZ--DR--0001--", which is worse than a short code. Such a
        /// profile keeps its own pattern and <paramref name="note"/> explains
        /// why, so the exception is reported rather than silent.
        /// </summary>
        public static string ResolvePattern(DrawingType dt, SheetNumberPolicyKind policy, out string note)
        {
            note = null;
            if (dt == null) return null;
            var own = dt.SheetNumberPattern;

            if (policy != SheetNumberPolicyKind.Iso) return own;
            if (IsAlreadyIso(own)) return own;

            if (dt.IsoNaming == null)
            {
                note = $"DrawingType '{dt.Id}' has no isoNaming block, so the ISO sheet-number policy cannot "
                     + "be applied to it; its own pattern is used. Add an isoNaming block to bring it into "
                     + "the ISO scheme.";
                return own;
            }

            note = $"DrawingType '{dt.Id}': ISO sheet-number policy active — pattern '{own}' replaced by "
                 + $"'{IsoPattern}'.";
            return IsoPattern;
        }

        private static readonly Regex _statusSuffix =
            new Regex(@"-(S\d|A\d{1,2}|B\d{1,2}|CR|AB|AR)-[PC]\d{2,3}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// DTW-44: a sheet number with the suitability-revision tail the old
        /// <see cref="IsoPattern"/> appended ("…-0003-S2-P01") cut back to its container
        /// identifier ("…-0003"); any other number unchanged. Used to seed the ISO counter
        /// from sheets numbered before the tail was dropped, so a new sheet does not take
        /// a sequence an existing one already carries.
        /// </summary>
        public static string StripStatusSuffix(string sheetNumber)
            => string.IsNullOrEmpty(sheetNumber) ? sheetNumber : _statusSuffix.Replace(sheetNumber, "");

        /// <summary>
        /// DTW-43: the value {lvl} takes in <paramref name="pattern"/> for the level
        /// called <paramref name="levelName"/>. An ISO-shaped number carries the ISO
        /// 19650 level code — the one the sheet's own level stamp uses
        /// (IsoLevelCode, built over every level in <paramref name="isoCodesByName"/>) —
        /// not the level's name cut to eight characters, which gave "Level1" and
        /// "Mezzanin" and let "Level 10" and "Level 1 A" collide. Any other pattern
        /// keeps the name, as before. Null in, null out (the profile's fallback applies).
        /// </summary>
        public static string LevelToken(string pattern, string levelName, IDictionary<string, string> isoCodesByName)
        {
            if (levelName == null) return null;
            if (!IsAlreadyIso(pattern)) return levelName;
            if (isoCodesByName != null && isoCodesByName.TryGetValue(levelName, out var code) && !string.IsNullOrWhiteSpace(code))
                return code;
            return IsoLevelCode.FromNameOnly(levelName);
        }

        /// <summary>
        /// Convenience overload without the note.
        /// </summary>
        public static string ResolvePattern(DrawingType dt, SheetNumberPolicyKind policy)
            => ResolvePattern(dt, policy, out _);
    }
}
