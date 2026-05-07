using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Commands.Electrical.FaultCurrent;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Reports
{
    /// <summary>
    /// Builds a Revit ViewSchedule of OST_ElectricalEquipment showing the
    /// fault levels stamped by FaultCurrentCommand. Sorted by fault kA so
    /// the worst-case panels rise to the top.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FaultCurrentScheduleCommand : IExternalCommand
    {
        private const string DrawingTypeId = "elec-panel-schedule-A3";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            if (FaultCurrentCommand.LastResults == null || FaultCurrentCommand.LastResults.Count == 0)
            {
                TaskDialog.Show("STING Fault Schedule", "Run fault-current calculation first.");
                return Result.Failed;
            }

            ViewSchedule view = null;
            using (var tx = new Transaction(doc, "STING Fault Schedule"))
            {
                tx.Start();
                view = ViewSchedule.CreateSchedule(doc,
                    new ElementId(BuiltInCategory.OST_ElectricalEquipment));
                try { view.Name = $"STING - Fault Level Schedule - {DateTime.Now:yyyyMMdd-HHmm}"; } catch { }
                AddFields(view);
                StampDrawingType(view);
                tx.Commit();
            }
            TaskDialog.Show("STING Fault Schedule",
                $"Created '{view?.Name}'. Open the schedule view to inspect / sort.");
            return Result.Succeeded;
        }

        private static void AddFields(ViewSchedule sched)
        {
            try
            {
                var def = sched.Definition;
                AddByName(def, "Panel Name");
                AddByName(def, "ELC_PNL_DESIGNATION_NAME_TXT");
                AddByName(def, "ELC_PNL_VLT_V");
                AddByName(def, "ELC_FEEDER_CSA_MM2");
                AddByName(def, "ELC_PNL_SHORT_CIRCUIT_RATING_KA");
                AddByName(def, "ELC_PNL_AIC_RATING_KA");
            }
            catch (Exception ex) { StingLog.Warn($"AddFields: {ex.Message}"); }
        }

        private static void AddByName(ScheduleDefinition def, string paramName)
        {
            try
            {
                var sf = def.GetSchedulableFields()
                    .FirstOrDefault(f => string.Equals(f.GetName(def.Document), paramName, StringComparison.OrdinalIgnoreCase));
                if (sf != null) def.AddField(sf);
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
