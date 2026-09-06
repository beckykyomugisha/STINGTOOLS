// ══════════════════════════════════════════════════════════════════════════
//  TileScanTally.cs — MAT-SCHED-3 diagnostic: what the tile scan looked at.
//
//  The first export after tiling shipped produced ZERO tiling rows and no
//  errors, and two very different causes fit that output exactly: the model
//  describes no finish layers at all, or it describes them under names the
//  pattern does not recognise. Nothing in the workbook or the log separated
//  the two.
//
//  An absent side effect never tells you why. This reports the DENOMINATOR,
//  so the next export answers the question instead of restating it.
//
//  Revit-free on purpose: the value of a diagnostic is entirely in whether
//  its message is right, so the message is the part that has to be testable.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;

namespace StingTools.Core.MaterialSchedule
{
    public sealed class TileScanTally
    {
        /// <summary>Host TYPES whose compound structure was read. Types, not
        /// instances: a compound structure belongs to the type.</summary>
        public int TypesInspected;
        public int TypesWithFinishLayer;
        public int TypesMatched;

        /// <summary>Finish-layer materials that did NOT read as tiling. Kept,
        /// not discarded: if the pattern is the thing that is wrong, these names
        /// are the evidence for it.</summary>
        public readonly SortedSet<string> RejectedMaterials =
            new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        private const int MaxNamesShown = 8;

        public void Reset()
        {
            TypesInspected = TypesWithFinishLayer = TypesMatched = 0;
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

            string s = $"Tiling scan: {TypesInspected} wall/floor type(s) inspected, "
                     + $"{TypesWithFinishLayer} carry a finish layer, "
                     + $"{TypesMatched} name a tile material.";

            if (TypesMatched > 0) return s;

            if (RejectedMaterials.Count > 0)
            {
                var names = new List<string>();
                foreach (string n in RejectedMaterials)
                {
                    if (names.Count == MaxNamesShown) break;
                    names.Add(n);
                }
                return s + " Finish materials found but not recognised as tiling: "
                         + string.Join(", ", names)
                         + (RejectedMaterials.Count > MaxNamesShown ? ", …" : "")
                         + ". Rename the material, or widen the tile pattern in the plugin.";
            }

            if (TypesWithFinishLayer == 0)
                return s + " No finish layers at all means the model does not describe its "
                         + "finishes, so no tiled area can be measured from it — this is not "
                         + "a plugin fault to chase.";

            // Finish layers exist, none carried a usable material name.
            return s + " The finish layers carry no named material, so nothing could be "
                     + "classified. Assign materials to the finish layers.";
        }
    }
}
