// StingTools — Visibility Center · commands
//
// Thin shells. All decision-making lives in VisibilityEngine / VisibilityRuleMatcher;
// these resolve the target view(s), own the transaction, and report.
//
// Transaction rules that are easy to get wrong and are therefore centralised here:
//   · Temporary hide/isolate must run with NO open transaction (Revit throws inside one).
//   · View filters need one.
//   · VisibilityEngine.Reset sequences both halves itself — do not wrap it.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
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
        internal static Result Run(ExternalCommandData cmd, VisibilityAction? forceAction)
        {
            Document doc;
            var view = ActiveView(cmd, out doc);
            if (view == null) return Result.Cancelled;

            var set = VisibilitySession.Current;
            if (set.Rules == null || set.Rules.Count == 0)
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
                // No transaction — temporary view modes are not transactional.
                result = new VisibilityResult { Ok = true };
                foreach (var v in targets)
                {
                    var r = VisibilityEngine.Apply(doc, v, plan);
                    if (r.Ok) { result.ViewsAffected++; result.ElementsAffected = Math.Max(result.ElementsAffected, r.ElementsAffected); }
                    else if (string.IsNullOrEmpty(result.Error)) result.Error = r.Error;
                    foreach (var b in r.Blockers) if (!result.Blockers.Contains(b)) result.Blockers.Add(b);
                }
                result.Ok = result.ViewsAffected > 0;
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

            Report(plan.IsIsolate ? "Isolated" : "Hidden", result);
            return result.Ok ? Result.Succeeded : Result.Failed;
        }
    }

    /// <summary>Opens the visibility dropdown. Runs on the Revit API thread because the
    /// popup's contents come from a FilteredElementCollector pass; it hands off to the panel's
    /// dispatcher to actually show the popup.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class OpenVisibilityDropdownCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            try
            {
                StingTools.UI.VisibilityCenter.VisibilityDropdownHost.ShowWindow(
                    VisibilityCommandHelper.ResolveApp(cmd));
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("OpenVisibilityDropdownCommand", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>Apply the current set as it stands — Hide rules hide, ShowOnly rules isolate.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ApplyVisibilityCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els) =>
            VisibilityCommandHelper.Run(cmd, null);   // respect the set's own action
    }

    /// <summary>Show only what the current set matches.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class IsolateVisibilityCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els) =>
            VisibilityCommandHelper.Run(cmd, VisibilityAction.ShowOnly);
    }

    /// <summary>Clear BOTH mechanisms on the active view — temporary hide and STING filters.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ResetVisibilityCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            Document doc;
            var view = VisibilityCommandHelper.ActiveView(cmd, out doc);
            if (view == null) return Result.Cancelled;

            // Reset opens its own transaction for the filter half — do not wrap it.
            var result = VisibilityEngine.Reset(doc, view, VisibilityMode.Temporary);

            var dlg = new TaskDialog(VisibilityCommandHelper.Title)
            {
                MainInstruction = "Visibility reset",
                MainContent = result.FiltersReused > 0 || result.ElementsAffected > 0
                    ? $"Cleared the temporary hide/isolate and removed {result.FiltersReused} STING filter(s) from '{view.Name}'."
                    : $"'{view.Name}' had no STING visibility state to clear."
            };
            if (result.Blockers.Count > 0)
                dlg.ExpandedContent = string.Join(Environment.NewLine, result.Blockers.Distinct());
            dlg.Show();

            return Result.Succeeded;
        }
    }

    /// <summary>Delete every "STING VIS - " ParameterFilterElement in the project.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PurgeVisibilityFiltersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            Document doc;
            var view = VisibilityCommandHelper.ActiveView(cmd, out doc);
            if (view == null) return Result.Cancelled;

            var doomed = new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>()
                .Where(f => VisibilityRuleMatcher.IsStingVisibilityFilter(f.Name))
                .ToList();

            if (doomed.Count == 0)
            {
                TaskDialog.Show(VisibilityCommandHelper.Title,
                    $"No '{VisibilityRuleMatcher.FilterPrefix}' filters exist in this project — nothing to purge.");
                return Result.Succeeded;
            }

            var confirm = new TaskDialog(VisibilityCommandHelper.Title)
            {
                MainInstruction = $"Delete {doomed.Count} STING visibility filter(s)?",
                MainContent = "They will be removed from every view that uses them. " +
                              "Filters you created yourself are not touched.",
                ExpandedContent = string.Join(Environment.NewLine, doomed.Select(f => f.Name).OrderBy(n => n)),
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };
            if (confirm.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            int deleted = 0;
            using (var t = new Transaction(doc, "STING Visibility — purge filters"))
            {
                t.Start();
                foreach (var f in doomed)
                {
                    try { doc.Delete(f.Id); deleted++; }
                    catch (Exception ex) { StingLog.Error($"Purge filter '{f.Name}'", ex); }
                }
                t.Commit();
            }

            TaskDialog.Show(VisibilityCommandHelper.Title,
                $"Deleted {deleted} of {doomed.Count} STING visibility filter(s).");
            return Result.Succeeded;
        }
    }

    /// <summary>Push the current set onto the active view's view template, so it applies to
    /// every view that template controls — the answer to the "locked by template" blocker.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ApplyVisibilityToTemplateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            Document doc;
            var view = VisibilityCommandHelper.ActiveView(cmd, out doc);
            if (view == null) return Result.Cancelled;

            if (view.ViewTemplateId == null || view.ViewTemplateId == ElementId.InvalidElementId)
            {
                TaskDialog.Show(VisibilityCommandHelper.Title,
                    $"'{view.Name}' has no view template, so there is nothing to push to.\n\n" +
                    "Assign a view template first, or apply to the view directly.");
                return Result.Cancelled;
            }

            var tpl = doc.GetElement(view.ViewTemplateId) as View;
            if (tpl == null) { TaskDialog.Show(VisibilityCommandHelper.Title, "Could not read the view template."); return Result.Failed; }

            var set = VisibilitySession.Current;
            if (set.Rules == null || set.Rules.Count == 0)
            {
                TaskDialog.Show(VisibilityCommandHelper.Title, "Nothing is selected yet.");
                return Result.Cancelled;
            }

            // A template can only carry saved filters — temporary modes are per-view session state.
            set.Mode = VisibilityMode.ViewFilter;
            var plan = VisibilityEngine.Plan(doc, view, set);
            if (plan.IsRejected) { TaskDialog.Show(VisibilityCommandHelper.Title, plan.RejectReason); return Result.Cancelled; }

            VisibilityResult result;
            using (var t = new Transaction(doc, "STING Visibility — apply to template"))
            {
                t.Start();
                result = VisibilityEngine.Apply(doc, tpl, plan);
                if (result.Ok) t.Commit(); else t.RollBack();
            }

            VisibilityCommandHelper.Report($"Applied to template '{tpl.Name}'", result);
            return result.Ok ? Result.Succeeded : Result.Failed;
        }
    }

    /// <summary>Save the current set as a named project preset.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SaveVisibilityPresetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            Document doc;
            var view = VisibilityCommandHelper.ActiveView(cmd, out doc);
            if (view == null) return Result.Cancelled;

            var set = VisibilitySession.Current;
            if (set.Rules == null || set.Rules.Count == 0)
            {
                TaskDialog.Show(VisibilityCommandHelper.Title, "Nothing is selected yet — there is no set to save.");
                return Result.Cancelled;
            }

            string name = StingTools.UI.VisibilityCenter.VisibilityDropdownHost.PromptForPresetName();
            if (string.IsNullOrWhiteSpace(name)) return Result.Cancelled;

            var toSave = new VisibilitySet
            {
                Name = name.Trim(),
                Mode = set.Mode,
                Target = set.Target,
                Origin = "project",
                Rules = set.Rules
            };

            var warnings = new List<string>();
            bool ok = VisibilitySession.SavePreset(doc, toSave, warnings);

            TaskDialog.Show(VisibilityCommandHelper.Title,
                ok ? $"Saved preset '{toSave.Name}'."
                   : "Could not save the preset.\n\n" + string.Join(Environment.NewLine, warnings));
            return ok ? Result.Succeeded : Result.Failed;
        }
    }

    /// <summary>Pick a preset (corporate baseline + project overrides) and make it current.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LoadVisibilityPresetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            Document doc;
            var view = VisibilityCommandHelper.ActiveView(cmd, out doc);
            if (view == null) return Result.Cancelled;

            var warnings = new List<string>();
            var presets = VisibilitySession.LoadPresets(doc, warnings);
            if (presets.Count == 0)
            {
                TaskDialog.Show(VisibilityCommandHelper.Title,
                    "No visibility presets found.\n\n" + string.Join(Environment.NewLine, warnings));
                return Result.Cancelled;
            }

            var chosen = StingTools.UI.VisibilityCenter.VisibilityDropdownHost.PromptForPreset(presets);
            if (chosen == null) return Result.Cancelled;

            VisibilityEngine.ResolveCategoryRules(doc, chosen);
            VisibilitySession.Current = chosen;

            var plan = VisibilityEngine.Plan(doc, view, chosen);
            TaskDialog.Show(VisibilityCommandHelper.Title,
                $"Loaded preset '{chosen.Name}'.\n\n{plan.Summary()}\n\n" +
                "Use Apply or Isolate on the SELECT tab to commit it.");
            return Result.Succeeded;
        }
    }
}
