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
    /// One-click "Tag and Combine All" — chains AutoTag + CombineParameters
    /// so the user gets a fully-tagged project with all containers populated
    /// in a single click. This is the max-automation counterpart to MasterSetup.
    ///
    /// Workflow:
    ///   1. Auto-populate tokens (DISC, PROD, SYS, FUNC, LVL) on all elements
    ///   2. Auto-tag all untagged elements (continues from existing sequence)
    ///   3. Combine parameters into all containers (ASS_TAG_1-6 + discipline + MAT)
    ///
    /// Scope options:
    ///   - Active view only
    ///   - Selected elements only
    ///   - Entire project
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagAndCombineCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Step 0: Choose scope
            TaskDialog scopeDlg = new TaskDialog("Tag & Combine All");
            scopeDlg.MainInstruction = "Tag and populate all containers";
            scopeDlg.MainContent =
                "This will:\n" +
                "  1. Auto-populate token parameters (DISC, PROD, SYS, FUNC, LVL)\n" +
                "  2. Tag all untagged elements (continuing from existing numbers)\n" +
                "  3. Combine all tokens into ALL tag containers\n\n" +
                "Choose scope:";
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Active View",
                "Process only elements visible in the current view");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Selected Elements",
                $"{uidoc.Selection.GetElementIds().Count} elements selected");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Entire Project",
                "Process all taggable elements across the entire model");
            scopeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var scopeResult = scopeDlg.Show();

            ICollection<ElementId> targetIds;
            string scopeLabel;
            switch (scopeResult)
            {
                case TaskDialogResult.CommandLink1:
                    targetIds = new FilteredElementCollector(doc, doc.ActiveView.Id)
                        .WhereElementIsNotElementType()
                        .Select(e => e.Id).ToList();
                    scopeLabel = $"active view '{doc.ActiveView.Name}'";
                    break;
                case TaskDialogResult.CommandLink2:
                    targetIds = uidoc.Selection.GetElementIds();
                    if (targetIds.Count == 0)
                    {
                        TaskDialog.Show("Tag & Combine", "No elements selected.");
                        return Result.Cancelled;
                    }
                    scopeLabel = $"{targetIds.Count} selected elements";
                    break;
                case TaskDialogResult.CommandLink3:
                    targetIds = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType()
                        .Select(e => e.Id).ToList();
                    scopeLabel = "entire project";
                    break;
                default:
                    return Result.Cancelled;
            }

            var known = new HashSet<string>(TagConfig.DiscMap.Keys);
            var seqCounters = TagConfig.GetExistingSequenceCounters(doc);
            var tagIndex = TagConfig.BuildExistingTagIndex(doc);

            int populated = 0;
            int tagged = 0;
            int combined = 0;
            int totalProcessed = 0;

            // Token parameter names for combine step
            string[] allTokenParams = new[]
            {
                "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT",
                "ASS_LVL_COD_TXT", "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT",
                "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT",
            };

            // Combine container definitions (inline for speed — no need to instantiate CombineParametersCommand)
            var containers = new (string param, string[] tokens)[]
            {
                ("ASS_TAG_1_TXT", allTokenParams),
                ("ASS_TAG_2_TXT", new[] { "ASS_DISCIPLINE_COD_TXT", "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT" }),
                ("ASS_TAG_3_TXT", new[] { "ASS_LOC_TXT", "ASS_ZONE_TXT", "ASS_LVL_COD_TXT" }),
                ("ASS_TAG_4_TXT", new[] { "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT" }),
                ("ASS_TAG_5_TXT", new[] { "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT", "ASS_LVL_COD_TXT" }),
                ("ASS_TAG_6_TXT", new[] { "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT", "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT" }),
            };

            using (Transaction tx = new Transaction(doc, "STING Tag & Combine All"))
            {
                tx.Start();

                foreach (ElementId id in targetIds)
                {
                    Element el = doc.GetElement(id);
                    if (el == null) continue;

                    string catName = ParameterHelpers.GetCategoryName(el);
                    if (string.IsNullOrEmpty(catName) || !known.Contains(catName))
                        continue;

                    totalProcessed++;

                    // Step 1: Auto-populate tokens from category lookup
                    string disc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : "XX";
                    if (ParameterHelpers.SetIfEmpty(el, "ASS_DISCIPLINE_COD_TXT", disc)) populated++;
                    if (TagConfig.ProdMap.TryGetValue(catName, out string prod))
                        if (ParameterHelpers.SetIfEmpty(el, "ASS_PRODCT_COD_TXT", prod)) populated++;
                    string sys = TagConfig.GetSysCode(catName);
                    if (!string.IsNullOrEmpty(sys))
                        if (ParameterHelpers.SetIfEmpty(el, "ASS_SYSTEM_TYPE_TXT", sys)) populated++;
                    string func = TagConfig.GetFuncCode(sys);
                    if (!string.IsNullOrEmpty(func))
                        if (ParameterHelpers.SetIfEmpty(el, "ASS_FUNC_TXT", func)) populated++;
                    string lvl = ParameterHelpers.GetLevelCode(doc, el);
                    if (lvl != "XX")
                        if (ParameterHelpers.SetIfEmpty(el, "ASS_LVL_COD_TXT", lvl)) populated++;

                    // Step 2: Tag if not already complete (with collision detection)
                    if (TagConfig.BuildAndWriteTag(doc, el, seqCounters,
                        existingTags: tagIndex))
                        tagged++;

                    // Step 3: Combine into all containers
                    var tokenValues = new Dictionary<string, string>();
                    foreach (string param in allTokenParams)
                        tokenValues[param] = ParameterHelpers.GetString(el, param);

                    if (tokenValues.Values.Any(v => !string.IsNullOrEmpty(v)))
                    {
                        foreach (var (param, tokens) in containers)
                        {
                            var parts = tokens.Select(t => tokenValues.TryGetValue(t, out string v) ? v : "").ToList();
                            string assembled = string.Join("-", parts);
                            if (ParameterHelpers.SetString(el, param, assembled, overwrite: true))
                                combined++;
                        }
                    }
                }

                tx.Commit();
            }

            var report = new StringBuilder();
            report.AppendLine("Tag & Combine All Complete");
            report.AppendLine(new string('═', 45));
            report.AppendLine($"  Scope:       {scopeLabel}");
            report.AppendLine($"  Processed:   {totalProcessed} elements");
            report.AppendLine($"  Populated:   {populated} token values");
            report.AppendLine($"  Tagged:      {tagged} new tags");
            report.AppendLine($"  Combined:    {combined} container values");

            TaskDialog td = new TaskDialog("Tag & Combine All");
            td.MainInstruction = $"Processed {totalProcessed} elements";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"TagAndCombine: scope={scopeLabel}, processed={totalProcessed}, " +
                $"populated={populated}, tagged={tagged}, combined={combined}");

            return Result.Succeeded;
        }
    }
}
