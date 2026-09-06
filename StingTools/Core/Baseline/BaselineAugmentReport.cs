// ══════════════════════════════════════════════════════════════════════════
//  BaselineAugmentReport.cs — what layer 3 did, and did not do.
//
//  Revit-free for the same reason TileScanTally and RoomFinishTally are: this
//  message is the ONLY account anyone gets of an operation that opened and
//  rewrote their families. A wrong or vague one sends somebody looking in the
//  wrong place, and a silent one reads as "nothing happened" when the truth
//  may be "everything failed".
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;

namespace StingTools.Core.Baseline
{
    public sealed class BaselineAugmentReport
    {
        public int FamiliesAugmented;
        public int ParametersAdded;
        public int FamiliesAlreadyConforming;

        /// <summary>Families that could not be edited, with the reason. In-place
        /// families, some vendor families and workshared families owned by
        /// another user all land here — expected, not exceptional.</summary>
        public readonly List<string> Failed = new List<string>();

        private const int MaxNamesShown = 10;

        public bool TouchedNothing =>
            FamiliesAugmented == 0 && Failed.Count == 0 && FamiliesAlreadyConforming == 0;

        /// <summary>
        /// Null when layer 3 was not asked to do anything. Silence is right ONLY
        /// when nothing was attempted; a run that attempted and failed must say
        /// so, which is why Failed alone is enough to produce a message.
        /// </summary>
        public string Summary()
        {
            if (TouchedNothing) return null;

            var parts = new List<string>();

            if (FamiliesAugmented > 0)
                parts.Add($"Added {ParametersAdded} shared type parameter(s) to "
                        + $"{FamiliesAugmented} famil(ies).");
            else if (Failed.Count > 0)
                parts.Add("No family was augmented.");

            if (FamiliesAlreadyConforming > 0)
                parts.Add($"{FamiliesAlreadyConforming} famil(ies) already carried them.");

            if (Failed.Count > 0)
            {
                var shown = new List<string>();
                foreach (string f in Failed)
                {
                    if (shown.Count == MaxNamesShown) break;
                    shown.Add(f);
                }
                parts.Add($"{Failed.Count} famil(ies) could not be edited:\n  • "
                        + string.Join("\n  • ", shown)
                        + (Failed.Count > MaxNamesShown
                            ? $"\n  • …and {Failed.Count - MaxNamesShown} more" : "")
                        + "\nIn-place families cannot be edited at all; a workshared family "
                        + "owned by someone else needs their ownership released.");
            }

            return string.Join("\n", parts);
        }
    }
}
