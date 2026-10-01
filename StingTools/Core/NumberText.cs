using System.Globalization;

namespace StingTools.Core
{
    /// <summary>
    /// Parses a number typed into a text parameter, a layer name or a cell:
    /// invariant culture first, then the machine's culture (the same rule as
    /// ParameterHelpers' text-to-number write path). Values written by
    /// $"{x:0.00}" on a comma-decimal Windows locale read as "3,45", so the
    /// current-culture pass keeps them readable, while "3.45" reads the same
    /// on every machine. Neither pass allows thousands separators, so "1,234"
    /// is never misread as 1234.
    /// </summary>
    public static class NumberText
    {
        public static bool TryParse(string s, out double value)
        {
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        /// <summary>Invariant culture only — for text this code wrote itself with
        /// <see cref="CultureInfo.InvariantCulture"/>.</summary>
        public static bool TryParseInvariant(string s, out double value)
            => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
