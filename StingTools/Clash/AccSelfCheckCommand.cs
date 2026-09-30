// AccSelfCheckCommand.cs — ACC_SelfCheck: is ACC ready for this project, and if not, why.
//
// The Revit half of V6/AccSelfCheck. Everything that decides PASS / WARN / FAIL / SKIPPED
// lives there (Revit-free, tested over loopback); this command only gathers the inputs from
// the document, writes the report and shows it.
//
// READ-ONLY against ACC (see AccSelfCheck's header). The only file it writes is its own
// report, routed through OutputLocationHelper. It never writes acc_settings.json.
//
// Returns Failed when any check FAILs, so a workflow step (or a person) can gate on it.

using System;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.V6;

namespace StingTools.Core.Clash
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AccSelfCheckCommand : IExternalCommand
    {
        private const string Title = "ACC — Self-check";

        /// <summary>
        /// Row 0.1 — where STING keeps THIS model's project state (_data/coord: issue register,
        /// escalation record, ACC settings, counters). A cloud model or a workshared local copy
        /// whose root resolves per user would split that state between people, silently. This
        /// row makes the resolution visible before anyone relies on it. Prompts are suppressed:
        /// a self-check must not pop a folder picker.
        /// </summary>
        private static AccCheckResult ProjectFolderRow(Document doc)
        {
            const string id = "0.1", title = "Project folder (shared project state)";
            try
            {
                string root;
                using (CloudProjectRootResolver.SuppressPrompts()) root = ProjectFolderEngine.GetRootPath(doc);
                bool cloud = CloudProjectRootResolver.IsCloud(doc);
                if (string.IsNullOrEmpty(root))
                {
                    string why = cloud ? (CloudProjectRootResolver.LastDecision(SafePath(doc))?.Reason ?? "no mapping for this cloud project")
                                       : "the model has no resolvable project folder (unsaved, or its central model is unreachable)";
                    return new AccCheckResult
                    {
                        Id = id, Title = title, Status = AccCheckStatus.Fail, Detail = why,
                        Remedy = "BIM tab > Cloud Project Root: choose the shared folder for this project (a network share or " +
                                 "synced folder every team member can reach).",
                    };
                }
                if (cloud)
                    return new AccCheckResult { Id = id, Title = title, Status = AccCheckStatus.Pass, Detail = $"cloud model; shared root from the project mapping: {root}" };

                string central = null;
                bool localCopy = false;
                try
                {
                    if (doc.IsWorkshared)
                    {
                        var cmp = doc.GetWorksharingCentralModelPath();
                        central = cmp == null ? null : ModelPathUtils.ConvertModelPathToUserVisiblePath(cmp);
                        localCopy = WorksharedProjectRoot.IsLocalCopy(doc.PathName, central);
                    }
                }
                catch (Exception ex) { StingLog.Warn("ACC_SelfCheck central path: " + ex.Message); }

                if (localCopy)
                {
                    string centralDir = WorksharedProjectRoot.CentralDirOf(central) ?? "";
                    bool shared = centralDir.Length > 0 && root.StartsWith(centralDir, StringComparison.OrdinalIgnoreCase);
                    return shared
                        ? new AccCheckResult { Id = id, Title = title, Status = AccCheckStatus.Pass, Detail = $"workshared local copy; shared root beside the central model: {root}" }
                        : new AccCheckResult
                        {
                            Id = id, Title = title, Status = AccCheckStatus.Warn,
                            Detail = $"workshared local copy, but project state is kept PER USER at {root} (central: {central}).",
                            Remedy = "BIM tab > Cloud Project Root > Move to the shared root (consented move; nothing is deleted).",
                        };
                }
                return new AccCheckResult { Id = id, Title = title, Status = AccCheckStatus.Pass, Detail = $"local model; project root {root}" };
            }
            catch (Exception ex)
            {
                return new AccCheckResult { Id = id, Title = title, Status = AccCheckStatus.Fail, Detail = "could not resolve: " + ex.Message };
            }
        }

        private static string SafePath(Document doc) { try { return doc.PathName; } catch { return null; } }

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var policy = AccProjectSettingsFile.LoadFor(doc, "ACC_SelfCheck");
            var creds = AccProjectSettingsFile.LoadCredentials(doc, "ACC_SelfCheck");

            System.Collections.Generic.List<AccCheckResult> results;
            try
            {
                // Network only, no Revit API inside: safe to block on from the API thread.
                results = AccSelfCheck.RunAsync(creds, policy, DateTime.UtcNow).GetAwaiter().GetResult();
                results.Insert(0, ProjectFolderRow(doc));
            }
            catch (Exception ex)
            {
                StingLog.Error("ACC_SelfCheck", ex);
                AccPullClashesCommand.Report(policy, Title, "The self-check could not run: " + ex.Message);
                return Result.Failed;
            }

            string header = $"STING ACC self-check — {DateTime.Now:yyyy-MM-dd HH:mm}\n" +
                            $"Model: {SafeTitle(doc)}\n" +
                            $"Build: {typeof(AccSelfCheckCommand).Assembly.Location}\n" +
                            $"Settings: {policy.DescribeSource()}";
            string text = AccSelfCheck.Format(results, header);

            string path = null;
            try
            {
                path = OutputLocationHelper.GetRoutedTimestampedPath(doc, "Issue", "STING_ACC_SelfCheck", ".txt");
                File.WriteAllText(path, text, Encoding.UTF8);
            }
            catch (Exception ex) { StingLog.Warn("ACC_SelfCheck report: " + ex.Message); path = null; }

            bool failed = AccSelfCheck.AnyFail(results);
            foreach (var r in results)
            {
                string line = $"ACC_SelfCheck {r}";
                if (r.Status == AccCheckStatus.Fail) StingLog.Warn(line); else StingLog.Info(line);
            }

            if (policy.MayPrompt)
            {
                var sb = new StringBuilder();
                foreach (var r in results)
                {
                    sb.AppendLine($"{Glyph(r.Status)} {r.Id}  {r.Title}");
                    if (r.Status != AccCheckStatus.Pass && !string.IsNullOrEmpty(r.Detail)) sb.AppendLine("      " + r.Detail);
                    if (r.Status == AccCheckStatus.Fail && !string.IsNullOrEmpty(r.Remedy)) sb.AppendLine("      → " + r.Remedy);
                }
                sb.AppendLine();
                sb.AppendLine(path != null ? "Full report (every detail and remedy): " + path
                                           : "The report file could not be written — see StingTools_yyyyMMdd.log.");
                sb.AppendLine("Read-only: nothing was created, uploaded or changed in ACC.");
                new TaskDialog(Title)
                {
                    MainInstruction = AccSelfCheck.Summary(results),
                    MainContent = sb.ToString(),
                }.Show();
            }
            else StingLog.Info($"{Title}: {AccSelfCheck.Summary(results)}" + (path != null ? " — report " + path : ""));

            return failed ? Result.Failed : Result.Succeeded;
        }

        private static string Glyph(AccCheckStatus s) => s switch
        {
            AccCheckStatus.Pass => "✔ PASS",
            AccCheckStatus.Warn => "⚠ WARN",
            AccCheckStatus.Fail => "✖ FAIL",
            _ => "– SKIP",
        };

        private static string SafeTitle(Document doc)
        {
            try { return doc.Title; }
            catch (Exception ex) { StingLog.Warn("ACC_SelfCheck title: " + ex.Message); return "(unknown)"; }
        }
    }
}
