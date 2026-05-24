// AuditLog.cs — template engine v1.1 (S16).
//
// Append-only monthly JSONL audit log with SHA-256 tamper-evidence chain.
// Every entry hashes {ts, user, action, doc_id, payload, prev_hash} — the
// resulting entry_hash becomes the next entry's prev_hash. Chain can be
// verified offline with <see cref="VerifyChain"/>.
//
// Storage: _BIM_COORD/audit_log_{yyyy}_{MM}.jsonl (new file each month).

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace Planscape.Docs.Workflow
{
    public class AuditEntry
    {
        [JsonProperty("ts")]         public string Ts { get; set; }
        [JsonProperty("user")]       public string User { get; set; }
        [JsonProperty("action")]     public string Action { get; set; }
        [JsonProperty("doc_id")]     public string DocId { get; set; }
        [JsonProperty("payload")]    public JObject Payload { get; set; }
        [JsonProperty("prev_hash")]  public string PrevHash { get; set; }
        [JsonProperty("entry_hash")] public string EntryHash { get; set; }
    }

    public static class AuditLog
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<string, StreamWriter> _writers =
            new Dictionary<string, StreamWriter>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, FileStream> _streams =
            new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);

        public static void Append(Document doc, string action, string docId, JObject payload)
        {
            lock (_lock)
            {
                try
                {
                    string path = LogPath(doc, DateTime.UtcNow);
                    string prevHash = GetLastHash(path);

                    var entry = new AuditEntry
                    {
                        Ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        User = Environment.UserName ?? "unknown",
                        Action = action,
                        DocId = docId,
                        Payload = payload ?? new JObject(),
                        PrevHash = prevHash ?? "sha256:" + new string('0', 64)
                    };
                    entry.EntryHash = ComputeHash(entry);

                    string line = JsonConvert.SerializeObject(entry, Formatting.None);

                    if (!_writers.TryGetValue(path, out var writer))
                    {
                        var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                        writer = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true };
                        _streams[path] = fs;
                        _writers[path] = writer;
                    }
                    writer.WriteLine(line);
                }
                catch (Exception ex) { StingLog.Error("AuditLog.Append failed", ex); }
            }
        }

        public static void Shutdown()
        {
            lock (_lock)
            {
                foreach (var w in _writers.Values)
                {
                    try { w.Flush(); w.Dispose(); } catch { /* ignored */ }
                }
                foreach (var s in _streams.Values)
                {
                    try { s.Dispose(); } catch { /* ignored */ }
                }
                _writers.Clear();
                _streams.Clear();
            }
        }

        public static List<AuditEntry> Read(Document doc, DateTime from, DateTime to, string actionFilter = null)
        {
            var results = new List<AuditEntry>();
            var cur = new DateTime(from.Year, from.Month, 1);
            var last = new DateTime(to.Year,   to.Month,   1);

            while (cur <= last)
            {
                string path = LogPath(doc, cur);
                if (File.Exists(path))
                {
                    foreach (string line in File.ReadAllLines(path))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        AuditEntry entry;
                        try { entry = JsonConvert.DeserializeObject<AuditEntry>(line); }
                        catch { continue; }
                        if (entry == null) continue;
                        if (!DateTime.TryParse(entry.Ts, null,
                                System.Globalization.DateTimeStyles.AssumeUniversal, out var ts)) continue;
                        if (ts < from || ts > to) continue;
                        if (!string.IsNullOrEmpty(actionFilter) &&
                            !string.Equals(entry.Action, actionFilter, StringComparison.OrdinalIgnoreCase)) continue;
                        results.Add(entry);
                    }
                }
                cur = cur.AddMonths(1);
            }
            return results;
        }

        public static bool VerifyChain(Document doc, string fileOrMonth)
        {
            string path = fileOrMonth != null && File.Exists(fileOrMonth)
                ? fileOrMonth
                : LogPath(doc, ParseMonth(fileOrMonth));
            if (!File.Exists(path)) return true;

            string prev = null;
            foreach (string line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                AuditEntry entry;
                try { entry = JsonConvert.DeserializeObject<AuditEntry>(line); }
                catch { return false; }
                if (entry == null) return false;

                if (prev != null && !string.Equals(entry.PrevHash, prev, StringComparison.Ordinal))
                    return false;
                string recomputed = ComputeHash(entry);
                if (!string.Equals(entry.EntryHash, recomputed, StringComparison.Ordinal))
                    return false;
                prev = entry.EntryHash;
            }
            return true;
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static string ComputeHash(AuditEntry e)
        {
            string canonical = JsonConvert.SerializeObject(new
            {
                ts        = e.Ts,
                user      = e.User,
                action    = e.Action,
                doc_id    = e.DocId,
                payload   = e.Payload,
                prev_hash = e.PrevHash
            }, Formatting.None);
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
            return "sha256:" + BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private static string GetLastHash(string path)
        {
            if (!File.Exists(path)) return null;
            string last = null;
            foreach (string line in File.ReadAllLines(path))
                if (!string.IsNullOrWhiteSpace(line)) last = line;
            if (string.IsNullOrEmpty(last)) return null;
            try { return JsonConvert.DeserializeObject<AuditEntry>(last)?.EntryHash; }
            catch { return null; }
        }

        private static string LogPath(Document doc, DateTime ts)
        {
            string root = ResolveProjectRoot(doc);
            string dir  = Path.Combine(root, "_BIM_COORD");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"audit_log_{ts:yyyy}_{ts:MM}.jsonl");
        }

        private static DateTime ParseMonth(string s)
        {
            if (DateTime.TryParse(s, out var d)) return d;
            return DateTime.UtcNow;
        }

        private static string ResolveProjectRoot(Document doc)
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
                string p = doc?.PathName;
                if (!string.IsNullOrEmpty(p))
                {
                    string dir = Path.GetDirectoryName(p);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
                }
            }
            catch { /* ignored */ }
            return Path.Combine(Path.GetTempPath(), "Planscape", "BIMCoord");
        }
    }
}
