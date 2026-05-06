// StingTools — Move-To-Option command.
//
// Moves the user's selection from the main model into a chosen design
// option via Document.AddToDesignOption. Pre-validates against API
// rules (annotation/view-specific/host-without-host categories cannot
// move). Surfaces the workshared "first-edit-owns-the-option" warning.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.DesignOptions;
using StingTools.UI;

namespace StingTools.Commands.DesignOptions
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MoveToOptionCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            var doc = ctx.Doc;
            var uidoc = ctx.UIDoc;

            var selIds = uidoc.Selection.GetElementIds()?.ToList() ?? new List<ElementId>();
            if (selIds.Count == 0)
            {
                TaskDialog.Show("STING — Move to Option", "Select model elements first.");
                return Result.Cancelled;
            }

            // ── Build option list ────────────────────────────────────────
            var sets = DesignOptionRegistry.Snapshot(doc);
            if (sets.Count == 0)
            {
                TaskDialog.Show("STING — Move to Option",
                    "No design option sets in this document.\n\n" +
                    "Create sets and options via Manage → Design Options first.");
                return Result.Failed;
            }

            var labels = new List<string>();
            var ids = new List<ElementId>();
            foreach (var s in sets)
            foreach (var o in s.Options)
            {
                labels.Add($"{s.Name}  →  {o.Name}{(o.IsPrimary ? "  (primary)" : "")}");
                ids.Add(o.OptionId);
            }
            string picked = StingListPicker.Show(
                "STING — Move to Option",
                "Choose a target option for the selected elements:",
                labels);
            int pick = picked == null ? -1 : labels.IndexOf(picked);
            if (pick < 0 || pick >= ids.Count) return Result.Cancelled;

            var targetId = ids[pick];
            string targetLabel = labels[pick];

            // ── Workshared safety prompt ─────────────────────────────────
            if (!OptionContext.ConfirmWorksharingMove(doc, targetLabel))
                return Result.Cancelled;

            // ── Filter elements that can legally move ────────────────────
            var movable = new List<ElementId>();
            var rejected = new List<string>();
            foreach (var id in selIds)
            {
                var el = doc.GetElement(id);
                if (el == null) continue;

                if (el.DesignOption != null)
                {
                    rejected.Add($"{el.Id} {el.Category?.Name}: already in an option");
                    continue;
                }
                if (el.ViewSpecific)
                {
                    rejected.Add($"{el.Id} {el.Category?.Name}: view-specific (annotation)");
                    continue;
                }
                if (!IsModelCategory(el))
                {
                    rejected.Add($"{el.Id} {el.Category?.Name}: not a model category");
                    continue;
                }
                movable.Add(id);
            }

            if (movable.Count == 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("No elements were eligible to move.");
                sb.AppendLine();
                foreach (var r in rejected.Take(20)) sb.AppendLine("  · " + r);
                if (rejected.Count > 20) sb.AppendLine($"  · … +{rejected.Count - 20} more");
                TaskDialog.Show("STING — Move to Option", sb.ToString());
                return Result.Cancelled;
            }

            // ── Execute move ─────────────────────────────────────────────
            int moved = 0;
            var failures = new List<string>();
            using (var t = new Transaction(doc, "STING Move to Design Option"))
            {
                t.Start();
                try
                {
                    doc.AddToDesignOption(targetId, movable);
                    moved = movable.Count;
                }
                catch (Exception ex)
                {
                    // Some elements may have been rejected by AddToDesignOption.
                    // Try one-at-a-time so we can report which failed.
                    StingLog.Warn($"AddToDesignOption batch failed: {ex.Message}");
                    foreach (var id in movable)
                    {
                        try
                        {
                            doc.AddToDesignOption(targetId, new List<ElementId> { id });
                            moved++;
                        }
                        catch (Exception e2)
                        {
                            failures.Add($"{id}: {e2.Message}");
                        }
                    }
                }
                t.Commit();
            }

            DesignOptionRegistry.InvalidateCache(doc);

            // ── Report ───────────────────────────────────────────────────
            var report = new StringBuilder();
            report.AppendLine($"Target  : {targetLabel}");
            report.AppendLine($"Selected: {selIds.Count}");
            report.AppendLine($"Moved   : {moved}");
            report.AppendLine($"Skipped : {rejected.Count}");
            report.AppendLine($"Failed  : {failures.Count}");
            if (failures.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("First failures:");
                foreach (var f in failures.Take(10)) report.AppendLine("  · " + f);
                if (failures.Count > 10) report.AppendLine($"  · … +{failures.Count - 10} more");
            }
            if (rejected.Count > 0 && moved > 0)
            {
                report.AppendLine();
                report.AppendLine("Skipped (first 10):");
                foreach (var r in rejected.Take(10)) report.AppendLine("  · " + r);
            }

            foreach (var f in failures) StingLog.Warn($"MoveToOption failure: {f}");
            TaskDialog.Show("STING — Move to Option", report.ToString());
            return moved > 0 ? Result.Succeeded : Result.Failed;
        }

        private static bool IsModelCategory(Element el)
        {
            try
            {
                var cat = el?.Category;
                if (cat == null) return false;
                return cat.CategoryType == CategoryType.Model;
            }
            catch { return false; }
        }
    }
}
