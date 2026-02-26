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
    /// Ported from STINGTags.extension — core tagging logic from tag_logic.py + script.py.
    /// Automatically applies ISO 19650 asset tags to all taggable elements in the active view.
    /// Assembles: DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ → ASS_TAG_1_TXT.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoTagCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            if (activeView == null)
            {
                TaskDialog.Show("Auto Tag", "No active view.");
                return Result.Failed;
            }

            // Collect elements visible in active view
            var collector = new FilteredElementCollector(doc, activeView.Id)
                .WhereElementIsNotElementType();

            var knownCategories = new HashSet<string>(TagConfig.DiscMap.Keys);

            int tagged = 0;
            int skipped = 0;
            var sequenceCounters = new Dictionary<string, int>();

            using (Transaction tx = new Transaction(doc, "STING Auto Tag"))
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

                    // Check if already tagged
                    string existingTag = ParameterHelpers.GetString(el, "ASS_TAG_1_TXT");
                    if (TagConfig.TagIsComplete(existingTag))
                    {
                        skipped++;
                        continue;
                    }

                    // Derive tokens — use existing LOC/ZONE if already set
                    string disc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : "XX";
                    string loc = ParameterHelpers.GetString(el, "ASS_LOC_TXT");
                    if (string.IsNullOrEmpty(loc)) loc = "BLD1";
                    string zone = ParameterHelpers.GetString(el, "ASS_ZONE_TXT");
                    if (string.IsNullOrEmpty(zone)) zone = "Z01";
                    string lvl = ParameterHelpers.GetLevelCode(doc, el);
                    string sys = TagConfig.GetSysCode(catName);
                    string func = TagConfig.GetFuncCode(sys);
                    string prod = TagConfig.ProdMap.TryGetValue(catName, out string p) ? p : "GEN";

                    // Sequence number per (disc, sys, lvl)
                    string seqKey = $"{disc}_{sys}_{lvl}";
                    if (!sequenceCounters.ContainsKey(seqKey))
                        sequenceCounters[seqKey] = 0;
                    sequenceCounters[seqKey]++;
                    string seq = sequenceCounters[seqKey]
                        .ToString()
                        .PadLeft(TagConfig.NumPad, '0');

                    // Assemble tag
                    string tag = string.Join(TagConfig.Separator,
                        disc, loc, zone, lvl, sys, func, prod, seq);

                    // Write token parameters
                    ParameterHelpers.SetIfEmpty(el, "ASS_DISCIPLINE_COD_TXT", disc);
                    ParameterHelpers.SetIfEmpty(el, "ASS_LOC_TXT", loc);
                    ParameterHelpers.SetIfEmpty(el, "ASS_ZONE_TXT", zone);
                    ParameterHelpers.SetIfEmpty(el, "ASS_LVL_COD_TXT", lvl);
                    ParameterHelpers.SetIfEmpty(el, "ASS_SYSTEM_TYPE_TXT", sys);
                    ParameterHelpers.SetIfEmpty(el, "ASS_FUNC_TXT", func);
                    ParameterHelpers.SetIfEmpty(el, "ASS_PRODCT_COD_TXT", prod);
                    ParameterHelpers.SetIfEmpty(el, "ASS_SEQ_NUM_TXT", seq);

                    // Write assembled tag
                    ParameterHelpers.SetString(el, "ASS_TAG_1_TXT", tag, overwrite: true);
                    tagged++;
                }

                tx.Commit();
            }

            TaskDialog.Show("Auto Tag",
                $"Tagged {tagged} elements in '{activeView.Name}'.\n" +
                $"Skipped {skipped} (already tagged or unsupported category).");

            return Result.Succeeded;
        }
    }
}
