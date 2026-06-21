// ══════════════════════════════════════════════════════════════════════════
//  BoqUnits.cs — pure unit-normalisation + mass-scale arithmetic for take-off.
//
//  Extracted from BOQCostManager.NormaliseUnit so the tonne↔kg scale fix is
//  unit-testable without a Revit host (linked into StingTools.Boq.Tests the
//  same way WasteFactor / MeasuredAddition are).
//
//  ACCURACY FIX (audit #5 — latent 1000× tonne bug):
//  The previous NormaliseUnit collapsed "tonne"/"t"/"kg" all to "kg", so a
//  rate quoted PER TONNE aligned with a quantity measured in KG and produced a
//  1000× overcharge. Tonne now normalises to its own token ("tonne"), so a
//  kg-rule and a tonne-rate no longer falsely align, and MassKgToRateUnit
//  applies the ÷1000 scale when a measured mass (always kilograms internally)
//  is priced against a per-tonne rate.
//
//  Zero Autodesk.Revit.* dependencies on purpose.
// ══════════════════════════════════════════════════════════════════════════
using System;

namespace StingTools.BOQ
{
    public static class BoqUnits
    {
        /// <summary>
        /// Canonicalises a unit string so equivalent spellings compare equal.
        /// Mass is split into two DISTINCT tokens — "kg" and "tonne" — so that
        /// a kilogram quantity is never silently treated as a tonne quantity
        /// (or vice-versa). All other dimensions collapse their synonyms.
        /// </summary>
        public static string Normalise(string u)
        {
            if (string.IsNullOrEmpty(u)) return "";
            string s = u.Trim().ToLowerInvariant();
            switch (s)
            {
                case "m²": case "sqm": case "m2": return "m2";
                case "m³": case "cum": case "m3": return "m3";
                case "lm": case "lin-m": case "linear-m": case "m": return "m";
                // FIX #5 — tonne is now its OWN dimensionally-scaled token, no
                // longer aliased onto "kg". kg stays kg.
                case "t": case "tonne": case "tonnes": return "tonne";
                case "kg": return "kg";
                case "no": case "nr": case "item": case "each": case "ea": return "each";
                default: return s;
            }
        }

        /// <summary>True when the normalised unit is a mass unit (kg or tonne).</summary>
        public static bool IsMassUnit(string unit)
        {
            string n = Normalise(unit);
            return n == "kg" || n == "tonne";
        }

        /// <summary>
        /// Convert a mass measured in KILOGRAMS (Revit's internal mass unit) into
        /// the scale demanded by <paramref name="rateUnit"/>:
        ///   • rate per "kg"    → kilograms unchanged
        ///   • rate per "tonne" → kilograms ÷ 1000
        /// Any non-mass rate unit returns the kilograms unchanged (caller error —
        /// it should only invoke this for mass units).
        /// </summary>
        public static double MassKgToRateUnit(double massKg, string rateUnit)
        {
            return Normalise(rateUnit) == "tonne" ? massKg / 1000.0 : massKg;
        }
    }
}
