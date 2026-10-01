using System.Globalization;

namespace StingTools.Core
{
    /// <summary>
    /// Parse numbers that come from files, parameters, JSON or other machine text.
    /// <para>
    /// <c>double.TryParse(s, out v)</c> uses the CURRENT culture, so on a comma-decimal
    /// Windows locale "12.5" from a data file reads as 125 (the dot is a group separator) or
    /// fails. This tries the invariant culture first and only then the current culture, with
    /// <see cref="NumberStyles.Float"/> both times (no thousands separators), so "1,234" is
    /// never misread as 1234 — the same rule ParameterHelpers.GetValueText's readers use.
    /// </para>
    /// Not for text a user typed into a dialog: that is current-culture by intent.
    /// </summary>
    internal static class NumberText
    {
        public static bool TryParse(string s, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }
    }
}
