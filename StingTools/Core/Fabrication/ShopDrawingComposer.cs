// StingTools v4 MVP — ShopDrawingComposer.
//
// Creates a ViewSheet using the discipline-specific title block,
// places the 5 views from AssemblyViewSet at fixed slot positions
// and the BOM schedule via ScheduleSheetInstance.Create. Title-block
// parameters are populated from the assembly's spool metadata.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Fabrication
{
    public static class ShopDrawingComposer
    {
        // Title-block family names per discipline. Stub families live
        // under Families/AssemblyTitleBlocks/ — see S5.15 for the
        // parameter list each family must expose.
        private static readonly Dictionary<string, string> TitleBlockByDiscipline =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Pipe",       "STING_TB_ASSEMBLY_PIPE" },
            { "Plumbing",   "STING_TB_ASSEMBLY_PIPE" },
            { "Duct",       "STING_TB_ASSEMBLY_DUCT" },
            { "HVAC",       "STING_TB_ASSEMBLY_DUCT" },
            { "Electrical", "STING_TB_ASSEMBLY_COND" },
            { "Hanger",     "STING_TB_ASSEMBLY_HANGER" },
            { "Generic",    "STING_TB_ASSEMBLY_PIPE" }
        };

        // Slot positions on a 1:50 A1 sheet (Revit feet, sheet origin).
        // Plan TL, ISO TR, Elev0 BL, Elev90 ML, 3D BR, BOM RIGHT-PANEL.
        private static readonly Dictionary<string, XYZ> SlotPositions =
            new Dictionary<string, XYZ>
        {
            { "PLAN",       new XYZ( 0.20, 1.95, 0) },
            { "ISO",        new XYZ( 1.55, 1.95, 0) },
            { "ELEV0",      new XYZ( 0.20, 1.20, 0) },
            { "ELEV90",     new XYZ( 1.05, 1.20, 0) },
            { "3D",         new XYZ( 1.55, 1.20, 0) },
            { "BOM",        new XYZ( 2.20, 1.95, 0) }
        };

        public static ElementId ComposeSheet(
            Document doc,
            string discipline,
            ElementId assemblyId,
            AssemblyViewSet views,
            FabricationResult result)
        {
            if (doc == null || assemblyId == null || views == null) return null;

            ElementId tbId = ResolveTitleBlock(doc, discipline);
            ViewSheet sheet = null;
            try
            {
                sheet = ViewSheet.Create(doc, tbId);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"ShopDrawingComposer: ViewSheet.Create failed: {ex.Message}");
                return null;
            }
            if (sheet == null) return null;

            try { ApplySheetMetadata(doc, sheet, assemblyId, discipline, result); }
            catch (Exception ex) { result.Warnings.Add($"Sheet metadata: {ex.Message}"); }

            // Place views at fixed slots
            PlaceView(doc, sheet, views.ViewPlan,    SlotPositions["PLAN"],   result);
            PlaceView(doc, sheet, views.ViewIso6412, SlotPositions["ISO"],    result);
            PlaceView(doc, sheet, views.Elevation0,  SlotPositions["ELEV0"],  result);
            PlaceView(doc, sheet, views.Elevation90, SlotPositions["ELEV90"], result);
            PlaceView(doc, sheet, views.View3D,      SlotPositions["3D"],     result);

            // Place BOM schedule
            try
            {
                if (views.BomSchedule != null && views.BomSchedule != ElementId.InvalidElementId)
                {
                    ScheduleSheetInstance.Create(doc, sheet.Id, views.BomSchedule,
                        SlotPositions["BOM"]);
                }
            }
            catch (Exception ex) { result.Warnings.Add($"BOM placement: {ex.Message}"); }

            return sheet.Id;
        }

        private static void PlaceView(Document doc, ViewSheet sheet, ElementId viewId, XYZ pos,
            FabricationResult result)
        {
            if (viewId == null || viewId == ElementId.InvalidElementId) return;
            try
            {
                if (Viewport.CanAddViewToSheet(doc, sheet.Id, viewId))
                    Viewport.Create(doc, sheet.Id, viewId, pos);
                else
                    result.Warnings.Add($"View {viewId.Value} cannot be placed on sheet {sheet.SheetNumber}");
            }
            catch (Exception ex) { result.Warnings.Add($"PlaceView {viewId.Value}: {ex.Message}"); }
        }

        private static ElementId ResolveTitleBlock(Document doc, string discipline)
        {
            string familyName = TitleBlockByDiscipline.TryGetValue(discipline ?? "", out var n)
                ? n : TitleBlockByDiscipline["Generic"];
            try
            {
                var col = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilySymbol));
                foreach (var el in col)
                {
                    if (el is FamilySymbol fs && string.Equals(fs.FamilyName, familyName, StringComparison.OrdinalIgnoreCase))
                        return fs.Id;
                }
                // Fallback: first available title block
                foreach (var el in col)
                    if (el is FamilySymbol fs) return fs.Id;
            }
            catch (Exception ex) { StingLog.Warn($"ShopDrawingComposer: title block resolve: {ex.Message}"); }
            return ElementId.InvalidElementId;
        }

        private static void ApplySheetMetadata(Document doc, ViewSheet sheet, ElementId assemblyId,
            string discipline, FabricationResult result)
        {
            var ai = doc.GetElement(assemblyId) as AssemblyInstance;
            if (ai == null) return;

            string spool = ReadString(ai, AssyParams.SPOOL_NR_TXT);
            if (!string.IsNullOrEmpty(spool))
            {
                try { sheet.SheetNumber = spool; } catch { }
                try { sheet.Name        = spool; } catch { }
            }

            // Title-block instance parameters live on the title block
            // FamilyInstance, accessible via collector. We set the
            // common ones if present.
            try
            {
                var tbInst = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks).FirstElement();
                if (tbInst != null)
                {
                    TrySetString(tbInst, AssyParams.SPOOL_NR_TXT,    spool);
                    TrySetString(tbInst, AssyParams.WEIGHT_KG,       ReadString(ai, AssyParams.WEIGHT_KG));
                    TrySetString(tbInst, AssyParams.FAB_LOC_TXT,     ReadString(ai, AssyParams.FAB_LOC_TXT));
                    TrySetString(tbInst, AssyParams.FAB_STATUS_TXT,  ReadString(ai, AssyParams.FAB_STATUS_TXT));
                    TrySetString(tbInst, AssyParams.BOM_REV_TXT,     ReadString(ai, AssyParams.BOM_REV_TXT));
                    TrySetString(tbInst, "DISCIPLINE",               discipline);
                }
            }
            catch (Exception ex) { result.Warnings.Add($"Title block populate: {ex.Message}"); }
        }

        private static string ReadString(Element el, string param)
        {
            try { return el?.LookupParameter(param)?.AsString() ?? ""; } catch { return ""; }
        }
        private static void TrySetString(Element el, string param, string val)
        {
            try { var p = el.LookupParameter(param);
                  if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String) p.Set(val); }
            catch { }
        }
    }
}
