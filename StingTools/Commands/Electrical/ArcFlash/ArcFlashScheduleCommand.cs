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
        private const string DrawingTypeId = "elec-arc-flash-schedule";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            ViewSchedule view = null;
            using (var tx = new Transaction(doc, "STING Arc Flash Schedule"))
            {
                tx.Start();
                view = ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_ElectricalEquipment));
                try { view.Name = $"STING - Arc Flash Schedule - {DateTime.Now:yyyyMMdd-HHmm}"; } catch { }
                var def = view.Definition;

                AddByName(def, doc, "Mark");
                AddByName(def, doc, "ELC_PNL_DESIGNATION_NAME_TXT");
                AddByName(def, doc, "ELC_PNL_SHORT_CIRCUIT_RATING_KA", "Available Fault (kA)");
                AddByName(def, doc, "ELC_ARC_FLASH_IE_CAL_CM2", "Incident Energy (cal/cm²)");
                AddByName(def, doc, "ELC_ARC_FLASH_BOUNDARY_MM", "Arc Flash Boundary (mm)");
                AddByName(def, doc, "ELC_ARC_FLASH_PPE_CAT", "PPE Category");
                AddByName(def, doc, "ELC_ARC_FLASH_WORK_DIST_MM", "Working Distance (mm)");

                StampDrawingType(view);
                tx.Commit();
            }
            try { ctx.UIDoc.ActiveView = view; } catch { }
            TaskDialog.Show("STING Arc Flash Schedule",
                $"Schedule created: {view?.Name}\n" +
                "Place on a sheet manually — PanelScheduleSheetInstance.Create is broken in Revit 2024+.");
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
                    try { added.ColumnHeading = columnHeading; } catch { }
                }
            }
            catch (Exception ex) { StingLog.Info($"AddByName {paramName}: {ex.Message}"); }
        }

        private static void StampDrawingType(View v)
        {
            try
            {
                var t = Type.GetType("StingTools.Core.Drawing.DrawingTypeStamper");
                t?.GetMethod("Stamp", new[] { typeof(Element), typeof(string) })
                  ?.Invoke(null, new object[] { v, DrawingTypeId });
            }
            catch (Exception ex) { StingLog.Warn($"StampDrawingType: {ex.Message}"); }
        }
    }
}
