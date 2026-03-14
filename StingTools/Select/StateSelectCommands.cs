using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx?.ActiveView == null) { TaskDialog.Show("Select", "No active view."); return Result.Failed; }
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            var ids = new FilteredElementCollector(ctx.Doc, ctx.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(e =>
                {
                    if (e.Category == null) return false;
                    string cat = ParameterHelpers.GetCategoryName(e);
                    if (!known.Contains(cat)) return false;
                    string tag = ParameterHelpers.GetString(e, ParamRegistry.TAG1);
                    return string.IsNullOrEmpty(tag);
                })
                .Select(e => e.Id).ToList();

            ctx.UIDoc.Selection.SetElementIds(ids);
            TaskDialog.Show("Select Untagged", $"Selected {ids.Count} untagged elements.");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class SelectTaggedCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx?.ActiveView == null) { TaskDialog.Show("Select", "No active view."); return Result.Failed; }
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            var ids = new FilteredElementCollector(ctx.Doc, ctx.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(e =>
                {
                    if (e.Category == null) return false;
                    string cat = ParameterHelpers.GetCategoryName(e);
                    if (!known.Contains(cat)) return false;
                    string tag = ParameterHelpers.GetString(e, ParamRegistry.TAG1);
                    return TagConfig.TagIsComplete(tag);
                })
                .Select(e => e.Id).ToList();

            ctx.UIDoc.Selection.SetElementIds(ids);
            TaskDialog.Show("Select Tagged", $"Selected {ids.Count} tagged elements.");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class SelectEmptyMarkCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx?.ActiveView == null) { TaskDialog.Show("Select", "No active view."); return Result.Failed; }

            var ids = new FilteredElementCollector(ctx.Doc, ctx.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(e =>
                {
                    Parameter p = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                    if (p == null) return false;
                    string val = p.AsString();
                    return string.IsNullOrEmpty(val);
                })
                .Select(e => e.Id).ToList();

            ctx.UIDoc.Selection.SetElementIds(ids);
            TaskDialog.Show("Select Empty Mark", $"Selected {ids.Count} elements with empty Mark.");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class SelectPinnedCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx?.ActiveView == null) { TaskDialog.Show("Select", "No active view."); return Result.Failed; }

            var ids = new FilteredElementCollector(ctx.Doc, ctx.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Pinned)
                .Select(e => e.Id).ToList();

            ctx.UIDoc.Selection.SetElementIds(ids);
            TaskDialog.Show("Select Pinned", $"Selected {ids.Count} pinned elements.");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class SelectUnpinnedCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx?.ActiveView == null) { TaskDialog.Show("Select", "No active view."); return Result.Failed; }
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            var ids = new FilteredElementCollector(ctx.Doc, ctx.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(e => !e.Pinned && e.Category != null && known.Contains(ParameterHelpers.GetCategoryName(e)))
                .Select(e => e.Id).ToList();

            ctx.UIDoc.Selection.SetElementIds(ids);
            TaskDialog.Show("Select Unpinned", $"Selected {ids.Count} unpinned elements.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Select elements by level. Works in ALL view types (not just plan views):
    /// - Plan views: uses associated level directly
    /// - Section/3D/other: shows a level picker with element counts per level
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class SelectByLevelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx?.ActiveView == null) { TaskDialog.Show("Select", "No active view."); return Result.Failed; }
            UIDocument uidoc = ctx.UIDoc;
            Document doc = ctx.Doc;
            View view = ctx.ActiveView;

            // Try to get level from plan view directly
            ElementId levelId = null;
            if (view is ViewPlan vp)
                levelId = vp.GenLevel?.Id;

            if (levelId != null && levelId != ElementId.InvalidElementId)
            {
                // Direct plan view: select elements on this level
                var ids = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .Where(e => e.LevelId == levelId)
                    .Select(e => e.Id).ToList();

                Level lvl = doc.GetElement(levelId) as Level;
                uidoc.Selection.SetElementIds(ids);
                TaskDialog.Show("Select by Level",
                    $"Selected {ids.Count} elements on '{lvl?.Name ?? "level"}'.");
                return Result.Succeeded;
            }

            // Non-plan view: show level picker with counts
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            if (levels.Count == 0)
            {
                TaskDialog.Show("Select by Level", "No levels found in project.");
                return Result.Succeeded;
            }

            // Count elements per level in active view
            var elemsByLevel = new Dictionary<ElementId, List<ElementId>>();
            foreach (Level l in levels)
                elemsByLevel[l.Id] = new List<ElementId>();

            foreach (Element e in new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType())
            {
                if (e.LevelId != null && elemsByLevel.ContainsKey(e.LevelId))
                    elemsByLevel[e.LevelId].Add(e.Id);
            }

            // Page through levels (4 at a time)
            var nonEmpty = levels.Where(l => elemsByLevel[l.Id].Count > 0).ToList();
            if (nonEmpty.Count == 0)
            {
                TaskDialog.Show("Select by Level", "No elements with assigned levels in this view.");
                return Result.Succeeded;
            }

            // Show top 4 levels by element count
            var top = nonEmpty.OrderByDescending(l => elemsByLevel[l.Id].Count).Take(4).ToList();
            TaskDialog dlg = new TaskDialog("Select by Level");
            dlg.MainInstruction = $"Pick a level ({nonEmpty.Count} levels with elements in view)";
            for (int i = 0; i < top.Count; i++)
            {
                dlg.AddCommandLink(
                    (TaskDialogCommandLinkId)(i + 1001),
                    $"{top[i].Name} — {elemsByLevel[top[i].Id].Count} elements",
                    $"Elevation: {top[i].Elevation:F2}");
            }
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            int idx = -1;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: idx = 0; break;
                case TaskDialogResult.CommandLink2: idx = 1; break;
                case TaskDialogResult.CommandLink3: idx = 2; break;
                case TaskDialogResult.CommandLink4: idx = 3; break;
                default: return Result.Cancelled;
            }

            if (idx >= 0 && idx < top.Count)
            {
                var selectedIds = elemsByLevel[top[idx].Id];
                uidoc.Selection.SetElementIds(selectedIds);
                TaskDialog.Show("Select by Level",
                    $"Selected {selectedIds.Count} elements on '{top[idx].Name}'.");
            }

            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Select elements in the same room as the first selected element.
    /// Works with ALL element types (not just FamilyInstance) by using:
    /// 1. FamilyInstance.Room property (direct)
    /// 2. Spatial lookup via ParameterHelpers.GetRoomAtElement (bounding box point-in-room)
    /// 3. Room name from STING LOC/ZONE parameters as fallback
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class SelectByRoomCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("Select", "No document open."); return Result.Failed; }
            UIDocument uidoc = ctx.UIDoc;
            Document doc = ctx.Doc;

            var selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 0)
            {
                TaskDialog.Show("Select by Room", "Select an element first, then run this command.");
                return Result.Succeeded;
            }

            Element seed = doc.GetElement(selected.First());
            if (seed == null) return Result.Succeeded;

            // Try multiple strategies to find the seed element's room
            Room room = null;

            // Strategy 1: FamilyInstance.Room
            if (seed is FamilyInstance fi)
                room = fi.Room;

            // Strategy 2: spatial lookup via helper
            if (room == null)
                room = ParameterHelpers.GetRoomAtElement(doc, seed);

            // Strategy 3: if still null, try all rooms to find one containing the element's point
            if (room == null)
            {
                XYZ point = null;
                if (seed.Location is LocationPoint lp) point = lp.Point;
                else if (seed.Location is LocationCurve lc)
                    point = (lc.Curve.GetEndPoint(0) + lc.Curve.GetEndPoint(1)) / 2.0;
                else
                {
                    var bb = seed.get_BoundingBox(null);
                    if (bb != null) point = (bb.Min + bb.Max) / 2.0;
                }

                if (point != null)
                {
                    room = doc.GetRoomAtPoint(point);
                }
            }

            if (room == null)
            {
                TaskDialog.Show("Select by Room",
                    "Cannot determine room for selected element.\n" +
                    "The element may not be inside a room boundary.");
                return Result.Succeeded;
            }

            // Find ALL elements in the same room (not just FamilyInstance)
            var roomId = room.Id;
            var ids = new List<ElementId>();
            var activeView = ctx.ActiveView ?? doc.ActiveView;
            if (activeView == null)
            {
                TaskDialog.Show("Select by Room", "No active view. Switch to a model view first.");
                return Result.Succeeded;
            }

            foreach (Element e in new FilteredElementCollector(doc, activeView.Id)
                .WhereElementIsNotElementType())
            {
                try
                {
                    // Check FamilyInstance.Room first
                    if (e is FamilyInstance fInst && fInst.Room?.Id == roomId)
                    {
                        ids.Add(e.Id);
                        continue;
                    }

                    // Check via spatial helper
                    Room elemRoom = ParameterHelpers.GetRoomAtElement(doc, e);
                    if (elemRoom?.Id == roomId)
                    {
                        ids.Add(e.Id);
                        continue;
                    }

                    // Check via point-in-room
                    XYZ pt = null;
                    if (e.Location is LocationPoint lp2) pt = lp2.Point;
                    else if (e.Location is LocationCurve lc2)
                        pt = (lc2.Curve.GetEndPoint(0) + lc2.Curve.GetEndPoint(1)) / 2.0;

                    if (pt != null)
                    {
                        Room r = doc.GetRoomAtPoint(pt);
                        if (r?.Id == roomId)
                            ids.Add(e.Id);
                    }
                }
                catch (Exception ex) { StingLog.Warn($"SelectByRoom element {e.Id}: {ex.Message}"); }
            }

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
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("Bulk Param Write", "No document open."); return Result.Failed; }
            UIDocument uidoc = ctx.UIDoc;
            Document doc = ctx.Doc;

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
            // Page 1: Choose token category
            TaskDialog catDlg = new TaskDialog("Set Token");
            catDlg.MainInstruction = $"Set token on {selected.Count} elements";
            catDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Set LOC (Location)");
            catDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Set ZONE");
            catDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Set STATUS");
            catDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Auto-detect STATUS from phases");
            catDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var catResult = catDlg.Show();

            string paramName = null;
            string paramValue = null;
            bool autoDetectStatus = false;

            switch (catResult)
            {
                case TaskDialogResult.CommandLink1:
                {
                    // LOC picker with all location codes
                    TaskDialog locDlg = new TaskDialog("Set LOC");
                    locDlg.MainInstruction = "Choose location code";
                    locDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "BLD1", "Building 1 (primary)");
                    locDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "BLD2", "Building 2");
                    locDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "BLD3", "Building 3");
                    locDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "EXT", "External / Site");
                    locDlg.CommonButtons = TaskDialogCommonButtons.Cancel;
                    paramName = ParamRegistry.LOC;
                    switch (locDlg.Show())
                    {
                        case TaskDialogResult.CommandLink1: paramValue = "BLD1"; break;
                        case TaskDialogResult.CommandLink2: paramValue = "BLD2"; break;
                        case TaskDialogResult.CommandLink3: paramValue = "BLD3"; break;
                        case TaskDialogResult.CommandLink4: paramValue = "EXT"; break;
                        default: return Result.Cancelled;
                    }
                    break;
                }
                case TaskDialogResult.CommandLink2:
                {
                    // ZONE picker
                    TaskDialog zoneDlg = new TaskDialog("Set ZONE");
                    zoneDlg.MainInstruction = "Choose zone code";
                    zoneDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Z01", "Zone 01");
                    zoneDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Z02", "Zone 02");
                    zoneDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Z03", "Zone 03");
                    zoneDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Z04", "Zone 04");
                    zoneDlg.CommonButtons = TaskDialogCommonButtons.Cancel;
                    paramName = ParamRegistry.ZONE;
                    switch (zoneDlg.Show())
                    {
                        case TaskDialogResult.CommandLink1: paramValue = "Z01"; break;
                        case TaskDialogResult.CommandLink2: paramValue = "Z02"; break;
                        case TaskDialogResult.CommandLink3: paramValue = "Z03"; break;
                        case TaskDialogResult.CommandLink4: paramValue = "Z04"; break;
                        default: return Result.Cancelled;
                    }
                    break;
                }
                case TaskDialogResult.CommandLink3:
                {
                    // All 4 valid ISO 19650 construction statuses
                    TaskDialog stsDlg = new TaskDialog("Set STATUS");
                    stsDlg.MainInstruction = "Choose construction status (ISO 19650)";
                    stsDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "NEW", "New construction — element to be built");
                    stsDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "EXISTING", "Existing — element already in place");
                    stsDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "DEMOLISHED", "Demolished — element to be removed");
                    stsDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "TEMPORARY", "Temporary — temporary element (hoarding, propping)");
                    stsDlg.CommonButtons = TaskDialogCommonButtons.Cancel;
                    paramName = ParamRegistry.STATUS;
                    switch (stsDlg.Show())
                    {
                        case TaskDialogResult.CommandLink1: paramValue = "NEW"; break;
                        case TaskDialogResult.CommandLink2: paramValue = "EXISTING"; break;
                        case TaskDialogResult.CommandLink3: paramValue = "DEMOLISHED"; break;
                        case TaskDialogResult.CommandLink4: paramValue = "TEMPORARY"; break;
                        default: return Result.Cancelled;
                    }
                    break;
                }
                case TaskDialogResult.CommandLink4:
                    autoDetectStatus = true;
                    break;
                default:
                    return Result.Cancelled;
            }

            int written = 0;
            int skipped = 0;
            using (Transaction tx = new Transaction(doc, "STING Bulk Set Token"))
            {
                tx.Start();
                foreach (ElementId id in selected)
                {
                    Element elem = doc.GetElement(id);
                    if (elem == null) { skipped++; continue; }

                    if (autoDetectStatus)
                    {
                        string status = PhaseAutoDetect.DetectStatus(doc, elem);
                        if (string.IsNullOrEmpty(status)) status = "NEW";
                        if (ParameterHelpers.SetString(elem, ParamRegistry.STATUS, status, overwrite: true))
                            written++;
                        else
                            skipped++;
                    }
                    else
                    {
                        if (ParameterHelpers.SetString(elem, paramName, paramValue, overwrite: true))
                            written++;
                        else
                            skipped++;
                    }
                }
                tx.Commit();
            }

            var sb = new StringBuilder();
            if (autoDetectStatus)
                sb.AppendLine($"Auto-detected STATUS from Revit phases on {written} of {selected.Count} elements.");
            else
                sb.AppendLine($"Set '{paramName}' = '{paramValue}' on {written} of {selected.Count} elements.");
            if (skipped > 0)
                sb.AppendLine($"Skipped {skipped} elements (parameter not bound or read-only).");
            TaskDialog.Show("Bulk Set Token", sb.ToString());
            return Result.Succeeded;
        }

        private static Result BulkAutoPopulate(Document doc, ICollection<ElementId> selected)
        {
            var popCtx = TokenAutoPopulator.PopulationContext.Build(doc);
            int populated = 0;
            int statusDetected = 0, revSet = 0;

            using (Transaction tx = new Transaction(doc, "STING Bulk Auto-Populate"))
            {
                tx.Start();
                foreach (ElementId id in selected)
                {
                    Element elem = doc.GetElement(id);
                    if (elem == null) continue;

                    // Full 9-token auto-population via shared helper
                    var result = TokenAutoPopulator.PopulateAll(doc, elem, popCtx);
                    populated += result.TokensSet;
                    if (result.StatusDetected) statusDetected++;
                    if (result.RevSet) revSet++;
                }
                tx.Commit();
            }

            var msg = new System.Text.StringBuilder();
            msg.AppendLine($"Auto-populated {populated} token values on {selected.Count} elements.");
            msg.AppendLine($"Tokens: DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, STATUS, REV");
            if (statusDetected > 0)
                msg.AppendLine($"STATUS auto-detected: {statusDetected} (from Revit phases/worksets)");
            if (revSet > 0)
                msg.AppendLine($"REV auto-set: {revSet} (revision '{popCtx.ProjectRev}')");
            TaskDialog.Show("Bulk Auto-Populate", msg.ToString());
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
            var (tagIndex, seqCounters) = TagConfig.BuildTagIndexAndCounters(doc);
            if (tagIndex == null) tagIndex = new HashSet<string>();
            if (seqCounters == null) seqCounters = new Dictionary<string, int>();
            int retagged = 0;
            int failed = 0;

            using (Transaction tx = new Transaction(doc, "STING Bulk Re-Tag"))
            {
                tx.Start();
                foreach (ElementId id in selected)
                {
                    Element elem = doc.GetElement(id);
                    if (elem == null) continue;
                    try
                    {
                        if (TagConfig.BuildAndWriteTag(doc, elem, seqCounters,
                            skipComplete: false,
                            existingTags: tagIndex,
                            collisionMode: TagCollisionMode.Overwrite))
                            retagged++;
                        else
                            failed++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        StingLog.Warn($"BulkRetag failed for element {id}: {ex.Message}");
                    }
                }
                tx.Commit();
            }

            string report = $"Re-tagged {retagged} of {selected.Count} elements.";
            if (failed > 0) report += $"\nFailed: {failed} elements (check log for details).";
            TaskDialog.Show("Bulk Re-Tag", report);
            return Result.Succeeded;
        }
    }
}
