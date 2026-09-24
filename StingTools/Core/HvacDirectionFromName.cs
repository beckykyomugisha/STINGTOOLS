// HvacDirectionFromName — airflow direction inferred from a family name.
//
// THE BUG THIS EXISTS TO STOP
//
// The rule was written as one ordered chain:
//
//     if (name.Contains("SUPPLY") || name.Contains("DIFFUSER")) return "SUP";
//     if (name.Contains("RETURN")) return "RTN";
//     if (name.Contains("EXHAUST")) return "EXH";
//
// "M_Return Diffuser" contains RETURN *and* DIFFUSER, so the first line won and
// every return diffuser in the model was tagged SUP. Measured 2026-09-23 on
// M-BLD1-Z01-L01-HVAC-SUP-SAT-002, a return diffuser reading as supply.
//
// The error is not the ordering, it is treating a SHAPE word as a DIRECTION word.
// "Diffuser", "grille" and "louvre" describe what a device looks like; return
// diffusers, exhaust grilles and supply louvres all exist. Using DIFFUSER as a
// synonym for SUPPLY means any family that names both its shape and its
// direction gets whichever the author happened to test first.
//
// So direction words are checked first, all of them, and a shape word is only a
// fallback for a name that states no direction at all. A wrong airflow direction
// on a tag is not cosmetic: it is the difference between a supply and a return
// on a drawing someone builds from.
//
// Revit-free by construction so the rule can be tested directly.

using System;

namespace StingTools.Core
{
    public static class HvacDirectionFromName
    {
        /// <summary>
        /// "SUP" / "RTN" / "EXH" / "FRA", or null when the name says nothing.
        /// <paramref name="familyName"/> is matched case-insensitively.
        /// </summary>
        public static string Resolve(string familyName)
        {
            if (string.IsNullOrWhiteSpace(familyName)) return null;
            string n = familyName.ToUpperInvariant();

            // 1. EXPLICIT DIRECTION — checked before any shape word, and checked
            //    in full before falling through, so a name carrying two shape
            //    words but one direction still resolves by the direction.
            if (n.Contains("RETURN")) return "RTN";
            if (n.Contains("EXHAUST") || n.Contains("EXTRACT")) return "EXH";
            if (n.Contains("FRESH AIR") || n.Contains("OUTSIDE AIR") || n.Contains("OUTDOOR AIR"))
                return "FRA";
            if (n.Contains("SUPPLY")) return "SUP";

            // 2. SHAPE ONLY — a weak inference, and only for a name that stated no
            //    direction at all. Most bare "diffuser" families are supply; a
            //    return one would have said so, and is caught above.
            if (n.Contains("DIFFUSER")) return "SUP";

            return null;
        }
    }
}
