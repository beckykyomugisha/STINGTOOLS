// ============================================================================
// StructuralDWGCommands.cs — Command classes for DWG-to-Structural BIM
//
// Commands:
//   1. StructuralDWGWizardCommand — Full 7-page wizard
//   2. QuickStructuralDWGCommand — Quick mode with auto-detection
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Model
{
    /// <summary>
    /// Full 7-page structural DWG-to-BIM wizard with complete user control
    /// over layer mapping, properties, joining, and tagging.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StructuralDWGWizardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    var app = ParameterHelpers.GetApp(commandData);
                    doc = app?.ActiveUIDocument?.Document;
                }
                if (doc == null)
                {
                    TaskDialog.Show("STING", "No active document.");
                    return Result.Cancelled;
                }

                // Check for imported DWGs
                var imports = CADToModelEngine.FindImportInstances(doc);
                if (imports.Count == 0)
                {
                    TaskDialog.Show("STING Structural DWG",
                        "No imported DWG files found in the active document.\n\n" +
                        "Please import or link a structural DWG drawing first:\n" +
                        "  Insert → Link CAD / Import CAD");
                    return Result.Cancelled;
                }

                // Show wizard
                var config = StructuralDWGWizard.Show(doc);
                if (!config.Confirmed)
                    return Result.Cancelled;

                // Execute conversion
                var result = StructuralDWGEngine.Execute(doc, config);

                // Show results
                var td = new TaskDialog("STING Structural DWG Conversion")
                {
                    MainInstruction = result.CreatedElementIds.Count > 0
                        ? $"Successfully created {result.CreatedElementIds.Count} structural elements"
                        : "No elements were created",
                    MainContent = result.GetSummary(),
                    MainIcon = result.Errors == 0 ? TaskDialogIcon.TaskDialogIconInformation
                        : TaskDialogIcon.TaskDialogIconWarning,
                };
                td.Show();

                // Select created elements
                if (result.CreatedElementIds.Count > 0)
                {
                    try
                    {
                        var uidoc = commandData?.Application?.ActiveUIDocument;
                        if (uidoc == null)
                        {
                            var app = ParameterHelpers.GetApp(commandData);
                            uidoc = app?.ActiveUIDocument;
                        }
                        uidoc?.Selection?.SetElementIds(result.CreatedElementIds);
                    }
                    catch (Exception ex) { StingLog.Warn($"Selection: {ex.Message}"); }
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StructuralDWGWizardCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Quick structural DWG conversion with auto-detection and default settings.
    /// Shows a simplified confirmation dialog before proceeding.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class QuickStructuralDWGCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    var app = ParameterHelpers.GetApp(commandData);
                    doc = app?.ActiveUIDocument?.Document;
                }
                if (doc == null)
                {
                    TaskDialog.Show("STING", "No active document.");
                    return Result.Cancelled;
                }

                var imports = CADToModelEngine.FindImportInstances(doc);
                if (imports.Count == 0)
                {
                    TaskDialog.Show("STING", "No imported DWG files found.");
                    return Result.Cancelled;
                }

                // Quick confirm dialog
                var td = new TaskDialog("Quick Structural DWG Conversion")
                {
                    MainInstruction = "Convert DWG to Structural Model?",
                    MainContent = $"Found {imports.Count} DWG import(s).\n\n" +
                        "This will use auto-detection with default settings:\n" +
                        "  • Wall height: 3000mm, thickness: 200mm\n" +
                        "  • Column: 300×300mm, height: 3000mm\n" +
                        "  • Beam: 250×450mm\n" +
                        "  • Slab: 200mm thick\n" +
                        "  • Auto-join, auto-tag, create new types\n\n" +
                        "For full control, use the DWG Wizard instead.",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.Yes,
                };

                if (td.Show() != TaskDialogResult.Yes)
                    return Result.Cancelled;

                // Build default config with auto-mapped layers
                var config = new StructuralDWGConfig
                {
                    Confirmed = true,
                    SelectedImport = imports[0],
                    AutoTag = true,
                    AutoNumber = true,
                    CreateNewTypes = true,
                    AutoJoinWalls = true,
                    AutoJoinColumns = true,
                    AutoExtendBeams = true,
                    MergeCollinearWalls = true,
                    CreateGridLines = true,
                    DetectFoundations = true,
                    DetectShearWalls = true,
                };

                // Get base level
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation).ToList();
                if (levels.Count > 0) config.BaseLevelId = levels[0].Id;
                if (levels.Count > 1) config.TopLevelId = levels[1].Id;

                // Auto-map ALL layers (let the engine figure out types)
                // Use the DWG geometry to detect layers and auto-classify
                try
                {
                    var geoElem = imports[0].get_Geometry(new Options());
                    if (geoElem != null)
                    {
                        var layerNames = new HashSet<string>();
                        foreach (var geoObj in geoElem)
                        {
                            if (geoObj is GeometryInstance gi)
                            {
                                foreach (var subGeo in gi.GetInstanceGeometry())
                                {
                                    try
                                    {
                                        var gStyle = doc.GetElement(subGeo.GraphicsStyleId) as GraphicsStyle;
                                        var layer = gStyle?.GraphicsStyleCategory?.Name;
                                        if (!string.IsNullOrEmpty(layer)) layerNames.Add(layer);
                                    }
                                    catch (Exception ex) { StingLog.Warn($"Quick layer: {ex.Message}"); }
                                }
                            }
                        }

                        foreach (var layer in layerNames)
                        {
                            string cat = LayerMapper.InferCategory(layer) ?? "";
                            string elemType = cat switch
                            {
                                "Walls" => "Wall",
                                "Columns" => "Column",
                                "Beams" => "Beam",
                                "Floors" => "Slab",
                                "Grids" => "Grid Line",
                                _ => null
                            };

                            // Also check structural classifier
                            if (elemType == null && StructuralLayerClassifier.IsStructuralLayer(layer))
                            {
                                string lower = layer.ToLowerInvariant();
                                if (lower.Contains("wall") || lower.Contains("shear")) elemType = "Wall";
                                else if (lower.Contains("col")) elemType = "Column";
                                else if (lower.Contains("beam")) elemType = "Beam";
                                else if (lower.Contains("slab") || lower.Contains("floor")) elemType = "Slab";
                                else if (lower.Contains("found") || lower.Contains("foot")) elemType = "Foundation";
                                else if (lower.Contains("grid")) elemType = "Grid Line";
                                else elemType = "Wall"; // Default structural to wall
                            }

                            if (elemType != null)
                            {
                                if (!config.LayerMapping.ContainsKey(elemType))
                                    config.LayerMapping[elemType] = new System.Collections.Generic.List<string>();
                                config.LayerMapping[elemType].Add(layer);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Quick auto-map: {ex.Message}");
                }

                if (config.LayerMapping.Count == 0)
                {
                    TaskDialog.Show("STING", "No structural layers detected.\nUse the full DWG Wizard for manual mapping.");
                    return Result.Cancelled;
                }

                var result = StructuralDWGEngine.Execute(doc, config);

                TaskDialog.Show("STING Quick Structural",
                    result.CreatedElementIds.Count > 0
                        ? result.GetSummary()
                        : "No elements created. Try the full DWG Wizard for manual layer mapping.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("QuickStructuralDWGCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
