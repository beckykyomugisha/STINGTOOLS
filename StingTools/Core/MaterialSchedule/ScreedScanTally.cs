// ══════════════════════════════════════════════════════════════════════════
//  ScreedScanTally.cs — MATSCHED-T1 diagnostic: what the screed scan looked at.
//
//  Same contract as TileScanTally, written for the same reason. A schedule
//  with no screed cement can mean four unrelated things:
//
//    * no floor type was inspected at all,
//    * the types carry no substrate/finish layer to read,
//    * they carry one, but its material name does not read as a screed, or
//    * it reads as a screed and the layer declares zero thickness,
//
//  and every one of those produces the identical output: nothing. Silence is
//  a bug — an absent side effect never tells you why — so the scan reports its
//  own DENOMINATOR and names what it rejected.
//
//  Revit-free on purpose: the value of a diagnostic is entirely in whether its
//  message is right, so the message is the part that has to be testable.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;

namespace StingTools.Core.MaterialSchedule
{
    public sealed class ScreedScanTally
    {
        /// <summary>Floor/roof TYPES whose compound structure was read. Types,
        /// not instances: a compound structure belongs to the type.</summary>
        public int TypesInspected;

        /// <summary>Types carrying at least one layer in a function a screed
        /// could legitimately occupy (Substrate / Finish1 / Finish2).</summary>
        public int TypesWithCandidateLayer;

        /// <summary>Types that produced a usable screed thickness.</summary>
        public int TypesMatched;

        /// <summary>Layers whose material DID read as a screed but whose declared
        /// width was zero. Counted separately because it is the one failure the
        /// other counters cannot express: the name was right and the driver was
        /// missing, so nothing was emitted rather than a default thickness.</summary>
        public int MatchedButZeroThickness;

        /// <summary>Candidate-layer materials that did NOT read as a screed.
        /// Kept, not discarded: if the pattern is the thing that is wrong, these
        /// names are the evidence for it.</summary>
        public readonly SortedSet<string> RejectedMaterials =
            new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        private const int MaxNamesShown = 8;

        public void Reset()
        {
            TypesInspected = TypesWithCandidateLayer = TypesMatched = 0;
            MatchedButZeroThickness = 0;
            RejectedMaterials.Clear();
        }

        /// <summary>
        /// The line reported on every export, or NULL when nothing was
        /// inspected — no scan means nothing to report, and an invented zero
        /// would read as a finding.
        /// </summary>
        public string Summary()
        {
            if (TypesInspected <= 0) return null;

            string s = $"Screed scan: {TypesInspected} floor/roof type(s) inspected, "
                     + $"{TypesWithCandidateLayer} carry a substrate or finish layer, "
                     + $"{TypesMatched} name a screed material with a stated thickness.";

            if (MatchedButZeroThickness > 0)
                s += $" {MatchedButZeroThickness} layer(s) read as screed but declare zero "
                   + "thickness, so no cement or sand was derived from them — a screed with "
                   + "no thickness has no volume, and a default one would be invented.";

            if (TypesMatched > 0) return s;

            if (RejectedMaterials.Count > 0)
            {
                var names = new List<string>();
                foreach (string n in RejectedMaterials)
                {
                    if (names.Count == MaxNamesShown) break;
                    names.Add(n);
                }
                return s + " Substrate/finish materials found but not recognised as screed: "
                         + string.Join(", ", names)
                         + (RejectedMaterials.Count > MaxNamesShown ? ", …" : "")
                         + ". Rename the material, or widen the screed pattern in the plugin.";
            }

            if (TypesWithCandidateLayer == 0)
                return s + " No substrate or finish layers at all means the model does not "
                         + "describe the build-up under its floors, so no screed can be "
                         + "measured from it — this is not a plugin fault to chase.";

            return s + " The substrate/finish layers carry no named material, so nothing could "
                     + "be classified. Assign materials to the layers.";
        }
    }
}
