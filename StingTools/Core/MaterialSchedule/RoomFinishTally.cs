// ══════════════════════════════════════════════════════════════════════════
//  RoomFinishTally.cs — MAT-SCHED-3: what the ROOM finish scan looked at.
//
//  Same contract as TileScanTally, for the same reason: a schedule with no
//  tiling rows must say whether the rooms were silent, whether their finish
//  names went unrecognised, or whether the layer source already covered the
//  surface. Three very different situations, one identical-looking output.
//
//  Revit-free so the MESSAGE is testable — a diagnostic that misreports is
//  worse than none, because it sends someone looking in the wrong place.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;

namespace StingTools.Core.MaterialSchedule
{
    public sealed class RoomFinishTally
    {
        /// <summary>Placed rooms only. An unplaced room has Area 0 and is skipped.</summary>
        public int RoomsRead;
        public int FloorsTiled;
        public int WallsTiled;
        public int SkirtingRuns;

        /// <summary>True when the wall/floor TYPES already carried tiled finish
        /// layers, so room tiling was deliberately not measured.</summary>
        public bool TilingSuppressedByLayerSource;

        /// <summary>Floor finishes that named something real but did not read as
        /// tiling. Kept as evidence: if the classifier is what is wrong, these
        /// are the names that prove it.</summary>
        public readonly SortedSet<string> UnrecognisedFloorFinishes =
            new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        private const int MaxNamesShown = 8;

        public void Reset()
        {
            RoomsRead = FloorsTiled = WallsTiled = SkirtingRuns = 0;
            TilingSuppressedByLayerSource = false;
            UnrecognisedFloorFinishes.Clear();
        }

        /// <summary>The line reported on every export, or NULL when there were
        /// no placed rooms — nothing scanned, nothing to report.</summary>
        public string Summary()
        {
            if (RoomsRead <= 0) return null;

            string s = $"Room finishes: {RoomsRead} placed room(s) read, "
                     + $"{FloorsTiled} name a tiled floor, {WallsTiled} a tiled wall, "
                     + $"{SkirtingRuns} a skirting.";

            if (TilingSuppressedByLayerSource)
                return s + " Room tiling was NOT measured: the wall/floor types already carry "
                         + "tiled finish layers, and measuring both sources would price the same "
                         + "surface twice. Skirting still comes from the rooms.";

            if (FloorsTiled == 0 && UnrecognisedFloorFinishes.Count > 0)
            {
                var names = new List<string>();
                foreach (string n in UnrecognisedFloorFinishes)
                {
                    if (names.Count == MaxNamesShown) break;
                    names.Add(n);
                }
                return s + " Floor finishes named but not recognised as tiling: "
                         + string.Join(", ", names)
                         + (UnrecognisedFloorFinishes.Count > MaxNamesShown ? ", …" : "")
                         + ". Rename the finish, or price the surface by hand.";
            }

            if (FloorsTiled == 0 && WallsTiled == 0 && SkirtingRuns == 0)
                return s + " The rooms carry no finish text, so no finish can be measured from "
                         + "them — record Floor / Wall / Base Finish on the rooms, or give the "
                         + "floor and wall types a tiled finish layer.";

            return s;
        }
    }
}
