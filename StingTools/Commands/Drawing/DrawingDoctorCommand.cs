// StingTools — Drawing Template Manager · Phase 168
//
// DrawingDoctorCommand audits the title-block layer for cross-stamp
// conflicts between the legacy CSV populate path
// (TitleBlockPopulateCommand → PRJ_TB_LAST_SYNC_TXT) and the recipe
// binding path (DrawingTypePresentation.ApplyToSheet →
// STING_DRAWING_TYPE_ID_TXT). A sheet that carries BOTH stamps may
// have diverged values: the recipe applies on each sync, but the CSV
// populate writes a separate vocabulary, leaving the operator unsure
// which is authoritative. This command reports cross-stamps, missing
// title blocks, family swaps, and stale syncs so the operator can
// pick a single doctrine per project.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.ReadOnly)]
    public class DrawingDoctorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            try
            {
                var doc = data?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { msg = "No document open."; return Result.Failed; }

                var sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => !s.IsPlaceholder)
                    .OrderBy(s => s.SheetNumber, StringComparer.Ordinal)
                    .ToList();

                int totalSheets = sheets.Count;
                var crossStamped = new List<string>();
                var recipeOnly   = new List<string>();
                var csvOnly      = new List<string>();
                var unstamped    = new List<string>();
                var missingTb    = new List<string>();
                var familySwap   = new List<string>();
                var staleSync    = new List<string>(); // CSV-stamp older than 30 days

                foreach (var s in sheets)
                {
                    var dtId    = SafeRead(s, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID);
                    var lastSync = SafeReadFromTb(doc, s, "PRJ_TB_LAST_SYNC_TXT");
                    bool hasRecipe = !string.IsNullOrEmpty(dtId);
                    bool hasCsv    = !string.IsNullOrEmpty(lastSync);

                    if (hasRecipe && hasCsv) crossStamped.Add($"{s.SheetNumber}  recipe='{dtId}'  csvSync='{lastSync}'");
                    else if (hasRecipe)      recipeOnly.Add($"{s.SheetNumber}  recipe='{dtId}'");
                    else if (hasCsv)         csvOnly.Add($"{s.SheetNumber}  csvSync='{lastSync}'");
                    else                     unstamped.Add(s.SheetNumber);

                    var tbs = TbsOnSheet(doc, s);
                    if (tbs.Count == 0) { missingTb.Add(s.SheetNumber); continue; }

                    if (hasRecipe)
                    {
                        var dt = DrawingTypeRegistry.Get(doc, dtId);
                        if (dt != null && !string.IsNullOrEmpty(dt.TitleBlockFamily))
                        {
                            foreach (var tb in tbs)
                            {
                                var liveFam = tb.Symbol?.FamilyName ?? "(unknown)";
                                if (!string.Equals(liveFam, dt.TitleBlockFamily, StringComparison.OrdinalIgnoreCase))
                                {
                                    familySwap.Add($"{s.SheetNumber}  live='{liveFam}'  profile='{dt.TitleBlockFamily}'");
                                    break;
                                }
                            }
                        }
                    }

                    if (hasCsv && DateTime.TryParse(lastSync, CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var when)
                        && (DateTime.UtcNow - when).TotalDays > 30)
                        staleSync.Add($"{s.SheetNumber}  lastSync={when:yyyy-MM-dd}");
                }

                var sb = new StringBuilder();
                sb.AppendLine($"STING — Drawing Doctor");
                sb.AppendLine($"  Total sheets: {totalSheets}");
                sb.AppendLine($"  Recipe-stamped only:    {recipeOnly.Count}");
                sb.AppendLine($"  CSV-populated only:     {csvOnly.Count}");
                sb.AppendLine($"  Cross-stamped (BOTH):   {crossStamped.Count}    ◀ pick a doctrine");
                sb.AppendLine($"  Unstamped:              {unstamped.Count}");
                sb.AppendLine($"  Sheets with no TB:      {missingTb.Count}");
                sb.AppendLine($"  Family swaps:           {familySwap.Count}");
                sb.AppendLine($"  Stale CSV sync (>30d):  {staleSync.Count}");
                AppendList(sb, "Cross-stamped sheets", crossStamped);
                AppendList(sb, "Family swaps",         familySwap);
                AppendList(sb, "Missing title block",  missingTb);
                AppendList(sb, "Stale CSV sync",       staleSync);
                AppendList(sb, "Unstamped sheets",     unstamped);

                var dlg = new TaskDialog("STING — Drawing Doctor")
                {
                    MainInstruction = $"{crossStamped.Count} cross-stamp(s), {familySwap.Count} family swap(s), {missingTb.Count} missing TB",
                    MainContent = "Doctor inspects the title-block layer for divergence between the CSV-populate path and the recipe-binding path. " +
                                  "Cross-stamped sheets carry stamps from both paths — values may have diverged.",
                    ExpandedContent = sb.ToString(),
                    CommonButtons = TaskDialogCommonButtons.Close,
                };
                dlg.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingDoctor", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }

        private static string SafeRead(Element el, string paramName)
        {
            try { return el?.LookupParameter(paramName)?.AsString(); }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        private static string SafeReadFromTb(Document doc, ViewSheet sheet, string paramName)
        {
            try
            {
                var tb = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .FirstOrDefault();
                return tb?.LookupParameter(paramName)?.AsString();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        private static List<FamilyInstance> TbsOnSheet(Document doc, ViewSheet sheet)
        {
            try
            {
                return new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .ToList();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return new List<FamilyInstance>(); }
        }

        private static void AppendList(StringBuilder sb, string label, List<string> items)
        {
            if (items == null || items.Count == 0) return;
            sb.AppendLine();
            sb.AppendLine($"{label} ({items.Count}):");
            foreach (var it in items.Take(25)) sb.AppendLine("  " + it);
            if (items.Count > 25) sb.AppendLine($"  …({items.Count - 25} more)");
        }
    }
}
