using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Organise
{
    /// <summary>Tag selected elements only (Tag Sel from STINGTags ORGANISE tab).</summary>
    [Transaction(TransactionMode.Manual)]
    public class TagSelectedCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 0)
            {
                TaskDialog.Show("Tag Selected", "Select elements first.");
                return Result.Succeeded;
            }

            int tagged = 0;
            var seqCounters = TagConfig.GetExistingSequenceCounters(doc);

            using (Transaction tx = new Transaction(doc, "STING Tag Selected"))
            {
                tx.Start();
                foreach (ElementId id in selected)
                {
                    Element elem = doc.GetElement(id);
                    if (elem == null) continue;
                    if (TagConfig.BuildAndWriteTag(doc, elem, seqCounters))
                        tagged++;
                }
                tx.Commit();
            }

            TaskDialog.Show("Tag Selected", $"Tagged {tagged} of {selected.Count} selected elements.");
            return Result.Succeeded;
        }
    }

    /// <summary>Delete tag parameter values from selected elements.</summary>
    [Transaction(TransactionMode.Manual)]
    public class DeleteTagsCommand : IExternalCommand
    {
        private static readonly string[] TagParams = new[]
        {
            "ASS_TAG_1_TXT", "ASS_TAG_2_TXT", "ASS_TAG_3_TXT",
            "ASS_TAG_4_TXT", "ASS_TAG_5_TXT", "ASS_TAG_6_TXT",
            "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT",
            "ASS_LVL_COD_TXT", "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT",
            "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT", "ASS_STATUS_TXT",
        };

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 0)
            {
                TaskDialog.Show("Delete Tags", "Select elements first.");
                return Result.Succeeded;
            }

            TaskDialog confirm = new TaskDialog("Delete Tags");
            confirm.MainInstruction = $"Clear tags from {selected.Count} elements?";
            confirm.MainContent = "This will clear all ISO 19650 tag parameters.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel) return Result.Cancelled;

            int cleared = 0;
            using (Transaction tx = new Transaction(doc, "STING Delete Tags"))
            {
                tx.Start();
                foreach (ElementId id in selected)
                {
                    Element elem = doc.GetElement(id);
                    if (elem == null) continue;
                    bool any = false;
                    foreach (string param in TagParams)
                    {
                        if (ParameterHelpers.SetString(elem, param, "", overwrite: true))
                            any = true;
                    }
                    if (any) cleared++;
                }
                tx.Commit();
            }

            TaskDialog.Show("Delete Tags", $"Cleared tags from {cleared} elements.");
            return Result.Succeeded;
        }
    }

    /// <summary>Renumber sequence numbers for selected elements within their group key.</summary>
    [Transaction(TransactionMode.Manual)]
    public class RenumberTagsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 0)
            {
                TaskDialog.Show("Renumber", "Select elements first.");
                return Result.Succeeded;
            }

            // Group elements by (DISC, SYS, LVL) and renumber sequentially
            var groups = new Dictionary<string, List<Element>>();
            foreach (ElementId id in selected)
            {
                Element elem = doc.GetElement(id);
                if (elem == null) continue;
                string disc = ParameterHelpers.GetString(elem, "ASS_DISCIPLINE_COD_TXT");
                string sys = ParameterHelpers.GetString(elem, "ASS_SYSTEM_TYPE_TXT");
                string lvl = ParameterHelpers.GetString(elem, "ASS_LVL_COD_TXT");
                if (string.IsNullOrEmpty(disc)) continue;

                string key = $"{disc}_{sys}_{lvl}";
                if (!groups.ContainsKey(key)) groups[key] = new List<Element>();
                groups[key].Add(elem);
            }

            int renumbered = 0;
            using (Transaction tx = new Transaction(doc, "STING Renumber Tags"))
            {
                tx.Start();
                foreach (var kvp in groups)
                {
                    int seq = 1;
                    foreach (Element elem in kvp.Value)
                    {
                        string seqStr = seq.ToString().PadLeft(TagConfig.NumPad, '0');
                        ParameterHelpers.SetString(elem, "ASS_SEQ_NUM_TXT", seqStr, overwrite: true);

                        // Rebuild assembled tag
                        string disc = ParameterHelpers.GetString(elem, "ASS_DISCIPLINE_COD_TXT");
                        string loc = ParameterHelpers.GetString(elem, "ASS_LOC_TXT");
                        string zone = ParameterHelpers.GetString(elem, "ASS_ZONE_TXT");
                        string lvl = ParameterHelpers.GetString(elem, "ASS_LVL_COD_TXT");
                        string sys = ParameterHelpers.GetString(elem, "ASS_SYSTEM_TYPE_TXT");
                        string func = ParameterHelpers.GetString(elem, "ASS_FUNC_TXT");
                        string prod = ParameterHelpers.GetString(elem, "ASS_PRODCT_COD_TXT");
                        string tag = string.Join(TagConfig.Separator,
                            disc, loc, zone, lvl, sys, func, prod, seqStr);
                        ParameterHelpers.SetString(elem, "ASS_TAG_1_TXT", tag, overwrite: true);

                        seq++;
                        renumbered++;
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Renumber", $"Renumbered {renumbered} elements in {groups.Count} groups.");
            return Result.Succeeded;
        }
    }

    /// <summary>Audit tag data and export to CSV.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class AuditTagsCSVCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            Document doc = cmd.Application.ActiveUIDocument.Document;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            var sb = new StringBuilder();
            sb.AppendLine("ElementId,Category,Tag,DISC,LOC,ZONE,LVL,SYS,FUNC,PROD,SEQ,STATUS,Valid");

            int total = 0;
            foreach (Element elem in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(elem);
                if (!known.Contains(cat)) continue;

                total++;
                string tag = ParameterHelpers.GetString(elem, "ASS_TAG_1_TXT");
                string disc = ParameterHelpers.GetString(elem, "ASS_DISCIPLINE_COD_TXT");
                string loc = ParameterHelpers.GetString(elem, "ASS_LOC_TXT");
                string zone = ParameterHelpers.GetString(elem, "ASS_ZONE_TXT");
                string lvl = ParameterHelpers.GetString(elem, "ASS_LVL_COD_TXT");
                string sys = ParameterHelpers.GetString(elem, "ASS_SYSTEM_TYPE_TXT");
                string func = ParameterHelpers.GetString(elem, "ASS_FUNC_TXT");
                string prod = ParameterHelpers.GetString(elem, "ASS_PRODCT_COD_TXT");
                string seq = ParameterHelpers.GetString(elem, "ASS_SEQ_NUM_TXT");
                string status = ParameterHelpers.GetString(elem, "ASS_STATUS_TXT");
                bool valid = TagConfig.TagIsComplete(tag);

                sb.AppendLine($"{elem.Id},\"{CsvEscape(cat)}\",\"{CsvEscape(tag)}\",{disc},{loc},{zone},{lvl},{sys},{func},{prod},{seq},{status},{valid}");
            }

            // Write to file
            string dir = System.IO.Path.GetDirectoryName(doc.PathName);
            if (string.IsNullOrEmpty(dir)) dir = System.IO.Path.GetTempPath();
            string path = System.IO.Path.Combine(dir, "STING_Tag_Audit.csv");

            try
            {
                System.IO.File.WriteAllText(path, sb.ToString());
                TaskDialog.Show("Audit CSV",
                    $"Exported {total} elements to:\n{path}");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Audit CSV", $"Export failed: {ex.Message}");
                StingLog.Error("AuditCSV export failed", ex);
            }

            return Result.Succeeded;
        }

        private static string CsvEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("\"", "\"\"");
        }
    }

    /// <summary>Find duplicate tag values across the project.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class FindDuplicateTagsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            var tagMap = new Dictionary<string, List<ElementId>>();
            foreach (Element elem in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(elem);
                if (!known.Contains(cat)) continue;

                string tag = ParameterHelpers.GetString(elem, "ASS_TAG_1_TXT");
                if (string.IsNullOrEmpty(tag)) continue;

                if (!tagMap.ContainsKey(tag)) tagMap[tag] = new List<ElementId>();
                tagMap[tag].Add(elem.Id);
            }

            var duplicates = tagMap.Where(kvp => kvp.Value.Count > 1)
                .OrderByDescending(kvp => kvp.Value.Count).ToList();

            if (duplicates.Count == 0)
            {
                TaskDialog.Show("Find Duplicates", "No duplicate tags found.");
                return Result.Succeeded;
            }

            var report = new StringBuilder();
            report.AppendLine($"Found {duplicates.Count} duplicate tag values:\n");
            int selectCount = 0;
            var selectIds = new List<ElementId>();

            foreach (var kvp in duplicates.Take(20))
            {
                report.AppendLine($"  {kvp.Key} → {kvp.Value.Count} instances");
                selectIds.AddRange(kvp.Value);
                selectCount += kvp.Value.Count;
            }
            if (duplicates.Count > 20)
                report.AppendLine($"  ... and {duplicates.Count - 20} more");

            // Select all duplicates for visual review
            uidoc.Selection.SetElementIds(selectIds);

            TaskDialog.Show("Find Duplicates",
                $"{duplicates.Count} duplicate tags ({selectCount} elements selected).\n\n" +
                report.ToString());

            return Result.Succeeded;
        }
    }

    /// <summary>Highlight elements with incomplete/missing tags using graphic overrides.</summary>
    [Transaction(TransactionMode.Manual)]
    public class HighlightInvalidCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            // Red = missing, Orange = incomplete
            var red = new OverrideGraphicSettings();
            red.SetProjectionLineColor(new Color(255, 0, 0));
            red.SetProjectionLineWeight(5);

            var orange = new OverrideGraphicSettings();
            orange.SetProjectionLineColor(new Color(255, 165, 0));
            orange.SetProjectionLineWeight(4);

            int missing = 0, incomplete = 0;

            using (Transaction tx = new Transaction(doc, "STING Highlight Invalid"))
            {
                tx.Start();
                foreach (Element elem in new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType())
                {
                    string cat = ParameterHelpers.GetCategoryName(elem);
                    if (!known.Contains(cat)) continue;

                    string tag = ParameterHelpers.GetString(elem, "ASS_TAG_1_TXT");
                    if (string.IsNullOrEmpty(tag))
                    {
                        view.SetElementOverrides(elem.Id, red);
                        missing++;
                    }
                    else if (!TagConfig.TagIsComplete(tag))
                    {
                        view.SetElementOverrides(elem.Id, orange);
                        incomplete++;
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Highlight Invalid",
                $"Red (missing): {missing}\nOrange (incomplete): {incomplete}\n\n" +
                "Use 'Clear Overrides' to reset.");
            return Result.Succeeded;
        }
    }

    /// <summary>Clear graphic overrides from all elements in active view.</summary>
    [Transaction(TransactionMode.Manual)]
    public class ClearOverridesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            Document doc = cmd.Application.ActiveUIDocument.Document;
            View view = doc.ActiveView;

            var reset = new OverrideGraphicSettings();
            int cleared = 0;

            using (Transaction tx = new Transaction(doc, "STING Clear Overrides"))
            {
                tx.Start();
                foreach (Element elem in new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType())
                {
                    view.SetElementOverrides(elem.Id, reset);
                    cleared++;
                }
                tx.Commit();
            }

            TaskDialog.Show("Clear Overrides", $"Cleared overrides from {cleared} elements.");
            return Result.Succeeded;
        }
    }
}
