using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    /// <summary>
    /// Pure codec for the warning-snapshot trend store — no Revit, no filesystem,
    /// so it is unit-testable on a plain net8.0 host (linked into
    /// StingTools.Tags.Tests via &lt;Compile Include&gt;).
    ///
    /// Deliberately mirrors <see cref="CoordLogFormat"/>: a line-delimited JSON
    /// store, one object per line, written with <c>Formatting.None</c> so a line
    /// break always terminates a record. That property is load-bearing — a
    /// pretty-printed record would corrupt every subsequent parse.
    ///
    /// This is TREND data, not a warnings dump: severity/category tallies and the
    /// health-score inputs, never the per-warning rows. A snapshot is ~200 bytes.
    /// </summary>
    internal static class WarningSnapshotFormat
    {
        /// <summary>Canonical file name under the project data folder.</summary>
        public const string FileName = "warning_snapshots.jsonl";

        /// <summary>Sidecar used only when no project root resolves (unsaved doc).</summary>
        public const string SidecarFileName = ".sting_warning_snapshots.jsonl";

        /// <summary>A snapshot written after a real scan (not a cache hit).</summary>
        public const string KindScan = "scan";

        /// <summary>A snapshot written when a warning baseline is saved.</summary>
        public const string KindBaseline = "baseline";

        /// <summary>
        /// Retention cap. The store is append-only below this; only once a file
        /// exceeds the cap is it rewritten, dropping the oldest lines.
        /// </summary>
        public const int MaxEntries = 2000;

        /// <summary>One row of the trend store. Revit-free by construction —
        /// severity/category keys arrive as strings, not enums.</summary>
        internal class WarningSnapshot
        {
            public DateTime TsUtc { get; set; } = DateTime.UtcNow;
            public string Kind { get; set; } = KindScan;
            public string User { get; set; } = "";
            public int Total { get; set; }
            public int AutoFixable { get; set; }
            public int ManualReview { get; set; }
            public int HealthScore { get; set; }
            /// <summary>Baseline total at scan time, when one exists.</summary>
            public int? BaselineTotal { get; set; }
            public Dictionary<string, int> BySeverity { get; set; } = new Dictionary<string, int>();
            public Dictionary<string, int> ByCategory { get; set; } = new Dictionary<string, int>();
        }

        /// <summary>Serialise one snapshot to a single line (no trailing newline).</summary>
        public static string FormatLine(WarningSnapshot snap)
        {
            if (snap == null) return "";

            var o = new JObject
            {
                ["ts"]            = snap.TsUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                ["kind"]          = string.IsNullOrEmpty(snap.Kind) ? KindScan : snap.Kind,
                ["user"]          = snap.User ?? "",
                ["total"]         = snap.Total,
                ["auto_fixable"]  = snap.AutoFixable,
                ["manual_review"] = snap.ManualReview,
                ["health_score"]  = snap.HealthScore,
                ["by_severity"]   = ToCountObject(snap.BySeverity),
                ["by_category"]   = ToCountObject(snap.ByCategory),
            };
            if (snap.BaselineTotal.HasValue) o["baseline_total"] = snap.BaselineTotal.Value;

            // Formatting.None is load-bearing — see class remarks.
            return o.ToString(Formatting.None);
        }

        private static JObject ToCountObject(Dictionary<string, int> counts)
        {
            var o = new JObject();
            if (counts == null) return o;
            foreach (var kv in counts)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                o[kv.Key] = kv.Value;
            }
            return o;
        }

        /// <summary>
        /// Parse a whole file. Malformed lines are skipped rather than throwing —
        /// a truncated final line (crash mid-append) must not cost the caller the
        /// entire trend history.
        /// </summary>
        public static List<WarningSnapshot> ParseLines(string content)
        {
            var result = new List<WarningSnapshot>();
            if (string.IsNullOrWhiteSpace(content)) return result;

            foreach (string raw in content.Split('\n'))
            {
                string line = raw?.Trim()?.TrimEnd(',');
                if (string.IsNullOrWhiteSpace(line)) continue;
                // Tolerate a file that was once a JSON array (defensive; this store
                // has only ever been JSONL, but the coord log had exactly that legacy).
                if (line == "[" || line == "]") continue;

                WarningSnapshot snap = TryParseLine(line);
                if (snap != null) result.Add(snap);
            }
            return result;
        }

        /// <summary>Parse a single line; null when the line is not a usable record.</summary>
        public static WarningSnapshot TryParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            try
            {
                var o = JObject.Parse(line);
                var snap = new WarningSnapshot
                {
                    Kind          = (string)o["kind"] ?? KindScan,
                    User          = (string)o["user"] ?? "",
                    Total         = ReadInt(o, "total"),
                    AutoFixable   = ReadInt(o, "auto_fixable"),
                    ManualReview  = ReadInt(o, "manual_review"),
                    HealthScore   = ReadInt(o, "health_score"),
                    BySeverity    = ReadCounts(o["by_severity"] as JObject),
                    ByCategory    = ReadCounts(o["by_category"] as JObject),
                };

                string ts = o["ts"]?.Type == JTokenType.Date
                    ? o["ts"].Value<DateTime>().ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)
                    : o["ts"]?.Value<string>();
                // RoundtripKind must NOT be OR-ed with AdjustToUniversal — that
                // combination throws ArgumentException. Round-trip the kind, then
                // normalise to UTC explicitly.
                if (!string.IsNullOrEmpty(ts) && DateTime.TryParse(ts, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out DateTime parsed))
                    snap.TsUtc = parsed.ToUniversalTime();

                int? baseline = ReadNullableInt(o, "baseline_total");
                if (baseline.HasValue) snap.BaselineTotal = baseline.Value;

                return snap;
            }
            catch { return null; }   // malformed line — skip, never throw
        }

        private static int ReadInt(JObject o, string key) => ReadNullableInt(o, key) ?? 0;

        /// <summary>
        /// Typed read. A plain <c>(string)</c> cast is NOT usable here: Newtonsoft
        /// throws converting a numeric JValue to string, which would land every
        /// well-formed line in TryParseLine's catch and silently return null.
        /// </summary>
        private static int? ReadNullableInt(JObject o, string key)
        {
            var tok = o?[key];
            if (tok == null || tok.Type == JTokenType.Null) return null;
            try
            {
                if (tok.Type == JTokenType.Integer || tok.Type == JTokenType.Float)
                    return tok.Value<int>();
                return int.TryParse(tok.Value<string>(), out int v) ? v : (int?)null;
            }
            catch { return null; }
        }

        private static Dictionary<string, int> ReadCounts(JObject o)
        {
            var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (o == null) return d;
            foreach (var p in o.Properties())
            {
                int? v = ReadNullableInt(o, p.Name);
                if (v.HasValue) d[p.Name] = v.Value;
            }
            return d;
        }

        /// <summary>Keep the most recent <paramref name="maxLines"/> lines.</summary>
        public static List<string> Cap(List<string> lines, int maxLines)
        {
            if (lines == null) return new List<string>();
            if (maxLines <= 0 || lines.Count <= maxLines) return lines;
            return lines.Skip(lines.Count - maxLines).ToList();
        }

        // ── Server payloads ────────────────────────────────────────────────
        //
        // Built here rather than at the call site so the exact wire contract is
        // asserted by unit tests. Field names must match the server records in
        // Planscape.API/Controllers/WarningsController.cs — ASP.NET model binding
        // is case-insensitive, so camelCase binds to the PascalCase record.

        /// <summary>
        /// Payload for <c>POST /api/projects/{id}/warnings/report</c> —
        /// server record <c>PushWarningReportRequest(int TotalWarnings,
        /// int HealthScore, string? ByCategoryJson, string? BySeverityJson)</c>.
        /// </summary>
        public static JObject BuildReportPayload(WarningSnapshot snap)
        {
            if (snap == null) return null;
            return new JObject
            {
                ["totalWarnings"]  = snap.Total,
                ["healthScore"]    = snap.HealthScore,
                // The server stores these as opaque JSON *strings*, not objects.
                ["byCategoryJson"] = ToCountObject(snap.ByCategory).ToString(Formatting.None),
                ["bySeverityJson"] = ToCountObject(snap.BySeverity).ToString(Formatting.None),
            };
        }

        /// <summary>
        /// Payload for <c>POST /api/projects/{id}/warnings/baseline</c> —
        /// server record <c>SaveWarningBaselineRequest(int WarningCount,
        /// int HealthScore, int TotalElements, double CompliancePercent)</c>.
        /// </summary>
        public static JObject BuildBaselinePayload(WarningSnapshot snap, int totalElements, double compliancePercent)
        {
            if (snap == null) return null;
            return new JObject
            {
                ["warningCount"]      = snap.Total,
                ["healthScore"]       = snap.HealthScore,
                ["totalElements"]     = totalElements,
                ["compliancePercent"] = compliancePercent,
            };
        }

        // ── Trend ──────────────────────────────────────────────────────────

        /// <summary>
        /// Previous-scan → now delta, for the BCC "since last scan" hint.
        /// Returns null when there is no earlier snapshot to compare against.
        /// </summary>
        public static string DescribeDelta(WarningSnapshot previous, WarningSnapshot current)
        {
            if (previous == null || current == null) return null;
            int d = current.Total - previous.Total;
            string arrow = d > 0 ? $"↑{d}" : d < 0 ? $"↓{Math.Abs(d)}" : "→0";
            return $"{arrow} since {previous.TsUtc.ToLocalTime():dd MMM HH:mm}";
        }
    }
}
