using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

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
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var allGroups = ParamRegistry.ContainerGroups;

            // Step 1: Mode selection using StingModePicker
            int totalContainers = allGroups.Sum(g => g.Params.Length);
            var modeOptions = new List<UI.StingModePicker.ModeOption>
            {
                new("All Containers",
                    $"Populate all {allGroups.Length} groups ({totalContainers} parameters)", "all", true),
                new("Universal Only (ASS_TAG_1-6)",
                    "6 universal containers applied to all tagged elements", "universal"),
                new("Discipline Only",
                    "MEP + Comms discipline-specific containers (excludes Universal and Material)", "discipline"),
                new("Pick Container Groups...",
                    "Interactively choose which groups to populate", "pick"),
            };
            string modeResult = UI.StingModePicker.Show(
                "Combine Parameters",
                "Assemble token parameters into tag containers",
                modeOptions,
                $"Available: {allGroups.Length} groups, {totalContainers} total containers");

            HashSet<string> selectedGroupCodes;
            switch (modeResult)
            {
                case "all":
                    selectedGroupCodes = new HashSet<string>(allGroups.Select(g => g.GroupCode));
                    break;
                case "universal":
                    selectedGroupCodes = new HashSet<string> { "UNIVERSAL" };
                    break;
                case "discipline":
                    selectedGroupCodes = new HashSet<string>(
                        allGroups.Where(g => g.GroupCode != "UNIVERSAL" && g.GroupCode != "MAT_TAG")
                                 .Select(g => g.GroupCode));
                    break;
                case "pick":
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

        // ── Group picker: StingListPicker selection ─────────────────

        private HashSet<string> ShowGroupPicker(Document doc, ParamRegistry.ContainerGroupDef[] allGroups)
        {
            var knownCategories = new HashSet<string>(TagConfig.DiscMap.Keys);
            var catCounts = new Dictionary<string, int>();
            var gpColl = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            var gpCatEnums = SharedParamGuids.AllCategoryEnums;
            if (gpCatEnums != null && gpCatEnums.Length > 0)
                gpColl.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(gpCatEnums)));
            foreach (Element el in gpColl)
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                if (!knownCategories.Contains(cat)) continue;
                if (catCounts.ContainsKey(cat)) catCounts[cat]++;
                else catCounts[cat] = 1;
            }

            var groupItems = allGroups.Select(g =>
            {
                int elemCount = g.Categories != null
                    ? g.Categories.Sum(c => catCounts.TryGetValue(c, out int n) ? n : 0)
                    : catCounts.Values.Sum();
                return new Select.StingListPicker.ListItem
                {
                    Label = g.Group,
                    Detail = $"{g.Params.Length} containers | {elemCount} elements",
                    Tag = g.GroupCode
                };
            }).ToList();

            var picked = Select.StingListPicker.Show(
                "Combine Parameters — Select Groups",
                $"{allGroups.Length} container groups available. Select groups to populate.",
                groupItems, allowMultiSelect: true);

            if (picked == null || picked.Count == 0) return null;
            return new HashSet<string>(
                picked.Select(p => p.Tag as string).Where(s => !string.IsNullOrEmpty(s)));
        }

        // ── Core combine logic ───────────────────────────────────────

        private Result ExecuteCombine(Document doc, ParamRegistry.ContainerGroupDef[] activeGroups)
        {
            var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            var combCatEnums = SharedParamGuids.AllCategoryEnums;
            if (combCatEnums != null && combCatEnums.Length > 0)
                collector.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(combCatEnums)));
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
                        // Auto-detect DISC from category before skipping
                        disc = TagConfig.DiscMap.TryGetValue(catName, out string autoDisc) ? autoDisc : null;
                        if (!string.IsNullOrEmpty(disc))
                        {
                            ParameterHelpers.SetIfEmpty(el, ParamRegistry.DISC, disc);
                        }
                        else
                        {
                            skippedNoDisc++;
                            continue;
                        }
                    }

                    totalElements++;

                    // Read all source tokens once
                    string[] tokenValues = ParamRegistry.ReadTokenValues(el);

                    // Ensure TAG1 is current: rebuild from tokens if empty or stale
                    string currentTag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                    string rebuiltTag1 = string.Join(ParamRegistry.Separator, tokenValues);
                    if (!string.IsNullOrEmpty(TagConfig.TagPrefix))
                        rebuiltTag1 = TagConfig.TagPrefix + ParamRegistry.Separator + rebuiltTag1;
                    if (!string.IsNullOrEmpty(TagConfig.TagSuffix))
                        rebuiltTag1 = rebuiltTag1 + ParamRegistry.Separator + TagConfig.TagSuffix;
                    if (string.IsNullOrEmpty(currentTag1) || currentTag1 != rebuiltTag1)
                        ParameterHelpers.SetString(el, ParamRegistry.TAG1, rebuiltTag1, overwrite: true);

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
                    // Only write TAG7 if core tokens (DISC, SYS, PROD) are populated to avoid
                    // generating incomplete narratives from partially-tagged elements
                    bool hasCoreTags = tokenValues.Length >= 8
                        && !string.IsNullOrEmpty(tokenValues[0])   // DISC
                        && !string.IsNullOrEmpty(tokenValues[4])   // SYS
                        && !string.IsNullOrEmpty(tokenValues[6]);  // PROD
                    int tag7Writes = 0;
                    if (hasCoreTags)
                        tag7Writes = TagConfig.WriteTag7All(doc, el, catName, tokenValues, overwrite: true);
                    totalWrites += tag7Writes;
                    if (tag7Writes > 0 && writesPerGroup.ContainsKey("UNIVERSAL"))
                        writesPerGroup["UNIVERSAL"] += tag7Writes;
                }

                tx.Commit();
            }
            // GAP-01: Invalidate caches after container writes
            ComplianceScan.InvalidateCache();
            StingAutoTagger.InvalidateContext();
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
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
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

            var pfColl = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            var pfCatEnums = SharedParamGuids.AllCategoryEnums;
            if (pfCatEnums != null && pfCatEnums.Length > 0)
                pfColl.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(pfCatEnums)));
            foreach (Element el in pfColl)
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

    /// <summary>
    /// Container Pre-Check — verifies all container parameters are bound and writable
    /// before running Combine Parameters. Reports any unbound or read-only parameters
    /// that would silently fail during combine.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ContainerPreCheckCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var allGroups = ParamRegistry.ContainerGroups;
            if (allGroups == null || allGroups.Length == 0)
            {
                TaskDialog.Show("Container Pre-Check", "No container groups defined in ParamRegistry.");
                return Result.Failed;
            }

            // Get a sample tagged element to test parameter writability
            var catEnums = SharedParamGuids.AllCategoryEnums;
            var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            if (catEnums != null && catEnums.Length > 0)
                collector.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));

            Element sample = collector.FirstOrDefault(e =>
                !string.IsNullOrEmpty(ParameterHelpers.GetString(e, ParamRegistry.TAG1)));

            var report = new StringBuilder();
            report.AppendLine("Container Parameter Pre-Check");
            report.AppendLine(new string('═', 55));

            int totalParams = 0;
            int bound = 0;
            int unbound = 0;
            int readOnly = 0;
            var unboundList = new List<string>();

            foreach (var group in allGroups)
            {
                int groupBound = 0;
                int groupUnbound = 0;

                foreach (var cpd in group.Params)
                {
                    string paramName = cpd.ParamName;
                    totalParams++;
                    if (sample != null)
                    {
                        Parameter p = sample.LookupParameter(paramName);
                        if (p == null)
                        {
                            groupUnbound++;
                            unbound++;
                            unboundList.Add(paramName);
                        }
                        else if (p.IsReadOnly)
                        {
                            readOnly++;
                            bound++;
                        }
                        else
                        {
                            bound++;
                            groupBound++;
                        }
                    }
                    else
                    {
                        // No sample element — check definition exists
                        var def = ParamRegistry.GetGuid(paramName);
                        if (def != Guid.Empty) bound++;
                        else { unbound++; unboundList.Add(paramName); }
                    }
                }

                string status = groupUnbound == 0 ? "OK" : $"{groupUnbound} missing";
                report.AppendLine($"  {group.Group,-25} {group.Params.Length,3} params — {status}");
            }

            report.AppendLine();
            report.AppendLine($"  Total:    {totalParams} container parameters");
            report.AppendLine($"  Bound:    {bound}");
            report.AppendLine($"  Unbound:  {unbound}");
            if (readOnly > 0)
                report.AppendLine($"  ReadOnly: {readOnly}");

            if (unboundList.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("  Unbound parameters (run Load Shared Params first):");
                foreach (string p in unboundList.Take(20))
                    report.AppendLine($"    - {p}");
                if (unboundList.Count > 20)
                    report.AppendLine($"    ... and {unboundList.Count - 20} more");
            }

            report.AppendLine();
            if (unbound == 0)
                report.AppendLine("RESULT: All container parameters are bound and ready.");
            else
                report.AppendLine($"RESULT: {unbound} parameters not bound. Run Load Shared Params to fix.");

            TaskDialog td = new TaskDialog("Container Pre-Check");
            td.MainInstruction = unbound == 0
                ? $"All {totalParams} container parameters ready"
                : $"{unbound} of {totalParams} container parameters not bound";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"ContainerPreCheck: total={totalParams}, bound={bound}, unbound={unbound}, readOnly={readOnly}");
            return Result.Succeeded;
        }
    }
}
