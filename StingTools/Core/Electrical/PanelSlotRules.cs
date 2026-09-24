// PanelSlotRules — Revit-free rules for panel slots.
//
// ELEC-15: three commands each had their own idea of how many slots a panel
// has, and two of them invented one (42, 24) when the family did not say.
// ELEC-16: CircuitSlotParser.FromStartSlot assumed every panel was a two-column
// board numbered across, so a 3-pole breaker at slot 7 on a switchboard was
// drawn in 7, 9, 11 instead of 7, 8, 9.
//
// The Revit half (reading the parameters and the panel schedule) is
// PanelSlotReader; the decisions live here so they can be tested.

using System.Collections.Generic;

namespace StingTools.Core.Electrical
{
    /// <summary>How a panel schedule numbers its circuits (mirrors
    /// Autodesk.Revit.DB.Electrical.PanelConfiguration, plus Unknown).</summary>
    internal enum SlotNumbering
    {
        Unknown,
        /// <summary>One column, numbered top to bottom.</summary>
        OneColumn,
        /// <summary>Two columns, odd down the left and even down the right.</summary>
        TwoColumnsAcross,
        /// <summary>Two columns, numbered down the left side then down the right.</summary>
        TwoColumnsDown,
    }

    internal static class PanelSlotRules
    {
        /// <summary>
        /// The panel's slot count: Revit's "Max Number of Single Pole Breakers"
        /// when the family reports it, else "Max Number of Circuits". Null when
        /// neither is set — the caller reports the panel, it does not guess a size.
        /// </summary>
        public static int? ChooseSlotCount(int? maxSinglePoleBreakers, int? maxNumberOfCircuits)
        {
            if (maxSinglePoleBreakers.HasValue && maxSinglePoleBreakers.Value > 0) return maxSinglePoleBreakers.Value;
            if (maxNumberOfCircuits.HasValue && maxNumberOfCircuits.Value > 0) return maxNumberOfCircuits.Value;
            return null;
        }

        /// <summary>
        /// Distance between the slots one multi-pole breaker occupies. A switchboard
        /// schedule is always consecutive. <paramref name="known"/> is false when the
        /// numbering could not be read; the returned 2 is then the conventional
        /// two-column panelboard, and the caller should say it assumed that.
        /// </summary>
        public static int StepFor(SlotNumbering numbering, bool isSwitchboard, out bool known)
        {
            known = true;
            if (isSwitchboard) return 1;
            switch (numbering)
            {
                case SlotNumbering.OneColumn:        return 1;
                case SlotNumbering.TwoColumnsDown:   return 1;
                case SlotNumbering.TwoColumnsAcross: return 2;
                default: known = false; return 2;
            }
        }

        /// <summary>
        /// The lowest start slot below <paramref name="currentStart"/> where a breaker
        /// of <paramref name="poles"/> poles fits in slots nobody else occupies, or
        /// null when it cannot move lower. Used to compact a panel after deletions.
        /// </summary>
        public static int? LowestFreeStart(ISet<int> occupiedByOthers, int currentStart,
            int poles, int step, int totalSlots)
        {
            if (currentStart <= 1) return null;
            occupiedByOthers ??= new HashSet<int>();
            for (int s = 1; s < currentStart; s++)
            {
                var need = CircuitSlotParser.FromStartSlot(s, poles, step);
                bool fits = true;
                foreach (int slot in need)
                {
                    if (occupiedByOthers.Contains(slot) || (totalSlots > 0 && slot > totalSlots)) { fits = false; break; }
                }
                if (fits) return s;
            }
            return null;
        }
    }
}
