using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// F2 — Surface other-user material edits in workshared projects.
    ///
    /// Strategy: every MAT Refresh writes a per-user material-state
    /// snapshot to <c>&lt;project&gt;/_BIM_COORD/.mat_snapshot_&lt;user&gt;.json</c>.
    /// Peers' snapshots are then diffed against the current project
    /// material set; any peer-modified material whose name/class/cost/
    /// carbon differs from what this user last saw surfaces as an
    /// info-level alert in the MAT status footer + coord log.
    ///
    /// This is a lightweight "since-I-last-looked" detector — it doesn't
    /// hook the workshared Reload Latest event (that's a Revit-level
    /// nuisance to subscribe to safely). On every panel Refresh, the
    /// user's snapshot becomes the new baseline.
    /// </summary>
    public class MatSnapshotRow
    {
        public string Name { get; set; }
        public string Class { get; set; }
        public double Cost { get; set; }
        public double Carbon { get; set; }
    }

    public class PeerMaterialEdit
    {
        public string Peer { get; set; }
        public string MaterialName { get; set; }
        public string Field { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
    }

    public static class MaterialPeerEditWatcher
    {
        private const string SnapshotPrefix = ".mat_snapshot_";

        /// <summary>Write the current user's snapshot of the material set.</summary>
        public static void WriteMySnapshot(Document doc, IReadOnlyList<MaterialRow> rows)
        {
            if (doc == null || rows == null) return;
            try
            {
                string dir = Core.ProjectFolderEngine.GetDataPath(doc, "");
                if (string.IsNullOrEmpty(dir)) return;
                string path = Path.Combine(dir, SnapshotPrefix + SanitiseUser(Environment.UserName) + ".json");
                var data = rows.Select(r => new MatSnapshotRow
                {
                    Name = r.Name,
                    Class = r.Class,
                    Cost = r.Cost,
                    Carbon = r.CarbonKgCo2e,
                }).ToList();
                Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.None));
            }
            catch (Exception ex) { StingLog.Warn($"MaterialPeerEditWatcher.WriteMySnapshot: {ex.Message}"); }
        }

        /// <summary>Compare every peer snapshot against the current project
        /// material set and report differences.</summary>
        public static List<PeerMaterialEdit> Scan(Document doc, IReadOnlyList<MaterialRow> currentRows)
        {
            var edits = new List<PeerMaterialEdit>();
            if (doc == null || currentRows == null) return edits;
            try
            {
                string dir = Core.ProjectFolderEngine.GetDataPath(doc, "");
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return edits;

                string me = SanitiseUser(Environment.UserName);
                var current = currentRows.ToDictionary(r => r.Name ?? "", StringComparer.OrdinalIgnoreCase);

                foreach (var file in Directory.EnumerateFiles(dir, SnapshotPrefix + "*.json"))
                {
                    string fname = Path.GetFileNameWithoutExtension(file);
                    if (!fname.StartsWith(SnapshotPrefix)) continue;
                    string peer = fname.Substring(SnapshotPrefix.Length);
                    if (string.Equals(peer, me, StringComparison.OrdinalIgnoreCase)) continue;

                    List<MatSnapshotRow> peerRows;
                    try { peerRows = JsonConvert.DeserializeObject<List<MatSnapshotRow>>(File.ReadAllText(file)) ?? new List<MatSnapshotRow>(); }
                    catch (Exception ex) { StingLog.WarnRateLimited("PeerWatcher.Read", $"Peer snapshot '{file}': {ex.Message}"); continue; }

                    // Compare peer's snapshot row against current state.
                    // Mismatch → peer's view was different from now, which
                    // implies someone moved the project state. Coarse but
                    // useful — the snapshot file's mtime tells us when.
                    foreach (var prow in peerRows)
                    {
                        if (!current.TryGetValue(prow.Name ?? "", out var curr)) continue;
                        if (Math.Abs(prow.Cost - curr.Cost) > 0.01)
                            edits.Add(new PeerMaterialEdit { Peer = peer, MaterialName = prow.Name, Field = "Cost",
                                OldValue = prow.Cost.ToString("F2"), NewValue = curr.Cost.ToString("F2") });
                        if (Math.Abs(prow.Carbon - curr.CarbonKgCo2e) > 0.01)
                            edits.Add(new PeerMaterialEdit { Peer = peer, MaterialName = prow.Name, Field = "Carbon",
                                OldValue = prow.Carbon.ToString("F1"), NewValue = curr.CarbonKgCo2e.ToString("F1") });
                        if (!string.Equals(prow.Class ?? "", curr.Class ?? "", StringComparison.OrdinalIgnoreCase))
                            edits.Add(new PeerMaterialEdit { Peer = peer, MaterialName = prow.Name, Field = "Class",
                                OldValue = prow.Class, NewValue = curr.Class });
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"MaterialPeerEditWatcher.Scan: {ex.Message}"); }
            return edits;
        }

        private static string SanitiseUser(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "anon";
            var sb = new System.Text.StringBuilder(raw.Length);
            foreach (var c in raw)
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }
    }
}
