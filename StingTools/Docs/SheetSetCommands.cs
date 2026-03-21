using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Select;
using StingTools.UI;

namespace StingTools.Docs
{
    // ════════════════════════════════════════════════════════════════════════════
    //  SHEET SET COMMANDS — Phase 2
    //  Batch operations, layout presets, viewport type management, and export.
    //
    //  Commands:
    //    1. MaxRectsLayoutCommand       — Auto-layout using MaxRects algorithm
    //    2. SaveLayoutPresetCommand     — Save current sheet layout as preset
    //    3. ApplyLayoutPresetCommand    — Apply saved preset to active sheet
    //    4. BatchCloneSheetsCommand     — Clone multiple sheets at once
    //    5. BatchRenumberSheetsCommand  — Renumber sheets within a discipline
    //    6. AutoAssignVPTypesCommand    — Auto-assign viewport types by rules
    //    7. ExportSheetSetCommand       — Export sheet set to CSV
    //    8. PlaceWithOverflowCommand    — Place views with auto-overflow to new sheets
    // ════════════════════════════════════════════════════════════════════════════

    // ── 1. MaxRects Layout ───────────────────────────────────────────────

    /// <summary>
    /// Auto-arrange viewports on active sheet using Maximal Rectangles algorithm.
    /// Better packing density than shelf packing for irregular viewport sizes.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MaxRectsLayoutCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            if (!(ctx.ActiveView is ViewSheet sheet))
            {
                TaskDialog.Show("MaxRects Layout", "Navigate to a sheet view first.");
                return Result.Succeeded;
            }

            var vpIds = sheet.GetAllViewports().ToList();
            if (vpIds.Count == 0)
            {
                TaskDialog.Show("MaxRects Layout", "Sheet has no viewports to arrange.");
                return Result.Succeeded;
            }

            // Collect views from existing viewports
            var views = new List<View>();
            var vpData = new Dictionary<ElementId, ElementId>(); // vpId → typeId
            doc.Regenerate();

            foreach (var vpId in vpIds)
            {
                var vp = doc.GetElement(vpId) as Viewport;
                if (vp == null) continue;
                var view = doc.GetElement(vp.ViewId) as View;
                if (view == null) continue;
                views.Add(view);
                vpData[vp.ViewId] = vp.GetTypeId();
            }

            var zone = SheetManagerEngine.GetDrawableZone(doc, sheet);
            var layout = SheetManagerEngineExt.RunMaxRectsPacking(doc, views, zone);

            using (var tx = new Transaction(doc, "STING MaxRects Layout"))
            {
                tx.Start();

                // Delete existing viewports
                foreach (var vpId in vpIds)
                    doc.Delete(vpId);

                // Place at new positions
                var placed = SheetManagerEngine.PlaceViewports(doc, sheet, layout, setScale: false);

                // Restore viewport types
                doc.Regenerate();
                foreach (var newVpId in placed)
                {
                    var vp = doc.GetElement(newVpId) as Viewport;
                    if (vp == null) continue;
                    if (vpData.TryGetValue(vp.ViewId, out var typeId) && typeId != ElementId.InvalidElementId)
                    {
                        try { vp.ChangeTypeId(typeId); }
                        catch (Exception) { /* type may not exist */ }
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("MaxRects Layout", layout.Summary);
            return Result.Succeeded;
        }
    }

    // ── 2. Save Layout Preset ────────────────────────────────────────────

    /// <summary>
    /// Save the current viewport arrangement on the active sheet as a named layout preset.
    /// Presets store normalised positions that transfer between paper sizes.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SaveLayoutPresetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            if (!(ctx.ActiveView is ViewSheet sheet))
            {
                TaskDialog.Show("Save Layout", "Navigate to a sheet view first.");
                return Result.Succeeded;
            }

            if (sheet.GetAllViewports().Count == 0)
            {
                TaskDialog.Show("Save Layout", "Sheet has no viewports to save.");
                return Result.Succeeded;
            }

            // Get preset name from user
            var td = new TaskDialog("Save Layout Preset");
            td.MainInstruction = $"Save layout from sheet '{sheet.SheetNumber}'";
            td.MainContent = $"Viewports: {sheet.GetAllViewports().Count}\n\n" +
                "Enter a name for this preset in the footer input, or use the default name.";
            td.FooterText = $"Layout_{sheet.SheetNumber}_{DateTime.Now:yyyyMMdd}";
            td.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            td.DefaultButton = TaskDialogResult.Ok;

            if (td.Show() == TaskDialogResult.Cancel) return Result.Cancelled;

            string presetName = td.FooterText;
            if (string.IsNullOrWhiteSpace(presetName))
                presetName = $"Layout_{sheet.SheetNumber}";

            var preset = SheetManagerEngineExt.SaveLayoutPreset(doc, sheet, presetName);

            TaskDialog.Show("Save Layout",
                $"Saved preset '{preset.Name}' with {preset.Slots.Count} viewport positions.\n" +
                $"Paper size: {preset.PaperSize}");

            return Result.Succeeded;
        }
    }


    // ── 3. Apply Layout Preset ───────────────────────────────────────────

    /// <summary>
    /// Apply a saved layout preset to the active sheet, repositioning viewports
    /// to match the saved normalised positions.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ApplyLayoutPresetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            if (!(ctx.ActiveView is ViewSheet sheet))
            {
                TaskDialog.Show("Apply Layout", "Navigate to a sheet view first.");
                return Result.Succeeded;
            }

            // Load available presets (built-in + saved)
            var builtIn = SheetManagerEngineExt.GetBuiltInPresets();
            var library = SheetManagerEngineExt.LoadPresetLibrary(doc);
            var allPresets = new List<LayoutPreset>(builtIn);
            allPresets.AddRange(library.Presets);

            if (allPresets.Count == 0)
            {
                TaskDialog.Show("Apply Layout", "No layout presets available. Save a layout first.");
                return Result.Succeeded;
            }

            // Pick preset
            var options = allPresets.Select(p =>
                $"{p.Name} ({p.Slots.Count} slots) — {p.Description ?? p.PaperSize}").ToList();

            string picked = StingListPicker.Show("Apply Layout Preset",
                "Select a layout preset to apply:", options);
            if (picked == null) return Result.Cancelled;

            int idx = options.IndexOf(picked);
            if (idx < 0) return Result.Cancelled;

            var preset = allPresets[idx];

            using (var tx = new Transaction(doc, "STING Apply Layout Preset"))
            {
                tx.Start();
                int moved = SheetManagerEngineExt.ApplyLayoutPreset(doc, sheet, preset);
                tx.Commit();

                TaskDialog.Show("Apply Layout",
                    $"Applied preset '{preset.Name}'.\n" +
                    $"Repositioned {moved} viewports.");
            }
            return Result.Succeeded;
        }
    }

    // ── 4. Batch Clone Sheets ────────────────────────────────────────────

    /// <summary>
    /// Clone multiple selected sheets at once with options for view duplication.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchCloneSheetsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber)
                .ToList();

            if (sheets.Count == 0)
            {
                TaskDialog.Show("Batch Clone", "No sheets found in the project.");
                return Result.Succeeded;
            }

            // Pick sheets to clone
            var listItems = sheets.Select(s =>
                new StingListPicker.ListItem { Label = $"{s.SheetNumber} - {s.Name}" }).ToList();
            var picked = StingListPicker.Show("Batch Clone Sheets",
                "Select sheets to clone:", listItems, allowMultiSelect: true);
            if (picked == null || picked.Count == 0) return Result.Cancelled;

            var pickedLabels = new HashSet<string>(picked.Select(p => p.Label));
            var selectedSheets = new List<ViewSheet>();
            for (int i = 0; i < sheets.Count; i++)
            {
                if (pickedLabels.Contains(listItems[i].Label))
                    selectedSheets.Add(sheets[i]);
            }

            // Clone mode
            var modes = new List<StingModePicker.ModeOption>
            {
                new StingModePicker.ModeOption { Label = "Duplicate views", Description = "Create copies of all views (with detailing)", Tag = "dup", IsRecommended = true },
                new StingModePicker.ModeOption { Label = "Reference views", Description = "Reference same views (no duplication)", Tag = "ref" },
            };

            string mode = StingModePicker.Show("Batch Clone",
                $"Clone {selectedSheets.Count} sheets — select mode:", modes);
            if (mode == null) return Result.Cancelled;

            using (var tx = new Transaction(doc, "STING Batch Clone Sheets"))
            {
                tx.Start();
                var cloned = SheetManagerEngineExt.BatchCloneSheets(doc, selectedSheets,
                    duplicateViews: mode == "dup");
                tx.Commit();

                TaskDialog.Show("Batch Clone",
                    $"Cloned {cloned.Count} of {selectedSheets.Count} sheets.\n" +
                    $"Mode: {(mode == "dup" ? "Duplicate views" : "Reference views")}");
            }
            return Result.Succeeded;
        }
    }

    // ── 5. Batch Renumber Sheets ─────────────────────────────────────────

    /// <summary>
    /// Sequentially renumber all sheets within a selected discipline group.
    /// Uses two-pass rename (temporary → final) to avoid number conflicts.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchRenumberSheetsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Get disciplines present in project
            var byDisc = SheetManagerEngine.GetSheetsByDiscipline(doc);
            if (byDisc.Count == 0)
            {
                TaskDialog.Show("Batch Renumber", "No sheets found.");
                return Result.Succeeded;
            }

            var discOptions = byDisc.Select(kv => $"{kv.Key} ({kv.Value.Count} sheets)").ToList();
            string picked = StingListPicker.Show("Batch Renumber",
                "Select discipline group to renumber:", discOptions);
            if (picked == null) return Result.Cancelled;

            string disc = picked.Split(' ')[0];

            // Starting number
            var td = new TaskDialog("Batch Renumber");
            td.MainInstruction = $"Renumber {byDisc[disc].Count} sheets in discipline '{disc}'";
            td.MainContent = $"Sheets will be renumbered as {disc}-001, {disc}-002, etc.\n" +
                "This uses a two-pass rename to avoid conflicts.";
            td.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;

            if (td.Show() == TaskDialogResult.Cancel) return Result.Cancelled;

            using (var tx = new Transaction(doc, "STING Batch Renumber"))
            {
                tx.Start();
                int renamed = SheetManagerEngineExt.BatchRenumberSheets(doc, disc);
                tx.Commit();

                TaskDialog.Show("Batch Renumber", $"Renumbered {renamed} sheets in discipline '{disc}'.");
            }
            return Result.Succeeded;
        }
    }


    // ── 6. Auto-Assign Viewport Types ────────────────────────────────────

    /// <summary>
    /// Auto-assign viewport types to all viewports on the active sheet
    /// based on view type and discipline rules.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoAssignVPTypesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Choose scope
            var scopes = new List<StingModePicker.ModeOption>
            {
                new StingModePicker.ModeOption { Label = "Active sheet only", Description = "Apply rules to viewports on the current sheet", Tag = "active", IsRecommended = true },
                new StingModePicker.ModeOption { Label = "All sheets", Description = "Apply rules to ALL viewports in the project", Tag = "all" },
            };

            string scope = StingModePicker.Show("Auto-Assign VP Types",
                "Select scope for viewport type assignment:", scopes);
            if (scope == null) return Result.Cancelled;

            List<ViewSheet> targetSheets;
            if (scope == "active")
            {
                if (!(ctx.ActiveView is ViewSheet sheet))
                {
                    TaskDialog.Show("Auto-Assign VP Types", "Navigate to a sheet view first.");
                    return Result.Succeeded;
                }
                targetSheets = new List<ViewSheet> { sheet };
            }
            else
            {
                targetSheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => !s.IsPlaceholder && s.GetAllViewports().Count > 0)
                    .ToList();
            }

            int totalChanged = 0;

            using (var tx = new Transaction(doc, "STING Auto-Assign VP Types"))
            {
                tx.Start();
                foreach (var sheet in targetSheets)
                {
                    int changed = SheetManagerEngineExt.AutoAssignViewportTypes(doc, sheet);
                    totalChanged += changed;
                }
                tx.Commit();
            }

            TaskDialog.Show("Auto-Assign VP Types",
                $"Changed {totalChanged} viewport types across {targetSheets.Count} sheets.\n\n" +
                "Note: If STING viewport types are not loaded in the project, no changes will be made. " +
                "Load viewport family types first.");
            return Result.Succeeded;
        }
    }

    // ── 7. Export Sheet Set ──────────────────────────────────────────────

    /// <summary>
    /// Export the complete sheet set to a CSV file.
    /// Includes sheet number, name, discipline, paper size, viewports, and title block.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportSheetSetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            string outputPath = OutputLocationHelper.GetTimestampedPath(doc, "SheetSet", ".csv");

            try
            {
                SheetManagerEngineExt.ExportSheetSet(doc, outputPath);

                var td = new TaskDialog("Export Sheet Set");
                td.MainInstruction = "Sheet set exported successfully";
                td.MainContent = $"File: {outputPath}";
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Open file", "Open the CSV in the default application");
                td.CommonButtons = TaskDialogCommonButtons.Close;

                if (td.Show() == TaskDialogResult.CommandLink1)
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(outputPath) { UseShellExecute = true }); }
                    catch (Exception) { /* file may not have an associated app */ }
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Export Sheet Set", $"Export failed: {ex.Message}");
                StingLog.Error("Sheet set export failed", ex);
            }

            return Result.Succeeded;
        }
    }

    // ── 8. Place With Overflow ───────────────────────────────────────────

    /// <summary>
    /// Place unplaced views on the active sheet with automatic overflow
    /// to continuation sheets when the current sheet fills up.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceWithOverflowCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            if (!(ctx.ActiveView is ViewSheet sheet))
            {
                TaskDialog.Show("Place with Overflow", "Navigate to a sheet view first.");
                return Result.Succeeded;
            }

            var unplaced = SheetManagerEngine.GetUnplacedViews(doc);
            if (unplaced.Count == 0)
            {
                TaskDialog.Show("Place with Overflow", "All views are already placed on sheets.");
                return Result.Succeeded;
            }

            // Pick views to place
            var viewItems = unplaced.Select(v =>
                new StingListPicker.ListItem { Label = $"{v.ViewType,-15} {v.Name} (1:{v.Scale})" }).ToList();
            var picked = StingListPicker.Show("Place with Overflow",
                "Select views to place (overflow creates new sheets):", viewItems, allowMultiSelect: true);
            if (picked == null || picked.Count == 0) return Result.Cancelled;

            var pickedLabels = new HashSet<string>(picked.Select(p => p.Label));
            var selectedViews = new List<View>();
            for (int i = 0; i < unplaced.Count; i++)
            {
                if (pickedLabels.Contains(viewItems[i].Label))
                    selectedViews.Add(unplaced[i]);
            }

            // Algorithm choice
            var algos = new List<StingModePicker.ModeOption>
            {
                new StingModePicker.ModeOption { Label = "MaxRects (best density)", Description = "Maximal Rectangles bin packing for optimal space utilisation", Tag = "maxrects", IsRecommended = true },
                new StingModePicker.ModeOption { Label = "Shelf packing (fast)", Description = "Row-based packing, faster but less efficient", Tag = "shelf" },
            };

            string algo = StingModePicker.Show("Place with Overflow",
                $"Place {selectedViews.Count} views — select algorithm:", algos);
            if (algo == null) return Result.Cancelled;

            // Find title block type for overflow sheets
            var tb = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Cast<FamilyInstance>()
                .FirstOrDefault();

            ElementId tbTypeId = tb?.GetTypeId() ?? ElementId.InvalidElementId;
            if (tbTypeId == ElementId.InvalidElementId)
            {
                var anyTb = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsElementType()
                    .FirstOrDefault();
                tbTypeId = anyTb?.Id ?? ElementId.InvalidElementId;
            }

            if (tbTypeId == ElementId.InvalidElementId)
            {
                TaskDialog.Show("Place with Overflow", "No title block type available for overflow sheets.");
                return Result.Succeeded;
            }

            using (var tx = new Transaction(doc, "STING Place with Overflow"))
            {
                tx.Start();
                var (sheetsCreated, placed) = SheetManagerEngineExt.PlaceWithOverflow(
                    doc, selectedViews, sheet, tbTypeId,
                    useMaxRects: algo == "maxrects", autoScale: true);
                tx.Commit();

                TaskDialog.Show("Place with Overflow",
                    $"Placed {placed} of {selectedViews.Count} views.\n" +
                    $"Overflow sheets created: {sheetsCreated}");
            }
            return Result.Succeeded;
        }
    }
}
