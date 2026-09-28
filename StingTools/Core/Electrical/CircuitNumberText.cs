// CircuitNumberText — Revit-free formatting of a circuit number held as a NUMBER.
//
// ELC_CKT_NR is TEXT from 2026-09-24 so it can carry multi-pole numbers such as
// "1,3,5". A project bound before then still holds it as NUMBER, and the project
// unit display for a number is "3.00" — which is what TAG7 printed ("connected
// to circuit 3.00"). A circuit number is an identifier, not a quantity: an
// integral value reads as "3". A non-integral value is not a circuit number
// anyone meant, so it is left exactly as Revit displayed it rather than being
// silently rounded into one.
//
// TEXT values never come through here: "3.00" typed as text is what the user
// wrote, and is kept.

using System;
using System.Globalization;

namespace StingTools.Core.Electrical
{
    internal static class CircuitNumberText
    {
        /// <summary>
        /// Text for a circuit number stored as a NUMBER. An integral value (within
        /// 1e-9) is written with no decimals in invariant culture; anything else
        /// returns <paramref name="displayText"/> unchanged (or, when that is empty,
        /// the value in invariant "G" form so a value is never dropped).
        /// </summary>
        public static string FromNumber(double value, string displayText)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return displayText ?? string.Empty;

            double rounded = Math.Round(value);
            if (Math.Abs(value - rounded) < 1e-9 && Math.Abs(rounded) < 1e15)
                return ((long)rounded).ToString(CultureInfo.InvariantCulture);

            return string.IsNullOrEmpty(displayText)
                ? value.ToString("G", CultureInfo.InvariantCulture)
                : displayText;
        }
    }
}
