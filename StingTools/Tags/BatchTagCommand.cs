using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// Batch-apply ISO 19650 tags to ALL taggable elements in the entire project model.
    /// Unlike AutoTagCommand which works on the active view, this processes the full model.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchTagCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType();

            var knownCategories = new HashSet<string>(TagConfig.DiscMap.Keys);
            int tagged = 0;
            int skipped = 0;
            int alreadyTagged = 0;
            var sequenceCounters = new Dictionary<string, int>();

            TaskDialog confirm = new TaskDialog("Batch Tag");
            confirm.MainInstruction = "Batch tag entire project?";
            confirm.MainContent =
                "This will apply ISO 19650 tags to all untagged elements " +
                "across the entire model. This may take a while for large projects.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            using (Transaction tx = new Transaction(doc, "STING Batch Tag"))
            {
                tx.Start();

                foreach (Element el in collector)
                {
                    string catName = ParameterHelpers.GetCategoryName(el);
                    if (string.IsNullOrEmpty(catName) || !knownCategories.Contains(catName))
                    {
                        skipped++;
                        continue;
                    }

                    string existingTag = ParameterHelpers.GetString(el, "ASS_TAG_1_TXT");
                    if (TagConfig.TagIsComplete(existingTag))
                    {
                        alreadyTagged++;
                        continue;
                    }

                    string disc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : "XX";
                    string loc = ParameterHelpers.GetString(el, "ASS_LOC_TXT");
                    if (string.IsNullOrEmpty(loc)) loc = "BLD1";
                    string zone = ParameterHelpers.GetString(el, "ASS_ZONE_TXT");
                    if (string.IsNullOrEmpty(zone)) zone = "Z01";
                    string lvl = ParameterHelpers.GetLevelCode(doc, el);
                    string sys = TagConfig.GetSysCode(catName);
                    string func = TagConfig.GetFuncCode(sys);
                    string prod = TagConfig.ProdMap.TryGetValue(catName, out string p) ? p : "GEN";

                    string seqKey = $"{disc}_{sys}_{lvl}";
                    if (!sequenceCounters.ContainsKey(seqKey))
                        sequenceCounters[seqKey] = 0;
                    sequenceCounters[seqKey]++;
                    string seq = sequenceCounters[seqKey]
                        .ToString()
                        .PadLeft(TagConfig.NumPad, '0');

                    string tag = string.Join(TagConfig.Separator,
                        disc, loc, zone, lvl, sys, func, prod, seq);

                    ParameterHelpers.SetIfEmpty(el, "ASS_DISCIPLINE_COD_TXT", disc);
                    ParameterHelpers.SetIfEmpty(el, "ASS_LOC_TXT", loc);
                    ParameterHelpers.SetIfEmpty(el, "ASS_ZONE_TXT", zone);
                    ParameterHelpers.SetIfEmpty(el, "ASS_LVL_COD_TXT", lvl);
                    ParameterHelpers.SetIfEmpty(el, "ASS_SYSTEM_TYPE_TXT", sys);
                    ParameterHelpers.SetIfEmpty(el, "ASS_FUNC_TXT", func);
                    ParameterHelpers.SetIfEmpty(el, "ASS_PRODCT_COD_TXT", prod);
                    ParameterHelpers.SetIfEmpty(el, "ASS_SEQ_NUM_TXT", seq);
                    ParameterHelpers.SetString(el, "ASS_TAG_1_TXT", tag, overwrite: true);
                    tagged++;
                }

                tx.Commit();
            }

            TaskDialog.Show("Batch Tag",
                $"Batch tagging complete.\n\n" +
                $"  Tagged:         {tagged}\n" +
                $"  Already tagged: {alreadyTagged}\n" +
                $"  Skipped:        {skipped}\n" +
                $"  Total:          {tagged + alreadyTagged + skipped}");

            return Result.Succeeded;
        }
    }
}
