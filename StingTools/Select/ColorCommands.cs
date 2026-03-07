using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Select
{
    /// <summary>
    /// Color By Parameter system — Graitec/Naviate-inspired element coloring by any parameter value.
    /// Provides 10 built-in palettes, custom presets, &lt;No Value&gt; detection, and view filter generation.
    /// </summary>
    internal static class ColorHelper
    {
        // ── Built-in palettes ──────────────────────────────────────────
        public static readonly Dictionary<string, Color[]> Palettes =
            new Dictionary<string, Color[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["STING Discipline"] = new[]
            {
                new Color(0, 102, 204),    // M = Blue
                new Color(255, 204, 0),    // E = Gold
                new Color(0, 153, 51),     // P = Green
                new Color(153, 153, 153),  // A = Grey
                new Color(204, 0, 0),      // S = Red
                new Color(255, 128, 0),    // FP = Orange
                new Color(128, 0, 255),    // LV = Purple
                new Color(139, 90, 43),    // G = Brown
            },
            ["RAG Status"] = new[]
            {
                new Color(204, 0, 0),      // Red
                new Color(255, 165, 0),    // Amber
                new Color(0, 153, 51),     // Green
            },
            ["Monochrome"] = new[]
            {
                new Color(0, 0, 0),
                new Color(64, 64, 64),
                new Color(128, 128, 128),
                new Color(192, 192, 192),
                new Color(255, 255, 255),
            },
            ["Spectral"] = new[]
            {
                new Color(213, 62, 79),
                new Color(244, 109, 67),
                new Color(253, 174, 97),
                new Color(254, 224, 139),
                new Color(230, 245, 152),
                new Color(171, 221, 164),
                new Color(102, 194, 165),
                new Color(50, 136, 189),
            },
            ["Warm"] = new[]
            {
                new Color(180, 0, 0),
                new Color(220, 60, 0),
                new Color(255, 128, 0),
                new Color(255, 200, 50),
                new Color(255, 240, 180),
            },
            ["Cool"] = new[]
            {
                new Color(0, 0, 128),
                new Color(0, 51, 204),
                new Color(0, 153, 204),
                new Color(0, 204, 204),
                new Color(153, 230, 179),
            },
            ["Pastel"] = new[]
            {
                new Color(179, 205, 227),
                new Color(204, 235, 197),
                new Color(254, 217, 166),
                new Color(222, 203, 228),
                new Color(254, 178, 178),
                new Color(255, 255, 204),
                new Color(229, 216, 189),
                new Color(253, 218, 236),
            },
            ["High Contrast"] = new[]
            {
                new Color(255, 0, 0),
                new Color(0, 0, 255),
                new Color(0, 180, 0),
                new Color(255, 255, 0),
                new Color(255, 0, 255),
                new Color(0, 255, 255),
                new Color(255, 128, 0),
                new Color(128, 0, 255),
            },
            ["Accessible"] = new[]
            {
                new Color(68, 1, 84),
                new Color(72, 40, 120),
                new Color(62, 74, 137),
                new Color(49, 104, 142),
                new Color(38, 130, 142),
                new Color(31, 158, 137),
                new Color(53, 183, 121),
                new Color(109, 205, 89),
                new Color(180, 222, 44),
                new Color(253, 231, 37),
            },
        };

        /// <summary>Default color for elements with empty/null parameter values.</summary>
        public static readonly Color NoValueColor = new Color(255, 0, 0); // Red — instant QA

        /// <summary>Find the solid fill pattern (required for surface overrides).</summary>
        public static FillPatternElement FindSolidFill(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
        }

        /// <summary>Build OverrideGraphicSettings for a given color with full surface fill.</summary>
        public static OverrideGraphicSettings BuildOverride(Color color,
            FillPatternElement solidFill, int transparency = 0)
        {
            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(color);
            ogs.SetProjectionLineWeight(3);
            if (solidFill != null)
            {
                ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                ogs.SetSurfaceForegroundPatternColor(color);
                ogs.SetCutForegroundPatternId(solidFill.Id);
                ogs.SetCutForegroundPatternColor(color);
            }
            ogs.SetSurfaceTransparency(transparency);
            return ogs;
        }

        /// <summary>Get distinct parameter values from elements, grouping elements by value.</summary>
        public static Dictionary<string, List<ElementId>> GroupByParameterValue(
            Document doc, IEnumerable<Element> elements, string paramName)
        {
            var groups = new Dictionary<string, List<ElementId>>(StringComparer.OrdinalIgnoreCase);

            foreach (Element elem in elements)
            {
                string value = GetParameterValue(elem, paramName);
                if (string.IsNullOrWhiteSpace(value))
                    value = "<No Value>";

                if (!groups.TryGetValue(value, out var list))
                {
                    list = new List<ElementId>();
                    groups[value] = list;
                }
                list.Add(elem.Id);
            }

            return groups;
        }

        /// <summary>Read any parameter (instance or type, text or numeric) as string.</summary>
        public static string GetParameterValue(Element elem, string paramName)
        {
            // Try instance parameter first
            Parameter p = elem.LookupParameter(paramName);
            if (p == null)
            {
                // Try type parameter
                ElementId typeId = elem.GetTypeId();
                if (typeId != ElementId.InvalidElementId)
                {
                    Element type = elem.Document.GetElement(typeId);
                    if (type != null)
                        p = type.LookupParameter(paramName);
                }
            }
            if (p == null) return null;

            switch (p.StorageType)
            {
                case StorageType.String:
                    return p.AsString();
                case StorageType.Integer:
                    return p.AsInteger().ToString();
                case StorageType.Double:
                    return p.AsValueString() ?? p.AsDouble().ToString("F2");
                case StorageType.ElementId:
                    var eid = p.AsElementId();
                    if (eid == ElementId.InvalidElementId) return null;
                    var refElem = elem.Document.GetElement(eid);
                    return refElem?.Name ?? eid.ToString();
                default:
                    return null;
            }
        }

        /// <summary>Get available parameter names from a set of elements.</summary>
        public static List<string> GetAvailableParameters(Document doc, IEnumerable<Element> elements)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int sampled = 0;
            foreach (Element elem in elements)
            {
                foreach (Parameter p in elem.Parameters)
                {
                    if (p.Definition != null && !string.IsNullOrEmpty(p.Definition.Name))
                        names.Add(p.Definition.Name);
                }
                // Also check type parameters
                ElementId typeId = elem.GetTypeId();
                if (typeId != ElementId.InvalidElementId)
                {
                    Element type = doc.GetElement(typeId);
                    if (type != null)
                    {
                        foreach (Parameter p in type.Parameters)
                        {
                            if (p.Definition != null && !string.IsNullOrEmpty(p.Definition.Name))
                                names.Add(p.Definition.Name);
                        }
                    }
                }
                if (++sampled >= 50) break; // Sample enough for parameter list
            }
            var sorted = names.ToList();
            sorted.Sort(StringComparer.OrdinalIgnoreCase);
            return sorted;
        }

        /// <summary>Assign colors from palette to values (cycling if more values than colors).</summary>
        public static Dictionary<string, Color> AssignColors(
            IEnumerable<string> values, Color[] palette)
        {
            var map = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
            int i = 0;
            foreach (string val in values)
            {
                if (val == "<No Value>")
                    map[val] = NoValueColor;
                else
                    map[val] = palette[i++ % palette.Length];
            }
            return map;
        }

        // ── Preset persistence ─────────────────────────────────────────

        public static string PresetFilePath =>
            Path.Combine(StingToolsApp.DataPath ?? "", "COLOR_PRESETS.json");

        public static Dictionary<string, ColorPreset> LoadPresets()
        {
            string path = PresetFilePath;
            if (!File.Exists(path))
                return new Dictionary<string, ColorPreset>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<Dictionary<string, ColorPreset>>(json)
                    ?? new Dictionary<string, ColorPreset>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LoadPresets: {ex.Message}");
                return new Dictionary<string, ColorPreset>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public static void SavePresets(Dictionary<string, ColorPreset> presets)
        {
            string path = PresetFilePath;
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonConvert.SerializeObject(presets, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                StingLog.Error("SavePresets failed", ex);
            }
        }
    }

    /// <summary>Serializable color preset for save/load.</summary>
    public class ColorPreset
    {
        public string ParameterName { get; set; }
        public string PaletteName { get; set; }
        public Dictionary<string, int[]> ValueColors { get; set; } // value → [R, G, B]
    }

    // ════════════════════════════════════════════════════════════════════
    //  Color By Parameter — main command
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Color elements by any parameter value. User picks parameter and palette via dialogs.
    /// Supports active view scope, &lt;No Value&gt; detection (red), and all 10 built-in palettes.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ColorByParameterCommand : IExternalCommand, IPanelCommand
    {
        public Result Execute(UIApplication app)
        {
            string msg = "";
            var el = new ElementSet();
            return Execute(null, ref msg, el);
        }

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            if (uidoc == null) { TaskDialog.Show("STING Tools", "No document is open."); return Result.Failed; }
            Document doc = uidoc.Document;
            View view = doc.ActiveView;
            if (view == null) { TaskDialog.Show("Color By Parameter", "No active view."); return Result.Failed; }

            // Collect taggable elements in active view
            var elems = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.HasMaterialQuantities)
                .ToList();

            if (elems.Count == 0)
            {
                TaskDialog.Show("Color By Parameter", "No elements found in active view.");
                return Result.Succeeded;
            }

            // Step 1: Pick parameter
            var paramNames = ColorHelper.GetAvailableParameters(doc, elems);
            if (paramNames.Count == 0)
            {
                TaskDialog.Show("Color By Parameter", "No parameters found.");
                return Result.Succeeded;
            }

            // Show common tag params first, then all others
            var priorityParams = new[]
            {
                ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
                ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC,
                ParamRegistry.PROD, ParamRegistry.TAG1, "Mark",
                "Comments", "Type Name", "Family", "Level"
            };

            var dlg = new TaskDialog("Color By Parameter — Pick Parameter");
            dlg.MainInstruction = "Which parameter to color by?";

            // Show up to 4 command links for the most useful params
            var top4 = priorityParams.Where(p => paramNames.Contains(p)).Take(4).ToList();
            string selectedParam = null;

            if (top4.Count >= 1)
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    top4[0], $"Color by {top4[0]}");
            if (top4.Count >= 2)
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    top4[1], $"Color by {top4[1]}");
            if (top4.Count >= 3)
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    top4[2], $"Color by {top4[2]}");
            if (top4.Count >= 4)
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                    top4[3], $"Color by {top4[3]}");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;
            dlg.FooterText = $"{paramNames.Count} parameters available. " +
                "Cancel and re-run with elements selected to filter.";

            var pick = dlg.Show();
            switch (pick)
            {
                case TaskDialogResult.CommandLink1: selectedParam = top4[0]; break;
                case TaskDialogResult.CommandLink2: selectedParam = top4[1]; break;
                case TaskDialogResult.CommandLink3: selectedParam = top4[2]; break;
                case TaskDialogResult.CommandLink4: selectedParam = top4[3]; break;
                default: return Result.Cancelled;
            }

            // Step 2: Pick palette
            var palDlg = new TaskDialog("Color By Parameter — Pick Palette");
            palDlg.MainInstruction = $"Select color palette for '{selectedParam}'";
            palDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "STING Discipline", "M=Blue, E=Gold, P=Green, A=Grey (8 colors)");
            palDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "High Contrast", "Saturated primaries for QA checking (8 colors)");
            palDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Accessible", "Colorblind-safe viridis palette (10 colors)");
            palDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Spectral", "Rainbow gradient for continuous ranges (8 colors)");
            palDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            string paletteName;
            switch (palDlg.Show())
            {
                case TaskDialogResult.CommandLink1: paletteName = "STING Discipline"; break;
                case TaskDialogResult.CommandLink2: paletteName = "High Contrast"; break;
                case TaskDialogResult.CommandLink3: paletteName = "Accessible"; break;
                case TaskDialogResult.CommandLink4: paletteName = "Spectral"; break;
                default: return Result.Cancelled;
            }

            Color[] palette = ColorHelper.Palettes[paletteName];

            // Group elements by parameter value
            var groups = ColorHelper.GroupByParameterValue(doc, elems, selectedParam);
            var sortedValues = groups.Keys.OrderBy(v => v).ToList();
            var colorMap = ColorHelper.AssignColors(sortedValues, palette);

            // Find solid fill pattern
            var solidFill = ColorHelper.FindSolidFill(doc);

            int colored = 0;
            int noValueCount = groups.ContainsKey("<No Value>") ? groups["<No Value>"].Count : 0;

            using (Transaction tx = new Transaction(doc,
                $"STING Color By {selectedParam}"))
            {
                tx.Start();
                foreach (var kvp in groups)
                {
                    Color color = colorMap[kvp.Key];
                    var ogs = ColorHelper.BuildOverride(color, solidFill);
                    foreach (ElementId id in kvp.Value)
                    {
                        view.SetElementOverrides(id, ogs);
                        colored++;
                    }
                }
                tx.Commit();
            }

            // Build legend report
            var report = new StringBuilder();
            report.AppendLine($"Colored {colored} elements by '{selectedParam}'");
            report.AppendLine($"Palette: {paletteName}");
            report.AppendLine($"Unique values: {groups.Count}");
            if (noValueCount > 0)
                report.AppendLine($"⚠ {noValueCount} elements with <No Value> (RED)");
            report.AppendLine();
            report.AppendLine("── Legend ──");
            foreach (string val in sortedValues.Take(20))
            {
                Color c = colorMap[val];
                report.AppendLine($"  [{c.Red:D3},{c.Green:D3},{c.Blue:D3}]  {val}  ({groups[val].Count})");
            }
            if (sortedValues.Count > 20)
                report.AppendLine($"  ... and {sortedValues.Count - 20} more values");

            // Offer legend creation
            report.AppendLine();
            report.AppendLine("Click 'Yes' to create a persistent color legend (drafting view).");

            var resultDlg = new TaskDialog("Color By Parameter");
            resultDlg.MainContent = report.ToString();
            resultDlg.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
            resultDlg.DefaultButton = TaskDialogResult.No;

            if (resultDlg.Show() == TaskDialogResult.Yes)
            {
                var legendEntries = Tags.LegendBuilder.FromColorMap(colorMap, groups);
                var legendConfig = new Tags.LegendBuilder.LegendConfig
                {
                    Title = $"Color By {selectedParam}",
                    Subtitle = $"Palette: {paletteName} | {groups.Count} unique values",
                    Footer = $"Generated by STING Tools | View: {view.Name}",
                };

                using (Transaction ltx = new Transaction(doc, "STING Color Legend"))
                {
                    ltx.Start();
                    var legendView = Tags.LegendBuilder.CreateLegendView(doc, legendEntries, legendConfig);
                    ltx.Commit();

                    if (legendView != null)
                        TaskDialog.Show("Legend Created", $"Legend view: '{legendView.Name}'\nPlace on a sheet for documentation.");
                }
            }

            StingLog.Info($"ColorByParameter: param={selectedParam}, palette={paletteName}, " +
                $"elements={colored}, values={groups.Count}");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Clear Color Overrides
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Clear all per-element graphic overrides in the active view.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ClearColorOverridesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            if (uidoc == null) { TaskDialog.Show("STING Tools", "No document is open."); return Result.Failed; }
            Document doc = uidoc.Document;
            View view = doc.ActiveView;
            if (view == null) { TaskDialog.Show("Clear Overrides", "No active view."); return Result.Failed; }

            // Check selection first — clear only selected elements if any
            var selected = uidoc.Selection.GetElementIds();
            IEnumerable<ElementId> targetIds;
            string scope;

            if (selected.Count > 0)
            {
                targetIds = selected;
                scope = $"{selected.Count} selected elements";
            }
            else
            {
                targetIds = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .ToElementIds();
                scope = "all elements in active view";
            }

            var idList = targetIds.ToList();
            var blank = new OverrideGraphicSettings(); // default = clear

            int cleared = 0;
            using (Transaction tx = new Transaction(doc, "STING Clear Color Overrides"))
            {
                tx.Start();
                foreach (ElementId id in idList)
                {
                    var existing = view.GetElementOverrides(id);
                    // Check all override types — projection, surface, cut, and transparency
                    bool hasOverride =
                        existing.ProjectionLineColor.IsValid ||
                        existing.ProjectionLineWeight > 0 ||
                        existing.Halftone ||
                        existing.SurfaceForegroundPatternColor.IsValid ||
                        existing.SurfaceBackgroundPatternColor.IsValid ||
                        existing.CutLineColor.IsValid ||
                        existing.CutLineWeight > 0 ||
                        existing.CutForegroundPatternColor.IsValid ||
                        existing.CutBackgroundPatternColor.IsValid ||
                        existing.Transparency > 0 ||
                        existing.SurfaceForegroundPatternId != ElementId.InvalidElementId ||
                        existing.CutForegroundPatternId != ElementId.InvalidElementId;
                    if (hasOverride)
                    {
                        view.SetElementOverrides(id, blank);
                        cleared++;
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Clear Color Overrides",
                $"Cleared overrides from {cleared} elements ({scope}).");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Save Color Preset
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Save current view color overrides as a named preset to COLOR_PRESETS.json.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SaveColorPresetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            if (uidoc == null) { TaskDialog.Show("STING Tools", "No document is open."); return Result.Failed; }
            Document doc = uidoc.Document;
            View view = doc.ActiveView;
            if (view == null) { TaskDialog.Show("Save Preset", "No active view."); return Result.Failed; }

            // Scan elements for overrides
            var elems = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .ToList();

            var colorGroups = new Dictionary<string, List<ElementId>>();
            foreach (Element elem in elems)
            {
                var ogs = view.GetElementOverrides(elem.Id);
                // Check both surface and projection colors — BuildOverride sets both
                Color c = ogs.SurfaceForegroundPatternColor;
                if (!c.IsValid)
                    c = ogs.ProjectionLineColor;
                if (!c.IsValid) continue;

                string key = $"{c.Red},{c.Green},{c.Blue}";
                if (!colorGroups.ContainsKey(key))
                    colorGroups[key] = new List<ElementId>();
                colorGroups[key].Add(elem.Id);
            }

            if (colorGroups.Count == 0)
            {
                TaskDialog.Show("Save Color Preset",
                    "No color overrides found in active view.\n" +
                    "Apply Color By Parameter first, then save.");
                return Result.Succeeded;
            }

            // Ask for preset name
            TaskDialog nameDlg = new TaskDialog("Save Color Preset");
            nameDlg.MainInstruction = $"Save {colorGroups.Count} color groups as preset?";
            nameDlg.MainContent = $"Found {colorGroups.Count} distinct colors applied to " +
                $"{colorGroups.Values.Sum(v => v.Count)} elements.\n\n" +
                "Preset will be saved to COLOR_PRESETS.json in the data directory.";
            nameDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Save as 'Default'", "Save with preset name 'Default'");
            nameDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Save as 'Discipline'", "Save with preset name 'Discipline'");
            nameDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Save as 'QA Check'", "Save with preset name 'QA Check'");
            nameDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            string presetName;
            switch (nameDlg.Show())
            {
                case TaskDialogResult.CommandLink1: presetName = "Default"; break;
                case TaskDialogResult.CommandLink2: presetName = "Discipline"; break;
                case TaskDialogResult.CommandLink3: presetName = "QA Check"; break;
                default: return Result.Cancelled;
            }

            var preset = new ColorPreset
            {
                ParameterName = "Manual",
                PaletteName = "Custom",
                ValueColors = new Dictionary<string, int[]>()
            };

            int groupNum = 1;
            foreach (var kvp in colorGroups)
            {
                var parts = kvp.Key.Split(',');
                if (parts.Length < 3) { groupNum++; continue; }
                preset.ValueColors[$"Group_{groupNum++} ({kvp.Value.Count} elements)"] =
                    new[] { int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]) };
            }

            var presets = ColorHelper.LoadPresets();
            presets[presetName] = preset;
            ColorHelper.SavePresets(presets);

            TaskDialog.Show("Save Color Preset",
                $"Saved preset '{presetName}' with {colorGroups.Count} color groups " +
                $"to {ColorHelper.PresetFilePath}.");
            StingLog.Info($"SaveColorPreset: name={presetName}, groups={colorGroups.Count}");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Load Color Preset
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Load a named color preset from COLOR_PRESETS.json and apply to view.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LoadColorPresetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            if (uidoc == null) { TaskDialog.Show("STING Tools", "No document is open."); return Result.Failed; }
            Document doc = uidoc.Document;
            View view = doc.ActiveView;
            if (view == null) { TaskDialog.Show("Load Preset", "No active view."); return Result.Failed; }

            var presets = ColorHelper.LoadPresets();
            if (presets.Count == 0)
            {
                TaskDialog.Show("Load Color Preset",
                    "No presets found.\n" +
                    "Use 'Save Color Preset' to create one first.");
                return Result.Succeeded;
            }

            TaskDialog dlg = new TaskDialog("Load Color Preset");
            dlg.MainInstruction = $"Load a color preset ({presets.Count} available)";
            var presetNames = presets.Keys.ToList();

            if (presetNames.Count >= 1)
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    presetNames[0], $"Parameter: {presets[presetNames[0]].ParameterName}");
            if (presetNames.Count >= 2)
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    presetNames[1], $"Parameter: {presets[presetNames[1]].ParameterName}");
            if (presetNames.Count >= 3)
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    presetNames[2], $"Parameter: {presets[presetNames[2]].ParameterName}");
            if (presetNames.Count >= 4)
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                    presetNames[3], $"Parameter: {presets[presetNames[3]].ParameterName}");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            string selected;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: selected = presetNames[0]; break;
                case TaskDialogResult.CommandLink2: selected = presetNames[1]; break;
                case TaskDialogResult.CommandLink3: selected = presetNames[2]; break;
                case TaskDialogResult.CommandLink4: selected = presetNames[3]; break;
                default: return Result.Cancelled;
            }

            var preset = presets[selected];
            string paramName = preset.ParameterName;

            if (paramName == "Manual" || string.IsNullOrEmpty(paramName))
            {
                TaskDialog.Show("Load Color Preset",
                    $"Preset '{selected}' was saved from manual overrides.\n" +
                    "Re-apply using Color By Parameter with the original parameter.");
                return Result.Succeeded;
            }

            // Re-apply: group elements by parameter, assign colors from preset
            var elems = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null)
                .ToList();

            var groups = ColorHelper.GroupByParameterValue(doc, elems, paramName);
            var solidFill = ColorHelper.FindSolidFill(doc);

            int colored = 0;
            using (Transaction tx = new Transaction(doc, $"STING Load Preset '{selected}'"))
            {
                tx.Start();
                foreach (var kvp in groups)
                {
                    Color color;
                    if (preset.ValueColors.TryGetValue(kvp.Key, out int[] rgb))
                        color = new Color((byte)rgb[0], (byte)rgb[1], (byte)rgb[2]);
                    else
                        color = ColorHelper.NoValueColor;

                    var ogs = ColorHelper.BuildOverride(color, solidFill);
                    foreach (ElementId id in kvp.Value)
                    {
                        view.SetElementOverrides(id, ogs);
                        colored++;
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Load Color Preset",
                $"Applied preset '{selected}': colored {colored} elements.");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Create Filters From Colors
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Convert current per-element color overrides into persistent Revit ParameterFilterElements.
    /// These survive dialog close, are visible in View Templates, and can be applied to other views.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateFiltersFromColorsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            if (uidoc == null) { TaskDialog.Show("STING Tools", "No document is open."); return Result.Failed; }
            Document doc = uidoc.Document;
            View view = doc.ActiveView;
            if (view == null) { TaskDialog.Show("Create Filters", "No active view."); return Result.Failed; }

            // Ask which parameter was used for coloring
            TaskDialog paramDlg = new TaskDialog("Create Filters — Parameter");
            paramDlg.MainInstruction = "Which parameter were elements colored by?";
            paramDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                ParamRegistry.DISC, "Discipline code (most common)");
            paramDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                ParamRegistry.SYS, "System type");
            paramDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                ParamRegistry.LOC, "Location code");
            paramDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                ParamRegistry.ZONE, "Zone code");
            paramDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            string paramName;
            switch (paramDlg.Show())
            {
                case TaskDialogResult.CommandLink1: paramName = ParamRegistry.DISC; break;
                case TaskDialogResult.CommandLink2: paramName = ParamRegistry.SYS; break;
                case TaskDialogResult.CommandLink3: paramName = ParamRegistry.LOC; break;
                case TaskDialogResult.CommandLink4: paramName = ParamRegistry.ZONE; break;
                default: return Result.Cancelled;
            }

            // Get distinct values from elements
            var elems = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null)
                .ToList();

            var groups = ColorHelper.GroupByParameterValue(doc, elems, paramName);
            if (groups.Count == 0)
            {
                TaskDialog.Show("Create Filters", "No values found for this parameter.");
                return Result.Succeeded;
            }

            // Get the shared parameter ID for filter rules
            ParameterElement paramElem = null;
            foreach (ParameterElement pe in new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterElement)))
            {
                if (pe.GetDefinition()?.Name == paramName)
                {
                    paramElem = pe;
                    break;
                }
            }

            if (paramElem == null)
            {
                TaskDialog.Show("Create Filters",
                    $"Parameter '{paramName}' not found as a project parameter.\n" +
                    "Run 'Load Params' first.");
                return Result.Succeeded;
            }

            // Get categories that support this parameter
            var categories = new List<ElementId>();
            foreach (Category cat in doc.Settings.Categories)
            {
                if (cat.AllowsBoundParameters && cat.CategoryType == CategoryType.Model)
                    categories.Add(cat.Id);
            }

            if (categories.Count == 0)
            {
                TaskDialog.Show("Create Filters", "No model categories found.");
                return Result.Succeeded;
            }

            // Create filters and apply overrides
            var solidFill = ColorHelper.FindSolidFill(doc);
            var palette = ColorHelper.Palettes["STING Discipline"];
            int created = 0;
            int existing = 0;

            using (Transaction tx = new Transaction(doc, "STING Create Color Filters"))
            {
                tx.Start();
                int colorIdx = 0;
                foreach (var kvp in groups.OrderBy(g => g.Key))
                {
                    if (kvp.Key == "<No Value>") continue;

                    string filterName = $"STING {paramName.Replace("ASS_", "").Replace("_TXT", "")} = {kvp.Key}";

                    // Check if filter already exists
                    var existingFilter = new FilteredElementCollector(doc)
                        .OfClass(typeof(ParameterFilterElement))
                        .Cast<ParameterFilterElement>()
                        .FirstOrDefault(f => f.Name == filterName);

                    if (existingFilter != null)
                    {
                        existing++;
                        continue;
                    }

                    try
                    {
                        // Create filter rule
                        var rule = ParameterFilterRuleFactory.CreateEqualsRule(
                            paramElem.Id, kvp.Key);
                        var filter = ParameterFilterElement.Create(
                            doc, filterName, categories,
                            new ElementParameterFilter(rule));

                        // Add filter to view with color override
                        Color color = palette[colorIdx++ % palette.Length];
                        var ogs = ColorHelper.BuildOverride(color, solidFill);
                        view.AddFilter(filter.Id);
                        view.SetFilterOverrides(filter.Id, ogs);
                        created++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"CreateFilter '{filterName}': {ex.Message}");
                    }
                }
                tx.Commit();
            }

            string result2 = $"Created {created} view filters for '{paramName}'.";
            if (existing > 0) result2 += $"\n{existing} filters already existed (skipped).";
            result2 += "\n\nFilters are now visible in Visibility/Graphics → Filters tab.";
            TaskDialog.Show("Create Filters", result2);
            StingLog.Info($"CreateFiltersFromColors: param={paramName}, created={created}");
            return Result.Succeeded;
        }
    }
}
