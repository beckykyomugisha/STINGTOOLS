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

            var scoredAll = ClashTriageEngine.TriageAll(inputs);   // full set (CSV + burn-down)
            var scored = scoredAll.Take(10).ToList();              // top for display + push
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
                string sidecar = SidecarPath(doc);
                var pushedMap = LoadPushed(sidecar);
                var (pushed, skipped) = PushTopIssues(creds, scored, byId, chosen, pushedMap);
                SavePushed(sidecar, pushedMap);
                TaskDialog.Show("ACC — Pull Clashes",
                    $"Pushed {pushed} new clash(es) to ACC Issues; {skipped} already pushed (skipped).");
            }

            StingLog.Info($"ACC_PullClashes: {clashes.Count} clashes, {scored.Count} triaged, set '{chosen.Name}'.");
            return Result.Succeeded;
        }

        // Idempotent push: skip clashes already issued (by stable signature), record the
        // returned ACC issue id in the sidecar so re-runs don't create duplicate issues.
        private static (int pushed, int skipped) PushTopIssues(AccCredentials creds, List<ScoredClash> top,
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
            string accDir = Path.Combine(dir, "_BIM_COORD", "acc");
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
