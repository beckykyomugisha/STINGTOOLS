// The plugin half of planscape:// link handling. See PlanscapeProtocol.cs for
// the whole flow and why the handler is a separate .exe.
//
// This is an IIdlingJob rather than a FileSystemWatcher on purpose. A watcher
// fires on a threadpool thread, and everything it would want to do — open the
// Coordination Center, read the document — must happen on Revit's API thread
// anyway. That means a watcher's only real job would be to push into a queue
// that an Idling job then drains, so the watcher is a thread with no work.
// Polling a directory once a second costs a directory enumeration of an almost
// always empty folder.
//
// The job never completes (Execute always returns false), so it stays queued for
// the life of the session. That is what IIdlingJob's contract means by "still
// has work to do".

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace StingTools.Core
{
    internal sealed class PlanscapeLinkWatcher : IIdlingJob
    {
        /// <summary>Idling fires 10–100×/s. Checking the inbox that often would be absurd.</summary>
        private const int PollIntervalMs = 1000;

        private readonly Stopwatch _sinceLastPoll = Stopwatch.StartNew();
        private bool _firstPoll = true;

        public string Name => "PlanscapeLinkWatcher";

        /// <summary>Lowest priority — a queued link can wait behind a compliance scan.</summary>
        public int Priority => 5;

        public int BudgetMs => 5;

        public bool Execute(UIApplication uiApp)
        {
            try
            {
                if (!_firstPoll && _sinceLastPoll.ElapsedMilliseconds < PollIntervalMs)
                    return false;
                _firstPoll = false;
                _sinceLastPoll.Restart();

                List<PlanscapeLink> links = PlanscapeProtocol.TakePending();
                if (links.Count == 0) return false;

                // More than one queued link means the user clicked several while
                // Revit was busy or shut. Acting on all of them would open and
                // re-open the same window; the newest is the one they meant.
                if (links.Count > 1)
                    StingLog.Info($"PlanscapeLink: {links.Count} queued links; handling the newest and discarding the rest.");

                Handle(uiApp, links[links.Count - 1]);
            }
            catch (Exception ex)
            {
                // A throw here would be raised on every Idling tick. Log once per
                // occurrence and keep the job alive.
                StingLog.Warn($"PlanscapeLinkWatcher: {ex.Message}");
            }
            return false; // never done
        }

        private static void Handle(UIApplication uiApp, PlanscapeLink link)
        {
            StingLog.Info($"PlanscapeLink received: {link.Raw}");

            Document doc = uiApp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                // Shouldn't happen — the scheduler only ticks with a document
                // open — but the link is already consumed at this point, so say
                // so rather than dropping it in silence.
                TaskDialog.Show("Planscape link",
                    $"Open a model first — the link ({link}) needs a project to open the Coordination Center against.");
                return;
            }

            if (!ConfirmProjectMatch(doc, link)) return;

            string tab = TabFor(link.Kind);
            if (!BIMCoordinationCenterCommand.ShowFor(uiApp, tab, out string error))
            {
                TaskDialog.Show("Planscape link",
                    $"Could not open the Coordination Center for {link}.\n\n{error}");
                return;
            }

            // Honest about the limit: the tab opens, the individual row is not
            // selected. Resolving an issue GUID or a deliverable code to a row
            // means reaching into the BCC's grids, which is a larger change than
            // this slice took on — see docs/PLANSCAPE_PROTOCOL.md.
            if (!string.IsNullOrEmpty(link.Target) && link.Kind != "dashboard")
                StingLog.Info($"PlanscapeLink: opened the {tab} tab; target '{link.Target}' is not row-selected yet.");
        }

        /// <summary>
        /// A <c>dashboard</c> link names the project it was shared from. Opening
        /// a different project's coordination data because the recipient happened
        /// to have another model open would be quietly wrong, so ask.
        /// Non-dashboard links carry an id, not a project, and skip this.
        /// </summary>
        private static bool ConfirmProjectMatch(Document doc, PlanscapeLink link)
        {
            if (link.Kind != "dashboard" || string.IsNullOrWhiteSpace(link.Target)) return true;

            string open = ResolveProjectName(doc);
            if (string.IsNullOrWhiteSpace(open)) return true; // nothing to compare against
            if (string.Equals(open.Trim(), link.Target.Trim(), StringComparison.OrdinalIgnoreCase)) return true;

            var td = new TaskDialog("Planscape link")
            {
                MainInstruction = "This link is for a different project.",
                MainContent = $"The link points at “{link.Target}”, but the open model is “{open}”.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
                FooterText = $"Open the Coordination Center for “{open}” anyway?"
            };
            return td.Show() == TaskDialogResult.Yes;
        }

        private static string ResolveProjectName(Document doc)
        {
            try
            {
                // The share link is built from the same project name the BCC
                // shows, which comes from ProjectInformation. Fall back to the
                // file title, which is what a user recognises.
                string name = doc.ProjectInformation?.Name;
                if (!string.IsNullOrWhiteSpace(name)) return name;
                return doc.Title;
            }
            catch (Exception) { return null; }
        }

        private static string TabFor(string kind)
        {
            switch (kind)
            {
                case "issue": return "ISSUES";
                case "deliverable": return "DELIVERABLES";
                case "warning": return "WARNINGS";
                case "meeting": return "MEETINGS";
                case "dashboard":
                default: return "OVERVIEW";
            }
        }
    }
}
