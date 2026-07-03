// ══════════════════════════════════════════════════════════════════════════
//  BoqUnits.cs — Document-free unit canonicalisation for the BOQ (RC-2).
//
//  Extracted from BOQCostManager so the unit tokens + the tonne↔kg mass factor
//  are headlessly testable. NormaliseUnit keeps tonne and kg DISTINCT (they were
//  collapsed to one token, hiding a latent 1000× error when a tonne rule met a kg
//  rate); MassFactor supplies the ×1000 / ÷1000 scale when they differ.
// ══════════════════════════════════════════════════════════════════════════
using System;

namespace StingTools.BOQ
{
    public static class BoqUnits
    {
        /// <summary>Canonical unit token. m²/sqm → m2; m³/cum → m3; lm → m;
        /// kg/kgs → kg; tonne/t/te → tonne (DISTINCT from kg); no/nr/item → each.</summary>
        public static string Normalise(string u)
        {
            if (string.IsNullOrEmpty(u)) return "";
            string s = u.Trim().ToLowerInvariant();
            switch (s)
            {
                case "m²": case "sqm": case "m2": return "m2";
                case "m³": case "cum": case "m3": return "m3";
                case "lm": case "lin-m": case "linear-m": case "m": return "m";
                case "kg": case "kgs": return "kg";
                case "tonne": case "tonnes": case "t": case "te": return "tonne";
                case "no": case "nr": case "item": case "each": case "ea": return "each";
                default: return s;
            }
        }

        /// <summary>Scale to convert a quantity from <paramref name="fromUnit"/> to
        /// <paramref name="toUnit"/> across the tonne↔kg boundary (1 otherwise).</summary>
        public static double MassFactor(string fromUnit, string toUnit)
        {
            string f = Normalise(fromUnit), t = Normalise(toUnit);
            if (f == "tonne" && t == "kg") return 1000.0;
            if (f == "kg" && t == "tonne") return 0.001;
            return 1.0;
        }

        /// <summary>True when two units denote the same quantity dimension. Exact
        /// token match, or the compatible tonne↔kg mass pair (apply MassFactor).</summary>
        public static bool Align(string a, string b)
        {
            string x = Normalise(a), y = Normalise(b);
            if (string.IsNullOrEmpty(x) || string.IsNullOrEmpty(y)) return false;
            if (string.Equals(x, y, StringComparison.Ordinal)) return true;
            return (x == "kg" && y == "tonne") || (x == "tonne" && y == "kg");
        }
    }
}
