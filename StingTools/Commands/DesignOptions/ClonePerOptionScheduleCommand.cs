// StingTools — Clone Per-Option Schedule.
//
// Schedules see all design options by default — instant double-counting
// risk on BOQs and quantity reports. This command takes the active
// schedule (or one picked from a list) and clones it once per option in
// a chosen set, then sets each clone's VIEWER_OPTION_VISIBILITY to that
// option. Names follow "<orig> [<set>: <option>]".

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
    public class ClonePerOptionScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            var doc = ctx.Doc;

            // ── Pick source schedule ─────────────────────────────────────
            var schedules = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(s => !s.IsTemplate && !s.IsTitleblockRevisionSchedule)
                .OrderBy(s => s.Name)
                .ToList();
            if (schedules.Count == 0)
            {
                TaskDialog.Show("STING", "No schedules in this document.");
                return Result.Cancelled;
            }
            var schedLabels = schedules.Select(s => s.Name).ToList();
            var schedPicked = StingListPicker.Show("STING — Per-Option Schedule",
                "Choose the source schedule to clone per option:", schedLabels);
            if (schedPicked == null) return Result.Cancelled;
            var src = schedules[schedLabels.IndexOf(schedPicked)];

            // ── Pick target set ──────────────────────────────────────────
            var sets = DesignOptionRegistry.Snapshot(doc);
            if (sets.Count == 0)
            {
                TaskDialog.Show("STING", "No design option sets in this document.");
                return Result.Cancelled;
            }
            var setLabels = sets.Select(s => $"{s.Name}  ({s.Options.Count} options)").ToList();
            var setPicked = StingListPicker.Show("STING — Per-Option Schedule",
                "Choose the option set to clone across:", setLabels);
            if (setPicked == null) return Result.Cancelled;
            var set = sets[setLabels.IndexOf(setPicked)];

            // ── Clone ─────────────────────────────────────────────────────
            int created = 0, failed = 0;
            var report = new StringBuilder();
            using (var t = new Transaction(doc, "STING Per-Option Schedule"))
            {
                t.Start();
                foreach (var opt in set.Options)
                {
                    string newName = $"{src.Name} [{set.Name}: {opt.Name}]";
                    try
                    {
                        var dupId = src.Duplicate(ViewDuplicateOption.Duplicate);
                        var dup = doc.GetElement(dupId) as ViewSchedule;
                        if (dup == null) { failed++; continue; }
                        try { dup.Name = UniqueName(doc, newName); }
                        catch (Exception ex) { StingLog.Warn($"rename {newName}: {ex.Message}"); }
                        var p = dup.get_Parameter(BuiltInParameter.VIEWER_OPTION_VISIBILITY);
                        if (p != null) p.Set(opt.OptionId);
                        created++;
                        report.AppendLine($"  · {newName}");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        report.AppendLine($"  ✗ {newName}: {ex.Message}");
                        StingLog.Warn($"ClonePerOptionSchedule: {ex.Message}");
                    }
                }
                t.Commit();
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Source set : {set.Name} ({set.Options.Count} options)");
            sb.AppendLine($"Created    : {created}");
            sb.AppendLine($"Failed     : {failed}");
            sb.AppendLine();
            sb.Append(report.ToString());
            TaskDialog.Show("STING — Per-Option Schedule", sb.ToString());
            return created > 0 ? Result.Succeeded : Result.Failed;
        }

        private static string UniqueName(Document doc, string desired)
        {
            var names = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Select(v => v.Name),
                StringComparer.OrdinalIgnoreCase);
            if (!names.Contains(desired)) return desired;
            for (int i = 2; i < 1000; i++)
            {
                string c = $"{desired} ({i})";
                if (!names.Contains(c)) return c;
            }
            return desired + " " + Guid.NewGuid().ToString("N").Substring(0, 6);
        }
    }
}
