using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Electrical.ArcFlash
{
    /// <summary>
    /// Builds a Revit ViewSchedule of OST_ElectricalEquipment showing the
    /// arc-flash parameters stamped by <see cref="ArcFlashCommand"/>.
    /// SchedulableField only surfaces shared parameters that have been
    /// bound and have at least one element with a non-null value, so this
    /// command should run AFTER the calc command.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ArcFlashScheduleCommand : IExternalCommand
    {
        // One schedule, remade on every run (a timestamped name left one per run).
        private const string ViewName = "STING - Arc Flash Schedule (" + ArcFlashEngine.BasisShort + ")";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // The drawing type routing gives E / ARC_FLASH_SCHEDULE. The id this stamped,
            // "elec-arc-flash-schedule", used to be in no drawing type.
            var req = StingTools.Core.Drawing.DrawingRouteRequests.ArcFlashSchedule;
            string drawingTypeId = StingTools.Core.Drawing.DrawingRouteResolver.IdFor(doc, req);

            ViewSchedule view = null;
            using (var tx = new Transaction(doc, "STING Arc Flash Schedule"))
            {
                tx.Start();
                var previous = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                    .FirstOrDefault(v => !v.IsTemplate && string.Equals(v.Name, ViewName, StringComparison.OrdinalIgnoreCase));
                if (previous != null)
                {
                    try { doc.Delete(previous.Id); }
                    catch (Exception ex)
                    {
                        StingTools.Core.Electrical.ElecTx.RollBackIfOpen(tx);
                        message = $"The previous '{ViewName}' could not be replaced: {ex.Message}";
                        StingLog.Warn(message);
                        if (!PresetDialog.Quiet) TaskDialog.Show("STING Arc Flash Schedule", message);
                        return Result.Failed;
                    }
                }
                view = ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_ElectricalEquipment));
                try { view.Name = ViewName; }
                catch (Exception ex) { StingLog.Warn($"Arc flash schedule name '{ViewName}': {ex.Message}"); }
                var def = view.Definition;

                AddByName(def, doc, "Mark");
                AddByName(def, doc, "ELC_PNL_DESIGNATION_NAME_TXT");
                AddByName(def, doc, "ELC_PNL_SHORT_CIRCUIT_RATING_KA", "Available Fault (kA)");
                AddByName(def, doc, "ELC_ARC_FLASH_IE_CAL_CM2", $"Incident Energy (cal/cm²) — {ArcFlashEngine.BasisShort}");
                AddByName(def, doc, "ELC_ARC_FLASH_BOUNDARY_MM", $"Arc Flash Boundary (mm) — {ArcFlashEngine.BasisShort}");
                AddByName(def, doc, "ELC_ARC_FLASH_PPE_CAT", "PPE Category (by energy, indicative)");
                AddByName(def, doc, "ELC_ARC_FLASH_WORK_DIST_MM", "Working Distance (mm)");
                AddByName(def, doc, "ELC_ARC_FLASH_LABEL_TXT", "Label / Basis");

                StampDrawingType(view, drawingTypeId);
                StingTools.Core.Electrical.ElecTx.Commit(tx, null);
            }

            // Onto its drawing type's sheet (found again by stamp; a re-run's schedule replaces it).
            string sheetLine = StingTools.Core.SLD.SldSheetPlacement.Place(doc, req, view);

            if (!PresetDialog.Quiet)
            {
                try { ctx.UIDoc.ActiveView = view; } catch (Exception ex) { StingLog.Warn($"Arc flash schedule: activate view: {ex.Message}"); }
            }
            PresetDialog.Show("STING Arc Flash Schedule",
                $"Schedule created: {view?.Name}\n" +
                $"Basis: {ArcFlashEngine.Basis}.\n\n" + sheetLine, ref message);
            return Result.Succeeded;
        }

        private static void AddByName(ScheduleDefinition def, Document doc,
            string paramName, string columnHeading = null)
        {
            try
            {
                var sf = def.GetSchedulableFields()
                    .FirstOrDefault(f => string.Equals(f.GetName(doc), paramName, StringComparison.OrdinalIgnoreCase));
                if (sf == null) return;
                var added = def.AddField(sf);
                if (!string.IsNullOrEmpty(columnHeading) && added != null)
                {
                    try { added.ColumnHeading = columnHeading; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Info($"AddByName {paramName}: {ex.Message}"); }
        }

        private static void StampDrawingType(View v, string drawingTypeId)
        {
            try
            {
                StingTools.Core.Drawing.DrawingTypeStamper.Stamp(v, drawingTypeId);
            }
            catch (Exception ex) { StingLog.Warn($"StampDrawingType: {ex.Message}"); }
        }
    }
}
