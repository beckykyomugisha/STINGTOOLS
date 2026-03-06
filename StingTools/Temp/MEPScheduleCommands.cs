using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Temp
{
    // ════════════════════════════════════════════════════════════════════════════
    //  MEP SCHEDULING — Panel, Fixture, Device schedules
    //  Creates discipline-specific schedules for electrical panels, lighting
    //  fixtures, mechanical equipment, plumbing fixtures, and fire devices.
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates electrical panel schedules for all panel boards in the project.
    /// Each panel gets a standard schedule showing circuits, loads, and breaker sizes.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PanelScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            // Collect electrical panels
            var panels = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .Where(e => e is FamilyInstance)
                .ToList();

            if (panels.Count == 0)
            {
                TaskDialog.Show("Panel Schedules", "No electrical equipment found in the project.");
                return Result.Succeeded;
            }

            // Check for existing panel schedules
            var existingSchedules = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(PanelScheduleView))
                    .Cast<PanelScheduleView>()
                    .Select(ps => ps.Name),
                StringComparer.OrdinalIgnoreCase);

            int created = 0, skipped = 0;

            using (Transaction tx = new Transaction(doc, "STING Create Panel Schedules"))
            {
                tx.Start();

                // Get panel schedule template IDs
                var templateIds = PanelScheduleTemplate.GetAllTemplates(doc);
                ElementId templateId = templateIds != null && templateIds.Count > 0
                    ? templateIds[0] : ElementId.InvalidElementId;

                foreach (var panel in panels)
                {
                    string panelName = panel.Name;
                    if (string.IsNullOrEmpty(panelName))
                        panelName = ParameterHelpers.GetString(panel, "Panel Name");
                    if (string.IsNullOrEmpty(panelName))
                        panelName = panel.Id.ToString();

                    // Check if schedule already exists for this panel
                    PanelScheduleView existingPanelSchedule = null;
                    try
                    {
                        existingPanelSchedule = new FilteredElementCollector(doc)
                            .OfClass(typeof(PanelScheduleView))
                            .Cast<PanelScheduleView>()
                            .FirstOrDefault(ps => ps.GetPanel() == panel.Id);
                    }
                    catch { }

                    if (existingPanelSchedule != null)
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        PanelScheduleView psv = null;
                        if (templateId != ElementId.InvalidElementId)
                            psv = PanelScheduleView.CreateInstanceView(doc, templateId, panel.Id);

                        if (psv == null)
                        {
                            // Try without template — use first available template type
                            var allTemplateIds = PanelScheduleTemplate.GetAllTemplates(doc);
                            foreach (var tid in allTemplateIds)
                            {
                                try
                                {
                                    psv = PanelScheduleView.CreateInstanceView(doc, tid, panel.Id);
                                    if (psv != null) break;
                                }
                                catch { }
                            }
                        }

                        if (psv != null)
                        {
                            created++;
                            StingLog.Info($"Panel schedule created for: {panelName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Panel schedule failed for {panelName}: {ex.Message}");
                        skipped++;
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Panel Schedules",
                $"Panel Schedule Creation Complete\n\n" +
                $"  Panels found: {panels.Count}\n" +
                $"  Schedules created: {created}\n" +
                $"  Skipped (existing): {skipped}");

            StingLog.Info($"Panel schedules: {created} created, {skipped} skipped of {panels.Count} panels");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Creates a fixture schedule for lighting fixtures with lamp data,
    /// wattage, circuit, and STING tag information.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LightingFixtureScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            string scheduleName = "STING - Lighting Fixture Schedule";

            // Check existing
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(s => s.Name == scheduleName);

            if (existing != null)
            {
                var td = new TaskDialog("Lighting Schedule");
                td.MainInstruction = $"'{scheduleName}' already exists.";
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Recreate", "Delete and recreate");
                td.CommonButtons = TaskDialogCommonButtons.Cancel;
                if (td.Show() != TaskDialogResult.CommandLink1)
                    return Result.Cancelled;
            }

            using (Transaction tx = new Transaction(doc, "STING Lighting Fixture Schedule"))
            {
                tx.Start();

                if (existing != null)
                {
                    try { doc.Delete(existing.Id); } catch { }
                }

                var schedule = ViewSchedule.CreateSchedule(doc,
                    new ElementId(BuiltInCategory.OST_LightingFixtures));
                schedule.Name = scheduleName;

                // Add standard fields
                MEPScheduleHelper.AddScheduleField(doc, schedule, "Family", BuiltInCategory.OST_LightingFixtures);
                MEPScheduleHelper.AddScheduleField(doc, schedule, "Type", BuiltInCategory.OST_LightingFixtures);
                MEPScheduleHelper.AddScheduleField(doc, schedule, "Level", BuiltInCategory.OST_LightingFixtures);
                MEPScheduleHelper.AddBuiltInField(schedule, BuiltInParameter.RBS_ELEC_APPARENT_LOAD);
                MEPScheduleHelper.AddBuiltInField(schedule, BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER);
                MEPScheduleHelper.AddBuiltInField(schedule, BuiltInParameter.RBS_ELEC_CIRCUIT_PANEL_PARAM);
                MEPScheduleHelper.AddSharedParamField(doc, schedule, ParamRegistry.TAG1);
                MEPScheduleHelper.AddSharedParamField(doc, schedule, ParamRegistry.DISC);
                MEPScheduleHelper.AddSharedParamField(doc, schedule, ParamRegistry.SYS);
                MEPScheduleHelper.AddBuiltInField(schedule, BuiltInParameter.ALL_MODEL_MARK);
                MEPScheduleHelper.AddBuiltInField(schedule, BuiltInParameter.ELEM_ROOM_NAME);

                // Count field
                MEPScheduleHelper.AddCountField(schedule);

                // Sort by Level, then Family
                MEPScheduleHelper.ApplySorting(schedule, "Level", "Family");

                tx.Commit();
            }

            var count = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_LightingFixtures)
                .WhereElementIsNotElementType()
                .GetElementCount();

            TaskDialog.Show("Lighting Schedule",
                $"Schedule created: {scheduleName}\n\n" +
                $"  Lighting fixtures in project: {count}\n" +
                "  Fields: Family, Type, Level, Load, Circuit, Panel, Tag, Mark, Room");

            StingLog.Info($"Lighting fixture schedule created: {count} fixtures");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Creates a mechanical equipment schedule with capacity, airflow,
    /// system type, and STING tag fields.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MechanicalEquipmentScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            string scheduleName = "STING - Mechanical Equipment Schedule";

            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(s => s.Name == scheduleName);

            if (existing != null)
            {
                var td = new TaskDialog("Mechanical Schedule");
                td.MainInstruction = $"'{scheduleName}' already exists.";
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Recreate", "Delete and recreate");
                td.CommonButtons = TaskDialogCommonButtons.Cancel;
                if (td.Show() != TaskDialogResult.CommandLink1)
                    return Result.Cancelled;
            }

            using (Transaction tx = new Transaction(doc, "STING Mechanical Equipment Schedule"))
            {
                tx.Start();

                if (existing != null)
                {
                    try { doc.Delete(existing.Id); } catch { }
                }

                var schedule = ViewSchedule.CreateSchedule(doc,
                    new ElementId(BuiltInCategory.OST_MechanicalEquipment));
                schedule.Name = scheduleName;

                MEPScheduleHelper.AddScheduleField(doc, schedule, "Family", BuiltInCategory.OST_MechanicalEquipment);
                MEPScheduleHelper.AddScheduleField(doc, schedule, "Type", BuiltInCategory.OST_MechanicalEquipment);
                MEPScheduleHelper.AddScheduleField(doc, schedule, "Level", BuiltInCategory.OST_MechanicalEquipment);
                MEPScheduleHelper.AddBuiltInField(schedule, BuiltInParameter.ALL_MODEL_MARK);
                MEPScheduleHelper.AddSharedParamField(doc, schedule, ParamRegistry.TAG1);
                MEPScheduleHelper.AddSharedParamField(doc, schedule, ParamRegistry.DISC);
                MEPScheduleHelper.AddSharedParamField(doc, schedule, ParamRegistry.SYS);
                MEPScheduleHelper.AddSharedParamField(doc, schedule, ParamRegistry.FUNC);
                MEPScheduleHelper.AddSharedParamField(doc, schedule, ParamRegistry.PROD);
                MEPScheduleHelper.AddBuiltInField(schedule, BuiltInParameter.ELEM_ROOM_NAME);

                MEPScheduleHelper.AddCountField(schedule);
                MEPScheduleHelper.ApplySorting(schedule, "Level", "Family");

                tx.Commit();
            }

            var count = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                .WhereElementIsNotElementType()
                .GetElementCount();

            TaskDialog.Show("Mechanical Schedule",
                $"Schedule created: {scheduleName}\n\n" +
                $"  Mechanical equipment in project: {count}\n" +
                "  Fields: Family, Type, Level, Mark, Tag, Disc, Sys, Func, Prod, Room");

            StingLog.Info($"Mechanical equipment schedule created: {count} items");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Creates a plumbing fixture schedule with fixture unit counts,
    /// connection types, and STING tag fields.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbingFixtureScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            string scheduleName = "STING - Plumbing Fixture Schedule";

            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(s => s.Name == scheduleName);

            if (existing != null)
            {
                var td = new TaskDialog("Plumbing Schedule");
                td.MainInstruction = $"'{scheduleName}' already exists.";
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Recreate", "Delete and recreate");
                td.CommonButtons = TaskDialogCommonButtons.Cancel;
                if (td.Show() != TaskDialogResult.CommandLink1)
                    return Result.Cancelled;
            }

            using (Transaction tx = new Transaction(doc, "STING Plumbing Fixture Schedule"))
            {
                tx.Start();

                if (existing != null)
                {
                    try { doc.Delete(existing.Id); } catch { }
                }

                var schedule = ViewSchedule.CreateSchedule(doc,
                    new ElementId(BuiltInCategory.OST_PlumbingFixtures));
                schedule.Name = scheduleName;

                MEPScheduleHelper.AddScheduleField(doc, schedule, "Family", BuiltInCategory.OST_PlumbingFixtures);
                MEPScheduleHelper.AddScheduleField(doc, schedule, "Type", BuiltInCategory.OST_PlumbingFixtures);
                MEPScheduleHelper.AddScheduleField(doc, schedule, "Level", BuiltInCategory.OST_PlumbingFixtures);
                MEPScheduleHelper.AddBuiltInField(schedule, BuiltInParameter.ALL_MODEL_MARK);
                MEPScheduleHelper.AddSharedParamField(doc, schedule, ParamRegistry.TAG1);
                MEPScheduleHelper.AddSharedParamField(doc, schedule, ParamRegistry.DISC);
                MEPScheduleHelper.AddSharedParamField(doc, schedule, ParamRegistry.SYS);
                MEPScheduleHelper.AddSharedParamField(doc, schedule, ParamRegistry.PROD);
                MEPScheduleHelper.AddBuiltInField(schedule, BuiltInParameter.ELEM_ROOM_NAME);

                MEPScheduleHelper.AddCountField(schedule);
                MEPScheduleHelper.ApplySorting(schedule, "Level", "Family");

                tx.Commit();
            }

            var count = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                .WhereElementIsNotElementType()
                .GetElementCount();

            TaskDialog.Show("Plumbing Schedule",
                $"Schedule created: {scheduleName}\n\n" +
                $"  Plumbing fixtures in project: {count}\n" +
                "  Fields: Family, Type, Level, Mark, Tag, Disc, Sys, Prod, Room");

            StingLog.Info($"Plumbing fixture schedule created: {count} items");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Creates a fire safety device schedule for sprinklers, detectors,
    /// and alarm devices with coverage area and STING tag information.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FireDeviceScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            string scheduleName = "STING - Fire Safety Device Schedule";

            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(s => s.Name == scheduleName);

            if (existing != null)
            {
                var td = new TaskDialog("Fire Device Schedule");
                td.MainInstruction = $"'{scheduleName}' already exists.";
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Recreate", "Delete and recreate");
                td.CommonButtons = TaskDialogCommonButtons.Cancel;
                if (td.Show() != TaskDialogResult.CommandLink1)
                    return Result.Cancelled;
            }

            using (Transaction tx = new Transaction(doc, "STING Fire Device Schedule"))
            {
                tx.Start();

                if (existing != null)
                {
                    try { doc.Delete(existing.Id); } catch { }
                }

                var schedule = ViewSchedule.CreateSchedule(doc,
                    new ElementId(BuiltInCategory.OST_Sprinklers));
                schedule.Name = scheduleName;

                MEPScheduleHelper.AddScheduleField(doc, schedule, "Family", BuiltInCategory.OST_Sprinklers);
                MEPScheduleHelper.AddScheduleField(doc, schedule, "Type", BuiltInCategory.OST_Sprinklers);
                MEPScheduleHelper.AddScheduleField(doc, schedule, "Level", BuiltInCategory.OST_Sprinklers);
                MEPScheduleHelper.AddBuiltInField(schedule, BuiltInParameter.ALL_MODEL_MARK);
                MEPScheduleHelper.AddSharedParamField(doc, schedule, ParamRegistry.TAG1);
                MEPScheduleHelper.AddSharedParamField(doc, schedule, ParamRegistry.DISC);
                MEPScheduleHelper.AddSharedParamField(doc, schedule, ParamRegistry.SYS);
                MEPScheduleHelper.AddBuiltInField(schedule, BuiltInParameter.ELEM_ROOM_NAME);

                MEPScheduleHelper.AddCountField(schedule);
                MEPScheduleHelper.ApplySorting(schedule, "Level", "Family");

                tx.Commit();
            }

            var count = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Sprinklers)
                .WhereElementIsNotElementType()
                .GetElementCount();

            TaskDialog.Show("Fire Device Schedule",
                $"Schedule created: {scheduleName}\n\n" +
                $"  Fire devices (sprinklers) in project: {count}\n" +
                "  Fields: Family, Type, Level, Mark, Tag, Disc, Sys, Room");

            StingLog.Info($"Fire device schedule created: {count} devices");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Creates an electrical device schedule for power outlets, switches,
    /// and data/telecom outlets with circuit and STING tag information.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ElectricalDeviceScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            string scheduleName = "STING - Electrical Device Schedule";

            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(s => s.Name == scheduleName);

            if (existing != null)
            {
                var td = new TaskDialog("Electrical Schedule");
                td.MainInstruction = $"'{scheduleName}' already exists.";
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Recreate", "Delete and recreate");
                td.CommonButtons = TaskDialogCommonButtons.Cancel;
                if (td.Show() != TaskDialogResult.CommandLink1)
                    return Result.Cancelled;
            }

            using (Transaction tx = new Transaction(doc, "STING Electrical Device Schedule"))
            {
                tx.Start();

                if (existing != null)
                {
                    try { doc.Delete(existing.Id); } catch { }
                }

                var schedule = ViewSchedule.CreateSchedule(doc,
                    new ElementId(BuiltInCategory.OST_ElectricalFixtures));
                schedule.Name = scheduleName;

                MEPScheduleHelper.AddScheduleField(doc, schedule, "Family", BuiltInCategory.OST_ElectricalFixtures);
                MEPScheduleHelper.AddScheduleField(doc, schedule, "Type", BuiltInCategory.OST_ElectricalFixtures);
                MEPScheduleHelper.AddScheduleField(doc, schedule, "Level", BuiltInCategory.OST_ElectricalFixtures);
                MEPScheduleHelper.AddBuiltInField(schedule, BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER);
                MEPScheduleHelper.AddBuiltInField(schedule, BuiltInParameter.RBS_ELEC_CIRCUIT_PANEL_PARAM);
                MEPScheduleHelper.AddBuiltInField(schedule, BuiltInParameter.ALL_MODEL_MARK);
                MEPScheduleHelper.AddSharedParamField(doc, schedule, ParamRegistry.TAG1);
                MEPScheduleHelper.AddSharedParamField(doc, schedule, ParamRegistry.DISC);
                MEPScheduleHelper.AddSharedParamField(doc, schedule, ParamRegistry.SYS);
                MEPScheduleHelper.AddBuiltInField(schedule, BuiltInParameter.ELEM_ROOM_NAME);

                MEPScheduleHelper.AddCountField(schedule);
                MEPScheduleHelper.ApplySorting(schedule, "Level", "Family");

                tx.Commit();
            }

            var count = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalFixtures)
                .WhereElementIsNotElementType()
                .GetElementCount();

            TaskDialog.Show("Electrical Device Schedule",
                $"Schedule created: {scheduleName}\n\n" +
                $"  Electrical devices in project: {count}\n" +
                "  Fields: Family, Type, Level, Circuit, Panel, Mark, Tag, Disc, Sys, Room");

            StingLog.Info($"Electrical device schedule created: {count} devices");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Batch creates all MEP schedules (Panel, Lighting, Mechanical,
    /// Plumbing, Fire, Electrical) in a single operation.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchMEPSchedulesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var results = new StringBuilder();
            results.AppendLine("Batch MEP Schedule Creation");
            results.AppendLine(new string('═', 40));

            int total = 0;
            var commands = new (string name, IExternalCommand cmd)[]
            {
                ("Panel Schedules", new PanelScheduleCommand()),
                ("Lighting Fixtures", new LightingFixtureScheduleCommand()),
                ("Mechanical Equipment", new MechanicalEquipmentScheduleCommand()),
                ("Plumbing Fixtures", new PlumbingFixtureScheduleCommand()),
                ("Fire Devices", new FireDeviceScheduleCommand()),
                ("Electrical Devices", new ElectricalDeviceScheduleCommand()),
            };

            foreach (var (name, cmd) in commands)
            {
                try
                {
                    string msg = "";
                    ElementSet es = new ElementSet();
                    var result = cmd.Execute(commandData, ref msg, es);
                    string status = result == Result.Succeeded ? "OK" : result == Result.Cancelled ? "Skipped" : "Failed";
                    results.AppendLine($"  {name}: {status}");
                    if (result == Result.Succeeded) total++;
                }
                catch (Exception ex)
                {
                    results.AppendLine($"  {name}: Error — {ex.Message}");
                    StingLog.Error($"Batch MEP schedule '{name}' failed", ex);
                }
            }

            results.AppendLine($"\nTotal schedules created: {total}");

            TaskDialog.Show("Batch MEP Schedules", results.ToString());
            StingLog.Info($"Batch MEP schedules: {total} created");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  MEP Schedule Helper — shared utilities for schedule field management
    // ════════════════════════════════════════════════════════════════════════════

    internal static class MEPScheduleHelper
    {
        /// <summary>Add a schedulable field by name (Family, Type, Level, etc.).</summary>
        public static void AddScheduleField(Document doc, ViewSchedule schedule,
            string fieldName, BuiltInCategory category)
        {
            try
            {
                var defs = schedule.Definition;
                var schedulableFields = defs.GetSchedulableFields();

                foreach (var sf in schedulableFields)
                {
                    string name = sf.GetName(doc);
                    if (name.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                    {
                        defs.AddField(sf);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"AddScheduleField '{fieldName}': {ex.Message}");
            }
        }

        /// <summary>Add a built-in parameter field to the schedule.</summary>
        public static void AddBuiltInField(ViewSchedule schedule, BuiltInParameter bip)
        {
            try
            {
                var defs = schedule.Definition;
                var schedulableFields = defs.GetSchedulableFields();

                foreach (var sf in schedulableFields)
                {
                    if (sf.ParameterId == new ElementId(bip))
                    {
                        defs.AddField(sf);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"AddBuiltInField '{bip}': {ex.Message}");
            }
        }

        /// <summary>Add a STING shared parameter field to the schedule.</summary>
        public static void AddSharedParamField(Document doc, ViewSchedule schedule, string paramName)
        {
            if (string.IsNullOrEmpty(paramName)) return;

            try
            {
                var defs = schedule.Definition;
                var schedulableFields = defs.GetSchedulableFields();

                foreach (var sf in schedulableFields)
                {
                    string name = sf.GetName(doc);
                    if (name.Equals(paramName, StringComparison.OrdinalIgnoreCase))
                    {
                        defs.AddField(sf);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"AddSharedParamField '{paramName}': {ex.Message}");
            }
        }

        /// <summary>Add a Count field to the schedule definition.</summary>
        public static void AddCountField(ViewSchedule schedule)
        {
            try
            {
                var defs = schedule.Definition;
                var schedulableFields = defs.GetSchedulableFields();

                foreach (var sf in schedulableFields)
                {
                    if (sf.FieldType == ScheduleFieldType.Count)
                    {
                        defs.AddField(sf);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"AddCountField: {ex.Message}");
            }
        }

        /// <summary>Apply sorting to the schedule by up to 2 field names.</summary>
        public static void ApplySorting(ViewSchedule schedule, string field1, string field2 = null)
        {
            try
            {
                var defs = schedule.Definition;
                int fieldCount = defs.GetFieldCount();

                for (int i = 0; i < fieldCount; i++)
                {
                    var field = defs.GetField(i);
                    string name = field.GetName();

                    if (name.Equals(field1, StringComparison.OrdinalIgnoreCase))
                    {
                        var sortGroup = new ScheduleSortGroupField(field.FieldId);
                        sortGroup.SortOrder = ScheduleSortOrder.Ascending;
                        sortGroup.ShowHeader = true;
                        defs.AddSortGroupField(sortGroup);
                    }
                    else if (field2 != null && name.Equals(field2, StringComparison.OrdinalIgnoreCase))
                    {
                        var sortGroup = new ScheduleSortGroupField(field.FieldId);
                        sortGroup.SortOrder = ScheduleSortOrder.Ascending;
                        defs.AddSortGroupField(sortGroup);
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ApplySorting: {ex.Message}");
            }
        }
    }
}
