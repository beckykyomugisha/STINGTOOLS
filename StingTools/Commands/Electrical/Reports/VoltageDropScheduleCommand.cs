using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Commands.Electrical.VoltageDrop;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Electrical.Reports
{
    /// <summary>
    /// Writes ELC_CKT_VD_PCT to every power circuit then creates a Revit
    /// ViewSchedule of OST_ElectricalCircuit sorted by panel + circuit number.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class VoltageDropScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            // The id routing gives E / ELEC_PANEL_SCHEDULE, once per run.
            string drawingTypeId = StingTools.Core.Drawing.DrawingRouteResolver.IdFor(doc, StingTools.Core.Drawing.DrawingRouteRequests.PanelSchedule);

            var opts = StingElectricalCommandHandler.CurrentVDOptions
                       ?? new VDOptionsSnapshot { LightingLimitPct = 3.0, OtherLimitPct = 5.0,
                                                  Material = null, OperatingTempC = 70.0,
                                                  Standard = "BS7671" };
            var results = VoltageDropCommand.Calculate(doc, opts.Standard,
                opts.LightingLimitPct, opts.OtherLimitPct, opts.Material, opts.OperatingTempC);

            int written = 0;
            ViewSchedule view = null;
            using (var tx = new Transaction(doc, "STING VD Schedule"))
            {
                tx.Start();
                foreach (var r in results)
                {
                    try
                    {
                        var sys = doc.GetElement(r.CircuitId) as ElectricalSystem;
                        if (sys == null) continue;
                        // Count only writes that landed - the old count included
                        // every circuit whether or not the parameter was bound.
                        // The same stamp as Voltage Drop: value plus basis, NONE with a reason
                        // instead of 0.00 when a circuit cannot be calculated (ELEC-22).
                        if (r.Vd != null && StingTools.Core.Electrical.CircuitVoltageDropModel.Stamp(sys, r.Vd))
                            written++;
                    }
                    catch (Exception ex) { StingLog.Warn($"VD param write: {ex.Message}"); }
                }
                view = ViewSchedule.CreateSchedule(doc,
                    new ElementId(BuiltInCategory.OST_ElectricalCircuit));
                try { view.Name = $"STING - Voltage Drop Schedule - {DateTime.Now:yyyyMMdd-HHmm}"; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                AddVDFields(doc, view);
                AddSortPanelCircuit(view);
                StampDrawingType(view, drawingTypeId);
                tx.Commit();
            }
            int fail = results.Count(r => r.ExceedsThreshold);
            int possible = results.Count(r => r.PossiblyExceeds);
            int notCalc = results.Count(r => !r.HasValue);
            string vdNote = written == 0 && results.Count > 0
                ? $"\n\nNo voltage-drop values were written - '{ParamRegistry.ELC_CKT_VD_PCT}' / '{StingTools.Core.Electrical.CircuitVoltageDropModel.BasisParam}' are not bound to Electrical Circuits. Run Load Shared Params, then re-run."
                : "";
            TaskDialog.Show("STING VD Schedule",
                $"Created '{view?.Name}'. VD % and basis written to {written} of {results.Count} circuit(s). {fail} exceed the limit" +
                (possible > 0 ? $", {possible} possibly (upper bound)" : "") +
                (notCalc > 0 ? $", {notCalc} not calculated (see the basis column)" : "") + $".{vdNote}");
            return Result.Succeeded;
        }

        private static void AddVDFields(Document doc, ViewSchedule sched)
        {
            try
            {
                var def = sched.Definition;
                BuiltInParameter[] bips =
                {
                    BuiltInParameter.RBS_ELEC_CIRCUIT_PANEL_PARAM,
                    BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER,
                    BuiltInParameter.RBS_ELEC_CIRCUIT_NAME,
                    BuiltInParameter.RBS_ELEC_APPARENT_CURRENT_PARAM,
                    BuiltInParameter.RBS_ELEC_CIRCUIT_LENGTH_PARAM,
                    BuiltInParameter.RBS_ELEC_CIRCUIT_WIRE_SIZE_PARAM
                };
                foreach (var b in bips)
                {
                    try
                    {
                        var pid = new ElementId(b);
                        var sf = def.GetSchedulableFields().FirstOrDefault(f => f.ParameterId == pid);
                        if (sf != null) def.AddField(sf);
                    }
                    catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                }

                // The voltage-drop column itself. It is a shared parameter, so it
                // is found by name, not BuiltInParameter - the schedule used to be
                // created with every column EXCEPT the one it is named after. ELEC-26:
                // the text mirror, not the number — the number cannot be cleared when a
                // circuit is no longer calculable, the mirror always shows "—" then.
                string vdName = "ELC_VLT_DROP_TXT";
                var vdField = def.GetSchedulableFields()
                    .FirstOrDefault(f => string.Equals(f.GetName(doc), vdName, StringComparison.Ordinal));
                if (vdField != null) def.AddField(vdField);
                else StingLog.Warn($"VD schedule: '{vdName}' is not bound to Electrical Circuits - run Load Shared Params.");

                // How each figure was obtained (A4 / A4-MAX / R60228 / IMPORT / NONE + reason).
                string basisName = StingTools.Core.Electrical.CircuitVoltageDropModel.BasisParam;
                var basisField = def.GetSchedulableFields()
                    .FirstOrDefault(f => string.Equals(f.GetName(doc), basisName, StringComparison.Ordinal));
                if (basisField != null) def.AddField(basisField);
                else StingLog.Warn($"VD schedule: '{basisName}' is not bound to Electrical Circuits - run Load Shared Params.");
            }
            catch (Exception ex) { StingLog.Warn($"AddVDFields: {ex.Message}"); }
        }

        private static void AddSortPanelCircuit(ViewSchedule sched)
        {
            try
            {
                var def = sched.Definition;
                var fields = def.GetFieldOrder().Select(id => def.GetField(id)).ToList();
                var panel = fields.FirstOrDefault(f => f.GetName() == "Panel");
                var num   = fields.FirstOrDefault(f => f.GetName() == "Circuit Number");
                if (panel != null) def.AddSortGroupField(new ScheduleSortGroupField(panel.FieldId));
                if (num != null)   def.AddSortGroupField(new ScheduleSortGroupField(num.FieldId));
            }
            catch (Exception ex) { StingLog.Warn($"AddSort: {ex.Message}"); }
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
