using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Select;

namespace StingTools.Docs
{
    // ════════════════════════════════════════════════════════════════════════════
    //  SHEET TEMPLATE COMMANDS — Phase 3
    //  Commands for sheet templates, ISO compliance, grid alignment, print/export.
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a new sheet from a built-in or saved template,
    /// matching available views to template slots.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateFromTemplateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx.Doc == null) return Result.Failed;
            var doc = ctx.Doc;

            // Gather templates: built-in + user-saved
            var builtIn = SheetTemplateEngine.GetBuiltInTemplates();
            var library = SheetTemplateEngine.LoadTemplateLibrary(doc);
            var allTemplates = new List<SheetTemplate>();
            allTemplates.AddRange(builtIn);
            allTemplates.AddRange(library.Templates.Where(t =>
                !builtIn.Any(b => b.Name.Equals(t.Name, StringComparison.OrdinalIgnoreCase))));

            if (allTemplates.Count == 0)
            {
                TaskDialog.Show("Sheet Templates", "No templates available.");
                return Result.Cancelled;
            }

            // Pick template
            var items = allTemplates.Select(t => new StingListPicker.ListItem
            {
                Label = t.Name,
                Detail = $"{t.Discipline} | {t.PaperSize} | {t.ViewportSlots.Count} slots",
                Tag = t
            }).ToList();

            var picked = StingListPicker.Show("Create Sheet from Template",
                "Select a sheet template", items, false);
            if (picked == null || picked.Count == 0) return Result.Cancelled;

            var template = picked[0].Tag as SheetTemplate;
            if (template == null) return Result.Cancelled;

            // Get unplaced views matching slot types
            var unplaced = SheetManagerEngine.GetUnplacedViews(doc);
            if (unplaced.Count == 0)
            {
                TaskDialog.Show("Sheet Templates", "No unplaced views available to fill template slots.");
                return Result.Cancelled;
            }

            // Get title block
            var titleBlocks = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>().ToList();

            ElementId tbId = ElementId.InvalidElementId;
            if (titleBlocks.Count == 1)
            {
                tbId = titleBlocks[0].Id;
            }
            else if (titleBlocks.Count > 1)
            {
                var tbItems = titleBlocks.Select(tb => new StingListPicker.ListItem
                {
                    Label = $"{tb.FamilyName}: {tb.Name}",
                    Tag = tb
                }).ToList();
                var tbPick = StingListPicker.Show("Title Block", "Select title block type", tbItems, false);
                if (tbPick == null || tbPick.Count == 0) return Result.Cancelled;
                tbId = (tbPick[0].Tag as FamilySymbol)?.Id ?? ElementId.InvalidElementId;
            }
            else
            {
                TaskDialog.Show("Sheet Templates", "No title block families loaded.");
                return Result.Cancelled;
            }

            using (var tx = new Transaction(doc, "STING Create Sheet from Template"))
            {
                tx.Start();
                var sheet = SheetTemplateEngine.CreateSheetFromTemplate(doc, template, unplaced, tbId);
                if (sheet != null)
                {
                    tx.Commit();
                    TaskDialog.Show("Sheet Templates",
                        $"Created sheet {sheet.SheetNumber} from template '{template.Name}'.");
                    return Result.Succeeded;
                }
                tx.RollBack();
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Save current sheet layout as a reusable template.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SaveSheetTemplateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx.Doc == null) return Result.Failed;
            var doc = ctx.Doc;
            var view = doc.ActiveView as ViewSheet;

            if (view == null)
            {
                TaskDialog.Show("Save Template", "Active view must be a sheet.");
                return Result.Cancelled;
            }

            // Get template name from user
            var td = new TaskDialog("Save Sheet Template")
            {
                MainInstruction = "Enter a name for this template:",
                MainContent = $"Sheet: {view.SheetNumber} - {view.Name}\nViewports: {view.GetAllViewports().Count}",
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Save as: " + view.Name + " Template");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Save with custom name...");
            var result = td.Show();

            string templateName;
            if (result == TaskDialogResult.CommandLink1)
            {
                templateName = view.Name + " Template";
            }
            else if (result == TaskDialogResult.CommandLink2)
            {
                templateName = view.Name + " Template"; // Default; Revit API has no text input dialog
            }
            else
            {
                return Result.Cancelled;
            }

            var template = SheetTemplateEngine.SaveTemplateFromSheet(doc, view, templateName);

            TaskDialog.Show("Save Template",
                $"Template '{template.Name}' saved with {template.ViewportSlots.Count} viewport slots.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Run ISO 19650 compliance check on all sheets.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetComplianceCheckCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx.Doc == null) return Result.Failed;

            var results = SheetTemplateEngine.CheckCompliance(ctx.Doc);
            string report = SheetTemplateEngine.BuildComplianceReport(results);

            TaskDialog.Show("ISO 19650 Sheet Compliance", report);

            // Offer to select non-compliant sheets
            var failedIds = new List<ElementId>();
            foreach (var r in results.Where(r => !r.IsCompliant))
            {
                var sheet = new FilteredElementCollector(ctx.Doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .FirstOrDefault(s => s.SheetNumber == r.SheetNumber);
                if (sheet != null) failedIds.Add(sheet.Id);
            }

            if (failedIds.Count > 0 && ctx.UIDoc != null)
            {
                var td = new TaskDialog("Compliance Results")
                {
                    MainInstruction = $"{failedIds.Count} non-compliant sheets found.",
                    CommonButtons = TaskDialogCommonButtons.Close
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    $"Select {failedIds.Count} non-compliant sheets");
                if (td.Show() == TaskDialogResult.CommandLink1)
                    ctx.UIDoc.Selection.SetElementIds(failedIds);
            }

            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Snap viewport positions to an alignment grid on the active sheet.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class GridAlignViewportsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx.Doc == null) return Result.Failed;
            var doc = ctx.Doc;
            var sheet = doc.ActiveView as ViewSheet;

            if (sheet == null)
            {
                TaskDialog.Show("Grid Align", "Active view must be a sheet.");
                return Result.Cancelled;
            }

            var zone = SheetManagerEngine.GetDrawableZone(doc, sheet);
            var grid = SheetTemplateEngine.BuildAlignmentGrid(zone);

            using (var tx = new Transaction(doc, "STING Grid Align Viewports"))
            {
                tx.Start();
                int count = SheetTemplateEngine.SnapViewportsToGrid(doc, sheet, grid);
                tx.Commit();
                TaskDialog.Show("Grid Align", $"Adjusted {count} viewport(s) to grid positions.");
            }
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Align viewport edges on the active sheet (left, right, top, bottom, center).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignViewportEdgesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx.Doc == null) return Result.Failed;
            var doc = ctx.Doc;
            var sheet = doc.ActiveView as ViewSheet;

            if (sheet == null)
            {
                TaskDialog.Show("Align Edges", "Active view must be a sheet.");
                return Result.Cancelled;
            }

            if (sheet.GetAllViewports().Count < 2)
            {
                TaskDialog.Show("Align Edges", "Sheet must have at least 2 viewports.");
                return Result.Cancelled;
            }

            // Pick alignment direction
            var modes = new List<StingModePicker.ModeOption>
            {
                new StingModePicker.ModeOption { Label = "Left", Description = "Align left edges" },
                new StingModePicker.ModeOption { Label = "Right", Description = "Align right edges" },
                new StingModePicker.ModeOption { Label = "Top", Description = "Align top edges" },
                new StingModePicker.ModeOption { Label = "Bottom", Description = "Align bottom edges" },
                new StingModePicker.ModeOption { Label = "Center H", Description = "Centre horizontally" },
                new StingModePicker.ModeOption { Label = "Center V", Description = "Centre vertically" }
            };

            var mode = StingModePicker.Show("Align Viewport Edges", "Select alignment direction", modes);
            if (mode == null) return Result.Cancelled;

            string edge = mode.Label.Replace(" ", "_").ToUpperInvariant();

            using (var tx = new Transaction(doc, "STING Align Viewport Edges"))
            {
                tx.Start();
                int count = SheetTemplateEngine.AlignViewportEdges(doc, sheet, edge);
                tx.Commit();
                TaskDialog.Show("Align Edges", $"Aligned {count} viewport(s) to {mode.Label}.");
            }
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Distribute viewports evenly across the sheet (horizontal or vertical).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DistributeViewportsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx.Doc == null) return Result.Failed;
            var doc = ctx.Doc;
            var sheet = doc.ActiveView as ViewSheet;

            if (sheet == null)
            {
                TaskDialog.Show("Distribute", "Active view must be a sheet.");
                return Result.Cancelled;
            }

            if (sheet.GetAllViewports().Count < 3)
            {
                TaskDialog.Show("Distribute", "Sheet must have at least 3 viewports.");
                return Result.Cancelled;
            }

            var modes = new List<StingModePicker.ModeOption>
            {
                new StingModePicker.ModeOption { Label = "Horizontal", Description = "Distribute evenly left to right" },
                new StingModePicker.ModeOption { Label = "Vertical", Description = "Distribute evenly top to bottom" }
            };

            var mode = StingModePicker.Show("Distribute Viewports", "Select distribution direction", modes);
            if (mode == null) return Result.Cancelled;

            bool horizontal = mode.Label == "Horizontal";

            using (var tx = new Transaction(doc, "STING Distribute Viewports"))
            {
                tx.Start();
                int count = SheetTemplateEngine.DistributeViewports(doc, sheet, horizontal);
                tx.Commit();
                TaskDialog.Show("Distribute", $"Distributed {count} viewport(s) {mode.Label.ToLower()}ly.");
            }
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Export selected or all sheets to PDF using Revit's built-in PDF export.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchPrintSheetsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx.Doc == null) return Result.Failed;
            var doc = ctx.Doc;

            // Pick scope
            var modes = new List<StingModePicker.ModeOption>
            {
                new StingModePicker.ModeOption { Label = "All Sheets", Description = "Export all sheets to PDF" },
                new StingModePicker.ModeOption { Label = "By Discipline", Description = "Export sheets for one discipline" },
                new StingModePicker.ModeOption { Label = "Selected Sheets", Description = "Pick sheets to export" }
            };

            var mode = StingModePicker.Show("Batch Print / PDF", "Select export scope", modes);
            if (mode == null) return Result.Cancelled;

            string outputDir = OutputLocationHelper.PromptForExportPath("SheetPDF",
                Path.GetDirectoryName(doc.PathName) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            if (string.IsNullOrEmpty(outputDir)) return Result.Cancelled;

            int exported = 0;

            if (mode.Label == "All Sheets")
            {
                var sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => !s.IsPlaceholder)
                    .OrderBy(s => s.SheetNumber)
                    .ToList();
                exported = SheetTemplateEngine.ExportSheetsToPDF(doc, sheets, outputDir);
            }
            else if (mode.Label == "By Discipline")
            {
                var disciplines = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => !s.IsPlaceholder)
                    .Select(s => SheetManagerEngine.ExtractDisciplinePrefix(s.SheetNumber))
                    .Where(d => !string.IsNullOrEmpty(d))
                    .Distinct()
                    .OrderBy(d => d)
                    .ToList();

                if (disciplines.Count == 0)
                {
                    TaskDialog.Show("Batch Print", "No discipline prefixes found in sheet numbers.");
                    return Result.Cancelled;
                }

                var discPick = StingListPicker.Show("Select Discipline", "Pick discipline to export", disciplines);
                if (discPick == null) return Result.Cancelled;
                exported = SheetTemplateEngine.ExportDisciplineToPDF(doc, discPick, outputDir);
            }
            else // Selected Sheets
            {
                var allSheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => !s.IsPlaceholder)
                    .OrderBy(s => s.SheetNumber)
                    .ToList();

                var sheetItems = allSheets.Select(s => new StingListPicker.ListItem
                {
                    Label = $"{s.SheetNumber} - {s.Name}",
                    Tag = s
                }).ToList();

                var picked = StingListPicker.Show("Select Sheets", "Pick sheets to export",
                    sheetItems, true);
                if (picked == null || picked.Count == 0) return Result.Cancelled;

                var selectedSheets = picked.Select(p => p.Tag as ViewSheet).Where(s => s != null).ToList();
                exported = SheetTemplateEngine.ExportSheetsToPDF(doc, selectedSheets, outputDir);
            }

            TaskDialog.Show("Batch Print",
                $"Exported {exported} sheet(s) to PDF.\nOutput: {outputDir}");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Export a comprehensive sheet register to CSV including compliance status.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportSheetRegisterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx.Doc == null) return Result.Failed;
            var doc = ctx.Doc;

            string outputDir = OutputLocationHelper.PromptForExportPath("SheetRegister",
                Path.GetDirectoryName(doc.PathName) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            if (string.IsNullOrEmpty(outputDir)) return Result.Cancelled;

            string filePath = Path.Combine(outputDir,
                $"SheetRegister_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            SheetTemplateEngine.ExportSheetRegister(doc, filePath);

            TaskDialog.Show("Sheet Register", $"Sheet register exported to:\n{filePath}");
            return Result.Succeeded;
        }
    }
}
