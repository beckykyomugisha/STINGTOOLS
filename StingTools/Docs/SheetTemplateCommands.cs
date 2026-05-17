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

            // Gather templates: built-in + user-saved + Drawing Type profiles
            // Drawing Type profiles are adapted on the fly via
            // DrawingTypeSheetAdapter so the existing SheetTemplateEngine.
            // CreateSheetFromTemplate pipeline stays the single path.
            var builtIn = SheetTemplateEngine.GetBuiltInTemplates();
            var library = SheetTemplateEngine.LoadTemplateLibrary(doc);
            var allTemplates = new List<SheetTemplate>();
            allTemplates.AddRange(builtIn);
            allTemplates.AddRange(library.Templates.Where(t =>
                !builtIn.Any(b => b.Name.Equals(t.Name, StringComparison.OrdinalIgnoreCase))));

            var items = allTemplates.Select(t => new StingListPicker.ListItem
            {
                Label  = t.Name,
                Detail = $"[Template] {t.Discipline} | {t.PaperSize} | {t.ViewportSlots.Count} slots",
                Tag    = t,
            }).ToList();

            // Append Drawing Type profiles — prefixed so they sort
            // under a distinct group in the picker. The Tag carries
            // the raw DrawingType so the post-pick branch can detect
            // which source produced the selection.
            try
            {
                foreach (var dt in StingTools.Core.Drawing.DrawingTypeRegistry.ListAll(doc)
                             .OrderBy(d => d.Discipline).ThenBy(d => d.Purpose).ThenBy(d => d.Id))
                {
                    items.Add(new StingListPicker.ListItem
                    {
                        Label  = dt.Id,
                        Detail = $"[Profile] {dt.Discipline} / {dt.Purpose} | {dt.PaperSize} @ 1:{dt.Scale} | {(dt.Slots?.Count ?? 0)} slots",
                        Tag    = dt,
                    });
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CreateFromTemplate: DrawingType enumeration failed — {ex.Message}");
            }

            if (items.Count == 0)
            {
                TaskDialog.Show("Sheet Templates", "No templates or Drawing Type profiles available.");
                return Result.Cancelled;
            }

            var picked = StingListPicker.Show("Create Sheet from Template",
                "Select a sheet template or Drawing Type profile", items, false);
            if (picked == null || picked.Count == 0) return Result.Cancelled;

            // Detect which source the user picked. DrawingType → adapt
            // to SheetTemplate via the adapter; SheetTemplate → use as-is.
            StingTools.Core.Drawing.DrawingType pickedDt = picked[0].Tag as StingTools.Core.Drawing.DrawingType;
            SheetTemplate template = picked[0].Tag as SheetTemplate;
            if (pickedDt != null)
            {
                template = DrawingTypeSheetAdapter.ToSheetTemplate(pickedDt);
            }
            if (template == null) return Result.Cancelled;

            // Get unplaced views matching slot types
            var unplaced = SheetManagerEngine.GetUnplacedViews(doc);
            if (unplaced.Count == 0)
            {
                TaskDialog.Show("Sheet Templates", "No unplaced views available to fill template slots.");
                return Result.Cancelled;
            }

            // Get title block — prefer the DrawingType profile's
            // declared family when the pick was a profile and the
            // family is loaded, so corporate consistency wins without
            // asking the user to choose every time.
            var titleBlocks = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>().ToList();

            ElementId tbId = ElementId.InvalidElementId;
            if (pickedDt != null)
            {
                tbId = DrawingTypeSheetAdapter.ResolveTitleBlock(doc, pickedDt);
            }
            if (tbId != ElementId.InvalidElementId)
            {
                // Resolved from profile — skip the picker.
            }
            else if (titleBlocks.Count == 1)
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
                    // DrawingType post-create: stamp the profile id +
                    // apply title-block parameter binding. No-op when
                    // the pick was a legacy SheetTemplate.
                    var warnings = new List<string>();
                    if (pickedDt != null)
                        DrawingTypeSheetAdapter.PostCreate(doc, sheet, pickedDt, warnings);

                    tx.Commit();

                    var msg = $"Created sheet {sheet.SheetNumber} from {(pickedDt != null ? "profile" : "template")} '{template.Name}'.";
                    if (warnings.Count > 0)
                    {
                        msg += $"\n\n{warnings.Count} warning(s):\n  ";
                        msg += string.Join("\n  ", warnings.Take(10));
                        if (warnings.Count > 10) msg += $"\n  …({warnings.Count - 10} more)";
                    }
                    TaskDialog.Show("Sheet Templates", msg);
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

            var td = new TaskDialog("Save Sheet Template")
            {
                MainInstruction = "Save this sheet as a reusable template?",
                MainContent = $"Sheet: {view.SheetNumber} - {view.Name}\nViewports: {view.GetAllViewports().Count}",
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel
            };
            if (td.Show() != TaskDialogResult.Ok) return Result.Cancelled;

            string templateName = view.Name + " Template";
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
            var failIds = new List<ElementId>();
            foreach (var r in results.Where(r => !r.IsCompliant))
            {
                var sheet = new FilteredElementCollector(ctx.Doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .FirstOrDefault(s => s.SheetNumber == r.SheetNumber);
                if (sheet != null) failIds.Add(sheet.Id);
            }

            if (failIds.Count > 0 && ctx.UIDoc != null)
            {
                var td2 = new TaskDialog("Compliance Results")
                {
                    MainInstruction = $"{failIds.Count} non-compliant sheets found.",
                    CommonButtons = TaskDialogCommonButtons.Close
                };
                td2.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    $"Select {failIds.Count} non-compliant sheets");
                if (td2.Show() == TaskDialogResult.CommandLink1)
                    ctx.UIDoc.Selection.SetElementIds(failIds);
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

            // Pick alignment direction — StingModePicker.Show returns string (Tag)
            var modes = new List<StingModePicker.ModeOption>
            {
                new StingModePicker.ModeOption("Left", "Align left edges", "LEFT"),
                new StingModePicker.ModeOption("Right", "Align right edges", "RIGHT"),
                new StingModePicker.ModeOption("Top", "Align top edges", "TOP"),
                new StingModePicker.ModeOption("Bottom", "Align bottom edges", "BOTTOM"),
                new StingModePicker.ModeOption("Center H", "Centre horizontally", "CENTER_H"),
                new StingModePicker.ModeOption("Center V", "Centre vertically", "CENTER_V")
            };

            string edge = StingModePicker.Show("Align Viewport Edges", "Select alignment direction", modes);
            if (edge == null) return Result.Cancelled;

            using (var tx = new Transaction(doc, "STING Align Viewport Edges"))
            {
                tx.Start();
                int count = SheetTemplateEngine.AlignViewportEdges(doc, sheet, edge);
                tx.Commit();
                TaskDialog.Show("Align Edges", $"Aligned {count} viewport(s) to {edge} edge.");
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
                new StingModePicker.ModeOption("Horizontal", "Distribute evenly left to right", "H"),
                new StingModePicker.ModeOption("Vertical", "Distribute evenly top to bottom", "V")
            };

            string dir = StingModePicker.Show("Distribute Viewports", "Select distribution direction", modes);
            if (dir == null) return Result.Cancelled;

            bool horizontal = dir == "H";

            using (var tx = new Transaction(doc, "STING Distribute Viewports"))
using StingTools.Core.Drawing;
            {
                tx.Start();
                int count = SheetTemplateEngine.DistributeViewports(doc, sheet, horizontal);
                tx.Commit();
                TaskDialog.Show("Distribute",
                    $"Distributed {count} viewport(s) {(horizontal ? "horizontally" : "vertically")}.");
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
                new StingModePicker.ModeOption("All Sheets", "Export all sheets to PDF", "ALL"),
                new StingModePicker.ModeOption("By Discipline", "Export sheets for one discipline", "DISC"),
                new StingModePicker.ModeOption("Selected Sheets", "Pick sheets to export", "SEL")
            };

            string scope = StingModePicker.Show("Batch Print / PDF", "Select export scope", modes);
            if (scope == null) return Result.Cancelled;

            string outputDir = OutputLocationHelper.PromptForExportPath(doc,
                $"SheetPDF_{DateTime.Now:yyyyMMdd}.pdf", "PDF Files|*.pdf", "SheetPDF");
            if (string.IsNullOrEmpty(outputDir)) return Result.Cancelled;

            // Use directory portion only
            outputDir = Path.GetDirectoryName(outputDir) ?? outputDir;

            int exported = 0;

            if (scope == "ALL")
            {
                var sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => !s.IsPlaceholder)
                    .OrderBy(s => s.SheetNumber)
                    .ToList();
                // Phase 97 — spec §7.8 PreExportValidate gate
                if (!PreExportValidateGate.CheckOrAbort(doc, sheets)) return Result.Cancelled;
                exported = SheetTemplateEngine.ExportSheetsToPDF(doc, sheets, outputDir);
            }
            else if (scope == "DISC")
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

                string discPick = StingListPicker.Show("Select Discipline", "Pick discipline to export", disciplines);
                if (discPick == null) return Result.Cancelled;
                // Phase 97 — spec §7.8 PreExportValidate gate (discipline-scoped)
                var discSheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => !s.IsPlaceholder
                        && string.Equals(SheetManagerEngine.ExtractDisciplinePrefix(s.SheetNumber),
                            discPick, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (!PreExportValidateGate.CheckOrAbort(doc, discSheets)) return Result.Cancelled;
                exported = SheetTemplateEngine.ExportDisciplineToPDF(doc, discPick, outputDir);
            }
            else // SEL
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
                // Phase 97 — spec §7.8 PreExportValidate gate (selection-scoped)
                if (!PreExportValidateGate.CheckOrAbort(doc, selectedSheets)) return Result.Cancelled;
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

            string filePath = OutputLocationHelper.PromptForExportPath(doc,
                $"SheetRegister_{DateTime.Now:yyyyMMdd_HHmmss}.csv", "CSV Files|*.csv", "SheetRegister");
            if (string.IsNullOrEmpty(filePath)) return Result.Cancelled;

            SheetTemplateEngine.ExportSheetRegister(doc, filePath);

            TaskDialog.Show("Sheet Register", $"Sheet register exported to:\n{filePath}");
            return Result.Succeeded;
        }
    }
}
