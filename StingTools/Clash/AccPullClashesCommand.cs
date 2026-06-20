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
                    "ProjectId is the ACC coordination container id.");
                return Result.Cancelled;
            }
            string containerId = creds.ProjectId;

            // 1. List model sets and let the user pick.
            List<AccModelSet> sets;
            try { sets = AccModelCoordSync.ListModelSetsAsync(creds, containerId).GetAwaiter().GetResult(); }
            catch (Exception ex) { StingLog.Error("ACC ListModelSets", ex); TaskDialog.Show("ACC", "Model-set request failed: " + ex.Message); return Result.Failed; }

            if (sets == null || sets.Count == 0)
            {
                TaskDialog.Show("ACC — Pull Clashes",
                    "No coordination model sets returned for this container.\n\n" +
                    "Confirm the container id (AccCredentials.ProjectId) and that Model " +
                    "Coordination is enabled on the ACC project.");
                return Result.Succeeded;
            }

            string pick = StingListPicker.Show("ACC — pick a coordination model set",
                "Clash results are pulled from the selected model set's latest test, then triaged.",
                sets.Select(s => $"{s.Name}  [{s.Id}]").ToList());
            if (string.IsNullOrEmpty(pick)) return Result.Cancelled;
            var chosen = sets.First(s => $"{s.Name}  [{s.Id}]" == pick);

            // 2. Pull clashes (latest test -> resources -> scope files -> join).
            List<AccClashRecord> clashes;
            try { clashes = AccModelCoordSync.GetClashesAsync(creds, containerId, chosen.Id).GetAwaiter().GetResult(); }
            catch (Exception ex) { StingLog.Error("ACC GetClashes", ex); TaskDialog.Show("ACC", "Clash request failed: " + ex.Message); return Result.Failed; }

            if (clashes == null || clashes.Count == 0)
            {
                TaskDialog.Show("ACC — Pull Clashes",
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

            var scored = ClashTriageEngine.Triage(inputs);   // top-N by score
            var byId = clashes.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // 4. Report + CSV.
            var report = new StringBuilder();
            report.AppendLine($"Model set: {chosen.Name}");
            report.AppendLine($"Clashes pulled: {clashes.Count}   (top {scored.Count} by triage score)");
            report.AppendLine();
            foreach (var s in scored.Take(10))
            {
                byId.TryGetValue(s.ClashId, out var c);
                report.AppendLine($"  {s.Score:F2}  [{s.Category}]  clash {s.ClashId}" +
                                  (c != null ? $"  pen {c.PenetrationMm:F0}mm  {DocShort(c.LeftDocument)} ↔ {DocShort(c.RightDocument)}" : ""));
            }
            string csv = WriteCsv(doc, chosen, scored, byId);
            if (csv != null) { report.AppendLine(); report.AppendLine("CSV: " + csv); }

            // 5. Offer to push the triaged top clashes back to ACC Issues.
            var dlg = new TaskDialog("ACC — Pull Clashes")
            {
                MainInstruction = $"{clashes.Count} clashes triaged — top {Math.Min(10, scored.Count)} shown",
                MainContent = report.ToString(),
                CommonButtons = TaskDialogCommonButtons.Close,
                AllowCancellation = true,
            };
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"Push top {Math.Min(scored.Count, 10)} clashes to ACC Issues",
                "Creates one ACC Issue per top-ranked clash, tagged with the STING triage score.");
            var res = dlg.Show();

            if (res == TaskDialogResult.CommandLink1)
            {
                int pushed = PushTopIssues(doc, creds, scored.Take(10).ToList(), byId, chosen, out int skipped);
                TaskDialog.Show("ACC — Pull Clashes",
                    $"Pushed {pushed} new clash(es) to ACC Issues." +
                    (skipped > 0 ? $"\nSkipped {skipped} already pushed (idempotent — see _BIM_COORD/acc/pushed_clashes.json)." : ""));
            }

            StingLog.Info($"ACC_PullClashes: {clashes.Count} clashes, {scored.Count} triaged, set '{chosen.Name}'.");
            return Result.Succeeded;
        }

        // N-G1: idempotent push. A sidecar maps a STABLE clash signature
        // (leftObjectId|rightObjectId|leftDoc|rightDoc — NOT the volatile ACC
        // clash id, which can churn between tests) to the ACC issue id it
        // created, so re-running pushes 0 new issues when nothing changed.
        private static int PushTopIssues(Document doc, AccCredentials creds, List<ScoredClash> top,
            Dictionary<string, AccClashRecord> byId, AccModelSet set, out int skipped)
        {
            skipped = 0;
            int pushed = 0;
            string sidecar = PushedSidecarPath(doc);
            var pushedMap = LoadPushedMap(sidecar);
            bool dirty = false;

            foreach (var s in top)
            {
                byId.TryGetValue(s.ClashId, out var c);
                if (c == null) continue;
                string sig = ClashSignature(c);
                if (pushedMap.ContainsKey(sig)) { skipped++; continue; }   // already pushed

                var issue = new AccIssue
                {
                    Title = $"Clash {s.ClashId} [{s.Category}] (STING score {s.Score:F2})",
                    Description = $"Triaged from ACC Model Coordination set '{set.Name}'.\n" +
                                  $"Score {s.Score:F2} — {s.Rationale}\n" +
                                  $"Penetration {c.PenetrationMm:F0} mm; {c.LeftDocument} ↔ {c.RightDocument}",
                    Status = "open",
                    LocationDescription = set.Name,
                };
                try
                {
                    var id = AccIssueSync.PushIssueAsync(creds, issue).GetAwaiter().GetResult();
                    if (!string.IsNullOrEmpty(id))
                    {
                        pushedMap[sig] = id;
                        dirty = true;
                        pushed++;
                    }
                }
                catch (Exception ex) { StingLog.Warn("ACC push issue: " + ex.Message); }
            }

            if (dirty) SavePushedMap(sidecar, pushedMap);
            return pushed;
        }

        // Stable signature: object dbIds + source document names. Deliberately
        // NOT the ACC clash id (it is per-test and not stable across re-runs).
        private static string ClashSignature(AccClashRecord c)
            => $"{c.LeftObjectId}|{c.RightObjectId}|{c.LeftDocument}|{c.RightDocument}";

        private static string PushedSidecarPath(Document doc)
        {
            string projDir = string.IsNullOrEmpty(doc?.PathName) ? null : Path.GetDirectoryName(doc.PathName);
            // Fall back to the standard output location for unsaved/cloud models.
            string baseDir = !string.IsNullOrEmpty(projDir)
                ? Path.Combine(projDir, "_BIM_COORD", "acc")
                : Path.Combine(Path.GetDirectoryName(OutputLocationHelper.GetOutputPath(doc, "x.txt")) ?? Path.GetTempPath(), "_BIM_COORD", "acc");
            return Path.Combine(baseDir, "pushed_clashes.json");
        }

        private static Dictionary<string, string> LoadPushedMap(string path)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                if (path != null && File.Exists(path))
                {
                    var j = JObject.Parse(File.ReadAllText(path));
                    foreach (var p in j.Properties())
                        map[p.Name] = (string)p.Value ?? string.Empty;
                }
            }
            catch (Exception ex) { StingLog.Warn("ACC pushed_clashes load: " + ex.Message); }
            return map;
        }

        private static void SavePushedMap(string path, Dictionary<string, string> map)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var j = new JObject();
                foreach (var kv in map) j[kv.Key] = kv.Value;
                File.WriteAllText(path, j.ToString());
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
