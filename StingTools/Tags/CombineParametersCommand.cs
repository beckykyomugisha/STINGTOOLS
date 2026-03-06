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
                    break; // Always exit — caller checks selected.Count
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
                        (TaskDialogCommandLinkId)(i + 1001),
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
                            // Skip TAG7 in normal assembly — it uses the narrative builder
                            if (container.ParamName == ParamRegistry.TAG7)
                                continue;

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

                    // Write TAG7 + sub-sections (TAG7A-TAG7F) — rich descriptive narrative
                    int tag7Writes = TagConfig.WriteTag7All(doc, el, catName, tokenValues, overwrite: true);
                    totalWrites += tag7Writes;
                    if (tag7Writes > 0 && writesPerGroup.ContainsKey("UNIVERSAL"))
                        writesPerGroup["UNIVERSAL"] += tag7Writes;
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

    /// <summary>
    /// Combine Pre-Flight Check: audits token completeness BEFORE writing containers.
    /// Reports which tokens are missing, how many elements are ready vs incomplete,
    /// and which disciplines/systems have gaps. Non-destructive ReadOnly audit.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CombinePreFlightCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            var knownCategories = new HashSet<string>(TagConfig.DiscMap.Keys);

            int total = 0, fullyReady = 0, partial = 0, empty = 0;
            var missingByToken = new Dictionary<string, int>
            {
                { "DISC", 0 }, { "LOC", 0 }, { "ZONE", 0 }, { "LVL", 0 },
                { "SYS", 0 }, { "FUNC", 0 }, { "PROD", 0 }, { "SEQ", 0 },
                { "STATUS", 0 }, { "REV", 0 }
            };
            var readyByDisc = new Dictionary<string, int>();
            var incompleteByDisc = new Dictionary<string, int>();
            var placeholderCount = 0;
            var incompleteTagCount = 0;
            var existingTagCount = 0;

            string[] tokenNames = { "DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ", "STATUS", "REV" };
            string[] tokenParams = {
                ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
                ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC,
                ParamRegistry.PROD, ParamRegistry.SEQ, ParamRegistry.STATUS, ParamRegistry.REV
            };

            foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string catName = ParameterHelpers.GetCategoryName(el);
                if (!knownCategories.Contains(catName)) continue;

                total++;

                // Check existing tag
                string existingTag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                if (TagConfig.TagIsComplete(existingTag))
                    existingTagCount++;
                else if (!string.IsNullOrEmpty(existingTag))
                    incompleteTagCount++;

                // Check token completeness
                int filledCount = 0;
                bool hasPlaceholder = false;
                string disc = "";

                for (int i = 0; i < tokenParams.Length; i++)
                {
                    string val = ParameterHelpers.GetString(el, tokenParams[i]);
                    if (string.IsNullOrEmpty(val))
                    {
                        missingByToken[tokenNames[i]]++;
                    }
                    else
                    {
                        filledCount++;
                        if (val == "XX" || val == "ZZ" || val == "0000")
                            hasPlaceholder = true;
                    }
                    if (i == 0) disc = val;
                }

                if (hasPlaceholder) placeholderCount++;

                if (filledCount == tokenParams.Length)
                {
                    fullyReady++;
                    if (!string.IsNullOrEmpty(disc))
                    {
                        if (!readyByDisc.ContainsKey(disc)) readyByDisc[disc] = 0;
                        readyByDisc[disc]++;
                    }
                }
                else if (filledCount > 0)
                {
                    partial++;
                    if (!string.IsNullOrEmpty(disc))
                    {
                        if (!incompleteByDisc.ContainsKey(disc)) incompleteByDisc[disc] = 0;
                        incompleteByDisc[disc]++;
                    }
                }
                else
                {
                    empty++;
                }
            }

            // Build report
            var report = new StringBuilder();
            report.AppendLine("Combine Pre-Flight Check");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Taggable elements:     {total}");
            report.AppendLine($"  Fully ready ({tokenParams.Length}/{tokenParams.Length}):   {fullyReady}");
            report.AppendLine($"  Partial tokens:        {partial}");
            report.AppendLine($"  No tokens at all:      {empty}");
            report.AppendLine($"  With placeholders:     {placeholderCount}");
            report.AppendLine($"  Already have TAG1:     {existingTagCount}");
            if (incompleteTagCount > 0)
                report.AppendLine($"  Incomplete TAG1:       {incompleteTagCount}");

            double readyPct = total > 0 ? fullyReady * 100.0 / total : 0;
            report.AppendLine($"  Readiness:             {readyPct:F1}%");

            report.AppendLine();
            report.AppendLine("Missing Tokens:");
            report.AppendLine($"  {"Token",-8} {"Missing",8} {"Filled",8} {"%Ready",8}");
            report.AppendLine($"  {new string('─', 34)}");
            for (int i = 0; i < tokenNames.Length; i++)
            {
                int missing = missingByToken[tokenNames[i]];
                int filled = total - missing;
                double pct = total > 0 ? filled * 100.0 / total : 0;
                string bar = missing > 0 ? " !!!" : "";
                report.AppendLine($"  {tokenNames[i],-8} {missing,8} {filled,8} {pct,7:F0}%{bar}");
            }

            if (readyByDisc.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("Ready by Discipline:");
                foreach (var kvp in readyByDisc.OrderByDescending(x => x.Value))
                {
                    int inc = incompleteByDisc.TryGetValue(kvp.Key, out int n) ? n : 0;
                    report.AppendLine($"  {kvp.Key,-6} {kvp.Value,5} ready, {inc,5} incomplete");
                }
            }

            // Recommendation
            report.AppendLine();
            if (readyPct >= 95)
                report.AppendLine("RECOMMENDATION: Ready to combine! High token completeness.");
            else if (readyPct >= 70)
                report.AppendLine("RECOMMENDATION: Mostly ready. Run Family-Stage Populate to fill gaps.");
            else if (readyPct >= 30)
                report.AppendLine("RECOMMENDATION: Significant gaps. Run Auto Tag or Family-Stage Populate first.");
            else
                report.AppendLine("RECOMMENDATION: Too many gaps. Run the full tagging pipeline before combining.");

            TaskDialog td = new TaskDialog("Combine Pre-Flight");
            td.MainInstruction = $"Pre-Flight: {fullyReady}/{total} ready ({readyPct:F0}%)";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"CombinePreFlight: total={total}, ready={fullyReady}, " +
                $"partial={partial}, empty={empty}, readiness={readyPct:F1}%");
            return Result.Succeeded;
        }
    }
}
