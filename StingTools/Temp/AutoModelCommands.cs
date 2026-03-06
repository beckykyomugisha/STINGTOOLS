using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Temp
{
    // ════════════════════════════════════════════════════════════════════
    //  AutoModel — DWG-to-BIM automation commands
    //  Links DWG files, traces geometry, places families, maps layers.
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Link DWG/DXF files to Revit levels with automatic level-to-file matching.
    /// Supports batch linking of multiple CAD files with origin-to-origin or shared coordinates.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LinkDWGCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // Prompt user to select DWG files
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select DWG/DXF Files to Link",
                    Filter = "CAD Files (*.dwg;*.dxf)|*.dwg;*.dxf|DWG Files (*.dwg)|*.dwg|DXF Files (*.dxf)|*.dxf",
                    Multiselect = true
                };

                if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0)
                    return Result.Cancelled;

                // Get all levels sorted by elevation
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .ToList();

                if (levels.Count == 0)
                {
                    TaskDialog.Show("Link DWG", "No levels found in project. Create levels first.");
                    return Result.Failed;
                }

                // Ask placement mode
                var td = new TaskDialog("Link DWG — Placement Mode");
                td.MainInstruction = "Select coordinate placement mode";
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Origin-to-Origin",
                    "Place CAD file origin at Revit internal origin");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Auto-Match to Levels",
                    "Match files to levels by name (L01→Level 1, GF→Ground, B1→Basement 1)");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    "Center-to-Center",
                    "Place CAD file center at Revit project base point");

                var result = td.Show();
                if (result == TaskDialogResult.Cancel) return Result.Cancelled;

                bool autoMatch = (result == TaskDialogResult.CommandLink2);
                ImportPlacement placement = (result == TaskDialogResult.CommandLink3)
                    ? ImportPlacement.Centered : ImportPlacement.Origin;

                int linked = 0;
                int failed = 0;
                var report = new StringBuilder();

                using (var tg = new TransactionGroup(doc, "STING Link DWG Files"))
                {
                    tg.Start();

                    foreach (string filePath in dialog.FileNames)
                    {
                        try
                        {
                            string fileName = Path.GetFileNameWithoutExtension(filePath);
                            Level targetLevel = autoMatch
                                ? AutoModelHelper.MatchFileToLevel(fileName, levels)
                                : levels.First();

                            using (var t = new Transaction(doc, $"STING Link {fileName}"))
                            {
                                t.Start();

                                var options = new DWGImportOptions
                                {
                                    Placement = placement,
                                    AutoCorrectAlmostVHLines = true,
                                    Unit = ImportUnit.Default,
                                    ThisViewOnly = false
                                };

                                ElementId linkId = ElementId.InvalidElementId;
                                doc.Link(filePath, options, doc.ActiveView, out linkId);

                                if (linkId != ElementId.InvalidElementId)
                                {
                                    linked++;
                                    report.AppendLine($"  ✓ {fileName} → {targetLevel.Name}");
                                    StingLog.Info($"Linked DWG: {filePath} to {targetLevel.Name}");
                                }
                                else
                                {
                                    failed++;
                                    report.AppendLine($"  ✗ {fileName} — link returned invalid ID");
                                }

                                t.Commit();
                            }
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            report.AppendLine($"  ✗ {Path.GetFileName(filePath)} — {ex.Message}");
                            StingLog.Warn($"Link DWG failed: {filePath}: {ex.Message}");
                        }
                    }

                    tg.Assimilate();
                }

                var summary = new TaskDialog("Link DWG — Results");
                summary.MainInstruction = $"Linked {linked} file(s)" +
                    (failed > 0 ? $", {failed} failed" : "");
                summary.MainContent = report.ToString();
                summary.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("LinkDWG failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Trace walls from CAD polylines/lines on linked DWG layers.
    /// Creates Revit walls from CAD geometry with configurable wall type and layer mapping.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TraceWallsFromCADCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            try
            {
                // Find linked CAD instances in the active view
                var cadInstances = new FilteredElementCollector(doc, activeView.Id)
                    .OfClass(typeof(ImportInstance))
                    .Cast<ImportInstance>()
                    .Where(i => !i.IsLinked || i.IsLinked) // get all imports/links
                    .ToList();

                if (cadInstances.Count == 0)
                {
                    TaskDialog.Show("Trace Walls",
                        "No linked/imported CAD files found in the active view.\n\n" +
                        "Link a DWG file first using 'Link DWG', then switch to the plan view.");
                    return Result.Failed;
                }

                // Select which CAD link to trace from
                ImportInstance cadLink = cadInstances.First();
                if (cadInstances.Count > 1)
                {
                    var selectTd = new TaskDialog("Trace Walls — Select CAD Link");
                    selectTd.MainInstruction = "Multiple CAD files found. Using first linked file.";
                    selectTd.MainContent = string.Join("\n",
                        cadInstances.Select((c, i) => $"  {i + 1}. {c.Category?.Name ?? "Import"} (ID: {c.Id})"));
                    selectTd.Show();
                }

                // Get geometry from the CAD link
                var geoElem = cadLink.get_Geometry(new Options { View = activeView });
                if (geoElem == null)
                {
                    TaskDialog.Show("Trace Walls", "Cannot read geometry from CAD link.");
                    return Result.Failed;
                }

                // Extract lines and polylines from CAD geometry
                var cadLines = new List<Line>();
                var cadTransform = cadLink.GetTransform();
                AutoModelHelper.ExtractLinesFromGeometry(geoElem, cadTransform, cadLines);

                if (cadLines.Count == 0)
                {
                    TaskDialog.Show("Trace Walls",
                        "No usable line geometry found in the CAD file.\n" +
                        "Ensure the DWG contains lines, polylines, or lwpolylines on wall layers.");
                    return Result.Failed;
                }

                // Get wall types
                var wallTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .OrderBy(w => w.Name)
                    .ToList();

                if (wallTypes.Count == 0)
                {
                    TaskDialog.Show("Trace Walls", "No wall types found in project.");
                    return Result.Failed;
                }

                // Use the first basic wall type (or "Generic - 200mm" if found)
                WallType defaultType = wallTypes.FirstOrDefault(w =>
                    w.Name.Contains("Generic") && w.Name.Contains("200")) ?? wallTypes.First();

                // Get the level for wall placement
                Level level = activeView.GenLevel;
                if (level == null)
                {
                    var levels = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level)).Cast<Level>()
                        .OrderBy(l => l.Elevation).ToList();
                    level = levels.FirstOrDefault();
                }

                if (level == null)
                {
                    TaskDialog.Show("Trace Walls", "No level found. Switch to a plan view.");
                    return Result.Failed;
                }

                // Default wall height (3m = ~9.84 feet)
                double wallHeight = 3.0 / 0.3048; // convert metres to feet

                int created = 0;
                int skipped = 0;
                double minLength = 0.5 / 0.3048; // skip lines < 0.5m

                using (var t = new Transaction(doc, "STING Trace Walls from CAD"))
                {
                    t.Start();

                    foreach (var line in cadLines)
                    {
                        try
                        {
                            if (line.Length < minLength)
                            {
                                skipped++;
                                continue;
                            }

                            // Project line to the level elevation (flatten Z)
                            XYZ start = new XYZ(line.GetEndPoint(0).X, line.GetEndPoint(0).Y, level.Elevation);
                            XYZ end = new XYZ(line.GetEndPoint(1).X, line.GetEndPoint(1).Y, level.Elevation);

                            if (start.DistanceTo(end) < minLength)
                            {
                                skipped++;
                                continue;
                            }

                            Line wallLine = Line.CreateBound(start, end);
                            Wall wall = Wall.Create(doc, wallLine, defaultType.Id, level.Id,
                                wallHeight, 0, false, false);

                            if (wall != null) created++;
                        }
                        catch (Exception ex)
                        {
                            skipped++;
                            StingLog.Warn($"Trace wall skipped: {ex.Message}");
                        }
                    }

                    t.Commit();
                }

                var resultTd = new TaskDialog("Trace Walls — Results");
                resultTd.MainInstruction = $"Created {created} walls from {cadLines.Count} CAD lines";
                resultTd.MainContent =
                    $"Wall type: {defaultType.Name}\n" +
                    $"Level: {level.Name}\n" +
                    $"Height: 3.0m\n" +
                    $"Skipped: {skipped} (too short or invalid)\n\n" +
                    "Review the generated walls and adjust types/heights as needed.";
                resultTd.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("TraceWalls failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Extract room data from CAD text/attributes — reads room names, numbers,
    /// and areas from DWG text entities and creates Revit rooms.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExtractRoomsFromCADCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            try
            {
                // Find CAD imports in view
                var cadInstances = new FilteredElementCollector(doc, activeView.Id)
                    .OfClass(typeof(ImportInstance))
                    .Cast<ImportInstance>()
                    .ToList();

                if (cadInstances.Count == 0)
                {
                    TaskDialog.Show("Extract Rooms",
                        "No linked/imported CAD files found in the active view.");
                    return Result.Failed;
                }

                var cadLink = cadInstances.First();
                var cadTransform = cadLink.GetTransform();

                // Extract text from CAD geometry
                var geoElem = cadLink.get_Geometry(new Options { View = activeView });
                if (geoElem == null)
                {
                    TaskDialog.Show("Extract Rooms", "Cannot read geometry from CAD link.");
                    return Result.Failed;
                }

                var textItems = new List<AutoModelHelper.CadTextItem>();
                AutoModelHelper.ExtractTextFromGeometry(geoElem, cadTransform, textItems);

                if (textItems.Count == 0)
                {
                    TaskDialog.Show("Extract Rooms",
                        "No text found in CAD file.\n\n" +
                        "Ensure the DWG has text or attribute entities for room names/numbers.");
                    return Result.Failed;
                }

                // Get level
                Level level = activeView.GenLevel;
                if (level == null)
                {
                    TaskDialog.Show("Extract Rooms", "Active view has no associated level. Use a plan view.");
                    return Result.Failed;
                }

                // Get the phase for room creation
                Phase phase = null;
                var phases = doc.Phases;
                if (phases.Size > 0)
                    phase = phases.get_Item(phases.Size - 1); // last phase = current

                int created = 0;
                int skipped = 0;
                var report = new StringBuilder();
                report.AppendLine("Extracted text items:");

                using (var t = new Transaction(doc, "STING Extract Rooms from CAD"))
                {
                    t.Start();

                    foreach (var item in textItems)
                    {
                        try
                        {
                            // Filter: only create rooms from text that looks like room names
                            // (skip dimension text, notes, etc.)
                            string text = item.Text.Trim();
                            if (string.IsNullOrEmpty(text) || text.Length < 2 || text.Length > 100)
                            {
                                skipped++;
                                continue;
                            }

                            // Skip text that looks like dimensions or coordinates
                            if (AutoModelHelper.IsDimensionText(text))
                            {
                                skipped++;
                                continue;
                            }

                            // Create room at the text location
                            UV point = new UV(item.Position.X, item.Position.Y);
                            Room room = doc.Create.NewRoom(level, point);

                            if (room != null)
                            {
                                // Try to set room name
                                Parameter nameParam = room.get_Parameter(BuiltInParameter.ROOM_NAME);
                                if (nameParam != null && !nameParam.IsReadOnly)
                                    nameParam.Set(text);

                                created++;
                                report.AppendLine($"  ✓ \"{text}\" at ({item.Position.X:F1}, {item.Position.Y:F1})");
                            }
                        }
                        catch (Exception ex)
                        {
                            skipped++;
                            report.AppendLine($"  ✗ \"{item.Text}\" — {ex.Message}");
                        }
                    }

                    t.Commit();
                }

                var resultTd = new TaskDialog("Extract Rooms — Results");
                resultTd.MainInstruction = $"Created {created} rooms from {textItems.Count} text items";
                resultTd.MainContent = report.ToString() +
                    (skipped > 0 ? $"\n\nSkipped {skipped} text items (dimensions, notes, too short)" : "");
                resultTd.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ExtractRooms failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Map DWG layers to Revit categories — lists all layers from linked CAD,
    /// reports layer-to-category mapping, and applies graphic overrides by layer.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LayerMappingCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            try
            {
                // Find CAD imports
                var cadInstances = new FilteredElementCollector(doc, activeView.Id)
                    .OfClass(typeof(ImportInstance))
                    .Cast<ImportInstance>()
                    .ToList();

                if (cadInstances.Count == 0)
                {
                    TaskDialog.Show("Layer Mapping",
                        "No linked/imported CAD files found in the active view.");
                    return Result.Failed;
                }

                var cadLink = cadInstances.First();

                // Get CAD layer info
                var geoElem = cadLink.get_Geometry(new Options());
                if (geoElem == null)
                {
                    TaskDialog.Show("Layer Mapping", "Cannot read geometry from CAD link.");
                    return Result.Failed;
                }

                // Collect layer names from subcategories of the import
                var layers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                Category cadCategory = cadLink.Category;
                if (cadCategory?.SubCategories != null)
                {
                    foreach (Category sub in cadCategory.SubCategories)
                    {
                        layers[sub.Name] = 0;
                    }
                }

                if (layers.Count == 0)
                {
                    TaskDialog.Show("Layer Mapping",
                        "No layers found in CAD link.\n" +
                        "The DWG may need to be exploded or have layer data.");
                    return Result.Failed;
                }

                // Build report with standard layer-to-category mapping
                var report = new StringBuilder();
                report.AppendLine($"CAD Link: {cadLink.Category?.Name ?? "Import"}");
                report.AppendLine($"Layers found: {layers.Count}");
                report.AppendLine(new string('─', 60));
                report.AppendLine($"  {"CAD Layer",-30} → {"Revit Category",-25}");
                report.AppendLine(new string('─', 60));

                foreach (var layerName in layers.Keys.OrderBy(k => k))
                {
                    string mapped = AutoModelHelper.MapLayerToCategory(layerName);
                    report.AppendLine($"  {layerName,-30} → {mapped,-25}");
                }

                report.AppendLine(new string('─', 60));
                report.AppendLine();
                report.AppendLine("Standard AEC layer prefixes detected:");

                // Count by prefix pattern
                var prefixes = new Dictionary<string, int>();
                foreach (string layer in layers.Keys)
                {
                    string prefix = layer.Split(new[] { '-', '_', ' ' }, 2)[0].ToUpperInvariant();
                    if (!prefixes.ContainsKey(prefix)) prefixes[prefix] = 0;
                    prefixes[prefix]++;
                }

                foreach (var kvp in prefixes.OrderByDescending(p => p.Value))
                {
                    string desc = AutoModelHelper.GetAecPrefixDescription(kvp.Key);
                    report.AppendLine($"  {kvp.Key,-8} ({kvp.Value} layers) — {desc}");
                }

                // Apply VG overrides to make wall layers bold
                int overrides = 0;
                using (var t = new Transaction(doc, "STING Layer Mapping VG"))
                {
                    t.Start();

                    if (cadCategory?.SubCategories != null)
                    {
                        foreach (Category sub in cadCategory.SubCategories)
                        {
                            string mapped = AutoModelHelper.MapLayerToCategory(sub.Name);
                            if (mapped == "Walls" || mapped == "Structural")
                            {
                                // Make wall/structural layers bold (thicker, darker)
                                var ogs = new OverrideGraphicSettings();
                                ogs.SetProjectionLineWeight(5);
                                ogs.SetProjectionLineColor(new Color(0, 0, 0));
                                activeView.SetCategoryOverrides(sub.Id, ogs);
                                overrides++;
                            }
                            else if (mapped == "Hidden" || mapped == "Ignore")
                            {
                                // Hide non-relevant layers
                                try
                                {
                                    activeView.SetCategoryHidden(sub.Id, true);
                                    overrides++;
                                }
                                catch { /* some categories can't be hidden */ }
                            }
                        }
                    }

                    t.Commit();
                }

                report.AppendLine();
                report.AppendLine($"Applied {overrides} VG overrides (wall layers bold, non-AEC layers hidden)");

                var resultTd = new TaskDialog("Layer Mapping — Results");
                resultTd.MainInstruction = $"{layers.Count} CAD layers mapped";
                resultTd.MainContent = report.ToString();
                resultTd.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("LayerMapping failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Place Revit families at CAD block insertion points.
    /// Reads block references from linked DWG and places matching families.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceFamiliesFromCADCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            try
            {
                // Find CAD imports
                var cadInstances = new FilteredElementCollector(doc, activeView.Id)
                    .OfClass(typeof(ImportInstance))
                    .Cast<ImportInstance>()
                    .ToList();

                if (cadInstances.Count == 0)
                {
                    TaskDialog.Show("Place Families",
                        "No linked/imported CAD files found in the active view.");
                    return Result.Failed;
                }

                var cadLink = cadInstances.First();
                var cadTransform = cadLink.GetTransform();

                // Extract block insertion points from geometry
                var geoElem = cadLink.get_Geometry(new Options { View = activeView });
                if (geoElem == null)
                {
                    TaskDialog.Show("Place Families", "Cannot read geometry from CAD link.");
                    return Result.Failed;
                }

                var blockPoints = new List<AutoModelHelper.CadBlockItem>();
                AutoModelHelper.ExtractBlocksFromGeometry(geoElem, cadTransform, blockPoints);

                if (blockPoints.Count == 0)
                {
                    TaskDialog.Show("Place Families",
                        "No block references found in CAD file.\n\n" +
                        "This command reads DWG block insertion points to place Revit families.\n" +
                        "Ensure the DWG contains block references (not exploded geometry).");
                    return Result.Failed;
                }

                // Get level
                Level level = activeView.GenLevel;
                if (level == null)
                {
                    var levels = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level)).Cast<Level>()
                        .OrderBy(l => l.Elevation).ToList();
                    level = levels.FirstOrDefault();
                }

                // Collect available family symbols
                var familySymbols = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(fs => fs.Family.FamilyPlacementType == FamilyPlacementType.OneLevelBased ||
                                 fs.Family.FamilyPlacementType == FamilyPlacementType.OneLevelBasedHosted)
                    .ToList();

                // Build block-to-family mapping report
                var blockNames = blockPoints.Select(b => b.BlockName).Distinct().OrderBy(n => n).ToList();
                var report = new StringBuilder();
                report.AppendLine($"Found {blockPoints.Count} block references ({blockNames.Count} unique block names):");
                report.AppendLine();

                int placed = 0;
                int skipped = 0;

                using (var t = new Transaction(doc, "STING Place Families from CAD"))
                {
                    t.Start();

                    foreach (var block in blockPoints)
                    {
                        try
                        {
                            // Try to find a matching family symbol by block name
                            FamilySymbol symbol = AutoModelHelper.FindMatchingSymbol(
                                familySymbols, block.BlockName);

                            if (symbol == null)
                            {
                                skipped++;
                                continue;
                            }

                            if (!symbol.IsActive) symbol.Activate();

                            // Place at block insertion point
                            XYZ point = new XYZ(block.Position.X, block.Position.Y,
                                level?.Elevation ?? 0);

                            FamilyInstance inst = doc.Create.NewFamilyInstance(
                                point, symbol, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                            if (inst != null)
                            {
                                placed++;
                                // Rotate to match block rotation
                                if (Math.Abs(block.Rotation) > 0.001)
                                {
                                    Line axis = Line.CreateBound(point, point + XYZ.BasisZ);
                                    ElementTransformUtils.RotateElement(doc, inst.Id, axis, block.Rotation);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            skipped++;
                            StingLog.Warn($"Place family from block '{block.BlockName}': {ex.Message}");
                        }
                    }

                    t.Commit();
                }

                report.AppendLine("Block Name → Family Mapping:");
                foreach (string name in blockNames)
                {
                    var sym = AutoModelHelper.FindMatchingSymbol(familySymbols, name);
                    int count = blockPoints.Count(b => b.BlockName == name);
                    report.AppendLine(sym != null
                        ? $"  ✓ {name} ({count}x) → {sym.Family.Name} : {sym.Name}"
                        : $"  ✗ {name} ({count}x) → no match");
                }

                var resultTd = new TaskDialog("Place Families — Results");
                resultTd.MainInstruction = $"Placed {placed} families, {skipped} skipped";
                resultTd.MainContent = report.ToString();
                resultTd.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("PlaceFamilies failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Full AutoModel pipeline — one-click: Link DWG → Trace Walls → Extract Rooms → Place Families.
    /// Runs all steps in sequence with progress reporting.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoModelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                var td = new TaskDialog("AutoModel — DWG to BIM");
                td.MainInstruction = "AutoModel will convert DWG files to Revit BIM elements";
                td.MainContent =
                    "The following pipeline will execute:\n\n" +
                    "Step 1: Link DWG file(s) to project\n" +
                    "Step 2: Trace walls from CAD lines/polylines\n" +
                    "Step 3: Extract room names from CAD text\n" +
                    "Step 4: Map DWG layers to Revit categories\n" +
                    "Step 5: Place families at block insertion points\n\n" +
                    "You can also run each step individually from the AutoModel panel.\n\n" +
                    "Select a DWG file to begin.";
                td.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;

                if (td.Show() == TaskDialogResult.Cancel) return Result.Cancelled;

                // Step 1: Select and link DWG
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "AutoModel — Select DWG File",
                    Filter = "DWG Files (*.dwg)|*.dwg|DXF Files (*.dxf)|*.dxf",
                    Multiselect = false
                };

                if (dialog.ShowDialog() != true) return Result.Cancelled;

                string filePath = dialog.FileName;
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                View activeView = doc.ActiveView;

                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation).ToList();

                Level level = activeView.GenLevel ?? levels.FirstOrDefault();
                if (level == null)
                {
                    TaskDialog.Show("AutoModel", "No levels found. Create a level first.");
                    return Result.Failed;
                }

                var report = new StringBuilder();
                int wallCount = 0;
                int roomCount = 0;
                int familyCount = 0;

                using (var tg = new TransactionGroup(doc, "STING AutoModel Pipeline"))
                {
                    tg.Start();

                    // ── Step 1: Link DWG ──
                    report.AppendLine("── Step 1: Link DWG ──");
                    ElementId linkId = ElementId.InvalidElementId;

                    using (var t = new Transaction(doc, "STING AutoModel Link"))
                    {
                        t.Start();
                        var options = new DWGImportOptions
                        {
                            Placement = ImportPlacement.Origin,
                            AutoCorrectAlmostVHLines = true,
                            Unit = ImportUnit.Default,
                            ThisViewOnly = false
                        };
                        doc.Link(filePath, options, activeView, out linkId);
                        t.Commit();
                    }

                    if (linkId == ElementId.InvalidElementId)
                    {
                        report.AppendLine("  ✗ Failed to link DWG file");
                        tg.RollBack();
                        TaskDialog.Show("AutoModel", report.ToString());
                        return Result.Failed;
                    }
                    report.AppendLine($"  ✓ Linked {fileName}");

                    // Get the linked CAD instance
                    var cadLink = doc.GetElement(linkId) as ImportInstance;
                    if (cadLink == null)
                    {
                        report.AppendLine("  ✗ Cannot access linked CAD element");
                        tg.Assimilate();
                        TaskDialog.Show("AutoModel", report.ToString());
                        return Result.Succeeded;
                    }

                    var cadTransform = cadLink.GetTransform();
                    var geoElem = cadLink.get_Geometry(new Options { View = activeView });

                    // ── Step 2: Trace Walls ──
                    report.AppendLine("\n── Step 2: Trace Walls ──");
                    if (geoElem != null)
                    {
                        var cadLines = new List<Line>();
                        AutoModelHelper.ExtractLinesFromGeometry(geoElem, cadTransform, cadLines);

                        var wallTypes = new FilteredElementCollector(doc)
                            .OfClass(typeof(WallType)).Cast<WallType>().ToList();
                        WallType wallType = wallTypes.FirstOrDefault(w =>
                            w.Name.Contains("Generic") && w.Name.Contains("200")) ?? wallTypes.First();

                        double wallHeight = 3.0 / 0.3048;
                        double minLength = 0.5 / 0.3048;

                        using (var t = new Transaction(doc, "STING AutoModel Walls"))
                        {
                            t.Start();
                            foreach (var line in cadLines)
                            {
                                try
                                {
                                    if (line.Length < minLength) continue;
                                    XYZ start = new XYZ(line.GetEndPoint(0).X, line.GetEndPoint(0).Y, level.Elevation);
                                    XYZ end = new XYZ(line.GetEndPoint(1).X, line.GetEndPoint(1).Y, level.Elevation);
                                    if (start.DistanceTo(end) < minLength) continue;

                                    Line wallLine = Line.CreateBound(start, end);
                                    Wall wall = Wall.Create(doc, wallLine, wallType.Id, level.Id,
                                        wallHeight, 0, false, false);
                                    if (wall != null) wallCount++;
                                }
                                catch { /* skip invalid lines */ }
                            }
                            t.Commit();
                        }
                        report.AppendLine($"  ✓ Created {wallCount} walls from {cadLines.Count} CAD lines");
                    }
                    else
                    {
                        report.AppendLine("  — No geometry available for wall tracing");
                    }

                    // ── Step 3: Extract Rooms ──
                    report.AppendLine("\n── Step 3: Extract Rooms ──");
                    if (geoElem != null)
                    {
                        var textItems = new List<AutoModelHelper.CadTextItem>();
                        AutoModelHelper.ExtractTextFromGeometry(geoElem, cadTransform, textItems);

                        using (var t = new Transaction(doc, "STING AutoModel Rooms"))
                        {
                            t.Start();
                            foreach (var item in textItems)
                            {
                                try
                                {
                                    string text = item.Text.Trim();
                                    if (string.IsNullOrEmpty(text) || text.Length < 2 ||
                                        text.Length > 100 || AutoModelHelper.IsDimensionText(text))
                                        continue;

                                    UV point = new UV(item.Position.X, item.Position.Y);
                                    Room room = doc.Create.NewRoom(level, point);
                                    if (room != null)
                                    {
                                        Parameter nameParam = room.get_Parameter(BuiltInParameter.ROOM_NAME);
                                        if (nameParam != null && !nameParam.IsReadOnly)
                                            nameParam.Set(text);
                                        roomCount++;
                                    }
                                }
                                catch { /* skip invalid rooms */ }
                            }
                            t.Commit();
                        }
                        report.AppendLine($"  ✓ Created {roomCount} rooms from {textItems.Count} text items");
                    }

                    // ── Step 4: Layer Mapping ──
                    report.AppendLine("\n── Step 4: Layer Mapping ──");
                    int layerCount = 0;
                    Category cadCategory = cadLink.Category;
                    if (cadCategory?.SubCategories != null)
                    {
                        using (var t = new Transaction(doc, "STING AutoModel Layers"))
                        {
                            t.Start();
                            foreach (Category sub in cadCategory.SubCategories)
                            {
                                string mapped = AutoModelHelper.MapLayerToCategory(sub.Name);
                                layerCount++;
                                if (mapped == "Walls" || mapped == "Structural")
                                {
                                    var ogs = new OverrideGraphicSettings();
                                    ogs.SetProjectionLineWeight(5);
                                    activeView.SetCategoryOverrides(sub.Id, ogs);
                                }
                                else if (mapped == "Hidden" || mapped == "Ignore")
                                {
                                    try { activeView.SetCategoryHidden(sub.Id, true); } catch { }
                                }
                            }
                            t.Commit();
                        }
                    }
                    report.AppendLine($"  ✓ Mapped {layerCount} CAD layers");

                    // ── Step 5: Place Families ──
                    report.AppendLine("\n── Step 5: Place Families ──");
                    if (geoElem != null)
                    {
                        var blockPoints = new List<AutoModelHelper.CadBlockItem>();
                        AutoModelHelper.ExtractBlocksFromGeometry(geoElem, cadTransform, blockPoints);

                        if (blockPoints.Count > 0)
                        {
                            var familySymbols = new FilteredElementCollector(doc)
                                .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                                .Where(fs => fs.Family.FamilyPlacementType == FamilyPlacementType.OneLevelBased)
                                .ToList();

                            using (var t = new Transaction(doc, "STING AutoModel Families"))
                            {
                                t.Start();
                                foreach (var block in blockPoints)
                                {
                                    try
                                    {
                                        var symbol = AutoModelHelper.FindMatchingSymbol(familySymbols, block.BlockName);
                                        if (symbol == null) continue;
                                        if (!symbol.IsActive) symbol.Activate();

                                        XYZ pt = new XYZ(block.Position.X, block.Position.Y, level.Elevation);
                                        var inst = doc.Create.NewFamilyInstance(pt, symbol, level,
                                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                                        if (inst != null)
                                        {
                                            familyCount++;
                                            if (Math.Abs(block.Rotation) > 0.001)
                                            {
                                                Line axis = Line.CreateBound(pt, pt + XYZ.BasisZ);
                                                ElementTransformUtils.RotateElement(doc, inst.Id, axis, block.Rotation);
                                            }
                                        }
                                    }
                                    catch { /* skip failed placements */ }
                                }
                                t.Commit();
                            }
                        }
                        report.AppendLine($"  ✓ Placed {familyCount} families from {blockPoints.Count} blocks");
                    }

                    tg.Assimilate();
                }

                // ── Summary ──
                report.AppendLine();
                report.AppendLine(new string('═', 50));
                report.AppendLine($"  Walls: {wallCount}  |  Rooms: {roomCount}  |  Families: {familyCount}");
                report.AppendLine(new string('═', 50));
                report.AppendLine("\nReview and adjust the generated elements as needed.");

                var summaryTd = new TaskDialog("AutoModel — Complete");
                summaryTd.MainInstruction = "DWG-to-BIM conversion complete";
                summaryTd.MainContent = report.ToString();
                summaryTd.Show();

                StingLog.Info($"AutoModel complete: {wallCount} walls, {roomCount} rooms, {familyCount} families from {fileName}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("AutoModel failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Explode/query CAD geometry — reports all geometry types, layer names,
    /// line counts, text entities, and block references in the linked DWG.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CADInventoryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            try
            {
                var cadInstances = new FilteredElementCollector(doc, activeView.Id)
                    .OfClass(typeof(ImportInstance))
                    .Cast<ImportInstance>()
                    .ToList();

                if (cadInstances.Count == 0)
                {
                    TaskDialog.Show("CAD Inventory",
                        "No linked/imported CAD files in the active view.");
                    return Result.Failed;
                }

                var report = new StringBuilder();
                report.AppendLine("CAD File Inventory");
                report.AppendLine(new string('═', 60));

                foreach (var cad in cadInstances)
                {
                    report.AppendLine($"\nFile: {cad.Category?.Name ?? "Import"} (ID: {cad.Id})");
                    report.AppendLine($"Is Linked: {cad.IsLinked}");

                    // Count layers
                    int layerCount = 0;
                    if (cad.Category?.SubCategories != null)
                    {
                        foreach (Category sub in cad.Category.SubCategories)
                            layerCount++;
                    }
                    report.AppendLine($"Layers: {layerCount}");

                    // Count geometry types
                    var geoElem = cad.get_Geometry(new Options { View = activeView });
                    if (geoElem != null)
                    {
                        var cadTransform = cad.GetTransform();
                        var lines = new List<Line>();
                        var texts = new List<AutoModelHelper.CadTextItem>();
                        var blocks = new List<AutoModelHelper.CadBlockItem>();

                        AutoModelHelper.ExtractLinesFromGeometry(geoElem, cadTransform, lines);
                        AutoModelHelper.ExtractTextFromGeometry(geoElem, cadTransform, texts);
                        AutoModelHelper.ExtractBlocksFromGeometry(geoElem, cadTransform, blocks);

                        report.AppendLine($"Lines/Polylines: {lines.Count}");
                        report.AppendLine($"Text Items: {texts.Count}");
                        report.AppendLine($"Block References: {blocks.Count}");

                        if (blocks.Count > 0)
                        {
                            var uniqueBlocks = blocks.Select(b => b.BlockName).Distinct().OrderBy(n => n).ToList();
                            report.AppendLine($"\nUnique Block Names ({uniqueBlocks.Count}):");
                            foreach (string name in uniqueBlocks.Take(30))
                            {
                                int count = blocks.Count(b => b.BlockName == name);
                                report.AppendLine($"  {name} ({count}x)");
                            }
                            if (uniqueBlocks.Count > 30)
                                report.AppendLine($"  ... and {uniqueBlocks.Count - 30} more");
                        }

                        if (texts.Count > 0)
                        {
                            report.AppendLine($"\nSample Text Items (first 20):");
                            foreach (var t in texts.Take(20))
                                report.AppendLine($"  \"{t.Text}\" at ({t.Position.X:F1}, {t.Position.Y:F1})");
                        }
                    }

                    // List layers
                    if (cad.Category?.SubCategories != null)
                    {
                        report.AppendLine($"\nLayers:");
                        foreach (Category sub in cad.Category.SubCategories)
                        {
                            string mapped = AutoModelHelper.MapLayerToCategory(sub.Name);
                            report.AppendLine($"  {sub.Name,-30} → {mapped}");
                        }
                    }
                }

                var resultTd = new TaskDialog("CAD Inventory");
                resultTd.MainInstruction = $"Found {cadInstances.Count} CAD file(s)";
                resultTd.MainContent = report.ToString();
                resultTd.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("CADInventory failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  AutoModelHelper — shared utilities for DWG-to-BIM operations
    // ════════════════════════════════════════════════════════════════════
    internal static class AutoModelHelper
    {
        /// <summary>Simple data class for CAD text items.</summary>
        internal class CadTextItem
        {
            public string Text { get; set; }
            public XYZ Position { get; set; }
        }

        /// <summary>Simple data class for CAD block references.</summary>
        internal class CadBlockItem
        {
            public string BlockName { get; set; }
            public XYZ Position { get; set; }
            public double Rotation { get; set; }
        }

        /// <summary>
        /// Extract line segments from CAD geometry (recursively traverses GeometryInstances).
        /// </summary>
        internal static void ExtractLinesFromGeometry(GeometryElement geoElem,
            Transform transform, List<Line> lines)
        {
            foreach (GeometryObject gObj in geoElem)
            {
                if (gObj is Line line)
                {
                    try
                    {
                        XYZ p0 = transform.OfPoint(line.GetEndPoint(0));
                        XYZ p1 = transform.OfPoint(line.GetEndPoint(1));
                        if (p0.DistanceTo(p1) > 0.01)
                            lines.Add(Line.CreateBound(p0, p1));
                    }
                    catch { }
                }
                else if (gObj is PolyLine polyline)
                {
                    var coords = polyline.GetCoordinates();
                    for (int i = 0; i < coords.Count - 1; i++)
                    {
                        try
                        {
                            XYZ p0 = transform.OfPoint(coords[i]);
                            XYZ p1 = transform.OfPoint(coords[i + 1]);
                            if (p0.DistanceTo(p1) > 0.01)
                                lines.Add(Line.CreateBound(p0, p1));
                        }
                        catch { }
                    }
                }
                else if (gObj is GeometryInstance geoInst)
                {
                    var instGeo = geoInst.GetInstanceGeometry();
                    if (instGeo != null)
                        ExtractLinesFromGeometry(instGeo, Transform.Identity, lines);
                }
            }
        }

        /// <summary>
        /// Extract text from CAD geometry (text notes, attributes).
        /// Note: Revit API doesn't directly expose CAD text; this extracts from
        /// GeometryInstance names and any available text data.
        /// </summary>
        internal static void ExtractTextFromGeometry(GeometryElement geoElem,
            Transform transform, List<CadTextItem> items)
        {
            foreach (GeometryObject gObj in geoElem)
            {
                if (gObj is GeometryInstance geoInst)
                {
                    // Block name may contain text info
                    string symbolName = geoInst.Symbol?.Name;
                    if (!string.IsNullOrEmpty(symbolName) && symbolName.Length > 1)
                    {
                        var instTransform = geoInst.Transform;
                        items.Add(new CadTextItem
                        {
                            Text = symbolName,
                            Position = transform.OfPoint(instTransform.Origin)
                        });
                    }

                    // Recurse into nested geometry
                    var instGeo = geoInst.GetInstanceGeometry();
                    if (instGeo != null)
                        ExtractTextFromGeometry(instGeo, Transform.Identity, items);
                }
            }
        }

        /// <summary>
        /// Extract block references from CAD geometry.
        /// </summary>
        internal static void ExtractBlocksFromGeometry(GeometryElement geoElem,
            Transform transform, List<CadBlockItem> items)
        {
            foreach (GeometryObject gObj in geoElem)
            {
                if (gObj is GeometryInstance geoInst)
                {
                    string symbolName = geoInst.Symbol?.Name;
                    if (!string.IsNullOrEmpty(symbolName))
                    {
                        var instTransform = geoInst.Transform;
                        XYZ origin = transform.OfPoint(instTransform.Origin);

                        // Extract rotation from transform
                        double rotation = 0;
                        try
                        {
                            XYZ basisX = instTransform.BasisX;
                            rotation = Math.Atan2(basisX.Y, basisX.X);
                        }
                        catch { }

                        items.Add(new CadBlockItem
                        {
                            BlockName = symbolName,
                            Position = origin,
                            Rotation = rotation
                        });
                    }
                }
            }
        }

        /// <summary>Check if text looks like a dimension/coordinate (not a room name).</summary>
        internal static bool IsDimensionText(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;

            // Pure numbers or numbers with units
            text = text.Trim();
            if (double.TryParse(text, out _)) return true;
            if (text.EndsWith("mm") || text.EndsWith("m") || text.EndsWith("\"") || text.EndsWith("'"))
            {
                string num = text.TrimEnd('m', '"', '\'', ' ');
                if (double.TryParse(num, out _)) return true;
            }

            // Coordinate-like text
            if (text.Contains(",") && text.Split(',').All(p => double.TryParse(p.Trim(), out _)))
                return true;

            // Grid labels (single letters/numbers)
            if (text.Length <= 2 && (char.IsDigit(text[0]) || char.IsUpper(text[0])))
                return true;

            return false;
        }

        /// <summary>Match DWG file name to Revit level by common patterns.</summary>
        internal static Level MatchFileToLevel(string fileName, List<Level> levels)
        {
            string upper = fileName.ToUpperInvariant();

            // Common patterns: L01, Level01, 01-plan, GF, Ground, B1, Basement
            foreach (var level in levels)
            {
                string lvlUpper = level.Name.ToUpperInvariant();

                // Direct name match
                if (upper.Contains(lvlUpper.Replace(" ", ""))) return level;

                // Level number extraction
                string lvlNum = new string(lvlUpper.Where(char.IsDigit).ToArray());
                string fileNum = new string(upper.Where(char.IsDigit).ToArray());
                if (!string.IsNullOrEmpty(lvlNum) && !string.IsNullOrEmpty(fileNum) && lvlNum == fileNum)
                    return level;
            }

            // Ground floor keywords
            if (upper.Contains("GF") || upper.Contains("GROUND") || upper.Contains("G00"))
                return levels.FirstOrDefault(l => l.Elevation >= -0.1 && l.Elevation <= 1.0)
                    ?? levels.First();

            // Basement keywords
            if (upper.Contains("BASE") || upper.Contains("B1") || upper.Contains("B01"))
                return levels.FirstOrDefault(l => l.Elevation < 0) ?? levels.First();

            // Roof keywords
            if (upper.Contains("ROOF") || upper.Contains("RF"))
                return levels.Last();

            return levels.First();
        }

        /// <summary>Map DWG layer name to Revit category using AEC naming conventions.</summary>
        internal static string MapLayerToCategory(string layerName)
        {
            string upper = layerName.ToUpperInvariant();
            string prefix = upper.Split(new[] { '-', '_', ' ' }, 2)[0];

            // AEC/ISO 13567 layer prefix mapping
            switch (prefix)
            {
                case "A": case "ARCH": case "AR": return "Architecture";
                case "S": case "STR": case "ST": return "Structural";
                case "M": case "MECH": case "ME": case "HVAC": return "Mechanical";
                case "E": case "ELEC": case "EL": return "Electrical";
                case "P": case "PLMB": case "PL": return "Plumbing";
                case "F": case "FIRE": case "FP": return "Fire Protection";
                case "L": case "LAND": case "LA": return "Landscape";
                case "C": case "CIVIL": case "CE": return "Civil";
                case "I": case "INT": return "Interiors";
                case "G": case "GEN": return "General";
            }

            // Content-based matching
            if (upper.Contains("WALL")) return "Walls";
            if (upper.Contains("DOOR")) return "Doors";
            if (upper.Contains("WIND")) return "Windows";
            if (upper.Contains("FURN")) return "Furniture";
            if (upper.Contains("STAIR")) return "Stairs";
            if (upper.Contains("COLM") || upper.Contains("COLUMN")) return "Structural";
            if (upper.Contains("BEAM")) return "Structural";
            if (upper.Contains("SLAB") || upper.Contains("FLOOR")) return "Floors";
            if (upper.Contains("CEIL")) return "Ceilings";
            if (upper.Contains("ROOF")) return "Roofs";
            if (upper.Contains("PIPE")) return "Pipes";
            if (upper.Contains("DUCT")) return "Ducts";
            if (upper.Contains("COND")) return "Conduits";
            if (upper.Contains("CABLE") || upper.Contains("TRAY")) return "Cable Trays";
            if (upper.Contains("LIGHT") || upper.Contains("LTG")) return "Lighting";
            if (upper.Contains("SPRINK")) return "Fire Protection";
            if (upper.Contains("GRID")) return "Grids";
            if (upper.Contains("DIM")) return "Dimensions";
            if (upper.Contains("TEXT") || upper.Contains("NOTE") || upper.Contains("ANNO")) return "Annotations";
            if (upper.Contains("HATCH") || upper.Contains("PATT")) return "Patterns";
            if (upper.Contains("DEFPOINTS") || upper.Contains("0")) return "Hidden";
            if (upper.Contains("XREF") || upper.Contains("REF")) return "Reference";

            return "Other";
        }

        /// <summary>Get description for AEC layer prefix.</summary>
        internal static string GetAecPrefixDescription(string prefix)
        {
            switch (prefix)
            {
                case "A": case "ARCH": case "AR": return "Architecture";
                case "S": case "STR": case "ST": return "Structural";
                case "M": case "MECH": case "ME": return "Mechanical";
                case "E": case "ELEC": case "EL": return "Electrical";
                case "P": case "PLMB": case "PL": return "Plumbing";
                case "F": case "FIRE": case "FP": return "Fire Protection";
                case "L": case "LAND": case "LA": return "Landscape";
                case "C": case "CIVIL": case "CE": return "Civil/Site";
                case "I": case "INT": return "Interiors";
                case "G": case "GEN": return "General";
                case "HVAC": return "HVAC Systems";
                case "XREF": return "External References";
                default: return "Unknown discipline";
            }
        }

        /// <summary>
        /// Find a family symbol matching a CAD block name by fuzzy matching.
        /// Tries exact match, then contains match, then keyword match.
        /// </summary>
        internal static FamilySymbol FindMatchingSymbol(
            List<FamilySymbol> symbols, string blockName)
        {
            if (string.IsNullOrEmpty(blockName) || symbols.Count == 0) return null;

            string upper = blockName.ToUpperInvariant();

            // Exact family name match
            var exact = symbols.FirstOrDefault(s =>
                s.Family.Name.Equals(blockName, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            // Exact symbol name match
            exact = symbols.FirstOrDefault(s =>
                s.Name.Equals(blockName, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            // Contains match (block name in family name or vice versa)
            var contains = symbols.FirstOrDefault(s =>
                s.Family.Name.ToUpperInvariant().Contains(upper) ||
                upper.Contains(s.Family.Name.ToUpperInvariant()));
            if (contains != null) return contains;

            // Keyword matching for common blocks
            if (upper.Contains("DOOR"))
                return symbols.FirstOrDefault(s => s.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_Doors);
            if (upper.Contains("WINDOW") || upper.Contains("WIN"))
                return symbols.FirstOrDefault(s => s.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_Windows);
            if (upper.Contains("CHAIR") || upper.Contains("DESK") || upper.Contains("TABLE") || upper.Contains("FURN"))
                return symbols.FirstOrDefault(s => s.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_Furniture);
            if (upper.Contains("LIGHT") || upper.Contains("LTG") || upper.Contains("LAMP"))
                return symbols.FirstOrDefault(s => s.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_LightingFixtures);
            if (upper.Contains("SPRINK"))
                return symbols.FirstOrDefault(s => s.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_Sprinklers);
            if (upper.Contains("TOILET") || upper.Contains("WC") || upper.Contains("SINK") || upper.Contains("BASIN"))
                return symbols.FirstOrDefault(s => s.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_PlumbingFixtures);
            if (upper.Contains("SWITCH") || upper.Contains("OUTLET") || upper.Contains("SOCKET"))
                return symbols.FirstOrDefault(s => s.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_ElectricalFixtures);

            return null;
        }
    }
}
