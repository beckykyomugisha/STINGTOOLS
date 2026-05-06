// StingTools — Lock View(s) to a Design Option.
//
// Sets BuiltInParameter.VIEWER_OPTION_VISIBILITY on the active view (or
// every selected view) to a chosen option, or to InvalidElementId for
// "<Automatic>". This is the only documented way to programmatically
// constrain a view to a single option's visibility.

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
    public class LockViewToOptionCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            var doc = ctx.Doc;
            var uidoc = ctx.UIDoc;

            // Scope: selected views (if user picked from project browser) else active view.
            var targets = new List<View>();
            try
            {
                foreach (var id in uidoc.Selection.GetElementIds())
                {
                    if (doc.GetElement(id) is View v && !v.IsTemplate) targets.Add(v);
                }
            } catch { }
            if (targets.Count == 0 && uidoc.ActiveView != null && !uidoc.ActiveView.IsTemplate)
                targets.Add(uidoc.ActiveView);

            if (targets.Count == 0)
            {
                TaskDialog.Show("STING — Lock View to Option",
                    "Select view(s) in the Project Browser or open a view first.");
                return Result.Cancelled;
            }

            // ── Pick option (or Automatic) ───────────────────────────────
            var sets = DesignOptionRegistry.Snapshot(doc);
            var labels = new List<string> { "<Automatic — show primary of every set>" };
            var ids = new List<ElementId> { ElementId.InvalidElementId };
            foreach (var s in sets)
            foreach (var o in s.Options)
            {
                labels.Add($"{s.Name}  →  {o.Name}{(o.IsPrimary ? "  (primary)" : "")}");
                ids.Add(o.OptionId);
            }
            string picked = StingListPicker.Show("STING — Lock View to Option",
                $"Lock {targets.Count} view(s) to which option?", labels);
            int pick = picked == null ? -1 : labels.IndexOf(picked);
            if (pick < 0) return Result.Cancelled;

            var targetId = ids[pick];
            string label = labels[pick];

            int ok = 0, skipped = 0;
            var fail = new List<string>();
            using (var t = new Transaction(doc, "STING Lock View to Option"))
            {
                t.Start();
                foreach (var v in targets)
                {
                    try
                    {
                        var p = v.get_Parameter(BuiltInParameter.VIEWER_OPTION_VISIBILITY);
                        if (p == null) { skipped++; continue; }
                        p.Set(targetId);
                        ok++;
                    }
                    catch (Exception ex) { fail.Add($"{v.Name}: {ex.Message}"); }
                }
                t.Commit();
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Target  : {label}");
            sb.AppendLine($"Locked  : {ok}");
            sb.AppendLine($"Skipped : {skipped}");
            sb.AppendLine($"Failed  : {fail.Count}");
            foreach (var f in fail.Take(10)) sb.AppendLine("  · " + f);
            TaskDialog.Show("STING — Lock View to Option", sb.ToString());
            return ok > 0 ? Result.Succeeded : Result.Failed;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ResetViewOptionVisibilityCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) return Result.Failed;
            var doc = ctx.Doc; var uidoc = ctx.UIDoc;

            var targets = new List<View>();
            try
            {
                foreach (var id in uidoc.Selection.GetElementIds())
                    if (doc.GetElement(id) is View v && !v.IsTemplate) targets.Add(v);
            } catch { }
            if (targets.Count == 0 && uidoc.ActiveView != null && !uidoc.ActiveView.IsTemplate)
                targets.Add(uidoc.ActiveView);
            if (targets.Count == 0) return Result.Cancelled;

            int ok = 0;
            using (var t = new Transaction(doc, "STING Reset View Option Visibility"))
            {
                t.Start();
                foreach (var v in targets)
                {
                    try
                    {
                        var p = v.get_Parameter(BuiltInParameter.VIEWER_OPTION_VISIBILITY);
                        if (p != null) { p.Set(ElementId.InvalidElementId); ok++; }
                    }
                    catch (Exception ex) { StingLog.Warn($"ResetViewOption: {ex.Message}"); }
                }
                t.Commit();
            }
            TaskDialog.Show("STING", $"Reset {ok} view(s) to <Automatic>.");
            return Result.Succeeded;
        }
    }
}
