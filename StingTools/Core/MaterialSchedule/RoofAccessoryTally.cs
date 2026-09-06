// ══════════════════════════════════════════════════════════════════════════
//  RoofAccessoryTally.cs — MATSCHED-T5 diagnostic for roof edge accessories.
//
//  This tally reports MORE about what was NOT measured than about what was,
//  and that is deliberate. Of the three accessories a roof edge carries —
//  fascia, barge board and ridge cap — only ONE has a length the footprint
//  states outright:
//
//    * FASCIA runs along the eaves. An eave is horizontal, so the footprint's
//      plan length IS its length. Measured.
//    * BARGE BOARD runs up the RAKE of a gable. The footprint carries the
//      gable's PLAN length, which is shorter by 1/cos(slope). Recovering the
//      rake needs a slope angle whose units this code has not been able to
//      confirm against a running Revit — so it is NOT measured, and the plan
//      length is reported as information, clearly labelled as not the answer.
//    * RIDGE is an internal line the footprint does not carry at all. Not
//      measured, and no amount of boundary data would give it.
//
//  Deriving either from the roof AREA would be the thing this whole feature
//  exists to avoid. Reporting exactly what is missing, and why, is worth more
//  than a confident wrong number — and more than silence, which is the failure
//  the four layer scans were written to end.
//
//  It also carries the rafters/purlins note, so their absence is read as a
//  scope boundary rather than an omission.
//
//  Revit-free on purpose: the value of a diagnostic is entirely in whether its
//  message is right, so the message is the part that has to be testable.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;

namespace StingTools.Core.MaterialSchedule
{
    public sealed class RoofAccessoryTally
    {
        /// <summary>Roof elements looked at. Instances, not types: a footprint
        /// belongs to the element, not to its type.</summary>
        public int RoofsInspected;

        /// <summary>Roofs whose footprint could be read at all.</summary>
        public int RoofsWithFootprint;

        /// <summary>Roofs that produced an eaves length.</summary>
        public int RoofsWithEaves;

        /// <summary>Roofs that are not FootPrintRoofs — extrusion roofs and
        /// roofs by face carry no footprint sketch to read.</summary>
        public int RoofsWithoutFootprint;

        /// <summary>Concrete roofs, skipped: a flat RC slab has no fascia.</summary>
        public int ConcreteRoofsSkipped;

        /// <summary>Roofs whose footprint had more than one loop. Only the outer
        /// loop is measured — an opening's edge is not an eave.</summary>
        public int RoofsWithInnerLoops;

        public double EavesLengthM;

        /// <summary>PLAN length of the non-slope-defining edges. Reported, NEVER
        /// emitted: a barge board runs up the rake, which is longer.</summary>
        public double VergePlanLengthM;

        private const double Eps = 1e-6;

        public void Reset()
        {
            RoofsInspected = RoofsWithFootprint = RoofsWithEaves = 0;
            RoofsWithoutFootprint = ConcreteRoofsSkipped = RoofsWithInnerLoops = 0;
            EavesLengthM = VergePlanLengthM = 0;
        }

        /// <summary>
        /// The line reported on every export, or NULL when no roof was looked at
        /// — no scan means nothing to report, and an invented zero would read as
        /// a finding.
        /// </summary>
        public string Summary()
        {
            if (RoofsInspected <= 0) return null;

            string s = $"Roof accessory scan: {RoofsInspected} roof(s) inspected, "
                     + $"{RoofsWithFootprint} carry a readable footprint, "
                     + $"{RoofsWithEaves} produced an eaves length "
                     + $"({EavesLengthM:0.##} m of fascia).";

            if (RoofsWithoutFootprint > 0)
                s += $" {RoofsWithoutFootprint} roof(s) are not footprint roofs (extrusion roofs and "
                   + "roofs by face carry no boundary sketch), so no edge length could be read from "
                   + "them and no fascia was measured.";

            if (ConcreteRoofsSkipped > 0)
                s += $" {ConcreteRoofsSkipped} concrete roof(s) were skipped: a flat RC slab carries "
                   + "no fascia board.";

            if (RoofsWithInnerLoops > 0)
                s += $" {RoofsWithInnerLoops} roof(s) have openings in the footprint; only the OUTER "
                   + "loop was measured, because an opening's edge is not an eave.";

            // Always said when any roof was seen, matched or not. These are the
            // two things a reader will look for and not find.
            s += " RIDGE CAPS AND BARGE BOARDS ARE NOT MEASURED.";

            if (VergePlanLengthM > Eps)
                s += $" The gable edges total {VergePlanLengthM:0.##} m in PLAN — that is NOT the "
                   + "barge-board length, which runs up the rake and is longer by 1/cos(pitch). "
                   + "Measure it by hand from the stated pitch.";
            else
                s += " No gable edges were found, so a barge board may genuinely not apply.";

            s += " A ridge is an internal line the footprint does not carry at all, so no boundary "
               + "data could give it. Deriving either from the roof AREA would be a guess, and this "
               + "export does not make it.";

            s += " Timber rafters and purlins are OUT OF SCOPE here: they are measured only when "
               + "modelled as Structural Framing, where they already decompose. Their absence from "
               + "this section is a scope boundary, not an omission.";

            return s;
        }
    }
}
