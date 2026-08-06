// CoordLog.cs — document-aware accessor for the coordination log.
//
// One resolver, one codec, one file name. Every writer and reader routes through
// here so the writer/reader extension split that silently emptied the BCC
// coordination timeline cannot reappear: there is no longer a second spelling of
// the path for a caller to pick.
//
// Wire format lives in CoordLogFormat (Revit-free, unit-tested).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    /// <summary>Canonical read/append access to <c>coord_log.jsonl</c>.</summary>
    public static class CoordLog
    {
        /// <summary>Max entries retained; the log is a rolling record, not an archive.</summary>
        public const int MaxEntries = 1000;

        /// <summary>
        /// Resolve the log path. Prefers the canonical project data folder and falls
        /// back to a sidecar next to the model when no root resolves.
        /// </summary>
        public static string ResolvePath(Document doc)
        {
            if (doc == null || string.IsNullOrEmpty(doc.PathName)) return "";
            try
            {
                string p = ProjectFolderEngine.GetDataPath(doc, CoordLogFormat.FileName);
                if (!string.IsNullOrEmpty(p)) return p;
            }
            catch (Exception ex) { StingLog.Warn($"CoordLog.ResolvePath: {ex.Message}"); }

            return Path.Combine(Path.GetDirectoryName(doc.PathName) ?? "", CoordLogFormat.SidecarFileName);
        }

        /// <summary>
        /// Existing log path to read from, preferring the canonical location but
        /// falling back to the sidecar and to the pre-unification <c>.json</c>
        /// spellings so history written before this change still displays.
        /// </summary>
        public static string ResolveReadPath(Document doc)
        {
            if (doc == null) return "";

            string canonical = ResolvePath(doc);
            if (!string.IsNullOrEmpty(canonical) && File.Exists(canonical)) return canonical;

            foreach (string legacy in LegacyReadCandidates(doc))
                if (!string.IsNullOrEmpty(legacy) && File.Exists(legacy)) return legacy;

            return canonical;
        }

        private static IEnumerable<string> LegacyReadCandidates(Document doc)
        {
            string modelDir = "";
            try { modelDir = Path.GetDirectoryName(doc.PathName ?? "") ?? ""; } catch { }

            string dataJson = "";
            try { dataJson = ProjectFolderEngine.GetDataPath(doc, "coord_log.json"); } catch { }
            if (!string.IsNullOrEmpty(dataJson)) yield return dataJson;

            if (!string.IsNullOrEmpty(modelDir))
            {
                yield return Path.Combine(modelDir, ".sting_coord_log.jsonl");
                yield return Path.Combine(modelDir, ".sting_coord_log.json");
            }
        }

        /// <summary>
        /// Every coordination-log file that exists for this document — canonical plus
        /// any pre-unification spelling. "Clear the log" must remove all of them, or
        /// entries the user believes they deleted reappear from the legacy file.
        /// </summary>
        public static IEnumerable<string> AllExistingPaths(Document doc)
        {
            if (doc == null) yield break;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string canonical = ResolvePath(doc);
            if (!string.IsNullOrEmpty(canonical) && File.Exists(canonical) && seen.Add(canonical))
                yield return canonical;

            foreach (string legacy in LegacyReadCandidates(doc))
                if (!string.IsNullOrEmpty(legacy) && File.Exists(legacy) && seen.Add(legacy))
                    yield return legacy;
        }

        /// <summary>Append one entry as a single JSONL line.</summary>
        public static void Append(Document doc, JObject entry)
        {
            if (entry == null) return;
            try
            {
                string path = ResolvePath(doc);
                if (string.IsNullOrEmpty(path)) return;

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                File.AppendAllText(path, CoordLogFormat.FormatLine(entry) + Environment.NewLine);
            }
            catch (Exception ex) { StingLog.Warn($"CoordLog.Append: {ex.Message}"); }
        }

        /// <summary>Read every entry, oldest first. Returns empty on any failure.</summary>
        public static List<JObject> Read(Document doc)
        {
            try
            {
                string path = ResolveReadPath(doc);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new List<JObject>();
                return CoordLogFormat.ParseLines(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CoordLog.Read: {ex.Message}");
                return new List<JObject>();
            }
        }

        /// <summary>Trim the log to <see cref="MaxEntries"/>, newest retained.</summary>
        public static void EnforceCap(Document doc)
        {
            try
            {
                string path = ResolvePath(doc);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

                var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                if (lines.Count <= MaxEntries) return;

                File.WriteAllLines(path, CoordLogFormat.Cap(lines, MaxEntries));
            }
            catch (Exception ex) { StingLog.Warn($"CoordLog.EnforceCap: {ex.Message}"); }
        }
    }
}
