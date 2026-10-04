// NumberText — a number from parameter text, whatever machine wrote it (ROADMAP ELEC-30).
// Revit-free: StingTools.Tags.Tests compiles this file.
//
// ParameterHelpers.GetDouble parsed TEXT with NumberStyles.Any + invariant culture, which
// allows thousands separators anywhere: "2,5" (a 2.5 written on a comma-decimal Windows)
// read as 25 and "12,50" as 1250. The rules here, in order:
//   1. invariant float ("2.5", "-3", "1e3")
//   2. the machine's culture, float only ("2,5" on de-DE)
//   3. a real thousands group ("1,250", "1,234.5") → commas removed, as before
//   4. one comma between digits ("2,5", "12,50") → a decimal comma
//   5. no comma at all: the old NumberStyles.Any reading (currency sign, parentheses)
// Anything else ("1,2,3") is not a number.

using System.Globalization;
using System.Text.RegularExpressions;

namespace StingTools.Core
{
    public static class NumberText
    {
        private static readonly Regex Thousands = new Regex(@"^[+-]?\d{1,3}(,\d{3})+(\.\d+)?$", RegexOptions.CultureInvariant);
        private static readonly Regex DecimalComma = new Regex(@"^[+-]?\d+,\d+$", RegexOptions.CultureInvariant);

        public static bool TryParse(string text, out double value) => TryParse(text, CultureInfo.CurrentCulture, out value);

        /// <summary>As <see cref="TryParse(string, out double)"/>, with the machine culture given (for tests).</summary>
        public static bool TryParse(string text, CultureInfo machine, out double value)
        {
            value = 0;
            string s = (text ?? "").Trim();
            if (s.Length == 0) return false;
            var inv = CultureInfo.InvariantCulture;
            if (double.TryParse(s, NumberStyles.Float, inv, out value)) return true;
            if (machine != null && double.TryParse(s, NumberStyles.Float, machine, out value)) return true;
            if (Thousands.IsMatch(s)) return double.TryParse(s.Replace(",", ""), NumberStyles.Float, inv, out value);
            if (DecimalComma.IsMatch(s)) return double.TryParse(s.Replace(',', '.'), NumberStyles.Float, inv, out value);
            if (s.IndexOf(',') < 0 && double.TryParse(s, NumberStyles.Any, inv, out value)) return true;
            value = 0;
            return false;
        }
    }
}
