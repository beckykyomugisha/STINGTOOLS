// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccSelfCheck.cs
//
// ONE CLICK THAT SAYS WHETHER ACC WILL WORK FOR THIS PROJECT, AND WHY NOT.
//
// The KUT go-live verification (docs/KUT_ACC_DAY1_PLAYBOOK.md section 7) is a sequence of
// live checks, each of which fails in a different place and reads differently. Run by hand,
// every one costs a command, a dialog and an interpretation. This runs the READ half of all
// of them in order and returns one row per check: PASS / WARN / FAIL / SKIPPED, the exact
// reason, and the remedy.
//
// READ-ONLY AGAINST ACC, BY CONSTRUCTION. Every request below is a GET. It never creates an
// issue, never creates storage, never uploads, never creates a custom-attribute definition
// (EnsureDefinitionsAsync is called with allowCreate: false), and never writes the project
// settings file. The issue type is resolved with the same chooser the push uses
// (IssueTypeChooser) over a list this file fetches itself - NOT through
// AccIssueSync.ResolveIssueTypeAsync, which rewrites the credentials file (it does not persist
// the choice for a project-scoped run - see its comment - but it is still a write).
// The one local write it can cause is the token refresh's own save of a ROTATED refresh
// token: APS invalidates the old one on use, so not saving it would sign the machine out.
//
// NEVER A PASS FOR SOMETHING NOT CHECKED. A check that could not run because an earlier one
// failed is SKIPPED, naming the check it depends on. Write permissions (data:create etc.)
// cannot be proved without writing, so that row is always SKIPPED with that reason - and a
// 403 seen on a read is reported as the scope/permission it implies.
//
// Revit-free (StingLog only, which the tests shim), so StingTools.Acc.Tests links it and
// drives it over a loopback listener.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.V6
{
    public enum AccCheckStatus { Pass, Warn, Fail, Skipped }

    /// <summary>One row of the self-check.</summary>
    public sealed class AccCheckResult
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public AccCheckStatus Status { get; set; }
        public string Detail { get; set; } = string.Empty;
        /// <summary>What to do. Empty only for a PASS.</summary>
        public string Remedy { get; set; } = string.Empty;

        public override string ToString() => $"{Id} {Status.ToString().ToUpperInvariant()} {Title}: {Detail}";
    }

    public static class AccSelfCheck
    {
        /// <summary>How many visible projects a "not visible" failure names.</summary>
        internal const int MaxProjectsNamed = 15;

        private static string DataBase => AccIssueSync.Host + "/data/v1";
        private static string ProjectBase => AccIssueSync.Host + "/project/v1";
        private static string IssuesProjectUrl(AccCredentials c) =>
            $"{AccIssueSync.Host}/construction/issues/v1/projects/{AccIds.ForAcc(c.ProjectId)}";

        /// <summary>Run every check in order. Never throws: an unexpected exception becomes a
        /// FAIL row for the step it happened in, and every later step is SKIPPED.</summary>
        /// <param name="creds">Machine credentials with the project scope already applied
        /// (AccProjectScope.Apply).</param>
        /// <param name="policy">The project's settings (AccOperatingPolicy.Load).</param>
        public static async Task<List<AccCheckResult>> RunAsync(AccCredentials creds, AccOperatingPolicy policy, DateTime utcNow)
        {
            var run = new Run(creds ?? new AccCredentials(), policy ?? AccOperatingPolicy.Interactive(), utcNow);
            await run.ExecuteAsync().ConfigureAwait(false);
            return run.Results;
        }

        public static bool AnyFail(IEnumerable<AccCheckResult> results)
            => results != null && results.Any(r => r.Status == AccCheckStatus.Fail);

        /// <summary>"12 PASS · 2 WARN · 1 FAIL · 3 SKIPPED".</summary>
        public static string Summary(IReadOnlyCollection<AccCheckResult> results)
        {
            results = results ?? Array.Empty<AccCheckResult>();
            int n(AccCheckStatus s) => results.Count(r => r.Status == s);
            string verdict = n(AccCheckStatus.Fail) > 0 ? "NOT READY"
                : n(AccCheckStatus.Warn) > 0 ? "READY WITH WARNINGS" : "READY";
            return $"{verdict} — {n(AccCheckStatus.Pass)} PASS · {n(AccCheckStatus.Warn)} WARN · " +
                   $"{n(AccCheckStatus.Fail)} FAIL · {n(AccCheckStatus.Skipped)} SKIPPED";
        }

        /// <summary>The plain-text report (also what the command writes to disk).</summary>
        public static string Format(IReadOnlyCollection<AccCheckResult> results, string header = null)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(header)) sb.AppendLine(header).AppendLine();
            sb.AppendLine(Summary(results)).AppendLine();
            foreach (var r in results ?? Array.Empty<AccCheckResult>())
            {
                sb.AppendLine($"[{r.Status.ToString().ToUpperInvariant(),-7}] {r.Id}  {r.Title}");
                if (!string.IsNullOrEmpty(r.Detail)) sb.AppendLine("          " + r.Detail);
                if (!string.IsNullOrEmpty(r.Remedy)) sb.AppendLine("          → " + r.Remedy);
            }
            sb.AppendLine();
            sb.AppendLine("Read-only: nothing was created, uploaded or changed in ACC.");
            return sb.ToString();
        }

        // ── the run ──────────────────────────────────────────────────────────

        private sealed class Run
        {
            private readonly AccCredentials _c;
            private readonly AccOperatingPolicy _p;
            private readonly DateTime _now;
            public readonly List<AccCheckResult> Results = new List<AccCheckResult>();
            /// <summary>Every 403 a read got: (what, detail). Feeds the scope inference.</summary>
            private readonly List<string> _forbidden = new List<string>();
            private int _readsOk;

            public Run(AccCredentials c, AccOperatingPolicy p, DateTime now)
            { _c = c; _p = p; _now = now; }

            private AccCheckResult Add(string id, string title, AccCheckStatus st, string detail, string remedy = "")
            {
                var r = new AccCheckResult { Id = id, Title = title, Status = st, Detail = detail ?? "", Remedy = st == AccCheckStatus.Pass ? "" : remedy ?? "" };
                Results.Add(r);
                return r;
            }

            private void Skip(string id, string title, string because)
                => Add(id, title, AccCheckStatus.Skipped, "not checked: " + because,
                       "Fix the failure it depends on, then run the self-check again.");

            private void Note(AccFetchStatus st, int http, string what)
            {
                if (st == AccFetchStatus.AuthFailed && http == 403) _forbidden.Add(what);
            }

            private static string Remedy(AccFetchStatus st, int http, string extra403 = "")
                => st == AccFetchStatus.AuthFailed && http == 403 && extra403.Length > 0
                    ? extra403
                    : AccCommandOutcome.Remedy(st == AccFetchStatus.Ok || st == AccFetchStatus.EmptyOk ? AccFetchStatus.TransportFailed : st);

            public async Task ExecuteAsync()
            {
                // 1 ─ configuration (local, never skipped)
                bool haveClient = CheckConfiguration(out bool haveProject);

                // 2 ─ sign-in
                bool signedIn = false;
                if (!haveClient)
                {
                    Skip("2.1", "Autodesk sign-in", "no APS Client ID is configured (1.1)");
                    Skip("2.2", "Sign-in lifetime", "no APS Client ID is configured (1.1)");
                }
                else signedIn = await Guard("2.1", "Autodesk sign-in", CheckSignInAsync).ConfigureAwait(false);

                string why = !signedIn ? "the Autodesk sign-in failed (2.1)"
                           : !haveProject ? "no ACC project is configured (1.3)" : null;

                // 3 ─ project
                AccProject match = null;
                if (why != null)
                {
                    Skip("3.1", "Configured project visible to this sign-in", why);
                    Skip("3.2", "Hub and region", why);
                }
                else
                {
                    bool ok = await Guard("3.1", "Configured project visible to this sign-in", async () =>
                    {
                        match = await CheckProjectAsync().ConfigureAwait(false);
                        return match != null;
                    }).ConfigureAwait(false);
                    EnsureRow("3.2", "Hub and region", "the project check did not complete (3.1)");
                    if (!ok) why = "the configured project is not visible to this sign-in (3.1)";
                }

                // 4 ─ issues
                if (why != null)
                {
                    Skip("4.1", "Issue type STING files under", why);
                    Skip("4.2", "Issue list readable", why);
                    Skip("4.3", "Escalation assignee resolves", why);
                }
                else
                {
                    await Guard("4.1", "Issue type STING files under", CheckIssueTypeAsync).ConfigureAwait(false);
                    await Guard("4.2", "Issue list readable", CheckIssueReadAsync).ConfigureAwait(false);
                    await Guard("4.3", "Escalation assignee resolves", CheckAssigneeAsync).ConfigureAwait(false);
                }

                // 5 ─ model coordination
                if (why != null)
                {
                    Skip("5.1", "Model Coordination model sets", why);
                    Skip("5.2", "Remembered model set", why);
                    Skip("5.3", "Latest clash test", why);
                }
                else
                {
                    await Guard("5.1", "Model Coordination model sets", CheckModelCoordinationAsync).ConfigureAwait(false);
                    EnsureRow("5.2", "Remembered model set", "the model-set check did not complete (5.1)");
                    EnsureRow("5.3", "Latest clash test", "the model-set check did not complete (5.1)");
                }

                // 6 ─ docs
                if (why != null)
                {
                    Skip("6.1", "Upload folder(s)", why);
                    Skip("6.2", "ISO 19650 custom attributes", why);
                }
                else
                {
                    await Guard("6.1", "Upload folder(s)", CheckDocsAsync).ConfigureAwait(false);
                    EnsureRow("6.2", "ISO 19650 custom attributes", "no upload folder was reachable (6.1)");
                }

                // 7 ─ scopes
                CheckScopes(signedIn && why == null);
            }

            /// <summary>A row a step promised but did not produce (it failed early or threw)
            /// becomes SKIPPED, so the report never silently omits a check.</summary>
            private void EnsureRow(string id, string title, string because)
            {
                if (!Results.Any(r => r.Id == id || r.Id.StartsWith(id + ".", StringComparison.Ordinal))) Skip(id, title, because);
            }

            /// <summary>Run one step; an exception is that step's FAIL, never a crash of the run.</summary>
            private async Task<bool> Guard(string id, string title, Func<Task<bool>> step)
            {
                try { return await step().ConfigureAwait(false); }
                catch (Exception ex)
                {
                    StingLog.Warn($"ACC self-check {id}: {ex}");
                    Add(id, title, AccCheckStatus.Fail, "the check itself threw: " + ex.Message,
                        "Report this with StingTools_yyyyMMdd.log (next to the loaded DLL).");
                    return false;
                }
            }

            // ── 1 ──
            private bool CheckConfiguration(out bool haveProject)
            {
                bool haveClient = !string.IsNullOrWhiteSpace(_c.ClientId);
                if (haveClient)
                    Add("1.1", "APS app (Client ID)", AccCheckStatus.Pass,
                        _c.IsPublicClient
                            ? "Client ID set; public (PKCE, \"Desktop app\") client — no secret is stored on this machine."
                            : "Client ID set; confidential (\"Traditional Web App\") client — a client secret is stored (DPAPI-protected).");
                else
                    Add("1.1", "APS app (Client ID)", AccCheckStatus.Fail, "no APS Client ID is configured on this machine.",
                        "Enter the Client ID on the ACC card (BIM Coordination Center > Platforms > ACC) and sign in.");

                switch (_p.Source)
                {
                    case AccPolicySource.Loaded:
                        Add("1.2", "Project ACC settings file", AccCheckStatus.Pass,
                            $"loaded {_p.SettingsPath}; {(_p.MayPrompt ? "interactive" : "UNATTENDED")} mode.");
                        break;
                    case AccPolicySource.Absent:
                        Add("1.2", "Project ACC settings file", AccCheckStatus.Warn,
                            string.IsNullOrEmpty(_p.SettingsPath)
                                ? "the model has not been saved, so it has no project folder and no ACC settings file."
                                : $"no settings file at {_p.SettingsPath} — every ACC command will prompt, and project values come from the machine file.",
                            "Use Discover on the ACC card and Save, so this project carries its own ACC project, hub and folders.");
                        break;
                    case AccPolicySource.Malformed:
                        Add("1.2", "Project ACC settings file", AccCheckStatus.Fail,
                            $"{_p.SettingsPath} could NOT be read: {_p.LoadError}. EVERY setting in it was discarded.",
                            "Fix the file (the reason names the key) or delete it and re-save from the ACC card.");
                        break;
                    default:
                        Add("1.2", "Project ACC settings file", AccCheckStatus.Fail, "unknown settings state " + _p.Source,
                            "Report this.");
                        break;
                }

                // The project's ACC values come ONLY from its settings file (the machine-file
                // fallback is retired). An old id the machine file still holds is reported, never used.
                haveProject = _c.ProjectScope == AccProjectScopeSource.ProjectSettings
                              && !string.IsNullOrWhiteSpace(_c.ProjectId);
                if (haveProject)
                    Add("1.3", "ACC project for this model", AccCheckStatus.Pass,
                        AccProjectScope.Describe(_c) + (_c.CoordContainer != _c.ProjectId ? $"; coordination container {_c.CoordContainer}" : "") + ".");
                else
                    Add("1.3", "ACC project for this model", AccCheckStatus.Fail,
                        "this project has no ACC project configured" +
                        (_p.Source == AccPolicySource.Malformed ? " (its settings file was discarded — see 1.2)" : "") + ".",
                        "BIM Coordination Center > ACC > Find my ACC project, pick the KUT project, Save.");

                if (!string.IsNullOrWhiteSpace(_c.LegacyProjectId))
                    Add("1.4", "Old machine-wide ACC ids", AccCheckStatus.Warn,
                        $"old machine ids present: this machine remembers ACC project {_c.LegacyProjectId} from before " +
                        "settings were per project. It is NOT used.",
                        "Adopt it via the ACC card Save (the old values are shown there), or use Find my ACC project.");
                return haveClient;
            }

            // ── 2 ──
            private async Task<bool> CheckSignInAsync()
            {
                // force: a cached access token would PASS without Autodesk ever being asked. A
                // forced refresh proves what actually lapses - the refresh token, and the app's
                // Client ID / secret / type - exactly as the keep-alive does it.
                var auth = await AccIssueSync.EnsureAuthDetailedAsync(_c, force: true).ConfigureAwait(false);
                if (!auth.Ok)
                {
                    bool unreachable = auth.Status == AccFetchStatus.TransportFailed;
                    Add("2.1", "Autodesk sign-in", AccCheckStatus.Fail,
                        (unreachable ? "could not reach Autodesk: " : "Autodesk refused the sign-in: ") + auth.Detail,
                        unreachable
                            ? "Check this machine's network access to developer.api.autodesk.com and run again — the sign-in itself may be fine."
                            : "Sign in with Autodesk again on the ACC card. If it is refused again, check the Client ID/secret and the app type (see 1.1).");
                    Skip("2.2", "Sign-in lifetime", "the Autodesk sign-in failed (2.1)");
                    return false;
                }
                if (!string.IsNullOrEmpty(auth.Warning))
                    Add("2.1", "Autodesk sign-in", AccCheckStatus.Warn, "token usable now, but: " + auth.Warning,
                        "Check that %APPDATA%\\Planscape is writable, then sign in again.");
                else
                    Add("2.1", "Autodesk sign-in", AccCheckStatus.Pass,
                        $"Autodesk accepted the refresh; new access token valid until {_c.AccessTokenExpiry:yyyy-MM-dd HH:mm} UTC.");

                string life = AccSignInLifetime.Describe(_c, _now);
                double? days = AccSignInLifetime.DaysRemaining(_c, _now);
                if (days == null)
                    Add("2.2", "Sign-in lifetime", AccCheckStatus.Warn, life + " The remaining days could not be computed.",
                        "Sign in again once so the issue time is recorded.");
                else if (days < 3)
                    Add("2.2", "Sign-in lifetime", AccCheckStatus.Warn, life, "Sign in again on the ACC card before the next cycle.");
                else
                    Add("2.2", "Sign-in lifetime", AccCheckStatus.Pass, life);
                return true;
            }

            // ── 3 ──
            private async Task<AccProject> CheckProjectAsync()
            {
                var all = await AccProjectDiscovery.ListAllProjectsAsync(_c).ConfigureAwait(false);
                if (!all.Succeeded)
                {
                    Note(all.Status, all.HttpStatus, "listing hubs/projects (account:read / data:read)");
                    Add("3.1", "Configured project visible to this sign-in", AccCheckStatus.Fail,
                        $"could not list the ACC projects this sign-in can see ({all.Status}): {all.Detail}",
                        Remedy(all.Status, all.HttpStatus,
                            "The app is not a Custom Integration on the hub (ACC Account Admin > Custom Integrations), or the account lacks access — see playbook §2."));
                    Skip("3.2", "Hub and region", "the project list could not be read (3.1)");
                    return null;
                }
                _readsOk++;

                string want = AccIds.ForAcc(_c.ProjectId);
                var hit = all.Value.FirstOrDefault(p => string.Equals(AccIds.ForAcc(p.Id), want, StringComparison.OrdinalIgnoreCase));
                if (hit == null)
                {
                    string visible = all.Value.Count == 0
                        ? "this sign-in can see NO ACC projects at all"
                        : $"this sign-in can see {all.Value.Count} project(s): " +
                          string.Join("; ", all.Value.Take(MaxProjectsNamed).Select(p => p.ToString())) +
                          (all.Value.Count > MaxProjectsNamed ? $"; … and {all.Value.Count - MaxProjectsNamed} more" : "");
                    Add("3.1", "Configured project visible to this sign-in", AccCheckStatus.Fail,
                        $"the configured project {_c.ProjectId} is not among them — {visible}.",
                        all.Value.Count == 0
                            ? "Add the app as a Custom Integration on the hub (playbook §2), and make sure this Autodesk account is a member of the KUT project."
                            : "Use Find my ACC project on the ACC card and pick the right project (the saved id is wrong, or this account is not a member).");
                    Skip("3.2", "Hub and region", "the configured project is not visible (3.1)");
                    return null;
                }
                Add("3.1", "Configured project visible to this sign-in", AccCheckStatus.Pass,
                    $"'{hit.Name}' in hub '{hit.HubName}' [{hit.HubId}].");

                // 3.2 hub + region against what is saved
                var problems = new List<string>();
                var st = AccCheckStatus.Pass;
                string savedHub = (_c.HubId ?? "").Trim();
                if (savedHub.Length == 0)
                {
                    st = AccCheckStatus.Warn;
                    problems.Add($"no hub id is saved (ACC has it as {hit.HubId}) — an upload with no folder configured cannot locate 'Project Files'");
                }
                else if (!string.Equals(AccIds.ForDataManagement(savedHub), AccIds.ForDataManagement(hit.HubId), StringComparison.OrdinalIgnoreCase))
                {
                    st = AccCheckStatus.Fail;
                    problems.Add($"the saved hub {savedHub} is not the project's hub {hit.HubId}");
                }

                string savedRegion = AccIds.NormaliseRegion(_c.Region);
                string hubRegion = AccIds.NormaliseRegion(hit.Region);
                if (savedRegion != hubRegion)
                {
                    if (savedRegion.Length > 0)
                    {
                        st = AccCheckStatus.Fail;
                        problems.Add($"the saved region '{savedRegion}' differs from the hub's '{(hubRegion.Length == 0 ? "US" : hubRegion)}' — requests are sent to the wrong region");
                    }
                    else
                    {
                        if (st == AccCheckStatus.Pass) st = AccCheckStatus.Warn;
                        problems.Add($"the hub is hosted in {hubRegion} but no region is saved, so requests carry no region hint");
                    }
                }
                Add("3.2", "Hub and region", st,
                    problems.Count == 0
                        ? $"hub {hit.HubId} and region {(hubRegion.Length == 0 ? "US" : hubRegion)} match what is saved."
                        : string.Join("; ", problems) + ".",
                    "Use Find my ACC project on the ACC card and Save — it records the hub and region from ACC.");
                return hit;
            }

            // ── 4 ──
            private async Task<bool> CheckIssueTypeAsync()
            {
                const string title = "Issue type STING files under";
                var resp = await AccHttp.SendAsync(() => AccIssueSync.WithRegion(new HttpRequestMessage(HttpMethod.Get,
                        $"{IssuesProjectUrl(_c)}/issue-types?include=subtypes&limit=100"), _c),
                    _c, idempotent: true).ConfigureAwait(false);
                if (!resp.IsSuccess)
                {
                    var st = resp.Classify();
                    Note(st, resp.Status, "listing issue types (ACC Issues permission)");
                    Add("4.1", title, AccCheckStatus.Fail, $"listing issue types failed ({st}): {resp.Describe()}",
                        Remedy(st, resp.Status, "This account cannot use Issues in this project — ask the project admin to give it Issues access (at least 'Create'), then re-run."));
                    return false;
                }
                _readsOk++;
                var results = AccFetchOutcome.FindArray(resp.Body, "results");
                if (results == null)
                {
                    Add("4.1", title, AccCheckStatus.Fail, "the issue-types response carried no 'results' array — the Issues v1 payload shape has changed.",
                        AccCommandOutcome.Remedy(AccFetchStatus.TransportFailed));
                    return false;
                }
                var active = results.Where(t => t["isActive"] == null || (bool?)t["isActive"] != false).ToList();

                if (!string.IsNullOrEmpty(_c.IssueTypeId) && !string.IsNullOrEmpty(_c.IssueSubtypeId))
                {
                    // Configured: prove both ids are still offered, rather than trusting them.
                    var type = active.FirstOrDefault(t => string.Equals((string)t["id"], _c.IssueTypeId, StringComparison.OrdinalIgnoreCase));
                    var sub = type == null ? null : ((type["subtypes"] as JArray) ?? new JArray())
                        .FirstOrDefault(s => string.Equals((string)s["id"], _c.IssueSubtypeId, StringComparison.OrdinalIgnoreCase)
                                          && (s["isActive"] == null || (bool?)s["isActive"] != false));
                    if (type == null || sub == null)
                    {
                        Add("4.1", title, AccCheckStatus.Fail,
                            $"the configured issue {(type == null ? "type" : "subtype")} " +
                            $"'{(type == null ? _c.IssueTypeId : _c.IssueSubtypeId)}' is not an active one in this project. " +
                            "Offered: " + string.Join(", ", active.Select(t => $"'{(string)t["title"]}' [{(string)t["id"]}]")),
                            "Correct issueTypeId / issueSubtypeId in the project's ACC settings, or clear them so STING chooses by name.");
                        return false;
                    }
                    Add("4.1", title, AccCheckStatus.Pass, $"configured: '{(string)type["title"]} / {(string)sub["title"]}' — both active.");
                    return true;
                }

                var choice = IssueTypeChooser.Choose(active, _c.IssueTypeId);
                if (!choice.Ok)
                {
                    Add("4.1", title, AccCheckStatus.Fail, "an escalated clash would be REFUSED: " + choice.Reason,
                        "Set issueTypeId / issueSubtypeId in the project's ACC settings to one of the types listed.");
                    return false;
                }
                Add("4.1", title, AccCheckStatus.Pass,
                    $"would file under '{choice.TypeTitle} / {choice.SubtypeTitle}' [{choice.SubtypeId}] (chosen by name; not saved by this check).");
                return true;
            }

            /// <summary>4.3: the escalateAssignedTo setting turns into a real ACC member / company /
            /// role. A GET of the project's members (construction/admin, account:read); a 403
            /// there is "needs Project/Account Admin", not "nobody is on the project".</summary>
            private async Task<bool> CheckAssigneeAsync()
            {
                const string title = "Escalation assignee resolves";
                if (string.IsNullOrEmpty(_p.EscalateAssignedTo))
                {
                    Add("4.3", title, AccCheckStatus.Skipped, "not checked: no escalateAssignedTo is configured, so escalated clash issues are left unassigned.",
                        "Optional: set escalateAssignedTo (an email, or a role/company name with escalateAssignedToType) in the project's ACC settings.");
                    return true;
                }
                AccProjectMembers.ClearCache();   // a check reads fresh, never a cached answer
                var dir = await AccProjectMembers.GetDirectoryAsync(_c).ConfigureAwait(false);
                if (dir.Succeeded) _readsOk++;
                // A 403 here is NOT fed to 7.1: the Admin API is closed to plain members by design.
                var r = dir.Succeeded
                    ? AccProjectMembers.Resolve(_p.EscalateAssignedTo, _p.EscalateAssignedToType, dir.Value)
                    : AccProjectMembers.Resolve(_p.EscalateAssignedTo, _p.EscalateAssignedToType, null, dir.Detail);
                if (!r.Ok)
                {
                    Add("4.3", title, AccCheckStatus.Fail, $"'{_p.EscalateAssignedTo}' ({_p.EscalateAssignedToType}) — escalation would be REFUSED: {r.Reason}",
                        dir.Succeeded
                            ? "Correct escalateAssignedTo to a project member's email, or a role/company name exactly as ACC shows it."
                            : "Configure the ACC id instead of a name/email, or run this as a Project Admin so the member list can be read.");
                    return false;
                }
                if (r.Unverified)
                {
                    Add("4.3", title, AccCheckStatus.Warn, $"'{_p.EscalateAssignedTo}' ({r.Type}) is an id and will be sent as configured, but it could not be checked: {dir.Detail}",
                        "Run the self-check once as a Project Admin to prove the id belongs to a project member.");
                    return true;
                }
                Add("4.3", title, AccCheckStatus.Pass, $"escalated clashes would be assigned to {r.Describe()}.");
                return true;
            }

            private async Task<bool> CheckIssueReadAsync()
            {
                const string title = "Issue list readable";
                var resp = await AccHttp.SendAsync(() => AccIssueSync.WithRegion(new HttpRequestMessage(HttpMethod.Get,
                        $"{IssuesProjectUrl(_c)}/issues?limit=1&offset=0"), _c),
                    _c, idempotent: true).ConfigureAwait(false);
                if (!resp.IsSuccess)
                {
                    var st = resp.Classify();
                    Note(st, resp.Status, "reading issues (ACC Issues permission)");
                    Add("4.2", title, AccCheckStatus.Fail, $"reading the issue list failed ({st}): {resp.Describe()}",
                        Remedy(st, resp.Status, "This account cannot read Issues in this project — ask the project admin for Issues access."));
                    return false;
                }
                _readsOk++;
                JObject j;
                try { j = JObject.Parse(resp.Body); }
                catch (Exception ex)
                {
                    Add("4.2", title, AccCheckStatus.Fail, "the issue list was not valid JSON: " + ex.Message,
                        AccCommandOutcome.Remedy(AccFetchStatus.TransportFailed));
                    return false;
                }
                if (!(j["results"] is JArray))
                {
                    Add("4.2", title, AccCheckStatus.Fail, "the issue list carried no 'results' array — the Issues v1 payload shape has changed.",
                        AccCommandOutcome.Remedy(AccFetchStatus.TransportFailed));
                    return false;
                }
                int? total = (int?)j["pagination"]?["totalResults"];
                Add("4.2", title, AccCheckStatus.Pass, total.HasValue
                    ? $"readable; the container holds {total.Value} issue(s)."
                    : $"readable (first page returned {((JArray)j["results"]).Count} row); ACC did not report a total.");
                return true;
            }

            // ── 5 ──
            private async Task<bool> CheckModelCoordinationAsync()
            {
                string container = _c.CoordContainer;
                var sets = await AccModelCoordSync.ListModelSetsAsync(_c, container).ConfigureAwait(false);
                if (!sets.Succeeded)
                {
                    Note(sets.Status, sets.HttpStatus, "listing model sets (Model Coordination permission)");
                    Add("5.1", "Model Coordination model sets", AccCheckStatus.Fail,
                        $"listing model sets on container {container} failed ({sets.Status}): {sets.Detail}",
                        Remedy(sets.Status, sets.HttpStatus, "This account cannot use Model Coordination in this project — ask the project admin for Model Coordination access."));
                    Skip("5.2", "Remembered model set", "the model sets could not be listed (5.1)");
                    Skip("5.3", "Latest clash test", "the model sets could not be listed (5.1)");
                    return false;
                }
                _readsOk++;
                if (sets.Value.Count == 0)
                {
                    Add("5.1", "Model Coordination model sets", AccCheckStatus.Warn,
                        $"container {container} answered and has NO coordination model sets.",
                        "Autodesk offers NO API to create model sets, so this is a one-time step in ACC: Model Coordination > " +
                        "Model sets > Create model set > pick the folder the discipline models are published to (e.g. 01_SHARED/Models) > " +
                        "Create. Clash tests then run automatically on every publish. Or set coordContainerId if coordination lives in " +
                        "another container. Meanwhile STING's own rule-based clash (ClashRun, cycle step 5) runs locally in Revit.");
                    Skip("5.2", "Remembered model set", "there are no model sets (5.1)");
                    Skip("5.3", "Latest clash test", "there are no model sets (5.1)");
                    return true;
                }
                Add("5.1", "Model Coordination model sets", AccCheckStatus.Pass,
                    $"{sets.Value.Count} set(s): " + string.Join(", ", sets.Value.Take(10).Select(s => $"'{s.Name}'")) +
                    (sets.Value.Count > 10 ? ", …" : "") + ".");

                var choice = _p.ResolveModelSet(sets.Value);
                switch (choice.Resolution)
                {
                    case AccModelSetResolution.Chosen:
                        Add("5.2", "Remembered model set", AccCheckStatus.Pass, $"'{choice.Chosen.Name}' [{choice.Chosen.Id}] is offered by the container.");
                        break;
                    case AccModelSetResolution.RememberedMissing:
                        Add("5.2", "Remembered model set", AccCheckStatus.Fail, choice.Reason,
                            "Run Pull Clashes once interactively and remember the right set, or edit coordModelSetId.");
                        Skip("5.3", "Latest clash test", "the remembered model set is not offered (5.2)");
                        return true;
                    default:
                        Add("5.2", "Remembered model set", _p.IsUnattended ? AccCheckStatus.Fail : AccCheckStatus.Warn,
                            choice.Reason + (_p.IsUnattended ? " — an UNATTENDED pull will refuse to run." : " — Pull Clashes will ask which set to use."),
                            "Run Pull Clashes once and accept 'remember this model set'.");
                        Skip("5.3", "Latest clash test", "no model set is remembered, so there is no set to check (5.2)");
                        return true;
                }

                var tests = await AccModelCoordSync.GetClashTestSummaryAsync(_c, container, choice.Chosen.Id).ConfigureAwait(false);
                if (!tests.Succeeded)
                {
                    Note(tests.Status, tests.HttpStatus, "listing clash tests");
                    Add("5.3", "Latest clash test", AccCheckStatus.Fail,
                        $"listing clash tests on '{choice.Chosen.Name}' failed ({tests.Status}): {tests.Detail}",
                        Remedy(tests.Status, tests.HttpStatus, "This account cannot read clash results — ask for Model Coordination access."));
                    return true;
                }
                _readsOk++;
                var t = tests.Value;
                if (t.TestCount == 0)
                    Add("5.3", "Latest clash test", AccCheckStatus.Warn, $"'{choice.Chosen.Name}' has no clash tests yet — a pull would check nothing.",
                        "Clash tests cannot be started through Autodesk's API; ACC runs them automatically when models are " +
                        "published into the model set's folder. Publish (or re-publish) at least two discipline models there, " +
                        "wait for the test to finish, then run Pull Clashes. Meanwhile STING's own ClashRun (cycle step 5) runs locally.");
                else if (!t.HasCompleted)
                    Add("5.3", "Latest clash test", AccCheckStatus.Warn,
                        $"{t.TestCount} test(s), none completed (status: {string.Join(", ", t.States)}) — a pull now would check nothing.",
                        "Wait for the clash test to finish in ACC (Model Coordination shows its progress), then run Pull Clashes. " +
                        "Meanwhile STING's own ClashRun (cycle step 5) runs locally.");
                else
                    Add("5.3", "Latest clash test", AccCheckStatus.Pass,
                        $"latest completed test {t.LatestCompletedId}" +
                        (string.IsNullOrEmpty(t.LatestCompletedAt) ? "" : $" at {t.LatestCompletedAt}") +
                        $" ({t.TestCount} test(s) in all). Results were not downloaded.");
                return true;
            }

            // ── 6 ──
            private async Task<bool> CheckDocsAsync()
            {
                string dmProject = AccIds.ForDataManagement(_c.ProjectId);
                var targets = new List<(string label, string urn)>();

                if (_p.CdeFolders.Count > 0)
                {
                    foreach (var state in AccOperatingPolicy.CdeStates)
                        if (_p.CdeFolders.TryGetValue(state, out var urn) && !string.IsNullOrWhiteSpace(urn))
                            targets.Add(($"CDE {state}", urn.Trim()));
                    var unmapped = AccOperatingPolicy.CdeStates.Where(s => !_p.CdeFolders.ContainsKey(s)).ToList();
                    if (unmapped.Count > 0)
                        Add("6.0", "CDE states without a folder", AccCheckStatus.Warn,
                            $"no folder for {string.Join(", ", unmapped)} — an upload in that state is REFUSED (never sent elsewhere).",
                            "Add the missing states to cdeFolders if files in them are to be uploaded.");
                }
                else if (!string.IsNullOrWhiteSpace(_c.FolderUrn))
                    targets.Add(("upload folder setting", _c.FolderUrn.Trim()));
                else
                {
                    // Exactly the upload's fallback: 'Project Files' by name, via the hub.
                    if (string.IsNullOrWhiteSpace(_c.HubId))
                    {
                        Add("6.1", "Upload folder(s)", AccCheckStatus.Fail,
                            "no upload folder, no CDE folders and no hub id are saved, so an upload cannot locate 'Project Files'.",
                            "Use Find my ACC project (records the hub), or set the CDE folders / upload folder in the project's ACC settings.");
                        return false;
                    }
                    var resp = await AccHttp.SendAsync(() => Get($"{ProjectBase}/hubs/{AccIds.ForDataManagement(_c.HubId)}/projects/{dmProject}/topFolders"),
                        _c, idempotent: true).ConfigureAwait(false);
                    if (!resp.IsSuccess)
                    {
                        var st = resp.Classify();
                        Note(st, resp.Status, "listing top folders (data:read)");
                        Add("6.1", "Upload folder(s)", AccCheckStatus.Fail, $"listing the project's top folders failed ({st}): {resp.Describe()}",
                            Remedy(st, resp.Status, "The token or account cannot read Docs folders — sign in again, and check Docs permission in the project."));
                        return false;
                    }
                    _readsOk++;
                    var data = AccFetchOutcome.FindArray(resp.Body, "data");
                    if (data == null)
                    {
                        Add("6.1", "Upload folder(s)", AccCheckStatus.Fail, "the top-folders response carried no 'data' array.",
                            AccCommandOutcome.Remedy(AccFetchStatus.TransportFailed));
                        return false;
                    }
                    var names = new List<string>();
                    string pf = null;
                    foreach (var f in data)
                    {
                        string name = (string)(f["attributes"]?["name"] ?? f["attributes"]?["displayName"]) ?? "";
                        names.Add(name);
                        if (pf == null && name.Equals("Project Files", StringComparison.OrdinalIgnoreCase)) pf = (string)f["id"];
                    }
                    if (pf == null)
                    {
                        Add("6.1", "Upload folder(s)", AccCheckStatus.Fail,
                            "no upload folder is configured and the project has no 'Project Files' top folder. Top folders: " +
                            string.Join(", ", names.Select(n => $"'{n}'")),
                            "Set the CDE folders or the upload folder in the project's ACC settings.");
                        return false;
                    }
                    Add("6.1", "Upload folder(s)", AccCheckStatus.Warn,
                        $"no folder is configured, so uploads go to the project's 'Project Files' folder [{pf}] — not a CDE folder.",
                        "Map cdeFolders (WIP / SHARED / PUBLISHED / ARCHIVE) in the project's ACC settings so uploads land by suitability.");
                    targets.Add(("'Project Files'", pf));
                    await CheckAttributesAsync(targets).ConfigureAwait(false);
                    return true;
                }

                if (targets.Count == 0)
                {
                    Add("6.1", "Upload folder(s)", AccCheckStatus.Fail, "cdeFolders is set but carries no folder URN for any CDE state.",
                        "Fill in the folder URNs in cdeFolders, or remove the key.");
                    return false;
                }

                var reachable = new List<(string label, string urn)>();
                int i = 0;
                foreach (var (label, urn) in targets)
                {
                    i++;
                    string id = targets.Count == 1 ? "6.1" : $"6.1.{i}";
                    var resp = await AccHttp.SendAsync(() => Get($"{DataBase}/projects/{dmProject}/folders/{Uri.EscapeDataString(urn)}"),
                        _c, idempotent: true).ConfigureAwait(false);
                    if (!resp.IsSuccess)
                    {
                        var st = resp.Classify();
                        Note(st, resp.Status, $"reading folder {label} (data:read / folder permission)");
                        Add(id, $"Upload folder: {label}", AccCheckStatus.Fail, $"folder {urn} is not reachable ({st}): {resp.Describe()}",
                            st == AccFetchStatus.NotFound
                                ? "The URN is wrong or belongs to another project — copy it again from ACC Docs (folder > Copy link) into the project's ACC settings."
                                : Remedy(st, resp.Status, "This account has no permission on the folder — ask the project admin for at least 'View + Download + Upload' on it."));
                        continue;
                    }
                    _readsOk++;
                    string name = null;
                    try
                    {
                        var d = JObject.Parse(resp.Body)["data"];
                        name = (string)(d?["attributes"]?["displayName"] ?? d?["attributes"]?["name"]);
                    }
                    catch (Exception) { /* name is cosmetic; reachability is what was checked */ }
                    Add(id, $"Upload folder: {label}", AccCheckStatus.Pass, $"reachable{(string.IsNullOrEmpty(name) ? "" : $": '{name}'")} [{urn}].");
                    reachable.Add((label, urn));
                }
                await CheckAttributesAsync(reachable).ConfigureAwait(false);
                return true;
            }

            private async Task CheckAttributesAsync(List<(string label, string urn)> folders)
            {
                const string title = "ISO 19650 custom attributes";
                if (!_p.DocsAttributes)
                {
                    Add("6.2", title, AccCheckStatus.Skipped, "not checked: docsAttributes is off in the project's ACC settings, so uploads stamp no attributes.",
                        "Nothing to do unless ISO 19650 metadata on ACC documents is required.");
                    return;
                }
                if (folders.Count == 0)
                {
                    Skip("6.2", title, "no upload folder was reachable (6.1)");
                    return;
                }
                int i = 0;
                foreach (var (label, urn) in folders)
                {
                    i++;
                    string id = folders.Count == 1 ? "6.2" : $"6.2.{i}";
                    // allowCreate: false — read-only. Nothing is created, whatever the settings say.
                    var defs = await AccDocsMetadata.EnsureDefinitionsAsync(_c.AccessToken, _c.ProjectId, urn, allowCreate: false,
                        _p.DocsAttributeNames.Specs(), _c).ConfigureAwait(false);
                    if (!defs.Succeeded)
                    {
                        Note(defs.Status, defs.HttpStatus, $"listing custom attributes on {label}");
                        Add(id, $"{title}: {label}", AccCheckStatus.Fail, $"could not list attribute definitions ({defs.Status}): {defs.Detail}",
                            Remedy(defs.Status, defs.HttpStatus, "The account cannot read the folder's custom attributes — Docs admin permission on the folder is needed to see/create them."));
                        continue;
                    }
                    _readsOk++;
                    var r = defs.Value;
                    if (r.IsComplete)
                    {
                        Add(id, $"{title}: {label}", AccCheckStatus.Pass, $"all {_p.DocsAttributeNames.Specs().Count} STING attributes are defined ({string.Join(", ", _p.DocsAttributeNames.All())}).");
                        continue;
                    }
                    var parts = new List<string>();
                    if (r.Missing.Count > 0) parts.Add("missing: " + string.Join(", ", r.Missing));
                    if (r.TypeMismatch.Count > 0) parts.Add("wrong type/ambiguous: " + string.Join(", ", r.TypeMismatch));
                    Add(id, $"{title}: {label}", AccCheckStatus.Warn,
                        string.Join("; ", parts) + ". Uploads still succeed, but those attributes are NOT written." +
                        (_p.DocsAttributesCreateMissing && r.Missing.Count > 0 ? " docsAttributesCreateMissing is on, so the first upload will try to create the missing ones (needs Docs admin)." : ""),
                        "Create them in ACC Docs (folder settings > Custom attributes, type Text), or set docsAttributesCreateMissing.");
                }
            }

            private static HttpRequestMessage Get(string url)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                return req;
            }

            // ── 7 ──
            private void CheckScopes(bool readsAttempted)
            {
                if (_forbidden.Count > 0)
                    Add("7.1", "Token scopes / permissions (reads)", AccCheckStatus.Fail,
                        "Autodesk answered 403 to: " + string.Join("; ", _forbidden) +
                        ". A 403 with a valid sign-in means the token lacks the scope, or the account lacks the project permission, for that service.",
                        "Sign in again (the sign-in requests data:read data:write data:create account:read), then check this account's project permissions for each service named.");
                else if (!readsAttempted || _readsOk == 0)
                    Skip("7.1", "Token scopes / permissions (reads)", "no ACC read was attempted, so no scope was exercised");
                else
                    Add("7.1", "Token scopes / permissions (reads)", AccCheckStatus.Pass,
                        $"{_readsOk} read(s) succeeded and none was refused with 403.");

                Add("7.2", "Write scopes (data:create / data:write, issue create)", AccCheckStatus.Skipped,
                    "not checked: this self-check never writes to ACC, and a write permission cannot be proved without writing.",
                    "Prove them with playbook V5 (escalate one clash) and V7 (upload one test PDF). A 403 on upload storage means the token lacks data:create — sign in again.");
            }
        }
    }
}
