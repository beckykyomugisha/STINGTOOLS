// StingTools — Visibility Center · commands
//
// Thin shells. All decision-making lives in VisibilityEngine / VisibilityRuleMatcher, and
// everything the eight commands share — view resolution, the transaction rules, the report
// dialog — lives in VisibilityCommandHelper.cs alongside this file.

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
                    VisibilityCommandHelper.ResolveApp(cmd), preferFloating: false);
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

    /// <summary>
    /// Same dropdown, launched from the ribbon Hub or the Quick Access Toolbar. Separate from
    /// <see cref="OpenVisibilityDropdownCommand"/> only so the launch SOURCE picks the
    /// presentation: a click at the top of the screen opens a floating panel under the cursor,
    /// while the SELECT-tab button keeps its anchored popup. Same content either way.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class OpenVisibilityDropdownFloatingCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            try
            {
                StingTools.UI.VisibilityCenter.VisibilityDropdownHost.ShowWindow(
                    VisibilityCommandHelper.ResolveApp(cmd), preferFloating: true);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("OpenVisibilityDropdownFloatingCommand", ex);
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

            // Reset clears BOTH mechanisms, so the badge must now read zero. Proving that on
            // screen is what stops "I hit Reset and it's still hidden" being a support ticket.
            StingTools.UI.VisibilityCenter.VisibilityBadge.Refresh(doc, view);

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

            StingTools.UI.VisibilityCenter.VisibilityBadge.Refresh(doc, view);

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

            string name = StingTools.UI.VisibilityCenter.VisibilityPresetPrompts.PromptForPresetName();
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

            var chosen = StingTools.UI.VisibilityCenter.VisibilityPresetPrompts.PromptForPresets(presets);
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
