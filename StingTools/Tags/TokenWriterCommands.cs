using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// Manual token writer commands — the 13 individual ISO 19650 token setters
    /// from the STINGTags v9.6 CREATE tab. Each sets a single token on all
    /// selected elements (or all taggable elements if nothing is selected).
    /// Includes PROJ, ORIG, VOL, LVL, DISC, LOC, ZONE, SYS, FUNC, PROD, SEQ, STATUS, REV.
    /// </summary>
    internal static class TokenWriter
    {
        public static Result WriteToken(ExternalCommandData cmd, string paramName,
            string label, string[] options)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Build target set: selected elements or all taggable in view
            var targetIds = uidoc.Selection.GetElementIds();
            bool usingSelection = targetIds.Count > 0;

            if (!usingSelection)
            {
                var known = new HashSet<string>(TagConfig.DiscMap.Keys);
                targetIds = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .WhereElementIsNotElementType()
                    .Where(e => known.Contains(ParameterHelpers.GetCategoryName(e)))
                    .Select(e => e.Id)
                    .ToList();
            }

            if (targetIds.Count == 0)
            {
                TaskDialog.Show(label, "No elements to update.");
                return Result.Succeeded;
            }

            // Show options dialog
            TaskDialog td = new TaskDialog(label);
            td.MainInstruction = $"Set {label} on {targetIds.Count} elements";
            td.MainContent = usingSelection ? "(Selected elements)" : "(All taggable in view)";

            for (int i = 0; i < Math.Min(options.Length, 4); i++)
            {
                td.AddCommandLink((TaskDialogCommandLinkId)(i + 201), options[i]);
            }
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            var result = td.Show();
            string value = null;
            for (int i = 0; i < Math.Min(options.Length, 4); i++)
            {
                if (result == (TaskDialogResult)(i + 1001))
                    value = options[i];
            }

            // Map TaskDialogResult back to command link
            switch (result)
            {
                case TaskDialogResult.CommandLink1: value = options.Length > 0 ? options[0] : null; break;
                case TaskDialogResult.CommandLink2: value = options.Length > 1 ? options[1] : null; break;
                case TaskDialogResult.CommandLink3: value = options.Length > 2 ? options[2] : null; break;
                case TaskDialogResult.CommandLink4: value = options.Length > 3 ? options[3] : null; break;
                default: return Result.Cancelled;
            }

            if (value == null) return Result.Cancelled;

            int written = 0;
            using (Transaction tx = new Transaction(doc, $"Set {label}"))
            {
                tx.Start();
                foreach (ElementId id in targetIds)
                {
                    Element elem = doc.GetElement(id);
                    if (elem == null) continue;
                    if (ParameterHelpers.SetString(elem, paramName, value, overwrite: true))
                        written++;
                }
                tx.Commit();
            }

            TaskDialog.Show(label, $"Set '{value}' on {written} elements.");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class SetLocCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
            => TokenWriter.WriteToken(cmd, "ASS_LOC_TXT", "Location (LOC)",
                TagConfig.LocCodes.ToArray());
    }

    [Transaction(TransactionMode.Manual)]
    public class SetZoneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
            => TokenWriter.WriteToken(cmd, "ASS_ZONE_TXT", "Zone (ZONE)",
                TagConfig.ZoneCodes.ToArray());
    }

    [Transaction(TransactionMode.Manual)]
    public class SetStatusCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
            => TokenWriter.WriteToken(cmd, "ASS_STATUS_TXT", "Status",
                new[] { "EXISTING", "NEW", "DEMOLISHED", "TEMPORARY" });
    }

    /// <summary>
    /// Assign sequential numbers to selected elements, grouped by (DISC, SYS, LVL).
    /// Standalone version of the sequence numbering embedded in AutoTag.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class AssignNumbersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            // Determine scope
            var targetIds = uidoc.Selection.GetElementIds();
            if (targetIds.Count == 0)
            {
                targetIds = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .WhereElementIsNotElementType()
                    .Where(e => known.Contains(ParameterHelpers.GetCategoryName(e)))
                    .Select(e => e.Id).ToList();
            }

            // Find highest existing sequence per group
            var maxSeq = new Dictionary<string, int>();
            foreach (Element elem in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(elem);
                if (!known.Contains(cat)) continue;
                string disc = ParameterHelpers.GetString(elem, "ASS_DISCIPLINE_COD_TXT");
                string sys = ParameterHelpers.GetString(elem, "ASS_SYSTEM_TYPE_TXT");
                string lvl = ParameterHelpers.GetString(elem, "ASS_LVL_COD_TXT");
                string seqStr = ParameterHelpers.GetString(elem, "ASS_SEQ_NUM_TXT");
                if (string.IsNullOrEmpty(disc)) continue;

                string key = $"{disc}_{sys}_{lvl}";
                if (int.TryParse(seqStr, out int seqNum))
                {
                    if (!maxSeq.ContainsKey(key) || seqNum > maxSeq[key])
                        maxSeq[key] = seqNum;
                }
            }

            int assigned = 0;
            using (Transaction tx = new Transaction(doc, "STING Assign Numbers"))
            {
                tx.Start();
                foreach (ElementId id in targetIds)
                {
                    Element elem = doc.GetElement(id);
                    if (elem == null) continue;
                    string cat = ParameterHelpers.GetCategoryName(elem);
                    if (!known.Contains(cat)) continue;

                    // Skip if already has a sequence number
                    string existing = ParameterHelpers.GetString(elem, "ASS_SEQ_NUM_TXT");
                    if (!string.IsNullOrEmpty(existing)) continue;

                    string disc = ParameterHelpers.GetString(elem, "ASS_DISCIPLINE_COD_TXT");
                    string sys = ParameterHelpers.GetString(elem, "ASS_SYSTEM_TYPE_TXT");
                    string lvl = ParameterHelpers.GetString(elem, "ASS_LVL_COD_TXT");
                    if (string.IsNullOrEmpty(disc))
                    {
                        disc = TagConfig.DiscMap.TryGetValue(cat, out string d) ? d : "XX";
                        ParameterHelpers.SetIfEmpty(elem, "ASS_DISCIPLINE_COD_TXT", disc);
                    }
                    if (string.IsNullOrEmpty(sys))
                    {
                        sys = TagConfig.GetSysCode(cat);
                        ParameterHelpers.SetIfEmpty(elem, "ASS_SYSTEM_TYPE_TXT", sys);
                    }
                    if (string.IsNullOrEmpty(lvl))
                    {
                        lvl = ParameterHelpers.GetLevelCode(doc, elem);
                        ParameterHelpers.SetIfEmpty(elem, "ASS_LVL_COD_TXT", lvl);
                    }

                    string key = $"{disc}_{sys}_{lvl}";
                    if (!maxSeq.ContainsKey(key)) maxSeq[key] = 0;
                    maxSeq[key]++;
                    string seq = maxSeq[key].ToString().PadLeft(TagConfig.NumPad, '0');
                    ParameterHelpers.SetString(elem, "ASS_SEQ_NUM_TXT", seq, overwrite: true);
                    assigned++;
                }
                tx.Commit();
            }

            TaskDialog.Show("Assign Numbers", $"Assigned sequence numbers to {assigned} elements.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Build/rebuild assembled tags (ASS_TAG_1_TXT) from existing individual token
    /// parameters without changing any token values. Respects existing LOC/ZONE values.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class BuildTagsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var targetIds = uidoc.Selection.GetElementIds();
            if (targetIds.Count == 0)
            {
                var known = new HashSet<string>(TagConfig.DiscMap.Keys);
                targetIds = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .WhereElementIsNotElementType()
                    .Where(e => known.Contains(ParameterHelpers.GetCategoryName(e)))
                    .Select(e => e.Id).ToList();
            }

            int built = 0;
            using (Transaction tx = new Transaction(doc, "STING Build Tags"))
            {
                tx.Start();
                foreach (ElementId id in targetIds)
                {
                    Element elem = doc.GetElement(id);
                    if (elem == null) continue;

                    string disc = ParameterHelpers.GetString(elem, "ASS_DISCIPLINE_COD_TXT");
                    string loc = ParameterHelpers.GetString(elem, "ASS_LOC_TXT");
                    string zone = ParameterHelpers.GetString(elem, "ASS_ZONE_TXT");
                    string lvl = ParameterHelpers.GetString(elem, "ASS_LVL_COD_TXT");
                    string sys = ParameterHelpers.GetString(elem, "ASS_SYSTEM_TYPE_TXT");
                    string func = ParameterHelpers.GetString(elem, "ASS_FUNC_TXT");
                    string prod = ParameterHelpers.GetString(elem, "ASS_PRODCT_COD_TXT");
                    string seq = ParameterHelpers.GetString(elem, "ASS_SEQ_NUM_TXT");

                    if (string.IsNullOrEmpty(disc)) continue;

                    string tag = string.Join(TagConfig.Separator,
                        disc, loc, zone, lvl, sys, func, prod, seq);
                    ParameterHelpers.SetString(elem, "ASS_TAG_1_TXT", tag, overwrite: true);
                    built++;
                }
                tx.Commit();
            }

            TaskDialog.Show("Build Tags", $"Built tags for {built} elements.");
            return Result.Succeeded;
        }
    }

    /// <summary>ISO 19650 completeness dashboard — reports per-discipline compliance.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class CompletenessDashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            Document doc = cmd.Application.ActiveUIDocument.Document;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            var stats = new Dictionary<string, (int total, int valid, int incomplete, int missing)>();

            foreach (Element elem in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(elem);
                if (!known.Contains(cat)) continue;

                string disc = TagConfig.DiscMap.TryGetValue(cat, out string d) ? d : "XX";
                if (!stats.ContainsKey(disc)) stats[disc] = (0, 0, 0, 0);

                var s = stats[disc];
                string tag = ParameterHelpers.GetString(elem, "ASS_TAG_1_TXT");
                if (string.IsNullOrEmpty(tag))
                    stats[disc] = (s.total + 1, s.valid, s.incomplete, s.missing + 1);
                else if (TagConfig.TagIsComplete(tag))
                    stats[disc] = (s.total + 1, s.valid + 1, s.incomplete, s.missing);
                else
                    stats[disc] = (s.total + 1, s.valid, s.incomplete + 1, s.missing);
            }

            var report = new StringBuilder();
            report.AppendLine("═══ ISO 19650 Completeness Dashboard ═══");
            report.AppendLine();
            report.AppendLine($"{"DISC",-6} {"Total",7} {"Valid",7} {"Incp",7} {"Miss",7} {"Comp%",7}");
            report.AppendLine(new string('─', 42));

            int grandTotal = 0, grandValid = 0, grandInc = 0, grandMiss = 0;
            foreach (var kvp in stats.OrderBy(x => x.Key))
            {
                var s = kvp.Value;
                double pct = s.total > 0 ? s.valid * 100.0 / s.total : 0;
                report.AppendLine($"{kvp.Key,-6} {s.total,7} {s.valid,7} {s.incomplete,7} {s.missing,7} {pct,6:F1}%");
                grandTotal += s.total;
                grandValid += s.valid;
                grandInc += s.incomplete;
                grandMiss += s.missing;
            }

            report.AppendLine(new string('─', 42));
            double grandPct = grandTotal > 0 ? grandValid * 100.0 / grandTotal : 0;
            report.AppendLine($"{"TOTAL",-6} {grandTotal,7} {grandValid,7} {grandInc,7} {grandMiss,7} {grandPct,6:F1}%");

            TaskDialog td = new TaskDialog("ISO Completeness Dashboard");
            td.MainInstruction = $"Overall compliance: {grandPct:F1}% ({grandValid}/{grandTotal})";
            td.MainContent = report.ToString();
            td.Show();

            return Result.Succeeded;
        }
    }
}
