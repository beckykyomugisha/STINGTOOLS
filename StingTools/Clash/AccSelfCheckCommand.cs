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
