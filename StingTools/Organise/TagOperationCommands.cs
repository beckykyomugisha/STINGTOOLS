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
    [Regeneration(RegenerationOption.Manual)]
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
                string tag = ParameterHelpers.GetString(elem, ParamRegistry.TAG1);
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

            var (tagIndex, seqCounters) = TagConfig.BuildTagIndexAndCounters(doc);
            var stats = new TaggingStats();
            var roomIndex = SpatialAutoDetect.BuildRoomIndex(doc);
            string projectLoc = SpatialAutoDetect.DetectProjectLoc(doc);

            using (Transaction tx = new Transaction(doc, "STING Tag Selected"))
            {
                tx.Start();
                foreach (ElementId id in selected)
                {
                    Element elem = doc.GetElement(id);
                    if (elem == null) continue;

                    // Pre-populate LOC/ZONE from spatial data before tagging
                    if (string.IsNullOrEmpty(ParameterHelpers.GetString(elem, ParamRegistry.LOC)))
                    {
                        string loc = SpatialAutoDetect.DetectLoc(doc, elem, roomIndex, projectLoc);
                        ParameterHelpers.SetIfEmpty(elem, ParamRegistry.LOC, loc);
                    }
                    if (string.IsNullOrEmpty(ParameterHelpers.GetString(elem, ParamRegistry.ZONE)))
                    {
                        string zone = SpatialAutoDetect.DetectZone(doc, elem, roomIndex);
                        ParameterHelpers.SetIfEmpty(elem, ParamRegistry.ZONE, zone);
                    }

                    bool skipComplete = (collisionMode != TagCollisionMode.Overwrite);
                    TagConfig.BuildAndWriteTag(doc, elem, seqCounters,
                        skipComplete: skipComplete,
                        existingTags: tagIndex,
                        collisionMode: collisionMode,
                        stats: stats);

                    // Write TAG7 + sub-sections (TAG7A-TAG7F) — rich descriptive narrative
                    string catTag7 = ParameterHelpers.GetCategoryName(elem);
                    string[] tVals = ParamRegistry.ReadTokenValues(elem);
                    TagConfig.WriteTag7All(doc, elem, catTag7, tVals, overwrite: true);
                }
                tx.Commit();
            }

            string report = $"Tagged {stats.TotalTagged} of {selected.Count} selected elements.";
            if (stats.TotalSkipped > 0) report += $"\nSkipped: {stats.TotalSkipped}";
            if (stats.TotalCollisions > 0) report += $"\nCollisions resolved: {stats.TotalCollisions}";
            TaskDialog.Show("Tag Selected", report);
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Re-tag selected elements: forces overwrite of all tag tokens and the assembled tag.
    /// Use when elements need fresh tags (e.g., after category changes or corrections).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
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

            var (tagIndex, seqCounters) = TagConfig.BuildTagIndexAndCounters(doc);
            var roomIndex = SpatialAutoDetect.BuildRoomIndex(doc);
            string projectLoc = SpatialAutoDetect.DetectProjectLoc(doc);
            int retagged = 0;

            using (Transaction tx = new Transaction(doc, "STING Re-Tag"))
            {
                tx.Start();
                foreach (ElementId id in selected)
                {
                    Element elem = doc.GetElement(id);
                    if (elem == null) continue;

                    // Update LOC/ZONE from spatial data before re-tagging
                    string detectedLoc = SpatialAutoDetect.DetectLoc(doc, elem, roomIndex, projectLoc);
                    if (!string.IsNullOrEmpty(detectedLoc))
                        ParameterHelpers.SetString(elem, ParamRegistry.LOC, detectedLoc, overwrite: true);
                    string detectedZone = SpatialAutoDetect.DetectZone(doc, elem, roomIndex);
                    if (!string.IsNullOrEmpty(detectedZone))
                        ParameterHelpers.SetString(elem, ParamRegistry.ZONE, detectedZone, overwrite: true);

                    if (TagConfig.BuildAndWriteTag(doc, elem, seqCounters,
                        skipComplete: false,
                        existingTags: tagIndex,
                        collisionMode: TagCollisionMode.Overwrite))
                        retagged++;

                    // Rebuild TAG7 + sub-sections with updated tokens
                    string catRT = ParameterHelpers.GetCategoryName(elem);
                    string[] tvRT = ParamRegistry.ReadTokenValues(elem);
                    TagConfig.WriteTag7All(doc, elem, catRT, tvRT, overwrite: true);
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
    [Regeneration(RegenerationOption.Manual)]
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
                string tag = ParameterHelpers.GetString(elem, ParamRegistry.TAG1);
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
            int fixedCount = 0;

            using (Transaction tx = new Transaction(doc, "STING Fix Duplicates"))
            {
                tx.Start();
                foreach (var kvp in duplicates)
                {
                    // Skip the first element (keep its tag); fix the rest
                    for (int i = 1; i < kvp.Value.Count; i++)
                    {
                        Element elem = kvp.Value[i];
                        string catName = ParameterHelpers.GetCategoryName(elem);
                        string disc = ParameterHelpers.GetString(elem, ParamRegistry.DISC);
                        string loc = ParameterHelpers.GetString(elem, ParamRegistry.LOC);
                        string zone = ParameterHelpers.GetString(elem, ParamRegistry.ZONE);
                        string lvl = ParameterHelpers.GetString(elem, ParamRegistry.LVL);
                        string sys = ParameterHelpers.GetString(elem, ParamRegistry.SYS);
                        string func = ParameterHelpers.GetString(elem, ParamRegistry.FUNC);
                        string prod = ParameterHelpers.GetString(elem, ParamRegistry.PROD);

                        // Guaranteed defaults for empty tokens
                        if (string.IsNullOrEmpty(disc))
                            disc = TagConfig.DiscMap.TryGetValue(catName, out string dd) ? dd : "A";
                        if (string.IsNullOrEmpty(loc)) loc = "BLD1";
                        if (string.IsNullOrEmpty(zone)) zone = "Z01";
                        if (string.IsNullOrEmpty(lvl)) lvl = "L00";
                        if (string.IsNullOrEmpty(sys)) sys = TagConfig.GetDiscDefaultSysCode(disc);
                        if (string.IsNullOrEmpty(func))
                        {
                            func = TagConfig.GetSmartFuncCode(elem, sys);
                            if (string.IsNullOrEmpty(func))
                                func = TagConfig.FuncMap.TryGetValue(sys, out string fv) ? fv : "GEN";
                        }
                        if (string.IsNullOrEmpty(prod))
                            prod = TagConfig.GetFamilyAwareProdCode(elem, catName);

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
                            newSeq = seqCounters[seqKey].ToString().PadLeft(ParamRegistry.NumPad, '0');
                            newTag = string.Join(ParamRegistry.Separator, disc, loc, zone, lvl, sys, func, prod, newSeq);
                        } while (tagIndex.Contains(newTag) && safety-- > 0);

                        tagIndex.Add(newTag);
                        ParameterHelpers.SetString(elem, ParamRegistry.SEQ, newSeq, overwrite: true);
                        ParameterHelpers.SetString(elem, ParamRegistry.TAG1, newTag, overwrite: true);

                        // Update containers and TAG7 + sub-sections with the new tag
                        try
                        {
                            string[] tokenVals = ParamRegistry.ReadTokenValues(elem);
                            if (tokenVals.Any(v => !string.IsNullOrEmpty(v)))
                            {
                                ParamRegistry.WriteContainers(elem, tokenVals, catName, overwrite: true);
                                TagConfig.WriteTag7All(doc, elem, catName, tokenVals, overwrite: true);
                            }
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"FixDuplicates: container write failed for {elem.Id}: {ex.Message}");
                        }

                        fixedCount++;
                    }
                }
                tx.Commit();
            }

            // GAP-010: Post-fix duplicate scan to verify all tags are now unique
            var postIndex = TagConfig.BuildExistingTagIndex(doc);
            var postTagMap = new Dictionary<string, int>();
            foreach (Element elem in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(elem);
                if (!known.Contains(cat)) continue;
                string tag = ParameterHelpers.GetString(elem, ParamRegistry.TAG1);
                if (string.IsNullOrEmpty(tag)) continue;
                if (!postTagMap.ContainsKey(tag)) postTagMap[tag] = 0;
                postTagMap[tag]++;
            }
            int remainingDupes = postTagMap.Count(kvp => kvp.Value > 1);

            string dupeNote = remainingDupes > 0
                ? $"\nWARNING: {remainingDupes} tag value(s) still have duplicates — check log for details."
                : "\nAll tags are now unique.";
            if (remainingDupes > 0)
                StingLog.Warn($"FixDuplicates: post-fix scan found {remainingDupes} remaining duplicate tag values");

            TaskDialog.Show("Fix Duplicates",
                $"Fixed {fixedCount} duplicate tags across {duplicates.Count} tag values.{dupeNote}");
            return Result.Succeeded;
        }
    }

    /// <summary>Delete tag parameter values from selected elements.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DeleteTagsCommand : IExternalCommand
    {
        private static readonly string[] TagParams = new[]
        {
            ParamRegistry.TAG1, ParamRegistry.TAG2, ParamRegistry.TAG3,
            ParamRegistry.TAG4, ParamRegistry.TAG5, ParamRegistry.TAG6,
            ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
            ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC,
            ParamRegistry.PROD, ParamRegistry.SEQ, ParamRegistry.STATUS,
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

                    // Also clear all discipline-specific containers
                    if (any)
                    {
                        try
                        {
                            string catName = ParameterHelpers.GetCategoryName(elem);
                            var emptyTokens = new string[8]; // All empty strings
                            for (int i = 0; i < emptyTokens.Length; i++) emptyTokens[i] = "";
                            ParamRegistry.WriteContainers(elem, emptyTokens, catName, overwrite: true);
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"DeleteTags: container clear failed for {elem.Id}: {ex.Message}");
                        }
                        cleared++;
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Delete Tags", $"Cleared tags and containers from {cleared} elements.");
            return Result.Succeeded;
        }
    }

    /// <summary>Renumber sequence numbers for selected elements within their group key.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
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
                string disc = ParameterHelpers.GetString(elem, ParamRegistry.DISC);
                string sys = ParameterHelpers.GetString(elem, ParamRegistry.SYS);
                string lvl = ParameterHelpers.GetString(elem, ParamRegistry.LVL);
                if (string.IsNullOrEmpty(disc)) continue;

                string key = $"{disc}_{sys}_{lvl}";
                if (!groups.ContainsKey(key)) groups[key] = new List<Element>();
                groups[key].Add(elem);
            }

            // BUG-008: Build existing tag index for post-renumber collision check
            var existingTagIndex = TagConfig.BuildExistingTagIndex(doc);
            var newTags = new HashSet<string>(StringComparer.Ordinal);
            int collisions = 0;

            int renumbered = 0;
            using (Transaction tx = new Transaction(doc, "STING Renumber Tags"))
            {
                tx.Start();
                foreach (var kvp in groups)
                {
                    int seq = 1;
                    foreach (Element elem in kvp.Value)
                    {
                        string seqStr = seq.ToString().PadLeft(ParamRegistry.NumPad, '0');
                        ParameterHelpers.SetString(elem, ParamRegistry.SEQ, seqStr, overwrite: true);

                        // Rebuild assembled tag with guaranteed defaults for empty tokens
                        string catName = ParameterHelpers.GetCategoryName(elem);
                        string disc = ParameterHelpers.GetString(elem, ParamRegistry.DISC);
                        string loc = ParameterHelpers.GetString(elem, ParamRegistry.LOC);
                        string zone = ParameterHelpers.GetString(elem, ParamRegistry.ZONE);
                        string lvl = ParameterHelpers.GetString(elem, ParamRegistry.LVL);
                        string sys = ParameterHelpers.GetString(elem, ParamRegistry.SYS);
                        string func = ParameterHelpers.GetString(elem, ParamRegistry.FUNC);
                        string prod = ParameterHelpers.GetString(elem, ParamRegistry.PROD);

                        if (string.IsNullOrEmpty(disc))
                            disc = TagConfig.DiscMap.TryGetValue(catName, out string dd) ? dd : "A";
                        if (string.IsNullOrEmpty(loc)) loc = "BLD1";
                        if (string.IsNullOrEmpty(zone)) zone = "Z01";
                        if (string.IsNullOrEmpty(lvl)) lvl = "L00";
                        if (string.IsNullOrEmpty(sys)) sys = TagConfig.GetDiscDefaultSysCode(disc);
                        if (string.IsNullOrEmpty(func))
                        {
                            func = TagConfig.GetSmartFuncCode(elem, sys);
                            if (string.IsNullOrEmpty(func))
                                func = TagConfig.FuncMap.TryGetValue(sys, out string fv) ? fv : "GEN";
                        }
                        if (string.IsNullOrEmpty(prod))
                            prod = TagConfig.GetFamilyAwareProdCode(elem, catName);

                        string tag = string.Join(ParamRegistry.Separator,
                            disc, loc, zone, lvl, sys, func, prod, seqStr);

                        // BUG-008: Check for collision with existing tags or within this batch
                        if (existingTagIndex.Contains(tag) || newTags.Contains(tag))
                        {
                            // Auto-increment SEQ to resolve collision
                            string groupKey = string.Join(ParamRegistry.Separator,
                                disc, loc, zone, lvl, sys, func, prod);
                            int incSeq = seq + 1;
                            string incTag;
                            do
                            {
                                string incSeqStr = incSeq.ToString().PadLeft(ParamRegistry.NumPad, '0');
                                incTag = groupKey + ParamRegistry.Separator + incSeqStr;
                                incSeq++;
                            }
                            while (existingTagIndex.Contains(incTag) || newTags.Contains(incTag));

                            // Use resolved tag and SEQ
                            int resolvedSeq = incSeq - 1;
                            seqStr = resolvedSeq.ToString().PadLeft(ParamRegistry.NumPad, '0');
                            tag = groupKey + ParamRegistry.Separator + seqStr;
                            ParameterHelpers.SetString(elem, ParamRegistry.SEQ, seqStr, overwrite: true);
                            collisions++;
                            StingLog.Warn($"Renumber collision: element {elem.Id} SEQ auto-incremented to {seqStr}");
                        }
                        newTags.Add(tag);

                        ParameterHelpers.SetString(elem, ParamRegistry.TAG1, tag, overwrite: true);

                        // Update containers and TAG7 + sub-sections with the new tag
                        try
                        {
                            string[] tokenVals = ParamRegistry.ReadTokenValues(elem);
                            if (tokenVals.Any(v => !string.IsNullOrEmpty(v)))
                            {
                                ParamRegistry.WriteContainers(elem, tokenVals, catName, overwrite: true);
                                TagConfig.WriteTag7All(doc, elem, catName, tokenVals, overwrite: true);
                            }
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Renumber: container write failed for {elem.Id}: {ex.Message}");
                        }

                        seq++;
                        renumbered++;
                    }
                }
                tx.Commit();
            }

            string collisionNote = collisions > 0
                ? $"\n{collisions} collision(s) auto-resolved by incrementing SEQ."
                : "";
            TaskDialog.Show("Renumber",
                $"Renumbered {renumbered} elements in {groups.Count} groups.{collisionNote}");
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
            sb.AppendLine("ElementId,Category,Tag,DISC,LOC,ZONE,LVL,SYS,FUNC,PROD,SEQ,STATUS,REV,Valid,FullyResolved");

            int total = 0;
            foreach (Element elem in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(elem);
                if (!known.Contains(cat)) continue;

                total++;
                string tag = ParameterHelpers.GetString(elem, ParamRegistry.TAG1);
                string disc = ParameterHelpers.GetString(elem, ParamRegistry.DISC);
                string loc = ParameterHelpers.GetString(elem, ParamRegistry.LOC);
                string zone = ParameterHelpers.GetString(elem, ParamRegistry.ZONE);
                string lvl = ParameterHelpers.GetString(elem, ParamRegistry.LVL);
                string sys = ParameterHelpers.GetString(elem, ParamRegistry.SYS);
                string func = ParameterHelpers.GetString(elem, ParamRegistry.FUNC);
                string prod = ParameterHelpers.GetString(elem, ParamRegistry.PROD);
                string seq = ParameterHelpers.GetString(elem, ParamRegistry.SEQ);
                string status = ParameterHelpers.GetString(elem, ParamRegistry.STATUS);
                string rev = ParameterHelpers.GetString(elem, ParamRegistry.REV);
                bool valid = TagConfig.TagIsComplete(tag);
                bool resolved = TagConfig.TagIsFullyResolved(tag);

                sb.AppendLine($"{elem.Id},\"{CsvEscape(cat)}\",\"{CsvEscape(tag)}\",{disc},{loc},{zone},{lvl},{sys},{func},{prod},{seq},{status},{rev},{valid},{resolved}");
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

                string tag = ParameterHelpers.GetString(elem, ParamRegistry.TAG1);
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
    [Regeneration(RegenerationOption.Manual)]
    public class HighlightInvalidCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            // Red = missing, Orange = incomplete, Yellow = ISO violation, Purple = placeholder
            FillPatternElement solidFill = ParameterHelpers.GetSolidFillPattern(doc);

            var red = new OverrideGraphicSettings();
            red.SetProjectionLineColor(new Color(255, 0, 0));
            red.SetProjectionLineWeight(5);
            if (solidFill != null)
            {
                red.SetSurfaceForegroundPatternId(solidFill.Id);
                red.SetSurfaceForegroundPatternColor(new Color(255, 200, 200));
                red.SetSurfaceTransparency(50);
            }

            var orange = new OverrideGraphicSettings();
            orange.SetProjectionLineColor(new Color(255, 165, 0));
            orange.SetProjectionLineWeight(4);
            if (solidFill != null)
            {
                orange.SetSurfaceForegroundPatternId(solidFill.Id);
                orange.SetSurfaceForegroundPatternColor(new Color(255, 230, 180));
                orange.SetSurfaceTransparency(50);
            }

            var yellow = new OverrideGraphicSettings();
            yellow.SetProjectionLineColor(new Color(255, 255, 0));
            yellow.SetProjectionLineWeight(3);
            if (solidFill != null)
            {
                yellow.SetSurfaceForegroundPatternId(solidFill.Id);
                yellow.SetSurfaceForegroundPatternColor(new Color(255, 255, 200));
                yellow.SetSurfaceTransparency(50);
            }

            var purple = new OverrideGraphicSettings();
            purple.SetProjectionLineColor(new Color(160, 32, 240));
            purple.SetProjectionLineWeight(3);
            if (solidFill != null)
            {
                purple.SetSurfaceForegroundPatternId(solidFill.Id);
                purple.SetSurfaceForegroundPatternColor(new Color(220, 200, 240));
                purple.SetSurfaceTransparency(50);
            }

            int missing = 0, incomplete = 0, isoInvalid = 0, unresolved = 0;

            using (Transaction tx = new Transaction(doc, "STING Highlight Invalid"))
            {
                tx.Start();
                foreach (Element elem in new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType())
                {
                    string cat = ParameterHelpers.GetCategoryName(elem);
                    if (!known.Contains(cat)) continue;

                    string tag = ParameterHelpers.GetString(elem, ParamRegistry.TAG1);
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
                    else if (!TagConfig.TagIsFullyResolved(tag))
                    {
                        view.SetElementOverrides(elem.Id, purple);
                        unresolved++;
                    }
                    else
                    {
                        // Check for ISO cross-validation errors (PROD/FUNC/DISC mismatches)
                        var isoErrors = ISO19650Validator.ValidateElement(elem);
                        if (isoErrors.Count > 0)
                        {
                            view.SetElementOverrides(elem.Id, yellow);
                            isoInvalid++;
                        }
                    }
                }
                tx.Commit();
            }

            var resultDlg = new TaskDialog("Highlight Invalid");
            resultDlg.MainContent =
                $"Red (missing tag):     {missing}\n" +
                $"Orange (incomplete):   {incomplete}\n" +
                $"Purple (placeholders): {unresolved}\n" +
                $"Yellow (ISO issues):   {isoInvalid}\n\n" +
                "Use 'Clear Overrides' to reset.\n\n" +
                "Create a persistent color legend?";
            resultDlg.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
            resultDlg.DefaultButton = TaskDialogResult.No;

            if (resultDlg.Show() == TaskDialogResult.Yes)
            {
                var entries = Tags.LegendBuilder.FromHighlightInvalid(missing, incomplete, unresolved, isoInvalid);
                var config = new Tags.LegendBuilder.LegendConfig
                {
                    Title = "Tag Validation Status",
                    Subtitle = "Highlight Invalid Results",
                    Footer = $"View: {view.Name} | Generated by STING Tools",
                };

                using (Transaction ltx = new Transaction(doc, "STING Validation Legend"))
                {
                    ltx.Start();
                    var legendView = Tags.LegendBuilder.CreateLegendView(doc, entries, config);
                    ltx.Commit();

                    if (legendView != null)
                        TaskDialog.Show("Legend Created", $"Legend view: '{legendView.Name}'\nPlace on a sheet for documentation.");
                }
            }

            return Result.Succeeded;
        }
    }

    /// <summary>Clear graphic overrides from all elements in active view.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
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
    [Regeneration(RegenerationOption.Manual)]
    public class CopyTagsCommand : IExternalCommand
    {
        // Only copy individual tokens (not TAG1-TAG6 containers which embed SEQ).
        // After copying, user should run "Build Tags" to reassemble with unique SEQ.
        private static readonly string[] CopyParams = new[]
        {
            ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
            ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC,
            ParamRegistry.PROD, ParamRegistry.STATUS,
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

            string sourceTag = values.TryGetValue(ParamRegistry.TAG1, out string t) ? t : "(empty)";

            // Check for discipline mismatches between source and targets
            string sourceCat = ParameterHelpers.GetCategoryName(source);
            string sourceDisc = values.TryGetValue(ParamRegistry.DISC, out string sd) ? sd : "";
            int discMismatches = 0;
            for (int i = 1; i < selected.Count; i++)
            {
                Element target = doc.GetElement(selected[i]);
                if (target == null) continue;
                string targetCat = ParameterHelpers.GetCategoryName(target);
                string expectedDisc = TagConfig.DiscMap.TryGetValue(targetCat, out string td2) ? td2 : "A";
                if (!string.IsNullOrEmpty(sourceDisc) && sourceDisc != expectedDisc)
                    discMismatches++;
            }

            string warnText = "";
            if (discMismatches > 0)
                warnText = $"\n\nWARNING: {discMismatches} targets have different expected disciplines. " +
                    "Copied DISC may cause cross-validation errors.";

            TaskDialog confirm = new TaskDialog("Copy Tags");
            confirm.MainInstruction = $"Copy tags from Element {source.Id}?";
            confirm.MainContent =
                $"Source tag: {sourceTag}\n" +
                $"Source category: {sourceCat}\n" +
                $"Target: {selected.Count - 1} elements\n\n" +
                "Copies all tag values except SEQ (sequence stays unique)." + warnText;
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

                    // Update containers with copied values
                    try
                    {
                        string catName = ParameterHelpers.GetCategoryName(target);
                        string[] tokenVals = ParamRegistry.ReadTokenValues(target);
                        if (tokenVals.Any(v => !string.IsNullOrEmpty(v)))
                            ParamRegistry.WriteContainers(target, tokenVals, catName, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"CopyTags: container write failed for {target.Id}: {ex.Message}");
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
    [Regeneration(RegenerationOption.Manual)]
    public class SwapTagsCommand : IExternalCommand
    {
        private static readonly string[] SwapParams = new[]
        {
            ParamRegistry.TAG1, ParamRegistry.TAG2, ParamRegistry.TAG3,
            ParamRegistry.TAG4, ParamRegistry.TAG5, ParamRegistry.TAG6,
            ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
            ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC,
            ParamRegistry.PROD, ParamRegistry.SEQ, ParamRegistry.STATUS,
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

            string tagA = ParameterHelpers.GetString(a, ParamRegistry.TAG1);
            string tagB = ParameterHelpers.GetString(b, ParamRegistry.TAG1);

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

                // Update containers for both elements
                try
                {
                    string catA = ParameterHelpers.GetCategoryName(a);
                    string[] tokensA = ParamRegistry.ReadTokenValues(a);
                    if (tokensA.Any(v => !string.IsNullOrEmpty(v)))
                        ParamRegistry.WriteContainers(a, tokensA, catA, overwrite: true);

                    string catB = ParameterHelpers.GetCategoryName(b);
                    string[] tokensB = ParamRegistry.ReadTokenValues(b);
                    if (tokensB.Any(v => !string.IsNullOrEmpty(v)))
                        ParamRegistry.WriteContainers(b, tokensB, catB, overwrite: true);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"SwapTags: container write failed: {ex.Message}");
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
                string disc = ParameterHelpers.GetString(elem, ParamRegistry.DISC);
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
                td.AddCommandLink((TaskDialogCommandLinkId)(i + 1001),
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

    // ══════════════════════════════════════════════════════════════════
    // Tag & Leader Appearance Commands
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Shared helper for annotation tag/leader color operations.
    /// </summary>
    internal static class AnnotationColorHelper
    {
        /// <summary>Discipline → Color mapping for annotation tags.</summary>
        public static readonly Dictionary<string, Color> DisciplineColors =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            { "M", new Color(0, 128, 255) },       // Blue
            { "E", new Color(255, 180, 0) },        // Gold
            { "P", new Color(0, 180, 0) },           // Green
            { "A", new Color(120, 120, 120) },       // Grey
            { "S", new Color(200, 0, 0) },           // Red
            { "FP", new Color(255, 100, 0) },        // Orange
            { "LV", new Color(160, 0, 200) },        // Purple
            { "G", new Color(128, 80, 0) },          // Brown
        };

        /// <summary>Named quick-pick colors.</summary>
        public static readonly (string name, Color color)[] QuickColors = new[]
        {
            ("Red", new Color(220, 20, 20)),
            ("Blue", new Color(0, 100, 220)),
            ("Green", new Color(0, 160, 0)),
            ("Black", new Color(0, 0, 0)),
            ("Orange", new Color(255, 140, 0)),
            ("Purple", new Color(140, 0, 200)),
            ("Grey", new Color(128, 128, 128)),
            ("Cyan", new Color(0, 180, 200)),
        };

        /// <summary>Find the solid fill pattern (needed for surface overrides).</summary>
        public static FillPatternElement FindSolidFill(Document doc)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
            }
            catch { return null; }
        }

        /// <summary>Build override settings for annotation coloring.</summary>
        public static OverrideGraphicSettings BuildAnnotationOverride(
            Color lineColor, int lineWeight = -1)
        {
            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(lineColor);
            if (lineWeight > 0) ogs.SetProjectionLineWeight(lineWeight);
            return ogs;
        }

        /// <summary>
        /// Build granular override settings with SEPARATE control of text (projection line),
        /// bounding box (surface pattern), and leader (cut line) aspects.
        /// In Revit, OverrideGraphicSettings exposes:
        ///   - Projection line color/weight → controls text + leader rendering
        ///   - Surface foreground pattern/color → controls tag bounding box fill
        ///   - Cut line color → we repurpose to differentiate (for tags in section)
        ///   - Halftone → mutes all aspects equally
        ///   - Transparency → controls surface fill transparency (box visibility)
        ///
        /// Strategy for "separate" control:
        ///   - Text color = SetProjectionLineColor (this is what renders tag text)
        ///   - Box fill color = SetSurfaceForegroundPatternColor + solid fill pattern
        ///   - Box visibility = transparency (0 = opaque, 100 = invisible)
        ///   - Leader visibility = controlled by toggling HasLeader on/off
        ///   - We split tags into with/without leaders and apply different overrides
        /// </summary>
        public static OverrideGraphicSettings BuildGranularOverride(
            Document doc,
            Color textColor = null,
            Color boxColor = null,
            int boxTransparency = -1,
            int lineWeight = -1,
            Color cutLineColor = null)
        {
            var ogs = new OverrideGraphicSettings();

            // Text + line color (projection line color controls tag text rendering)
            if (textColor != null)
                ogs.SetProjectionLineColor(textColor);

            if (lineWeight > 0)
                ogs.SetProjectionLineWeight(lineWeight);

            // Bounding box fill — use surface foreground pattern color
            if (boxColor != null)
            {
                var solidFill = FindSolidFill(doc);
                if (solidFill != null)
                {
                    ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                    ogs.SetSurfaceForegroundPatternColor(boxColor);
                }
            }

            // Box visibility via transparency (0=visible, 100=invisible)
            if (boxTransparency >= 0 && boxTransparency <= 100)
                ogs.SetSurfaceTransparency(boxTransparency);

            // Cut line color for section-cut tag aspects
            if (cutLineColor != null)
                ogs.SetCutLineColor(cutLineColor);

            return ogs;
        }

        /// <summary>Prompt user to pick a color from the standard 8 quick-pick options.</summary>
        public static Color PickColor(string title, string instruction)
        {
            TaskDialog dlg = new TaskDialog(title);
            dlg.MainInstruction = instruction;
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Red (220,20,20)", "Highlight / QA checking");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Blue (0,100,220)", "MEP / standard annotation");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Black (0,0,0)", "Print-ready / standard");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "More Colors...", "Extended palette (8 more options)");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: return new Color(220, 20, 20);
                case TaskDialogResult.CommandLink2: return new Color(0, 100, 220);
                case TaskDialogResult.CommandLink3: return new Color(0, 0, 0);
                case TaskDialogResult.CommandLink4: return PickColorExtended(title, instruction);
                default: return null;
            }
        }

        /// <summary>Prompt user to pick a color from an extended 8-option palette.</summary>
        public static Color PickColorExtended(string title, string instruction)
        {
            TaskDialog dlg = new TaskDialog(title);
            dlg.MainInstruction = instruction;
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Grey (128,128,128)", "Subtle / background");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Orange (255,140,0)", "Warning / attention");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Cyan (0,180,200)", "Reference / info");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "More Colors...", "Extra palette (green, white, yellow, purple)");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: return new Color(128, 128, 128);
                case TaskDialogResult.CommandLink2: return new Color(255, 140, 0);
                case TaskDialogResult.CommandLink3: return new Color(0, 180, 200);
                case TaskDialogResult.CommandLink4: return PickColorTertiary(title, instruction);
                default: return null;
            }
        }

        /// <summary>Tertiary color options (green, white, yellow, purple).</summary>
        public static Color PickColorTertiary(string title, string instruction)
        {
            TaskDialog dlg = new TaskDialog(title);
            dlg.MainInstruction = instruction;
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Green (0,160,0)", "Approved / verified");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "White (255,255,255)", "Clean / background");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Yellow (255,200,0)", "Caution / highlight");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Purple (140,0,200)", "Special / highlight");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: return new Color(0, 160, 0);
                case TaskDialogResult.CommandLink2: return new Color(255, 255, 255);
                case TaskDialogResult.CommandLink3: return new Color(255, 200, 0);
                case TaskDialogResult.CommandLink4: return new Color(140, 0, 200);
                default: return null;
            }
        }

        /// <summary>Get the transparency from a color name hint (for box visibility).</summary>
        public static int GetBoxTransparencyChoice(string title)
        {
            TaskDialog dlg = new TaskDialog(title);
            dlg.MainInstruction = "Tag bounding box visibility";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Fully Visible", "Opaque bounding box (0% transparent)");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Semi-Transparent", "50% transparent box background");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Faded", "80% transparent — subtle box");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Hidden (Invisible)", "100% transparent — no visible box");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: return 0;
                case TaskDialogResult.CommandLink2: return 50;
                case TaskDialogResult.CommandLink3: return 80;
                case TaskDialogResult.CommandLink4: return 100;
                default: return -1;
            }
        }

        /// <summary>
        /// Get annotation tags from selection or all in view.
        /// Returns (tags, isFromSelection).
        /// </summary>
        public static (List<IndependentTag> tags, bool fromSelection)
            GetTargetTags(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            var selIds = uidoc.Selection.GetElementIds();
            if (selIds.Count > 0)
            {
                var tags = selIds
                    .Select(id => doc.GetElement(id))
                    .OfType<IndependentTag>()
                    .ToList();
                if (tags.Count > 0) return (tags, true);
            }

            var allTags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();
            return (allTags, false);
        }

        /// <summary>Get the discipline code from the element that a tag hosts.</summary>
        public static string GetTagDiscipline(IndependentTag tag, Document doc)
        {
            try
            {
                var hostIds = tag.GetTaggedLocalElementIds();
                if (hostIds.Count == 0) return null;
                Element host = doc.GetElement(hostIds.First());
                if (host == null) return null;
                return ParameterHelpers.GetString(host, ParamRegistry.DISC);
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// Color annotation tag text and leaders by discipline (M=Blue, E=Gold, P=Green, etc.).
    /// Applies per-element graphic overrides to IndependentTag annotation elements.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ColorTagsByDisciplineCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            var (tags, fromSel) = AnnotationColorHelper.GetTargetTags(uidoc);
            if (tags.Count == 0)
            {
                TaskDialog.Show("Color Tags", "No annotation tags found in view or selection.");
                return Result.Succeeded;
            }

            int colored = 0;
            var discCounts = new Dictionary<string, int>();

            using (Transaction tx = new Transaction(doc, "STING Color Tags by Discipline"))
            {
                tx.Start();
                foreach (var tag in tags)
                {
                    string disc = AnnotationColorHelper.GetTagDiscipline(tag, doc);
                    if (string.IsNullOrEmpty(disc)) continue;

                    if (AnnotationColorHelper.DisciplineColors.TryGetValue(disc, out Color col))
                    {
                        var ogs = AnnotationColorHelper.BuildAnnotationOverride(col, 2);
                        view.SetElementOverrides(tag.Id, ogs);
                        colored++;

                        if (!discCounts.ContainsKey(disc)) discCounts[disc] = 0;
                        discCounts[disc]++;
                    }
                }
                tx.Commit();
            }

            var report = new StringBuilder();
            report.AppendLine($"Colored {colored} of {tags.Count} annotation tags by discipline:");
            foreach (var kvp in discCounts.OrderByDescending(x => x.Value))
            {
                var c = AnnotationColorHelper.DisciplineColors[kvp.Key];
                report.AppendLine($"  {kvp.Key}: {kvp.Value} tags (RGB {c.Red},{c.Green},{c.Blue})");
            }
            report.AppendLine();
            report.AppendLine("Create a persistent color legend?");

            var resultDlg = new TaskDialog("Color Tags by Discipline");
            resultDlg.MainContent = report.ToString();
            resultDlg.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
            resultDlg.DefaultButton = TaskDialogResult.No;

            if (resultDlg.Show() == TaskDialogResult.Yes)
            {
                var entries = Tags.LegendBuilder.FromDisciplineColors(
                    AnnotationColorHelper.DisciplineColors, discCounts);
                var config = new Tags.LegendBuilder.LegendConfig
                {
                    Title = "Discipline Color Coding",
                    Subtitle = "Annotation Tags by Discipline",
                    Footer = $"View: {view.Name} | Generated by STING Tools",
                };

                using (Transaction ltx = new Transaction(doc, "STING Discipline Legend"))
                {
                    ltx.Start();
                    var legendView = Tags.LegendBuilder.CreateLegendView(doc, entries, config);
                    ltx.Commit();

                    if (legendView != null)
                        TaskDialog.Show("Legend Created", $"Legend view: '{legendView.Name}'\nPlace on a sheet for documentation.");
                }
            }

            StingLog.Info($"ColorTagsByDiscipline: colored={colored}");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Set tag text color: applies a chosen color to tag annotation elements.
    /// User picks from quick color options.
    /// In Revit, the projection line color controls tag text rendering.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetTagTextColorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            var (tags, fromSel) = AnnotationColorHelper.GetTargetTags(uidoc);
            if (tags.Count == 0)
            {
                TaskDialog.Show("Tag Text Color", "No annotation tags found.");
                return Result.Succeeded;
            }

            // Filter to tags WITHOUT leaders (text-only tags)
            var textOnlyTags = tags.Where(t =>
            {
                try { return !t.HasLeader; } catch { return true; }
            }).ToList();

            TaskDialog dlg = new TaskDialog("Set Tag Text Color");
            dlg.MainInstruction = $"Choose color for {tags.Count} tags ({textOnlyTags.Count} without leaders):";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Red", "RGB 220,20,20 — for QA/checking");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Blue", "RGB 0,100,220 — for MEP tags");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Black", "RGB 0,0,0 — standard/print");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Green", "RGB 0,160,0 — for approved/verified");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            Color chosen;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: chosen = new Color(220, 20, 20); break;
                case TaskDialogResult.CommandLink2: chosen = new Color(0, 100, 220); break;
                case TaskDialogResult.CommandLink3: chosen = new Color(0, 0, 0); break;
                case TaskDialogResult.CommandLink4: chosen = new Color(0, 160, 0); break;
                default: return Result.Cancelled;
            }

            int colored = 0;
            var ogs = AnnotationColorHelper.BuildAnnotationOverride(chosen, -1);

            using (Transaction tx = new Transaction(doc, "STING Set Tag Text Color"))
            {
                tx.Start();
                foreach (var tag in tags)
                {
                    try
                    {
                        view.SetElementOverrides(tag.Id, ogs);
                        colored++;
                    }
                    catch { }
                }
                tx.Commit();
            }

            TaskDialog.Show("Tag Text Color",
                $"Applied color (RGB {chosen.Red},{chosen.Green},{chosen.Blue}) to {colored} tags.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Set leader line color: applies a chosen color to tags WITH leaders only.
    /// Tags without leaders are unaffected, giving the visual effect of separate
    /// text vs leader colors.
    ///
    /// Workflow for different text/leader colors:
    ///   1. Use 'Set Tag Text Color' to color ALL tags (text + any leaders)
    ///   2. Use 'Set Leader Color' to override color on leader-bearing tags only
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetLeaderColorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            var (allTags, fromSel) = AnnotationColorHelper.GetTargetTags(uidoc);
            var leaderTags = allTags.Where(t =>
            {
                try { return t.HasLeader; } catch { return false; }
            }).ToList();

            if (leaderTags.Count == 0)
            {
                TaskDialog.Show("Leader Color", "No tags with leaders found.");
                return Result.Succeeded;
            }

            TaskDialog dlg = new TaskDialog("Set Leader Color");
            dlg.MainInstruction = $"Choose leader color for {leaderTags.Count} tags with leaders:";
            dlg.MainContent =
                "This overrides the color of tags WITH leaders only.\n" +
                "Tags without leaders keep their current color.\n\n" +
                "Tip: Set text color first, then leader color for different colors.";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Grey", "RGB 128,128,128 — subtle leaders");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Red", "RGB 220,20,20 — highlight leaders");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Blue", "RGB 0,100,220 — standard leaders");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Black", "RGB 0,0,0 — print-ready leaders");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            Color chosen;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: chosen = new Color(128, 128, 128); break;
                case TaskDialogResult.CommandLink2: chosen = new Color(220, 20, 20); break;
                case TaskDialogResult.CommandLink3: chosen = new Color(0, 100, 220); break;
                case TaskDialogResult.CommandLink4: chosen = new Color(0, 0, 0); break;
                default: return Result.Cancelled;
            }

            int colored = 0;
            var ogs = AnnotationColorHelper.BuildAnnotationOverride(chosen, -1);

            using (Transaction tx = new Transaction(doc, "STING Set Leader Color"))
            {
                tx.Start();
                foreach (var tag in leaderTags)
                {
                    try
                    {
                        view.SetElementOverrides(tag.Id, ogs);
                        colored++;
                    }
                    catch { }
                }
                tx.Commit();
            }

            TaskDialog.Show("Leader Color",
                $"Applied color (RGB {chosen.Red},{chosen.Green},{chosen.Blue}) to {colored} leader tags.\n" +
                $"{allTags.Count - leaderTags.Count} tags without leaders unaffected.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Split color: apply one color to tag text (tags without leaders) and a different
    /// color to leader-bearing tags. Quick two-step color application in one command.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SplitTagLeaderColorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            var (allTags, _) = AnnotationColorHelper.GetTargetTags(uidoc);
            if (allTags.Count == 0)
            {
                TaskDialog.Show("Split Color", "No annotation tags found.");
                return Result.Succeeded;
            }

            var withLeaders = new List<IndependentTag>();
            var withoutLeaders = new List<IndependentTag>();
            foreach (var tag in allTags)
            {
                try
                {
                    if (tag.HasLeader) withLeaders.Add(tag);
                    else withoutLeaders.Add(tag);
                }
                catch { withoutLeaders.Add(tag); }
            }

            // Step 1: Pick text color
            TaskDialog textDlg = new TaskDialog("Split Color — Step 1: Text Tags");
            textDlg.MainInstruction = $"Color for {withoutLeaders.Count} tags WITHOUT leaders (text only):";
            textDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Black", "RGB 0,0,0");
            textDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Blue", "RGB 0,100,220");
            textDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Red", "RGB 220,20,20");
            textDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Green", "RGB 0,160,0");
            textDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            Color textColor;
            switch (textDlg.Show())
            {
                case TaskDialogResult.CommandLink1: textColor = new Color(0, 0, 0); break;
                case TaskDialogResult.CommandLink2: textColor = new Color(0, 100, 220); break;
                case TaskDialogResult.CommandLink3: textColor = new Color(220, 20, 20); break;
                case TaskDialogResult.CommandLink4: textColor = new Color(0, 160, 0); break;
                default: return Result.Cancelled;
            }

            // Step 2: Pick leader color
            TaskDialog leaderDlg = new TaskDialog("Split Color — Step 2: Leader Tags");
            leaderDlg.MainInstruction = $"Color for {withLeaders.Count} tags WITH leaders:";
            leaderDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Grey", "RGB 128,128,128");
            leaderDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Red", "RGB 220,20,20");
            leaderDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Orange", "RGB 255,140,0");
            leaderDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Cyan", "RGB 0,180,200");
            leaderDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            Color leaderColor;
            switch (leaderDlg.Show())
            {
                case TaskDialogResult.CommandLink1: leaderColor = new Color(128, 128, 128); break;
                case TaskDialogResult.CommandLink2: leaderColor = new Color(220, 20, 20); break;
                case TaskDialogResult.CommandLink3: leaderColor = new Color(255, 140, 0); break;
                case TaskDialogResult.CommandLink4: leaderColor = new Color(0, 180, 200); break;
                default: return Result.Cancelled;
            }

            var textOgs = AnnotationColorHelper.BuildAnnotationOverride(textColor, -1);
            var leaderOgs = AnnotationColorHelper.BuildAnnotationOverride(leaderColor, -1);

            int textColored = 0, leaderColored = 0;
            using (Transaction tx = new Transaction(doc, "STING Split Tag/Leader Color"))
            {
                tx.Start();
                foreach (var tag in withoutLeaders)
                {
                    try { view.SetElementOverrides(tag.Id, textOgs); textColored++; }
                    catch { }
                }
                foreach (var tag in withLeaders)
                {
                    try { view.SetElementOverrides(tag.Id, leaderOgs); leaderColored++; }
                    catch { }
                }
                tx.Commit();
            }

            TaskDialog.Show("Split Color",
                $"Text tags (no leader): {textColored} colored " +
                $"(RGB {textColor.Red},{textColor.Green},{textColor.Blue})\n" +
                $"Leader tags: {leaderColored} colored " +
                $"(RGB {leaderColor.Red},{leaderColor.Green},{leaderColor.Blue})");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Clear annotation tag overrides: reset tag text and leader colors to default.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ClearAnnotationColorsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            var (tags, _) = AnnotationColorHelper.GetTargetTags(uidoc);
            if (tags.Count == 0)
            {
                TaskDialog.Show("Clear Colors", "No annotation tags found.");
                return Result.Succeeded;
            }

            int cleared = 0;
            var blank = new OverrideGraphicSettings();

            using (Transaction tx = new Transaction(doc, "STING Clear Annotation Colors"))
            {
                tx.Start();
                foreach (var tag in tags)
                {
                    try { view.SetElementOverrides(tag.Id, blank); cleared++; }
                    catch { }
                }
                tx.Commit();
            }

            TaskDialog.Show("Clear Colors",
                $"Reset color overrides on {cleared} annotation tags.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Master tag appearance command: separate control of TEXT colour, LEADER colour,
    /// and BOUNDING BOX colour + visibility. Applies different overrides to tags
    /// with leaders vs without leaders, giving visual separation of all three aspects.
    ///
    /// Revit API strategy:
    ///   - Text color → SetProjectionLineColor on tags WITHOUT leaders
    ///   - Leader color → SetProjectionLineColor on tags WITH leaders
    ///   - Box fill → SetSurfaceForegroundPatternColor (solid fill) on ALL tags
    ///   - Box visibility → SetSurfaceTransparency (0=opaque to 100=invisible)
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagAppearanceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            var (allTags, fromSel) = AnnotationColorHelper.GetTargetTags(uidoc);
            if (allTags.Count == 0)
            {
                TaskDialog.Show("Tag Appearance", "No annotation tags found in view or selection.");
                return Result.Succeeded;
            }

            var withLeaders = new List<IndependentTag>();
            var withoutLeaders = new List<IndependentTag>();
            foreach (var tag in allTags)
            {
                try { if (tag.HasLeader) withLeaders.Add(tag); else withoutLeaders.Add(tag); }
                catch { withoutLeaders.Add(tag); }
            }

            // Step 1: Text color
            Color textColor = AnnotationColorHelper.PickColor(
                "Tag Appearance — Step 1/4: Text Color",
                $"Choose TEXT color for {allTags.Count} tags:");
            if (textColor == null) return Result.Cancelled;

            // Step 2: Leader color (if any leaders exist)
            Color leaderColor = textColor;
            if (withLeaders.Count > 0)
            {
                Color picked = AnnotationColorHelper.PickColor(
                    "Tag Appearance — Step 2/4: Leader Color",
                    $"Choose LEADER color for {withLeaders.Count} tags with leaders:");
                if (picked == null) return Result.Cancelled;
                leaderColor = picked;
            }

            // Step 3: Bounding box color
            Color boxColor = AnnotationColorHelper.PickColorExtended(
                "Tag Appearance — Step 3/4: Box Color",
                "Choose BOUNDING BOX fill color:");
            if (boxColor == null) return Result.Cancelled;

            // Step 4: Bounding box visibility (transparency)
            int boxTransparency = AnnotationColorHelper.GetBoxTransparencyChoice(
                "Tag Appearance — Step 4/4: Box Visibility");
            if (boxTransparency < 0) return Result.Cancelled;

            // Build overrides
            var textOgs = AnnotationColorHelper.BuildGranularOverride(
                doc, textColor: textColor, boxColor: boxColor,
                boxTransparency: boxTransparency);
            var leaderOgs = AnnotationColorHelper.BuildGranularOverride(
                doc, textColor: leaderColor, boxColor: boxColor,
                boxTransparency: boxTransparency);

            int textColored = 0, leaderColored = 0;
            using (Transaction tx = new Transaction(doc, "STING Tag Appearance"))
            {
                tx.Start();
                foreach (var tag in withoutLeaders)
                {
                    try { view.SetElementOverrides(tag.Id, textOgs); textColored++; }
                    catch { }
                }
                foreach (var tag in withLeaders)
                {
                    try { view.SetElementOverrides(tag.Id, leaderOgs); leaderColored++; }
                    catch { }
                }
                tx.Commit();
            }

            var report = new StringBuilder();
            report.AppendLine($"Tag Appearance Applied — {allTags.Count} tags");
            report.AppendLine(new string('═', 45));
            report.AppendLine($"  Text color:    RGB({textColor.Red},{textColor.Green},{textColor.Blue}) → {textColored} tags");
            report.AppendLine($"  Leader color:  RGB({leaderColor.Red},{leaderColor.Green},{leaderColor.Blue}) → {leaderColored} tags");
            report.AppendLine($"  Box color:     RGB({boxColor.Red},{boxColor.Green},{boxColor.Blue})");
            report.AppendLine($"  Box visibility: {(boxTransparency == 0 ? "Opaque" : boxTransparency == 100 ? "Hidden" : $"{100 - boxTransparency}%")}");

            TaskDialog.Show("Tag Appearance", report.ToString());
            StingLog.Info($"TagAppearance: text=RGB({textColor.Red},{textColor.Green},{textColor.Blue}), " +
                $"leader=RGB({leaderColor.Red},{leaderColor.Green},{leaderColor.Blue}), " +
                $"box=RGB({boxColor.Red},{boxColor.Green},{boxColor.Blue}), " +
                $"transparency={boxTransparency}%, applied={textColored + leaderColored}");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Control tag bounding box appearance independently: set fill colour, visibility
    /// (transparency), and outline weight. This targets the surface graphic overrides
    /// which control the tag's bounding box/background fill separately from text color.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetTagBoxAppearanceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            var (tags, fromSel) = AnnotationColorHelper.GetTargetTags(uidoc);
            if (tags.Count == 0)
            {
                TaskDialog.Show("Tag Box", "No annotation tags found.");
                return Result.Succeeded;
            }

            // Choose box mode
            TaskDialog modeDlg = new TaskDialog("Tag Bounding Box");
            modeDlg.MainInstruction = $"Bounding box control for {tags.Count} tags";
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Set Box Color + Visibility",
                "Choose fill color and transparency for tag background");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Hide Box (Transparent)",
                "Make bounding box fully transparent (text and leaders remain visible)");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Show Box (Opaque)",
                "Make bounding box fully opaque with white fill");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Reset Box (Clear Overrides)",
                "Remove all surface overrides, restoring default box appearance");
            modeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var result = modeDlg.Show();
            if (result == TaskDialogResult.Cancel) return Result.Cancelled;

            OverrideGraphicSettings ogs;

            switch (result)
            {
                case TaskDialogResult.CommandLink1:
                {
                    // Pick box color
                    Color boxColor = AnnotationColorHelper.PickColorExtended(
                        "Box Color", "Choose bounding box fill color:");
                    if (boxColor == null) return Result.Cancelled;

                    int transparency = AnnotationColorHelper.GetBoxTransparencyChoice("Box Visibility");
                    if (transparency < 0) return Result.Cancelled;

                    ogs = AnnotationColorHelper.BuildGranularOverride(
                        doc, boxColor: boxColor, boxTransparency: transparency);
                    break;
                }
                case TaskDialogResult.CommandLink2:
                    // Hide box — full transparency
                    ogs = new OverrideGraphicSettings();
                    ogs.SetSurfaceTransparency(100);
                    break;

                case TaskDialogResult.CommandLink3:
                {
                    // Show box — opaque white
                    var solidFill = AnnotationColorHelper.FindSolidFill(doc);
                    ogs = new OverrideGraphicSettings();
                    if (solidFill != null)
                    {
                        ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                        ogs.SetSurfaceForegroundPatternColor(new Color(255, 255, 255));
                    }
                    ogs.SetSurfaceTransparency(0);
                    break;
                }
                case TaskDialogResult.CommandLink4:
                default:
                    // Reset — blank override (clears surface-only, preserving line color)
                    // We need to read existing line color and reapply it
                    ogs = new OverrideGraphicSettings();
                    break;
            }

            int modified = 0;
            using (Transaction tx = new Transaction(doc, "STING Set Tag Box"))
            {
                tx.Start();
                foreach (var tag in tags)
                {
                    try
                    {
                        if (result == TaskDialogResult.CommandLink4)
                        {
                            // Reset: preserve existing projection line color but clear surface
                            var existing = view.GetElementOverrides(tag.Id);
                            var reset = new OverrideGraphicSettings();
                            // Preserve text/line color if set
                            var existingColor = existing.ProjectionLineColor;
                            if (existingColor.IsValid)
                                reset.SetProjectionLineColor(existingColor);
                            var existingWeight = existing.ProjectionLineWeight;
                            if (existingWeight > 0)
                                reset.SetProjectionLineWeight(existingWeight);
                            view.SetElementOverrides(tag.Id, reset);
                        }
                        else
                        {
                            // Merge: apply surface overrides while preserving existing line color
                            var existing = view.GetElementOverrides(tag.Id);
                            var merged = new OverrideGraphicSettings();

                            // Copy existing line overrides
                            var existingColor = existing.ProjectionLineColor;
                            if (existingColor.IsValid)
                                merged.SetProjectionLineColor(existingColor);
                            var existingWeight = existing.ProjectionLineWeight;
                            if (existingWeight > 0)
                                merged.SetProjectionLineWeight(existingWeight);

                            // Apply new surface overrides from ogs
                            var surfId = ogs.SurfaceForegroundPatternId;
                            if (surfId != null && surfId != ElementId.InvalidElementId)
                            {
                                merged.SetSurfaceForegroundPatternId(surfId);
                                merged.SetSurfaceForegroundPatternColor(
                                    ogs.SurfaceForegroundPatternColor);
                            }
                            merged.SetSurfaceTransparency(ogs.SurfaceTransparency);

                            view.SetElementOverrides(tag.Id, merged);
                        }
                        modified++;
                    }
                    catch { }
                }
                tx.Commit();
            }

            string modeLabel = result == TaskDialogResult.CommandLink1 ? "Color + Visibility" :
                               result == TaskDialogResult.CommandLink2 ? "Hidden (Transparent)" :
                               result == TaskDialogResult.CommandLink3 ? "Visible (Opaque White)" :
                               "Reset to Default";
            TaskDialog.Show("Tag Box", $"Box mode '{modeLabel}' applied to {modified} tags.");
            StingLog.Info($"SetTagBox: mode={modeLabel}, modified={modified}");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Quick discipline-aware tag appearance: applies discipline colors to text,
    /// grey to leaders, and transparent box in one click. Combines the most common
    /// workflow into a single command.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class QuickTagStyleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            var (allTags, _) = AnnotationColorHelper.GetTargetTags(uidoc);
            if (allTags.Count == 0)
            {
                TaskDialog.Show("Quick Style", "No annotation tags found.");
                return Result.Succeeded;
            }

            // Choose style preset
            TaskDialog dlg = new TaskDialog("Quick Tag Style");
            dlg.MainInstruction = $"Apply preset style to {allTags.Count} tags";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Discipline Colors + Grey Leaders",
                "Text = discipline color (M=Blue, E=Gold...), Leaders = grey, Box = transparent");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Black Text + Grey Leaders",
                "Text = black, Leaders = grey, Box = transparent — print-ready");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "QA Mode (Red Text, No Box)",
                "Text + leaders = red, Box = hidden — checking/markup mode");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Presentation (Blue Text, White Box)",
                "Text = blue, Leaders = blue, Box = white opaque — clean presentation");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var mode = dlg.Show();
            if (mode == TaskDialogResult.Cancel) return Result.Cancelled;

            int styled = 0;
            var discCounts = new Dictionary<string, int>();

            using (Transaction tx = new Transaction(doc, "STING Quick Tag Style"))
            {
                tx.Start();
                foreach (var tag in allTags)
                {
                    try
                    {
                        bool hasLeader = false;
                        try { hasLeader = tag.HasLeader; } catch { }

                        OverrideGraphicSettings ogs;

                        switch (mode)
                        {
                            case TaskDialogResult.CommandLink1:
                            {
                                // Discipline colors + grey leaders
                                string disc = AnnotationColorHelper.GetTagDiscipline(tag, doc);
                                Color textCol = new Color(0, 0, 0); // default black
                                if (!string.IsNullOrEmpty(disc) &&
                                    AnnotationColorHelper.DisciplineColors.TryGetValue(disc, out Color dc))
                                {
                                    textCol = dc;
                                    if (!discCounts.ContainsKey(disc)) discCounts[disc] = 0;
                                    discCounts[disc]++;
                                }

                                Color lineCol = hasLeader
                                    ? new Color(128, 128, 128) // grey leaders
                                    : textCol;

                                ogs = AnnotationColorHelper.BuildGranularOverride(
                                    doc, textColor: lineCol, boxTransparency: 100);
                                break;
                            }
                            case TaskDialogResult.CommandLink2:
                            {
                                // Black text + grey leaders
                                Color lineCol = hasLeader
                                    ? new Color(128, 128, 128)
                                    : new Color(0, 0, 0);
                                ogs = AnnotationColorHelper.BuildGranularOverride(
                                    doc, textColor: lineCol, boxTransparency: 100);
                                break;
                            }
                            case TaskDialogResult.CommandLink3:
                            {
                                // QA red, no box
                                ogs = AnnotationColorHelper.BuildGranularOverride(
                                    doc, textColor: new Color(220, 20, 20),
                                    lineWeight: 2, boxTransparency: 100);
                                break;
                            }
                            case TaskDialogResult.CommandLink4:
                            default:
                            {
                                // Presentation blue + white box
                                ogs = AnnotationColorHelper.BuildGranularOverride(
                                    doc, textColor: new Color(0, 100, 220),
                                    boxColor: new Color(255, 255, 255),
                                    boxTransparency: 0);
                                break;
                            }
                        }

                        view.SetElementOverrides(tag.Id, ogs);
                        styled++;
                    }
                    catch { }
                }
                tx.Commit();
            }

            var report = new StringBuilder();
            report.AppendLine($"Quick Style applied to {styled} tags.");
            if (discCounts.Count > 0)
            {
                report.AppendLine("Disciplines:");
                foreach (var kvp in discCounts.OrderByDescending(x => x.Value))
                    report.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }
            TaskDialog.Show("Quick Tag Style", report.ToString());
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Set bounding box line weight (thickness) on annotation tags.
    /// Controls the outline weight of the tag border independently from text color.
    /// Uses SetProjectionLineWeight for the outline.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetTagLineWeightCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            var (tags, _) = AnnotationColorHelper.GetTargetTags(uidoc);
            if (tags.Count == 0)
            {
                TaskDialog.Show("Tag Line Weight", "No annotation tags found.");
                return Result.Succeeded;
            }

            TaskDialog dlg = new TaskDialog("Tag Bounding Box Line Weight");
            dlg.MainInstruction = $"Set border line weight for {tags.Count} tags";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Thin (1)", "Hairline border — minimal visual weight");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Standard (3)", "Normal border weight — default appearance");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Bold (6)", "Thick border — strong emphasis / QA checking");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Extra Bold (10)", "Maximum emphasis — very heavy border for callouts");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            int weight;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: weight = 1; break;
                case TaskDialogResult.CommandLink2: weight = 3; break;
                case TaskDialogResult.CommandLink3: weight = 6; break;
                case TaskDialogResult.CommandLink4: weight = 10; break;
                default: return Result.Cancelled;
            }

            int modified = 0;
            using (Transaction tx = new Transaction(doc, "STING Tag Line Weight"))
            {
                tx.Start();
                foreach (var tag in tags)
                {
                    try
                    {
                        // Preserve existing color but change line weight
                        var existing = view.GetElementOverrides(tag.Id);
                        var ogs = new OverrideGraphicSettings();
                        var existingColor = existing.ProjectionLineColor;
                        if (existingColor.IsValid)
                            ogs.SetProjectionLineColor(existingColor);
                        ogs.SetProjectionLineWeight(weight);

                        // Preserve existing surface overrides
                        var surfId = existing.SurfaceForegroundPatternId;
                        if (surfId != null && surfId != ElementId.InvalidElementId)
                        {
                            ogs.SetSurfaceForegroundPatternId(surfId);
                            ogs.SetSurfaceForegroundPatternColor(existing.SurfaceForegroundPatternColor);
                        }
                        ogs.SetSurfaceTransparency(existing.SurfaceTransparency);

                        view.SetElementOverrides(tag.Id, ogs);
                        modified++;
                    }
                    catch { }
                }
                tx.Commit();
            }

            TaskDialog.Show("Tag Line Weight",
                $"Set border weight to {weight} on {modified} tags.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Color annotation tags by the value of any parameter on the host element.
    /// User picks from available parameters and palettes. Uses the same palette
    /// system as ColorByParameterCommand but targets annotation tags specifically.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ColorTagsByParameterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            var (allTags, _) = AnnotationColorHelper.GetTargetTags(uidoc);
            if (allTags.Count == 0)
            {
                TaskDialog.Show("Color Tags By Parameter", "No annotation tags found.");
                return Result.Succeeded;
            }

            // Collect host elements to discover parameters
            var hostElements = new List<Element>();
            foreach (var tag in allTags)
            {
                try
                {
                    var hostIds = tag.GetTaggedLocalElementIds();
                    foreach (var hid in hostIds)
                    {
                        Element host = doc.GetElement(hid);
                        if (host != null) { hostElements.Add(host); break; }
                    }
                }
                catch { }
            }

            if (hostElements.Count == 0)
            {
                TaskDialog.Show("Color Tags By Parameter", "No valid host elements found.");
                return Result.Succeeded;
            }

            // Get available parameters from host elements
            var paramNames = Select.ColorHelper.GetAvailableParameters(doc, hostElements);

            // Step 1: Pick parameter (paged — show most useful first)
            var priority = new[]
            {
                ParamRegistry.DISC, ParamRegistry.SYS, ParamRegistry.LOC,
                ParamRegistry.ZONE, ParamRegistry.LVL, ParamRegistry.FUNC,
                ParamRegistry.PROD, ParamRegistry.TAG1, ParamRegistry.STATUS,
                "Mark", "Comments", "Type Name", "Family"
            };
            var top = priority.Where(p => paramNames.Contains(p)).Take(4).ToList();
            if (top.Count == 0) top = paramNames.Take(4).ToList();

            TaskDialog paramDlg = new TaskDialog("Color Tags — Pick Parameter");
            paramDlg.MainInstruction = $"Color {allTags.Count} tags by which parameter?";
            paramDlg.FooterText = $"{paramNames.Count} parameters available on host elements.";
            for (int i = 0; i < top.Count; i++)
                paramDlg.AddCommandLink((TaskDialogCommandLinkId)(i + 1001), top[i]);
            paramDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            int paramIdx = -1;
            switch (paramDlg.Show())
            {
                case TaskDialogResult.CommandLink1: paramIdx = 0; break;
                case TaskDialogResult.CommandLink2: paramIdx = 1; break;
                case TaskDialogResult.CommandLink3: paramIdx = 2; break;
                case TaskDialogResult.CommandLink4: paramIdx = 3; break;
                default: return Result.Cancelled;
            }
            string selectedParam = top[paramIdx];

            // Step 2: Pick palette
            TaskDialog palDlg = new TaskDialog("Color Tags — Pick Palette");
            palDlg.MainInstruction = $"Select palette for '{selectedParam}'";
            palDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "STING Discipline", "M=Blue, E=Gold, P=Green (8 colors)");
            palDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "High Contrast", "Saturated primaries (8 colors)");
            palDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Accessible", "Colorblind-safe viridis (10 colors)");
            palDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Pastel", "Soft muted tones (8 colors)");
            palDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            string paletteName;
            Color[] palette;
            switch (palDlg.Show())
            {
                case TaskDialogResult.CommandLink1: paletteName = "STING Discipline"; break;
                case TaskDialogResult.CommandLink2: paletteName = "High Contrast"; break;
                case TaskDialogResult.CommandLink3: paletteName = "Accessible"; break;
                case TaskDialogResult.CommandLink4: paletteName = "Pastel"; break;
                default: return Result.Cancelled;
            }
            palette = Select.ColorHelper.Palettes[paletteName];

            // Group tags by host element's parameter value
            var groups = new Dictionary<string, List<IndependentTag>>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in allTags)
            {
                try
                {
                    var hostIds = tag.GetTaggedLocalElementIds();
                    Element host = hostIds.Count > 0 ? doc.GetElement(hostIds.First()) : null;
                    string val = host != null
                        ? Select.ColorHelper.GetParameterValue(host, selectedParam) ?? "<No Value>"
                        : "<No Value>";

                    if (!groups.ContainsKey(val)) groups[val] = new List<IndependentTag>();
                    groups[val].Add(tag);
                }
                catch { }
            }

            // Assign colors
            var sortedValues = groups.Keys.OrderBy(v => v).ToList();
            var colorMap = Select.ColorHelper.AssignColors(sortedValues, palette);

            int colored = 0;
            using (Transaction tx = new Transaction(doc, $"STING Color Tags By {selectedParam}"))
            {
                tx.Start();
                foreach (var kvp in groups)
                {
                    Color c = colorMap[kvp.Key];
                    var ogs = AnnotationColorHelper.BuildAnnotationOverride(c);
                    foreach (var tag in kvp.Value)
                    {
                        try { view.SetElementOverrides(tag.Id, ogs); colored++; }
                        catch { }
                    }
                }
                tx.Commit();
            }

            var report = new StringBuilder();
            report.AppendLine($"Colored {colored} tags by '{selectedParam}'");
            report.AppendLine($"Palette: {paletteName} | Values: {groups.Count}");
            report.AppendLine();
            foreach (string val in sortedValues.Take(15))
            {
                Color c = colorMap[val];
                report.AppendLine($"  [{c.Red:D3},{c.Green:D3},{c.Blue:D3}]  {val} ({groups[val].Count})");
            }
            if (sortedValues.Count > 15)
                report.AppendLine($"  ... and {sortedValues.Count - 15} more");

            TaskDialog.Show("Color Tags By Parameter", report.ToString());
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Batch swap tag types (families) in the active view. Allows changing from one
    /// tag family to another (e.g., from default tags to STING tags with different
    /// text styles/sizes). Uses ChangeTypeId on IndependentTag.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SwapTagTypeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            var tags = LeaderHelper.GetTargetTags(uidoc);
            if (tags.Count == 0)
            {
                TaskDialog.Show("Swap Tag Type", "No annotation tags found.");
                return Result.Succeeded;
            }

            // Find all loaded tag families (FamilySymbol for annotation tags)
            var tagTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs =>
                {
                    try { return fs.Family.FamilyCategory?.CategoryType == CategoryType.Annotation; }
                    catch { return false; }
                })
                .OrderBy(fs => fs.Family.Name)
                .ThenBy(fs => fs.Name)
                .ToList();

            if (tagTypes.Count < 2)
            {
                TaskDialog.Show("Swap Tag Type",
                    "Need at least 2 annotation tag families loaded.\n" +
                    "Load additional tag families first.");
                return Result.Succeeded;
            }

            // Show current tag type distribution
            var currentTypes = new Dictionary<string, int>();
            foreach (var tag in tags)
            {
                try
                {
                    ElementId typeId = tag.GetTypeId();
                    Element type = doc.GetElement(typeId);
                    string typeName = type?.Name ?? "Unknown";
                    if (!currentTypes.ContainsKey(typeName)) currentTypes[typeName] = 0;
                    currentTypes[typeName]++;
                }
                catch { }
            }

            // Pick target tag type (top 4 by prevalence)
            var topTypes = tagTypes.Take(4).ToList();
            TaskDialog dlg = new TaskDialog("Swap Tag Type");
            dlg.MainInstruction = $"Change tag type for {tags.Count} tags";
            dlg.MainContent = "Current types:\n" +
                string.Join("\n", currentTypes.Select(kvp => $"  {kvp.Key}: {kvp.Value}"));
            for (int i = 0; i < topTypes.Count; i++)
            {
                dlg.AddCommandLink((TaskDialogCommandLinkId)(i + 1001),
                    $"{topTypes[i].Family.Name}: {topTypes[i].Name}");
            }
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            int picked = -1;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: picked = 0; break;
                case TaskDialogResult.CommandLink2: picked = 1; break;
                case TaskDialogResult.CommandLink3: picked = 2; break;
                case TaskDialogResult.CommandLink4: picked = 3; break;
                default: return Result.Cancelled;
            }

            ElementId newTypeId = topTypes[picked].Id;
            int swapped = 0;
            int failed = 0;

            using (Transaction tx = new Transaction(doc, "STING Swap Tag Type"))
            {
                tx.Start();
                foreach (var tag in tags)
                {
                    try
                    {
                        if (tag.GetTypeId() != newTypeId && tag.IsValidType(newTypeId))
                        {
                            tag.ChangeTypeId(newTypeId);
                            swapped++;
                        }
                    }
                    catch
                    {
                        failed++;
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Swap Tag Type",
                $"Swapped {swapped} tags to '{topTypes[picked].Name}'.\n" +
                (failed > 0 ? $"Failed: {failed} (incompatible category)." : ""));
            return Result.Succeeded;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // Statistics / Analysis
    // ══════════════════════════════════════════════════════════════════

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
                string tag = ParameterHelpers.GetString(elem, ParamRegistry.TAG1);
                if (TagConfig.TagIsComplete(tag))
                    tagged++;
                else
                    untagged++;

                string disc = ParameterHelpers.GetString(elem, ParamRegistry.DISC);
                if (!string.IsNullOrEmpty(disc))
                {
                    if (!byDisc.ContainsKey(disc)) byDisc[disc] = 0;
                    byDisc[disc]++;
                }
                string sys = ParameterHelpers.GetString(elem, ParamRegistry.SYS);
                if (!string.IsNullOrEmpty(sys))
                {
                    if (!bySys.ContainsKey(sys)) bySys[sys] = 0;
                    bySys[sys]++;
                }
                string lvl = ParameterHelpers.GetString(elem, ParamRegistry.LVL);
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

    /// <summary>
    /// Comprehensive Tag Register Export — exports a detailed asset register
    /// to CSV (Excel-compatible) with ALL tag tokens, identity fields, spatial data,
    /// dimensional properties, MEP parameters, and validation status.
    /// Designed as a full BIM asset register / tag register for handover.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagRegisterExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            // Define ALL columns for the register
            string[] columns = new[]
            {
                // Identity
                "ElementId", "Category", "FamilyName", "TypeName",
                // Tag Tokens (ISO 19650)
                "DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ",
                // Assembled Tags
                "ASS_TAG_1 (Full)", "ASS_TAG_2 (Short)", "ASS_TAG_3 (Location)", "ASS_TAG_4 (System)",
                // Validation
                "TagValid", "TagResolved", "TagComplete", "ValidationIssues",
                // Status & Classification
                "STATUS", "REV", "Mark", "Description", "Manufacturer", "Model",
                // Spatial
                "Level", "RoomName", "RoomNumber", "Department", "GridRef",
                // Dimensional
                "Width_mm", "Height_mm", "Length_mm", "Area_m2", "Thickness_mm",
                // MEP Parameters
                "SystemType", "Size", "Flow", "Voltage", "Power_kW",
                // Cost & FM
                "UnitPrice_UGX", "TypeMark", "Keynote", "Uniformat", "OmniClass",
            };

            var sb = new StringBuilder();
            // BOM for Excel UTF-8 detection
            sb.Append('\uFEFF');
            sb.AppendLine(string.Join(",", columns));

            int total = 0;
            int valid = 0;
            int incomplete = 0;
            var discCounts = new Dictionary<string, int>();

            foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                if (!known.Contains(cat)) continue;

                total++;

                // Tag tokens
                string disc = Gs(el, ParamRegistry.DISC);
                string loc = Gs(el, ParamRegistry.LOC);
                string zone = Gs(el, ParamRegistry.ZONE);
                string lvl = Gs(el, ParamRegistry.LVL);
                string sys = Gs(el, ParamRegistry.SYS);
                string func = Gs(el, ParamRegistry.FUNC);
                string prod = Gs(el, ParamRegistry.PROD);
                string seq = Gs(el, ParamRegistry.SEQ);

                // Assembled tags
                string tag1 = Gs(el, ParamRegistry.TAG1);
                string tag2 = Gs(el, ParamRegistry.TAG2);
                string tag3 = Gs(el, ParamRegistry.TAG3);
                string tag4 = Gs(el, ParamRegistry.TAG4);

                // Validation
                bool isValid = TagConfig.TagIsComplete(tag1);
                bool isResolved = TagConfig.TagIsFullyResolved(tag1);
                if (isValid) valid++;

                // Check for empty tokens
                var issues = new List<string>();
                if (string.IsNullOrEmpty(disc)) issues.Add("DISC");
                if (string.IsNullOrEmpty(loc)) issues.Add("LOC");
                if (string.IsNullOrEmpty(zone)) issues.Add("ZONE");
                if (string.IsNullOrEmpty(lvl)) issues.Add("LVL");
                if (string.IsNullOrEmpty(sys)) issues.Add("SYS");
                if (string.IsNullOrEmpty(func)) issues.Add("FUNC");
                if (string.IsNullOrEmpty(prod)) issues.Add("PROD");
                if (string.IsNullOrEmpty(seq)) issues.Add("SEQ");

                // Cross-validation (ISO 19650)
                var isoErrors = ISO19650Validator.ValidateElement(el);
                if (isoErrors.Count > 0)
                    issues.AddRange(isoErrors);

                bool isComplete = issues.Count == 0;
                if (!isComplete) incomplete++;
                string issueStr = issues.Count > 0 ? string.Join("; ", issues) : "";

                // Identity
                string familyName = ParameterHelpers.GetFamilyName(el);
                string typeName = ParameterHelpers.GetFamilySymbolName(el);
                string status = Gs(el, ParamRegistry.STATUS);
                string rev = Gs(el, ParamRegistry.REV);
                string mark = Gp(el, BuiltInParameter.ALL_MODEL_MARK);
                string desc = Gs(el, ParamRegistry.DESC);
                if (string.IsNullOrEmpty(desc)) desc = Gp(el, BuiltInParameter.ALL_MODEL_DESCRIPTION);
                string mfr = Gs(el, ParamRegistry.MFR);
                if (string.IsNullOrEmpty(mfr)) mfr = Gp(el, BuiltInParameter.ALL_MODEL_MANUFACTURER);
                string model = Gs(el, ParamRegistry.MODEL);
                if (string.IsNullOrEmpty(model)) model = Gp(el, BuiltInParameter.ALL_MODEL_MODEL);

                // Spatial
                string level = ParameterHelpers.GetLevelCode(doc, el);
                string roomName = Gs(el, ParamRegistry.ROOM_NAME);
                if (string.IsNullOrEmpty(roomName)) roomName = Gs(el, ParamRegistry.BLE_ROOM_NAME);
                string roomNum = Gs(el, ParamRegistry.ROOM_NUM);
                if (string.IsNullOrEmpty(roomNum)) roomNum = Gs(el, ParamRegistry.BLE_ROOM_NUM);
                string dept = Gs(el, ParamRegistry.DEPT);
                string gridRef = Gs(el, ParamRegistry.GRID_REF);

                // Dimensional — try STING params, fallback to built-in
                string width = GetDim(el, ParamRegistry.DOOR_WIDTH, ParamRegistry.WINDOW_WIDTH,
                    ParamRegistry.WALL_THICKNESS, ParamRegistry.RAMP_WIDTH, ParamRegistry.STAIR_WIDTH);
                string height = GetDim(el, ParamRegistry.DOOR_HEIGHT, ParamRegistry.WINDOW_HEIGHT,
                    ParamRegistry.WALL_HEIGHT, ParamRegistry.CEILING_HEIGHT);
                string length = GetDim(el, ParamRegistry.WALL_LENGTH, ParamRegistry.PLM_PIPE_LENGTH);
                string area = Gs(el, ParamRegistry.ELE_AREA);
                if (string.IsNullOrEmpty(area)) area = Gs(el, ParamRegistry.ROOM_AREA);
                string thickness = GetDim(el, ParamRegistry.FLR_THICKNESS, ParamRegistry.WALL_THICKNESS);

                // MEP
                string sysType = Gp(el, BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
                if (string.IsNullOrEmpty(sysType)) sysType = sys;
                string size = Gs(el, ParamRegistry.SIZE);
                string flow = Gs(el, ParamRegistry.HVC_DUCT_FLOW);
                if (string.IsNullOrEmpty(flow)) flow = Gs(el, ParamRegistry.PLM_PIPE_FLOW);
                if (string.IsNullOrEmpty(flow)) flow = Gs(el, ParamRegistry.HVC_AIRFLOW);
                string voltage = Gs(el, ParamRegistry.ELC_VOLTAGE);
                if (string.IsNullOrEmpty(voltage)) voltage = Gs(el, ParamRegistry.ELC_PNL_VOLTAGE);
                string power = Gs(el, ParamRegistry.ELC_POWER);

                // Cost & FM
                string unitPrice = Gs(el, ParamRegistry.COST);
                string typeMark = Gs(el, ParamRegistry.TYPE_MARK);
                string keynote = Gs(el, ParamRegistry.KEYNOTE);
                string uniformat = Gs(el, ParamRegistry.UNIFORMAT);
                string omniclass = Gs(el, ParamRegistry.OMNICLASS);

                // Track discipline counts
                if (!string.IsNullOrEmpty(disc))
                {
                    if (!discCounts.ContainsKey(disc)) discCounts[disc] = 0;
                    discCounts[disc]++;
                }

                // Write CSV row
                sb.Append(el.Id).Append(',');
                sb.Append(Esc(cat)).Append(',');
                sb.Append(Esc(familyName)).Append(',');
                sb.Append(Esc(typeName)).Append(',');
                sb.Append(disc).Append(',');
                sb.Append(loc).Append(',');
                sb.Append(zone).Append(',');
                sb.Append(lvl).Append(',');
                sb.Append(sys).Append(',');
                sb.Append(func).Append(',');
                sb.Append(prod).Append(',');
                sb.Append(seq).Append(',');
                sb.Append(Esc(tag1)).Append(',');
                sb.Append(Esc(tag2)).Append(',');
                sb.Append(Esc(tag3)).Append(',');
                sb.Append(Esc(tag4)).Append(',');
                sb.Append(isValid).Append(',');
                sb.Append(isResolved).Append(',');
                sb.Append(isComplete).Append(',');
                sb.Append(Esc(issueStr)).Append(',');
                sb.Append(status).Append(',');
                sb.Append(rev).Append(',');
                sb.Append(Esc(mark)).Append(',');
                sb.Append(Esc(desc)).Append(',');
                sb.Append(Esc(mfr)).Append(',');
                sb.Append(Esc(model)).Append(',');
                sb.Append(level).Append(',');
                sb.Append(Esc(roomName)).Append(',');
                sb.Append(Esc(roomNum)).Append(',');
                sb.Append(Esc(dept)).Append(',');
                sb.Append(gridRef).Append(',');
                sb.Append(width).Append(',');
                sb.Append(height).Append(',');
                sb.Append(length).Append(',');
                sb.Append(area).Append(',');
                sb.Append(thickness).Append(',');
                sb.Append(Esc(sysType)).Append(',');
                sb.Append(size).Append(',');
                sb.Append(flow).Append(',');
                sb.Append(voltage).Append(',');
                sb.Append(power).Append(',');
                sb.Append(unitPrice).Append(',');
                sb.Append(Esc(typeMark)).Append(',');
                sb.Append(Esc(keynote)).Append(',');
                sb.Append(Esc(uniformat)).Append(',');
                sb.AppendLine(Esc(omniclass));
            }

            // Write to file
            string dir = System.IO.Path.GetDirectoryName(doc.PathName);
            if (string.IsNullOrEmpty(dir)) dir = System.IO.Path.GetTempPath();
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = System.IO.Path.Combine(dir,
                $"STING_Tag_Register_{timestamp}.csv");

            try
            {
                System.IO.File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);

                double pct = total > 0 ? (valid * 100.0 / total) : 0;
                var report = new StringBuilder();
                report.AppendLine($"Tag Register Export Complete");
                report.AppendLine(new string('═', 50));
                report.AppendLine($"  Total elements:    {total}");
                report.AppendLine($"  Valid tags:        {valid} ({pct:F1}%)");
                report.AppendLine($"  Incomplete:        {incomplete}");
                report.AppendLine($"  Columns:           {columns.Length}");
                report.AppendLine();
                if (discCounts.Count > 0)
                {
                    report.AppendLine("  By discipline:");
                    foreach (var kvp in discCounts.OrderBy(x => x.Key))
                        report.AppendLine($"    {kvp.Key,-4} {kvp.Value,6}");
                }
                report.AppendLine();
                report.AppendLine($"  File: {path}");
                report.AppendLine();
                report.AppendLine("Opens directly in Excel with full formatting support.");

                TaskDialog.Show("Tag Register Export", report.ToString());
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Tag Register Export", $"Export failed: {ex.Message}");
                StingLog.Error("Tag register export failed", ex);
            }

            return Result.Succeeded;
        }

        /// <summary>Get string parameter value.</summary>
        private static string Gs(Element el, string paramName)
        {
            return ParameterHelpers.GetString(el, paramName);
        }

        /// <summary>Get built-in parameter as string.</summary>
        private static string Gp(Element el, BuiltInParameter bip)
        {
            try
            {
                Parameter p = el.get_Parameter(bip);
                if (p == null || !p.HasValue) return "";
                return p.StorageType == StorageType.String
                    ? (p.AsString() ?? "")
                    : (p.AsValueString() ?? "");
            }
            catch { return ""; }
        }

        /// <summary>Get first non-empty dimensional value from parameter list.</summary>
        private static string GetDim(Element el, params string[] paramNames)
        {
            foreach (string name in paramNames)
            {
                string val = ParameterHelpers.GetString(el, name);
                if (!string.IsNullOrEmpty(val) && val != "0") return val;
            }
            return "";
        }

        /// <summary>CSV-escape a value (quote if contains comma, quote, or newline).</summary>
        private static string Esc(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  LEADER MANAGEMENT COMMANDS
    //  Annotation tag leader control — toggle, add, remove, align, reset
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Toggle leaders ON for all selected annotation tags (IndependentTag).
    /// If no elements selected, operates on all tags in the active view.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AddLeadersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var tags = LeaderHelper.GetTargetTags(uidoc);
            if (tags.Count == 0)
            {
                TaskDialog.Show("Add Leaders", "No annotation tags found in selection or view.");
                return Result.Succeeded;
            }

            int modified = 0;
            using (Transaction tx = new Transaction(doc, "STING Add Leaders"))
            {
                tx.Start();
                foreach (IndependentTag tag in tags)
                {
                    try
                    {
                        if (!tag.HasLeader)
                        {
                            tag.HasLeader = true;
                            modified++;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Add leader to tag {tag.Id}: {ex.Message}");
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Add Leaders",
                $"Leaders added to {modified} of {tags.Count} tags.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Remove leaders from all selected annotation tags.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RemoveLeadersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var tags = LeaderHelper.GetTargetTags(uidoc);
            if (tags.Count == 0)
            {
                TaskDialog.Show("Remove Leaders", "No annotation tags found in selection or view.");
                return Result.Succeeded;
            }

            int modified = 0;
            using (Transaction tx = new Transaction(doc, "STING Remove Leaders"))
            {
                tx.Start();
                foreach (IndependentTag tag in tags)
                {
                    try
                    {
                        if (tag.HasLeader)
                        {
                            tag.HasLeader = false;
                            modified++;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Remove leader from tag {tag.Id}: {ex.Message}");
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Remove Leaders",
                $"Leaders removed from {modified} of {tags.Count} tags.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Toggle leaders on selected tags: if any have leaders, remove all;
    /// if none have leaders, add to all.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ToggleLeadersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var tags = LeaderHelper.GetTargetTags(uidoc);
            if (tags.Count == 0)
            {
                TaskDialog.Show("Toggle Leaders", "No annotation tags found in selection or view.");
                return Result.Succeeded;
            }

            // If majority have leaders, remove all; otherwise add all
            int withLeaders = tags.Count(t => t.HasLeader);
            bool addLeaders = withLeaders <= tags.Count / 2;

            int modified = 0;
            using (Transaction tx = new Transaction(doc, "STING Toggle Leaders"))
            {
                tx.Start();
                foreach (IndependentTag tag in tags)
                {
                    try
                    {
                        if (addLeaders && !tag.HasLeader)
                        {
                            tag.HasLeader = true;
                            modified++;
                        }
                        else if (!addLeaders && tag.HasLeader)
                        {
                            tag.HasLeader = false;
                            modified++;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Toggle leader on tag {tag.Id}: {ex.Message}");
                    }
                }
                tx.Commit();
            }

            string action = addLeaders ? "added" : "removed";
            TaskDialog.Show("Toggle Leaders",
                $"Leaders {action} on {modified} of {tags.Count} tags.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Align tag heads horizontally or vertically for selected tags.
    /// Aligns to the first selected tag's position.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignTagsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var tags = LeaderHelper.GetSelectedTags(uidoc);
            if (tags.Count < 2)
            {
                TaskDialog.Show("Align Tags",
                    "Select at least 2 annotation tags to align.");
                return Result.Succeeded;
            }

            View view = doc.ActiveView;

            // Calculate actual tag widths for smart spacing
            double maxTagWidth = 0;
            foreach (var tag in tags)
            {
                try
                {
                    BoundingBoxXYZ bb = tag.get_BoundingBox(view);
                    if (bb != null)
                    {
                        double w = bb.Max.X - bb.Min.X;
                        if (w > maxTagWidth) maxTagWidth = w;
                    }
                }
                catch { }
            }
            // Fallback: use view scale-based estimate
            if (maxTagWidth < 0.001)
                maxTagWidth = view.Scale * 0.01;
            double autoSpacing = maxTagWidth * 1.2; // 20% gap between tags

            // Ask alignment direction
            TaskDialog dlg = new TaskDialog("Align Tags");
            dlg.MainInstruction = $"Align {tags.Count} tags";
            dlg.MainContent = "Choose alignment direction:";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Align Horizontally", "Align all tag heads to same Y as first selected tag");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Align Vertically", "Align all tag heads to same X as first selected tag");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Row (auto-spaced)", $"Distribute in row with auto-calculated spacing ({autoSpacing * 304.8:F0}mm)");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Column (auto-spaced)", "Distribute in vertical column with auto-calculated spacing");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var result = dlg.Show();
            if (result == TaskDialogResult.Cancel) return Result.Cancelled;

            XYZ refPoint = tags[0].TagHeadPosition;
            int aligned = 0;

            // For vertical column, calculate max tag height
            double maxTagHeight = 0;
            if (result == TaskDialogResult.CommandLink4)
            {
                foreach (var tag in tags)
                {
                    try
                    {
                        BoundingBoxXYZ bb = tag.get_BoundingBox(view);
                        if (bb != null)
                        {
                            double h = bb.Max.Y - bb.Min.Y;
                            if (h > maxTagHeight) maxTagHeight = h;
                        }
                    }
                    catch { }
                }
                if (maxTagHeight < 0.001)
                    maxTagHeight = view.Scale * 0.003;
            }
            double vSpacing = maxTagHeight * 1.3; // 30% gap for readability

            using (Transaction tx = new Transaction(doc, "STING Align Tags"))
            {
                tx.Start();
                for (int i = 1; i < tags.Count; i++)
                {
                    try
                    {
                        XYZ current = tags[i].TagHeadPosition;
                        XYZ newPos;

                        switch (result)
                        {
                            case TaskDialogResult.CommandLink1:
                                // Horizontal: same Y, keep X
                                newPos = new XYZ(current.X, refPoint.Y, current.Z);
                                break;
                            case TaskDialogResult.CommandLink2:
                                // Vertical: same X, keep Y
                                newPos = new XYZ(refPoint.X, current.Y, current.Z);
                                break;
                            case TaskDialogResult.CommandLink3:
                                // Row: same Y, auto-spaced X
                                newPos = new XYZ(refPoint.X + (i * autoSpacing),
                                    refPoint.Y, current.Z);
                                break;
                            case TaskDialogResult.CommandLink4:
                                // Column: same X, auto-spaced Y (descending)
                                newPos = new XYZ(refPoint.X,
                                    refPoint.Y - (i * vSpacing), current.Z);
                                break;
                            default:
                                continue;
                        }

                        tags[i].TagHeadPosition = newPos;
                        aligned++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Align tag {tags[i].Id}: {ex.Message}");
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Align Tags", $"Aligned {aligned} tags.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Reset tag positions — move tag heads back to their host element center.
    /// Useful for cleaning up manually-dragged tags.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ResetTagPositionsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var tags = LeaderHelper.GetTargetTags(uidoc);
            if (tags.Count == 0)
            {
                TaskDialog.Show("Reset Tags", "No annotation tags found in selection or view.");
                return Result.Succeeded;
            }

            int reset = 0;
            using (Transaction tx = new Transaction(doc, "STING Reset Tag Positions"))
            {
                tx.Start();
                foreach (IndependentTag tag in tags)
                {
                    try
                    {
                        // Get the tagged element's location
                        var _hostIds = tag.GetTaggedLocalElementIds();
                        Element host = _hostIds.Count > 0 ? doc.GetElement(_hostIds.First()) : null;
                        if (host == null) continue;

                        XYZ center = LeaderHelper.GetElementCenter(host);
                        if (center == null) continue;

                        // Move tag head to element center with small offset
                        View view = doc.GetElement(tag.OwnerViewId) as View;
                        double offset = view != null ? view.Scale * 0.005 : 0.5;
                        XYZ tagPos = center + new XYZ(offset, offset, 0);

                        tag.TagHeadPosition = tagPos;
                        if (tag.HasLeader)
                            tag.HasLeader = false;

                        reset++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Reset tag {tag.Id}: {ex.Message}");
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Reset Tags",
                $"Reset {reset} of {tags.Count} tag positions to element centers.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Change tag orientation — toggle between horizontal and vertical
    /// for all selected annotation tags.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ToggleTagOrientationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var tags = LeaderHelper.GetTargetTags(uidoc);
            if (tags.Count == 0)
            {
                TaskDialog.Show("Toggle Orientation",
                    "No annotation tags found in selection or view.");
                return Result.Succeeded;
            }

            int toggled = 0;
            using (Transaction tx = new Transaction(doc, "STING Toggle Tag Orientation"))
            {
                tx.Start();
                foreach (IndependentTag tag in tags)
                {
                    try
                    {
                        tag.TagOrientation = tag.TagOrientation == TagOrientation.Horizontal
                            ? TagOrientation.Vertical
                            : TagOrientation.Horizontal;
                        toggled++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Toggle orientation on tag {tag.Id}: {ex.Message}");
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Toggle Orientation",
                $"Toggled orientation on {toggled} of {tags.Count} tags.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Select all annotation tags in the active view that have leaders.
    /// Useful for batch-operating on leader tags.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SelectTagsWithLeadersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            var allTags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            var withLeaders = allTags.Where(t => t.HasLeader)
                .Select(t => t.Id).ToList();
            var withoutLeaders = allTags.Where(t => !t.HasLeader)
                .Select(t => t.Id).ToList();

            // Ask which to select
            TaskDialog dlg = new TaskDialog("Select Tags");
            dlg.MainInstruction = $"View has {allTags.Count} tags " +
                $"({withLeaders.Count} with leaders, {withoutLeaders.Count} without)";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"Tags WITH Leaders ({withLeaders.Count})",
                "Select all tags that currently have leader lines");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                $"Tags WITHOUT Leaders ({withoutLeaders.Count})",
                "Select all tags without leader lines");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                $"ALL Tags ({allTags.Count})",
                "Select all annotation tags in view");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var result = dlg.Show();

            ICollection<ElementId> selectIds;
            switch (result)
            {
                case TaskDialogResult.CommandLink1:
                    selectIds = withLeaders;
                    break;
                case TaskDialogResult.CommandLink2:
                    selectIds = withoutLeaders;
                    break;
                case TaskDialogResult.CommandLink3:
                    selectIds = allTags.Select(t => t.Id).ToList();
                    break;
                default:
                    return Result.Cancelled;
            }

            if (selectIds.Count > 0)
                uidoc.Selection.SetElementIds(selectIds);

            TaskDialog.Show("Select Tags", $"Selected {selectIds.Count} tags.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Shared helper for leader management commands.
    /// Gets annotation tags from selection or active view.
    /// </summary>
    internal static class LeaderHelper
    {
        /// <summary>Get tags from selection; if none selected, get all tags in active view.</summary>
        public static List<IndependentTag> GetTargetTags(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            var selIds = uidoc.Selection.GetElementIds();

            if (selIds.Count > 0)
            {
                return selIds
                    .Select(id => doc.GetElement(id))
                    .OfType<IndependentTag>()
                    .ToList();
            }

            // Fall back to all tags in active view
            return new FilteredElementCollector(doc, doc.ActiveView.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();
        }

        /// <summary>Get only selected tags (not from view).</summary>
        public static List<IndependentTag> GetSelectedTags(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            return uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<IndependentTag>()
                .ToList();
        }

        /// <summary>Get center point of an element for tag placement.</summary>
        public static XYZ GetElementCenter(Element el)
        {
            if (el.Location is LocationPoint lp)
                return lp.Point;
            if (el.Location is LocationCurve lc)
            {
                Curve c = lc.Curve;
                return (c.GetEndPoint(0) + c.GetEndPoint(1)) / 2.0;
            }

            // Fallback: bounding box center
            BoundingBoxXYZ bb = el.get_BoundingBox(null);
            if (bb != null)
                return (bb.Min + bb.Max) / 2.0;

            return null;
        }

        /// <summary>
        /// Determine which side of the host element the tag head is on.
        /// Returns: "right", "left", "above", or "below".
        /// </summary>
        public static string GetLeaderSide(Document doc, IndependentTag tag)
        {
            try
            {
                var hostIds = tag.GetTaggedLocalElementIds();
                if (hostIds.Count == 0) return "right";
                Element host = doc.GetElement(hostIds.First());
                if (host == null) return "right";

                XYZ hostCenter = GetElementCenter(host);
                if (hostCenter == null) return "right";

                XYZ tagHead = tag.TagHeadPosition;
                double dx = tagHead.X - hostCenter.X;
                double dy = tagHead.Y - hostCenter.Y;

                if (Math.Abs(dx) > Math.Abs(dy))
                    return dx > 0 ? "right" : "left";
                else
                    return dy > 0 ? "above" : "below";
            }
            catch { return "right"; }
        }

        /// <summary>
        /// Auto-align tag head position opposite to leader direction.
        /// When leader comes from the right, shift tag text left (and vice versa).
        /// When leader comes from below, shift tag text up (and vice versa).
        /// This creates cleaner annotation where the tag text reads away from the leader.
        /// </summary>
        public static int AutoAlignTagsToLeaders(Document doc, List<IndependentTag> tags, View view)
        {
            int aligned = 0;
            foreach (IndependentTag tag in tags)
            {
                try
                {
                    if (!tag.HasLeader) continue;

                    var hostIds = tag.GetTaggedLocalElementIds();
                    if (hostIds.Count == 0) continue;
                    Element host = doc.GetElement(hostIds.First());
                    if (host == null) continue;

                    XYZ hostCenter = GetElementCenter(host);
                    if (hostCenter == null) continue;

                    XYZ tagHead = tag.TagHeadPosition;
                    double dx = tagHead.X - hostCenter.X;
                    double dy = tagHead.Y - hostCenter.Y;

                    // Get tag bounding box for width/height estimate
                    BoundingBoxXYZ bb = tag.get_BoundingBox(view);
                    double tagW = bb != null ? (bb.Max.X - bb.Min.X) : view.Scale * 0.008;
                    double tagH = bb != null ? (bb.Max.Y - bb.Min.Y) : view.Scale * 0.003;
                    double halfW = tagW * 0.5;
                    double halfH = tagH * 0.5;

                    // Calculate the offset to shift tag text opposite to leader
                    // If leader comes from right (element is right of tag), shift tag left
                    XYZ newPos = tagHead;
                    if (Math.Abs(dx) > Math.Abs(dy))
                    {
                        // Primarily horizontal leader
                        if (dx > 0)
                        {
                            // Tag is to the right of element → keep tag right, but ensure
                            // tag head is offset rightward so text reads away from leader
                            newPos = new XYZ(hostCenter.X + Math.Abs(dx) + halfW * 0.1, tagHead.Y, tagHead.Z);
                        }
                        else
                        {
                            // Tag is to the left → offset leftward
                            newPos = new XYZ(hostCenter.X - Math.Abs(dx) - halfW * 0.1, tagHead.Y, tagHead.Z);
                        }
                    }
                    else
                    {
                        // Primarily vertical leader
                        if (dy > 0)
                        {
                            // Tag above → nudge up slightly
                            newPos = new XYZ(tagHead.X, hostCenter.Y + Math.Abs(dy) + halfH * 0.1, tagHead.Z);
                        }
                        else
                        {
                            // Tag below → nudge down slightly
                            newPos = new XYZ(tagHead.X, hostCenter.Y - Math.Abs(dy) - halfH * 0.1, tagHead.Z);
                        }
                    }

                    if (newPos.DistanceTo(tagHead) > 0.001)
                    {
                        tag.TagHeadPosition = newPos;
                        aligned++;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"AutoAlignTag {tag.Id}: {ex.Message}");
                }
            }
            return aligned;
        }
    }

    /// <summary>
    /// Snap leader elbows to 45°, 90°, or straight angles for clean annotation layout.
    /// Works on selected tags or all tags in view.
    /// Supports cycling: each invocation detects the current angle and rotates to the next
    /// in the sequence 90° → 45° → Straight → 90°.
    /// Also supports direct angle setting via the static SnapToAngle method.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SnapLeaderElbowCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var tags = LeaderHelper.GetTargetTags(uidoc)
                .Where(t => t.HasLeader).ToList();

            if (tags.Count == 0)
            {
                TaskDialog.Show("Snap Elbows", "No tags with leaders found.");
                return Result.Succeeded;
            }

            // Auto-cycle: detect current angle and rotate to next
            // Cycle order: 90° → 45° → Straight (0°) → 90°
            string targetAngle = DetectCurrentAngleAndCycle(doc, tags);

            int snapped = SnapToAngle(doc, tags, targetAngle);

            string angleLabel = targetAngle == "0" ? "Straight" : $"{targetAngle}°";
            TaskDialog.Show("Snap Elbows",
                $"Snapped {snapped} of {tags.Count} leader elbows to {angleLabel}.\n" +
                "(Click again to cycle to next angle)");
            return Result.Succeeded;
        }

        /// <summary>
        /// Detect the predominant current elbow angle and return the next in cycle.
        /// Cycle: 90° → 45° → Straight (0°) → 90°
        /// </summary>
        private static string DetectCurrentAngleAndCycle(Document doc, List<IndependentTag> tags)
        {
            int count90 = 0, count45 = 0, count0 = 0;

            foreach (IndependentTag tag in tags)
            {
                try
                {
                    var hostIds = tag.GetTaggedLocalElementIds();
                    Element host = hostIds.Count > 0 ? doc.GetElement(hostIds.First()) : null;
                    if (host == null) continue;

                    XYZ hostCenter = LeaderHelper.GetElementCenter(host);
                    if (hostCenter == null) continue;

                    XYZ tagHead = tag.TagHeadPosition;
                    var refs = tag.GetTaggedReferences();
                    if (refs == null || refs.Count == 0) continue;

                    XYZ elbow = tag.GetLeaderElbow(refs.First());
                    if (elbow == null) { count90++; continue; }

                    // Classify the elbow position
                    XYZ mid = (hostCenter + tagHead) / 2.0;
                    XYZ ortho90 = new XYZ(tagHead.X, hostCenter.Y, hostCenter.Z);

                    if (elbow.DistanceTo(mid) < 0.2)
                        count0++;       // Straight
                    else if (elbow.DistanceTo(ortho90) < 0.2)
                        count90++;      // 90°
                    else
                        count45++;      // 45° or other
                }
                catch { count90++; }
            }

            // Determine dominant angle and cycle to next
            if (count90 >= count45 && count90 >= count0)
                return "45";    // Currently 90° → cycle to 45°
            if (count45 >= count90 && count45 >= count0)
                return "0";     // Currently 45° → cycle to Straight
            return "90";        // Currently Straight → cycle to 90°
        }

        /// <summary>
        /// Snap leader elbows to a specific angle. Called by the handler for direct snapping.
        /// angleMode: "90", "45", or "0" (straight).
        /// </summary>
        public static int SnapToAngle(Document doc, List<IndependentTag> tags, string angleMode)
        {
            int snapped = 0;
            using (Transaction tx = new Transaction(doc, "STING Snap Leader Elbows"))
            {
                tx.Start();
                foreach (IndependentTag tag in tags)
                {
                    try
                    {
                        var hostIds = tag.GetTaggedLocalElementIds();
                        Element host = hostIds.Count > 0 ? doc.GetElement(hostIds.First()) : null;
                        if (host == null) continue;

                        XYZ hostCenter = LeaderHelper.GetElementCenter(host);
                        if (hostCenter == null) continue;

                        XYZ tagHead = tag.TagHeadPosition;
                        XYZ delta = tagHead - hostCenter;
                        if (delta.GetLength() < 0.01) continue;

                        XYZ elbowPos = CalculateElbowPosition(hostCenter, tagHead, delta, angleMode);

                        var refs = tag.GetTaggedReferences();
                        if (refs != null && refs.Count > 0)
                        {
                            tag.LeaderEndCondition = LeaderEndCondition.Free;
                            tag.SetLeaderElbow(refs.First(), elbowPos);
                            snapped++;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Snap elbow on tag {tag.Id}: {ex.Message}");
                    }
                }
                tx.Commit();
            }
            return snapped;
        }

        /// <summary>
        /// Calculate the elbow position for a given angle mode.
        /// </summary>
        private static XYZ CalculateElbowPosition(XYZ hostCenter, XYZ tagHead, XYZ delta, string angleMode)
        {
            if (angleMode == "0")
            {
                // Straight: elbow at midpoint — creates straight leader
                return (hostCenter + tagHead) / 2.0;
            }
            else if (angleMode == "45")
            {
                // 45° elbow: diagonal then horizontal/vertical
                double absDx = Math.Abs(delta.X);
                double absDy = Math.Abs(delta.Y);
                double diag = Math.Min(absDx, absDy);
                double signX = delta.X >= 0 ? 1 : -1;
                double signY = delta.Y >= 0 ? 1 : -1;

                if (absDx > absDy)
                    return new XYZ(hostCenter.X + diag * signX, tagHead.Y, hostCenter.Z);
                else
                    return new XYZ(tagHead.X, hostCenter.Y + diag * signY, hostCenter.Z);
            }
            else // "90"
            {
                // 90° elbow: horizontal from host, then vertical to tag head
                return new XYZ(tagHead.X, hostCenter.Y, hostCenter.Z);
            }
        }
    }

    /// <summary>
    /// Auto-align tag text opposite to leader direction.
    /// When leader points right → tag text offsets left. When leader points left → tag offsets right.
    /// Automatically repositions all tags with leaders so text reads away from the leader line.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoAlignLeaderTextCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            var tags = LeaderHelper.GetTargetTags(uidoc)
                .Where(t => t.HasLeader).ToList();

            if (tags.Count == 0)
            {
                TaskDialog.Show("Auto-Align Leader Text",
                    "No tags with leaders found.\nSelect tags or run on all tags in view.");
                return Result.Succeeded;
            }

            int aligned = 0;
            using (Transaction tx = new Transaction(doc, "STING Auto-Align Leader Text"))
            {
                tx.Start();
                aligned = LeaderHelper.AutoAlignTagsToLeaders(doc, tags, view);
                tx.Commit();
            }

            TaskDialog.Show("Auto-Align Leader Text",
                $"Auto-aligned {aligned} of {tags.Count} tagged leaders.\n" +
                "Tags repositioned so text reads away from leader direction.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Flip tag position relative to its host element — mirror across
    /// the element center point. Useful for moving tags from one side
    /// of a pipe/duct to the other.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FlipTagsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var tags = LeaderHelper.GetTargetTags(uidoc);
            if (tags.Count == 0)
            {
                TaskDialog.Show("Flip Tags", "No annotation tags found in selection or view.");
                return Result.Succeeded;
            }

            // Choose flip direction
            TaskDialog dlg = new TaskDialog("Flip Tags");
            dlg.MainInstruction = $"Flip {tags.Count} tags";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Flip Horizontal",
                "Mirror tag position left ↔ right across element center");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Flip Vertical",
                "Mirror tag position up ↔ down across element center");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Flip Both",
                "Mirror tag diagonally (180° rotation around element)");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var result = dlg.Show();
            if (result == TaskDialogResult.Cancel) return Result.Cancelled;

            int flipped = 0;
            using (Transaction tx = new Transaction(doc, "STING Flip Tags"))
            {
                tx.Start();
                foreach (IndependentTag tag in tags)
                {
                    try
                    {
                        var _hostIds = tag.GetTaggedLocalElementIds();
                        Element host = _hostIds.Count > 0 ? doc.GetElement(_hostIds.First()) : null;
                        if (host == null) continue;

                        XYZ center = LeaderHelper.GetElementCenter(host);
                        if (center == null) continue;

                        XYZ head = tag.TagHeadPosition;
                        XYZ delta = head - center;
                        XYZ newHead;

                        switch (result)
                        {
                            case TaskDialogResult.CommandLink1:
                                // Flip X (horizontal)
                                newHead = new XYZ(center.X - delta.X, head.Y, head.Z);
                                break;
                            case TaskDialogResult.CommandLink2:
                                // Flip Y (vertical)
                                newHead = new XYZ(head.X, center.Y - delta.Y, head.Z);
                                break;
                            case TaskDialogResult.CommandLink3:
                                // Flip both (180° around center)
                                newHead = new XYZ(center.X - delta.X,
                                    center.Y - delta.Y, head.Z);
                                break;
                            default:
                                continue;
                        }

                        tag.TagHeadPosition = newHead;
                        flipped++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Flip tag {tag.Id}: {ex.Message}");
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Flip Tags", $"Flipped {flipped} of {tags.Count} tags.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Align tag text (annotation text notes) — set justification and
    /// alignment for selected text notes in the view.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignTagTextCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Get selected text notes and tags
            var selIds = uidoc.Selection.GetElementIds();
            var textNotes = selIds
                .Select(id => doc.GetElement(id))
                .OfType<TextNote>()
                .ToList();
            var tags = selIds
                .Select(id => doc.GetElement(id))
                .OfType<IndependentTag>()
                .ToList();

            int total = textNotes.Count + tags.Count;
            if (total == 0)
            {
                TaskDialog.Show("Align Tag Text",
                    "Select annotation tags or text notes to align.");
                return Result.Succeeded;
            }

            // Choose alignment
            TaskDialog dlg = new TaskDialog("Align Tag Text");
            dlg.MainInstruction = $"Align text for {total} annotations";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Left Align", "Align all text to the left edge");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Center Align", "Center-align all text");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Right Align", "Align all text to the right edge");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var result = dlg.Show();
            if (result == TaskDialogResult.Cancel) return Result.Cancelled;

            int aligned = 0;
            using (Transaction tx = new Transaction(doc, "STING Align Tag Text"))
            {
                tx.Start();

                // Align text notes
                foreach (TextNote tn in textNotes)
                {
                    try
                    {
                        switch (result)
                        {
                            case TaskDialogResult.CommandLink1:
                                tn.HorizontalAlignment = HorizontalTextAlignment.Left;
                                break;
                            case TaskDialogResult.CommandLink2:
                                tn.HorizontalAlignment = HorizontalTextAlignment.Center;
                                break;
                            case TaskDialogResult.CommandLink3:
                                tn.HorizontalAlignment = HorizontalTextAlignment.Right;
                                break;
                        }
                        aligned++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Align text {tn.Id}: {ex.Message}");
                    }
                }

                // For tags, we can align the tag head positions
                // (tag text alignment is controlled by the tag family, not the instance)
                if (tags.Count > 1)
                {
                    // Sort tags by X position
                    var sorted = tags.OrderBy(t => t.TagHeadPosition.X).ToList();
                    double refX;

                    switch (result)
                    {
                        case TaskDialogResult.CommandLink1:
                            refX = sorted.First().TagHeadPosition.X;
                            break;
                        case TaskDialogResult.CommandLink2:
                            refX = sorted.Average(t => t.TagHeadPosition.X);
                            break;
                        case TaskDialogResult.CommandLink3:
                            refX = sorted.Last().TagHeadPosition.X;
                            break;
                        default:
                            refX = 0;
                            break;
                    }

                    foreach (IndependentTag tag in tags)
                    {
                        try
                        {
                            XYZ pos = tag.TagHeadPosition;
                            tag.TagHeadPosition = new XYZ(refX, pos.Y, pos.Z);
                            aligned++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Align tag text {tag.Id}: {ex.Message}");
                        }
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Align Tag Text",
                $"Aligned {aligned} annotations ({textNotes.Count} text notes, {tags.Count} tags).");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Pin (lock) tags to their host elements — prevents accidental movement.
    /// Also can unpin tags to allow repositioning.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PinTagsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var tags = LeaderHelper.GetTargetTags(uidoc);
            if (tags.Count == 0)
            {
                TaskDialog.Show("Pin Tags", "No annotation tags found.");
                return Result.Succeeded;
            }

            int pinned = tags.Count(t => t.Pinned);
            int unpinned = tags.Count - pinned;

            TaskDialog dlg = new TaskDialog("Pin/Unpin Tags");
            dlg.MainInstruction = $"{tags.Count} tags ({pinned} pinned, {unpinned} unpinned)";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"Pin All ({unpinned} to pin)",
                "Lock all tags to prevent accidental movement");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                $"Unpin All ({pinned} to unpin)",
                "Unlock all tags to allow repositioning");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var result = dlg.Show();
            if (result == TaskDialogResult.Cancel) return Result.Cancelled;

            bool pin = result == TaskDialogResult.CommandLink1;
            int modified = 0;

            using (Transaction tx = new Transaction(doc,
                pin ? "STING Pin Tags" : "STING Unpin Tags"))
            {
                tx.Start();
                foreach (IndependentTag tag in tags)
                {
                    try
                    {
                        if (pin && !tag.Pinned)
                        {
                            tag.Pinned = true;
                            modified++;
                        }
                        else if (!pin && tag.Pinned)
                        {
                            tag.Pinned = false;
                            modified++;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Pin/unpin tag {tag.Id}: {ex.Message}");
                    }
                }
                tx.Commit();
            }

            string action = pin ? "Pinned" : "Unpinned";
            TaskDialog.Show("Pin Tags", $"{action} {modified} of {tags.Count} tags.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Nudge annotation tags in a specific direction by a small offset.
    /// Adjusts TagHeadPosition by configurable increments.
    /// Direction is determined from the command tag dispatched via StingCommandHandler.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class NudgeTagsCommand : IExternalCommand
    {
        /// <summary>Nudge amount in feet (1/4 inch = 0.0208 ft, approx 6.35mm).</summary>
        private const double SmallNudge = 0.0208;
        /// <summary>Medium nudge (1 inch = 0.0833 ft, approx 25mm).</summary>
        private const double MediumNudge = 0.0833;
        /// <summary>Large nudge (3 inches = 0.25 ft, approx 76mm).</summary>
        private const double LargeNudge = 0.25;

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var tags = LeaderHelper.GetSelectedTags(uidoc);
            if (tags.Count == 0)
            {
                // Fall back to all tags in view
                tags = LeaderHelper.GetTargetTags(uidoc);
            }
            if (tags.Count == 0)
            {
                TaskDialog.Show("Nudge Tags", "No annotation tags found. Select tags first.");
                return Result.Succeeded;
            }

            // Ask direction if not evident from dispatch
            TaskDialog dlg = new TaskDialog("Nudge Tags");
            dlg.MainInstruction = $"Nudge {tags.Count} tags";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "↑ Up", "Move tags upward in the view");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "↓ Down", "Move tags downward in the view");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "← Left", "Move tags to the left");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "→ Right", "Move tags to the right");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            XYZ direction;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: direction = XYZ.BasisY; break;  // Up
                case TaskDialogResult.CommandLink2: direction = -XYZ.BasisY; break; // Down
                case TaskDialogResult.CommandLink3: direction = -XYZ.BasisX; break; // Left
                case TaskDialogResult.CommandLink4: direction = XYZ.BasisX; break;  // Right
                default: return Result.Cancelled;
            }

            int nudged = 0;
            XYZ offset = direction * MediumNudge;

            using (Transaction tx = new Transaction(doc, "STING Nudge Tags"))
            {
                tx.Start();
                foreach (IndependentTag tag in tags)
                {
                    try
                    {
                        XYZ current = tag.TagHeadPosition;
                        tag.TagHeadPosition = current + offset;
                        nudged++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Nudge tag {tag.Id}: {ex.Message}");
                    }
                }
                tx.Commit();
            }

            StingLog.Info($"NudgeTags: direction={direction}, nudged={nudged}");
            return Result.Succeeded;
        }

        /// <summary>
        /// Static nudge by direction string — called from StingCommandHandler inline.
        /// </summary>
        public static int NudgeInDirection(Document doc, View view, List<IndependentTag> tags, string direction)
        {
            XYZ offset;
            switch (direction?.ToUpperInvariant())
            {
                case "UP": offset = XYZ.BasisY * MediumNudge; break;
                case "DOWN": offset = -XYZ.BasisY * MediumNudge; break;
                case "LEFT": offset = -XYZ.BasisX * MediumNudge; break;
                case "RIGHT": offset = XYZ.BasisX * MediumNudge; break;
                case "NEAR": offset = XYZ.BasisY * SmallNudge; break;
                case "FAR": offset = -XYZ.BasisY * SmallNudge; break;
                default: return 0;
            }

            int nudged = 0;
            foreach (IndependentTag tag in tags)
            {
                try
                {
                    tag.TagHeadPosition = tag.TagHeadPosition + offset;
                    nudged++;
                }
                catch { }
            }
            return nudged;
        }
    }

    /// <summary>
    /// Attach leader end to a specific element — reattaches a free-end leader
    /// to the nearest element, or allows picking a target element.
    /// Sets LeaderEndCondition to Attached for robust element tracking.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AttachLeaderCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var tags = LeaderHelper.GetTargetTags(uidoc)
                .Where(t => t.HasLeader).ToList();

            if (tags.Count == 0)
            {
                TaskDialog.Show("Attach Leaders",
                    "No tags with leaders found. Add leaders first, then attach.");
                return Result.Succeeded;
            }

            TaskDialog dlg = new TaskDialog("Attach Leaders");
            dlg.MainInstruction = $"Attach {tags.Count} leaders to elements";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Attach to Host (Fixed)",
                "Set leader end to 'Attached' — follows element if moved");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Set Free End",
                "Set leader end to 'Free' — stays at fixed point in view");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var result = dlg.Show();
            if (result == TaskDialogResult.Cancel) return Result.Cancelled;

            bool attach = result == TaskDialogResult.CommandLink1;
            int modified = 0;

            using (Transaction tx = new Transaction(doc, "STING Attach Leaders"))
            {
                tx.Start();
                foreach (IndependentTag tag in tags)
                {
                    try
                    {
                        if (attach)
                        {
                            tag.LeaderEndCondition = LeaderEndCondition.Attached;
                        }
                        else
                        {
                            tag.LeaderEndCondition = LeaderEndCondition.Free;
                        }
                        modified++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Attach leader on tag {tag.Id}: {ex.Message}");
                    }
                }
                tx.Commit();
            }

            string action = attach ? "Attached (locked)" : "Set to Free";
            TaskDialog.Show("Attach Leaders",
                $"{action} {modified} of {tags.Count} leader endpoints.");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  ANOMALY AUTO-FIX
    //  Extends the detect-only HighlightInvalidCommand to automatically fix
    //  common parameter anomalies: empty DISC, wrong LOC format, missing ZONE,
    //  placeholder SEQ values, mismatched DISC/category.
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Auto-fix common parameter anomalies rather than just detecting them.
    /// Scans all taggable elements, identifies fixable issues, and corrects them.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AnomalyAutoFixCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc.Document;

            // Scan all taggable elements
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);
            var allElements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => known.Contains(ParameterHelpers.GetCategoryName(e)))
                .ToList();

            if (allElements.Count == 0)
            {
                TaskDialog.Show("Anomaly Auto-Fix", "No taggable elements found.");
                return Result.Succeeded;
            }

            // Build spatial context for LOC/ZONE auto-detection
            var ctx = TokenAutoPopulator.PopulationContext.Build(doc);

            // Identify anomalies
            int emptyDisc = 0, wrongDisc = 0, emptyLoc = 0, emptyZone = 0;
            int placeholderSeq = 0, emptyLvl = 0, emptySys = 0;
            int totalAnomalies = 0;
            var fixable = new List<(Element el, string[] issues)>();

            foreach (var el in allElements)
            {
                string catName = ParameterHelpers.GetCategoryName(el);
                var issues = new List<string>();

                string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                string loc = ParameterHelpers.GetString(el, ParamRegistry.LOC);
                string zone = ParameterHelpers.GetString(el, ParamRegistry.ZONE);
                string lvl = ParameterHelpers.GetString(el, ParamRegistry.LVL);
                string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                string seq = ParameterHelpers.GetString(el, ParamRegistry.SEQ);

                // Empty DISC — derive from category
                if (string.IsNullOrEmpty(disc))
                {
                    issues.Add("DISC:empty");
                    emptyDisc++;
                }
                // Wrong DISC — category says M but param says E
                else if (TagConfig.DiscMap.TryGetValue(catName, out string expected) && disc != expected)
                {
                    issues.Add($"DISC:mismatch({disc}→{expected})");
                    wrongDisc++;
                }

                if (string.IsNullOrEmpty(loc)) { issues.Add("LOC:empty"); emptyLoc++; }
                if (string.IsNullOrEmpty(zone)) { issues.Add("ZONE:empty"); emptyZone++; }
                if (string.IsNullOrEmpty(lvl)) { issues.Add("LVL:empty"); emptyLvl++; }
                if (string.IsNullOrEmpty(sys)) { issues.Add("SYS:empty"); emptySys++; }
                if (seq == "0000" || seq == "XX") { issues.Add("SEQ:placeholder"); placeholderSeq++; }

                if (issues.Count > 0)
                {
                    fixable.Add((el, issues.ToArray()));
                    totalAnomalies += issues.Count;
                }
            }

            if (fixable.Count == 0)
            {
                TaskDialog.Show("Anomaly Auto-Fix",
                    $"Scanned {allElements.Count} elements.\nNo anomalies detected — all parameters are healthy.");
                return Result.Succeeded;
            }

            // Confirm before fixing
            var confirm = new TaskDialog("Anomaly Auto-Fix");
            confirm.MainInstruction = $"Found {totalAnomalies} anomalies on {fixable.Count} elements";
            confirm.MainContent =
                $"Scanned: {allElements.Count} taggable elements\n\n" +
                $"  Empty DISC:    {emptyDisc}\n" +
                $"  Wrong DISC:    {wrongDisc}\n" +
                $"  Empty LOC:     {emptyLoc}\n" +
                $"  Empty ZONE:    {emptyZone}\n" +
                $"  Empty LVL:     {emptyLvl}\n" +
                $"  Empty SYS:     {emptySys}\n" +
                $"  Placeholder SEQ: {placeholderSeq}\n\n" +
                "Fix all automatically?";
            confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Fix All", "Auto-fix all detected anomalies using spatial detection and category mapping");
            confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Cancel", "Review anomalies without fixing");

            if (confirm.Show() != TaskDialogResult.CommandLink1)
                return Result.Cancelled;

            // Apply fixes
            int fixed_disc = 0, fixed_loc = 0, fixed_zone = 0, fixed_lvl = 0, fixed_sys = 0;
            int fixed_seq = 0, fixed_wrongDisc = 0;
            var seqCounters = TagConfig.GetExistingSequenceCounters(doc);

            using (Transaction tx = new Transaction(doc, "STING Anomaly Auto-Fix"))
            {
                tx.Start();

                foreach (var (el, issues) in fixable)
                {
                    string catName = ParameterHelpers.GetCategoryName(el);

                    foreach (string issue in issues)
                    {
                        if (issue == "DISC:empty" && TagConfig.DiscMap.TryGetValue(catName, out string dCode))
                        {
                            ParameterHelpers.SetString(el, ParamRegistry.DISC, dCode, overwrite: true);
                            fixed_disc++;
                        }
                        else if (issue.StartsWith("DISC:mismatch") && TagConfig.DiscMap.TryGetValue(catName, out string correctDisc))
                        {
                            ParameterHelpers.SetString(el, ParamRegistry.DISC, correctDisc, overwrite: true);
                            fixed_wrongDisc++;
                        }
                        else if (issue == "LOC:empty")
                        {
                            string loc = SpatialAutoDetect.DetectLoc(doc, el, ctx.RoomIndex, ctx.ProjectLoc);
                            if (!string.IsNullOrEmpty(loc))
                            {
                                ParameterHelpers.SetString(el, ParamRegistry.LOC, loc, overwrite: true);
                                fixed_loc++;
                            }
                        }
                        else if (issue == "ZONE:empty")
                        {
                            string zone = SpatialAutoDetect.DetectZone(doc, el, ctx.RoomIndex);
                            if (!string.IsNullOrEmpty(zone))
                            {
                                ParameterHelpers.SetString(el, ParamRegistry.ZONE, zone, overwrite: true);
                                fixed_zone++;
                            }
                        }
                        else if (issue == "LVL:empty")
                        {
                            string lvl = ParameterHelpers.GetLevelCode(doc, el);
                            if (!string.IsNullOrEmpty(lvl) && lvl != "XX")
                            {
                                ParameterHelpers.SetString(el, ParamRegistry.LVL, lvl, overwrite: true);
                                fixed_lvl++;
                            }
                        }
                        else if (issue == "SYS:empty")
                        {
                            string sys = TagConfig.GetMepSystemAwareSysCode(el, catName);
                            if (!string.IsNullOrEmpty(sys))
                            {
                                ParameterHelpers.SetString(el, ParamRegistry.SYS, sys, overwrite: true);
                                fixed_sys++;
                            }
                        }
                        else if (issue == "SEQ:placeholder")
                        {
                            // Re-derive a unique SEQ from existing counters
                            string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                            string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                            string lvl = ParameterHelpers.GetString(el, ParamRegistry.LVL);
                            string key = $"{disc}-{sys}-{lvl}";
                            if (!seqCounters.ContainsKey(key)) seqCounters[key] = 0;
                            seqCounters[key]++;
                            string newSeq = seqCounters[key].ToString().PadLeft(ParamRegistry.NumPad, '0');
                            ParameterHelpers.SetString(el, ParamRegistry.SEQ, newSeq, overwrite: true);
                            fixed_seq++;
                        }
                    }
                }

                tx.Commit();
            }

            int totalFixed = fixed_disc + fixed_wrongDisc + fixed_loc + fixed_zone +
                             fixed_lvl + fixed_sys + fixed_seq;
            TaskDialog.Show("Anomaly Auto-Fix",
                $"Fixed {totalFixed} of {totalAnomalies} anomalies:\n\n" +
                $"  DISC (empty → derived):    {fixed_disc}\n" +
                $"  DISC (mismatch → correct): {fixed_wrongDisc}\n" +
                $"  LOC (spatial detect):      {fixed_loc}\n" +
                $"  ZONE (spatial detect):     {fixed_zone}\n" +
                $"  LVL (level derive):        {fixed_lvl}\n" +
                $"  SYS (MEP system derive):   {fixed_sys}\n" +
                $"  SEQ (unique reassign):     {fixed_seq}");

            StingLog.Info($"AnomalyAutoFix: fixed {totalFixed}/{totalAnomalies} on {fixable.Count} elements");
            return Result.Succeeded;
        }
    }
}
