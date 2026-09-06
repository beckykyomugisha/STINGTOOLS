using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core
{
    /// <summary>
    /// Document-aware wrapper over <see cref="WarningSnapshotFormat"/> — resolves the
    /// canonical path and appends trend rows.
    ///
    /// Path discipline: the root comes from <see cref="ProjectFolderEngine.GetDataPath"/>,
    /// never a hand-rolled combine against the coordination bucket literal. This mirrors
    /// <see cref="CoordLog"/> exactly — CoordStores is array-only (whole-file
    /// <c>JArray.Parse</c> + indented <c>WriteArray</c>) and cannot host a JSONL store
    /// without fighting its legacy-merge path, so CoordLog is the right precedent.
    ///
    /// Append-only below <see cref="WarningSnapshotFormat.MaxEntries"/>: normal appends
    /// never rewrite an existing line. Only when the file exceeds the cap is it rewritten
    /// once, dropping the oldest rows.
    /// </summary>
    internal static class WarningSnapshotStore
    {
        /// <summary>Canonical path, or "" when no root resolves (unsaved doc).</summary>
        public static string ResolvePath(Document doc)
        {
            if (doc == null || string.IsNullOrEmpty(doc.PathName)) return "";
            try
            {
                string p = ProjectFolderEngine.GetDataPath(doc, WarningSnapshotFormat.FileName);
                if (!string.IsNullOrEmpty(p)) return p;
            }
            catch (Exception ex) { StingLog.Warn($"WarningSnapshotStore.ResolvePath: {ex.Message}"); }

            return Path.Combine(Path.GetDirectoryName(doc.PathName) ?? "",
                                WarningSnapshotFormat.SidecarFileName);
        }

        /// <summary>
        /// Append one snapshot. Never throws — trend data is diagnostic, and losing a
        /// row must never take down the scan that produced it.
        /// </summary>
        public static void Append(Document doc, WarningSnapshotFormat.WarningSnapshot snap)
        {
            if (snap == null) return;
            try
            {
                string path = ResolvePath(doc);
                if (string.IsNullOrEmpty(path)) return;   // unsaved doc — nothing to write to

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                File.AppendAllText(path, WarningSnapshotFormat.FormatLine(snap) + Environment.NewLine);
                EnforceCap(path);
            }
            catch (Exception ex) { StingLog.Warn($"WarningSnapshotStore.Append: {ex.Message}"); }
        }

        /// <summary>All snapshots, oldest first. Empty when absent or unreadable.</summary>
        public static List<WarningSnapshotFormat.WarningSnapshot> Read(Document doc)
        {
            try
            {
                string path = ResolvePath(doc);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return new List<WarningSnapshotFormat.WarningSnapshot>();
                return WarningSnapshotFormat.ParseLines(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"WarningSnapshotStore.Read: {ex.Message}");
                return new List<WarningSnapshotFormat.WarningSnapshot>();
            }
        }

        /// <summary>
        /// The two most recent scan snapshots (previous, current) for a
        /// "since last scan" hint. Either may be null. Baseline rows are excluded
        /// so the delta always compares like with like.
        /// </summary>
        public static (WarningSnapshotFormat.WarningSnapshot Previous,
                       WarningSnapshotFormat.WarningSnapshot Current) LastTwoScans(Document doc)
        {
            var scans = Read(doc)
                .Where(s => string.Equals(s.Kind, WarningSnapshotFormat.KindScan, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (scans.Count == 0) return (null, null);
            if (scans.Count == 1) return (null, scans[scans.Count - 1]);
            return (scans[scans.Count - 2], scans[scans.Count - 1]);
        }

        /// <summary>Rewrite only when the file has grown past the cap.</summary>
        private static void EnforceCap(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                if (lines.Count <= WarningSnapshotFormat.MaxEntries) return;   // append-only path

                var capped = WarningSnapshotFormat.Cap(lines, WarningSnapshotFormat.MaxEntries);
                OutputLocationHelper.WriteAllTextAtomic(
                    path, string.Join(Environment.NewLine, capped) + Environment.NewLine);
                StingLog.Info($"WarningSnapshotStore: capped trend store to {WarningSnapshotFormat.MaxEntries} rows.");
            }
            catch (Exception ex) { StingLog.Warn($"WarningSnapshotStore.EnforceCap: {ex.Message}"); }
        }
    }
}
