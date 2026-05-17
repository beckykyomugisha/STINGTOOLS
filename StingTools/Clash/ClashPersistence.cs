// ClashPersistence.cs — clashes.json I/O.
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Core.Clash
{
    public static class ClashPersistence
    {
        // F3: Ring-buffer cap for the archive directory. Older entries are
        // pruned during Save so the directory size stays bounded. Two months
        // at one run/day is a comfortable history window for trend reporting.
        public const int ArchiveCap = 30;

        public static ClashRunRecord Load(string path)
        {
            if (!File.Exists(path)) return null;
            try { return JsonConvert.DeserializeObject<ClashRunRecord>(File.ReadAllText(path)); }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        public static void Save(ClashRunRecord run, string path)
        {
            if (run == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string json = JsonConvert.SerializeObject(run, Formatting.Indented);
            File.WriteAllText(path, json);

            // F3: Mirror to <dir>/archive/clashes_<utc>.json so the prior
            //     overwrite isn't destructive. Trend reports (BCC dashboard,
            //     XLSX export) read the archive to render time series.
            //     Capped at ArchiveCap entries — oldest pruned.
            try
            {
                string dir = Path.GetDirectoryName(path) ?? "";
                if (string.IsNullOrEmpty(dir)) return;
                string archiveDir = Path.Combine(dir, "archive");
                Directory.CreateDirectory(archiveDir);
                string stamp = (run != null ? run.RunId : "")
                    + "_" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
                string archivePath = Path.Combine(archiveDir, $"clashes_{stamp}.json");
                File.WriteAllText(archivePath, json);
                PruneArchive(archiveDir);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ClashPersistence.Save archive: {ex.Message}");
            }
        }

        /// <summary>
        /// F3: Keep only the most recent ArchiveCap entries in the archive
        /// directory. Files older than that are deleted. Best-effort —
        /// archive is a derived artefact, never a system of record.
        /// </summary>
        public static void PruneArchive(string archiveDir)
        {
            try
            {
                if (!Directory.Exists(archiveDir)) return;
                var files = Directory.GetFiles(archiveDir, "clashes_*.json")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToList();
                if (files.Count <= ArchiveCap) return;
                foreach (var f in files.Skip(ArchiveCap))
                {
                    try { f.Delete(); }
                    catch (Exception ex) { StingLog.Warn($"PruneArchive {f.Name}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"PruneArchive: {ex.Message}"); }
        }

        /// <summary>
        /// F3: Load up to N most recent archived runs (newest first). Used
        /// by trend reporting + the XLSX exporter (F7).
        /// </summary>
        public static ClashRunRecord[] LoadArchive(string archiveDir, int max)
        {
            try
            {
                if (!Directory.Exists(archiveDir) || max <= 0) return Array.Empty<ClashRunRecord>();
                return Directory.GetFiles(archiveDir, "clashes_*.json")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Take(max)
                    .Select(f =>
                    {
                        try { return JsonConvert.DeserializeObject<ClashRunRecord>(File.ReadAllText(f.FullName)); }
                        catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
                    })
                    .Where(r => r != null)
                    .ToArray();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ClashPersistence.LoadArchive: {ex.Message}");
                return Array.Empty<ClashRunRecord>();
            }
        }
    }
}
