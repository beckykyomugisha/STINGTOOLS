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

        // ── E1: escalations closed in ACC while the clash still exists ──────────
        //
        // Kept beside the append-only entries, in the same file, but NOT append-only: a hold is
        // released once a complete clash pull no longer contains the clash, so a real recurrence
        // after that is escalated again. Files written before E1 have no such list; it loads empty.

        /// <summary>Escalations closed / voided in ACC whose clash was still present the last
        /// time anyone looked. ACC_PullClashes refuses to re-escalate these while they persist.</summary>
        [JsonProperty("closedInAcc")] public List<AccClosedInAccEntry> ClosedInAcc { get; set; } = new List<AccClosedInAccEntry>();

        /// <summary>The signatures currently held (closed in ACC, still clashing).</summary>
        public HashSet<string> HeldKeys(string origin)
            => new HashSet<string>((ClosedInAcc ?? new List<AccClosedInAccEntry>())
                .Where(e => e != null && string.Equals(e.Origin, origin, StringComparison.Ordinal) && !string.IsNullOrEmpty(e.Key))
                .Select(e => e.Key), StringComparer.Ordinal);

        /// <summary>Hold a signature. Idempotent on (origin, key); a re-hold refreshes the
        /// issue id and status. Returns true when the record changed.</summary>
        public bool Hold(string origin, string key, string issueId, string status, DateTime nowUtc)
        {
            origin = (origin ?? string.Empty).Trim();
            key = (key ?? string.Empty).Trim();
            if (origin.Length == 0 || key.Length == 0) return false;
            ClosedInAcc ??= new List<AccClosedInAccEntry>();
            var e = ClosedInAcc.FirstOrDefault(x => x != null && string.Equals(x.Origin, origin, StringComparison.Ordinal) &&
                                                    string.Equals(x.Key, key, StringComparison.Ordinal));
            if (e != null)
            {
                if (string.Equals(e.IssueId, issueId ?? string.Empty, StringComparison.Ordinal) &&
                    string.Equals(e.Status, status ?? string.Empty, StringComparison.Ordinal)) return false;
                e.IssueId = issueId ?? string.Empty;
                e.Status = status ?? string.Empty;
                return true;
            }
            ClosedInAcc.Add(new AccClosedInAccEntry
            {
                Origin = origin, Key = key, IssueId = issueId ?? string.Empty, Status = status ?? string.Empty, HeldSinceUtc = nowUtc,
            });
            return true;
        }

        /// <summary>Release every hold of <paramref name="origin"/> whose signature is NOT in
        /// <paramref name="presentInCompletePull"/>. The caller passes only a COMPLETE pull's
        /// active signatures - a partial or failed one proves nothing is gone. Returns the
        /// released signatures.</summary>
        public List<string> ReleaseAbsent(string origin, ISet<string> presentInCompletePull)
        {
            var released = new List<string>();
            if (presentInCompletePull == null || ClosedInAcc == null) return released;
            foreach (var e in ClosedInAcc.ToList())
            {
                if (e == null || !string.Equals(e.Origin, origin, StringComparison.Ordinal)) continue;
                if (presentInCompletePull.Contains(e.Key)) continue;
                ClosedInAcc.Remove(e);
                released.Add(e.Key);
            }
            return released;
        }
    }

    /// <summary>An escalation ACC closed or voided while its clash was still present (E1).</summary>
    public sealed class AccClosedInAccEntry
    {
        [JsonProperty("origin")] public string Origin { get; set; } = string.Empty;
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        [JsonProperty("issueId")] public string IssueId { get; set; } = string.Empty;
        /// <summary>The ACC status it had when held (closed / void / not_an_issue).</summary>
        [JsonProperty("status")] public string Status { get; set; } = string.Empty;
        [JsonProperty("heldSinceUtc")] public DateTime HeldSinceUtc { get; set; }
    }

    /// <summary>acc_clash_presence.json - per coordination model set, the ACTIVE clash
    /// signatures in the latest COMPLETE clash pull (E1). Only a complete pull is ever written
    /// here (not a failed, not-ready or truncated one), so "absent from it" means the clash is
    /// really gone - the one fact that may un-track a closed escalation.</summary>
    public sealed class AccClashPresence
    {
        public const string FileName = "acc_clash_presence.json";

        [JsonProperty("modelSets")]
        public Dictionary<string, AccClashPresenceSet> ModelSets { get; set; } =
            new Dictionary<string, AccClashPresenceSet>(StringComparer.Ordinal);

        /// <summary>Tri-state, as <see cref="AccPushedMap.Load"/>.</summary>
        public static AccClashPresence Load(string path, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path)) { error = "no path for the clash presence record"; return null; }
            try
            {
                if (!File.Exists(path)) return new AccClashPresence();
                string text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text)) { error = $"{FileName} is empty"; return null; }
                var r = JsonConvert.DeserializeObject<AccClashPresence>(text);
                if (r == null) { error = $"{FileName} is not a clash presence record"; return null; }
                var sets = new Dictionary<string, AccClashPresenceSet>(StringComparer.Ordinal);
                foreach (var kv in r.ModelSets ?? new Dictionary<string, AccClashPresenceSet>())
                    if (kv.Value != null) { kv.Value.Signatures ??= new List<string>(); sets[kv.Key] = kv.Value; }
                r.ModelSets = sets;
                return r;
            }
            catch (Exception ex) { error = $"{FileName} could not be read: {ex.Message}"; return null; }
        }

        /// <summary>
        /// F1: may this pull be recorded as complete evidence of which clashes exist? Only when
        /// it was not truncated AND every clash that could still be live has a signature. A
        /// clash ACC gave no document names for has no key, so a pull of them proves nothing
        /// about any held clash; recording it as an empty complete pull released every hold.
        /// <paramref name="liveWithoutSignature"/> counts clashes not positively excluded by
        /// status that lack a signature.
        /// </summary>
        public static bool IsCompleteEvidence(bool truncated, int liveWithoutSignature, out string reason)
        {
            reason = null;
            if (truncated)
            {
                reason = "Clash pull was truncated, so it was not recorded as complete: no 'closed in ACC' hold was released.";
                return false;
            }
            if (liveWithoutSignature > 0)
            {
                reason = $"{liveWithoutSignature} clash(es) had no document names, so this pull cannot show which clashes are gone: " +
                         "it was not recorded as complete and no 'closed in ACC' hold was released.";
                return false;
            }
            return true;
        }

        /// <summary>Replace one model set's snapshot with a complete pull's active signatures.</summary>
        public void Record(string modelSetId, string modelSetName, IEnumerable<string> activeSignatures, DateTime nowUtc)
        {
            if (string.IsNullOrEmpty(modelSetId)) return;
            ModelSets[modelSetId] = new AccClashPresenceSet
            {
                Name = modelSetName ?? string.Empty,
                PulledUtc = nowUtc,
                Signatures = (activeSignatures ?? Enumerable.Empty<string>())
                    .Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.Ordinal)
                    .OrderBy(s => s, StringComparer.Ordinal).ToList(),
            };
        }

        /// <summary>F5: a model set whose last complete pull is this much older than the newest
        /// one no longer counts. Otherwise a set pulled once (then switched away from, or deleted
        /// in ACC) kept every matching hold alive for ever.</summary>
        public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(30);

        /// <summary>The union of the current sets' signatures, or <c>null</c> when no complete
        /// pull has ever been recorded - "unknown", which must not read as "nothing present".
        /// F5: "current" is measured against the newest pull, not the clock, so the newest set
        /// always counts and a project that has not pulled for a while is not emptied.</summary>
        public HashSet<string> Present()
        {
            if (ModelSets == null || ModelSets.Count == 0) return null;
            var sets = ModelSets.Values.Where(s => s != null).ToList();
            if (sets.Count == 0) return null;
            DateTime newest = sets.Max(s => s.PulledUtc);
            var all = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in sets)
            {
                if (newest - s.PulledUtc > StaleAfter) continue;
                foreach (var sig in s.Signatures ?? new List<string>()) all.Add(sig);
            }
            return all;
        }

        public bool TrySave(string path, out string error)
            => AtomicFile.TryWrite(path, JsonConvert.SerializeObject(this, Formatting.Indented), out error);
    }

    public sealed class AccClashPresenceSet
    {
        [JsonProperty("name")] public string Name { get; set; } = string.Empty;
        [JsonProperty("pulledUtc")] public DateTime PulledUtc { get; set; }
        [JsonProperty("signatures")] public List<string> Signatures { get; set; } = new List<string>();
    }

    /// <summary>What ACC_SyncIssueStatus does with one tracked escalation (E1).</summary>
    public enum AccEscalationAction
    {
        /// <summary>Still open in ACC: stays tracked, so the pull keeps skipping it.</summary>
        Keep,
        /// <summary>Closed in ACC AND absent from the latest complete clash pull: resolved.
        /// Un-tracked, so a real recurrence later is escalated again.</summary>
        Untrack,
        /// <summary>Closed / voided in ACC but the clash is still present (or no complete pull
        /// is known). Un-tracked and HELD: the pull refuses to raise it again while it persists
        /// and reports it as "closed in ACC, still clashing".</summary>
        HoldClosedStillClashing,
        /// <summary>The ACC issue no longer exists (deleted). Un-tracked and reported.</summary>
        UntrackNotFound,
    }

    public sealed class AccEscalationDecision
    {
        public string Signature { get; set; } = string.Empty;
        public string IssueId { get; set; } = string.Empty;
        /// <summary>The ACC status, or NOT_FOUND.</summary>
        public string Status { get; set; } = string.Empty;
        public AccEscalationAction Action { get; set; }
        public bool RemovesFromTracking => Action != AccEscalationAction.Keep;
    }

    /// <summary>The ACC_SyncIssueStatus decision, without Revit or the network (E1).
    ///
    /// The old rule un-tracked every escalation ACC had closed and ACC_PullClashes checked only
    /// the tracking map - so a clash still active in Model Coordination whose issue somebody
    /// closed or voided was raised again on every cycle. Now an escalation leaves tracking for
    /// good only when the clash is absent from a COMPLETE pull; otherwise it is held.</summary>
    public static class AccEscalationReconcile
    {
        public const string NotFoundStatus = "NOT_FOUND";

        /// <param name="pushed">signature -> ACC issue id (the tracking set).</param>
        /// <param name="statusById">ACC issue id -> status, from a COMPLETE issue read.</param>
        /// <param name="isClosed">Is an ACC status terminal (closed / void / not_an_issue)?</param>
        /// <param name="presentInLatestCompletePull">Active clash signatures in the latest
        /// complete pull, or null when none is known.</param>
        public static List<AccEscalationDecision> Decide(
            IReadOnlyDictionary<string, string> pushed,
            IReadOnlyDictionary<string, string> statusById,
            Func<string, bool> isClosed,
            ISet<string> presentInLatestCompletePull)
        {
            var result = new List<AccEscalationDecision>();
            if (pushed == null) return result;
            statusById ??= new Dictionary<string, string>();
            foreach (var kv in pushed.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var d = new AccEscalationDecision { Signature = kv.Key ?? string.Empty, IssueId = kv.Value ?? string.Empty };
                if (string.IsNullOrEmpty(d.IssueId) || !statusById.TryGetValue(d.IssueId, out string st))
                {
                    d.Status = NotFoundStatus;
                    d.Action = AccEscalationAction.UntrackNotFound;
                }
                else
                {
                    d.Status = st ?? string.Empty;
                    if (isClosed != null && isClosed(st))
                        d.Action = presentInLatestCompletePull != null && !presentInLatestCompletePull.Contains(d.Signature)
                            ? AccEscalationAction.Untrack
                            : AccEscalationAction.HoldClosedStillClashing;
                    else d.Action = AccEscalationAction.Keep;
                }
                result.Add(d);
            }
            return result;
        }
    }

    /// <summary>Write-temp-then-replace, reporting failure instead of throwing.</summary>
    internal static class AtomicFile
    {
        /// <summary>F10: a temp name unique to this write. A fixed "path.tmp" let two sessions on
        /// the shared project folder overwrite or delete each other's half-written file.</summary>
        public static string TempFor(string path) => path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        /// <summary>Remove a temp file a failed write left behind (best effort; the failure itself
        /// is already being reported by the caller).</summary>
        public static void Discard(string tmp)
        {
            try { if (!string.IsNullOrEmpty(tmp) && File.Exists(tmp)) File.Delete(tmp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static bool TryWrite(string path, string text, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path)) { error = "no path"; return false; }
            string tmp = TempFor(path);
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
