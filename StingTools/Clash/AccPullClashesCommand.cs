// AccPullClashesCommand.cs — ACC Model Coordination read + triage.
//
// Closes the "clash in ACC AND STING" loop confirmed for the Kampala Temple
// engagement: ACC Model Coordination runs the federated clash (system of
// record); this command PULLS ACC's clash results, ranks them with the
// existing ClashTriageEngine, writes a triage CSV, and offers to push the top
// clashes back to ACC Issues with the STING triage score via AccIssueSync.
//
// ACC clash data carries no Revit category — only object dbIds + source
// document names — so triage severity is derived from the document-name
// discipline (AccModelCoordSync.DisciplineOst) and the real penetration
// distance (dist), not a Revit category lookup.
//
// Read-only with respect to the Revit model (no Transaction). Network I/O only.
// Credentials come from %APPDATA%\Planscape\acc_credentials.json (AccIssueSync),
// so nothing touches the project file or source control.
//
// Built without dotnet build verification (Linux sandbox); endpoint paths +
// payload field names are verified against the public APS aps-clash-data-view
// sample, but confirm with one live pull before the engagement relies on them.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.Select;
using StingTools.UI;
using StingTools.V6;

namespace StingTools.Core.Clash
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AccPullClashesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var creds = AccIssueSync.LoadCredentials();
            if (string.IsNullOrEmpty(creds.ClientId) || string.IsNullOrEmpty(creds.RefreshToken) ||
                string.IsNullOrEmpty(creds.ProjectId))
            {
                TaskDialog.Show("ACC — Pull Clashes",
                    "ACC credentials are not configured.\n\n" +
                    "Create %APPDATA%\\Planscape\\acc_credentials.json with at least:\n" +
                    "  ClientId, ClientSecret, RefreshToken, ProjectId\n\n" +
                    "ProjectId is the Issues container; set CoordContainerId if the Model " +
                    "Coordination container differs.");
                return Result.Cancelled;
            }
            string containerId = creds.CoordContainer;   // falls back to ProjectId

            // The project's operating settings decide whether this run may prompt, and what
            // it does instead. Absent or unreadable settings mean "prompt for everything",
            // which is every project that exists today.
            var policy = AccProjectSettingsFile.LoadFor(doc, "ACC_PullClashes");

            // 1. List model sets, then resolve which one WITHOUT a picker if the project
            //    remembers one.
            AccFetchResult<List<AccModelSet>> setsResult;
            try { setsResult = AccModelCoordSync.ListModelSetsAsync(creds, containerId).GetAwaiter().GetResult(); }
            catch (Exception ex) { StingLog.Error("ACC ListModelSets", ex); TaskDialog.Show("ACC", "Model-set request failed: " + ex.Message); return Result.Failed; }

            // A failed request is NOT "no model sets". Saying so let a wrong container id
            // read as a clean federation and pass a coordination gate that checked nothing.
            if (!setsResult.Succeeded)
            {
                Report(policy, "ACC — Pull Clashes", FailureMessage("model sets", setsResult.Status,
                    setsResult.HttpStatus, setsResult.Detail, containerId));
                StingLog.Warn($"ACC_PullClashes FAILED ({setsResult.Status}) listing model sets on container '{containerId}': {setsResult.Detail}");
                return Result.Failed;
            }
            var sets = setsResult.Value;
            if (sets.Count == 0)
            {
                Report(policy, "ACC — Pull Clashes",
                    "Autodesk answered, and this container has no coordination model sets.\n\n" +
                    $"Container queried: {containerId}\n\n" +
                    "Confirm Model Coordination is enabled on the ACC project and that a " +
                    "model set has been created.");
                return Result.Succeeded;
            }

            var choice = policy.ResolveModelSet(sets);
            AccModelSet chosen;

            if (choice.Resolution == AccModelSetResolution.Chosen)
            {
                chosen = choice.Chosen;
                StingLog.Info($"ACC_PullClashes: using the remembered coordination model set " +
                              $"'{chosen.Name}' [{chosen.Id}] — no picker shown.");
            }
            else
            {
                // A remembered id ACC no longer offers is REPORTED, never guessed past.
                // Falling through to "the first one" would pull clashes from a different
                // federation and report them as this one's — a wrong model set looks like a
                // clean model set, which is the defect PR #927 closed.
                if (choice.Resolution == AccModelSetResolution.RememberedMissing)
                    StingLog.Warn("ACC_PullClashes: " + choice.Reason);

                if (!policy.MayPrompt)
                {
                    // Unattended: there is nobody to ask, so stop rather than assume.
                    Report(policy, "ACC — Pull Clashes",
                        "Cannot choose a coordination model set without asking, and this project is " +
                        "configured for unattended operation.\n\n" +
                        choice.Reason + "\n\n" +
                        "Set the coordination model set on the BIM Coordination Center ACC card, or " +
                        "remove \"unattended\" from the project ACC settings so this run can prompt.");
                    StingLog.Warn($"ACC_PullClashes FAILED (unattended, {choice.Resolution}) on container " +
                                  $"'{containerId}': {choice.Reason}");
                    return Result.Failed;
                }

                if (choice.Resolution == AccModelSetResolution.RememberedMissing)
                    TaskDialog.Show("ACC — Pull Clashes",
                        "The coordination model set this project remembers is gone.\n\n" +
                        choice.Reason + "\n\nPick one below; nothing has been assumed.");

                string pick = StingListPicker.Show("ACC — pick a coordination model set",
                    "Clash results are pulled from the selected model set's latest test, then triaged.",
                    sets.Select(s => $"{s.Name}  [{s.Id}]").ToList());
                if (string.IsNullOrEmpty(pick)) return Result.Cancelled;
                chosen = sets.First(s => $"{s.Name}  [{s.Id}]" == pick);

                // Offer to remember it — an EXPLICIT choice, never a side effect of the read.
                OfferToRemember(doc, policy, chosen);
            }

            // 2. Pull clashes (latest test -> resources -> scope files -> join).
            AccFetchResult<List<AccClashRecord>> clashResult;
            try { clashResult = AccModelCoordSync.GetClashesAsync(creds, containerId, chosen.Id).GetAwaiter().GetResult(); }
            catch (Exception ex) { StingLog.Error("ACC GetClashes", ex); TaskDialog.Show("ACC", "Clash request failed: " + ex.Message); return Result.Failed; }

            // The load-bearing branch. Only a request that ACTUALLY SUCCEEDED may be
            // reported as "clash-clean"; every failure names itself and fails the step,
            // so a workflow cannot record a coordination cycle it never ran.
            if (!clashResult.Succeeded)
            {
                Report(policy, "ACC — Pull Clashes", FailureMessage($"clashes for model set '{chosen.Name}'",
                    clashResult.Status, clashResult.HttpStatus, clashResult.Detail, containerId));
                StingLog.Warn($"ACC_PullClashes FAILED ({clashResult.Status}) pulling clashes for set '{chosen.Name}' " +
                              $"on container '{containerId}': {clashResult.Detail}");
                return Result.Failed;
            }
            var clashes = clashResult.Value;
            if (clashes.Count == 0)
            {
                Report(policy, "ACC — Pull Clashes",
                    $"Model set '{chosen.Name}' returned no clashes.\n\n" +
                    "Either the model set is clash-clean, or a clash test has not completed in ACC yet.");
                return Result.Succeeded;
            }

            // 3. Map -> ClashInput -> triage. No Revit category in ACC data:
            //    severity comes from document-name discipline + penetration depth.
            var inputs = clashes.Select(c => new ClashInput
            {
                ClashId       = c.Id,
                ElementAId    = c.LeftObjectId,
                ElementBId    = c.RightObjectId,
                CategoryA     = AccModelCoordSync.DisciplineOst(c.LeftDocument),
                CategoryB     = AccModelCoordSync.DisciplineOst(c.RightDocument),
                PenetrationMm = c.PenetrationMm,
            }).ToList();

            var scoredAll = ClashTriageEngine.TriageAll(inputs);   // full set (CSV + burn-down)
            var scored = scoredAll.Take(10).ToList();              // top for DISPLAY only
            var byId = clashes.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // 4. Report + CSV.
            var report = new StringBuilder();
            report.AppendLine($"Model set: {chosen.Name}");
            report.AppendLine($"Clashes pulled: {clashes.Count}   (top {scored.Count} shown; CSV has all {scoredAll.Count})");
            report.AppendLine();
            foreach (var s in scored.Take(10))
            {
                byId.TryGetValue(s.ClashId, out var c);
                report.AppendLine($"  {s.Score:F2}  [{s.Category}]  clash {s.ClashId}" +
                                  (c != null ? $"  pen {c.PenetrationMm:F0}mm  {DocShort(c.LeftDocument)} ↔ {DocShort(c.RightDocument)}" : ""));
            }
            string csv = WriteCsv(doc, chosen, scoredAll, byId);
            if (csv != null) { report.AppendLine(); report.AppendLine("CSV: " + csv); }

            // 5. Escalate to ACC Issues — by POLICY, not by a hardcoded 10.
            //
            // Escalation creates ACC Issues assigned to real people, so it is the seam where
            // automation is most dangerous. An unattended run escalates only what the project
            // explicitly configured, and NOTHING at all without a policy: a pull + triage +
            // CSV is a useful cycle on its own and it creates no work for anyone.
            string sidecar = SidecarPath(doc);
            var pushedMap = LoadPushed(sidecar);
            var tracked = new HashSet<string>(pushedMap.Keys, StringComparer.Ordinal);
            var plan = policy.PlanEscalation(scoredAll, sc => SignatureFor(sc, byId), tracked);
            StingLog.Info("ACC_PullClashes escalation — " + plan.Reason);

            if (!policy.MayPrompt)
            {
                if (plan.ToPush.Count > 0)
                {
                    var (pushed, skipped) = PushTopIssues(creds, plan.ToPush, byId, chosen, pushedMap);
                    SavePushed(sidecar, pushedMap);
                    StingLog.Info($"ACC_PullClashes: escalated {pushed} clash(es) to ACC Issues " +
                                  $"({skipped} already tracked).");
                    report.AppendLine();
                    report.AppendLine($"Escalated {pushed} clash(es) to ACC Issues by policy ({skipped} already tracked).");
                }
                else
                {
                    report.AppendLine();
                    report.AppendLine(plan.Reason);
                }
                Report(policy, "ACC — Pull Clashes", report.ToString());
            }
            else
            {
                var dlg = new TaskDialog("ACC — Pull Clashes")
                {
                    MainInstruction = $"{clashes.Count} clashes triaged — top {Math.Min(10, scored.Count)} shown",
                    // The rule that would apply is shown, so an operator sees the policy
                    // rather than inferring it from what happens next.
                    MainContent = report.ToString() + Environment.NewLine + plan.Reason,
                    CommonButtons = TaskDialogCommonButtons.Close,
                    AllowCancellation = true,
                };
                if (plan.OfferInteractively)
                    dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        $"Push {plan.ToPush.Count} clash(es) to ACC Issues",
                        "Creates one ACC Issue per selected clash, tagged with the STING triage score.");
                var res = dlg.Show();

                if (res == TaskDialogResult.CommandLink1 && plan.OfferInteractively)
                {
                    var (pushed, skipped) = PushTopIssues(creds, plan.ToPush, byId, chosen, pushedMap);
                    SavePushed(sidecar, pushedMap);
                    TaskDialog.Show("ACC — Pull Clashes",
                        $"Pushed {pushed} new clash(es) to ACC Issues; {skipped} already pushed (skipped).");
                }
            }

            StingLog.Info($"ACC_PullClashes: {clashes.Count} clashes, {scored.Count} triaged, set '{chosen.Name}'.");
            return Result.Succeeded;
        }

        /// <summary>Show a result to whoever is there, or write it to the log when nobody
        /// is. NOT a prompt in either mode - it asks nothing and blocks no decision; it is
        /// suppressed in unattended mode only so a scheduled run does not sit on a modal
        /// window waiting for an OK that nobody will click.</summary>
        internal static void Report(AccOperatingPolicy policy, string title, string text)
        {
            if (policy != null && !policy.MayPrompt) { StingLog.Info($"{title}: {text}"); return; }
            TaskDialog.Show(title, text);
        }

        /// <summary>The escalation signature for a scored clash - the SAME order-invariant
        /// key PushTopIssues records in pushed_clashes.json. One function, so the plan and
        /// the push cannot disagree about what "already escalated" means.</summary>
        internal static string SignatureFor(ScoredClash s, Dictionary<string, AccClashRecord> byId)
        {
            if (s == null) return string.Empty;
            return byId != null && byId.TryGetValue(s.ClashId, out var c) && c != null
                ? Signature(c)
                : s.ClashId;
        }

        /// <summary>After an interactive pick, offer to remember it. Writing the project
        /// settings file is an EXPLICIT choice - never a side effect of running a pull.</summary>
        private static void OfferToRemember(Document doc, AccOperatingPolicy policy, AccModelSet chosen)
        {
            if (doc == null || chosen == null || policy == null || !policy.MayPrompt) return;
            if (string.Equals(policy.CoordModelSetId, chosen.Id, StringComparison.OrdinalIgnoreCase)) return;
            if (string.IsNullOrEmpty(AccProjectSettingsFile.PathFor(doc))) return;   // unsaved model

            var ask = new TaskDialog("ACC — Pull Clashes")
            {
                MainInstruction = "Remember this coordination model set for this project?",
                MainContent = $"'{chosen.Name}' [{chosen.Id}]\n\n" +
                              "Later runs would use it without asking. It is stored with the project, " +
                              "not with your credentials, and you can change it on the BIM Coordination " +
                              "Center ACC card.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
                AllowCancellation = true,
            };
            if (ask.Show() != TaskDialogResult.Yes) return;

            if (AccProjectSettingsFile.SaveCoordModelSet(doc, chosen.Id, chosen.Name, out string err))
                TaskDialog.Show("ACC — Pull Clashes", $"Remembered '{chosen.Name}' for this project.");
            else
                TaskDialog.Show("ACC — Pull Clashes", "Could not remember it: " + err);
        }

        /// <summary>Message for a read that did NOT succeed. Delegates to
        /// <see cref="AccCommandOutcome.FailureMessage"/> so this command and
        /// AccSyncIssueStatusCommand cannot describe the same failure differently, and so
        /// the wording is covered by a test — it lives in a Revit-free file precisely
        /// because this one cannot be linked into a test project.</summary>
        internal static string FailureMessage(string what, AccFetchStatus status, int httpStatus,
            string detail, string containerId)
            => AccCommandOutcome.FailureMessage(what, status, httpStatus, detail, containerId);

        // Idempotent push: skip clashes already issued (by stable signature), record the
        // returned ACC issue id in the sidecar so re-runs don't create duplicate issues.
        private static (int pushed, int skipped) PushTopIssues(AccCredentials creds, IReadOnlyList<ScoredClash> top,
            Dictionary<string, AccClashRecord> byId, AccModelSet set, Dictionary<string, string> pushedMap)
        {
            int pushed = 0, skipped = 0;
            foreach (var s in top)
            {
                byId.TryGetValue(s.ClashId, out var c);
                string sig = c != null ? Signature(c) : s.ClashId;
                if (pushedMap.ContainsKey(sig)) { skipped++; continue; }

                var issue = new AccIssue
                {
                    Title = $"Clash [{s.Category}] (STING score {s.Score:F2})",
                    Description = $"Triaged from ACC Model Coordination set '{set.Name}'.\n" +
                                  $"Score {s.Score:F2} — {s.Rationale}\n" +
                                  (c != null ? $"Penetration {c.PenetrationMm:F0} mm; {c.LeftDocument} ↔ {c.RightDocument}" : ""),
                    Status = "open",
                    LocationDescription = set.Name,
                };
                try
                {
                    var id = AccIssueSync.PushIssueAsync(creds, issue).GetAwaiter().GetResult();
                    if (!string.IsNullOrEmpty(id)) { pushed++; pushedMap[sig] = id; }
                }
                catch (Exception ex) { StingLog.Warn("ACC push issue: " + ex.Message); }
            }
            return (pushed, skipped);
        }

        /// <summary>Order-invariant clash signature (object dbid @ document for each side,
        /// sorted) so an A/B swap between ACC runs maps to the same key — true idempotency.</summary>
        internal static string Signature(AccClashRecord c)
        {
            string a = $"{c.LeftObjectId}@{c.LeftDocument}";
            string b = $"{c.RightObjectId}@{c.RightDocument}";
            return string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";
        }

        internal static string SidecarPath(Document doc)
        {
            string dir = Path.GetDirectoryName(doc?.PathName ?? "");
            if (string.IsNullOrEmpty(dir))
                dir = Path.GetDirectoryName(OutputLocationHelper.GetOutputPath(doc, "x.txt")) ?? Path.GetTempPath();
            string accDir = StingPaths.MetaFile(doc, "_BIM_COORD", "acc");
            try { Directory.CreateDirectory(accDir); } catch { }
            return Path.Combine(accDir, "pushed_clashes.json");
        }

        internal static Dictionary<string, string> LoadPushed(string path)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                if (path != null && File.Exists(path))
                    foreach (var p in JObject.Parse(File.ReadAllText(path)).Properties())
                        map[p.Name] = (string)p.Value ?? string.Empty;
            }
            catch (Exception ex) { StingLog.Warn("ACC pushed_clashes load: " + ex.Message); }
            return map;
        }

        internal static void SavePushed(string path, Dictionary<string, string> map)
        {
            try
            {
                var o = new JObject();
                foreach (var kv in map) o[kv.Key] = kv.Value;
                File.WriteAllText(path, o.ToString());
            }
            catch (Exception ex) { StingLog.Warn("ACC pushed_clashes save: " + ex.Message); }
        }

        private static string WriteCsv(Document doc, AccModelSet set, List<ScoredClash> scored,
            Dictionary<string, AccClashRecord> byId)
        {
            try
            {
                var rows = new List<string> { "Score,Category,ClashId,PenetrationMm,Status,LeftDocument,RightDocument,LeftObjectId,RightObjectId,Rationale" };
                foreach (var s in scored)
                {
                    byId.TryGetValue(s.ClashId, out var c);
                    rows.Add(string.Join(",",
                        s.Score.ToString("F3"), Csv(s.Category), Csv(s.ClashId),
                        (c?.PenetrationMm ?? 0).ToString("F0"), Csv(c?.Status),
                        Csv(c?.LeftDocument), Csv(c?.RightDocument),
                        c?.LeftObjectId ?? 0, c?.RightObjectId ?? 0, Csv(s.Rationale)));
                }
                string safe = new string((set.Name ?? "set").Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
                string path = OutputLocationHelper.GetOutputPath(doc, $"STING_ACC_Clashes_{safe}.csv");
                File.WriteAllLines(path, rows, Encoding.UTF8);
                return path;
            }
            catch (Exception ex) { StingLog.Warn("ACC clash CSV: " + ex.Message); return null; }
        }

        private static string DocShort(string s)
        {
            if (string.IsNullOrEmpty(s)) return "?";
            s = Path.GetFileNameWithoutExtension(s);
            return s.Length > 24 ? s.Substring(0, 24) : s;
        }

        private static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }
}
