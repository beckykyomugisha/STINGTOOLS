// DocumentIdentityGenerator.cs — template engine v1.1 (S04).
//
// Generates ISO 19650-style document numbers from a TemplateManifest's
// identifier_format token string. Sequence counters are persisted atomically
// to _BIM_COORD/doc_sequences.json keyed by (type|role|fb|sb).
//
// Format tokens supported: {project_code} {originator} {role} {fb} {sb}
// {type} {number} {number:D4} {number:D6}. Regex: \{(\w+)(?::D(\d+))?\}.
//
// v1.1 adds Reserve(...) to atomically grab a block of numbers for bulk
// operations (e.g. S13 "Issue selected" / S14 tabular deliverables).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace Planscape.Docs.Templates
{
    public static class DocumentIdentityGenerator
    {
        private static readonly Regex TokenRx = new Regex(@"\{(\w+)(?::D(\d+))?\}", RegexOptions.Compiled);
        private static readonly object _lock = new object();

        /// <summary>Mints the next document number, persisting the incremented counter.</summary>
        public static string Next(Document doc, TemplateManifest m, string type, string role, string fb, string sb)
        {
            lock (_lock)
            {
                int n = Increment(doc, Key(type, role, fb, sb), 1);
                return Format(m?.Project?.IdentifierFormat, m?.Project?.ProjectCode, m?.Project?.OriginatorCode,
                              type, role, fb, sb, n);
            }
        }

        /// <summary>Renders what Next would emit at <paramref name="number"/> without touching state.</summary>
        public static string Preview(TemplateManifest m, string type, string role, string fb, string sb, int number)
        {
            return Format(m?.Project?.IdentifierFormat, m?.Project?.ProjectCode, m?.Project?.OriginatorCode,
                          type, role, fb, sb, number);
        }

        /// <summary>Reads the number Next() would mint — without incrementing.</summary>
        public static int PeekNext(Document doc, TemplateManifest m, string type, string role, string fb, string sb)
        {
            lock (_lock)
            {
                var store = LoadStore(doc);
                store.TryGetValue(Key(type, role, fb, sb), out int current);
                return current + 1;
            }
        }

        /// <summary>Reserves a contiguous block of numbers and returns them (v1.1 bulk).</summary>
        public static List<int> Reserve(Document doc, TemplateManifest m, string type, string role, string fb, string sb, int count)
        {
            if (count <= 0) return new List<int>();
            lock (_lock)
            {
                var key = Key(type, role, fb, sb);
                var store = LoadStore(doc);
                store.TryGetValue(key, out int current);
                int first = current + 1;
                int last  = current + count;
                store[key] = last;
                SaveStore(doc, store);
                return Enumerable.Range(first, count).ToList();
            }
        }

        /// <summary>Expands <paramref name="format"/> with provided tokens and a zero-padded number.</summary>
        public static string Format(string format, string projectCode, string originator,
                                    string type, string role, string fb, string sb, int number)
        {
            if (string.IsNullOrEmpty(format))
                format = "{project_code}-{originator}-{role}-{fb}-{sb}-{type}-{number:D4}";

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "project_code", projectCode ?? "" },
                { "originator",   originator   ?? "PLNS" },
                { "role",         role         ?? "XX" },
                { "fb",           fb           ?? "ZZ" },
                { "sb",           sb           ?? "XX" },
                { "type",         type         ?? "DR" }
            };

            return TokenRx.Replace(format, match =>
            {
                string key = match.Groups[1].Value;
                string pad = match.Groups[2].Success ? match.Groups[2].Value : null;
                if (string.Equals(key, "number", StringComparison.OrdinalIgnoreCase))
                {
                    int width = 4;
                    if (!string.IsNullOrEmpty(pad) && int.TryParse(pad, out int parsed) && parsed > 0)
                        width = parsed;
                    return number.ToString(new string('0', width));
                }
                return values.TryGetValue(key, out string v) ? v : match.Value;
            });
        }

        // ── Internal persistence ────────────────────────────────────────────

        private static string Key(string type, string role, string fb, string sb)
            => $"{(type ?? "").ToUpperInvariant()}|{(role ?? "").ToUpperInvariant()}|{(fb ?? "").ToUpperInvariant()}|{(sb ?? "").ToUpperInvariant()}";

        private static int Increment(Document doc, string key, int by)
        {
            var store = LoadStore(doc);
            store.TryGetValue(key, out int current);
            int next = current + by;
            store[key] = next;
            SaveStore(doc, store);
            return next;
        }

        private static string StorePath(Document doc)
        {
            string root = TryProjectRoot(doc);
            string dir  = Path.Combine(root, "_BIM_COORD");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "doc_sequences.json");
        }

        private static Dictionary<string, int> LoadStore(Document doc)
        {
            string path = StorePath(doc);
            if (!File.Exists(path)) return new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                // S3.6.1 — version gate before deserialise.
                StingTools.Core.PluginSchemaVersion.EnsureFileVersion(
                    path, "planscape.doc-sequences",
                    StingTools.Core.PluginSchemaVersion.CurrentDocSequences);
                var dict = JsonConvert.DeserializeObject<Dictionary<string, int>>(File.ReadAllText(path));
                return dict ?? new Dictionary<string, int>(StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DocumentIdentityGenerator: could not read {path}: {ex.Message}");
                return new Dictionary<string, int>(StringComparer.Ordinal);
            }
        }

        private static void SaveStore(Document doc, Dictionary<string, int> store)
        {
            string path = StorePath(doc);
            string tmp  = path + ".tmp";
            try
            {
                File.WriteAllText(tmp, JsonConvert.SerializeObject(store, Formatting.Indented));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                StingLog.Error($"DocumentIdentityGenerator: atomic save failed for {path}", ex);
            }
        }

        private static string TryProjectRoot(Document doc)
        {
            // Folder consolidation: nest "_BIM_COORD" inside the unified
            // project root's _data folder rather than as a sibling of the .rvt.
            try
            {
                string consolidated = StingTools.Core.ProjectFolderEngine.GetDataPath(doc);
                if (!string.IsNullOrEmpty(consolidated)) return consolidated;
            }
            catch { /* fall through to legacy lookup */ }
            try
            {
                if (doc != null)
                {
                    string p = doc.PathName;
                    if (!string.IsNullOrEmpty(p))
                    {
                        string dir = Path.GetDirectoryName(p);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
                    }
                }
            }
            catch { /* fall through */ }
            return Path.Combine(Path.GetTempPath(), "Planscape", "BIMCoord");
        }
    }
}
