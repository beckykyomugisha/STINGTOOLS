// StingTools — ISO 19650 suitability → what a title block shows.
//
// ONE input, PRJ_DWG_SUITABILITY_COD_TXT, and everything else derived from it:
// the code (normalised), the ISO description, the CDE container (WIP / SHARED
// / PUBLISHED), and — new — a NUMBER for that container, PRJ_TB_CDE_STATE_INT,
// which drives the coloured status band in the title-block family.
//
// WHY A NUMBER. Revit family formulas cannot compare text, but they can compare
// integers, so each band's visibility is a Yes/No with the formula
// "PRJ_TB_CDE_STATE_INT = n". Only one band can be on; 0 (unknown) shows none,
// so an unrecognised code prints no colour rather than a plausible one.
//
// WHY ONE FUNCTION. Title Block Populate derived the CDE state from the code;
// TitleBlockRevisionSyncer wrote the code from a revision's "Issued to" field
// and did NOT re-derive the state — so after a revision sync a sheet could read
// suitability A1 while STATUS still said SHARED. Both now call Derive().
//
// Colour is office convention, not an ISO requirement (BS EN ISO 19650-2 and
// the UK NA define the codes and CDE states, not colours). The band therefore
// always sits beside the printed code: a monochrome print or a colour-blind
// reader loses nothing.
//
// Revit-free, so the mapping and the palette are unit-tested.

using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;

namespace StingTools.Core.Drawing
{
    /// <summary>CDE container as the number PRJ_TB_CDE_STATE_INT carries.</summary>
    public enum CdeState
    {
        Unknown = 0,
        Wip = 1,
        Shared = 2,
        Published = 3,
        /// <summary>Written only by Supersede / Cancel — no suitability code means "archived".</summary>
        Archived = 4,
    }

    /// <summary>Everything a title block needs, derived from one suitability value.</summary>
    public sealed class SuitabilityDerivation
    {
        /// <summary>Normalised code ("S2"), or "" when none could be read.</summary>
        public string Code { get; set; } = "";
        /// <summary>ISO wording, or null for an unrecognised code.</summary>
        public string Description { get; set; }
        /// <summary>"WIP" / "SHARED" / "PUBLISHED", or null when unknown.</summary>
        public string CdeStateName { get; set; }
        public CdeState State { get; set; }
        public int StateInt => (int)State;
        public bool IsKnown => State != CdeState.Unknown;
    }

    /// <summary>One band colour. Deserialised from STING_TITLE_BLOCKS.json "cdeBands".</summary>
    public sealed class CdeBandSpec
    {
        [JsonProperty("state")] public int State { get; set; }
        [JsonProperty("color")] public string Color { get; set; }
    }

    public static class SuitabilityPresentation
    {
        /// <summary>Name of the driving parameter (shared, INTEGER, sheet-bound).</summary>
        public const string StateParameter = "PRJ_TB_CDE_STATE_INT";

        /// <summary>Family-internal Yes/No that shows one band; one per state.</summary>
        public static string BandParameterFor(CdeState state)
            => "STING_CDE_BAND_" + state.ToString().ToUpperInvariant();

        /// <summary>The family formula that turns a band on.</summary>
        public static string BandFormulaFor(CdeState state)
            => StateParameter + " = " + ((int)state).ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Derive every presentation fact from a raw suitability value, which may
        /// hold the code, the description or both ("S4 - FOR APROVAL").
        /// </summary>
        public static SuitabilityDerivation Derive(string raw)
        {
            var d = new SuitabilityDerivation { State = CdeState.Unknown };
            var code = Iso19650Suitability.ExtractCode(raw);
            if (string.IsNullOrEmpty(code)) return d;
            d.Code = code;
            d.Description = Iso19650Suitability.DescriptionFor(code);
            d.CdeStateName = Iso19650Suitability.CdeStateFor(code);
            d.State = StateFor(d.CdeStateName);
            return d;
        }

        /// <summary>"WIP" → Wip etc.; anything else Unknown.</summary>
        public static CdeState StateFor(string cdeStateName)
        {
            switch ((cdeStateName ?? "").Trim().ToUpperInvariant())
            {
                case "WIP":       return CdeState.Wip;
                case "SHARED":    return CdeState.Shared;
                case "PUBLISHED": return CdeState.Published;
                case "ARCHIVED":
                case "ARCHIVE":   return CdeState.Archived;
                default:          return CdeState.Unknown;
            }
        }

        /// <summary>
        /// The shipped default palette, chosen clear of the corporate amber
        /// (#F2A341) and navy (#1F4E79) already used in the title blocks.
        /// STING_TITLE_BLOCKS.json "cdeBands" overrides it per state.
        /// </summary>
        public static readonly IReadOnlyDictionary<CdeState, string> DefaultColors =
            new Dictionary<CdeState, string>
            {
                [CdeState.Wip]       = "#BDBDBD",
                [CdeState.Shared]    = "#F9A825",
                [CdeState.Published] = "#2E7D32",
                [CdeState.Archived]  = "#616161",
            };

        /// <summary>
        /// Resolve the palette: data wins per state, defaults fill the rest.
        /// Entries whose state number or colour is invalid are reported in
        /// <paramref name="problems"/> and ignored — never half-applied.
        /// </summary>
        public static Dictionary<CdeState, string> ResolvePalette(
            IDictionary<string, CdeBandSpec> fromData, List<string> problems = null)
        {
            var result = new Dictionary<CdeState, string>();
            foreach (var kv in DefaultColors) result[kv.Key] = kv.Value;
            if (fromData == null) return result;
            foreach (var kv in fromData)
            {
                var byName = StateFor(kv.Key);
                var spec = kv.Value;
                if (byName == CdeState.Unknown)
                { problems?.Add($"cdeBands: '{kv.Key}' is not a CDE state"); continue; }
                if (spec == null || spec.State != (int)byName)
                { problems?.Add($"cdeBands: '{kv.Key}' must carry state {(int)byName}"); continue; }
                if (!TryParseHex(spec.Color, out _, out _, out _))
                { problems?.Add($"cdeBands: '{kv.Key}' colour '{spec?.Color}' is not #RRGGBB"); continue; }
                result[byName] = spec.Color.Trim().ToUpperInvariant();
            }
            return result;
        }

        /// <summary>Parse "#RRGGBB" (the hash optional).</summary>
        public static bool TryParseHex(string hex, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            var h = hex.Trim().TrimStart('#');
            if (h.Length != 6) return false;
            return byte.TryParse(h.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
                && byte.TryParse(h.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
                && byte.TryParse(h.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
        }

        /// <summary>
        /// Pre-export check: does the stored state number agree with the code?
        /// Null when it does (or when neither is set); otherwise the reason. A band
        /// that disagrees with the printed code must not be issued.
        /// </summary>
        public static string Disagreement(string suitabilityRaw, int? storedStateInt)
        {
            var d = Derive(suitabilityRaw);
            if (!storedStateInt.HasValue) return d.IsKnown
                ? $"{StateParameter} is not set; the status band will not show {d.CdeStateName}. Run Title Block Populate."
                : null;
            if (storedStateInt.Value == (int)CdeState.Archived) return null;   // set by Supersede/Cancel, outranks the code
            if (storedStateInt.Value != d.StateInt)
                return $"{StateParameter} = {storedStateInt.Value} but suitability '{d.Code}' means " +
                       $"{(d.IsKnown ? d.CdeStateName + " (" + d.StateInt + ")" : "no known state (0)")}. " +
                       "The band would show the wrong colour. Run Title Block Populate.";
            return null;
        }
    }
}
