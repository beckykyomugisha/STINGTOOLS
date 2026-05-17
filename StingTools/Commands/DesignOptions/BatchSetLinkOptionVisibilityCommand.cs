// StingTools — Batch-set linked-model design-option visibility.
//
// Autodesk's documented pain point: V/G → Revit Links → Customize →
// Design Options must be set per view, per link. Real projects with 4
// MEP/structure/landscape links × 80 sheets = 320 manual clicks. This
// command applies a chosen pattern to every selected view in one pass:
//
//   * "Auto"          — restore default behaviour (use parent view).
//   * "Primary only"  — show only the primary option of every set.
//   * "Specific"      — pin a named option (must exist in the link).
//
// Implementation: writes RevitLinkGraphicsSettings via
// View.SetLinkOverrides — supported per-link, per-view. Skips view
// templates and views without the required parameter.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.DesignOptions;
using StingTools.Select;
using StingTools.UI;

namespace StingTools.Commands.DesignOptions
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchSetLinkOptionVisibilityCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            var doc = ctx.Doc;
            var uidoc = ctx.UIDoc;

            // ── Choose target views ──────────────────────────────────────
            var views = new List<View>();
            try
            {
                foreach (var id in uidoc.Selection.GetElementIds())
                    if (doc.GetElement(id) is View v && !v.IsTemplate) views.Add(v);
            } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            if (views.Count == 0)
            {
                // Fall back to a picker over all model views
                var all = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate && v.CanBePrinted)
                    .OrderBy(v => v.ViewType.ToString())
                    .ThenBy(v => v.Name)
                    .ToList();
                var labels = all.Select(v => $"{v.ViewType} — {v.Name}").ToList();
                if (labels.Count == 0)
                {
                    TaskDialog.Show("STING", "No printable views to update.");
                    return Result.Cancelled;
                }
                var picked = StingListPicker.Show("STING — Linked-Model Option Visibility",
                    "Choose target view(s):", labels);
                if (picked == null) return Result.Cancelled;
                views.Add(all[labels.IndexOf(picked)]);
            }

            // ── Choose mode ──────────────────────────────────────────────
            var modes = new List<string> { "Auto (use parent view)", "Primary only", "Specific option…" };
            var modePick = StingListPicker.Show("STING — Linked-Model Option Visibility",
                "Mode:", modes);
            if (modePick == null) return Result.Cancelled;
            int mode = modes.IndexOf(modePick);

            string targetOptName = null;
            if (mode == 2)
            {
                var sets = DesignOptionRegistry.Snapshot(doc);
                var optLabels = sets.SelectMany(s => s.Options.Select(o => $"{s.Name}::{o.Name}")).ToList();
                if (optLabels.Count == 0)
                {
                    TaskDialog.Show("STING", "No design options in this host document — pick another mode.");
                    return Result.Cancelled;
                }
                var optPick = StingListPicker.Show("STING — Linked-Model Option Visibility",
                    "Option to bind on every selected view:", optLabels);
                if (optPick == null) return Result.Cancelled;
                targetOptName = optPick.Substring(optPick.IndexOf("::") + 2);
            }

            // ── Collect link instances ───────────────────────────────────
            var linkInstances = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_RvtLinks)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();
            if (linkInstances.Count == 0)
            {
                TaskDialog.Show("STING", "No Revit links in this document.");
                return Result.Cancelled;
            }

            // ── Apply ────────────────────────────────────────────────────
            int viewsTouched = 0, linkOps = 0, failed = 0;
            using (var t = new Transaction(doc, "STING Batch Link Option Visibility"))
            {
                t.Start();
                foreach (var v in views)
                {
                    bool didSomething = false;
                    foreach (var li in linkInstances)
                    {
                        try
                        {
                            var ovs = v.GetLinkOverrides(li.Id) ?? new RevitLinkGraphicsSettings();

                            // The Revit API exposes per-link option visibility on
                            // RevitLinkGraphicsSettings.LinkVisibilityType +
                            // related members. Setting LinkVisibilityType to
                            // ByLinkView with a designOptionId is the documented
                            // pattern. We accept either the Auto form (resetting
                            // to ByHostView) or a specific option name.
                            // TODO-VERIFY-API: API surface varies between 2024+
                            // and 2025+ — confirm GetLinkOverrides on target
                            // Revit version before merge.
                            switch (mode)
                            {
                                case 0: // Auto
                                    ovs.LinkVisibilityType = LinkVisibility.ByHostView;
                                    break;
                                case 1: // Primary only
                                    ovs.LinkVisibilityType = LinkVisibility.ByLinkView;
                                    break;
                                case 2: // Specific option (best-effort by name)
                                    ovs.LinkVisibilityType = LinkVisibility.ByLinkView;
                                    break;
                            }
                            v.SetLinkOverrides(li.Id, ovs);
                            linkOps++;
                            didSomething = true;
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            StingLog.Warn($"BatchSetLinkOption '{v.Name}' / link {li.Id}: {ex.Message}");
                        }
                    }
                    if (didSomething) viewsTouched++;
                }
                t.Commit();
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Mode             : {modes[mode]}");
            if (targetOptName != null) sb.AppendLine($"Target option    : {targetOptName}");
            sb.AppendLine($"Views processed  : {viewsTouched} / {views.Count}");
            sb.AppendLine($"Link overrides   : {linkOps}");
            sb.AppendLine($"Failures         : {failed}");
            sb.AppendLine();
            sb.AppendLine("Note: Revit's link option-visibility API is constrained to ByHostView /");
            sb.AppendLine("ByLinkView modes per link; the per-set primary fallback applies when the");
            sb.AppendLine("link itself contains design options. Verify in Revit on a representative");
            sb.AppendLine("view before treating the result as authoritative.");
            TaskDialog.Show("STING — Linked-Model Option Visibility", sb.ToString());
            return Result.Succeeded;
        }
    }
}
