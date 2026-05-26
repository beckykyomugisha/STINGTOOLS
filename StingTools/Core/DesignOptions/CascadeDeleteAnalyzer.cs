// StingTools — Cascade-delete dry-run for design options.
//
// doc.Delete(option.Id) cascades to every element inside the option AND
// every view whose VIEWER_OPTION_VISIBILITY parameter targets that
// option's set/option pair. Deleting an option without a heads-up has
// destroyed days of work in real projects (Autodesk support article
// confirms). This analyser produces a structured report so STING can
// always preview the blast radius before any destructive call.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace StingTools.Core.DesignOptions
{
    public class CascadeDeleteReport
    {
        public ElementId OptionId;
        public string OptionName;
        public int ElementCount;
        public List<string> ViewsAffected = new List<string>();
        public List<string> SchedulesAffected = new List<string>();
        public List<string> SheetsAffected = new List<string>();
        public List<string> Warnings = new List<string>();
    }

    public static class CascadeDeleteAnalyzer
    {
        public static CascadeDeleteReport Analyze(Document doc, ElementId optionId)
        {
            var rep = new CascadeDeleteReport { OptionId = optionId };
            if (doc == null || optionId == null || optionId == ElementId.InvalidElementId)
                return rep;

            try
            {
                var opt = doc.GetElement(optionId) as DesignOption;
                rep.OptionName = opt?.Name ?? optionId.ToString();
                rep.ElementCount = DesignOptionRegistry.CountElementsInOption(doc, optionId);
            }
            catch (Exception ex) { rep.Warnings.Add($"option lookup: {ex.Message}"); }

            try
            {
                var allViews = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate);

                foreach (var v in allViews)
                {
                    var p = v.get_Parameter(BuiltInParameter.VIEWER_OPTION_VISIBILITY);
                    if (p == null) continue;
                    var pid = p.AsElementId();
                    if (pid == null || pid == ElementId.InvalidElementId) continue;
                    if (pid != optionId) continue;

                    string label = $"{v.ViewType} — {v.Name}";
                    if (v is ViewSchedule)        rep.SchedulesAffected.Add(label);
                    else if (v is ViewSheet)      rep.SheetsAffected.Add(label);
                    else                          rep.ViewsAffected.Add(label);
                }
            }
            catch (Exception ex) { rep.Warnings.Add($"view scan: {ex.Message}"); }

            return rep;
        }

        public static string FormatReport(CascadeDeleteReport rep)
        {
            if (rep == null) return "(no report)";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Option: {rep.OptionName} ({rep.OptionId})");
            sb.AppendLine($"  Elements that will be deleted    : {rep.ElementCount}");
            sb.AppendLine($"  Views locked to this option      : {rep.ViewsAffected.Count}");
            sb.AppendLine($"  Schedules locked to this option  : {rep.SchedulesAffected.Count}");
            sb.AppendLine($"  Sheets locked to this option     : {rep.SheetsAffected.Count}");
            if (rep.Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Warnings:");
                foreach (var w in rep.Warnings) sb.AppendLine("  · " + w);
            }
            int sample = 12;
            void Sample(string title, List<string> list)
            {
                if (list.Count == 0) return;
                sb.AppendLine();
                sb.AppendLine(title);
                foreach (var x in list.Take(sample)) sb.AppendLine("  · " + x);
                if (list.Count > sample) sb.AppendLine($"  · … +{list.Count - sample} more");
            }
            Sample("Views:",      rep.ViewsAffected);
            Sample("Schedules:",  rep.SchedulesAffected);
            Sample("Sheets:",     rep.SheetsAffected);
            return sb.ToString();
        }
    }
}
