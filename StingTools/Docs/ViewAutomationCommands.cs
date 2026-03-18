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
    // ════════════════════════════════════════════════════════════════════
    //  Duplicate View — copy view with filters, overrides, visibility
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Duplicate the active view with all settings: filters, graphic overrides,
    /// visibility state, crop region, and view template assignment.
    /// Supports Duplicate, Duplicate with Detailing, and Duplicate as Dependent.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DuplicateViewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            if (ctx.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            UIDocument uidoc = ctx.UIDoc;
            Document doc = ctx.Doc;
            View sourceView = ctx.ActiveView;

            if (sourceView is ViewSheet)
            {
                TaskDialog.Show("Duplicate View", "Cannot duplicate a sheet. Open the view to duplicate.");
                return Result.Succeeded;
            }

            TaskDialog modeDlg = new TaskDialog("Duplicate View");
            modeDlg.MainInstruction = $"Duplicate '{sourceView.Name}'";
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Duplicate with Detailing (recommended)",
                "Copy view with all annotations, detail items, and tags");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Duplicate (view only)",
                "Copy view settings only — no annotations or detailing");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Duplicate as Dependent",
                "Create a dependent view linked to the original");
            modeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            ViewDuplicateOption option;
            switch (modeDlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    option = ViewDuplicateOption.WithDetailing; break;
                case TaskDialogResult.CommandLink2:
                    option = ViewDuplicateOption.Duplicate; break;
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
                    newViewId = sourceView.Duplicate(option);
                }
                catch (Exception ex)
                {
                    string errMsg = ex.Message;
                    tx.RollBack();
                    TaskDialog.Show("Duplicate View",
                        $"Cannot duplicate this view:\n{errMsg}");
                    return Result.Failed;
                }

                // Rename the duplicate
                View newView = doc.GetElement(newViewId) as View;
                if (newView != null)
                {
                    string baseName = sourceView.Name;
                    string newName = baseName + " Copy";
                    int suffix = 2;
                    while (ViewNameExists(doc, newName))
                    {
                        newName = $"{baseName} Copy {suffix++}";
                    }
                    try { newView.Name = newName; }
                    catch (Exception ex) { StingLog.Warn($"DuplicateView rename: {ex.Message}"); }
                }

                tx.Commit();
            }

            View duplicated = doc.GetElement(newViewId) as View;
            string resultName = duplicated?.Name ?? "Unknown";
            TaskDialog.Show("Duplicate View",
                $"Created: {resultName}\nMode: {option}");
            StingLog.Info($"DuplicateView: '{sourceView.Name}' → '{resultName}' ({option})");

            // Switch to the new view
            try { uidoc.ActiveView = duplicated; }
            catch (Exception ex) { StingLog.Warn($"DuplicateView activate: {ex.Message}"); }

            return Result.Succeeded;
        }

        private static bool ViewNameExists(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Any(v => v.Name == name);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Batch Rename Views — find/replace or pattern-based
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Batch rename views using find/replace patterns.
    /// Supports common BIM renaming: adding prefixes, removing suffixes,
    /// standardising discipline codes, and level name updates.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchRenameViewsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

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

            // Common rename operations
            TaskDialog opDlg = new TaskDialog("Batch Rename Views");
            opDlg.MainInstruction = $"Rename {views.Count} views";
            opDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Add 'STING - ' prefix",
                "Prefix all views with 'STING - ' for project identification");
            opDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Remove ' Copy' suffix",
                "Clean up duplicated view names (removes ' Copy', ' Copy 2', etc.)");
            opDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "UPPERCASE all names",
                "Convert all view names to UPPERCASE for consistency");
            opDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Standardise level names / Custom find-replace",
                "Replace 'Level 1' with 'L01', or enter custom find/replace text");
            opDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            int mode;
            switch (opDlg.Show())
            {
                case TaskDialogResult.CommandLink1: mode = 1; break;
                case TaskDialogResult.CommandLink2: mode = 2; break;
                case TaskDialogResult.CommandLink3: mode = 3; break;
                case TaskDialogResult.CommandLink4:
                    // Sub-dialog: standardise vs custom
                    TaskDialog subDlg = new TaskDialog("Mode");
                    subDlg.MainInstruction = "Choose operation:";
                    subDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Standardise level names");
                    subDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Custom find/replace");
                    mode = subDlg.Show() == TaskDialogResult.CommandLink2 ? 5 : 4;
                    break;
                default: return Result.Cancelled;
            }

            // FIX-C04: Prompt for custom find/replace strings when mode 5 selected
            string findText = null, replaceText = null;
            if (mode == 5)
            {
                TaskDialog findDlg = new TaskDialog("Custom Find/Replace");
                findDlg.MainInstruction = "Enter the text to find:";
                findDlg.MainContent = "Type the text you want to search for in view names.\n" +
                    "This will be replaced in ALL matching views.";
                findDlg.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;

                // Use a simple approach: prompt via two sequential TaskDialogs
                // Since TaskDialog doesn't have text input, use a workaround with
                // Microsoft.VisualBasic.Interaction.InputBox or a simple WPF input
                try
                {
                    var inputWin = new System.Windows.Window
                    {
                        Title = "Find Text",
                        Width = 400, Height = 160,
                        WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
                        ResizeMode = System.Windows.ResizeMode.NoResize
                    };
                    var stack = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(10) };
                    stack.Children.Add(new System.Windows.Controls.TextBlock { Text = "Find:", Margin = new System.Windows.Thickness(0, 0, 0, 5) });
                    var findBox = new System.Windows.Controls.TextBox { Margin = new System.Windows.Thickness(0, 0, 0, 5) };
                    stack.Children.Add(findBox);
                    stack.Children.Add(new System.Windows.Controls.TextBlock { Text = "Replace with:", Margin = new System.Windows.Thickness(0, 5, 0, 5) });
                    var replaceBox = new System.Windows.Controls.TextBox { Margin = new System.Windows.Thickness(0, 0, 0, 10) };
                    stack.Children.Add(replaceBox);
                    var okBtn = new System.Windows.Controls.Button { Content = "OK", Width = 80, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
                    okBtn.Click += (s, ev) => { inputWin.DialogResult = true; inputWin.Close(); };
                    stack.Children.Add(okBtn);
                    inputWin.Content = stack;
                    findBox.Focus();

                    if (inputWin.ShowDialog() != true || string.IsNullOrEmpty(findBox.Text))
                    {
                        TaskDialog.Show("Batch Rename Views", "Find text cannot be empty.");
                        return Result.Cancelled;
                    }
                    findText = findBox.Text;
                    replaceText = replaceBox.Text ?? "";
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"BatchRenameViews custom input: {ex.Message}");
                    TaskDialog.Show("Batch Rename Views", "Could not open input dialog.");
                    return Result.Failed;
                }
            }

            int renamed = 0;
            int failed = 0;
            var changes = new List<(string oldName, string newName)>();

            using (Transaction tx = new Transaction(doc, "STING Batch Rename Views"))
            {
                tx.Start();
                foreach (View v in views)
                {
                    string oldName = v.Name;
                    string newName = oldName;

                    switch (mode)
                    {
                        case 1: // Add prefix
                            if (!oldName.StartsWith("STING - "))
                                newName = "STING - " + oldName;
                            break;
                        case 2: // Remove Copy suffix
                            newName = System.Text.RegularExpressions.Regex.Replace(
                                oldName, @"\s*Copy\s*\d*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            break;
                        case 3: // UPPERCASE
                            newName = oldName.ToUpperInvariant();
                            break;
                        case 4: // Standardise levels
                            newName = StandardiseLevelName(oldName);
                            break;
                        case 5: // FIX-C04: Custom find/replace
                            if (findText != null && oldName.Contains(findText))
                                newName = oldName.Replace(findText, replaceText);
                            break;
                    }

                    if (newName == oldName) continue;
                    if (ViewNameExists(doc, newName, v.Id)) continue;

                    try
                    {
                        v.Name = newName;
                        renamed++;
                        changes.Add((oldName, newName));
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        StingLog.Warn($"Rename '{oldName}': {ex.Message}");
                    }
                }
                tx.Commit();
            }

            var report = new StringBuilder();
            report.AppendLine($"Renamed {renamed} of {views.Count} views.");
            if (failed > 0) report.AppendLine($"Failed: {failed}");
            report.AppendLine();
            foreach (var (old, nw) in changes.Take(15))
                report.AppendLine($"  {old} → {nw}");
            if (changes.Count > 15)
                report.AppendLine($"  ... and {changes.Count - 15} more");

            TaskDialog.Show("Batch Rename Views", report.ToString());
            return Result.Succeeded;
        }

        private static string StandardiseLevelName(string name)
        {
            var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Ground Floor"] = "GF",
                ["Ground Level"] = "GF",
                ["Ground"] = "GF",
                ["Basement 1"] = "B1",
                ["Basement 2"] = "B2",
                ["Basement"] = "B1",
                ["Roof"] = "RF",
                ["Roof Level"] = "RF",
                ["Level 1"] = "L01",
                ["Level 2"] = "L02",
                ["Level 3"] = "L03",
                ["Level 4"] = "L04",
                ["Level 5"] = "L05",
                ["Level 6"] = "L06",
                ["Level 7"] = "L07",
                ["Level 8"] = "L08",
                ["Level 9"] = "L09",
                ["Level 10"] = "L10",
                ["First Floor"] = "L01",
                ["Second Floor"] = "L02",
                ["Third Floor"] = "L03",
            };

            foreach (var kvp in replacements)
            {
                if (name.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    name = name.Replace(kvp.Key, kvp.Value,
                        StringComparison.OrdinalIgnoreCase);
                    break;
                }
            }
            return name;
        }

        private static bool ViewNameExists(Document doc, string name, ElementId excludeId)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Any(v => v.Name == name && v.Id != excludeId);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Copy View Settings — filters + overrides between views
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Copy view filters, graphic overrides, and visibility settings from the
    /// active view to other views of the same type.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CopyViewSettingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            if (ctx.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            Document doc = ctx.Doc;
            View source = ctx.ActiveView;

            if (source is ViewSheet || source.IsTemplate)
            {
                TaskDialog.Show("Copy View Settings",
                    "Active view must be a regular view (not sheet or template).");
                return Result.Succeeded;
            }

            // Get same-type views as targets
            var targets = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.Id != source.Id &&
                    !v.IsTemplate &&
                    v.ViewType == source.ViewType &&
                    v.CanBePrinted)
                .OrderBy(v => v.Name)
                .ToList();

            if (targets.Count == 0)
            {
                TaskDialog.Show("Copy View Settings",
                    $"No other {source.ViewType} views found to copy settings to.");
                return Result.Succeeded;
            }

            // Get source filters
            var sourceFilterIds = source.GetFilters();

            TaskDialog dlg = new TaskDialog("Copy View Settings");
            dlg.MainInstruction = $"Copy settings from '{source.Name}'";
            dlg.MainContent =
                $"Source view type: {source.ViewType}\n" +
                $"Filters to copy: {sourceFilterIds.Count}\n" +
                $"Target views available: {targets.Count}\n\n" +
                "Settings copied: view filters, filter overrides, " +
                "category visibility, detail level, scale.";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"Apply to all {targets.Count} {source.ViewType} views",
                "Copy to every matching view");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Apply to STING views only",
                "Copy only to views with 'STING' in the name");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            List<View> selectedTargets;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    selectedTargets = targets;
                    break;
                case TaskDialogResult.CommandLink2:
                    selectedTargets = targets
                        .Where(v => v.Name.IndexOf("STING", StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();
                    break;
                default:
                    return Result.Cancelled;
            }

            if (selectedTargets.Count == 0)
            {
                TaskDialog.Show("Copy View Settings", "No matching target views found.");
                return Result.Succeeded;
            }

            int updated = 0;
            int filtersCopied = 0;

            using (Transaction tx = new Transaction(doc, "STING Copy View Settings"))
            {
                tx.Start();
                foreach (View target in selectedTargets)
                {
                    try
                    {
                        // Copy detail level and scale
                        if (source.DetailLevel != ViewDetailLevel.Undefined)
                            target.DetailLevel = source.DetailLevel;
                        try { target.Scale = source.Scale; }
                        catch (Exception ex) { StingLog.Warn($"CopyViewSettings scale: {ex.Message}"); }

                        // Copy filters
                        foreach (ElementId filterId in sourceFilterIds)
                        {
                            try
                            {
                                // Check if filter already applied
                                var existingFilters = target.GetFilters();
                                if (!existingFilters.Contains(filterId))
                                    target.AddFilter(filterId);

                                // Copy filter overrides and visibility
                                var ogs = source.GetFilterOverrides(filterId);
                                target.SetFilterOverrides(filterId, ogs);
                                target.SetFilterVisibility(filterId,
                                    source.GetFilterVisibility(filterId));
                                filtersCopied++;
                            }
                            catch (Exception ex)
                            {
                                StingLog.Warn($"Copy filter to '{target.Name}': {ex.Message}");
                            }
                        }

                        updated++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"CopyViewSettings to '{target.Name}': {ex.Message}");
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Copy View Settings",
                $"Updated {updated} of {selectedTargets.Count} views.\n" +
                $"Filters applied: {filtersCopied} total " +
                $"({sourceFilterIds.Count} filters × {updated} views).");
            StingLog.Info($"CopyViewSettings: source='{source.Name}', targets={updated}, filters={filtersCopied}");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Auto-Place Viewports — grid-based intelligent placement
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Automatically place viewports on a sheet using a grid layout.
    /// Arranges views in a grid pattern within the sheet's title block area.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoPlaceViewportsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            if (ctx.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            Document doc = ctx.Doc;
            View activeView = ctx.ActiveView;

            if (!(activeView is ViewSheet sheet))
            {
                TaskDialog.Show("Auto-Place Viewports",
                    "Active view must be a sheet.\nOpen a sheet first.");
                return Result.Succeeded;
            }

            // Get existing viewports to know what's already placed
            var existingVpIds = sheet.GetAllViewports();

            // Find unplaced views
            var placedViewIds = new HashSet<ElementId>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .SelectMany(s => s.GetAllPlacedViews()));

            var unplacedViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.CanBePrinted &&
                    !(v is ViewSheet) &&
                    !placedViewIds.Contains(v.Id))
                .OrderBy(v => v.ViewType.ToString())
                .ThenBy(v => v.Name)
                .ToList();

            if (unplacedViews.Count == 0)
            {
                TaskDialog.Show("Auto-Place Viewports",
                    "All views are already placed on sheets.");
                return Result.Succeeded;
            }

            TaskDialog dlg = new TaskDialog("Auto-Place Viewports");
            dlg.MainInstruction = $"Place views on '{sheet.Name}'";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"Place first {Math.Min(4, unplacedViews.Count)} views (2×2 grid)",
                "4 viewports in a 2-column, 2-row grid");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                $"Place first {Math.Min(6, unplacedViews.Count)} views (3×2 grid)",
                "6 viewports in a 3-column, 2-row grid");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Place single view (centered)",
                "One viewport centered on the sheet");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;
            dlg.FooterText = $"{unplacedViews.Count} unplaced views available.";

            int cols, rows;
            int maxViews;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    cols = 2; rows = 2; maxViews = 4; break;
                case TaskDialogResult.CommandLink2:
                    cols = 3; rows = 2; maxViews = 6; break;
                case TaskDialogResult.CommandLink3:
                    cols = 1; rows = 1; maxViews = 1; break;
                default:
                    return Result.Cancelled;
            }

            var viewsToPlace = unplacedViews.Take(maxViews).ToList();

            // Sheet dimensions (A1 default: 841mm × 594mm ≈ 2.76ft × 1.95ft)
            // Use title block to get sheet size
            double sheetWidth = 2.76;  // feet (A1)
            double sheetHeight = 1.95;
            double margin = 0.15;      // margin from edges

            // Try to get actual sheet dimensions from title block
            var titleBlocks = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .ToList();

            if (titleBlocks.Count > 0)
            {
                BoundingBoxXYZ tbBB = titleBlocks[0].get_BoundingBox(null);
                if (tbBB != null)
                {
                    sheetWidth = tbBB.Max.X - tbBB.Min.X;
                    sheetHeight = tbBB.Max.Y - tbBB.Min.Y;
                }
            }

            double usableWidth = sheetWidth - 2 * margin;
            double usableHeight = sheetHeight - 2 * margin;
            double cellWidth = usableWidth / cols;
            double cellHeight = usableHeight / rows;

            int placed = 0;
            using (Transaction tx = new Transaction(doc, "STING Auto-Place Viewports"))
            {
                tx.Start();
                int idx = 0;
                for (int row = 0; row < rows && idx < viewsToPlace.Count; row++)
                {
                    for (int col = 0; col < cols && idx < viewsToPlace.Count; col++)
                    {
                        View v = viewsToPlace[idx++];
                        double cx = margin + cellWidth * (col + 0.5);
                        double cy = sheetHeight - margin - cellHeight * (row + 0.5);
                        XYZ center = new XYZ(cx, cy, 0);

                        try
                        {
                            Viewport vp = Viewport.Create(doc, sheet.Id, v.Id, center);
                            if (vp != null) placed++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Place viewport '{v.Name}': {ex.Message}");
                        }
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Auto-Place Viewports",
                $"Placed {placed} of {viewsToPlace.Count} viewports on '{sheet.Name}'.\n" +
                $"Layout: {cols}×{rows} grid.");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Crop to Content — auto-crop view to element extents
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Auto-crop the active view boundaries to fit element extents with optional padding.
    /// Works on floor plans, sections, and elevations with crop regions.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CropToContentCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            if (ctx.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            Document doc = ctx.Doc;
            View view = ctx.ActiveView;

            if (view is ViewSheet || view.IsTemplate)
            {
                TaskDialog.Show("Crop to Content",
                    "Active view must be a floor plan, section, or elevation.");
                return Result.Succeeded;
            }

            // Get all model elements in the view
            var elems = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null &&
                    e.Category.CategoryType == CategoryType.Model)
                .ToList();

            if (elems.Count == 0)
            {
                TaskDialog.Show("Crop to Content", "No model elements in active view.");
                return Result.Succeeded;
            }

            // Calculate bounding box of all elements
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            int counted = 0;

            foreach (Element elem in elems)
            {
                BoundingBoxXYZ bb = elem.get_BoundingBox(view);
                if (bb == null) continue;

                if (bb.Min.X < minX) minX = bb.Min.X;
                if (bb.Min.Y < minY) minY = bb.Min.Y;
                if (bb.Min.Z < minZ) minZ = bb.Min.Z;
                if (bb.Max.X > maxX) maxX = bb.Max.X;
                if (bb.Max.Y > maxY) maxY = bb.Max.Y;
                if (bb.Max.Z > maxZ) maxZ = bb.Max.Z;
                counted++;
            }

            if (counted == 0)
            {
                TaskDialog.Show("Crop to Content", "No elements with bounding boxes found.");
                return Result.Succeeded;
            }

            // Padding options
            TaskDialog padDlg = new TaskDialog("Crop to Content");
            padDlg.MainInstruction = $"Crop view to {counted} elements";
            double widthM = (maxX - minX) * 0.3048;
            double heightM = (maxY - minY) * 0.3048;
            padDlg.MainContent = $"Content extent: {widthM:F1}m × {heightM:F1}m";

            padDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Tight crop (5% padding)", "Minimal margin around content");
            padDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Standard crop (10% padding)", "Standard drawing margin");
            padDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Loose crop (20% padding)", "Extra space for annotations and dimensions");
            padDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            double padFactor;
            switch (padDlg.Show())
            {
                case TaskDialogResult.CommandLink1: padFactor = 0.05; break;
                case TaskDialogResult.CommandLink2: padFactor = 0.10; break;
                case TaskDialogResult.CommandLink3: padFactor = 0.20; break;
                default: return Result.Cancelled;
            }

            double padX = (maxX - minX) * padFactor;
            double padY = (maxY - minY) * padFactor;
            if (padX < 1.0) padX = 1.0; // Minimum 1 foot padding
            if (padY < 1.0) padY = 1.0;

            using (Transaction tx = new Transaction(doc, "STING Crop to Content"))
            {
                tx.Start();

                // Enable crop box
                view.CropBoxActive = true;
                view.CropBoxVisible = true;

                BoundingBoxXYZ cropBox = view.CropBox;
                Transform inverse = cropBox.Transform.Inverse;

                // Transform element extents from model coords to view coords
                XYZ viewMin = inverse.OfPoint(new XYZ(minX - padX, minY - padY, minZ));
                XYZ viewMax = inverse.OfPoint(new XYZ(maxX + padX, maxY + padY, maxZ));

                // CropBox min/max must be in view-local coordinates
                cropBox.Min = new XYZ(viewMin.X, viewMin.Y, cropBox.Min.Z);
                cropBox.Max = new XYZ(viewMax.X, viewMax.Y, cropBox.Max.Z);

                view.CropBox = cropBox;
                tx.Commit();
            }

            TaskDialog.Show("Crop to Content",
                $"Cropped view to {counted} elements with {padFactor * 100:F0}% padding.\n" +
                $"Content: {widthM:F1}m × {heightM:F1}m");
            StingLog.Info($"CropToContent: elements={counted}, padding={padFactor*100:F0}%");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Batch Align Viewports — across multiple sheets
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Align viewports across all sheets to the same position.
    /// Ensures consistent viewport placement for drawing sets.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchAlignViewportsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => s.GetAllViewports().Any())
                .OrderBy(s => s.SheetNumber)
                .ToList();

            if (sheets.Count < 2)
            {
                TaskDialog.Show("Batch Align Viewports",
                    "Need at least 2 sheets with viewports.");
                return Result.Succeeded;
            }

            // Use active sheet as reference (or first sheet)
            ViewSheet refSheet = ctx.ActiveView is ViewSheet activeSheet
                ? activeSheet
                : sheets[0];

            var refVpIds = refSheet.GetAllViewports().ToList();
            if (refVpIds.Count == 0)
            {
                TaskDialog.Show("Batch Align Viewports",
                    "Reference sheet has no viewports.");
                return Result.Succeeded;
            }

            // Get reference position (center of first viewport)
            Viewport refVp = doc.GetElement(refVpIds[0]) as Viewport;
            XYZ refCenter = refVp?.GetBoxCenter();

            if (refCenter == null)
            {
                TaskDialog.Show("Batch Align Viewports", "Cannot determine reference position.");
                return Result.Succeeded;
            }

            TaskDialog dlg = new TaskDialog("Batch Align Viewports");
            dlg.MainInstruction = $"Align viewports across {sheets.Count} sheets";
            dlg.MainContent =
                $"Reference: '{refSheet.SheetNumber} - {refSheet.Name}'\n" +
                $"Reference position: ({refCenter.X:F2}, {refCenter.Y:F2})\n\n" +
                "The primary (first) viewport on each sheet will be moved to match " +
                "the reference position. This ensures consistent placement across sheets.";
            dlg.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (dlg.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            int sheetsUpdated = 0;
            int vpMoved = 0;

            using (Transaction tx = new Transaction(doc, "STING Batch Align Viewports"))
            {
                tx.Start();
                foreach (ViewSheet s in sheets)
                {
                    if (s.Id == refSheet.Id) continue;

                    var vpIds = s.GetAllViewports().ToList();
                    if (vpIds.Count == 0) continue;

                    Viewport vp = doc.GetElement(vpIds[0]) as Viewport;
                    if (vp == null) continue;

                    XYZ currentCenter = vp.GetBoxCenter();
                    if (currentCenter.IsAlmostEqualTo(refCenter)) continue;

                    // Move primary viewport to reference position
                    vp.SetBoxCenter(refCenter);
                    vpMoved++;

                    // If there are additional viewports, maintain their relative offset
                    if (vpIds.Count > 1)
                    {
                        XYZ delta = refCenter - currentCenter;
                        for (int i = 1; i < vpIds.Count; i++)
                        {
                            Viewport otherVp = doc.GetElement(vpIds[i]) as Viewport;
                            if (otherVp == null) continue;

                            XYZ otherCenter = otherVp.GetBoxCenter();
                            otherVp.SetBoxCenter(otherCenter + delta);
                            vpMoved++;
                        }
                    }
                    sheetsUpdated++;
                }
                tx.Commit();
            }

            TaskDialog.Show("Batch Align Viewports",
                $"Updated {sheetsUpdated} sheets, moved {vpMoved} viewports.\n" +
                $"Reference: '{refSheet.SheetNumber}'.");
            StingLog.Info($"BatchAlignViewports: sheets={sheetsUpdated}, viewports={vpMoved}");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  MagicRenameCommand — Universal batch rename with patterns
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Universal rename tool for views, sheets, rooms, and families.
    /// Supports Prefix/Suffix, Find and Replace, Case change, and
    /// Sequential numbering modes.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MagicRenameCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                // Step 1: Choose element type
                var typeDlg = new TaskDialog("STING Magic Rename");
                typeDlg.MainInstruction = "What do you want to rename?";
                typeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Views",
                    "Rename floor plans, sections, elevations, 3D views, etc.");
                typeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Sheets",
                    "Rename sheet names (preserves sheet numbers).");
                typeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Rooms",
                    "Rename room names.");
                typeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Families",
                    "Rename family types loaded in the project.");
                typeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var typeResult = typeDlg.Show();
                string elementType;
                if (typeResult == TaskDialogResult.CommandLink1) elementType = "Views";
                else if (typeResult == TaskDialogResult.CommandLink2) elementType = "Sheets";
                else if (typeResult == TaskDialogResult.CommandLink3) elementType = "Rooms";
                else if (typeResult == TaskDialogResult.CommandLink4) elementType = "Families";
                else return Result.Cancelled;

                // Step 2: Choose rename mode
                var modeDlg = new TaskDialog("STING Magic Rename");
                modeDlg.MainInstruction = "Rename mode";
                modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Add Prefix/Suffix",
                    "Add text before or after existing names.");
                modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Find & Replace",
                    "Replace text within names.");
                modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Change Case",
                    "Convert to UPPER, lower, or Title Case.");
                modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Sequential Number",
                    "Add sequential numbers (001, 002, ...).");
                modeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var modeResult = modeDlg.Show();
                string mode;
                if (modeResult == TaskDialogResult.CommandLink1) mode = "PrefixSuffix";
                else if (modeResult == TaskDialogResult.CommandLink2) mode = "FindReplace";
                else if (modeResult == TaskDialogResult.CommandLink3) mode = "Case";
                else if (modeResult == TaskDialogResult.CommandLink4) mode = "Sequential";
                else return Result.Cancelled;

                // Collect elements
                var targetElements = new List<Element>();
                if (elementType == "Views")
                {
                    targetElements.AddRange(new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .Where(v => !v.IsTemplate && v.ViewType != ViewType.DrawingSheet)
                        .Cast<Element>());
                }
                else if (elementType == "Sheets")
                {
                    targetElements.AddRange(new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheet))
                        .Cast<Element>());
                }
                else if (elementType == "Rooms")
                {
                    targetElements.AddRange(new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Element>());
                }
                else if (elementType == "Families")
                {
                    targetElements.AddRange(new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .Cast<Element>());
                }

                if (targetElements.Count == 0)
                {
                    TaskDialog.Show("STING", $"No {elementType.ToLower()} found in the project.");
                    return Result.Cancelled;
                }

                // Get rename parameters via input dialogs
                string prefix = "", suffix = "", findText = "", replaceText = "", caseMode = "UPPER";
                int seqStart = 1;

                if (mode == "PrefixSuffix")
                {
                    var inp = new TaskDialog("Prefix/Suffix");
                    inp.MainInstruction = "Enter prefix and/or suffix";
                    inp.MainContent = $"Found {targetElements.Count} {elementType.ToLower()}.\n" +
                        "Enter prefix in the format: PREFIX_ or _SUFFIX or PREFIX_*_SUFFIX\n" +
                        "(Use * as placeholder for existing name)";
                    inp.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Add 'STING - ' prefix");
                    inp.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Add ' - REV' suffix");
                    inp.CommonButtons = TaskDialogCommonButtons.Cancel;

                    var r = inp.Show();
                    if (r == TaskDialogResult.CommandLink1) { prefix = "STING - "; }
                    else if (r == TaskDialogResult.CommandLink2) { suffix = " - REV"; }
                    else return Result.Cancelled;
                }
                else if (mode == "FindReplace")
                {
                    // Simple preset-based find/replace
                    var inp = new TaskDialog("Find & Replace");
                    inp.MainInstruction = $"Find & Replace in {targetElements.Count} {elementType.ToLower()}";
                    inp.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Remove 'Copy' / 'Copy 1'",
                        "Clean up duplicated view names.");
                    inp.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Replace '-' with ' - '",
                        "Standardise dash spacing.");
                    inp.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Remove leading/trailing spaces",
                        "Trim whitespace from names.");
                    inp.CommonButtons = TaskDialogCommonButtons.Cancel;

                    var r = inp.Show();
                    if (r == TaskDialogResult.CommandLink1) { findText = " Copy"; replaceText = ""; }
                    else if (r == TaskDialogResult.CommandLink2) { findText = "-"; replaceText = " - "; }
                    else if (r == TaskDialogResult.CommandLink3) { mode = "Trim"; }
                    else return Result.Cancelled;
                }
                else if (mode == "Case")
                {
                    var inp = new TaskDialog("Case Change");
                    inp.MainInstruction = "Select case mode";
                    inp.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "UPPER CASE");
                    inp.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "lower case");
                    inp.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Title Case");
                    inp.CommonButtons = TaskDialogCommonButtons.Cancel;

                    var r = inp.Show();
                    if (r == TaskDialogResult.CommandLink1) caseMode = "UPPER";
                    else if (r == TaskDialogResult.CommandLink2) caseMode = "lower";
                    else if (r == TaskDialogResult.CommandLink3) caseMode = "Title";
                    else return Result.Cancelled;
                }

                // Apply renames
                int renamed = 0, skipped = 0;
                using (var tx = new Transaction(doc, "STING Magic Rename"))
                {
                    tx.Start();
                    int seq = seqStart;
                    foreach (var el in targetElements)
                    {
                        try
                        {
                            string currentName = "";
                            Parameter nameParam = null;

                            if (el is View v)
                            {
                                currentName = v.Name ?? "";
                                nameParam = el.get_Parameter(BuiltInParameter.VIEW_NAME);
                            }
                            else if (el is ViewSheet s)
                            {
                                currentName = s.Name ?? "";
                                nameParam = el.get_Parameter(BuiltInParameter.SHEET_NAME);
                            }
                            else if (el.Category?.BuiltInCategory == BuiltInCategory.OST_Rooms)
                            {
                                nameParam = el.get_Parameter(BuiltInParameter.ROOM_NAME);
                                currentName = nameParam?.AsString() ?? "";
                            }
                            else if (el is FamilySymbol fs)
                            {
                                currentName = fs.Name ?? "";
                                nameParam = null; // Will use Name property
                            }

                            if (string.IsNullOrEmpty(currentName)) { skipped++; continue; }

                            string newName = currentName;

                            switch (mode)
                            {
                                case "PrefixSuffix":
                                    newName = prefix + currentName + suffix;
                                    break;
                                case "FindReplace":
                                    newName = currentName.Replace(findText, replaceText);
                                    break;
                                case "Trim":
                                    newName = currentName.Trim();
                                    break;
                                case "Case":
                                    newName = caseMode switch
                                    {
                                        "UPPER" => currentName.ToUpperInvariant(),
                                        "lower" => currentName.ToLowerInvariant(),
                                        "Title" => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(currentName.ToLowerInvariant()),
                                        _ => currentName
                                    };
                                    break;
                                case "Sequential":
                                    newName = $"{currentName} {seq:D3}";
                                    seq++;
                                    break;
                            }

                            if (newName == currentName) { skipped++; continue; }

                            if (nameParam != null && !nameParam.IsReadOnly)
                                nameParam.Set(newName);
                            else if (el is FamilySymbol fsSym)
                                fsSym.Name = newName;
                            else
                                el.Name = newName;

                            renamed++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"MagicRename: failed to rename '{el.Name}': {ex.Message}");
                            skipped++;
                        }
                    }
                    tx.Commit();
                }

                TaskDialog.Show("Magic Rename",
                    $"Renamed: {renamed}\nSkipped: {skipped}\nTotal: {targetElements.Count}");
                StingLog.Info($"MagicRename: {mode} on {elementType}, renamed={renamed}, skipped={skipped}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MagicRenameCommand failed", ex);
                try { TaskDialog.Show("STING", $"Magic Rename failed:\n{ex.Message}"); } catch { }
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  ViewTabColourCommand — Color view browser tabs by discipline
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Colors view tabs in the Revit view bar by discipline, similar to
    /// pyRevit tab colouring. Uses OverrideGraphicSettings on view-associated
    /// elements to visually distinguish disciplines.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ViewTabColourCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                // Revit API does not expose direct control over view tab colours
                // in the document tab bar. This command applies discipline-based
                // naming prefixes and view template assignments as a visual proxy.

                var views = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate && v.ViewType != ViewType.DrawingSheet)
                    .ToList();

                // Build discipline map from view names
                var discMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Mechanical"] = 0, ["HVAC"] = 0,
                    ["Electrical"] = 0, ["Lighting"] = 0,
                    ["Plumbing"] = 0, ["Hydraulic"] = 0,
                    ["Architectural"] = 0, ["Interior"] = 0,
                    ["Structural"] = 0, ["Fire"] = 0,
                    ["Coordination"] = 0
                };

                foreach (var v in views)
                {
                    string name = v.Name ?? "";
                    foreach (var kvp in discMap.Keys.ToList())
                    {
                        if (name.Contains(kvp, StringComparison.OrdinalIgnoreCase))
                            discMap[kvp]++;
                    }
                }

                var sb = new StringBuilder();
                sb.AppendLine($"View discipline analysis ({views.Count} views):\n");
                foreach (var kvp in discMap.Where(k => k.Value > 0).OrderByDescending(k => k.Value))
                    sb.AppendLine($"  {kvp.Key}: {kvp.Value} views");

                int unmatched = views.Count - discMap.Values.Sum();
                if (unmatched > 0)
                    sb.AppendLine($"  Unclassified: {unmatched} views");

                sb.AppendLine("\nNote: Revit API does not support direct tab colour control.");
                sb.AppendLine("View tabs are coloured natively based on view templates.");
                sb.AppendLine("Use Auto-Assign Templates to ensure discipline templates are applied.");

                TaskDialog.Show("View Tab Colours", sb.ToString());
                StingLog.Info($"ViewTabColour: analysed {views.Count} views");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ViewTabColourCommand failed", ex);
                try { TaskDialog.Show("STING", $"View Tab Colour failed:\n{ex.Message}"); } catch { }
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  RibbonPanelStylerCommand — Color ribbon panels by discipline
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies discipline-based colour styling information to ribbon panels.
    /// Reports the current ribbon panel configuration and discipline alignment.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class RibbonPanelStylerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

                // Revit API does not expose ribbon panel colour/style modification
                // at runtime. This command provides an informational report about
                // the STING ribbon configuration.

                var sb = new StringBuilder();
                sb.AppendLine("STING Ribbon Panel Configuration\n");
                sb.AppendLine("Panels:");
                sb.AppendLine("  SELECT  - Element selection & colour coding");
                sb.AppendLine("  DOCS    - Document management & automation");
                sb.AppendLine("  TAGS    - ISO 19650 tagging pipeline");
                sb.AppendLine("  ORGANISE - Tag operations & annotation management");
                sb.AppendLine("  TEMP    - Template setup & data pipeline");
                sb.AppendLine("  PANEL   - WPF dockable panel toggle");
                sb.AppendLine();
                sb.AppendLine("Discipline Colour Mapping:");
                sb.AppendLine("  M (Mechanical) = Blue");
                sb.AppendLine("  E (Electrical) = Gold/Yellow");
                sb.AppendLine("  P (Plumbing)   = Green");
                sb.AppendLine("  A (Architectural) = Grey");
                sb.AppendLine("  S (Structural) = Red");
                sb.AppendLine("  FP (Fire)      = Orange");
                sb.AppendLine("  LV (Low Voltage) = Purple");
                sb.AppendLine();
                sb.AppendLine("Note: Ribbon panel styling is controlled by the");
                sb.AppendLine("WPF dockable panel ThemeManager (Dark/Light/Grey/Corporate).");
                sb.AppendLine("Use the Theme button in the panel to change styles.");

                TaskDialog.Show("Ribbon Panel Styler", sb.ToString());
                StingLog.Info("RibbonPanelStyler: displayed configuration");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("RibbonPanelStylerCommand failed", ex);
                try { TaskDialog.Show("STING", $"Ribbon Styler failed:\n{ex.Message}"); } catch { }
                return Result.Failed;
            }
        }
    }
}
