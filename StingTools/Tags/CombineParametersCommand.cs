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
    /// Naviate-style "Combine Parameters" command with interactive selection.
    ///
    /// Presents a multi-step dialog where the user:
    ///   Step 1: Chooses a mode (All Containers, Universal Only, Discipline Only, Pick Containers)
    ///   Step 2: In "Pick" mode, selects which tag container groups to populate
    ///
    /// All container definitions loaded from ParamRegistry (PARAMETER_REGISTRY.json).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CombineParametersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            var allGroups = ParamRegistry.ContainerGroups;

            // Step 1: Mode selection
            int totalContainers = allGroups.Sum(g => g.Params.Length);
            TaskDialog modeDlg = new TaskDialog("Combine Parameters");
            modeDlg.MainInstruction = "Which tag containers to populate?";
            modeDlg.MainContent =
                "Reads token parameters (DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, SEQ) " +
                "and assembles them into tag container parameters.\n\n" +
                $"Available: {allGroups.Length} groups, {totalContainers} total containers";
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "All Containers",
                $"Populate all {allGroups.Length} groups ({totalContainers} parameters)");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Universal Only (ASS_TAG_1-6)",
                "6 universal containers applied to all tagged elements");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Discipline Only",
                "MEP + Comms discipline-specific containers (excludes Universal and Material)");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Pick Container Groups...",
                "Interactively choose which groups to populate");
            modeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var modeResult = modeDlg.Show();

            HashSet<string> selectedGroupCodes;
            switch (modeResult)
            {
                case TaskDialogResult.CommandLink1:
                    selectedGroupCodes = new HashSet<string>(allGroups.Select(g => g.GroupCode));
                    break;
                case TaskDialogResult.CommandLink2:
                    selectedGroupCodes = new HashSet<string> { "UNIVERSAL" };
                    break;
                case TaskDialogResult.CommandLink3:
                    selectedGroupCodes = new HashSet<string>(
                        allGroups.Where(g => g.GroupCode != "UNIVERSAL" && g.GroupCode != "MAT_TAG")
                                 .Select(g => g.GroupCode));
                    break;
                case TaskDialogResult.CommandLink4:
                    selectedGroupCodes = ShowGroupPicker(doc, allGroups);
                    if (selectedGroupCodes == null || selectedGroupCodes.Count == 0)
                        return Result.Cancelled;
                    break;
                default:
                    return Result.Cancelled;
            }

            var activeGroups = allGroups.Where(g => selectedGroupCodes.Contains(g.GroupCode)).ToArray();
            return ExecuteCombine(doc, activeGroups);
        }

        // ── Group picker: paged TaskDialog selection ─────────────────

        private HashSet<string> ShowGroupPicker(Document doc, ParamRegistry.ContainerGroupDef[] allGroups)
        {
            var knownCategories = new HashSet<string>(TagConfig.DiscMap.Keys);
            var catCounts = new Dictionary<string, int>();
            foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                if (!knownCategories.Contains(cat)) continue;
                if (catCounts.ContainsKey(cat)) catCounts[cat]++;
                else catCounts[cat] = 1;
            }

            var selected = new HashSet<string>();
            int page = 0;
            int pageSize = 4;

            while (true)
            {
                int start = page * pageSize;
                if (start >= allGroups.Length)
                {
                    if (selected.Count > 0) break;
                    page = 0;
                    start = 0;
                }

                int count = Math.Min(pageSize, allGroups.Length - start);
                var pageGroups = allGroups.Skip(start).Take(count).ToArray();
                int totalPages = (int)Math.Ceiling((double)allGroups.Length / pageSize);

                TaskDialog picker = new TaskDialog("Select Container Groups");
                picker.MainInstruction = $"Toggle groups (page {page + 1}/{totalPages})";
                picker.MainContent = selected.Count > 0
                    ? $"Selected: {string.Join(", ", allGroups.Where(g => selected.Contains(g.GroupCode)).Select(g => g.Group))}"
                    : "Click a group to select/deselect it. Cancel when done selecting.";

                for (int i = 0; i < pageGroups.Length; i++)
                {
                    var g = pageGroups[i];
                    int elemCount = g.Categories != null
                        ? g.Categories.Sum(c => catCounts.TryGetValue(c, out int n) ? n : 0)
                        : catCounts.Values.Sum();
                    string mark = selected.Contains(g.GroupCode) ? "[X] " : "[ ] ";
                    picker.AddCommandLink(
                        (TaskDialogCommandLinkId)(i + 201),
                        $"{mark}{g.Group}",
                        $"{g.Params.Length} containers | {elemCount} elements");
                }

                picker.CommonButtons = TaskDialogCommonButtons.Cancel;

                var pickResult = picker.Show();

                int linkIndex = -1;
                switch (pickResult)
                {
                    case TaskDialogResult.CommandLink1: linkIndex = 0; break;
                    case TaskDialogResult.CommandLink2: linkIndex = 1; break;
                    case TaskDialogResult.CommandLink3: linkIndex = 2; break;
                    case TaskDialogResult.CommandLink4: linkIndex = 3; break;
                    default:
                        page++;
                        if (page * pageSize >= allGroups.Length)
                            break;
                        continue;
                }

                if (linkIndex >= 0 && linkIndex < pageGroups.Length)
                {
                    string code = pageGroups[linkIndex].GroupCode;
                    if (selected.Contains(code))
                        selected.Remove(code);
                    else
                        selected.Add(code);
                    continue;
                }
            }

            return selected.Count > 0 ? selected : null;
        }

        // ── Core combine logic ───────────────────────────────────────

        private Result ExecuteCombine(Document doc, ParamRegistry.ContainerGroupDef[] activeGroups)
        {
            var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            var knownCategories = new HashSet<string>(TagConfig.DiscMap.Keys);

            int totalElements = 0;
            int totalWrites = 0;
            int skippedNoDisc = 0;
            var writesPerGroup = new Dictionary<string, int>();

            foreach (var g in activeGroups)
                writesPerGroup[g.GroupCode] = 0;

            using (Transaction tx = new Transaction(doc, "STING Combine Parameters"))
            {
                tx.Start();

                foreach (Element el in collector)
                {
                    string catName = ParameterHelpers.GetCategoryName(el);
                    if (string.IsNullOrEmpty(catName) || !knownCategories.Contains(catName))
                        continue;

                    string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                    if (string.IsNullOrEmpty(disc))
                    {
                        skippedNoDisc++;
                        continue;
                    }

                    totalElements++;

                    // Read all source tokens once
                    string[] tokenValues = ParamRegistry.ReadTokenValues(el);

                    foreach (var group in activeGroups)
                    {
                        if (group.Categories != null && !group.Categories.Contains(catName))
                            continue;

                        foreach (var container in group.Params)
                        {
                            string assembled = ParamRegistry.AssembleContainer(container, tokenValues);

                            if (!string.IsNullOrEmpty(assembled))
                            {
                                if (ParameterHelpers.SetString(el, container.ParamName,
                                    assembled, overwrite: true))
                                {
                                    totalWrites++;
                                    writesPerGroup[group.GroupCode]++;
                                }
                            }
                        }
                    }
                }

                tx.Commit();
            }

            // Build report
            var report = new StringBuilder();
            report.AppendLine("Combine Parameters Complete");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Elements processed:  {totalElements}");
            report.AppendLine($"  Parameters written:  {totalWrites}");
            if (skippedNoDisc > 0)
                report.AppendLine($"  Skipped (untagged):  {skippedNoDisc}");
            report.AppendLine();
            report.AppendLine("Container groups populated:");
            report.AppendLine($"  {"Group",-35} {"Writes",7}");
            report.AppendLine($"  {new string('─', 43)}");
            foreach (var group in activeGroups)
            {
                int w = writesPerGroup[group.GroupCode];
                report.AppendLine($"  {group.Group,-35} {w,7}");
                foreach (var c in group.Params)
                    report.AppendLine($"    -> {c.ParamName,-28} {c.Description}");
            }

            TaskDialog td = new TaskDialog("Combine Parameters");
            td.MainInstruction = $"Combined {totalWrites} parameters across {totalElements} elements";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"CombineParameters: {totalElements} elements, {totalWrites} writes, " +
                $"{activeGroups.Length} groups");

            return Result.Succeeded;
        }
    }
}
