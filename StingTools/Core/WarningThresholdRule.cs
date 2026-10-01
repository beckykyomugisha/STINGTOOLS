using System;

namespace StingTools.Core
{
    /// <summary>Which side of the threshold is the warning.</summary>
    public enum WarningDirection
    {
        /// <summary>Warn when the value is BELOW the threshold (a minimum).</summary>
        Min,
        /// <summary>Warn when the value is ABOVE the threshold (a limit).</summary>
        Max,
    }

    /// <summary>
    /// The comparison a <c>warning_thresholds</c> entry of PARAMETER_REGISTRY.json
    /// asks for, and the text it prints. Revit-free.
    ///
    /// <para>DSCH-47. The direction used to be read out of the entry's description:
    /// "minimum" or "min " meant below; "limit", "maximum", "max " or nothing at all
    /// meant above. A wording edit could invert a check, and the same sentence was
    /// also meant to be the parameter's tooltip. The direction is now the entry's own
    /// <c>"direction"</c> field ("min" / "max") and the printed text its
    /// <c>"message"</c> field. The description is the tooltip, generated from
    /// MR_PARAMETERS.txt by tools/sync_registry_from_txt.py, and nothing here reads it.</para>
    /// </summary>
    public static class WarningThresholdRule
    {
        /// <summary>"min" / "max" (case-insensitive). Anything else is not a direction:
        /// the caller must refuse the entry, never guess one.</summary>
        public static bool TryParseDirection(string text, out WarningDirection direction)
        {
            direction = WarningDirection.Max;
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "min": direction = WarningDirection.Min; return true;
                case "max": direction = WarningDirection.Max; return true;
                default: return false;
            }
        }

        /// <summary>
        /// The warning text when <paramref name="currentValue"/> is on the wrong side of
        /// <paramref name="threshold"/>, else null. Null too when either is not a number:
        /// there is nothing to compare.
        ///
        /// <para>A limit prints "exceeds", a minimum "&lt;". Before DSCH-47 a limit whose
        /// description happened to say "limit" / "maximum" printed "&gt;" instead (41 of
        /// 483 entries); the comparison was the same, only the word differed.</para>
        /// </summary>
        public static string Evaluate(WarningDirection direction, string severity, string message,
                                      string currentValue, string threshold, string unit)
        {
            if (!NumberText.TryParse(currentValue, out double val) ||
                !NumberText.TryParse(threshold, out double thresh))
                return null;

            if (direction == WarningDirection.Min)
                return val < thresh
                    ? $"[!{severity}: {message} — {currentValue} {unit} < {threshold} {unit}]"
                    : null;

            return val > thresh
                ? $"[!{severity}: {message} — {currentValue} {unit} exceeds {threshold} {unit}]"
                : null;
        }

        /// <summary>
        /// <paramref name="part"/> as a percentage of <paramref name="whole"/>, as
        /// invariant text ("25", "12.5"), or null when either is not a number or the
        /// whole is not positive - there is then nothing to compare, never a 0.
        /// </summary>
        public static string PercentOf(string part, string whole)
        {
            if (!NumberText.TryParse(part, out double p) || !NumberText.TryParse(whole, out double w) || w <= 0)
                return null;
            return Math.Round(100.0 * p / w, 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The direction the pre-DSCH-47 evaluator read out of a description. Kept ONLY so
        /// the migration stays checkable: every entry's <c>direction</c> must equal what
        /// its <c>message</c> (the old description, verbatim) implied, unless the entry is
        /// listed as a deliberate departure in the test. The plugin never calls it.
        /// </summary>
        public static WarningDirection LegacyDirectionFromText(string description)
        {
            string d = description ?? "";
            bool isMinimum = d.Contains("minimum") || d.Contains("min ");
            return isMinimum ? WarningDirection.Min : WarningDirection.Max;
        }
    }
}
