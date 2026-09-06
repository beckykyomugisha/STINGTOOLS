// ══════════════════════════════════════════════════════════════════════════
//  CeilingScanTally.cs — MATSCHED-T2 diagnostic: what the ceiling scan saw.
//
//  Same contract as TileScanTally and ScreedScanTally, for the same reason.
//  `CeilingType` appeared nowhere in the take-off at all, so a suspended
//  gypsum ceiling produced no boards, no furring and no skim — and produced
//  them silently, which is indistinguishable from a model with no ceilings.
//
//  This tally has one counter the others do not need: types with NO compound
//  structure. A ceiling drawn from a plain type is the single most likely
//  reason for an empty result, and it is a fact about the model rather than a
//  plugin fault, so it has to be sayable on its own.
//
//  Revit-free on purpose: the value of a diagnostic is entirely in whether its
//  message is right, so the message is the part that has to be testable.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;

namespace StingTools.Core.MaterialSchedule
{
    public sealed class CeilingScanTally
    {
        /// <summary>Ceiling TYPES whose compound structure was read.</summary>
        public int TypesInspected;

        /// <summary>Ceiling types that declare NO compound structure at all —
        /// nothing to read, so nothing can be measured from them.</summary>
        public int TypesWithoutCompoundStructure;

        /// <summary>Types carrying at least one Finish1/Finish2 layer.</summary>
        public int TypesWithFinishLayer;

        public int TypesWithBoard;
        public int TypesWithPlaster;

        /// <summary>Ceiling INSTANCES whose area could not be read. A type can
        /// be perfectly described and still yield nothing if the element has no
        /// computed area, and that is a different fix from a naming problem.</summary>
        public int InstancesWithNoArea;

        /// <summary>Finish materials that read as neither board nor plaster.
        /// Kept, not discarded: a mineral-fibre tile or a PVC ceiling is a real
        /// product this take-off deliberately does not price, and naming it is
        /// how that stays a stated decision rather than a silent loss.</summary>
        public readonly SortedSet<string> RejectedMaterials =
            new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        private const int MaxNamesShown = 8;

        /// <summary>True once any furring quantity has been derived, so the
        /// export can carry the heuristic banner ONLY when there is something
        /// for it to qualify.</summary>
        public bool FurringDerived;

        public void Reset()
        {
            TypesInspected = TypesWithoutCompoundStructure = TypesWithFinishLayer = 0;
            TypesWithBoard = TypesWithPlaster = InstancesWithNoArea = 0;
            FurringDerived = false;
            RejectedMaterials.Clear();
        }

        /// <summary>
        /// The line reported on every export, or NULL when no ceiling was looked
        /// at at all — no scan means nothing to report, and an invented zero
        /// would read as a finding.
        /// </summary>
        public string Summary()
        {
            if (TypesInspected <= 0 && TypesWithoutCompoundStructure <= 0) return null;

            int seen = TypesInspected + TypesWithoutCompoundStructure;
            string s = $"Ceiling scan: {seen} ceiling type(s) seen, "
                     + $"{TypesInspected} declare a compound structure, "
                     + $"{TypesWithBoard} name a board material, "
                     + $"{TypesWithPlaster} name a plaster or skim coat.";

            if (TypesWithoutCompoundStructure > 0)
                s += $" {TypesWithoutCompoundStructure} ceiling type(s) declare no compound structure "
                   + "at all, so there is no build-up to measure — draw them as a layered type, or "
                   + "price them by hand.";

            if (InstancesWithNoArea > 0)
                s += $" {InstancesWithNoArea} ceiling(s) had a usable type but no computed area, so "
                   + "nothing was measured from them.";

            if (TypesWithBoard > 0 || TypesWithPlaster > 0)
            {
                if (RejectedMaterials.Count > 0)
                    s += " " + RejectedLine();
                return s;
            }

            if (RejectedMaterials.Count > 0) return s + " " + RejectedLine();

            if (TypesInspected > 0 && TypesWithFinishLayer == 0)
                return s + " The ceiling types carry no finish layer, so no board or skim can be "
                         + "identified in them.";

            if (TypesInspected > 0)
                return s + " The finish layers carry no named material, so nothing could be "
                         + "classified. Assign materials to the finish layers.";

            return s;
        }

        private string RejectedLine()
        {
            var names = new List<string>();
            foreach (string n in RejectedMaterials)
            {
                if (names.Count == MaxNamesShown) break;
                names.Add(n);
            }
            return "Ceiling finish materials found but not priced here: "
                 + string.Join(", ", names)
                 + (RejectedMaterials.Count > MaxNamesShown ? ", …" : "")
                 + ". Mineral-fibre tiles, PVC and T&G are bought by the tile or the length, not by "
                 + "the 1200x2400 sheet, so they are named rather than mis-converted.";
        }

        /// <summary>
        /// The heuristic banner for furring, or NULL when no furring was
        /// derived. A ratio must never be presented as a measurement, and a
        /// banner with nothing behind it is noise.
        /// </summary>
        public string FurringBanner()
        {
            if (!FurringDerived) return null;
            return "Ceiling furring / suspension grid is DERIVED from ceiling area by a practice "
                 + "ratio (CEILING,DEFAULT,FURRING_M_PER_M2 in MATERIAL_LOOKUP.csv, ~2.7 m/m² for a "
                 + "600x600 exposed grid). The model does not state grid spacing, so this is NOT a "
                 + "measurement and no standard publishes it. Review the ratio against the specified "
                 + "grid before issue.";
        }
    }
}
