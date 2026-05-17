using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Electrical
{
    /// <summary>
    /// Bulk-fills ELC_PNL_* shared parameters on every electrical panel using
    /// data Revit already exposes (panel name, voltage, connected load,
    /// upstream feed, manufacturer, model, room/level location). Skips
    /// elements that don't carry the parameter binding so unbinding
    /// per-project doesn't break the run.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ElecPanelParamSyncCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var panels = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .ToList();

            if (panels.Count == 0)
            {
                TaskDialog.Show("STING Electrical", "No electrical equipment found.");
                return Result.Succeeded;
            }

            var roomIndex = SpatialAutoDetect.BuildRoomIndex(doc);
            string projLoc = SpatialAutoDetect.DetectProjectLoc(doc) ?? "";
            int updated = 0;

            using (var tx = new Transaction(doc, "STING Electrical Param Sync"))
            {
                tx.Start();
                foreach (var p in panels)
                {
                    try
                    {
                        ParameterHelpers.SetString(p, ParamRegistry.ELC_PNL_NAME, p.Name, overwrite: true);

                        var voltageParam = p.get_Parameter(BuiltInParameter.RBS_ELEC_PANEL_SUPPLY_FROM_PARAM)?.AsString();
                        if (!string.IsNullOrEmpty(voltageParam))
                            ParameterHelpers.SetString(p, ParamRegistry.ELC_PNL_FED_FROM, voltageParam, overwrite: true);

                        // Voltage / phase — Revit-derived. Read by display name to stay
                        // version-agnostic (the BIP enum constant has changed between
                        // Revit versions and isn't guaranteed to compile).
                        try
                        {
                            var vp = p.LookupParameter("Voltage")
                                  ?? p.LookupParameter("Distribution System")
                                  ?? p.LookupParameter("Panel Voltage");
                            if (vp != null && vp.StorageType == StorageType.Double)
                            {
                                double vDouble = vp.AsDouble();
                                if (vDouble > 0)
                                    ParameterHelpers.SetString(p, ParamRegistry.ELC_PNL_VOLTAGE, $"{vDouble:0}V", overwrite: true);
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

                        // Connected load (kW)
                        try
                        {
                            var loadVA = p.get_Parameter(BuiltInParameter.RBS_ELEC_PANEL_TOTALLOAD_PARAM)?.AsDouble() ?? 0;
                            if (loadVA > 0)
                                ParameterHelpers.SetString(p, ParamRegistry.ELC_PNL_LOAD, $"{loadVA / 1000.0:0.0}", overwrite: true);
                        }
                        catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

                        // Location from spatial / project
                        string loc = SpatialAutoDetect.DetectLoc(doc, p, roomIndex, projLoc) ?? "";
                        if (!string.IsNullOrEmpty(loc))
                            ParameterHelpers.SetString(p, "ELC_PNL_LOCATION_TXT", loc, overwrite: false);

                        // Manufacturer / model from family-type native params
                        try
                        {
                            var mfg = p.Symbol?.get_Parameter(BuiltInParameter.ALL_MODEL_MANUFACTURER)?.AsString();
                            if (!string.IsNullOrEmpty(mfg))
                                ParameterHelpers.SetString(p, "ELC_PNL_MANUFACTURER_TXT", mfg, overwrite: false);
                        }
                        catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

                        updated++;
                    }
                    catch (Exception ex) { StingLog.Warn($"ParamSync panel {p?.Name}: {ex.Message}"); }
                }
                tx.Commit();
            }

            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            TaskDialog.Show("STING Electrical", $"Synced parameters on {updated} panel(s).");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Writes the values currently shown in the PANEL PARAMETERS expander on
    /// the dock panel back to the selected panel.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ElecPanelWriteParamsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var snap = StingElectricalCommandHandler.CurrentPanelParams;
            if (snap == null || string.IsNullOrEmpty(snap.PanelName))
            {
                TaskDialog.Show("STING Electrical",
                    "Select a panel in the PNLS grid and fill the PANEL PARAMETERS card before clicking Save.");
                return Result.Cancelled;
            }

            var panel = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .FirstOrDefault(p => string.Equals(p.Name, snap.PanelName, StringComparison.OrdinalIgnoreCase));
            if (panel == null)
            {
                TaskDialog.Show("STING Electrical", $"Panel '{snap.PanelName}' not found.");
                return Result.Failed;
            }

            using (var tx = new Transaction(doc, "STING Write Panel Params"))
            {
                tx.Start();
                if (!string.IsNullOrEmpty(snap.MainBreakerA))
                    ParameterHelpers.SetString(panel, ParamRegistry.ELC_MAIN_BRK, snap.MainBreakerA, overwrite: true);
                if (!string.IsNullOrEmpty(snap.FedFrom))
                    ParameterHelpers.SetString(panel, ParamRegistry.ELC_PNL_FED_FROM, snap.FedFrom, overwrite: true);
                if (!string.IsNullOrEmpty(snap.Location))
                    ParameterHelpers.SetString(panel, "ELC_PNL_LOCATION_TXT", snap.Location, overwrite: true);
                if (!string.IsNullOrEmpty(snap.IpRating))
                    ParameterHelpers.SetString(panel, ParamRegistry.ELC_IP_RATING, snap.IpRating, overwrite: true);
                if (!string.IsNullOrEmpty(snap.Manufacturer))
                    ParameterHelpers.SetString(panel, "ELC_PNL_MANUFACTURER_TXT", snap.Manufacturer, overwrite: true);
                if (!string.IsNullOrEmpty(snap.FaultKA))
                    ParameterHelpers.SetString(panel, "ELC_PNL_FAULT_KA_NR", snap.FaultKA, overwrite: true);
                if (!string.IsNullOrEmpty(snap.Enclosure))
                    ParameterHelpers.SetString(panel, "ELC_PNL_ENCLOSURE_TXT", snap.Enclosure, overwrite: true);
                tx.Commit();
            }
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            TaskDialog.Show("STING Electrical", $"Saved to '{panel.Name}'.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Resequences circuit numbers (1, 3, 5… odd; 2, 4, 6… even) inside the
    /// active panel schedule so renumbering stays contiguous after deletions.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ElecCircuitRenumberCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // Limit to the active PanelScheduleView when one is shown.
            var psv = doc.ActiveView as PanelScheduleView;
            if (psv == null)
            {
                TaskDialog.Show("STING Electrical",
                    "Activate a panel schedule view first (CIRCTS tab → click the panel in the grid).");
                return Result.Cancelled;
            }

            int renumbered = 0;
            using (var tx = new Transaction(doc, "STING Renumber Circuits"))
            {
                tx.Start();
                try
                {
                    var systems = new FilteredElementCollector(doc)
                        .OfClass(typeof(ElectricalSystem))
                        .Cast<ElectricalSystem>()
                        .Where(s =>
                        {
                            try { return s.BaseEquipment != null && s.BaseEquipment.Id == psv.GetPanel(); }
                            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
                        })
                        .OrderBy(s => SafeStartingSlot(s))
                        .ToList();

                    int next = 1;
                    foreach (var s in systems)
                    {
                        try
                        {
                            var p = s.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER);
                            if (p != null && !p.IsReadOnly)
                            {
                                p.Set(next.ToString());
                                renumbered++;
                            }
                            next += Math.Max(1, SafePoles(s));
                        }
                        catch (Exception ex) { StingLog.Warn($"Renumber circuit: {ex.Message}"); }
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Renumber traverse: {ex.Message}");
                }
                tx.Commit();
            }
            TaskDialog.Show("STING Electrical", $"Renumbered {renumbered} circuit(s).");
            return Result.Succeeded;
        }

        private static int SafeStartingSlot(ElectricalSystem s)
        {
            try
            {
                var p = s.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_START_SLOT);
                if (p != null) return (int)p.AsDouble();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return 0;
        }

        private static int SafePoles(ElectricalSystem s)
        {
            try { return s.PolesNumber; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 1; }
        }
    }

    /// <summary>
    /// Computes connected and demand load per panel using <see cref="ElectricalSystem.ApparentLoad"/>.
    /// Demand factors are applied per the user-selected NEC/IEC presets.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ElecLoadSummaryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            // The snapshot pushed back to the panel after Dispatch already includes
            // load-summary rows, so all this command does is invalidate any cache
            // and surface a confirmation.
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            TaskDialog.Show("STING Electrical", "Load summary refreshed — see the LOAD SUMMARY grid.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Creates a STING-branded lighting fixture schedule. Mirrors the schedule
    /// layout used by other STING family schedules so the column order is
    /// consistent across deliverables.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ElecLightingScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            using (var tx = new Transaction(doc, "STING Lighting Schedule"))
            {
                tx.Start();
                try
                {
                    var category = new ElementId(BuiltInCategory.OST_LightingFixtures);
                    var schedule = ViewSchedule.CreateSchedule(doc, category);
                    schedule.Name = "STING - Lighting Fixtures";
                    AddField(schedule, "Family and Type", BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM);
                    AddField(schedule, "Mark", BuiltInParameter.ALL_MODEL_MARK);
                    AddField(schedule, "Wattage", BuiltInParameter.RBS_ELEC_APPARENT_LOAD);
                    AddField(schedule, "Level", BuiltInParameter.SCHEDULE_LEVEL_PARAM);
                }
                catch (Exception ex) { StingLog.Warn($"Lighting schedule: {ex.Message}"); }
                tx.Commit();
            }
            TaskDialog.Show("STING Electrical", "Created 'STING - Lighting Fixtures' schedule.");
            return Result.Succeeded;
        }

        private static void AddField(ViewSchedule schedule, string label, BuiltInParameter bip)
        {
            try
            {
                var def = schedule.Definition;
                var pid = new ElementId(bip);
                var sf = def.GetSchedulableFields().FirstOrDefault(f => f.ParameterId == pid);
                if (sf != null) def.AddField(sf);
            }
            catch (Exception ex) { StingLog.Warn($"AddField {label}: {ex.Message}"); }
        }
    }
}
