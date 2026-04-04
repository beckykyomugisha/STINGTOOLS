using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.ExLink
{
    // ════════════════════════════════════════════════════════════════════════
    //  EXLINK COMMANDS — 13 IExternalCommand classes for Ideate-style
    //  .link file data exchange
    //
    //   1. ExLinkBrowserCommand        — browse .link files, select + execute
    //   2. ExLinkExportCommand         — export via selected .link to Excel
    //   3. ExLinkImportCommand         — import from Excel via .link
    //   4. ExLinkMultiExportCommand    — export multiple .link files at once
    //   5. ExLinkQuickViewCommand      — preview elements a .link will capture
    //   6. ExLinkBatchExportCommand    — export ALL .link files to a folder
    //   7. ExLinkCustomLinkCommand     — create a custom .link definition
    //   8. ExLinkQTOCommand            — quantity take-off export
    //   9. ExLinkDocIssuanceCommand    — document issuance export
    //  10. ExLinkCOBieSyncCommand      — COBie data sync via .link
    //  11. ExLinkDynamicPDFCommand     — export sheets to PDF
    //  12. ExLinkDynamicDWGCommand     — export sheets to DWG
    //  13. ExLinkDynamicNWCCommand     — export to NWC (Navisworks)
    // ════════════════════════════════════════════════════════════════════════

    // ── 1. Browser ──
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExLinkBrowserCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var app = ParameterHelpers.GetApp(commandData);
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { message = "No active document."; return Result.Failed; }

            var files = ExLinkEngine.BrowseLinkFiles();
            if (files.Count == 0)
            {
                TaskDialog.Show("STING — ExLink Browser", "No .link files found in the Data/ExLink folder.\n\nPlace .link definition files in the Data/ExLink/ directory.");
                return Result.Succeeded;
            }

            var selected = ExLinkBrowserDialog.ShowDialog(files);
            if (string.IsNullOrEmpty(selected)) return Result.Succeeded;

            // Parse and export
            var def = ExLinkEngine.ParseLinkFile(selected);
            var elems = ExLinkEngine.CollectElements(doc, def);

            TaskDialog.Show("STING — ExLink Browser",
                $"Link: {def.FileName}\n" +
                $"Element Type: {def.ElementType}\n" +
                $"Properties: {def.Properties.Count}\n" +
                $"Filters: {def.Filters.Count}\n" +
                $"Matched Elements: {elems.Count}\n\n" +
                "Use Export or Import commands to exchange data.");
            return Result.Succeeded;
        }
    }

    // ── 2. Export ──
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExLinkExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var app = ParameterHelpers.GetApp(commandData);
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { message = "No active document."; return Result.Failed; }

            // Pick .link file
            var linkPath = ExLinkHelpers.PickLinkFile("Select .link for Export");
            if (string.IsNullOrEmpty(linkPath)) return Result.Succeeded;

            var def = ExLinkEngine.ParseLinkFile(linkPath);
            var outputPath = ExLinkHelpers.PickSavePath(def.FileName, "xlsx");
            if (string.IsNullOrEmpty(outputPath)) return Result.Succeeded;

            var result = ExLinkEngine.ExportToExcel(doc, def, outputPath);
            var msg = result.Success
                ? $"Exported {result.RowCount} rows × {result.ColumnCount} columns.\n\n{outputPath}"
                : $"Export failed.\n{string.Join("\n", result.Warnings)}";
            TaskDialog.Show("STING — ExLink Export", msg);
            return Result.Succeeded;
        }
    }

    // ── 3. Import ──
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExLinkImportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var app = ParameterHelpers.GetApp(commandData);
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { message = "No active document."; return Result.Failed; }

            var linkPath = ExLinkHelpers.PickLinkFile("Select .link for Import");
            if (string.IsNullOrEmpty(linkPath)) return Result.Succeeded;

            var def = ExLinkEngine.ParseLinkFile(linkPath);
            var inputPath = ExLinkHelpers.PickOpenPath("xlsx");
            if (string.IsNullOrEmpty(inputPath)) return Result.Succeeded;

            using var tx = new Transaction(doc, "STING ExLink Import");
            tx.Start();
            var result = ExLinkEngine.ImportFromExcel(doc, def, inputPath);
            tx.Commit();

            var msg = result.Success
                ? $"Read: {result.RowsRead} rows\nUpdated: {result.RowsUpdated}\nSkipped: {result.RowsSkipped}\nProperties written: {result.PropertiesWritten}"
                : $"Import failed.\n{string.Join("\n", result.Warnings)}";
            TaskDialog.Show("STING — ExLink Import", msg);
            return Result.Succeeded;
        }
    }

    // ── 4. Multi-Export ──
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExLinkMultiExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var app = ParameterHelpers.GetApp(commandData);
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { message = "No active document."; return Result.Failed; }

            var files = ExLinkEngine.BrowseLinkFiles();
            if (files.Count == 0)
            {
                TaskDialog.Show("STING — Multi-Export", "No .link files found.");
                return Result.Succeeded;
            }

            // Pick all .link files via list picker
            var names = files.Select(f => f.FileName).ToList();
            var pickItems = names.Select(n => new StingListPicker.ListItem { Label = n }).ToList();
            var pickResult = StingListPicker.Show("Select .link files to export", "Choose one or more .link definitions", pickItems, true);
            if (pickResult == null || pickResult.Count == 0) return Result.Succeeded;
            var picks = pickResult.Select(r => r.Label).ToList();

            var outputDir = ExLinkHelpers.PickFolderPath("Select output folder for exports");
            if (string.IsNullOrEmpty(outputDir)) return Result.Succeeded;

            int exported = 0, failed = 0;
            foreach (var pick in picks)
            {
                var info = files.FirstOrDefault(f => f.FileName == pick);
                if (info == null) continue;
                try
                {
                    var def = ExLinkEngine.ParseLinkFile(info.FilePath);
                    var outPath = Path.Combine(outputDir, Path.ChangeExtension(info.FileName, ".xlsx"));
                    var result = ExLinkEngine.ExportToExcel(doc, def, outPath);
                    if (result.Success) exported++; else failed++;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Multi-export failed for {pick}: {ex.Message}");
                    failed++;
                }
            }

            TaskDialog.Show("STING — Multi-Export", $"Exported: {exported}\nFailed: {failed}\n\nOutput: {outputDir}");
            return Result.Succeeded;
        }
    }

    // ── 5. Quick View ──
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExLinkQuickViewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var app = ParameterHelpers.GetApp(commandData);
            var uidoc = app?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) { message = "No active document."; return Result.Failed; }

            var linkPath = ExLinkHelpers.PickLinkFile("Select .link to Preview");
            if (string.IsNullOrEmpty(linkPath)) return Result.Succeeded;

            var def = ExLinkEngine.ParseLinkFile(linkPath);
            var elems = ExLinkEngine.CollectElements(doc, def);

            if (elems.Count == 0)
            {
                TaskDialog.Show("STING — Quick View", $"No elements matched.\n\nElement Type: {def.ElementType}\nFilters: {def.Filters.Count}");
                return Result.Succeeded;
            }

            // Select matched elements in the model
            uidoc.Selection.SetElementIds(elems.Select(e => e.Id).ToList());

            // Show preview of first 20 rows
            var preview = new System.Text.StringBuilder();
            preview.AppendLine($"Link: {def.FileName}");
            preview.AppendLine($"Element Type: {def.ElementType}");
            preview.AppendLine($"Matched: {elems.Count} elements (selected in model)");
            preview.AppendLine($"Properties: {def.Properties.Count}");
            preview.AppendLine();

            var cols = def.Properties.Take(6).ToList();
            preview.AppendLine(string.Join(" | ", cols.Select(c => c.Name)));
            preview.AppendLine(new string('-', 80));

            foreach (var el in elems.Take(20))
            {
                var vals = cols.Select(c => ExLinkEngine.GetPropertyValue(doc, el, c));
                preview.AppendLine(string.Join(" | ", vals.Select(v => v.Length > 20 ? v.Substring(0, 17) + "..." : v)));
            }

            if (elems.Count > 20) preview.AppendLine($"... and {elems.Count - 20} more rows");

            TaskDialog.Show("STING — ExLink Quick View", preview.ToString());
            return Result.Succeeded;
        }
    }

    // ── 6. Batch Export ──
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExLinkBatchExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var app = ParameterHelpers.GetApp(commandData);
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { message = "No active document."; return Result.Failed; }

            var files = ExLinkEngine.GetAllLinkFiles();
            if (files.Count == 0)
            {
                TaskDialog.Show("STING — Batch Export", "No .link files found.");
                return Result.Succeeded;
            }

            var outputDir = ExLinkHelpers.PickFolderPath("Select output folder for batch export");
            if (string.IsNullOrEmpty(outputDir)) return Result.Succeeded;

            int exported = 0, failed = 0;
            var progress = StingProgressDialog.Show("ExLink Batch Export", files.Count);
            foreach (var f in files)
            {
                if (progress.IsCancelled) break;
                progress.Increment(Path.GetFileName(f));
                try
                {
                    var def = ExLinkEngine.ParseLinkFile(f);
                    var outPath = Path.Combine(outputDir, Path.ChangeExtension(Path.GetFileName(f), ".xlsx"));
                    var result = ExLinkEngine.ExportToExcel(doc, def, outPath);
                    if (result.Success) exported++; else failed++;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Batch export failed for {f}: {ex.Message}");
                    failed++;
                }
            }
            progress.Close();

            TaskDialog.Show("STING — Batch Export", $"Total .link files: {files.Count}\nExported: {exported}\nFailed: {failed}\n\nOutput: {outputDir}");
            return Result.Succeeded;
        }
    }

    // ── 7. Custom Link ──
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExLinkCustomLinkCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var app = ParameterHelpers.GetApp(commandData);
            var uidoc = app?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) { message = "No active document."; return Result.Failed; }

            // Pick category
            var categories = new List<string>
            {
                "Walls", "Doors", "Windows", "Rooms", "Floors", "Ceilings", "Roofs",
                "Structural Columns", "Structural Framing", "Furniture",
                "Mechanical Equipment", "Electrical Equipment", "Lighting Fixtures",
                "Plumbing Fixtures", "Ducts", "Pipes", "Cable Trays", "Conduits",
                "Sheets", "Views", "Generic Models"
            };
            var catPick = StingListPicker.Show("Select element category", "Choose the Revit category to export", categories);
            if (catPick == null) return Result.Succeeded;

            var elementType = catPick;

            // Build a basic link definition from selected category
            var def = new LinkDefinition
            {
                FileName = $"Custom_{elementType.Replace(" ", "_")}.link",
                ElementType = elementType.ToUpperInvariant().Replace(" ", "_")
            };

            // Collect sample element to discover available parameters
            var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            var sample = ExLinkEngine.CollectElements(doc, def).FirstOrDefault();
            if (sample == null)
            {
                TaskDialog.Show("STING — Custom Link", $"No {elementType} elements found in the model.");
                return Result.Succeeded;
            }

            // List parameters from the sample element
            var paramNames = new List<string>();
            foreach (Parameter p in sample.Parameters)
            {
                if (p.Definition != null && !string.IsNullOrEmpty(p.Definition.Name))
                    paramNames.Add(p.Definition.Name);
            }
            paramNames = paramNames.Distinct().OrderBy(n => n).ToList();

            // Add calculated properties
            paramNames.InsertRange(0, new[] { "[Element ID]", "[Category]", "[Family]", "[Type]", "[Family and Type]", "[Level]" });

            var paramItems = paramNames.Select(n => new StingListPicker.ListItem { Label = n }).ToList();
            var paramPickResult = StingListPicker.Show("Select properties to export", "Choose parameters to include in the .link definition", paramItems, true);
            if (paramPickResult == null || paramPickResult.Count == 0) return Result.Succeeded;
            var paramPicks = paramPickResult.Select(r => r.Label).ToList();

            // Build properties
            foreach (var pName in paramPicks)
            {
                var prop = new PropertyDef { Name = pName.TrimStart('[').TrimEnd(']') };
                if (pName.StartsWith("["))
                {
                    prop.PropertyType = "CALCULATED_PROPERTY";
                    prop.LookupType = "CALCULATED_PROPERTY";
                    prop.IsReadOnly = true;
                }
                else
                {
                    var p = sample.LookupParameter(pName);
                    if (p != null)
                    {
                        prop.IsReadOnly = p.IsReadOnly;
                        if (p.Definition is Autodesk.Revit.DB.InternalDefinition intDef)
                        {
                            prop.PropertyType = "BUILT_IN_PARAMETER";
                            prop.BuiltInName = intDef.BuiltInParameter.ToString();
                        }
                        else
                        {
                            prop.PropertyType = "SHARED_PARAMETER";
                        }
                    }
                }
                def.Properties.Add(prop);
            }

            // Export with custom definition
            var outputPath = ExLinkHelpers.PickSavePath(def.FileName, "xlsx");
            if (string.IsNullOrEmpty(outputPath)) return Result.Succeeded;

            var result = ExLinkEngine.ExportToExcel(doc, def, outputPath);
            var msg = result.Success
                ? $"Custom export: {result.RowCount} rows × {result.ColumnCount} columns\n\n{outputPath}"
                : $"Export failed.\n{string.Join("\n", result.Warnings)}";
            TaskDialog.Show("STING — Custom Link Export", msg);
            return Result.Succeeded;
        }
    }

    // ── 8. QTO ──
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExLinkQTOCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var app = ParameterHelpers.GetApp(commandData);
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { message = "No active document."; return Result.Failed; }

            var outputPath = ExLinkHelpers.PickSavePath("QTO_Report", "xlsx");
            if (string.IsNullOrEmpty(outputPath)) return Result.Succeeded;

            var result = ExLinkEngine.ExportQTO(doc, outputPath);
            var msg = result.Success
                ? $"QTO exported: {result.RowCount} type groups\n\n{outputPath}"
                : $"QTO export failed.\n{string.Join("\n", result.Warnings)}";
            TaskDialog.Show("STING — Quantity Take-Off", msg);
            return Result.Succeeded;
        }
    }

    // ── 9. Document Issuance ──
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExLinkDocIssuanceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var app = ParameterHelpers.GetApp(commandData);
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { message = "No active document."; return Result.Failed; }

            var outputPath = ExLinkHelpers.PickSavePath("Document_Issuance", "xlsx");
            if (string.IsNullOrEmpty(outputPath)) return Result.Succeeded;

            try
            {
                var sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .OrderBy(s => s.SheetNumber).ToList();

                using var wb = new ClosedXML.Excel.XLWorkbook();
                var ws = wb.Worksheets.Add("Document Issuance");
                var headers = new[] { "Sheet Number", "Sheet Name", "Drawn By", "Checked By", "Approved By", "Issue Date", "Revision", "Status" };
                for (int c = 0; c < headers.Length; c++)
                    ws.Cell(1, c + 1).Value = headers[c];

                var headerRange = ws.Range(1, 1, 1, headers.Length);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#6A1B9A");
                headerRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;

                int row = 2;
                foreach (var s in sheets)
                {
                    ws.Cell(row, 1).Value = s.SheetNumber;
                    ws.Cell(row, 2).Value = s.Name;
                    ws.Cell(row, 3).Value = s.get_Parameter(BuiltInParameter.SHEET_DRAWN_BY)?.AsString() ?? "";
                    ws.Cell(row, 4).Value = s.get_Parameter(BuiltInParameter.SHEET_CHECKED_BY)?.AsString() ?? "";
                    ws.Cell(row, 5).Value = s.get_Parameter(BuiltInParameter.SHEET_APPROVED_BY)?.AsString() ?? "";
                    ws.Cell(row, 6).Value = s.get_Parameter(BuiltInParameter.SHEET_ISSUE_DATE)?.AsString() ?? "";
                    ws.Cell(row, 7).Value = s.get_Parameter(BuiltInParameter.SHEET_CURRENT_REVISION)?.AsString() ?? "";
                    ws.Cell(row, 8).Value = ParameterHelpers.GetString(s, "STING_CDE_STATUS") != "" ? ParameterHelpers.GetString(s, "STING_CDE_STATUS") : "WIP";
                    row++;
                }

                ws.Columns().AdjustToContents(1, 40);
                wb.SaveAs(outputPath);
                TaskDialog.Show("STING — Doc Issuance", $"Exported {sheets.Count} sheets.\n\n{outputPath}");
            }
            catch (Exception ex)
            {
                StingLog.Error($"Doc issuance export: {ex.Message}", ex);
                TaskDialog.Show("STING — Doc Issuance", $"Export failed: {ex.Message}");
            }
            return Result.Succeeded;
        }
    }

    // ── 10. COBie Sync ──
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExLinkCOBieSyncCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var app = ParameterHelpers.GetApp(commandData);
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { message = "No active document."; return Result.Failed; }

            // Find COBie .link files
            var allLinks = ExLinkEngine.GetAllLinkFiles();
            var cobieLinks = allLinks.Where(f => Path.GetFileName(f).IndexOf("cobie", StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            if (cobieLinks.Count == 0)
            {
                TaskDialog.Show("STING — COBie Sync", "No COBie .link files found.\nPlace COBie-related .link files in the Data/ExLink/ directory.");
                return Result.Succeeded;
            }

            var outputDir = ExLinkHelpers.PickFolderPath("Select COBie output folder");
            if (string.IsNullOrEmpty(outputDir)) return Result.Succeeded;

            int exported = 0;
            foreach (var linkPath in cobieLinks)
            {
                try
                {
                    var def = ExLinkEngine.ParseLinkFile(linkPath);
                    var outPath = Path.Combine(outputDir, Path.ChangeExtension(Path.GetFileName(linkPath), ".xlsx"));
                    var result = ExLinkEngine.ExportToExcel(doc, def, outPath);
                    if (result.Success) exported++;
                }
                catch (Exception ex) { StingLog.Warn($"COBie sync failed for {linkPath}: {ex.Message}"); }
            }

            TaskDialog.Show("STING — COBie Sync", $"COBie link files processed: {cobieLinks.Count}\nExported: {exported}\n\nOutput: {outputDir}");
            return Result.Succeeded;
        }
    }

    // ── 11. Dynamic PDF ──
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExLinkDynamicPDFCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var app = ParameterHelpers.GetApp(commandData);
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { message = "No active document."; return Result.Failed; }

            var outputDir = ExLinkHelpers.PickFolderPath("Select PDF output folder");
            if (string.IsNullOrEmpty(outputDir)) return Result.Succeeded;

            AutomationEngine.ExportSheetsToPDF(doc, outputDir, out int count, out var warnings);
            var msg = $"Exported {count} sheets to PDF.\n\n{outputDir}";
            if (warnings.Count > 0) msg += $"\n\nWarnings:\n{string.Join("\n", warnings.Take(5))}";
            TaskDialog.Show("STING — PDF Export", msg);
            return Result.Succeeded;
        }
    }

    // ── 12. Dynamic DWG ──
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExLinkDynamicDWGCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var app = ParameterHelpers.GetApp(commandData);
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { message = "No active document."; return Result.Failed; }

            var outputDir = ExLinkHelpers.PickFolderPath("Select DWG output folder");
            if (string.IsNullOrEmpty(outputDir)) return Result.Succeeded;

            AutomationEngine.ExportSheetsToDWG(doc, outputDir, out int count, out var warnings);
            var msg = $"Exported {count} sheets to DWG.\n\n{outputDir}";
            if (warnings.Count > 0) msg += $"\n\nWarnings:\n{string.Join("\n", warnings.Take(5))}";
            TaskDialog.Show("STING — DWG Export", msg);
            return Result.Succeeded;
        }
    }

    // ── 13. Dynamic NWC ──
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExLinkDynamicNWCCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var app = ParameterHelpers.GetApp(commandData);
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { message = "No active document."; return Result.Failed; }

            var outputDir = ExLinkHelpers.PickFolderPath("Select NWC output folder");
            if (string.IsNullOrEmpty(outputDir)) return Result.Succeeded;

            AutomationEngine.ExportToNWC(doc, outputDir, out bool ok, out string nwcMsg);
            TaskDialog.Show("STING — NWC Export", nwcMsg);
            return Result.Succeeded;
        }
    }

    // ── Shared helpers ──

    internal static class ExLinkHelpers
    {
        internal static string PickLinkFile(string title)
        {
            var files = ExLinkEngine.BrowseLinkFiles();
            if (files.Count == 0) return null;
            var selected = ExLinkBrowserDialog.ShowDialog(files, title);
            return selected;
        }

        internal static string PickSavePath(string defaultName, string ext)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = Path.GetFileNameWithoutExtension(defaultName),
                DefaultExt = $".{ext}",
                Filter = ext == "xlsx" ? "Excel Files|*.xlsx" : $"{ext.ToUpper()} Files|*.{ext}",
                Title = "Save As"
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        internal static string PickOpenPath(string ext)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                DefaultExt = $".{ext}",
                Filter = ext == "xlsx" ? "Excel Files|*.xlsx" : $"{ext.ToUpper()} Files|*.{ext}",
                Title = "Select File"
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        internal static string PickFolderPath(string description)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = description,
                FileName = "Select Folder",
                CheckFileExists = false,
                CheckPathExists = true,
                OverwritePrompt = false
            };
            if (dlg.ShowDialog() == true)
                return Path.GetDirectoryName(dlg.FileName);
            return null;
        }
    }
}
