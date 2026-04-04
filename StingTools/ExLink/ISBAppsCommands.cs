using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.ExLink
{
    // ════════════════════════════════════════════════════════════════════════
    //  ISB APPS COMMANDS — In-Sheet Builder schedule creation commands
    //
    //  Each command creates a Revit ViewSchedule using the native API
    //  (ViewSchedule.CreateSchedule + ScheduleField addition).
    //
    //  10 IExternalCommand classes:
    //    1. ISBDoorScheduleCommand
    //    2. ISBWindowScheduleCommand
    //    3. ISBRoomFinishCommand
    //    4. ISBWallTypeCommand
    //    5. ISBFloorTypeCommand
    //    6. ISBEquipmentScheduleCommand
    //    7. ISBLightingScheduleCommand
    //    8. ISBPlumbingScheduleCommand
    //    9. ISBElectricalScheduleCommand
    //   10. ISBKeyPlanCommand
    // ════════════════════════════════════════════════════════════════════════

    #region -- ISB Engine --

    internal static class ISBEngine
    {
        /// <summary>
        /// Create a ViewSchedule from a category and a list of schedulable field names.
        /// Returns the created schedule, or null on failure.
        /// </summary>
        internal static ViewSchedule CreateScheduleFromTemplate(
            Document doc,
            string scheduleName,
            BuiltInCategory category,
            string[] fieldNames)
        {
            try
            {
                var schedule = ViewSchedule.CreateSchedule(doc, new ElementId(category));
                if (schedule == null)
                {
                    StingLog.Warn($"ISBEngine: ViewSchedule.CreateSchedule returned null for {category}");
                    return null;
                }

                // Set name with collision fallback
                try { schedule.Name = scheduleName; }
                catch (Exception)
                {
                    try { schedule.Name = $"{scheduleName}_{DateTime.Now:HHmmss}"; }
                    catch (Exception ex) { StingLog.Warn($"ISBEngine rename fallback: {ex.Message}"); }
                }

                // Build lookup of available schedulable fields
                var definition = schedule.Definition;
                var availableFields = definition.GetSchedulableFields();
                var fieldLookup = new Dictionary<string, SchedulableField>(StringComparer.OrdinalIgnoreCase);
                foreach (var sf in availableFields)
                {
                    string name = sf.GetName(doc);
                    if (!string.IsNullOrEmpty(name) && !fieldLookup.ContainsKey(name))
                        fieldLookup[name] = sf;
                }

                int addedCount = 0;
                foreach (string fieldName in fieldNames)
                {
                    if (fieldLookup.TryGetValue(fieldName, out var schedulableField))
                    {
                        try
                        {
                            definition.AddField(schedulableField);
                            addedCount++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"ISBEngine: Could not add field '{fieldName}': {ex.Message}");
                        }
                    }
                    else
                    {
                        StingLog.Info($"ISBEngine: Field '{fieldName}' not available for {category}");
                    }
                }

                StingLog.Info($"ISBEngine: Created '{scheduleName}' with {addedCount}/{fieldNames.Length} fields");
                return schedule;
            }
            catch (Exception ex)
            {
                StingLog.Error($"ISBEngine.CreateScheduleFromTemplate failed for '{scheduleName}'", ex);
                return null;
            }
        }

        /// <summary>
        /// Wrapper that runs a schedule-creation command inside a STING ISB transaction.
        /// Returns Result.Succeeded or Result.Failed.
        /// </summary>
        internal static Result ExecuteScheduleCommand(
            ExternalCommandData commandData,
            ref string message,
            string scheduleName,
            BuiltInCategory category,
            string[] fieldNames,
            string commandLabel)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No document open."; return Result.Failed; }
                var doc = ctx.Doc;

                // Count existing elements for reporting
                int instanceCount = new FilteredElementCollector(doc)
                    .OfCategory(category)
                    .WhereElementIsNotElementType()
                    .GetElementCount();

                ViewSchedule schedule;
                using (var tx = new Transaction(doc, $"STING ISB {commandLabel}"))
                {
                    tx.Start();
                    schedule = CreateScheduleFromTemplate(doc, scheduleName, category, fieldNames);
                    if (schedule == null)
                    {
                        tx.RollBack();
                        message = $"Failed to create schedule for {commandLabel}.";
                        return Result.Failed;
                    }
                    tx.Commit();
                }

                TaskDialog.Show("STING ISB",
                    $"{commandLabel} created successfully.\n\n" +
                    $"Schedule: {schedule.Name}\n" +
                    $"Elements in model: {instanceCount}");

                StingLog.Info($"ISB {commandLabel}: created schedule '{schedule.Name}', {instanceCount} elements");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error($"ISB {commandLabel} failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    //  1. ISBDoorScheduleCommand
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ISBDoorScheduleCommand : IExternalCommand
    {
        private static readonly string[] Fields = new[]
        {
            "Mark", "Level", "From Room: Name", "To Room: Name",
            "Width", "Height", "Type", "Family", "Fire Rating",
            "Comments"
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return ISBEngine.ExecuteScheduleCommand(
                commandData, ref message,
                "STING ISB - Door Schedule",
                BuiltInCategory.OST_Doors,
                Fields,
                "Door Schedule");
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  2. ISBWindowScheduleCommand
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ISBWindowScheduleCommand : IExternalCommand
    {
        private static readonly string[] Fields = new[]
        {
            "Mark", "Level", "Width", "Height",
            "Sill Height", "Type", "Family",
            "Comments"
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return ISBEngine.ExecuteScheduleCommand(
                commandData, ref message,
                "STING ISB - Window Schedule",
                BuiltInCategory.OST_Windows,
                Fields,
                "Window Schedule");
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  3. ISBRoomFinishCommand
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ISBRoomFinishCommand : IExternalCommand
    {
        private static readonly string[] Fields = new[]
        {
            "Number", "Name", "Level", "Department",
            "Area", "Floor Finish", "Wall Finish",
            "Ceiling Finish", "Base Finish", "Comments"
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return ISBEngine.ExecuteScheduleCommand(
                commandData, ref message,
                "STING ISB - Room Finish Schedule",
                BuiltInCategory.OST_Rooms,
                Fields,
                "Room Finish Schedule");
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  4. ISBWallTypeCommand
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ISBWallTypeCommand : IExternalCommand
    {
        private static readonly string[] Fields = new[]
        {
            "Family and Type", "Width", "Function",
            "Fire Rating", "Structural Material",
            "Assembly Description", "Cost", "Comments"
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return ISBEngine.ExecuteScheduleCommand(
                commandData, ref message,
                "STING ISB - Wall Type Schedule",
                BuiltInCategory.OST_Walls,
                Fields,
                "Wall Type Schedule");
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  5. ISBFloorTypeCommand
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ISBFloorTypeCommand : IExternalCommand
    {
        private static readonly string[] Fields = new[]
        {
            "Family and Type", "Level", "Area",
            "Thickness", "Structural Material",
            "Fire Rating", "Comments"
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return ISBEngine.ExecuteScheduleCommand(
                commandData, ref message,
                "STING ISB - Floor Type Schedule",
                BuiltInCategory.OST_Floors,
                Fields,
                "Floor Type Schedule");
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  6. ISBEquipmentScheduleCommand
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ISBEquipmentScheduleCommand : IExternalCommand
    {
        private static readonly string[] Fields = new[]
        {
            "Mark", "Level", "Family", "Type",
            "Manufacturer", "Model", "Description",
            "Voltage", "Apparent Power", "Comments"
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return ISBEngine.ExecuteScheduleCommand(
                commandData, ref message,
                "STING ISB - Equipment Schedule",
                BuiltInCategory.OST_MechanicalEquipment,
                Fields,
                "Equipment Schedule");
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  7. ISBLightingScheduleCommand
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ISBLightingScheduleCommand : IExternalCommand
    {
        private static readonly string[] Fields = new[]
        {
            "Mark", "Level", "Family", "Type",
            "Apparent Power", "Lamp Type",
            "Number of Poles", "Comments"
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return ISBEngine.ExecuteScheduleCommand(
                commandData, ref message,
                "STING ISB - Lighting Schedule",
                BuiltInCategory.OST_LightingFixtures,
                Fields,
                "Lighting Schedule");
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  8. ISBPlumbingScheduleCommand
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ISBPlumbingScheduleCommand : IExternalCommand
    {
        private static readonly string[] Fields = new[]
        {
            "Mark", "Level", "Family", "Type",
            "System Type", "System Name",
            "Flow", "Size", "Comments"
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return ISBEngine.ExecuteScheduleCommand(
                commandData, ref message,
                "STING ISB - Plumbing Schedule",
                BuiltInCategory.OST_PlumbingFixtures,
                Fields,
                "Plumbing Schedule");
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  9. ISBElectricalScheduleCommand
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ISBElectricalScheduleCommand : IExternalCommand
    {
        private static readonly string[] Fields = new[]
        {
            "Mark", "Level", "Family", "Type",
            "Voltage", "Apparent Power", "Number of Poles",
            "Panel", "Circuit Number", "Comments"
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return ISBEngine.ExecuteScheduleCommand(
                commandData, ref message,
                "STING ISB - Electrical Schedule",
                BuiltInCategory.OST_ElectricalFixtures,
                Fields,
                "Electrical Schedule");
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  10. ISBKeyPlanCommand — creates a key plan drafting view
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ISBKeyPlanCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No document open."; return Result.Failed; }
                var doc = ctx.Doc;

                // Gather level names for the key plan annotation
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .Select(l => l.Name)
                    .ToList();

                // Find a drafting view family type
                var draftViewTypeId = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Drafting)?.Id;

                if (draftViewTypeId == null)
                {
                    message = "No drafting view family type found.";
                    return Result.Failed;
                }

                using (var tx = new Transaction(doc, "STING ISB Key Plan"))
                {
                    tx.Start();

                    var view = ViewDrafting.Create(doc, draftViewTypeId);
                    if (view == null)
                    {
                        tx.RollBack();
                        message = "Failed to create drafting view.";
                        return Result.Failed;
                    }

                    try { view.Name = "STING ISB - Key Plan"; }
                    catch (Exception)
                    {
                        try { view.Name = $"STING ISB - Key Plan_{DateTime.Now:HHmmss}"; }
                        catch (Exception ex) { StingLog.Warn($"ISBKeyPlan rename: {ex.Message}"); }
                    }

                    // Place level list as text notes
                    var textTypeId = new FilteredElementCollector(doc)
                        .OfClass(typeof(TextNoteType))
                        .FirstElementId();

                    if (textTypeId != null && textTypeId != ElementId.InvalidElementId)
                    {
                        double y = 0;
                        double lineSpacing = -0.02;

                        TextNote.Create(doc, view.Id, new XYZ(0, y, 0), "KEY PLAN", textTypeId);
                        y += lineSpacing;
                        TextNote.Create(doc, view.Id, new XYZ(0, y, 0), new string('=', 30), textTypeId);
                        y += lineSpacing;

                        foreach (string levelName in levels)
                        {
                            TextNote.Create(doc, view.Id, new XYZ(0, y, 0), $"  {levelName}", textTypeId);
                            y += lineSpacing;
                        }
                    }

                    tx.Commit();
                    TaskDialog.Show("STING ISB",
                        $"Key Plan created.\nView: {view.Name}\nLevels: {levels.Count}");
                }

                StingLog.Info($"ISBKeyPlan: created key plan with {levels.Count} levels");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ISBKeyPlanCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
