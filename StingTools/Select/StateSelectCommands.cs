using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Select
{
    /// <summary>
    /// State-based selection commands from STINGTags v9.6 SELECT tab:
    /// Untagged, Tagged, EmptyMark, Pinned, Unpinned.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class SelectUntaggedCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            var ids = new FilteredElementCollector(doc, doc.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(e =>
                {
                    string cat = ParameterHelpers.GetCategoryName(e);
                    if (!known.Contains(cat)) return false;
                    string tag = ParameterHelpers.GetString(e, "ASS_TAG_1_TXT");
                    return string.IsNullOrEmpty(tag);
                })
                .Select(e => e.Id).ToList();

            uidoc.Selection.SetElementIds(ids);
            TaskDialog.Show("Select Untagged", $"Selected {ids.Count} untagged elements.");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class SelectTaggedCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            var ids = new FilteredElementCollector(doc, doc.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(e =>
                {
                    string cat = ParameterHelpers.GetCategoryName(e);
                    if (!known.Contains(cat)) return false;
                    string tag = ParameterHelpers.GetString(e, "ASS_TAG_1_TXT");
                    return TagConfig.TagIsComplete(tag);
                })
                .Select(e => e.Id).ToList();

            uidoc.Selection.SetElementIds(ids);
            TaskDialog.Show("Select Tagged", $"Selected {ids.Count} tagged elements.");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class SelectEmptyMarkCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var ids = new FilteredElementCollector(doc, doc.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(e =>
                {
                    Parameter p = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                    if (p == null) return false;
                    string val = p.AsString();
                    return string.IsNullOrEmpty(val);
                })
                .Select(e => e.Id).ToList();

            uidoc.Selection.SetElementIds(ids);
            TaskDialog.Show("Select Empty Mark", $"Selected {ids.Count} elements with empty Mark.");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class SelectPinnedCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var ids = new FilteredElementCollector(doc, doc.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Pinned)
                .Select(e => e.Id).ToList();

            uidoc.Selection.SetElementIds(ids);
            TaskDialog.Show("Select Pinned", $"Selected {ids.Count} pinned elements.");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class SelectUnpinnedCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            var ids = new FilteredElementCollector(doc, doc.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(e => !e.Pinned && known.Contains(ParameterHelpers.GetCategoryName(e)))
                .Select(e => e.Id).ToList();

            uidoc.Selection.SetElementIds(ids);
            TaskDialog.Show("Select Unpinned", $"Selected {ids.Count} unpinned elements.");
            return Result.Succeeded;
        }
    }

    /// <summary>Select elements by level in active view.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class SelectByLevelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Use the active view's associated level if it's a plan view
            View view = doc.ActiveView;
            ElementId levelId = null;
            if (view is ViewPlan vp)
                levelId = vp.GenLevel?.Id;

            if (levelId == null || levelId == ElementId.InvalidElementId)
            {
                TaskDialog.Show("Select by Level", "Active view has no associated level.");
                return Result.Succeeded;
            }

            var ids = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.LevelId == levelId)
                .Select(e => e.Id).ToList();

            uidoc.Selection.SetElementIds(ids);
            TaskDialog.Show("Select by Level", $"Selected {ids.Count} elements on this level.");
            return Result.Succeeded;
        }
    }

    /// <summary>Select elements in the same room as the first selected element.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class SelectByRoomCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 0)
            {
                TaskDialog.Show("Select by Room", "Select an element first, then run this command.");
                return Result.Succeeded;
            }

            Element seed = doc.GetElement(selected.First());
            if (seed == null) return Result.Succeeded;

            // Get the room of the seed element
            var fi = seed as FamilyInstance;
            if (fi == null)
            {
                TaskDialog.Show("Select by Room", "Selected element is not a family instance.");
                return Result.Succeeded;
            }

            Room room = fi.Room;
            if (room == null)
            {
                TaskDialog.Show("Select by Room", "Selected element is not in a room.");
                return Result.Succeeded;
            }

            // Find all family instances in the same room
            var ids = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(f => f.Room?.Id == room.Id)
                .Select(f => f.Id).ToList();

            uidoc.Selection.SetElementIds(ids);
            TaskDialog.Show("Select by Room",
                $"Selected {ids.Count} elements in room '{room.Name}'.");
            return Result.Succeeded;
        }
    }

    /// <summary>Bulk write a parameter value to all selected elements.</summary>
    [Transaction(TransactionMode.Manual)]
    public class BulkParamWriteCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 0)
            {
                TaskDialog.Show("Bulk Param Write", "No elements selected.");
                return Result.Succeeded;
            }

            // Prompt for parameter name and value
            // Use TaskDialog with input (Revit doesn't have input dialogs, use common params)
            TaskDialog td = new TaskDialog("Bulk Param Write");
            td.MainInstruction = $"Write parameter to {selected.Count} selected elements";
            td.MainContent =
                "This will write a value to a named parameter on all selected elements.\n\n" +
                "Common parameters:\n" +
                "  ASS_LOC_TXT, ASS_ZONE_TXT, ASS_STATUS_TXT,\n" +
                "  ASS_DISCIPLINE_COD_TXT, ASS_SYSTEM_TYPE_TXT";
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Set LOC to BLD1");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Set ZONE to Z01");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Set STATUS to EXISTING");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Clear all tags");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            var result = td.Show();
            string paramName = null;
            string paramValue = null;
            bool overwrite = false;

            switch (result)
            {
                case TaskDialogResult.CommandLink1:
                    paramName = "ASS_LOC_TXT"; paramValue = "BLD1"; break;
                case TaskDialogResult.CommandLink2:
                    paramName = "ASS_ZONE_TXT"; paramValue = "Z01"; break;
                case TaskDialogResult.CommandLink3:
                    paramName = "ASS_STATUS_TXT"; paramValue = "EXISTING"; break;
                case TaskDialogResult.CommandLink4:
                    paramName = "ASS_TAG_1_TXT"; paramValue = ""; overwrite = true; break;
                default:
                    return Result.Cancelled;
            }

            int written = 0;
            using (Transaction tx = new Transaction(doc, "Bulk Parameter Write"))
            {
                tx.Start();
                foreach (ElementId id in selected)
                {
                    Element elem = doc.GetElement(id);
                    if (elem == null) continue;
                    if (overwrite)
                    {
                        if (ParameterHelpers.SetString(elem, paramName, paramValue, overwrite: true))
                            written++;
                    }
                    else
                    {
                        if (ParameterHelpers.SetIfEmpty(elem, paramName, paramValue))
                            written++;
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Bulk Param Write",
                $"Set '{paramName}' on {written} of {selected.Count} elements.");
            return Result.Succeeded;
        }
    }
}
