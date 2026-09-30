// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccEscalationRecord.cs
//
// The files that remember which ACC issues STING itself raised.
//
//   pushed_clashes.json / pushed_lifecycle_gaps.json   the TRACKING set: signature -> ACC
//       issue id for escalations that are still live. ACC_SyncIssueStatus removes an entry
//       once ACC closes the issue, so the same clash is raised again if it recurs.
//
//   acc_issue_origins.json                              the ORIGIN record (A15): every
//       (origin, signature) -> ACC issue id STING ever created. Append-only; nothing prunes
//       it. ACC_ImportIssues reads it to recognise STING-raised issues, which it used to do
//       from the tracking set - so the moment Sync untracked a closed escalation, Import
//       stopped knowing STING had raised it.
//
// THE RULE BOTH FILES FOLLOW (A6), copied from AccUploadLedger: a missing file is an empty
// record; an UNREADABLE one is NOT. Reading a corrupt pushed_clashes.json as empty made every
// clash look "never escalated", so the next run re-raised the lot, assigned to real people,
// and then overwrote the file - destroying the record that would have shown what happened.
// Load returns null with the reason instead, and the callers refuse to act.
//
// Saves are atomic (write a temp file, then replace) and REPORT failure: an issue that exists
// in ACC but is not recorded here is raised again next run, so a failed save must be said out
// loud, never swallowed.
//
// Revit-free and log-free: linked into StingTools.Acc.Tests.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StingTools.V6
{
    /// <summary>A signature -> ACC issue id map (pushed_clashes.json and siblings).</summary>
    public static class AccPushedMap
    {
        /// <summary>The clash escalation tracking set (ACC_PullClashes / ACC_SyncIssueStatus).</summary>
        public const string ClashFileName = "pushed_clashes.json";
        /// <summary>The lifecycle-gap tracking set (KUT_PushLifecycleGapsToAcc).</summary>
        public const string LifecycleGapFileName = "pushed_lifecycle_gaps.json";

        /// <summary>Tri-state read. Absent file: an empty map. Readable: the map. Unreadable,
        /// not JSON, or not an object of strings: <c>null</c>, with <paramref name="error"/>
        /// naming why - never an empty map standing in for "could not read".</summary>
        public static Dictionary<string, string> Load(string path, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path)) { error = "no path for the escalation record"; return null; }
            string text;
            try
            {
                if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.Ordinal);
                text = File.ReadAllText(path);
            }
            catch (Exception ex) { error = $"{Path.GetFileName(path)} could not be read: {ex.Message}"; return null; }

            // A zero-byte file is what an interrupted non-atomic write leaves. It is not "no
            // escalations"; treat it as unreadable so nothing is re-raised on its account.
            if (string.IsNullOrWhiteSpace(text)) { error = $"{Path.GetFileName(path)} is empty"; return null; }

            JObject o;
            try { o = JObject.Parse(text); }
            catch (Exception ex) { error = $"{Path.GetFileName(path)} is not valid JSON: {ex.Message}"; return null; }

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in o.Properties())
            {
                if (p.Value.Type != JTokenType.String)
                {
                    error = $"{Path.GetFileName(path)}: entry '{p.Name}' is {p.Value.Type}, not an ACC issue id";
                    return null;
                }
                map[p.Name] = (string)p.Value ?? string.Empty;
            }
            return map;
        }

        /// <summary>Atomic write. False, with the reason, when it did not happen.</summary>
        public static bool TrySave(string path, IReadOnlyDictionary<string, string> map, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path)) { error = "no path for the escalation record"; return false; }
            var o = new JObject();
            foreach (var kv in (map ?? new Dictionary<string, string>()).OrderBy(k => k.Key, StringComparer.Ordinal))
                o[kv.Key] = kv.Value ?? string.Empty;
            return AtomicFile.TryWrite(path, o.ToString(Formatting.Indented), out error);
        }
    }

    /// <summary>One ACC issue STING created.</summary>
    public sealed class AccIssueOriginEntry
    {
        /// <summary>What raised it: <see cref="AccIssueImport.ClashEscalationOrigin"/> or
        /// <see cref="AccIssueImport.LifecycleGapOrigin"/>.</summary>
        [JsonProperty("origin")] public string Origin { get; set; } = string.Empty;
        /// <summary>The raiser's own key: the clash signature, or TYPE:elementId for a gap.</summary>
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        [JsonProperty("issueId")] public string IssueId { get; set; } = string.Empty;
        [JsonProperty("recordedUtc")] public DateTime RecordedUtc { get; set; }
    }

    /// <summary>acc_issue_origins.json - the append-only record of every ACC issue STING raised
    /// (A15). Unlike the tracking set, nothing ever removes an entry from it.</summary>
    public sealed class AccIssueOrigins
    {
        public const string FileName = "acc_issue_origins.json";

        [JsonProperty("entries")] public List<AccIssueOriginEntry> Entries { get; set; } = new List<AccIssueOriginEntry>();

        /// <summary>Tri-state, as <see cref="AccPushedMap.Load"/>.</summary>
        public static AccIssueOrigins Load(string path, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path)) { error = "no path for the ACC issue origin record"; return null; }
            try
            {
                if (!File.Exists(path)) return new AccIssueOrigins();
                string text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text)) { error = $"{FileName} is empty"; return null; }
                var r = JsonConvert.DeserializeObject<AccIssueOrigins>(text);
                if (r == null) { error = $"{FileName} is not an origin record"; return null; }
                r.Entries = (r.Entries ?? new List<AccIssueOriginEntry>()).Where(e => e != null).ToList();
                return r;
            }
            catch (Exception ex) { error = $"{FileName} could not be read: {ex.Message}"; return null; }
        }

        /// <summary>Add one issue. Idempotent on (origin, key, issueId). Returns true when the
        /// record changed.</summary>
        public bool Record(string origin, string key, string issueId, DateTime nowUtc)
        {
            origin = (origin ?? string.Empty).Trim();
            key = (key ?? string.Empty).Trim();
            issueId = (issueId ?? string.Empty).Trim();
            if (origin.Length == 0 || key.Length == 0 || issueId.Length == 0) return false;
            if (Entries.Any(e => string.Equals(e.Origin, origin, StringComparison.Ordinal) &&
                                 string.Equals(e.Key, key, StringComparison.Ordinal) &&
                                 string.Equals(e.IssueId, issueId, StringComparison.Ordinal)))
                return false;
            Entries.Add(new AccIssueOriginEntry { Origin = origin, Key = key, IssueId = issueId, RecordedUtc = nowUtc });
            return true;
        }

        /// <summary>Backfill from a tracking map (backward compatibility: projects whose
        /// escalations predate this file). Returns how many were added.</summary>
        public int Absorb(string origin, IEnumerable<KeyValuePair<string, string>> keyToIssueId, DateTime nowUtc)
        {
            int n = 0;
            foreach (var kv in keyToIssueId ?? Enumerable.Empty<KeyValuePair<string, string>>())
                if (Record(origin, kv.Key, kv.Value, nowUtc)) n++;
            return n;
        }

        /// <summary>Add every recorded issue to an ACC id -> origin link map. The first entry
        /// for an ACC id wins (earliest recorded), so the result is not order-of-file dependent.</summary>
        public void AddTo(IDictionary<string, AccOriginLink> into)
        {
            if (into == null) return;
            foreach (var e in Entries.OrderBy(e => e.RecordedUtc).ThenBy(e => e.Key, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(e.IssueId) || into.ContainsKey(e.IssueId)) continue;
                into[e.IssueId] = new AccOriginLink { Origin = e.Origin, Key = e.Key };
            }
        }

        public bool TrySave(string path, out string error)
            => AtomicFile.TryWrite(path, JsonConvert.SerializeObject(this, Formatting.Indented), out error);
    }

    /// <summary>Write-temp-then-replace, reporting failure instead of throwing.</summary>
    internal static class AtomicFile
    {
        public static bool TryWrite(string path, string text, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path)) { error = "no path"; return false; }
            string tmp = path + ".tmp";
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(tmp, text ?? string.Empty);
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
                return true;
            }
            catch (Exception ex)
            {
                error = $"{Path.GetFileName(path)} could not be written: {ex.Message}";
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { /* the write already failed and is reported */ }
                return false;
            }
        }
    }

    /// <summary>What one escalation run did, in counts a report and a Result can be built from.
    /// Shared by the clash escalation and the lifecycle-gap push so the two cannot describe the
    /// same outcome differently.</summary>
    public sealed class AccIssueCreateOutcome
    {
        public int Created, Skipped, Failed;
        /// <summary>Issues that EXIST in ACC but could not be written to the record. The next
        /// run will raise them again - the loudest line in any report.</summary>
        public int NotRecorded;
        /// <summary>Stopped early because Autodesk refused the sign-in; the rest were not tried.</summary>
        public bool StoppedOnAuth;
        public int NotAttempted;
        public List<string> Failures { get; } = new List<string>();
        public List<string> RecordFailures { get; } = new List<string>();

        /// <summary>A run that failed to create or to record anything is not a clean run.</summary>
        public bool HasProblems => Failed > 0 || NotRecorded > 0 || StoppedOnAuth;

        public string Describe(string noun = "issue")
        {
            var parts = new List<string> { $"{Created} {noun}(s) created in ACC", $"{Skipped} already tracked" };
            if (Failed > 0)
                parts.Add($"{Failed} FAILED (not created, the next run retries them): " +
                          string.Join(" | ", Failures.Take(3)) + (Failures.Count > 3 ? " …" : ""));
            if (StoppedOnAuth)
                parts.Add($"STOPPED - Autodesk refused the sign-in, {NotAttempted} not attempted");
            if (NotRecorded > 0)
                parts.Add($"{NotRecorded} created in ACC but NOT RECORDED - they exist in ACC and the next run " +
                          "will raise them AGAIN unless the record is fixed: " + string.Join(" | ", RecordFailures.Take(2)));
            return string.Join("; ", parts) + ".";
        }
    }

    /// <summary>The create-and-record loop both escalations run, without Revit.
    ///
    /// Per item: skip it when already tracked; create it; on success record it in the
    /// tracking map AND the origin record and SAVE BOTH immediately (a crash half-way used to
    /// lose every id created so far); on an auth failure stop - every remaining create would
    /// fail the same way.</summary>
    public static class AccIssueCreateLoop
    {
        /// <param name="saveTracked">Persist the tracking map; returns an error or null.</param>
        /// <param name="saveOrigins">Persist the origin record; returns an error or null.</param>
        public static AccIssueCreateOutcome Run<T>(
            IReadOnlyList<T> items,
            Func<T, string> signatureOf,
            Func<T, string> labelOf,
            Func<T, AccPushResult> create,
            Dictionary<string, string> tracked,
            Func<Dictionary<string, string>, string> saveTracked,
            AccIssueOrigins origins,
            string origin,
            Func<AccIssueOrigins, string> saveOrigins,
            DateTime nowUtc,
            Action<T, string> afterCreate = null)
        {
            var outcome = new AccIssueCreateOutcome();
            items ??= Array.Empty<T>();
            tracked ??= new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                string sig = signatureOf?.Invoke(item) ?? string.Empty;
                string label = labelOf?.Invoke(item) ?? sig;
                if (string.IsNullOrEmpty(sig)) continue;
                if (tracked.ContainsKey(sig)) { outcome.Skipped++; continue; }

                AccPushResult r;
                try { r = create(item) ?? new AccPushResult { Status = AccFetchStatus.TransportFailed, Detail = "no result" }; }
                catch (Exception ex) { r = new AccPushResult { Status = AccFetchStatus.TransportFailed, Detail = ex.Message }; }

                if (r.Ok)
                {
                    outcome.Created++;
                    tracked[sig] = r.Id;
                    string e1 = saveTracked?.Invoke(tracked);
                    string e2 = null;
                    if (origins != null)
                    {
                        origins.Record(origin, sig, r.Id, nowUtc);
                        e2 = saveOrigins?.Invoke(origins);
                    }
                    if (!string.IsNullOrEmpty(e1) || !string.IsNullOrEmpty(e2))
                    {
                        outcome.NotRecorded++;
                        outcome.RecordFailures.Add($"{label} (ACC issue {r.Id}): " +
                                                   string.Join("; ", new[] { e1, e2 }.Where(x => !string.IsNullOrEmpty(x))));
                    }
                    afterCreate?.Invoke(item, r.Id);
                    continue;
                }

                outcome.Failed++;
                outcome.Failures.Add($"{label}: {r.Detail}");
                if (r.Status == AccFetchStatus.AuthFailed)
                {
                    outcome.StoppedOnAuth = true;
                    for (int j = i + 1; j < items.Count; j++)
                    {
                        string s2 = signatureOf?.Invoke(items[j]) ?? string.Empty;
                        if (!string.IsNullOrEmpty(s2) && !tracked.ContainsKey(s2)) outcome.NotAttempted++;
                    }
                    break;
                }
            }
            return outcome;
        }
    }
}
