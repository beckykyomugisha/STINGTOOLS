using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace StingTools.Core.TemplateManager
{
    /// <summary>
    /// SHA-256 tamper-evident audit log for Template Manager ops. Each entry
    /// embeds the previous entry's hash so subsequent edits to the file
    /// invalidate the chain (governance / client-defence use case).
    ///
    /// Persists to <project>/_BIM_COORD/template_audit_log_{yyyy}_{MM}.jsonl.
    /// Mirror of Docs/Workflow/AuditLog.cs pattern, scoped to template ops
    /// so the two logs stay distinct + queryable.
    /// </summary>
    public sealed class TemplateAuditEntry
    {
        public DateTime UtcAt { get; set; } = DateTime.UtcNow;
        public string Operation { get; set; } = "";
        public string OperationLabel { get; set; } = "";
        public string User { get; set; } = "";
        public string Severity { get; set; } = "Info";
        public string Headline { get; set; } = "";
        public double DurationMs { get; set; }
        public string DocumentPath { get; set; } = "";
        public string DocumentTitle { get; set; } = "";
        public int CreatedCount { get; set; }
        public int SkippedCount { get; set; }
        public int FailedCount { get; set; }
        public string PrevHash { get; set; } = "";
        public string Hash { get; set; } = "";
    }

    public static class AuditLog
    {
        private static readonly object _lock = new();
        // Per-file last-hash cache — multi-project Revit sessions don't cross-contaminate chains.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _lastHashByFile = new();

        public static string Append(Document doc, OperationResult result)
        {
            try
            {
                if (doc == null || result == null) return "";
                string projDir = Path.GetDirectoryName(doc.PathName ?? "") ?? "";
                if (string.IsNullOrEmpty(projDir)) return "";
                string dir = Path.Combine(projDir, "_BIM_COORD");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir,
                    $"template_audit_log_{result.CompletedUtc:yyyy_MM}.jsonl");

                lock (_lock)
                {
                    string lastHash = _lastHashByFile.GetValueOrDefault(file, "");
                    // On first append per file, seed chain by tailing the file.
                    if (string.IsNullOrEmpty(lastHash) && File.Exists(file))
                    {
                        try
                        {
                            var tail = File.ReadLines(file).LastOrDefault();
                            if (!string.IsNullOrEmpty(tail))
                            {
                                var prev = JsonConvert.DeserializeObject<TemplateAuditEntry>(tail);
                                if (prev != null) lastHash = prev.Hash ?? "";
                            }
                        }
                        catch { }
                    }
                    var entry = new TemplateAuditEntry
                    {
                        UtcAt = result.CompletedUtc,
                        Operation = result.Operation,
                        OperationLabel = result.OperationLabel,
                        User = Environment.UserName,
                        Severity = result.Severity.ToString(),
                        Headline = result.Headline,
                        DurationMs = result.DurationMs,
                        DocumentPath = doc.PathName ?? "",
                        DocumentTitle = doc.Title ?? "",
                        PrevHash = lastHash
                    };
                    // Counters
                    if (result.Counters != null)
                    {
                        if (result.Counters.TryGetValue("created", out var c) && int.TryParse(c, out var ci)) entry.CreatedCount = ci;
                        if (result.Counters.TryGetValue("skipped", out var sk) && int.TryParse(sk, out var si)) entry.SkippedCount = si;
                        if (result.Counters.TryGetValue("failed",  out var f)  && int.TryParse(f, out var fi))  entry.FailedCount = fi;
                    }
                    // Chain hash
                    string body = JsonConvert.SerializeObject(entry);
                    entry.Hash = Sha256(lastHash + body);
                    _lastHashByFile[file] = entry.Hash;
                    string line = JsonConvert.SerializeObject(entry);
                    File.AppendAllText(file, line + Environment.NewLine);
                    return file;
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"TemplateAuditLog.Append: {ex.Message}");
                return "";
            }
        }

        /// <summary>Verify the SHA-256 hash chain of one month's log file.</summary>
        public static bool VerifyChain(string path)
        {
            try
            {
                if (!File.Exists(path)) return true;
                string prev = "";
                foreach (var line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var entry = JsonConvert.DeserializeObject<TemplateAuditEntry>(line);
                    if (entry == null) continue;
                    // Recompute
                    string saved = entry.Hash;
                    entry.Hash = "";
                    string body = JsonConvert.SerializeObject(entry);
                    string recomputed = Sha256(prev + body);
                    if (!string.Equals(saved, recomputed, StringComparison.Ordinal)) return false;
                    prev = saved;
                }
                return true;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"VerifyChain: {ex.Message}");
                return false;
            }
        }

        private static string Sha256(string s)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(s ?? "")));
        }
    }
}
