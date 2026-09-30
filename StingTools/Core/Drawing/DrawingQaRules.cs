// StingTools — Drawing Types QA / finishing rules (Revit-free).
//
// The decisions the drawing QA tools make (Sync Styles, Heal Title Blocks,
// Renumber, managed templates) live here, free of the Revit API, so they can
// be unit-tested by StingTools.Tags.Tests through <Compile Include>. The Revit
// callers gather facts, ask these rules, and act on the answer.

using System;

namespace StingTools.Core.Drawing
{
    internal static class DrawingQaRules
    {
        /// <summary>
        /// DTW-2: may a cached managed-template id be returned as-is? Only when
        /// the template's stamped checksum equals the pack's current checksum.
        /// An empty stamp is never current — the template was never stamped by
        /// the syncer, or the stamp was cleared, so it must be re-applied.
        /// </summary>
        internal static bool IsCachedTemplateCurrent(string storedChecksum, string currentChecksum)
            => !string.IsNullOrEmpty(storedChecksum)
               && string.Equals(storedChecksum, currentChecksum, StringComparison.Ordinal);

        /// <summary>
        /// DTW-5: may a profile's titleBlockParams entry write this parameter?
        /// Not when it is the sheet's own number or name. A title block exposes
        /// both, so a "Sheet Number" key renumbered the sheet behind
        /// SheetNumbering.Apply — bypassing the ISO policy, the style lock and the
        /// renumber history. Decided by the parameter's BuiltInParameter when the
        /// caller has it (<paramref name="builtInParameterName"/>, e.g.
        /// "SHEET_NUMBER") so a localised label is still caught, and by the
        /// English key otherwise.
        /// </summary>
        internal static bool IsSheetIdentityParam(string key, string builtInParameterName)
        {
            if (string.Equals(builtInParameterName, "SHEET_NUMBER", StringComparison.Ordinal)
                || string.Equals(builtInParameterName, "SHEET_NAME", StringComparison.Ordinal))
                return true;
            var k = (key ?? string.Empty).Trim();
            return string.Equals(k, "Sheet Number", StringComparison.OrdinalIgnoreCase)
                || string.Equals(k, "Sheet Name", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// DTW-15: do two invariant number texts ("2.5", "2.500") name the same
        /// value? Culture-free, so a comma-decimal machine does not read "2.5" as
        /// 25. Empty text equals zero — the applier writes 0 for an empty cell.
        /// False when either side is not a number.
        /// </summary>
        internal static bool NumericTextEquals(string a, string b)
        {
            if (!TryParseInvariant(a, out var x) || !TryParseInvariant(b, out var y)) return false;
            return Math.Abs(x - y) <= 1e-6 * Math.Max(1.0, Math.Max(Math.Abs(x), Math.Abs(y)));
        }

        internal static bool TryParseInvariant(string text, out double value)
        {
            if (string.IsNullOrWhiteSpace(text)) { value = 0; return true; }
            return double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }
    }
}
