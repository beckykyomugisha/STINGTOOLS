// ══════════════════════════════════════════════════════════════════════════
//  MembraneScanTally.cs — MATSCHED-T3 diagnostic: what the membrane scan saw.
//
//  Same contract as TileScanTally, ScreedScanTally and CeilingScanTally.
//  `MaterialFunctionAssignment.Membrane` layers were ignored entirely, so a
//  ground slab's DPM and a roof's underlay produced nothing and said nothing.
//
//  This tally carries two counters the others do not, and both exist because
//  the runner's caution for this task is not "do not match it" but "say that
//  you saw it":
//
//    * INSULATION layers, which are bought by THICKNESS and are deliberately
//      not priced here. Absorbing them into a per-m² membrane commodity would
//      mis-price both, so they are named instead — an omission stated is not
//      the same failure as an omission hidden.
//    * WALL membrane layers, which are deliberately not measured at all. A
//      layer's area on a wall is the wall FACE area, and a horizontal
//      damp-proof course occupies one course of it; measuring the face would
//      over-order by an order of magnitude. The scope boundary is reported so
//      it is a stated decision rather than an unexplained absence.
//
//  Revit-free on purpose: the value of a diagnostic is entirely in whether its
//  message is right, so the message is the part that has to be testable.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;

namespace StingTools.Core.MaterialSchedule
{
    public sealed class MembraneScanTally
    {
        /// <summary>Floor/roof TYPES whose compound structure was read.</summary>
        public int TypesInspected;

        /// <summary>Types carrying at least one Membrane-function layer.</summary>
        public int TypesWithMembraneLayer;

        /// <summary>Types that produced a priced membrane.</summary>
        public int TypesMatched;

        /// <summary>Insulation layers SEEN and deliberately not priced.</summary>
        public int InsulationLayersSeen;

        /// <summary>Wall types carrying a membrane layer. Counted, never measured.</summary>
        public int WallMembraneLayersSeen;

        /// <summary>Membrane-function materials that read as neither DPM nor
        /// underlay. Kept as evidence: if the pattern is what is wrong, these
        /// names are the proof.</summary>
        public readonly SortedSet<string> RejectedMaterials =
            new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Insulation materials, by name. An insulation layer that
        /// produced nothing AND said nothing would be the same silent omission
        /// this task exists to remove.</summary>
        public readonly SortedSet<string> InsulationMaterials =
            new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        private const int MaxNamesShown = 8;

        public void Reset()
        {
            TypesInspected = TypesWithMembraneLayer = TypesMatched = 0;
            InsulationLayersSeen = WallMembraneLayersSeen = 0;
            RejectedMaterials.Clear();
            InsulationMaterials.Clear();
        }

        /// <summary>
        /// The line reported on every export, or NULL when nothing was looked
        /// at — no scan means nothing to report, and an invented zero would read
        /// as a finding.
        /// </summary>
        public string Summary()
        {
            if (TypesInspected <= 0 && WallMembraneLayersSeen <= 0) return null;

            string s = $"Membrane scan: {TypesInspected} floor/roof type(s) inspected, "
                     + $"{TypesWithMembraneLayer} carry a membrane layer, "
                     + $"{TypesMatched} name a DPM or underlay.";

            if (TypesMatched == 0 && RejectedMaterials.Count > 0)
                s += " Membrane layers found but not recognised: " + Names(RejectedMaterials)
                   + ". Rename the material, or widen the membrane pattern in the plugin.";
            else if (TypesMatched == 0 && TypesWithMembraneLayer == 0 && TypesInspected > 0)
                s += " No membrane layers at all means the model does not describe a DPM or an "
                   + "underlay, so none can be measured from it — this is not a plugin fault to chase.";
            else if (RejectedMaterials.Count > 0)
                s += " Membrane layers also found but not recognised: " + Names(RejectedMaterials) + ".";

            // ALWAYS reported when present, matched or not. This is the runner's
            // explicit caution: insulation is a separate commodity bought by
            // thickness, and absorbing it into a per-m² membrane would silently
            // mis-price both.
            if (InsulationLayersSeen > 0)
                s += $" {InsulationLayersSeen} insulation layer(s) were seen and deliberately NOT "
                   + "priced — insulation is bought by thickness, not by the square metre of roll, "
                   + "so it is not a membrane and is not converted as one"
                   + (InsulationMaterials.Count > 0 ? " (" + Names(InsulationMaterials) + ")" : "")
                   + ". Price it by hand until a thickness-driven rule exists.";

            if (WallMembraneLayersSeen > 0)
                s += $" {WallMembraneLayersSeen} wall type(s) carry a membrane layer, which is NOT "
                   + "measured: a layer's area on a wall is the wall face area, and a horizontal "
                   + "damp-proof course occupies one course of it, so measuring the face would "
                   + "over-order by an order of magnitude. Measure wall DPC by hand.";

            return s;
        }

        private static string Names(SortedSet<string> set)
        {
            var names = new List<string>();
            foreach (string n in set)
            {
                if (names.Count == MaxNamesShown) break;
                names.Add(n);
            }
            return string.Join(", ", names) + (set.Count > MaxNamesShown ? ", …" : "");
        }
    }
}
