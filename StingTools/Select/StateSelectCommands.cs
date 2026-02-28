using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
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
                    string tag = ParameterHelpers.GetString(e, ParamRegistry.TAG1);
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
                    string tag = ParameterHelpers.GetString(e, ParamRegistry.TAG1);
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

    /// <summary>
    /// Bulk write parameter values to selected elements. Provides quick-access
    /// presets for common operations plus multi-page options for all token types.
    /// </summary>
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

            // Page 1: Operation category
            TaskDialog td = new TaskDialog("Bulk Param Write");
            td.MainInstruction = $"Bulk operation on {selected.Count} selected elements";
            td.MainContent = "Choose an operation category:";
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Location / Zone / Status",
                "Set LOC, ZONE, or STATUS tokens");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Auto-populate all tokens",
                "Auto-derive DISC, PROD, SYS, FUNC, LVL, LOC, ZONE from category and spatial data");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Clear tags",
                "Clear ASS_TAG_1 and all token values (with confirmation)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Re-tag with overwrite",
                "Force re-derive all tokens and regenerate tags for selected elements");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            var page1 = td.Show();

            switch (page1)
            {
                case TaskDialogResult.CommandLink1:
                    return BulkSetToken(doc, selected);
                case TaskDialogResult.CommandLink2:
                    return BulkAutoPopulate(doc, selected);
                case TaskDialogResult.CommandLink3:
                    return BulkClearTags(doc, selected);
                case TaskDialogResult.CommandLink4:
                    return BulkRetag(doc, selected);
                default:
                    return Result.Cancelled;
            }
        }

        private static Result BulkSetToken(Document doc, ICollection<ElementId> selected)
        {
            TaskDialog td2 = new TaskDialog("Set Token");
            td2.MainInstruction = $"Set token on {selected.Count} elements";
            td2.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Set LOC to BLD1");
            td2.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Set ZONE to Z01");
            td2.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Set STATUS to NEW");
            td2.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Set STATUS to EXISTING");
            td2.CommonButtons = TaskDialogCommonButtons.Cancel;

            string paramName; string paramValue;
            switch (td2.Show())
            {
                case TaskDialogResult.CommandLink1: paramName = ParamRegistry.LOC; paramValue = "BLD1"; break;
                case TaskDialogResult.CommandLink2: paramName = ParamRegistry.ZONE; paramValue = "Z01"; break;
                case TaskDialogResult.CommandLink3: paramName = ParamRegistry.STATUS; paramValue = "NEW"; break;
                case TaskDialogResult.CommandLink4: paramName = ParamRegistry.STATUS; paramValue = "EXISTING"; break;
                default: return Result.Cancelled;
            }

            int written = 0;
            using (Transaction tx = new Transaction(doc, "STING Bulk Set Token"))
            {
                tx.Start();
                foreach (ElementId id in selected)
                {
                    Element elem = doc.GetElement(id);
                    if (elem != null && ParameterHelpers.SetString(elem, paramName, paramValue, overwrite: true))
                        written++;
                }
                tx.Commit();
            }
            TaskDialog.Show("Bulk Set Token", $"Set '{paramName}' = '{paramValue}' on {written} elements.");
            return Result.Succeeded;
        }

        private static Result BulkAutoPopulate(Document doc, ICollection<ElementId> selected)
        {
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);
            var roomIndex = SpatialAutoDetect.BuildRoomIndex(doc);
            string projectLoc = SpatialAutoDetect.DetectProjectLoc(doc);
            int populated = 0;

            using (Transaction tx = new Transaction(doc, "STING Bulk Auto-Populate"))
            {
                tx.Start();
                foreach (ElementId id in selected)
                {
                    Element elem = doc.GetElement(id);
                    if (elem == null) continue;
                    string catName = ParameterHelpers.GetCategoryName(elem);
                    if (!known.Contains(catName)) continue;

                    // DISC
                    string disc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : "XX";
                    if (ParameterHelpers.SetIfEmpty(elem, ParamRegistry.DISC, disc)) populated++;
                    // LOC
                    string loc = SpatialAutoDetect.DetectLoc(doc, elem, roomIndex, projectLoc);
                    if (ParameterHelpers.SetIfEmpty(elem, ParamRegistry.LOC, loc)) populated++;
                    // ZONE
                    string zone = SpatialAutoDetect.DetectZone(doc, elem, roomIndex);
                    if (ParameterHelpers.SetIfEmpty(elem, ParamRegistry.ZONE, zone)) populated++;
                    // LVL
                    string lvl = ParameterHelpers.GetLevelCode(doc, elem);
                    if (lvl != "XX") if (ParameterHelpers.SetIfEmpty(elem, ParamRegistry.LVL, lvl)) populated++;
                    // SYS
                    string sys = TagConfig.GetSysCode(catName);
                    if (!string.IsNullOrEmpty(sys)) if (ParameterHelpers.SetIfEmpty(elem, ParamRegistry.SYS, sys)) populated++;
                    // FUNC
                    string func = TagConfig.GetFuncCode(sys);
                    if (!string.IsNullOrEmpty(func)) if (ParameterHelpers.SetIfEmpty(elem, ParamRegistry.FUNC, func)) populated++;
                    // PROD (family-aware)
                    string prod = TagConfig.GetFamilyAwareProdCode(elem, catName);
                    if (ParameterHelpers.SetIfEmpty(elem, ParamRegistry.PROD, prod)) populated++;
                }
                tx.Commit();
            }
            TaskDialog.Show("Bulk Auto-Populate", $"Auto-populated {populated} token values on {selected.Count} elements.");
            return Result.Succeeded;
        }

        private static Result BulkClearTags(Document doc, ICollection<ElementId> selected)
        {
            TaskDialog confirm = new TaskDialog("Clear Tags");
            confirm.MainInstruction = $"Clear all tags from {selected.Count} elements?";
            confirm.MainContent = "This will clear ASS_TAG_1_TXT and all 8 token parameters.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel) return Result.Cancelled;

            string[] clearParams = ParamRegistry.AllTokenParams
                .Concat(new[] {
                    ParamRegistry.TAG1, ParamRegistry.TAG2, ParamRegistry.TAG3,
                    ParamRegistry.TAG4, ParamRegistry.TAG5, ParamRegistry.TAG6,
                    ParamRegistry.STATUS,
                }).Distinct().ToArray();

            int cleared = 0;
            using (Transaction tx = new Transaction(doc, "STING Clear Tags"))
            {
                tx.Start();
                foreach (ElementId id in selected)
                {
                    Element elem = doc.GetElement(id);
                    if (elem == null) continue;
                    bool any = false;
                    foreach (string p in clearParams)
                        if (ParameterHelpers.SetString(elem, p, "", overwrite: true)) any = true;
                    if (any) cleared++;
                }
                tx.Commit();
            }
            TaskDialog.Show("Clear Tags", $"Cleared tags from {cleared} elements.");
            return Result.Succeeded;
        }

        private static Result BulkRetag(Document doc, ICollection<ElementId> selected)
        {
            var seqCounters = TagConfig.GetExistingSequenceCounters(doc);
            var tagIndex = TagConfig.BuildExistingTagIndex(doc);
            int retagged = 0;

            using (Transaction tx = new Transaction(doc, "STING Bulk Re-Tag"))
            {
                tx.Start();
                foreach (ElementId id in selected)
                {
                    Element elem = doc.GetElement(id);
                    if (elem == null) continue;
                    if (TagConfig.BuildAndWriteTag(doc, elem, seqCounters,
                        skipComplete: false,
                        existingTags: tagIndex,
                        collisionMode: TagCollisionMode.Overwrite))
                        retagged++;
                }
                tx.Commit();
            }
            TaskDialog.Show("Bulk Re-Tag", $"Re-tagged {retagged} of {selected.Count} elements.");
            return Result.Succeeded;
        }
    }
}
