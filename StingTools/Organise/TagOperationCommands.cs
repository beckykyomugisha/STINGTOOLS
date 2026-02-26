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
    /// <summary>Tag selected elements only (Tag Sel from STINGTags ORGANISE tab).
    /// Includes collision mode selection for handling already-tagged elements.</summary>
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

            // Check if any are already tagged
            int alreadyTagged = 0;
            foreach (ElementId id in selected)
            {
                Element elem = doc.GetElement(id);
                if (elem == null) continue;
                string tag = ParameterHelpers.GetString(elem, "ASS_TAG_1_TXT");
                if (TagConfig.TagIsComplete(tag)) alreadyTagged++;
            }

            TagCollisionMode collisionMode = TagCollisionMode.AutoIncrement;
            if (alreadyTagged > 0)
            {
                TaskDialog modeDlg = new TaskDialog("Tag Selected — Collision Mode");
                modeDlg.MainInstruction = $"{alreadyTagged} of {selected.Count} elements already have tags";
                modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Skip already-tagged",
                    "Only tag elements that don't have complete tags");
                modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Overwrite all tags",
                    "Re-derive and overwrite ALL tokens, including already-tagged elements");
                modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    "Auto-increment on collision",
                    "Tag all; auto-increment SEQ if generated tag already exists");
                modeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                switch (modeDlg.Show())
                {
                    case TaskDialogResult.CommandLink1:
                        collisionMode = TagCollisionMode.Skip; break;
                    case TaskDialogResult.CommandLink2:
                        collisionMode = TagCollisionMode.Overwrite; break;
                    case TaskDialogResult.CommandLink3:
                        collisionMode = TagCollisionMode.AutoIncrement; break;
                    default:
                        return Result.Cancelled;
                }
            }

            int tagged = 0;
            var seqCounters = TagConfig.GetExistingSequenceCounters(doc);
            var tagIndex = TagConfig.BuildExistingTagIndex(doc);

            using (Transaction tx = new Transaction(doc, "STING Tag Selected"))
            {
                tx.Start();
                foreach (ElementId id in selected)
                {
                    Element elem = doc.GetElement(id);
                    if (elem == null) continue;
                    bool skipComplete = (collisionMode != TagCollisionMode.Overwrite);
                    if (TagConfig.BuildAndWriteTag(doc, elem, seqCounters,
                        skipComplete: skipComplete,
                        existingTags: tagIndex,
                        collisionMode: collisionMode))
                        tagged++;
                }
                tx.Commit();
            }

            TaskDialog.Show("Tag Selected", $"Tagged {tagged} of {selected.Count} selected elements.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Re-tag selected elements: forces overwrite of all tag tokens and the assembled tag.
    /// Use when elements need fresh tags (e.g., after category changes or corrections).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ReTagCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 0)
            {
                TaskDialog.Show("Re-Tag", "Select elements to re-tag first.");
                return Result.Succeeded;
            }

            TaskDialog confirm = new TaskDialog("Re-Tag Elements");
            confirm.MainInstruction = $"Re-tag {selected.Count} elements?";
            confirm.MainContent =
                "This will OVERWRITE existing tag values with freshly derived tokens.\n" +
                "Existing LOC/ZONE values will be preserved; all other tokens regenerated.\n" +
                "New sequence numbers will be assigned.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            var seqCounters = TagConfig.GetExistingSequenceCounters(doc);
            var tagIndex = TagConfig.BuildExistingTagIndex(doc);
            int retagged = 0;

            using (Transaction tx = new Transaction(doc, "STING Re-Tag"))
            {
                tx.Start();
                foreach (ElementId id in selected)
                {
                    Element elem = doc.GetElement(id);
                    if (elem == null) continue;
                    if (TagConfig.BuildAndWriteTag(doc, elem, seqCounters,
                        skipComplete: false,
                        existingTags: tagIndex,
                        collisionMode: TagCollisionMode.Overwrite))
                        retagged++;
                }
                tx.Commit();
            }

            TaskDialog.Show("Re-Tag", $"Re-tagged {retagged} of {selected.Count} elements.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Auto-resolve duplicate tags across the entire project by incrementing
    /// SEQ numbers on duplicate entries. Preserves the first occurrence;
    /// reassigns subsequent duplicates with new unique SEQ values.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class FixDuplicateTagsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            // Find all duplicates
            var tagMap = new Dictionary<string, List<Element>>();
            foreach (Element elem in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(elem);
                if (!known.Contains(cat)) continue;
                string tag = ParameterHelpers.GetString(elem, "ASS_TAG_1_TXT");
                if (string.IsNullOrEmpty(tag)) continue;
                if (!tagMap.ContainsKey(tag)) tagMap[tag] = new List<Element>();
                tagMap[tag].Add(elem);
            }

            var duplicates = tagMap.Where(kvp => kvp.Value.Count > 1).ToList();
            if (duplicates.Count == 0)
            {
                TaskDialog.Show("Fix Duplicates", "No duplicate tags found. Project is clean.");
                return Result.Succeeded;
            }

            int totalDups = duplicates.Sum(d => d.Value.Count - 1);
            TaskDialog confirm = new TaskDialog("Fix Duplicate Tags");
            confirm.MainInstruction = $"Fix {totalDups} duplicate tags?";
            confirm.MainContent =
                $"Found {duplicates.Count} tag values with duplicates ({totalDups} elements to reassign).\n\n" +
                "The first element with each tag will be kept. All subsequent duplicates will get " +
                "new unique SEQ numbers.\n\nExisting tokens (DISC, LOC, ZONE, etc.) are preserved.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            var seqCounters = TagConfig.GetExistingSequenceCounters(doc);
            var tagIndex = new HashSet<string>(tagMap.Keys, StringComparer.Ordinal);
            int fixed_ = 0;

            using (Transaction tx = new Transaction(doc, "STING Fix Duplicates"))
            {
                tx.Start();
                foreach (var kvp in duplicates)
                {
                    // Skip the first element (keep its tag); fix the rest
                    for (int i = 1; i < kvp.Value.Count; i++)
                    {
                        Element elem = kvp.Value[i];
                        string disc = ParameterHelpers.GetString(elem, "ASS_DISCIPLINE_COD_TXT");
                        string loc = ParameterHelpers.GetString(elem, "ASS_LOC_TXT");
                        string zone = ParameterHelpers.GetString(elem, "ASS_ZONE_TXT");
                        string lvl = ParameterHelpers.GetString(elem, "ASS_LVL_COD_TXT");
                        string sys = ParameterHelpers.GetString(elem, "ASS_SYSTEM_TYPE_TXT");
                        string func = ParameterHelpers.GetString(elem, "ASS_FUNC_TXT");
                        string prod = ParameterHelpers.GetString(elem, "ASS_PRODCT_COD_TXT");

                        if (string.IsNullOrEmpty(disc)) continue;

                        string seqKey = $"{disc}_{sys}_{lvl}";
                        if (!seqCounters.ContainsKey(seqKey)) seqCounters[seqKey] = 0;

                        // Find next unique SEQ
                        string newTag;
                        string newSeq;
                        int safety = 10000;
                        do
                        {
                            seqCounters[seqKey]++;
                            newSeq = seqCounters[seqKey].ToString().PadLeft(TagConfig.NumPad, '0');
                            newTag = string.Join(TagConfig.Separator, disc, loc, zone, lvl, sys, func, prod, newSeq);
                        } while (tagIndex.Contains(newTag) && safety-- > 0);

                        tagIndex.Add(newTag);
                        ParameterHelpers.SetString(elem, "ASS_SEQ_NUM_TXT", newSeq, overwrite: true);
                        ParameterHelpers.SetString(elem, "ASS_TAG_1_TXT", newTag, overwrite: true);
                        fixed_++;
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Fix Duplicates",
                $"Fixed {fixed_} duplicate tags across {duplicates.Count} tag values.\n" +
                "All tags are now unique.");
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

    /// <summary>
    /// Copy tag values from one element to all other selected elements.
    /// Picks the first selected element as source; writes its tag tokens to all others.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CopyTagsCommand : IExternalCommand
    {
        private static readonly string[] CopyParams = new[]
        {
            "ASS_TAG_1_TXT", "ASS_TAG_2_TXT", "ASS_TAG_3_TXT",
            "ASS_TAG_4_TXT", "ASS_TAG_5_TXT", "ASS_TAG_6_TXT",
            "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT",
            "ASS_LVL_COD_TXT", "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT",
            "ASS_PRODCT_COD_TXT", "ASS_STATUS_TXT",
        };

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var selected = uidoc.Selection.GetElementIds().ToList();
            if (selected.Count < 2)
            {
                TaskDialog.Show("Copy Tags",
                    "Select at least 2 elements. The first selected element " +
                    "is the source; all others receive its tag values.\n" +
                    "(Note: SEQ numbers are NOT copied — each element keeps unique SEQ)");
                return Result.Succeeded;
            }

            Element source = doc.GetElement(selected[0]);
            var values = new Dictionary<string, string>();
            foreach (string p in CopyParams)
                values[p] = ParameterHelpers.GetString(source, p);

            string sourceTag = values.TryGetValue("ASS_TAG_1_TXT", out string t) ? t : "(empty)";

            TaskDialog confirm = new TaskDialog("Copy Tags");
            confirm.MainInstruction = $"Copy tags from Element {source.Id}?";
            confirm.MainContent =
                $"Source tag: {sourceTag}\n" +
                $"Target: {selected.Count - 1} elements\n\n" +
                "Copies all tag values except SEQ (sequence stays unique).";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            int copied = 0;
            using (Transaction tx = new Transaction(doc, "STING Copy Tags"))
            {
                tx.Start();
                for (int i = 1; i < selected.Count; i++)
                {
                    Element target = doc.GetElement(selected[i]);
                    if (target == null) continue;
                    foreach (var kvp in values)
                    {
                        ParameterHelpers.SetString(target, kvp.Key, kvp.Value, overwrite: true);
                    }
                    copied++;
                }
                tx.Commit();
            }

            TaskDialog.Show("Copy Tags", $"Copied tag values to {copied} elements.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Swap tag values between two selected elements (including SEQ).
    /// Useful for correcting tag assignment errors.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class SwapTagsCommand : IExternalCommand
    {
        private static readonly string[] SwapParams = new[]
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

            var selected = uidoc.Selection.GetElementIds().ToList();
            if (selected.Count != 2)
            {
                TaskDialog.Show("Swap Tags", "Select exactly 2 elements to swap their tags.");
                return Result.Succeeded;
            }

            Element a = doc.GetElement(selected[0]);
            Element b = doc.GetElement(selected[1]);

            string tagA = ParameterHelpers.GetString(a, "ASS_TAG_1_TXT");
            string tagB = ParameterHelpers.GetString(b, "ASS_TAG_1_TXT");

            TaskDialog confirm = new TaskDialog("Swap Tags");
            confirm.MainInstruction = "Swap tags between two elements?";
            confirm.MainContent =
                $"Element A ({a.Id}): {(string.IsNullOrEmpty(tagA) ? "(empty)" : tagA)}\n" +
                $"Element B ({b.Id}): {(string.IsNullOrEmpty(tagB) ? "(empty)" : tagB)}";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            using (Transaction tx = new Transaction(doc, "STING Swap Tags"))
            {
                tx.Start();
                foreach (string param in SwapParams)
                {
                    string valA = ParameterHelpers.GetString(a, param);
                    string valB = ParameterHelpers.GetString(b, param);
                    ParameterHelpers.SetString(a, param, valB, overwrite: true);
                    ParameterHelpers.SetString(b, param, valA, overwrite: true);
                }
                tx.Commit();
            }

            TaskDialog.Show("Swap Tags", "Tags swapped successfully.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Select all elements matching a specific discipline code (M, E, P, A, S, etc.).
    /// Presents the user with discipline options and selects matching elements.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class SelectByDisciplineCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Count elements per discipline
            var discCounts = new Dictionary<string, int>();
            var discElements = new Dictionary<string, List<ElementId>>();
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            foreach (Element elem in new FilteredElementCollector(doc, doc.ActiveView.Id)
                .WhereElementIsNotElementType())
            {
                string disc = ParameterHelpers.GetString(elem, "ASS_DISCIPLINE_COD_TXT");
                if (string.IsNullOrEmpty(disc)) continue;

                if (!discCounts.ContainsKey(disc))
                {
                    discCounts[disc] = 0;
                    discElements[disc] = new List<ElementId>();
                }
                discCounts[disc]++;
                discElements[disc].Add(elem.Id);
            }

            if (discCounts.Count == 0)
            {
                TaskDialog.Show("Select by Discipline", "No tagged elements found in active view.");
                return Result.Succeeded;
            }

            // Show top 4 disciplines
            var top = discCounts.OrderByDescending(x => x.Value).Take(4).ToList();
            TaskDialog td = new TaskDialog("Select by Discipline");
            td.MainInstruction = "Select elements by discipline code";
            for (int i = 0; i < top.Count; i++)
            {
                td.AddCommandLink((TaskDialogCommandLinkId)(i + 201),
                    $"{top[i].Key} — {top[i].Value} elements");
            }
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            var result = td.Show();
            int idx = -1;
            switch (result)
            {
                case TaskDialogResult.CommandLink1: idx = 0; break;
                case TaskDialogResult.CommandLink2: idx = 1; break;
                case TaskDialogResult.CommandLink3: idx = 2; break;
                case TaskDialogResult.CommandLink4: idx = 3; break;
                default: return Result.Cancelled;
            }

            if (idx >= 0 && idx < top.Count)
            {
                string disc = top[idx].Key;
                uidoc.Selection.SetElementIds(discElements[disc]);
                TaskDialog.Show("Select by Discipline",
                    $"Selected {discElements[disc].Count} elements with discipline '{disc}'.");
            }

            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Quick tag statistics for the active view — shows counts per discipline,
    /// system, level, and overall tag/untag ratio.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class TagStatsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            Document doc = cmd.Application.ActiveUIDocument.Document;
            View view = doc.ActiveView;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            int total = 0, tagged = 0, untagged = 0;
            var byDisc = new Dictionary<string, int>();
            var bySys = new Dictionary<string, int>();
            var byLvl = new Dictionary<string, int>();

            foreach (Element elem in new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(elem);
                if (!known.Contains(cat)) continue;

                total++;
                string tag = ParameterHelpers.GetString(elem, "ASS_TAG_1_TXT");
                if (TagConfig.TagIsComplete(tag))
                    tagged++;
                else
                    untagged++;

                string disc = ParameterHelpers.GetString(elem, "ASS_DISCIPLINE_COD_TXT");
                if (!string.IsNullOrEmpty(disc))
                {
                    if (!byDisc.ContainsKey(disc)) byDisc[disc] = 0;
                    byDisc[disc]++;
                }
                string sys = ParameterHelpers.GetString(elem, "ASS_SYSTEM_TYPE_TXT");
                if (!string.IsNullOrEmpty(sys))
                {
                    if (!bySys.ContainsKey(sys)) bySys[sys] = 0;
                    bySys[sys]++;
                }
                string lvl = ParameterHelpers.GetString(elem, "ASS_LVL_COD_TXT");
                if (!string.IsNullOrEmpty(lvl))
                {
                    if (!byLvl.ContainsKey(lvl)) byLvl[lvl] = 0;
                    byLvl[lvl]++;
                }
            }

            var report = new StringBuilder();
            report.AppendLine($"Tag Statistics — {view.Name}");
            report.AppendLine(new string('═', 45));
            report.AppendLine($"  Total taggable: {total}");
            report.AppendLine($"  Tagged:         {tagged}");
            report.AppendLine($"  Untagged:       {untagged}");
            double pct = total > 0 ? tagged * 100.0 / total : 0;
            report.AppendLine($"  Compliance:     {pct:F1}%");

            if (byDisc.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("  By Discipline:");
                foreach (var kvp in byDisc.OrderByDescending(x => x.Value))
                    report.AppendLine($"    {kvp.Key,-6} {kvp.Value,5}");
            }
            if (bySys.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("  By System:");
                foreach (var kvp in bySys.OrderByDescending(x => x.Value).Take(10))
                    report.AppendLine($"    {kvp.Key,-8} {kvp.Value,5}");
            }
            if (byLvl.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("  By Level:");
                foreach (var kvp in byLvl.OrderBy(x => x.Key))
                    report.AppendLine($"    {kvp.Key,-6} {kvp.Value,5}");
            }

            TaskDialog td = new TaskDialog("Tag Statistics");
            td.MainInstruction = $"View: {tagged}/{total} tagged ({pct:F1}%)";
            td.MainContent = report.ToString();
            td.Show();

            return Result.Succeeded;
        }
    }
}
