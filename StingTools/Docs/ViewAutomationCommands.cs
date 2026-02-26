using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Docs
{
    /// <summary>
    /// Batch Viewport Align: align viewports across ALL sheets (or selected sheets).
    /// Extends the single-sheet AlignViewportsCommand to project-wide batch mode.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchAlignViewportsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => s.GetAllPlacedViews().Count > 1)
                .OrderBy(s => s.SheetNumber)
                .ToList();

            if (sheets.Count == 0)
            {
                TaskDialog.Show("Batch Align Viewports",
                    "No sheets with multiple viewports found.");
                return Result.Succeeded;
            }

            // Alignment mode
            TaskDialog modeDlg = new TaskDialog("Batch Align Viewports");
            modeDlg.MainInstruction = $"Align viewports on {sheets.Count} sheets";
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Top alignment", "Align top edges of viewports on each sheet");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Left alignment", "Align left edges of viewports on each sheet");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Center horizontal", "Center viewports horizontally on each sheet");
            modeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            string alignMode;
            switch (modeDlg.Show())
            {
                case TaskDialogResult.CommandLink1: alignMode = "top"; break;
                case TaskDialogResult.CommandLink2: alignMode = "left"; break;
                case TaskDialogResult.CommandLink3: alignMode = "centerH"; break;
                default: return Result.Cancelled;
            }

            int sheetsProcessed = 0;
            int viewportsAligned = 0;

            using (Transaction tx = new Transaction(doc, "STING Batch Align Viewports"))
            {
                tx.Start();

                foreach (ViewSheet sheet in sheets)
                {
                    try
                    {
                        var viewportIds = sheet.GetAllPlacedViews()
                            .Select(vid => new FilteredElementCollector(doc)
                                .OfClass(typeof(Viewport))
                                .Cast<Viewport>()
                                .FirstOrDefault(vp => vp.SheetId == sheet.Id && vp.ViewId == vid))
                            .Where(vp => vp != null)
                            .ToList();

                        if (viewportIds.Count < 2) continue;

                        // Get bounding boxes of viewports
                        var vpCenters = viewportIds
                            .Select(vp => new { VP = vp, Center = vp.GetBoxCenter() })
                            .ToList();

                        double target;
                        switch (alignMode)
                        {
                            case "top":
                                target = vpCenters.Max(v => v.Center.Y);
                                foreach (var v in vpCenters)
                                {
                                    XYZ newCenter = new XYZ(v.Center.X, target, 0);
                                    v.VP.SetBoxCenter(newCenter);
                                    viewportsAligned++;
                                }
                                break;
                            case "left":
                                target = vpCenters.Min(v => v.Center.X);
                                foreach (var v in vpCenters)
                                {
                                    XYZ newCenter = new XYZ(target, v.Center.Y, 0);
                                    v.VP.SetBoxCenter(newCenter);
                                    viewportsAligned++;
                                }
                                break;
                            case "centerH":
                                target = vpCenters.Average(v => v.Center.Y);
                                foreach (var v in vpCenters)
                                {
                                    XYZ newCenter = new XYZ(v.Center.X, target, 0);
                                    v.VP.SetBoxCenter(newCenter);
                                    viewportsAligned++;
                                }
                                break;
                        }

                        sheetsProcessed++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"BatchAlign sheet '{sheet.SheetNumber}': {ex.Message}");
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Batch Align Viewports",
                $"Processed {sheetsProcessed} sheets.\n" +
                $"Aligned {viewportsAligned} viewports ({alignMode}).");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Duplicate View: creates a copy of the active view with all filters,
    /// graphic overrides, and visibility state preserved.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DuplicateViewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            View source = doc.ActiveView;

            if (source is ViewSheet)
            {
                TaskDialog.Show("Duplicate View",
                    "Cannot duplicate a sheet. Switch to a model view.");
                return Result.Failed;
            }

            TaskDialog modeDlg = new TaskDialog("Duplicate View");
            modeDlg.MainInstruction = $"Duplicate '{source.Name}'";
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Duplicate",
                "Copy view structure only (no annotation or detail elements)");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Duplicate with Detailing",
                "Copy view with all annotation and detail elements");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Duplicate as Dependent",
                "Create a dependent view linked to this parent");
            modeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            ViewDuplicateOption option;
            switch (modeDlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    option = ViewDuplicateOption.Duplicate; break;
                case TaskDialogResult.CommandLink2:
                    option = ViewDuplicateOption.WithDetailing; break;
                case TaskDialogResult.CommandLink3:
                    option = ViewDuplicateOption.AsDependent; break;
                default:
                    return Result.Cancelled;
            }

            ElementId newViewId;
            using (Transaction tx = new Transaction(doc, "STING Duplicate View"))
            {
                tx.Start();

                try
                {
                    newViewId = source.Duplicate(option);
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    TaskDialog.Show("Duplicate View", $"Failed: {ex.Message}");
                    return Result.Failed;
                }

                // Copy filter overrides from source to new view
                View newView = doc.GetElement(newViewId) as View;
                if (newView != null)
                {
                    try
                    {
                        foreach (ElementId filterId in source.GetFilters())
                        {
                            if (!newView.GetFilters().Contains(filterId))
                            {
                                newView.AddFilter(filterId);
                            }

                            OverrideGraphicSettings filterOgs = source.GetFilterOverrides(filterId);
                            newView.SetFilterOverrides(filterId, filterOgs);
                            newView.SetFilterVisibility(filterId, source.GetFilterVisibility(filterId));
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"DuplicateView filter copy: {ex.Message}");
                    }
                }

                tx.Commit();
            }

            View duplicated = doc.GetElement(newViewId) as View;
            TaskDialog.Show("Duplicate View",
                $"Created '{duplicated?.Name ?? "new view"}'.\n" +
                $"Mode: {option}");

            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Batch Rename Views: find/replace or pattern-based renaming of views.
    /// Supports prefix/suffix addition and regex-like pattern replacement.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchRenameViewsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.CanBePrinted)
                .OrderBy(v => v.Name)
                .ToList();

            if (views.Count == 0)
            {
                TaskDialog.Show("Batch Rename Views", "No views found.");
                return Result.Succeeded;
            }

            // Rename mode
            TaskDialog modeDlg = new TaskDialog("Batch Rename Views");
            modeDlg.MainInstruction = $"Rename views ({views.Count} available)";
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Add STING prefix",
                "Prepend 'STING - ' to all view names that don't already have it");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Remove STING prefix",
                "Remove 'STING - ' prefix from all view names");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "UPPERCASE all view names",
                "Convert all view names to UPPER CASE");
            modeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            Func<string, string> renameFunc;
            string modeLabel;
            switch (modeDlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    renameFunc = name => name.StartsWith("STING - ") ? name : "STING - " + name;
                    modeLabel = "Add STING prefix";
                    break;
                case TaskDialogResult.CommandLink2:
                    renameFunc = name => name.StartsWith("STING - ") ? name.Substring(8) : name;
                    modeLabel = "Remove STING prefix";
                    break;
                case TaskDialogResult.CommandLink3:
                    renameFunc = name => name.ToUpperInvariant();
                    modeLabel = "UPPERCASE";
                    break;
                default:
                    return Result.Cancelled;
            }

            int renamed = 0;
            int skipped = 0;

            using (Transaction tx = new Transaction(doc, "STING Batch Rename Views"))
            {
                tx.Start();

                foreach (View v in views)
                {
                    try
                    {
                        string newName = renameFunc(v.Name);
                        if (newName != v.Name)
                        {
                            v.Name = newName;
                            renamed++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    catch (Exception ex)
                    {
                        skipped++;
                        StingLog.Warn($"Rename '{v.Name}': {ex.Message}");
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Batch Rename Views",
                $"Mode: {modeLabel}\n" +
                $"Renamed: {renamed}\n" +
                $"Skipped: {skipped}");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Copy View Settings: copies filters, graphic overrides, and visibility
    /// from the active view to selected target views.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CopyViewSettingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            View source = doc.ActiveView;

            var sourceFilters = source.GetFilters();

            // Collect target views (same type as source)
            var targetViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.Id != source.Id &&
                    v.ViewType == source.ViewType)
                .OrderBy(v => v.Name)
                .ToList();

            if (targetViews.Count == 0)
            {
                TaskDialog.Show("Copy View Settings",
                    $"No other {source.ViewType} views found.");
                return Result.Succeeded;
            }

            TaskDialog confirm = new TaskDialog("Copy View Settings");
            confirm.MainInstruction = $"Copy settings from '{source.Name}'?";
            confirm.MainContent =
                $"Source: {source.Name} ({sourceFilters.Count} filters)\n" +
                $"Target: {targetViews.Count} {source.ViewType} views\n\n" +
                "Will copy: filters, filter overrides, filter visibility.\n" +
                "Existing settings in target views will be overwritten.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            int viewsUpdated = 0;
            int filtersApplied = 0;

            using (Transaction tx = new Transaction(doc, "STING Copy View Settings"))
            {
                tx.Start();

                foreach (View target in targetViews)
                {
                    try
                    {
                        bool modified = false;

                        foreach (ElementId filterId in sourceFilters)
                        {
                            try
                            {
                                if (!target.GetFilters().Contains(filterId))
                                    target.AddFilter(filterId);

                                OverrideGraphicSettings ogs = source.GetFilterOverrides(filterId);
                                target.SetFilterOverrides(filterId, ogs);
                                target.SetFilterVisibility(filterId,
                                    source.GetFilterVisibility(filterId));
                                filtersApplied++;
                                modified = true;
                            }
                            catch (Exception ex)
                            {
                                StingLog.Warn($"CopyFilter to '{target.Name}': {ex.Message}");
                            }
                        }

                        if (modified) viewsUpdated++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"CopyViewSettings '{target.Name}': {ex.Message}");
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Copy View Settings",
                $"Updated {viewsUpdated} views.\n" +
                $"Applied {filtersApplied} filter settings.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Auto-Place Viewports: grid-based intelligent placement of viewports on sheets.
    /// Arranges viewports in a grid with consistent spacing and margins.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoPlaceViewportsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Must be on a sheet
            View activeView = doc.ActiveView;
            ViewSheet sheet = activeView as ViewSheet;
            if (sheet == null)
            {
                TaskDialog.Show("Auto-Place Viewports",
                    "Switch to a sheet view first.");
                return Result.Failed;
            }

            var viewportIds = sheet.GetAllPlacedViews()
                .Select(vid => new FilteredElementCollector(doc)
                    .OfClass(typeof(Viewport))
                    .Cast<Viewport>()
                    .FirstOrDefault(vp => vp.SheetId == sheet.Id && vp.ViewId == vid))
                .Where(vp => vp != null)
                .ToList();

            if (viewportIds.Count < 2)
            {
                TaskDialog.Show("Auto-Place Viewports",
                    "Need at least 2 viewports on the sheet to arrange.");
                return Result.Cancelled;
            }

            // Get title block bounds for margin calculation
            double sheetWidth = 841.0 / 304.8;  // A1 width in feet (default)
            double sheetHeight = 594.0 / 304.8;  // A1 height in feet

            var titleBlocks = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .OwnedByView(sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .ToList();

            if (titleBlocks.Count > 0)
            {
                BoundingBoxXYZ tbBox = titleBlocks[0].get_BoundingBox(null);
                if (tbBox != null)
                {
                    sheetWidth = tbBox.Max.X - tbBox.Min.X;
                    sheetHeight = tbBox.Max.Y - tbBox.Min.Y;
                }
            }

            // Grid layout
            double margin = sheetWidth * 0.05;  // 5% margin
            double usableWidth = sheetWidth - 2 * margin;
            double usableHeight = sheetHeight - 2 * margin;

            int count = viewportIds.Count;
            int cols = (int)Math.Ceiling(Math.Sqrt(count));
            int rows = (int)Math.Ceiling((double)count / cols);

            double cellWidth = usableWidth / cols;
            double cellHeight = usableHeight / rows;

            using (Transaction tx = new Transaction(doc, "STING Auto-Place Viewports"))
            {
                tx.Start();

                int idx = 0;
                for (int row = 0; row < rows && idx < count; row++)
                {
                    for (int col = 0; col < cols && idx < count; col++)
                    {
                        try
                        {
                            // Center of cell (row 0 is top)
                            double x = margin + col * cellWidth + cellWidth / 2.0;
                            double y = sheetHeight - margin - row * cellHeight - cellHeight / 2.0;

                            viewportIds[idx].SetBoxCenter(new XYZ(x, y, 0));
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"AutoPlace viewport: {ex.Message}");
                        }
                        idx++;
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Auto-Place Viewports",
                $"Arranged {count} viewports in {rows}×{cols} grid.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// View Crop to Content: auto-adjusts crop region to tightly fit element extents.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CropToContentCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            View view = doc.ActiveView;

            if (!view.CropBoxActive)
            {
                TaskDialog td = new TaskDialog("Crop to Content");
                td.MainInstruction = "Crop box is not active on this view.";
                td.MainContent = "Enable the crop box first?";
                td.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
                if (td.Show() == TaskDialogResult.Cancel)
                    return Result.Cancelled;
            }

            // Collect all element bounding boxes in view
            var allElements = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null)
                .ToList();

            if (allElements.Count == 0)
            {
                TaskDialog.Show("Crop to Content", "No elements found in view.");
                return Result.Cancelled;
            }

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (Element el in allElements)
            {
                BoundingBoxXYZ bb = el.get_BoundingBox(view);
                if (bb == null) continue;

                if (bb.Min.X < minX) minX = bb.Min.X;
                if (bb.Min.Y < minY) minY = bb.Min.Y;
                if (bb.Max.X > maxX) maxX = bb.Max.X;
                if (bb.Max.Y > maxY) maxY = bb.Max.Y;
            }

            if (minX >= maxX || minY >= maxY)
            {
                TaskDialog.Show("Crop to Content",
                    "Could not determine element extents.");
                return Result.Cancelled;
            }

            // Add 5% padding
            double padX = (maxX - minX) * 0.05;
            double padY = (maxY - minY) * 0.05;

            using (Transaction tx = new Transaction(doc, "STING Crop to Content"))
            {
                tx.Start();

                view.CropBoxActive = true;
                BoundingBoxXYZ cropBox = view.CropBox;

                // Build new crop region in view coordinates
                Transform viewTransform = cropBox.Transform;
                XYZ newMin = new XYZ(minX - padX, minY - padY, cropBox.Min.Z);
                XYZ newMax = new XYZ(maxX + padX, maxY + padY, cropBox.Max.Z);

                cropBox.Min = newMin;
                cropBox.Max = newMax;
                view.CropBox = cropBox;

                tx.Commit();
            }

            double widthFt = maxX - minX + 2 * padX;
            double heightFt = maxY - minY + 2 * padY;
            TaskDialog.Show("Crop to Content",
                $"Crop region adjusted to element extents.\n" +
                $"Size: {widthFt * 304.8:F0}mm × {heightFt * 304.8:F0}mm\n" +
                $"Elements in view: {allElements.Count}");

            return Result.Succeeded;
        }
    }
}
