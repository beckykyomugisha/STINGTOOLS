using System.Globalization;

namespace StingTools.Core
{
    /// <summary>
    /// The one parser for numbers that come from files, parameters, JSON or other
    /// machine text (DSCH-9). <c>double.TryParse(s, out v)</c> uses the CURRENT
    /// culture, so on a comma-decimal Windows locale "12.5" from a data file reads
    /// as 125 (the dot is a group separator) or fails.
    /// <para>
    /// <see cref="TryParse"/> tries the invariant culture first, then the current
    /// culture - values written by <c>$"{x:0.00}"</c> on a comma-decimal machine
    /// read as "3,45" and stay readable - with <see cref="NumberStyles.Float"/> both
    /// times (no thousands separators), so "1,234" is never misread as 1234: the
    /// same rule as ParameterHelpers' text-to-number path.
    /// </para>
    /// Not for text a user typed into a dialog: that is current-culture by intent.
    /// </summary>
    public static class NumberText
    {
        public static bool TryParse(string s, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        /// <summary>Invariant culture only - for text this code wrote itself with
        /// <see cref="CultureInfo.InvariantCulture"/>.</summary>
        public static bool TryParseInvariant(string s, out double value)
        {
            value = 0;
            return !string.IsNullOrWhiteSpace(s)
                && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary><see cref="TryParse"/>, or <paramref name="fallback"/>.</summary>
        public static double ParseOr(string s, double fallback = 0)
            => TryParse(s, out double v) ? v : fallback;
    }
}
