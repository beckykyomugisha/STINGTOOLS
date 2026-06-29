// ══════════════════════════════════════════════════════════════════════════
//  BoqDriftStatus.cs — Phase 2C. Persisted drift state for the passive banner.
//
//  Records whether the live bill has drifted from the last saved snapshot
//  (checksum compare) plus a summary, so the dashboard drift banner survives a
//  panel reopen until a new snapshot is saved. Persisted to
//  <project>/_BIM_COORD/boq_drift.json (same pattern as rate_feeds.json).
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.BOQ
{
    public class BoqDriftStatus
    {
        public string SchemaVersion = "1.0";
        public bool Drifted;
        public int ChangedLines;
        public double NetDeltaUgx;
        public string SnapshotLabel = "";
        public string SnapshotChecksum = "";
        public string LiveChecksum = "";
        public DateTime CheckedUtc = DateTime.UtcNow;
    }

    internal static class BoqDriftStore
    {
        private const string FileName = "boq_drift.json";

        public static BoqDriftStatus Load(Document doc)
        {
            try
            {
                string path = ResolvePath(doc);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                return JsonConvert.DeserializeObject<BoqDriftStatus>(File.ReadAllText(path));
            }
            catch (Exception ex) { StingLog.Warn($"BoqDriftStore.Load: {ex.Message}"); return null; }
        }

        public static void Save(Document doc, BoqDriftStatus status)
        {
            if (status == null) return;
            try
            {
                string path = ResolvePath(doc);
                if (string.IsNullOrEmpty(path)) return;   // unsaved doc — memory only
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                status.CheckedUtc = DateTime.UtcNow;
                File.WriteAllText(path, JsonConvert.SerializeObject(status, Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn($"BoqDriftStore.Save: {ex.Message}"); }
        }

        private static string ResolvePath(Document doc)
        {
            string parent = Path.GetDirectoryName(doc?.PathName ?? "");
            if (string.IsNullOrEmpty(parent)) return null;
            return Path.Combine(parent, "_BIM_COORD", FileName);
        }
    }
}
