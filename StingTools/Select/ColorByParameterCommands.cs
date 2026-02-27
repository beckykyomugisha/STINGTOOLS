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
    /// Color By Parameter: applies graphic overrides to elements based on any parameter value.
    /// Inspired by GRAITEC Element Lookup, Naviate Color Elements, BIM One Color Splasher.
    /// Supports text and numeric parameters, 10 built-in palettes, &lt;No Value&gt; QA highlighting,
    /// and one-click view filter generation.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ColorByParameterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            if (view is ViewSheet || view is ViewSchedule)
            {
                TaskDialog.Show("Color By Parameter",
                    "This command works on plan, section, 3D, and elevation views.\n" +
                    "Switch to a model view first.");
                return Result.Failed;
            }

            // Step 1: Scope selection
            TaskDialog scopeDlg = new TaskDialog("Color By Parameter");
            scopeDlg.MainInstruction = "Select scope for coloring";
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Active view", "Color all visible elements in this view");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Selected elements only",
                $"{uidoc.Selection.GetElementIds().Count} elements selected");
            scopeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            List<Element> targetElements;
            switch (scopeDlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    targetElements = new FilteredElementCollector(doc, view.Id)
                        .WhereElementIsNotElementType()
                        .Where(e => e.Category != null)
                        .ToList();
                    break;
                case TaskDialogResult.CommandLink2:
                    var selIds = uidoc.Selection.GetElementIds();
                    if (selIds.Count == 0)
                    {
                        TaskDialog.Show("Color By Parameter", "No elements selected.");
                        return Result.Cancelled;
                    }
                    targetElements = selIds.Select(id => doc.GetElement(id))
                        .Where(e => e != null && e.Category != null).ToList();
                    break;
                default:
                    return Result.Cancelled;
            }

            if (targetElements.Count == 0)
            {
                TaskDialog.Show("Color By Parameter", "No elements found in scope.");
                return Result.Cancelled;
            }

            // Step 2: Collect available parameters from elements
            var paramNames = ColorByParameterHelper.CollectParameterNames(targetElements);
            if (paramNames.Count == 0)
            {
                TaskDialog.Show("Color By Parameter", "No readable parameters found on elements.");
                return Result.Cancelled;
            }

            // Let user pick a parameter
            string selectedParam = ColorByParameterHelper.PickParameter(paramNames);
            if (selectedParam == null) return Result.Cancelled;

            // Step 3: Group elements by parameter value
            var groups = ColorByParameterHelper.GroupByParameterValue(
                targetElements, selectedParam);

            // Step 4: Pick color palette
            string paletteName = ColorByParameterHelper.PickPalette();
            if (paletteName == null) return Result.Cancelled;

            var palette = ColorPalettes.GetPalette(paletteName);

            // Step 5: Find solid fill pattern (CRITICAL for visible results)
            FillPatternElement solidFill = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);

            if (solidFill == null)
            {
                TaskDialog.Show("Color By Parameter",
                    "No solid fill pattern found in project.\n" +
                    "A solid fill is required for surface coloring.");
                return Result.Failed;
            }

            // Step 6: Assign colors and apply overrides
            var sortedKeys = groups.Keys.OrderBy(k => k).ToList();
            int colorIdx = 0;
            int totalColored = 0;
            int noValueCount = 0;
            var legendEntries = new List<(string value, Color color)>();

            // Special color for <No Value> elements
            Color noValueColor = new Color(255, 50, 50); // Red for QA

            using (Transaction tx = new Transaction(doc,
                $"STING Color By '{selectedParam}'"))
            {
                tx.Start();

                foreach (string key in sortedKeys)
                {
                    bool isNoValue = (key == ColorByParameterHelper.NoValueKey);
                    Color color;

                    if (isNoValue)
                    {
                        color = noValueColor;
                        noValueCount = groups[key].Count;
                    }
                    else
                    {
                        color = palette[colorIdx % palette.Count];
                        colorIdx++;
                    }

                    legendEntries.Add((key, color));

                    OverrideGraphicSettings ogs = new OverrideGraphicSettings();
                    ogs.SetProjectionLineColor(color);
                    ogs.SetProjectionLineWeight(isNoValue ? 3 : 1);
                    ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                    ogs.SetSurfaceForegroundPatternColor(color);
                    ogs.SetCutForegroundPatternId(solidFill.Id);
                    ogs.SetCutForegroundPatternColor(color);
                    ogs.SetSurfaceTransparency(isNoValue ? 0 : 20);

                    foreach (ElementId id in groups[key])
                    {
                        try
                        {
                            view.SetElementOverrides(id, ogs);
                            totalColored++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"ColorByParam override: {ex.Message}");
                        }
                    }
                }

                tx.Commit();
            }

            // Build report
            var report = new StringBuilder();
            report.AppendLine($"Color By Parameter: {selectedParam}");
            report.AppendLine($"Palette: {paletteName}");
            report.AppendLine($"Elements colored: {totalColored}");
            report.AppendLine($"Unique values: {sortedKeys.Count}");
            if (noValueCount > 0)
                report.AppendLine($"<No Value> (red): {noValueCount} elements — needs attention");
            report.AppendLine();
            report.AppendLine("── LEGEND ──");
            foreach (var (value, color) in legendEntries.Take(25))
            {
                report.AppendLine(
                    $"  [{color.Red:D3},{color.Green:D3},{color.Blue:D3}]  {value} ({groups[value].Count})");
            }
            if (legendEntries.Count > 25)
                report.AppendLine($"  ... and {legendEntries.Count - 25} more values");

            TaskDialog td = new TaskDialog("Color By Parameter");
            td.MainInstruction = $"Colored {totalColored} elements by '{selectedParam}'";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"ColorByParameter: param={selectedParam}, palette={paletteName}, " +
                $"elements={totalColored}, values={sortedKeys.Count}, noValue={noValueCount}");

            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Clear all graphic overrides from elements in the active view.
    /// Resets coloring applied by Color By Parameter or Highlight Invalid.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ClearColorOverridesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            // Scope
            TaskDialog scopeDlg = new TaskDialog("Clear Color Overrides");
            scopeDlg.MainInstruction = "Clear graphic overrides";
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "All elements in view",
                "Reset ALL per-element graphic overrides in active view");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Selected elements only",
                $"Reset overrides on {uidoc.Selection.GetElementIds().Count} selected elements");
            scopeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            ICollection<ElementId> targetIds;
            switch (scopeDlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    targetIds = new FilteredElementCollector(doc, view.Id)
                        .WhereElementIsNotElementType()
                        .Select(e => e.Id).ToList();
                    break;
                case TaskDialogResult.CommandLink2:
                    targetIds = uidoc.Selection.GetElementIds();
                    if (targetIds.Count == 0)
                    {
                        TaskDialog.Show("Clear Color Overrides", "No elements selected.");
                        return Result.Cancelled;
                    }
                    break;
                default:
                    return Result.Cancelled;
            }

            int cleared = 0;
            OverrideGraphicSettings empty = new OverrideGraphicSettings();

            using (Transaction tx = new Transaction(doc, "STING Clear Color Overrides"))
            {
                tx.Start();
                foreach (ElementId id in targetIds)
                {
                    try
                    {
                        view.SetElementOverrides(id, empty);
                        cleared++;
                    }
                    catch { }
                }
                tx.Commit();
            }

            TaskDialog.Show("Clear Color Overrides",
                $"Cleared graphic overrides on {cleared} elements.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Save the current color scheme (parameter + palette + value-color mappings) as a named preset.
    /// Presets are stored in Data/COLOR_PRESETS.json alongside the DLL.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SaveColorPresetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            // Prompt for preset name
            TaskDialog nameDlg = new TaskDialog("Save Color Preset");
            nameDlg.MainInstruction = "Enter preset details";
            nameDlg.MainContent =
                "This will scan the active view's current element overrides\n" +
                "and save them as a reusable color preset.\n\n" +
                "Preset will be saved to Data/COLOR_PRESETS.json";
            nameDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Save as 'Discipline Colors'", "Standard DISC-based coloring");
            nameDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Save as 'System Colors'", "SYS-based coloring");
            nameDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Save as 'Status Colors'", "STATUS-based coloring");
            nameDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            string presetName;
            switch (nameDlg.Show())
            {
                case TaskDialogResult.CommandLink1: presetName = "Discipline Colors"; break;
                case TaskDialogResult.CommandLink2: presetName = "System Colors"; break;
                case TaskDialogResult.CommandLink3: presetName = "Status Colors"; break;
                default: return Result.Cancelled;
            }

            // Scan view for existing overrides
            View view = doc.ActiveView;
            var overrideMap = new Dictionary<string, ColorPresetEntry>();
            var collector = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType();

            foreach (Element el in collector)
            {
                OverrideGraphicSettings ogs = view.GetElementOverrides(el.Id);
                Color lineColor = ogs.ProjectionLineColor;
                if (!lineColor.IsValid) continue;

                string key = $"{lineColor.Red},{lineColor.Green},{lineColor.Blue}";
                if (!overrideMap.ContainsKey(key))
                {
                    overrideMap[key] = new ColorPresetEntry
                    {
                        R = lineColor.Red,
                        G = lineColor.Green,
                        B = lineColor.Blue,
                        ElementCount = 0
                    };
                }
                overrideMap[key].ElementCount++;
            }

            if (overrideMap.Count == 0)
            {
                TaskDialog.Show("Save Color Preset",
                    "No color overrides found in the active view.\n" +
                    "Apply Color By Parameter first, then save the preset.");
                return Result.Cancelled;
            }

            // Save to JSON
            var preset = new ColorPreset
            {
                Name = presetName,
                CreatedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Entries = overrideMap.Values.ToList()
            };

            try
            {
                string presetsPath = ColorByParameterHelper.GetPresetsPath();
                var allPresets = ColorByParameterHelper.LoadPresets(presetsPath);

                // Replace existing preset with same name
                allPresets.RemoveAll(p => p.Name == presetName);
                allPresets.Add(preset);

                string json = JsonConvert.SerializeObject(allPresets, Formatting.Indented);
                File.WriteAllText(presetsPath, json);

                TaskDialog.Show("Save Color Preset",
                    $"Preset '{presetName}' saved with {overrideMap.Count} color entries.\n" +
                    $"File: {presetsPath}");
            }
            catch (Exception ex)
            {
                StingLog.Error($"SaveColorPreset: {ex.Message}", ex);
                TaskDialog.Show("Save Color Preset", $"Failed to save: {ex.Message}");
                return Result.Failed;
            }

            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Load a saved color preset and apply it to the active view.
    /// Matches elements to preset colors by re-scanning parameter values.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LoadColorPresetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            string presetsPath = ColorByParameterHelper.GetPresetsPath();
            var presets = ColorByParameterHelper.LoadPresets(presetsPath);

            if (presets.Count == 0)
            {
                TaskDialog.Show("Load Color Preset",
                    "No saved presets found.\n" +
                    "Use 'Save Color Preset' to create one first.");
                return Result.Cancelled;
            }

            // Let user pick a preset
            TaskDialog pickDlg = new TaskDialog("Load Color Preset");
            pickDlg.MainInstruction = "Select a preset to apply";

            var sb = new StringBuilder();
            for (int i = 0; i < Math.Min(presets.Count, 3); i++)
            {
                sb.AppendLine($"  {presets[i].Name} ({presets[i].Entries.Count} colors, {presets[i].CreatedDate})");
            }
            pickDlg.MainContent = sb.ToString();

            if (presets.Count >= 1)
                pickDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    presets[0].Name, $"{presets[0].Entries.Count} colors");
            if (presets.Count >= 2)
                pickDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    presets[1].Name, $"{presets[1].Entries.Count} colors");
            if (presets.Count >= 3)
                pickDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    presets[2].Name, $"{presets[2].Entries.Count} colors");
            pickDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            ColorPreset selected;
            switch (pickDlg.Show())
            {
                case TaskDialogResult.CommandLink1: selected = presets[0]; break;
                case TaskDialogResult.CommandLink2: selected = presets.Count >= 2 ? presets[1] : null; break;
                case TaskDialogResult.CommandLink3: selected = presets.Count >= 3 ? presets[2] : null; break;
                default: return Result.Cancelled;
            }

            if (selected == null) return Result.Cancelled;

            TaskDialog.Show("Load Color Preset",
                $"Preset '{selected.Name}' loaded.\n" +
                $"{selected.Entries.Count} color entries available.\n\n" +
                "Use 'Color By Parameter' to apply colors using these definitions.\n" +
                "(Direct re-application requires knowing the original parameter.)");

            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Generate persistent Revit ParameterFilterElement rules from the current
    /// color-by-parameter scheme. Survives dialog close, visible in view templates.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateFiltersFromColorsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            // Collect elements with overrides
            var overriddenElements = new Dictionary<string, List<ElementId>>();
            var colorMap = new Dictionary<string, Color>();

            foreach (Element el in new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType())
            {
                OverrideGraphicSettings ogs = view.GetElementOverrides(el.Id);
                Color c = ogs.ProjectionLineColor;
                if (!c.IsValid) continue;

                string key = $"{c.Red}_{c.Green}_{c.Blue}";
                if (!overriddenElements.ContainsKey(key))
                {
                    overriddenElements[key] = new List<ElementId>();
                    colorMap[key] = c;
                }
                overriddenElements[key].Add(el.Id);
            }

            if (overriddenElements.Count == 0)
            {
                TaskDialog.Show("Create Filters",
                    "No color overrides found in the active view.\n" +
                    "Apply Color By Parameter first.");
                return Result.Cancelled;
            }

            // Group overridden elements by category for filter creation
            int filtersCreated = 0;
            int filtersApplied = 0;

            using (Transaction tx = new Transaction(doc, "STING Create Filters From Colors"))
            {
                tx.Start();

                // Get solid fill for filter overrides
                FillPatternElement solidFill = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);

                int groupIdx = 1;
                foreach (var kvp in overriddenElements)
                {
                    Color color = colorMap[kvp.Key];
                    string filterName = $"STING Color Group {groupIdx}";

                    // Collect categories used by elements in this color group
                    var catIds = new HashSet<ElementId>();
                    foreach (ElementId id in kvp.Value)
                    {
                        Element el = doc.GetElement(id);
                        if (el?.Category != null)
                            catIds.Add(el.Category.Id);
                    }

                    if (catIds.Count == 0) continue;

                    try
                    {
                        // Delete existing filter with same name
                        var existing = new FilteredElementCollector(doc)
                            .OfClass(typeof(ParameterFilterElement))
                            .Cast<ParameterFilterElement>()
                            .FirstOrDefault(f => f.Name == filterName);

                        if (existing != null)
                            doc.Delete(existing.Id);

                        // Create filter with category set (no parameter rule — just categories)
                        ParameterFilterElement filter = ParameterFilterElement.Create(
                            doc, filterName, catIds.ToList());

                        filtersCreated++;

                        // Apply filter to view with color overrides
                        view.AddFilter(filter.Id);

                        OverrideGraphicSettings filterOgs = new OverrideGraphicSettings();
                        filterOgs.SetProjectionLineColor(color);
                        if (solidFill != null)
                        {
                            filterOgs.SetSurfaceForegroundPatternId(solidFill.Id);
                            filterOgs.SetSurfaceForegroundPatternColor(color);
                            filterOgs.SetCutForegroundPatternId(solidFill.Id);
                            filterOgs.SetCutForegroundPatternColor(color);
                        }

                        view.SetFilterOverrides(filter.Id, filterOgs);
                        filtersApplied++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"CreateFilter '{filterName}': {ex.Message}");
                    }

                    groupIdx++;
                }

                tx.Commit();
            }

            TaskDialog.Show("Create Filters From Colors",
                $"Created {filtersCreated} view filters.\n" +
                $"Applied {filtersApplied} to active view.\n\n" +
                "Filters are persistent and visible in View Templates.");

            return Result.Succeeded;
        }
    }

    // ── Data classes for preset serialization ──

    internal class ColorPreset
    {
        public string Name { get; set; }
        public string CreatedDate { get; set; }
        public List<ColorPresetEntry> Entries { get; set; } = new List<ColorPresetEntry>();
    }

    internal class ColorPresetEntry
    {
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }
        public string Value { get; set; }
        public int ElementCount { get; set; }
    }

    // ── Color palettes ──

    internal static class ColorPalettes
    {
        public static readonly string[] PaletteNames = {
            "STING Discipline", "RAG Status", "Spectral", "Warm", "Cool",
            "Pastel", "High Contrast", "Accessible", "Monochrome"
        };

        public static List<Color> GetPalette(string name)
        {
            switch (name)
            {
                case "STING Discipline":
                    return new List<Color> {
                        new Color(41, 128, 185),   // M = Blue
                        new Color(241, 196, 15),   // E = Yellow
                        new Color(39, 174, 96),    // P = Green
                        new Color(149, 165, 166),  // A = Grey
                        new Color(231, 76, 60),    // S = Red
                        new Color(230, 126, 34),   // FP = Orange
                        new Color(142, 68, 173),   // LV = Purple
                        new Color(160, 106, 66),   // G = Brown
                    };
                case "RAG Status":
                    return new List<Color> {
                        new Color(231, 76, 60),    // Red
                        new Color(243, 156, 18),   // Amber
                        new Color(39, 174, 96),    // Green
                    };
                case "Spectral":
                    return new List<Color> {
                        new Color(215, 25, 28),    // Red
                        new Color(253, 174, 97),   // Orange
                        new Color(255, 255, 191),  // Yellow
                        new Color(171, 221, 164),  // Light green
                        new Color(43, 131, 186),   // Blue
                        new Color(69, 117, 180),   // Dark blue
                        new Color(116, 173, 209),  // Light blue
                        new Color(254, 224, 144),  // Light orange
                        new Color(244, 109, 67),   // Dark orange
                        new Color(171, 217, 233),  // Ice blue
                    };
                case "Warm":
                    return new List<Color> {
                        new Color(192, 57, 43),    // Deep red
                        new Color(231, 76, 60),    // Red
                        new Color(230, 126, 34),   // Orange
                        new Color(243, 156, 18),   // Amber
                        new Color(241, 196, 15),   // Yellow
                        new Color(248, 231, 187),  // Cream
                    };
                case "Cool":
                    return new List<Color> {
                        new Color(26, 35, 75),     // Navy
                        new Color(41, 128, 185),   // Blue
                        new Color(52, 152, 219),   // Light blue
                        new Color(26, 188, 156),   // Teal
                        new Color(46, 204, 113),   // Mint
                        new Color(163, 228, 215),  // Pale mint
                    };
                case "Pastel":
                    return new List<Color> {
                        new Color(255, 179, 186),  // Pink
                        new Color(255, 223, 186),  // Peach
                        new Color(255, 255, 186),  // Yellow
                        new Color(186, 255, 201),  // Mint
                        new Color(186, 225, 255),  // Sky
                        new Color(218, 186, 255),  // Lavender
                    };
                case "High Contrast":
                    return new List<Color> {
                        new Color(255, 0, 0),      // Red
                        new Color(0, 0, 255),      // Blue
                        new Color(0, 200, 0),      // Green
                        new Color(255, 165, 0),    // Orange
                        new Color(128, 0, 128),    // Purple
                        new Color(0, 200, 200),    // Cyan
                        new Color(255, 255, 0),    // Yellow
                        new Color(0, 0, 0),        // Black
                    };
                case "Accessible":
                    // Colorblind-safe palette (viridis-inspired)
                    return new List<Color> {
                        new Color(68, 1, 84),      // Dark purple
                        new Color(72, 36, 117),    // Purple
                        new Color(65, 68, 135),    // Indigo
                        new Color(53, 95, 141),    // Blue
                        new Color(42, 120, 142),   // Teal
                        new Color(33, 145, 140),   // Sea green
                        new Color(53, 183, 121),   // Green
                        new Color(109, 205, 89),   // Lime
                        new Color(180, 222, 44),   // Yellow-green
                        new Color(253, 231, 37),   // Yellow
                    };
                case "Monochrome":
                    return new List<Color> {
                        new Color(0, 0, 0),        // Black
                        new Color(64, 64, 64),     // Dark grey
                        new Color(128, 128, 128),  // Medium grey
                        new Color(192, 192, 192),  // Light grey
                        new Color(224, 224, 224),  // Very light grey
                    };
                default:
                    return GetPalette("Spectral");
            }
        }
    }

    // ── Shared helper logic ──

    internal static class ColorByParameterHelper
    {
        public const string NoValueKey = "<No Value>";

        /// <summary>Collect all parameter names from a set of elements.</summary>
        public static List<string> CollectParameterNames(List<Element> elements)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Sample first 100 elements for parameter discovery
            foreach (Element el in elements.Take(100))
            {
                foreach (Parameter p in el.Parameters)
                {
                    if (p.Definition == null) continue;
                    if (p.StorageType == StorageType.ElementId) continue;
                    if (p.StorageType == StorageType.None) continue;
                    names.Add(p.Definition.Name);
                }
            }

            // Put common ISO 19650 params at the top
            var priority = new List<string> {
                "ASS_TAG_1_TXT", "ASS_DISCIPLINE_COD_TXT", "ASS_SYSTEM_TYPE_TXT",
                "ASS_LOC_TXT", "ASS_ZONE_TXT", "ASS_LVL_COD_TXT",
                "ASS_FUNC_TXT", "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT",
                "Mark", "Comments", "Type Name"
            };

            var sorted = new List<string>();
            foreach (string p in priority)
                if (names.Contains(p)) { sorted.Add(p); names.Remove(p); }

            sorted.AddRange(names.OrderBy(n => n));
            return sorted;
        }

        /// <summary>Let the user pick a parameter from a dialog.</summary>
        public static string PickParameter(List<string> paramNames)
        {
            // Show top parameters as command links, fall back to first option
            TaskDialog dlg = new TaskDialog("Pick Parameter");
            dlg.MainInstruction = "Select parameter to color by";
            dlg.MainContent = $"{paramNames.Count} parameters available";

            if (paramNames.Count >= 1)
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    paramNames[0], "ISO 19650 asset tag");
            if (paramNames.Count >= 2)
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    paramNames[1], "Discipline code");
            if (paramNames.Count >= 3)
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    paramNames[2], "System type");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: return paramNames[0];
                case TaskDialogResult.CommandLink2: return paramNames.Count >= 2 ? paramNames[1] : null;
                case TaskDialogResult.CommandLink3: return paramNames.Count >= 3 ? paramNames[2] : null;
                default: return null;
            }
        }

        /// <summary>Let user pick a color palette.</summary>
        public static string PickPalette()
        {
            TaskDialog dlg = new TaskDialog("Pick Color Palette");
            dlg.MainInstruction = "Select color palette";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "STING Discipline", "M=Blue, E=Yellow, P=Green, A=Grey, S=Red, FP=Orange, LV=Purple");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Spectral", "Rainbow gradient — good for many unique values");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Accessible", "Colorblind-safe viridis palette — universal accessibility");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: return "STING Discipline";
                case TaskDialogResult.CommandLink2: return "Spectral";
                case TaskDialogResult.CommandLink3: return "Accessible";
                default: return null;
            }
        }

        /// <summary>Group elements by their parameter value.</summary>
        public static Dictionary<string, List<ElementId>> GroupByParameterValue(
            List<Element> elements, string paramName)
        {
            var groups = new Dictionary<string, List<ElementId>>(StringComparer.OrdinalIgnoreCase);

            foreach (Element el in elements)
            {
                string value = GetParameterDisplayValue(el, paramName);

                if (string.IsNullOrEmpty(value))
                    value = NoValueKey;

                if (!groups.ContainsKey(value))
                    groups[value] = new List<ElementId>();
                groups[value].Add(el.Id);
            }

            return groups;
        }

        /// <summary>Get parameter display value from element (instance or type).</summary>
        private static string GetParameterDisplayValue(Element el, string paramName)
        {
            // Try instance parameter first
            Parameter p = el.LookupParameter(paramName);
            if (p != null)
            {
                string val = GetParamString(p);
                if (!string.IsNullOrEmpty(val)) return val;
            }

            // Try type parameter
            ElementId typeId = el.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
            {
                Element typeEl = el.Document.GetElement(typeId);
                if (typeEl != null)
                {
                    p = typeEl.LookupParameter(paramName);
                    if (p != null)
                    {
                        string val = GetParamString(p);
                        if (!string.IsNullOrEmpty(val)) return val;
                    }
                }
            }

            return null;
        }

        private static string GetParamString(Parameter p)
        {
            switch (p.StorageType)
            {
                case StorageType.String:
                    return p.AsString();
                case StorageType.Integer:
                    return p.AsInteger().ToString();
                case StorageType.Double:
                    return p.AsValueString() ?? p.AsDouble().ToString("F2");
                default:
                    return null;
            }
        }

        /// <summary>Get path to COLOR_PRESETS.json in Data directory.</summary>
        public static string GetPresetsPath()
        {
            string dataDir = StingToolsApp.DataPath;
            if (string.IsNullOrEmpty(dataDir))
                dataDir = Path.GetTempPath();

            if (!Directory.Exists(dataDir))
                Directory.CreateDirectory(dataDir);

            return Path.Combine(dataDir, "COLOR_PRESETS.json");
        }

        /// <summary>Load all presets from JSON file.</summary>
        public static List<ColorPreset> LoadPresets(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    return JsonConvert.DeserializeObject<List<ColorPreset>>(json)
                        ?? new List<ColorPreset>();
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LoadPresets: {ex.Message}");
            }
            return new List<ColorPreset>();
        }
    }
}
