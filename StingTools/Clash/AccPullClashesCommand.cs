// AccPullClashesCommand.cs — ACC Model Coordination read + triage.
//
// Closes the "clash in ACC AND STING" loop confirmed for the Kampala Temple
// engagement: ACC Model Coordination runs the federated clash (system of
// record); this command PULLS ACC's grouped clash results, ranks them with
// the existing ClashTriageEngine, writes a triage CSV, and offers to push the
// top clashes back to ACC Issues with an assigned priority via AccIssueSync.
//
// Read-only with respect to the Revit model (no Transaction). Network I/O only.
// Credentials come from %APPDATA%\Planscape\acc_credentials.json (AccIssueSync),
// so nothing touches the project file or source control.
//
// Built without dotnet build verification (Linux sandbox); the ACC Model
// Coordination v3 endpoint paths + grouped-clash field mapping are marked
// // TODO-VERIFY-API in AccModelCoordSync — confirm against APS docs in Revit
// before the engagement relies on the field mapping.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
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
                "Grouped clash results are pulled from the selected model set, then triaged.",
                sets.Select(s => $"{s.Name}  [{s.Id}]").ToList());
            if (string.IsNullOrEmpty(pick)) return Result.Cancelled;
            var chosen = sets.First(s => $"{s.Name}  [{s.Id}]" == pick);

            // 2. Pull grouped clashes.
            List<AccClashGroup> groups;
            try { groups = AccModelCoordSync.GetGroupedClashesAsync(creds, containerId, chosen.Id).GetAwaiter().GetResult(); }
            catch (Exception ex) { StingLog.Error("ACC GetGroupedClashes", ex); TaskDialog.Show("ACC", "Clash request failed: " + ex.Message); return Result.Failed; }

            if (groups == null || groups.Count == 0)
            {
                TaskDialog.Show("ACC — Pull Clashes",
                    $"Model set '{chosen.Name}' returned no grouped clashes.\n\n" +
                    "Either the model set is clash-clean, or a clash test has not run in ACC yet.");
                return Result.Succeeded;
            }

            // 3. Map → ClashInput → triage.
            var inputs = groups.Select(g => new ClashInput
            {
                ClashId         = g.Id,
                ElementAId      = g.LeftObjectId,
                ElementBId      = g.RightObjectId,
                CategoryA       = g.LeftCategory,
                CategoryB       = g.RightCategory,
                RecurrenceCount = Math.Max(0, g.Count - 1),  // larger groups rank higher
            }).ToList();

            var scored = ClashTriageEngine.Triage(inputs);   // top-N by score
            var byId = groups.ToDictionary(g => g.Id, g => g, StringComparer.OrdinalIgnoreCase);

            // 4. Report + CSV.
            var report = new StringBuilder();
            report.AppendLine($"Model set: {chosen.Name}");
            report.AppendLine($"Grouped clashes pulled: {groups.Count}   (top {scored.Count} by triage score)");
            report.AppendLine();
            foreach (var s in scored.Take(10))
            {
                byId.TryGetValue(s.ClashId, out var g);
                report.AppendLine($"  {s.Score:F2}  [{s.Category}]  group {s.ClashId}" +
                                  (g != null ? $"  x{g.Count}  {Short(g.LeftCategory)} ↔ {Short(g.RightCategory)}" : ""));
            }
            string csv = WriteCsv(doc, chosen, scored, byId);
            if (csv != null) { report.AppendLine(); report.AppendLine("CSV: " + csv); }

            // 5. Offer to push the triaged top clashes back to ACC Issues.
            var dlg = new TaskDialog("ACC — Pull Clashes")
            {
                MainInstruction = $"{groups.Count} grouped clashes triaged — top {Math.Min(10, scored.Count)} shown",
                MainContent = report.ToString(),
                CommonButtons = TaskDialogCommonButtons.Close,
                AllowCancellation = true,
            };
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"Push top {Math.Min(scored.Count, 10)} clashes to ACC Issues",
                "Creates one ACC Issue per top-ranked clash group, tagged with the STING triage score.");
            var res = dlg.Show();

            if (res == TaskDialogResult.CommandLink1)
            {
                int pushed = PushTopIssues(creds, scored.Take(10).ToList(), byId, chosen);
                TaskDialog.Show("ACC — Pull Clashes", $"Pushed {pushed} clash(es) to ACC Issues.");
            }

            StingLog.Info($"ACC_PullClashes: {groups.Count} groups, {scored.Count} triaged, set '{chosen.Name}'.");
            return Result.Succeeded;
        }

        private static int PushTopIssues(AccCredentials creds, List<ScoredClash> top,
            Dictionary<string, AccClashGroup> byId, AccModelSet set)
        {
            int pushed = 0;
            foreach (var s in top)
            {
                byId.TryGetValue(s.ClashId, out var g);
                var issue = new AccIssue
                {
                    Title = $"Clash {s.ClashId} [{s.Category}] (STING score {s.Score:F2})",
                    Description = $"Triaged from ACC Model Coordination set '{set.Name}'.\n" +
                                  $"Score {s.Score:F2} — {s.Rationale}\n" +
                                  (g != null ? $"Instances: {g.Count}; {g.LeftCategory} ↔ {g.RightCategory}" : ""),
                    Status = "open",
                    LocationDescription = set.Name,
                };
                try
                {
                    var id = AccIssueSync.PushIssueAsync(creds, issue).GetAwaiter().GetResult();
                    if (!string.IsNullOrEmpty(id)) pushed++;
                }
                catch (Exception ex) { StingLog.Warn("ACC push issue: " + ex.Message); }
            }
            return pushed;
        }

        private static string WriteCsv(Document doc, AccModelSet set, List<ScoredClash> scored,
            Dictionary<string, AccClashGroup> byId)
        {
            try
            {
                var rows = new List<string> { "Score,Category,GroupId,Instances,LeftCategory,RightCategory,LeftObjectId,RightObjectId,Rationale" };
                foreach (var s in scored)
                {
                    byId.TryGetValue(s.ClashId, out var g);
                    rows.Add(string.Join(",",
                        s.Score.ToString("F3"), Csv(s.Category), Csv(s.ClashId),
                        g?.Count ?? 0, Csv(g?.LeftCategory), Csv(g?.RightCategory),
                        g?.LeftObjectId ?? 0, g?.RightObjectId ?? 0, Csv(s.Rationale)));
                }
                string safe = new string((set.Name ?? "set").Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
                string path = OutputLocationHelper.GetOutputPath(doc, $"STING_ACC_Clashes_{safe}.csv");
                File.WriteAllLines(path, rows, Encoding.UTF8);
                return path;
            }
            catch (Exception ex) { StingLog.Warn("ACC clash CSV: " + ex.Message); return null; }
        }

        private static string Short(string s) => string.IsNullOrEmpty(s) ? "?" : s.Replace("OST_", "");
        private static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }
}
