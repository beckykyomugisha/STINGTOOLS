// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccIssueImportState.cs — the incremental-import watermark for ACC_ImportIssues.
//
// One small JSON file per project, next to the register's other ACC state
// (_BIM_COORD/acc/acc_issue_import_state.json - the caller resolves the path through
// StingPaths). It records when the last SUCCESSFUL import started, so the next one can ask
// ACC only for issues updated since then (filter[updatedAt]).
//
// RULES
//  * The watermark is the time the successful PULL STARTED, not when it finished: an issue
//    edited while the pull was paging must fall inside the next window.
//  * The next window opens <see cref="Overlap"/> before the watermark, so clock skew between
//    this workstation and ACC cannot open a gap. Re-reading an unchanged issue is harmless
//    (the merge is idempotent); missing a changed one is not.
//  * A failed read, a refused merge or a failed register write does NOT advance it.
//  * The watermark is keyed to the ACC container. Pointing the project at another container
//    forces a full read rather than an incremental one against the wrong history.
//  * An incremental read cannot see deletions, so a full read is forced every
//    <see cref="FullEvery"/> (and on request, and when there is no usable state).
//
// Revit-free and log-free: linked into StingTools.Acc.Tests.

using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;

namespace StingTools.V6
{
    public sealed class AccIssueImportState
    {
        public const string FileName = "acc_issue_import_state.json";

        /// <summary>How far before the watermark the next window opens.</summary>
        public static readonly TimeSpan Overlap = TimeSpan.FromMinutes(5);

        /// <summary>A full read is forced when the last one is older than this.</summary>
        public static readonly TimeSpan FullEvery = TimeSpan.FromDays(7);

        public string ProjectId { get; set; } = string.Empty;
        /// <summary>UTC start of the last successful pull (full or incremental).</summary>
        public DateTime? LastSuccessUtc { get; set; }
        /// <summary>UTC start of the last successful FULL pull.</summary>
        public DateTime? LastFullUtc { get; set; }

        /// <summary>Read the state. Absent = empty state (first run is a full read). An
        /// unreadable file is an empty state plus a warning, never an exception - the worst
        /// outcome is one unnecessary full read.</summary>
        public static AccIssueImportState Load(string path, out string warning)
        {
            warning = string.Empty;
            var s = new AccIssueImportState();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return s;
            try
            {
                var o = JObject.Parse(File.ReadAllText(path));
                s.ProjectId = (string)o["projectId"] ?? string.Empty;
                s.LastSuccessUtc = ReadUtc(o["lastSuccessUtc"]);
                s.LastFullUtc = ReadUtc(o["lastFullUtc"]);
            }
            catch (Exception ex)
            {
                warning = $"the import state '{path}' could not be read ({ex.Message}) - doing a full read";
                return new AccIssueImportState();
            }
            return s;
        }

        /// <summary>Write atomically (temp file + replace). False with a reason on failure.</summary>
        public bool Save(string path, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrEmpty(path)) { error = "no path (the model has never been saved)"; return false; }
            try
            {
                var o = new JObject
                {
                    ["projectId"] = ProjectId ?? string.Empty,
                    ["lastSuccessUtc"] = Iso(LastSuccessUtc),
                    ["lastFullUtc"] = Iso(LastFullUtc),
                };
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, o.ToString());
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        /// <summary>The updatedAt lower bound for the next pull, or null for a full read.
        /// <paramref name="reason"/> says which and why, for the report.</summary>
        public DateTime? SinceFor(string projectId, bool forceFull, DateTime nowUtc, out string reason)
        {
            string pid = AccIds.ForAcc(projectId);
            if (forceFull) { reason = "full read (requested)"; return null; }
            if (!LastSuccessUtc.HasValue) { reason = "full read (no previous import recorded)"; return null; }
            if (!string.Equals(AccIds.ForAcc(ProjectId), pid, StringComparison.OrdinalIgnoreCase))
            { reason = "full read (the ACC project changed since the last import)"; return null; }
            if (!LastFullUtc.HasValue || nowUtc - LastFullUtc.Value > FullEvery)
            { reason = $"full read (none in the last {FullEvery.TotalDays:F0} days, so deletions are re-checked)"; return null; }
            if (LastSuccessUtc.Value > nowUtc.AddMinutes(1))
            { reason = "full read (the recorded import time is in the future - clock changed)"; return null; }
            var since = LastSuccessUtc.Value - Overlap;
            reason = $"incremental: issues updated since {since:yyyy-MM-dd HH:mm} UTC";
            return since;
        }

        /// <summary>Advance after a pull, merge AND register write all succeeded.</summary>
        public void RecordSuccess(string projectId, DateTime pullStartedUtc, bool wasFull)
        {
            ProjectId = AccIds.ForAcc(projectId);
            LastSuccessUtc = pullStartedUtc;
            if (wasFull) LastFullUtc = pullStartedUtc;
        }

        private static DateTime? ReadUtc(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return null;
            if (t.Type == JTokenType.Date) return ((DateTime)t).ToUniversalTime();
            return DateTime.TryParse((string)t, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : (DateTime?)null;
        }

        private static JToken Iso(DateTime? d)
            => d.HasValue ? (JToken)DateTime.SpecifyKind(d.Value, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture) : JValue.CreateNull();
    }
}
