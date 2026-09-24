// CircuitSlotParser — Revit-free parse of an ElectricalSystem circuit number.
//
// Revit stores the circuit number as TEXT: "5" for a single-pole circuit,
// "1,3,5" (or "2-4-6" in some templates) for a multi-pole one. Reading it as
// an integer returns 0, which is how the panel door diagram came to draw every
// slot as SPARE.

using System.Collections.Generic;

namespace StingTools.Core.Electrical
{
    internal static class CircuitSlotParser
    {
        /// <summary>
        /// Every positive slot number named in a circuit number, in order,
        /// without duplicates. Separators are anything that is not a digit;
        /// an empty or non-numeric value yields no slots.
        /// </summary>
        public static List<int> Parse(string circuitNumber)
        {
            var slots = new List<int>();
            if (string.IsNullOrWhiteSpace(circuitNumber)) return slots;

            int current = -1;
            foreach (char c in circuitNumber + ",")
            {
                if (c >= '0' && c <= '9')
                {
                    current = (current < 0 ? 0 : current * 10) + (c - '0');
                    if (current > 100000) current = 100000; // guard absurd input
                }
                else if (current >= 0)
                {
                    if (current > 0 && !slots.Contains(current)) slots.Add(current);
                    current = -1;
                }
            }
            return slots;
        }
    }
}
