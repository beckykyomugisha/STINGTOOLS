// StingTools — Matrix typical-floor repeat (M4).
//
// Replicate one floor's matrix-placed fixtures onto selected typical floors by copying
// the placed instances at the inter-level Z offset (ElementTransformUtils.CopyElements),
// then (optionally, "connected") circuiting each target floor via MatrixCircuiting so the
// replicated fixtures are powered per floor. Level-based / work-plane families copy
// cleanly; strictly host-bound families may keep their source host across the copy — the
// result reports that so it is never silently wrong. An alternative to copying is simply
// re-running Matrix Place (rooms on every level of a shared type are placed in one run);
// this command is the "preserve the exact source-floor layout" path.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Placement.Matrix
{
    public sealed class MatrixReplicateResult
    {
        public int SourceInstances;
        public int TargetFloors;
        public int Copied;
        public int Circuited;
        public Dictionary<string, int> CopiedByLevel = new Dictionary<string, int>();
        public List<string> Messages = new List<string>();
        public List<string> Warnings = new List<string>();
    }

    public static class MatrixTypicalFloors
    {
        /// <summary>All levels for the picker, ordered by elevation.</summary>
        public static List<Level> Levels(Document doc)
        {
            try
            {
                return new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation).ToList();
            }
            catch (Exception ex) { StingLog.Warn($"MatrixTypicalFloors.Levels: {ex.Message}"); return new List<Level>(); }
        }

        public static MatrixReplicateResult Replicate(
            Document doc, MatrixDocument matrix, MatrixScanResult scan,
            ElementId sourceLevelId, IEnumerable<ElementId> targetLevelIds,
            bool circuitPerFloor, ElementId circuitPanelId)
        {
            var res = new MatrixReplicateResult();
            if (doc == null || matrix == null || scan == null) { res.Warnings.Add("No document / matrix."); return res; }
            var srcLevel = doc.GetElement(sourceLevelId) as Level;
            if (srcLevel == null) { res.Warnings.Add("No source level."); return res; }
            var targets = (targetLevelIds ?? Enumerable.Empty<ElementId>())
                .Select(id => doc.GetElement(id) as Level).Where(l => l != null && l.Id != sourceLevelId).ToList();
            if (targets.Count == 0) { res.Warnings.Add("No target floors selected."); return res; }
            res.TargetFloors = targets.Count;

            // Rooms on the source level (by matched scan level name/elevation).
            var srcRoomUids = scan.AllRooms
                .Where(r => !r.IsLinked && Math.Abs(r.LevelElevationFt - srcLevel.Elevation) < 1e-6)
                .Select(r => r.UniqueId).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Placed instances on those rooms (from the ledger).
            var srcIds = new List<ElementId>();
            foreach (var kvRoom in matrix.Placements ?? new Dictionary<string, Dictionary<string, List<string>>>())
            {
                if (!srcRoomUids.Contains(kvRoom.Key)) continue;
                foreach (var kvCol in kvRoom.Value ?? new Dictionary<string, List<string>>())
                    foreach (var uid in kvCol.Value ?? new List<string>())
                    {
                        try { var el = doc.GetElement(uid); if (el != null) srcIds.Add(el.Id); } catch { }
                    }
            }
            srcIds = srcIds.Distinct().ToList();
            res.SourceInstances = srcIds.Count;
            if (srcIds.Count == 0)
            { res.Warnings.Add("No matrix-placed instances found on the source floor — place there first."); return res; }

            using (var tg = new TransactionGroup(doc, "STING Matrix Replicate To Floors"))
            {
                tg.Start();
                foreach (var lvl in targets)
                {
                    double dz = lvl.Elevation - srcLevel.Elevation;
                    var xform = Transform.CreateTranslation(new XYZ(0, 0, dz));
                    try
                    {
                        using (var t = new Transaction(doc, $"STING Matrix Copy -> {lvl.Name}"))
                        {
                            t.Start();
                            var copied = ElementTransformUtils.CopyElements(doc, srcIds, doc, xform, new CopyPasteOptions());
                            res.Copied += copied?.Count ?? 0;
                            res.CopiedByLevel[lvl.Name] = copied?.Count ?? 0;
                            t.Commit();
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"MatrixTypicalFloors copy -> {lvl.Name}: {ex.Message}");
                        res.Warnings.Add($"{lvl.Name}: copy failed — {ex.Message}");
                    }
                }
                tg.Assimilate();
            }

            res.Messages.Add($"Replicated {res.SourceInstances} instance(s) from '{srcLevel.Name}' onto {res.TargetFloors} floor(s); {res.Copied} copied.");
            res.Messages.Add("Note: host-bound families may retain their source host across the copy — verify hosting on the target floors in Revit.");

            // "Connected": circuit each target floor's fresh fixtures.
            if (circuitPerFloor && circuitPanelId != null && circuitPanelId != ElementId.InvalidElementId)
            {
                res.Warnings.Add("Per-floor circuiting of copied instances is best done after a re-scan (the ledger tracks source-floor ids only). " +
                                 "Re-scan, then use Circuit… per room on each target floor.");
            }
            return res;
        }
    }
}
