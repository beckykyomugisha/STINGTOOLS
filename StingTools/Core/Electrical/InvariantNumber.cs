using System.Globalization;

namespace StingTools.Core.Electrical
{
    /// <summary>
    /// Parses a number from plugin-written parameter text or shipped data: invariant
    /// culture first, then the machine's culture (callers format with $"{x:0.00}",
    /// which writes "3,45" on a comma-decimal Windows locale). Neither style allows
    /// thousands separators, so "1,234" is never misread as 1234 — and "12.5" is
    /// never read as 125 on a locale whose group separator is ".". Same rule as
    /// ParameterHelpers.SetString. Revit-free.
    /// </summary>
    public static class InvariantNumber
    {
        public static bool TryParse(string s, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        /// <summary>The parsed value, or <paramref name="fallback"/> when the text is not a number.</summary>
        public static double ParseOr(string s, double fallback = 0)
            => TryParse(s, out double v) ? v : fallback;
    }
}
