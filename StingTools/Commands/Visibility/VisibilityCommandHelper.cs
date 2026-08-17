// StingTools — Visibility Center · shared command helper
//
// Split out of VisibilityCommands.cs, which had outgrown the 400-line rule. Everything the
// eight Vis_* commands share lives here: app/view resolution, target-view fan-out, the
// report dialog, and the plan+apply sequence with its transaction rules.
//
// Transaction rules that are easy to get wrong and are therefore centralised here:
//   · EVERYTHING here needs a transaction, temporary hide/isolate included. They modify the
//     View element; "temporary" means the state is not saved with the document, NOT that it
//     is transaction-free. Believing otherwise made Apply fail outright with "Attempt to
//     modify the model outside of transaction".
//   · VisibilityEngine.Reset opens its own — do not wrap it.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Visibility;

namespace StingTools.Commands.Visibility
{
    internal static class VisibilityCommandHelper
    {
        internal const string Title = "STING Visibility";

        /// <summary>
        /// The live <see cref="UIApplication"/>, however this command was launched.
        /// <para><b>ExternalCommandData is null on every panel and Hub dispatch.</b>
        /// <c>StingCommandHandler.RunCommand&lt;T&gt;</c> calls <c>Execute(null, …)</c> on
        /// purpose — see its comment at StingCommandHandler.cs:4417 — because building a real
        /// ExternalCommandData needed a reflection hack that broke across Revit versions.
        /// Commands are expected to fall back to <c>StingCommandHandler.CurrentApp</c>, which
        /// 53 other files in this repo already do. Reading <c>cmd.Application</c> alone makes a
        /// command work when invoked from a ribbon PushButton and fail with a misleading
        /// "No active view." from the dock panel or the Hub.</para>
        /// </summary>
        internal static UIApplication ResolveApp(ExternalCommandData cmd)
            => cmd?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp;

        /// <summary>Active view, or null with a message already shown.</summary>
        internal static View ActiveView(ExternalCommandData cmd, out Document doc)
        {
            doc = null;

            // Distinguish "no Revit context" from "no view open". Collapsing both into
            // "No active view." is what made the null-ExternalCommandData dispatch bug read
            // as a view problem while a view was plainly open on screen.
            var app = ResolveApp(cmd);
            if (app == null)
            {
                StingLog.Error("VisibilityCommandHelper.ActiveView: no UIApplication — " +
                               "ExternalCommandData was null and StingCommandHandler.CurrentApp " +
                               "was never set.", null);
                TaskDialog.Show(Title, "No Revit application context — the STING command handler " +
                                       "has not been initialised yet. Open the STING panel once, " +
                                       "then try again.");
                return null;
            }

            var uidoc = app.ActiveUIDocument;
            if (uidoc?.ActiveView == null)
            {
                TaskDialog.Show(Title, "No active view. Open a model view and try again.");
                return null;
            }
            doc = uidoc.Document;
            return uidoc.ActiveView;
        }

        /// <summary>Show a result with its blockers in the expandable section.</summary>
        internal static void Report(string heading, VisibilityResult result)
        {
            var dlg = new TaskDialog(Title)
            {
                MainInstruction = heading,
                MainContent = result.Summary()
            };
            if (result.Blockers != null && result.Blockers.Count > 0)
            {
                dlg.ExpandedContent = string.Join(Environment.NewLine + Environment.NewLine,
                                                  result.Blockers.Distinct());
                dlg.MainContent += $"\n\n{result.Blockers.Distinct().Count()} thing(s) need your attention — expand for detail.";
            }
            dlg.Show();
        }

        /// <summary>Views the current target resolves to. Always at least the active view.</summary>
        internal static List<View> ResolveTargets(Document doc, View active, VisibilityTarget target)
        {
            var views = new List<View> { active };
            try
            {
                if (target == VisibilityTarget.AllViewsOnSheet)
                {
                    var sheet = active as ViewSheet
                                ?? new FilteredElementCollector(doc).OfClass(typeof(ViewSheet))
                                    .Cast<ViewSheet>()
                                    .FirstOrDefault(s => s.GetAllPlacedViews().Contains(active.Id));
                    if (sheet != null)
                    {
                        var placed = sheet.GetAllPlacedViews()
                            .Select(id => doc.GetElement(id) as View)
                            .Where(v => v != null && !v.IsTemplate)
                            .ToList();
                        if (placed.Count > 0) views = placed;
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"VisibilityCommandHelper.ResolveTargets: {ex.Message}");
            }
            return views;
        }

        /// <summary>
        /// Plan + apply the session's current set.
        /// <para><paramref name="forceAction"/> is null for Apply, which respects whatever the
        /// set already carries — the dropdown's Apply button snapshots Hide rules, and a loaded
        /// preset carries its own intent. Forcing Hide here would silently invert a ShowOnly
        /// preset ("MEP only" would hide all MEP). Isolate passes ShowOnly explicitly, because
        /// that button *is* the user stating the action.</para>
        /// </summary>
        /// <param name="quiet">
        /// True for the live-apply path. Live apply fires on every tick, so a report dialog per
        /// apply would make the feature unusable — the footer and badge already say what
        /// happened. Blockers are still logged, and still surface on the next explicit Apply.
        /// </param>
        internal static Result Run(ExternalCommandData cmd, VisibilityAction? forceAction, bool quiet = false)
        {
            Document doc;
            var view = ActiveView(cmd, out doc);
            if (view == null) return Result.Cancelled;

            var set = VisibilitySession.Current;

            // An empty rule set is NOT necessarily nothing to do: re-ticking every category
            // produces zero hide rules but a full VisibleCategoryIds list, and that apply is
            // exactly how a user un-hides. Only bail when there is genuinely nothing either way.
            bool hasRestoreWork = set.VisibleCategoryIds != null && set.VisibleCategoryIds.Count > 0;
            if ((set.Rules == null || set.Rules.Count == 0) && !hasRestoreWork)
            {
                TaskDialog.Show(Title,
                    "Nothing is selected yet.\n\nOpen 'Show / Hide' on the SELECT tab, tick the " +
                    "categories or tag values you want to act on, then apply.");
                return Result.Cancelled;
            }

            if (forceAction.HasValue)
                foreach (var r in set.Rules) if (r != null) r.Action = forceAction.Value;

            var plan = VisibilityEngine.Plan(doc, view, set);
            if (plan.IsRejected)
            {
                TaskDialog.Show(Title, plan.RejectReason);
                return Result.Cancelled;
            }

            VisibilityResult result;
            var targets = ResolveTargets(doc, view, set.Target);

            if (set.Mode == VisibilityMode.Temporary)
            {
                // WRONG BELIEF, CORRECTED: this used to run with no transaction, on the claim
                // that "temporary view modes are not transactional". They are.
                // HideElementsTemporary / IsolateElementsTemporary modify the View element, so
                // Revit throws "Attempt to modify the model outside of transaction" without one.
                // The temporary state is not saved with the document, which is what makes it
                // *temporary* — that is a different thing from not needing a transaction.
                // One transaction spans every target view so a multi-view apply is atomic.
                result = new VisibilityResult { Ok = true };
                using (var t = new Transaction(doc, "STING Visibility — temporary"))
                {
                t.Start();
                foreach (var v in targets)
                {
                    var r = VisibilityEngine.Apply(doc, v, plan);
                    if (r.Ok) { result.ViewsAffected++; result.ElementsAffected = Math.Max(result.ElementsAffected, r.ElementsAffected); }
                    else if (string.IsNullOrEmpty(result.Error)) result.Error = r.Error;
                    foreach (var b in r.Blockers) if (!result.Blockers.Contains(b)) result.Blockers.Add(b);
                }
                result.Ok = result.ViewsAffected > 0;
                if (result.Ok) t.Commit(); else t.RollBack();
                }
            }
            else if (targets.Count > 1)
            {
                result = VisibilityEngine.ApplyToViews(doc, targets, plan);
            }
            else
            {
                using (var t = new Transaction(doc, "STING Visibility"))
                {
                    t.Start();
                    result = VisibilityEngine.Apply(doc, view, plan);
                    if (result.Ok) t.Commit(); else t.RollBack();
                }
            }

            // Re-read the view so the SELECT-tab badge reflects what just happened. The
            // harvest cache is stale by definition here — the model changed — so Refresh
            // invalidates it first.
            StingTools.UI.VisibilityCenter.VisibilityBadge.Refresh(doc, view);

            if (!quiet) Report(plan.IsIsolate ? "Isolated" : "Hidden", result);
            else if (result.Blockers.Count > 0)
                StingLog.Info("Vis live apply blockers: " + string.Join(" | ", result.Blockers.Distinct()));
            return result.Ok ? Result.Succeeded : Result.Failed;
        }
    }
}
