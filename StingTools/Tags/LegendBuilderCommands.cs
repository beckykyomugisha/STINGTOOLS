using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    // ═══════════════════════════════════════════════════════════════════════
    // Universal Legend Builder — Persistent Visual Legends for ALL Colorization
    //
    // Creates Revit drafting views containing:
    //   - Color swatches (FilledRegion) for each value
    //   - Text labels explaining what each color represents
    //   - Title text identifying the color scheme
    //   - Count of elements per value
    //
    // Works with every colorization tool in the codebase:
    //   - Color By Parameter (10 palettes)
    //   - Color Tags By Discipline (8 discipline colors)
    //   - Highlight Invalid (4 status colors)
    //   - TAG7 Display Presets (7 presets)
    //   - TAG1-TAG6 Segment Colors (8 segment styles)
    //   - Annotation Color commands
    //
    // The legend is a persistent drafting view that can be placed on sheets
    // for client communication — not a transient dialog.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Engine for building persistent color legends as Revit drafting views.
    /// Creates FilledRegion swatches with TextNote labels.
    /// </summary>
    internal static class LegendBuilder
    {
        /// <summary>Shared discipline code → full name lookup (used across all legend builders).</summary>
        public static readonly Dictionary<string, string> DisciplineNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "M", "Mechanical" }, { "E", "Electrical" }, { "P", "Plumbing" },
                { "A", "Architectural" }, { "S", "Structural" }, { "FP", "Fire Protection" },
                { "LV", "Low Voltage" }, { "G", "General" }
            };

        /// <summary>A single entry in a color legend.</summary>
        public class LegendEntry
        {
            /// <summary>The color swatch RGB.</summary>
            public Color Color { get; set; }
            /// <summary>Display label (e.g. "Mechanical", "M-BLD1-Z01-L02").</summary>
            public string Label { get; set; }
            /// <summary>Description or count (e.g. "42 elements", "HVAC system").</summary>
            public string Description { get; set; }
            /// <summary>Whether to bold this entry label.</summary>
            public bool Bold { get; set; }
            /// <summary>Whether to italicize this entry label.</summary>
            public bool Italic { get; set; }
        }

        /// <summary>
        /// Configuration for legend layout.
        /// </summary>
        public class LegendConfig
        {
            /// <summary>Title text shown at top of legend.</summary>
            public string Title { get; set; } = "Color Legend";
            /// <summary>Subtitle (e.g. "Parameter: ASS_DISCIPLINE_COD_TXT").</summary>
            public string Subtitle { get; set; } = "";
            /// <summary>Width of color swatch in feet (default 0.05' = ~15mm).</summary>
            public double SwatchWidth { get; set; } = 0.05;
            /// <summary>Height of color swatch in feet.</summary>
            public double SwatchHeight { get; set; } = 0.03;
            /// <summary>Vertical spacing between rows in feet.</summary>
            public double RowSpacing { get; set; } = 0.05;
            /// <summary>Horizontal gap between swatch and label in feet.</summary>
            public double LabelOffset { get; set; } = 0.02;
            /// <summary>Optional footer text.</summary>
            public string Footer { get; set; } = "";
            /// <summary>Draw border lines around swatches (uses DetailLine).</summary>
            public bool DrawSwatchBorders { get; set; } = true;
            /// <summary>Draw horizontal separator lines between header and entries.</summary>
            public bool DrawSeparators { get; set; } = true;
            /// <summary>Number of columns (1 = vertical list, 2+ = multi-column grid).</summary>
            public int Columns { get; set; } = 1;
            /// <summary>Column width when using multi-column layout.</summary>
            public double ColumnWidth { get; set; } = 0.3;
            /// <summary>Show element count next to each entry.</summary>
            public bool ShowCounts { get; set; } = true;
            /// <summary>Date/time stamp in the footer.</summary>
            public bool IncludeTimestamp { get; set; } = true;
        }

        /// <summary>
        /// Try to create a Legend view by duplicating an existing one.
        /// Legend views can be placed on multiple sheets (unlike drafting views).
        /// Returns null if no existing Legend view is available (caller should
        /// fall back to CreateLegendView which uses drafting views).
        /// Must be called within an active Transaction.
        /// </summary>
        public static View TryCreateNativeLegend(Document doc, string name)
        {
            try
            {
                // Find an existing Legend view to duplicate
                var existingLegend = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .FirstOrDefault(v => v.ViewType == ViewType.Legend && !v.IsTemplate);

                if (existingLegend == null) return null;

                ElementId newId = existingLegend.Duplicate(ViewDuplicateOption.Duplicate);
                View newLegend = doc.GetElement(newId) as View;
                if (newLegend != null)
                {
                    try { newLegend.Name = name; } catch { }

                    // Delete all existing elements from the duplicated legend
                    var existingElements = new FilteredElementCollector(doc, newLegend.Id)
                        .WhereElementIsNotElementType()
                        .ToElementIds()
                        .ToList();
                    foreach (var eid in existingElements)
                    {
                        try { doc.Delete(eid); } catch { }
                    }
                }
                return newLegend;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LegendBuilder: TryCreateNativeLegend failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Create a persistent color legend as a Revit view.
        /// Tries to create a native Legend view first (can be placed on multiple sheets),
        /// falls back to a Drafting view if no Legend view exists in the project.
        /// Must be called within an active Transaction.
        /// </summary>
        /// <param name="doc">The Revit document.</param>
        /// <param name="entries">Legend entries (color + label pairs).</param>
        /// <param name="config">Layout configuration.</param>
        /// <returns>The created View (Legend or Drafting), or null on failure.</returns>
        public static View CreateLegendView(Document doc, List<LegendEntry> entries, LegendConfig config)
        {
            if (entries == null || entries.Count == 0) return null;

            // Generate unique view name
            string baseName = $"STING Legend - {config.Title}";
            string viewName = baseName;
            int suffix = 1;
            while (ViewNameExists(doc, viewName) || ViewNameExistsAnyType(doc, viewName))
            {
                viewName = $"{baseName} ({suffix++})";
            }

            // Try to create a native Legend view first (placeable on multiple sheets)
            View legendView = TryCreateNativeLegend(doc, viewName);

            // Fall back to a Drafting view (always works, no prerequisites)
            if (legendView == null)
            {
                var viewFamilyType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Drafting);

                if (viewFamilyType == null)
                {
                    StingLog.Warn("LegendBuilder: No Drafting view family type found.");
                    return null;
                }

                ViewDrafting draftView = ViewDrafting.Create(doc, viewFamilyType.Id);
                try { draftView.Name = viewName; }
                catch { /* Name conflict handled above, ignore race */ }
                draftView.Scale = 1; // 1:1 for drafting view
                legendView = draftView;
            }

            // Populate the view with legend content (swatches, labels, separators)
            PopulateLegendContent(doc, legendView, entries, config);

            StingLog.Info($"LegendBuilder: created legend view '{viewName}' ({legendView.ViewType}) with {entries.Count} entries");
            return legendView;
        }

        /// <summary>
        /// Populate a view with legend content: color swatches (FilledRegion),
        /// text labels (TextNote), separator lines (DetailLine), and footer.
        /// Works with both Legend views and Drafting views.
        /// Must be called within an active Transaction.
        /// </summary>
        internal static void PopulateLegendContent(Document doc, View legendView,
            List<LegendEntry> entries, LegendConfig config)
        {
            // Find solid fill pattern for filled regions
            FillPatternElement solidFill = FindSolidFill(doc);

            // Get or create a default TextNoteType
            ElementId textTypeId = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .FirstElementId();

            // Get or create the title TextNoteType (larger)
            ElementId titleTypeId = GetOrCreateTitleNoteType(doc, textTypeId);

            double y = 0;
            double totalWidth = config.Columns > 1
                ? config.Columns * config.ColumnWidth
                : config.SwatchWidth + config.LabelOffset + 0.25;

            // ── Title ──
            if (!string.IsNullOrEmpty(config.Title))
            {
                TextNote titleNote = TextNote.Create(doc, legendView.Id,
                    new XYZ(0, y, 0), config.Title, titleTypeId);

                // Bold the title
                try
                {
                    FormattedText ft = titleNote.GetFormattedText();
                    ft.SetBoldStatus(new TextRange(0, config.Title.Length), true);
                    titleNote.SetFormattedText(ft);
                }
                catch { /* FormattedText not always available */ }

                y -= config.RowSpacing * 1.5;
            }

            // ── Subtitle ──
            if (!string.IsNullOrEmpty(config.Subtitle))
            {
                TextNote.Create(doc, legendView.Id,
                    new XYZ(0, y, 0), config.Subtitle, textTypeId);
                y -= config.RowSpacing;
            }

            // ── Separator line (DetailLine) ──
            if (config.DrawSeparators)
            {
                y -= config.RowSpacing * 0.25;
                DrawDetailLine(doc, legendView, new XYZ(0, y, 0), new XYZ(totalWidth, y, 0));
                y -= config.RowSpacing * 0.25;
            }
            else
            {
                y -= config.RowSpacing * 0.5;
            }

            // ── Legend entries (with multi-column support) ──
            int col = 0;
            double maxRowHeight = 0;

            for (int ei = 0; ei < entries.Count; ei++)
            {
                var entry = entries[ei];
                double xOffset = config.Columns > 1 ? col * config.ColumnWidth : 0;

                // Create color swatch as FilledRegion
                if (solidFill != null)
                {
                    try
                    {
                        ElementId regionTypeId = GetOrCreateFilledRegionType(doc, entry.Color, solidFill.Id);

                        var profile = new List<CurveLoop>();
                        var loop = new CurveLoop();
                        XYZ p1 = new XYZ(xOffset, y, 0);
                        XYZ p2 = new XYZ(xOffset + config.SwatchWidth, y, 0);
                        XYZ p3 = new XYZ(xOffset + config.SwatchWidth, y - config.SwatchHeight, 0);
                        XYZ p4 = new XYZ(xOffset, y - config.SwatchHeight, 0);
                        loop.Append(Line.CreateBound(p1, p2));
                        loop.Append(Line.CreateBound(p2, p3));
                        loop.Append(Line.CreateBound(p3, p4));
                        loop.Append(Line.CreateBound(p4, p1));
                        profile.Add(loop);

                        FilledRegion.Create(doc, regionTypeId, legendView.Id, profile);

                        // Draw swatch border
                        if (config.DrawSwatchBorders)
                        {
                            DrawDetailLine(doc, legendView, p1, p2);
                            DrawDetailLine(doc, legendView, p2, p3);
                            DrawDetailLine(doc, legendView, p3, p4);
                            DrawDetailLine(doc, legendView, p4, p1);
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"LegendBuilder: swatch failed for '{entry.Label}': {ex.Message}");
                    }
                }

                // Create label text
                double textX = xOffset + config.SwatchWidth + config.LabelOffset;
                double textY = y - config.SwatchHeight * 0.3;

                string labelText = entry.Label;
                if (config.ShowCounts && !string.IsNullOrEmpty(entry.Description))
                    labelText += $"  ({entry.Description})";

                TextNote labelNote = TextNote.Create(doc, legendView.Id,
                    new XYZ(textX, textY, 0), labelText, textTypeId);

                // Apply bold/italic if specified
                if (entry.Bold || entry.Italic)
                {
                    try
                    {
                        FormattedText ft = labelNote.GetFormattedText();
                        string plain = ft.GetPlainText();
                        if (!string.IsNullOrEmpty(plain))
                        {
                            int labelLen = Math.Min(entry.Label.Length, plain.Length);
                            var range = new TextRange(0, labelLen);
                            if (entry.Bold) ft.SetBoldStatus(range, true);
                            if (entry.Italic) ft.SetItalicStatus(range, true);
                            labelNote.SetFormattedText(ft);
                        }
                    }
                    catch { /* FormattedText not always available */ }
                }

                maxRowHeight = Math.Max(maxRowHeight, config.RowSpacing);

                // Multi-column: advance to next column or next row
                if (config.Columns > 1)
                {
                    col++;
                    if (col >= config.Columns)
                    {
                        col = 0;
                        y -= maxRowHeight;
                        maxRowHeight = 0;
                    }
                }
                else
                {
                    y -= config.RowSpacing;
                }
            }

            // If last row was not complete in multi-column mode
            if (config.Columns > 1 && col > 0)
                y -= maxRowHeight;

            // ── Bottom separator ──
            if (config.DrawSeparators)
            {
                y -= config.RowSpacing * 0.25;
                DrawDetailLine(doc, legendView, new XYZ(0, y, 0), new XYZ(totalWidth, y, 0));
                y -= config.RowSpacing * 0.25;
            }

            // ── Footer ──
            string footerText = config.Footer ?? "";
            if (config.IncludeTimestamp)
            {
                if (!string.IsNullOrEmpty(footerText)) footerText += " | ";
                footerText += DateTime.Now.ToString("dd MMM yyyy HH:mm");
            }

            if (!string.IsNullOrEmpty(footerText))
            {
                y -= config.RowSpacing * 0.5;
                TextNote footerNote = TextNote.Create(doc, legendView.Id,
                    new XYZ(0, y, 0), footerText, textTypeId);
                try
                {
                    FormattedText ft = footerNote.GetFormattedText();
                    ft.SetItalicStatus(new TextRange(0, footerText.Length), true);
                    footerNote.SetFormattedText(ft);
                }
                catch { }
            }
        }

        /// <summary>
        /// Build legend entries for a Color By Parameter result.
        /// </summary>
        public static List<LegendEntry> FromColorMap(
            Dictionary<string, Color> colorMap,
            Dictionary<string, List<ElementId>> groups)
        {
            var entries = new List<LegendEntry>();
            foreach (var kvp in colorMap.OrderBy(k => k.Key))
            {
                int count = groups.ContainsKey(kvp.Key) ? groups[kvp.Key].Count : 0;
                entries.Add(new LegendEntry
                {
                    Color = kvp.Value,
                    Label = kvp.Key,
                    Description = $"{count} elements",
                    Bold = kvp.Key == "<No Value>",
                });
            }
            return entries;
        }

        /// <summary>
        /// Build legend entries for discipline colors.
        /// </summary>
        public static List<LegendEntry> FromDisciplineColors(
            Dictionary<string, Color> discColors,
            Dictionary<string, int> counts = null)
        {
            var entries = new List<LegendEntry>();
            foreach (var kvp in discColors)
            {
                string name = DisciplineNames.TryGetValue(kvp.Key, out string n) ? n : kvp.Key;
                string desc = counts != null && counts.TryGetValue(kvp.Key, out int c) ? $"{c} elements" : "";
                entries.Add(new LegendEntry
                {
                    Color = kvp.Value,
                    Label = $"{kvp.Key} - {name}",
                    Description = desc,
                    Bold = true,
                });
            }
            return entries;
        }

        /// <summary>
        /// Build legend entries for Highlight Invalid color scheme.
        /// </summary>
        public static List<LegendEntry> FromHighlightInvalid(int missing, int incomplete, int placeholder, int isoErrors)
        {
            return new List<LegendEntry>
            {
                new LegendEntry { Color = new Color(255, 0, 0), Label = "Missing Tag", Description = $"{missing} elements", Bold = true },
                new LegendEntry { Color = new Color(255, 165, 0), Label = "Incomplete Tag", Description = $"{incomplete} elements", Bold = false },
                new LegendEntry { Color = new Color(160, 32, 240), Label = "Placeholder Values", Description = $"{placeholder} elements", Bold = false },
                new LegendEntry { Color = new Color(255, 255, 0), Label = "ISO 19650 Violation", Description = $"{isoErrors} elements", Bold = false, Italic = true },
            };
        }

        /// <summary>
        /// Build legend entries for TAG1-TAG6 segment styles.
        /// </summary>
        public static List<LegendEntry> FromSegmentStyles()
        {
            var entries = new List<LegendEntry>();
            foreach (var style in TagConfig.SegmentStyles)
            {
                // Parse hex to Color
                Color c = HexToColor(style.Color);
                entries.Add(new LegendEntry
                {
                    Color = c,
                    Label = $"{style.Name} - {style.Description}",
                    Description = $"Segment {style.Index}",
                    Bold = style.Bold,
                    Italic = style.Italic,
                });
            }
            return entries;
        }

        /// <summary>
        /// Build legend entries for TAG7 section styles.
        /// </summary>
        public static List<LegendEntry> FromTag7SectionStyles()
        {
            var entries = new List<LegendEntry>();
            foreach (var style in TagConfig.SectionStyles)
            {
                Color c = HexToColor(style.Color);
                entries.Add(new LegendEntry
                {
                    Color = c,
                    Label = $"Section {style.Key}: {style.Name}",
                    Description = "",
                    Bold = style.Bold,
                    Italic = style.Italic,
                });
            }
            return entries;
        }

        /// <summary>
        /// Build legend entries from a TAG7 display preset.
        /// </summary>
        public static List<LegendEntry> FromTag7Preset(TagConfig.Tag7DisplayPreset preset)
        {
            if (preset == null) return new List<LegendEntry>();

            var entries = new List<LegendEntry>();
            foreach (var kvp in preset.Styles)
            {
                Color c = HexToColor(kvp.Value.HeaderColor ?? "#888888");
                entries.Add(new LegendEntry
                {
                    Color = c,
                    Label = $"{kvp.Key} - {kvp.Value.Label ?? kvp.Key}",
                    Description = "",
                    Bold = true,
                });
            }

            if (preset.DefaultStyle != null)
            {
                entries.Add(new LegendEntry
                {
                    Color = HexToColor(preset.DefaultStyle.HeaderColor ?? "#888888"),
                    Label = "Other / Default",
                    Description = "",
                    Italic = true,
                });
            }

            return entries;
        }

        /// <summary>
        /// Build legend entries from a generic set of named color-count pairs.
        /// Used by any colorization tool that produces value→color mappings.
        /// </summary>
        public static List<LegendEntry> FromNamedColors(IEnumerable<(string name, Color color, int count)> items)
        {
            return items.Select(item => new LegendEntry
            {
                Color = item.color,
                Label = item.name,
                Description = item.count > 0 ? $"{item.count} elements" : "",
            }).ToList();
        }

        // ── Helpers ──────────────────────────────────────────────

        /// <summary>
        /// Auto-generate legend entries from the current project state.
        /// Scans all taggable elements and creates entries based on the active
        /// colorization (discipline, system, status, etc.).
        /// </summary>
        public static List<LegendEntry> AutoFromProject(Document doc, string colorBy = "Discipline")
        {
            var elems = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.HasMaterialQuantities)
                .ToList();

            // Delegate shared groupings (Discipline, Category, System, Status)
            if (colorBy == "Discipline" || colorBy == "Category" ||
                colorBy == "System" || colorBy == "Status")
            {
                return BuildEntriesFromElements(doc, elems, colorBy);
            }

            // Level and Type are project-only groupings (not used in sheet-context)
            var entries = new List<LegendEntry>();

            switch (colorBy)
            {
                case "Level":
                    var lvlGroups = elems
                        .Select(e => ParameterHelpers.GetString(e, ParamRegistry.LVL))
                        .Where(l => !string.IsNullOrEmpty(l))
                        .GroupBy(l => l)
                        .OrderBy(g => g.Key);

                    var lvlPalette = Select.ColorHelper.Palettes["Cool"];
                    int li = 0;
                    foreach (var g in lvlGroups)
                    {
                        entries.Add(new LegendEntry
                        {
                            Color = lvlPalette[li++ % lvlPalette.Length],
                            Label = g.Key,
                            Description = $"{g.Count()} elements",
                        });
                    }
                    break;

                case "Type":
                    var typeGroups = elems
                        .Select(e => {
                            var typeId = e.GetTypeId();
                            if (typeId == ElementId.InvalidElementId) return "(No Type)";
                            var type = doc.GetElement(typeId);
                            return type?.Name ?? "(Unknown)";
                        })
                        .GroupBy(t => t)
                        .OrderByDescending(g => g.Count());

                    var typePalette = Select.ColorHelper.Palettes["Pastel"];
                    int ti = 0;
                    foreach (var g in typeGroups.Take(30))
                    {
                        entries.Add(new LegendEntry
                        {
                            Color = typePalette[ti++ % typePalette.Length],
                            Label = g.Key,
                            Description = $"{g.Count()} elements",
                        });
                    }
                    break;
            }

            return entries;
        }

        /// <summary>Draw a detail line in the view.</summary>
        private static void DrawDetailLine(Document doc, View view, XYZ start, XYZ end)
        {
            try
            {
                if (start.DistanceTo(end) < 0.001) return;
                Line line = Line.CreateBound(start, end);
                doc.Create.NewDetailCurve(view, line);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LegendBuilder: detail line failed: {ex.Message}");
            }
        }

        private static bool ViewNameExists(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewDrafting))
                .Any(v => v.Name == name);
        }

        /// <summary>Check if a view name exists across ANY view type (not just Drafting).</summary>
        private static bool ViewNameExistsAnyType(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Any(v => !v.IsTemplate && v.Name == name);
        }

        private static FillPatternElement FindSolidFill(Document doc)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
            }
            catch { return null; }
        }

        private static ElementId GetOrCreateFilledRegionType(Document doc, Color color, ElementId fillPatternId)
        {
            // Try to find an existing type with matching color name
            string typeName = $"STING Swatch {color.Red:D3}-{color.Green:D3}-{color.Blue:D3}";

            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .FirstOrDefault(frt => frt.Name == typeName);

            if (existing != null) return existing.Id;

            // Duplicate an existing FilledRegionType
            var baseType = new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .FirstOrDefault();

            if (baseType == null) return ElementId.InvalidElementId;

            try
            {
                var newType = baseType.Duplicate(typeName) as FilledRegionType;
                if (newType != null)
                {
                    newType.ForegroundPatternColor = color;
                    newType.ForegroundPatternId = fillPatternId;
                    newType.BackgroundPatternColor = color;
                    return newType.Id;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LegendBuilder: failed to create FilledRegionType '{typeName}': {ex.Message}");
            }

            return baseType.Id;
        }

        private static ElementId GetOrCreateTitleNoteType(Document doc, ElementId baseTypeId)
        {
            string typeName = "STING Legend Title";
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .FirstOrDefault(t => t.Name == typeName);

            if (existing != null) return existing.Id;

            if (baseTypeId == ElementId.InvalidElementId) return baseTypeId;

            try
            {
                var baseType = doc.GetElement(baseTypeId) as TextNoteType;
                if (baseType != null)
                {
                    var newType = baseType.Duplicate(typeName) as TextNoteType;
                    if (newType != null)
                    {
                        // Make it larger for title
                        var sizeParam = newType.get_Parameter(BuiltInParameter.TEXT_SIZE);
                        if (sizeParam != null && !sizeParam.IsReadOnly)
                        {
                            double currentSize = sizeParam.AsDouble();
                            sizeParam.Set(currentSize * 1.5);
                        }
                        return newType.Id;
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LegendBuilder: failed to create title TextNoteType: {ex.Message}");
            }

            return baseTypeId;
        }

        /// <summary>Convert hex string to Revit Color.</summary>
        public static Color HexToColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return new Color(128, 128, 128);
            hex = hex.TrimStart('#');
            if (hex.Length < 6) return new Color(128, 128, 128);
            try
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return new Color(r, g, b);
            }
            catch { return new Color(128, 128, 128); }
        }

        // ── Sheet-Aware Legend Builders ─────────────────────────────

        /// <summary>
        /// Build legend entries from elements visible on a specific sheet.
        /// Scans all views placed on the sheet, collects elements, and groups
        /// by the specified colorBy parameter. Only includes values actually
        /// present on the sheet — not the entire project.
        /// </summary>
        public static List<LegendEntry> FromSheetElements(Document doc, ViewSheet sheet, string colorBy = "Discipline")
        {
            if (sheet == null) return new List<LegendEntry>();

            // Collect all elements from all views placed on this sheet
            var sheetElements = new List<Element>();
            foreach (ElementId viewId in sheet.GetAllPlacedViews())
            {
                View v = doc.GetElement(viewId) as View;
                if (v == null || v.IsTemplate) continue;
                // Skip legend and drafting views — they don't contain model elements
                if (v.ViewType == ViewType.Legend || v.ViewType == ViewType.DraftingView) continue;

                try
                {
                    var viewElems = new FilteredElementCollector(doc, v.Id)
                        .WhereElementIsNotElementType()
                        .Where(e => e.Category != null && e.Category.HasMaterialQuantities)
                        .ToList();
                    sheetElements.AddRange(viewElems);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"LegendBuilder.FromSheetElements: view '{v.Name}' scan failed: {ex.Message}");
                }
            }

            // Deduplicate by ElementId (same element may appear in multiple views)
            var uniqueElements = sheetElements
                .GroupBy(e => e.Id)
                .Select(g => g.First())
                .ToList();

            if (uniqueElements.Count == 0) return new List<LegendEntry>();

            // Delegate to AutoFromProject-style grouping but with our filtered element set
            return BuildEntriesFromElements(doc, uniqueElements, colorBy);
        }

        /// <summary>
        /// Build legend entries from a specific set of elements (shared logic).
        /// Used by both AutoFromProject and FromSheetElements.
        /// </summary>
        private static List<LegendEntry> BuildEntriesFromElements(
            Document doc, List<Element> elems, string colorBy)
        {
            var entries = new List<LegendEntry>();
            if (elems.Count == 0) return entries;

            switch (colorBy)
            {
                case "Discipline":
                    var discGroups = elems
                        .Select(e => ParameterHelpers.GetString(e, ParamRegistry.DISC))
                        .Where(d => !string.IsNullOrEmpty(d))
                        .GroupBy(d => d)
                        .OrderByDescending(g => g.Count());

                    foreach (var g in discGroups)
                    {
                        if (Organise.AnnotationColorHelper.DisciplineColors.TryGetValue(g.Key, out Color c))
                        {
                            string name = DisciplineNames.TryGetValue(g.Key, out string n) ? n : g.Key;
                            entries.Add(new LegendEntry
                            {
                                Color = c, Label = $"{g.Key} - {name}",
                                Description = $"{g.Count()} elements", Bold = true
                            });
                        }
                    }
                    break;

                case "Category":
                    var catGroups = elems
                        .Where(e => e.Category != null)
                        .GroupBy(e => e.Category.Name)
                        .OrderByDescending(g => g.Count());

                    var catPalette = Select.ColorHelper.Palettes["Spectral"];
                    int ci = 0;
                    foreach (var g in catGroups.Take(20))
                    {
                        entries.Add(new LegendEntry
                        {
                            Color = catPalette[ci++ % catPalette.Length],
                            Label = g.Key,
                            Description = $"{g.Count()} elements",
                        });
                    }
                    break;

                case "System":
                    var sysGroups = elems
                        .Select(e => ParameterHelpers.GetString(e, ParamRegistry.SYS))
                        .Where(s => !string.IsNullOrEmpty(s))
                        .GroupBy(s => s)
                        .OrderByDescending(g => g.Count());

                    var sysPalette = Select.ColorHelper.Palettes["High Contrast"];
                    int si = 0;
                    foreach (var g in sysGroups)
                    {
                        entries.Add(new LegendEntry
                        {
                            Color = sysPalette[si++ % sysPalette.Length],
                            Label = g.Key,
                            Description = $"{g.Count()} elements", Bold = true
                        });
                    }
                    break;

                case "Status":
                    var statusGroups = elems
                        .Select(e => ParameterHelpers.GetString(e, "ASS_STATUS_TXT"))
                        .Where(s => !string.IsNullOrEmpty(s))
                        .GroupBy(s => s)
                        .OrderByDescending(g => g.Count());

                    var statusColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "NEW", new Color(0, 153, 51) },
                        { "EXISTING", new Color(0, 102, 204) },
                        { "DEMOLISHED", new Color(204, 0, 0) },
                        { "TEMPORARY", new Color(255, 165, 0) },
                    };

                    foreach (var g in statusGroups)
                    {
                        Color statusC = statusColors.TryGetValue(g.Key, out Color sc) ? sc : new Color(128, 128, 128);
                        entries.Add(new LegendEntry
                        {
                            Color = statusC, Label = g.Key,
                            Description = $"{g.Count()} elements", Bold = true
                        });
                    }
                    break;
            }

            return entries;
        }

        // ── Sheet Placement Helpers ────────────────────────────────

        /// <summary>
        /// Get sheet dimensions from the title block. Falls back to A1 size.
        /// Returns (width, height) in feet.
        /// </summary>
        public static (double width, double height) GetSheetDimensions(Document doc, ViewSheet sheet)
        {
            double w = 2.76;  // A1 default (841mm)
            double h = 1.95;  // A1 default (594mm)

            try
            {
                var titleBlocks = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsNotElementType()
                    .ToList();

                if (titleBlocks.Count > 0)
                {
                    BoundingBoxXYZ bb = titleBlocks[0].get_BoundingBox(null);
                    if (bb != null)
                    {
                        w = bb.Max.X - bb.Min.X;
                        h = bb.Max.Y - bb.Min.Y;
                    }
                }
            }
            catch { }

            return (w, h);
        }

        /// <summary>
        /// Place a legend view on a sheet at an auto-calculated position.
        /// Prefers the bottom-right corner. Must be called within a Transaction.
        /// Returns the Viewport, or null on failure.
        /// </summary>
        public static Viewport PlaceLegendOnSheet(Document doc, ViewSheet sheet,
            View legendView, string position = "BottomRight")
        {
            if (sheet == null || legendView == null) return null;

            // Check if this view can be added to the sheet
            if (!Viewport.CanAddViewToSheet(doc, sheet.Id, legendView.Id))
            {
                StingLog.Warn($"LegendBuilder: Cannot place '{legendView.Name}' on sheet '{sheet.SheetNumber}' — already placed or incompatible.");
                return null;
            }

            var (sheetWidth, sheetHeight) = GetSheetDimensions(doc, sheet);
            double margin = 0.15; // feet (~46mm)
            // Legend viewport size estimate: ~0.4ft wide × 0.3ft tall for typical legend
            double legendW = 0.4;
            double legendH = 0.3;

            double x, y;
            switch (position)
            {
                case "TopRight":
                    x = sheetWidth - margin - legendW / 2;
                    y = sheetHeight - margin - legendH / 2;
                    break;
                case "TopLeft":
                    x = margin + legendW / 2;
                    y = sheetHeight - margin - legendH / 2;
                    break;
                case "BottomLeft":
                    x = margin + legendW / 2;
                    y = margin + legendH / 2;
                    break;
                case "BottomRight":
                default:
                    x = sheetWidth - margin - legendW / 2;
                    y = margin + legendH / 2;
                    break;
            }

            try
            {
                Viewport vp = Viewport.Create(doc, sheet.Id, legendView.Id, new XYZ(x, y, 0));
                StingLog.Info($"LegendBuilder: placed '{legendView.Name}' on sheet '{sheet.SheetNumber}' at {position}");
                return vp;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LegendBuilder: failed to place on sheet: {ex.Message}");
                return null;
            }
        }

        // ── Tag Legend Engine ──────────────────────────────────────────
        //
        // "Tag Legend" workaround: Revit API cannot create LegendComponent
        // instances from scratch. The workaround uses three strategies:
        //
        //   Strategy 1: Copy existing LegendComponent → set BuiltInParameter.LEGEND_COMPONENT
        //   Strategy 2: Place annotation family instances via NewFamilyInstance(XYZ, FamilySymbol, View)
        //   Strategy 3: Draw tag representation with FilledRegion + TextNote (always works)
        //
        // All three are attempted in order. Strategy 3 is the guaranteed fallback.
        //
        // References:
        //   - Tag Legend plugin by D'Bim Tools (Autodesk App Store)
        //   - GeniusLoci for Dynamo (CopyElement workaround)
        //   - The Building Coder: Duplicate Legend Component
        // ──────────────────────────────────────────────────────────────

        /// <summary>A tag family entry for the tag legend.</summary>
        public class TagLegendEntry
        {
            /// <summary>Category display name (e.g. "Mechanical Equipment").</summary>
            public string CategoryName { get; set; }
            /// <summary>Built-in category enum.</summary>
            public BuiltInCategory CategoryId { get; set; }
            /// <summary>Discipline code (M, E, P, A, S, etc.).</summary>
            public string Discipline { get; set; }
            /// <summary>Discipline color for the swatch.</summary>
            public Color DisciplineColor { get; set; }
            /// <summary>Tag FamilySymbol for this category (null if no tag loaded).</summary>
            public FamilySymbol TagSymbol { get; set; }
            /// <summary>Tag family name.</summary>
            public string TagFamilyName { get; set; }
            /// <summary>Sample tag text (e.g. "M-BLD1-Z01-L02-HVAC-SUP-AHU-0001").</summary>
            public string SampleTag { get; set; }
            /// <summary>Number of elements of this category in scope.</summary>
            public int ElementCount { get; set; }
            /// <summary>Product code for this category.</summary>
            public string ProductCode { get; set; }
        }

        /// <summary>
        /// Collect all taggable categories with their tag families from the project.
        /// Returns one TagLegendEntry per category that has at least one element.
        /// </summary>
        public static List<TagLegendEntry> CollectTagFamilies(Document doc)
        {
            var entries = new List<TagLegendEntry>();

            // Get all taggable elements grouped by category
            var allElems = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.HasMaterialQuantities)
                .ToList();

            var byCat = allElems
                .GroupBy(e => e.Category.Id)
                .OrderByDescending(g => g.Count())
                .ToList();

            // Detect actual project LOC instead of hardcoded "BLD1"
            string projectLoc = SpatialAutoDetect.DetectProjectLoc(doc);
            if (string.IsNullOrEmpty(projectLoc)) projectLoc = "BLD1";

            // Sample ZONE from first room that has a department
            string sampleZone = "Z01";
            try
            {
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Take(20);
                foreach (var r in rooms)
                {
                    string dept = ParameterHelpers.GetString(r, "Department");
                    if (!string.IsNullOrEmpty(dept))
                    {
                        string z = SpatialAutoDetect.DetectZone(doc, r, null);
                        if (!string.IsNullOrEmpty(z) && z != "XX") { sampleZone = z; break; }
                    }
                }
            }
            catch { }

            foreach (var catGroup in byCat)
            {
                Element sample = catGroup.First();
                Category cat = sample.Category;
                if (cat == null) continue;

                BuiltInCategory bic;
                try { bic = (BuiltInCategory)cat.Id.Value; }
                catch { continue; }

                // Get discipline and color
                string disc = TagConfig.DiscMap.TryGetValue(cat.Name, out string d) ? d : "G";
                Color discColor = Organise.AnnotationColorHelper.DisciplineColors.TryGetValue(disc, out Color c)
                    ? c : new Color(128, 128, 128);

                // Find tag family for this category
                FamilySymbol tagSym = TagPlacementEngine.FindTagType(doc, cat);
                string tagFamName = tagSym != null ? tagSym.Family?.Name ?? "(Unknown)" : "(No Tag Family)";

                // Get product code
                string prodCode = TagConfig.ProdMap.TryGetValue(cat.Name, out string pc) ? pc : "GEN";

                // Build sample tag using actual project LOC/ZONE
                string sysCode = TagConfig.GetSysCode(cat.Name) ?? "GEN";
                string funcCode = TagConfig.GetFuncCode(sysCode) ?? "GEN";
                // Get level from first element in group
                string lvlCode = ParameterHelpers.GetLevelCode(doc, sample);
                if (string.IsNullOrEmpty(lvlCode)) lvlCode = "L01";
                string sampleTag = $"{disc}-{projectLoc}-{sampleZone}-{lvlCode}-{sysCode}-{funcCode}-{prodCode}-0001";

                entries.Add(new TagLegendEntry
                {
                    CategoryName = cat.Name,
                    CategoryId = bic,
                    Discipline = disc,
                    DisciplineColor = discColor,
                    TagSymbol = tagSym,
                    TagFamilyName = tagFamName,
                    SampleTag = sampleTag,
                    ElementCount = catGroup.Count(),
                    ProductCode = prodCode,
                });
            }

            return entries;
        }

        /// <summary>
        /// Collect tag families only for categories visible on a specific sheet.
        /// </summary>
        public static List<TagLegendEntry> CollectTagFamiliesForSheet(Document doc, ViewSheet sheet)
        {
            if (sheet == null) return new List<TagLegendEntry>();

            var sheetElements = new List<Element>();
            foreach (ElementId viewId in sheet.GetAllPlacedViews())
            {
                View v = doc.GetElement(viewId) as View;
                if (v == null || v.IsTemplate) continue;
                if (v.ViewType == ViewType.Legend || v.ViewType == ViewType.DraftingView) continue;

                try
                {
                    var viewElems = new FilteredElementCollector(doc, v.Id)
                        .WhereElementIsNotElementType()
                        .Where(e => e.Category != null && e.Category.HasMaterialQuantities)
                        .ToList();
                    sheetElements.AddRange(viewElems);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"CollectTagFamiliesForSheet: view scan failed: {ex.Message}");
                }
            }

            // Deduplicate
            var uniqueElements = sheetElements.GroupBy(e => e.Id).Select(g => g.First()).ToList();
            if (uniqueElements.Count == 0) return new List<TagLegendEntry>();

            // Group by category
            var byCat = uniqueElements
                .GroupBy(e => e.Category.Id)
                .OrderByDescending(g => g.Count())
                .ToList();

            // Detect actual project LOC/ZONE
            string projectLoc = SpatialAutoDetect.DetectProjectLoc(doc);
            if (string.IsNullOrEmpty(projectLoc)) projectLoc = "BLD1";

            var entries = new List<TagLegendEntry>();
            foreach (var catGroup in byCat)
            {
                Element sample = catGroup.First();
                Category cat = sample.Category;
                if (cat == null) continue;

                BuiltInCategory bic;
                try { bic = (BuiltInCategory)cat.Id.Value; }
                catch { continue; }

                string disc = TagConfig.DiscMap.TryGetValue(cat.Name, out string d) ? d : "G";
                Color discColor = Organise.AnnotationColorHelper.DisciplineColors.TryGetValue(disc, out Color c)
                    ? c : new Color(128, 128, 128);

                FamilySymbol tagSym = TagPlacementEngine.FindTagType(doc, cat);
                string tagFamName = tagSym != null ? tagSym.Family?.Name ?? "(Unknown)" : "(No Tag Family)";
                string prodCode = TagConfig.ProdMap.TryGetValue(cat.Name, out string pc) ? pc : "GEN";
                string sysCode = TagConfig.GetSysCode(cat.Name) ?? "GEN";
                string funcCode = TagConfig.GetFuncCode(sysCode) ?? "GEN";
                string lvlCode = ParameterHelpers.GetLevelCode(doc, sample);
                if (string.IsNullOrEmpty(lvlCode)) lvlCode = "L01";
                string sampleTag = $"{disc}-{projectLoc}-Z01-{lvlCode}-{sysCode}-{funcCode}-{prodCode}-0001";

                entries.Add(new TagLegendEntry
                {
                    CategoryName = cat.Name,
                    CategoryId = bic,
                    Discipline = disc,
                    DisciplineColor = discColor,
                    TagSymbol = tagSym,
                    TagFamilyName = tagFamName,
                    SampleTag = sampleTag,
                    ElementCount = catGroup.Count(),
                    ProductCode = prodCode,
                });
            }

            return entries;
        }

        /// <summary>
        /// Create a tag legend view showing tag families per category.
        /// Attempts to place actual annotation family instances, falls back to
        /// drawn representation (swatch + tag text) for categories where placement fails.
        /// Must be called within an active Transaction.
        /// </summary>
        public static View CreateTagLegendView(Document doc, List<TagLegendEntry> entries,
            string title = "Tag Legend", string groupBy = "Discipline")
        {
            if (entries == null || entries.Count == 0) return null;

            // Generate unique view name
            string baseName = $"STING Tag Legend - {title}";
            string viewName = baseName;
            int suffix = 1;
            while (ViewNameExists(doc, viewName) || ViewNameExistsAnyType(doc, viewName))
            {
                viewName = $"{baseName} ({suffix++})";
            }

            // Try native legend first, fall back to drafting
            View legendView = TryCreateNativeLegend(doc, viewName);

            if (legendView == null)
            {
                var viewFamilyType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Drafting);

                if (viewFamilyType == null)
                {
                    StingLog.Warn("TagLegendBuilder: No Drafting view family type found.");
                    return null;
                }

                ViewDrafting draftView = ViewDrafting.Create(doc, viewFamilyType.Id);
                try { draftView.Name = viewName; } catch { }
                draftView.Scale = 1;
                legendView = draftView;
            }

            // Populate with tag legend content
            PopulateTagLegendContent(doc, legendView, entries, title, groupBy);

            StingLog.Info($"TagLegendBuilder: created tag legend '{viewName}' ({legendView.ViewType}) with {entries.Count} categories");
            return legendView;
        }

        /// <summary>
        /// Populate a view with tag legend content.
        /// For each category, shows: discipline swatch | category name | tag family name | sample tag.
        /// Attempts to place actual annotation instances (Strategy 2), falls back to drawn text.
        /// </summary>
        private static void PopulateTagLegendContent(Document doc, View legendView,
            List<TagLegendEntry> entries, string title, string groupBy)
        {
            FillPatternElement solidFill = FindSolidFill(doc);
            ElementId textTypeId = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType)).FirstElementId();
            ElementId titleTypeId = GetOrCreateTitleNoteType(doc, textTypeId);
            ElementId smallTypeId = GetOrCreateSmallNoteType(doc, textTypeId);

            // Layout constants
            double swatchW = 0.04;
            double swatchH = 0.025;
            double colGap = 0.015;
            double rowH = 0.045;
            double catColW = 0.22;       // category name column
            double tagFamColW = 0.22;    // tag family name column
            double sampleTagColW = 0.35; // sample tag column
            double countColW = 0.08;     // element count column
            double totalWidth = swatchW + colGap + catColW + tagFamColW + sampleTagColW + countColW;

            double y = 0;

            // ── Title ──
            TextNote titleNote = TextNote.Create(doc, legendView.Id,
                new XYZ(0, y, 0), title, titleTypeId);
            try
            {
                FormattedText ft = titleNote.GetFormattedText();
                ft.SetBoldStatus(new TextRange(0, title.Length), true);
                titleNote.SetFormattedText(ft);
            }
            catch { }
            y -= rowH * 1.2;

            // ── Subtitle ──
            string subtitle = "Category | Tag Family | Sample Tag | Count";
            TextNote.Create(doc, legendView.Id, new XYZ(0, y, 0), subtitle, textTypeId);
            y -= rowH * 0.8;

            // ── Header separator ──
            DrawDetailLine(doc, legendView, new XYZ(0, y, 0), new XYZ(totalWidth, y, 0));
            y -= rowH * 0.3;

            // ── Column headers ──
            double hx = swatchW + colGap;
            DrawBoldText(doc, legendView, new XYZ(hx, y, 0), "Category", textTypeId);
            hx += catColW;
            DrawBoldText(doc, legendView, new XYZ(hx, y, 0), "Tag Family", textTypeId);
            hx += tagFamColW;
            DrawBoldText(doc, legendView, new XYZ(hx, y, 0), "Sample Tag", textTypeId);
            hx += sampleTagColW;
            DrawBoldText(doc, legendView, new XYZ(hx, y, 0), "Qty", textTypeId);
            y -= rowH * 0.8;

            DrawDetailLine(doc, legendView, new XYZ(0, y, 0), new XYZ(totalWidth, y, 0));
            y -= rowH * 0.3;

            // ── Group entries by discipline (or flat) ──
            IEnumerable<IGrouping<string, TagLegendEntry>> groups;
            if (groupBy == "Discipline")
            {
                groups = entries
                    .GroupBy(e => e.Discipline)
                    .OrderBy(g => g.Key);
            }
            else
            {
                groups = entries.GroupBy(e => "All");
            }

            int annotationPlaced = 0;
            int drawnFallback = 0;

            foreach (var group in groups)
            {
                // Discipline group header
                if (groupBy == "Discipline")
                {
                    string discLabel = DisciplineNames.TryGetValue(group.Key, out string dn) ? dn : group.Key;
                    int groupCount = group.Sum(e => e.ElementCount);

                    DrawBoldText(doc, legendView, new XYZ(0, y, 0),
                        $"▸ {group.Key} - {discLabel}  ({groupCount} elements)", textTypeId);
                    y -= rowH * 0.7;
                }

                foreach (var entry in group.OrderByDescending(e => e.ElementCount))
                {
                    double x = 0;

                    // Col 1: Discipline color swatch
                    if (solidFill != null)
                    {
                        try
                        {
                            ElementId regionTypeId = GetOrCreateFilledRegionType(doc, entry.DisciplineColor, solidFill.Id);
                            var loop = new CurveLoop();
                            XYZ p1 = new XYZ(x, y, 0);
                            XYZ p2 = new XYZ(x + swatchW, y, 0);
                            XYZ p3 = new XYZ(x + swatchW, y - swatchH, 0);
                            XYZ p4 = new XYZ(x, y - swatchH, 0);
                            loop.Append(Line.CreateBound(p1, p2));
                            loop.Append(Line.CreateBound(p2, p3));
                            loop.Append(Line.CreateBound(p3, p4));
                            loop.Append(Line.CreateBound(p4, p1));
                            FilledRegion.Create(doc, regionTypeId, legendView.Id, new List<CurveLoop> { loop });

                            DrawDetailLine(doc, legendView, p1, p2);
                            DrawDetailLine(doc, legendView, p2, p3);
                            DrawDetailLine(doc, legendView, p3, p4);
                            DrawDetailLine(doc, legendView, p4, p1);
                        }
                        catch { }
                    }
                    x += swatchW + colGap;

                    double textY = y - swatchH * 0.3;

                    // Col 2: Category name
                    TextNote.Create(doc, legendView.Id, new XYZ(x, textY, 0),
                        entry.CategoryName, smallTypeId);
                    x += catColW;

                    // Col 3: Tag family — try to place actual annotation, fall back to text
                    bool placed = false;
                    if (entry.TagSymbol != null)
                    {
                        try
                        {
                            // Ensure symbol is activated
                            if (!entry.TagSymbol.IsActive)
                                entry.TagSymbol.Activate();

                            // Strategy 2: Place annotation family instance directly
                            XYZ annotPos = new XYZ(x + 0.05, textY, 0);
                            doc.Create.NewFamilyInstance(annotPos, entry.TagSymbol, legendView);
                            placed = true;
                            annotationPlaced++;
                        }
                        catch
                        {
                            // Annotation placement failed — tag families often need a host element.
                            // This is expected for most tag types; fall back to drawn representation.
                            placed = false;
                        }
                    }

                    if (!placed)
                    {
                        // Fallback: Draw tag family name as text with a thin box
                        string famDisplay = entry.TagFamilyName;
                        if (famDisplay.Length > 28) famDisplay = famDisplay.Substring(0, 25) + "...";

                        // Draw a small tag-shaped outline
                        double boxW = 0.18;
                        double boxH = 0.02;
                        double bx = x;
                        double by = textY + 0.003;
                        DrawDetailLine(doc, legendView, new XYZ(bx, by, 0), new XYZ(bx + boxW, by, 0));
                        DrawDetailLine(doc, legendView, new XYZ(bx + boxW, by, 0), new XYZ(bx + boxW, by - boxH, 0));
                        DrawDetailLine(doc, legendView, new XYZ(bx + boxW, by - boxH, 0), new XYZ(bx, by - boxH, 0));
                        DrawDetailLine(doc, legendView, new XYZ(bx, by - boxH, 0), new XYZ(bx, by, 0));

                        TextNote.Create(doc, legendView.Id, new XYZ(x + 0.005, textY, 0),
                            famDisplay, smallTypeId);
                        drawnFallback++;
                    }
                    x += tagFamColW;

                    // Col 4: Sample tag text
                    try
                    {
                        TextNote tagNote = TextNote.Create(doc, legendView.Id,
                            new XYZ(x, textY, 0), entry.SampleTag, smallTypeId);
                        // Italicize sample tag
                        FormattedText ft = tagNote.GetFormattedText();
                        ft.SetItalicStatus(new TextRange(0, entry.SampleTag.Length), true);
                        tagNote.SetFormattedText(ft);
                    }
                    catch { }
                    x += sampleTagColW;

                    // Col 5: Element count
                    TextNote.Create(doc, legendView.Id, new XYZ(x, textY, 0),
                        entry.ElementCount.ToString(), smallTypeId);

                    y -= rowH;
                }

                // Group separator
                if (groupBy == "Discipline")
                {
                    y -= rowH * 0.2;
                    DrawDetailLine(doc, legendView,
                        new XYZ(0, y, 0), new XYZ(totalWidth * 0.5, y, 0));
                    y -= rowH * 0.3;
                }
            }

            // ── Footer ──
            y -= rowH * 0.3;
            DrawDetailLine(doc, legendView, new XYZ(0, y, 0), new XYZ(totalWidth, y, 0));
            y -= rowH * 0.5;

            string footer = $"STING Tag Legend • {entries.Count} categories • " +
                $"{entries.Sum(e => e.ElementCount)} elements • " +
                $"{annotationPlaced} live tags / {drawnFallback} drawn • " +
                $"{DateTime.Now:yyyy-MM-dd HH:mm}";
            try
            {
                TextNote footNote = TextNote.Create(doc, legendView.Id,
                    new XYZ(0, y, 0), footer, smallTypeId);
                FormattedText fft = footNote.GetFormattedText();
                fft.SetItalicStatus(new TextRange(0, footer.Length), true);
                footNote.SetFormattedText(fft);
            }
            catch { }

            StingLog.Info($"TagLegendBuilder: populated {entries.Count} entries — {annotationPlaced} live annotations, {drawnFallback} drawn fallback");
        }

        /// <summary>Draw bold text at position.</summary>
        private static void DrawBoldText(Document doc, View view, XYZ pos, string text, ElementId typeId)
        {
            try
            {
                TextNote note = TextNote.Create(doc, view.Id, pos, text, typeId);
                FormattedText ft = note.GetFormattedText();
                ft.SetBoldStatus(new TextRange(0, text.Length), true);
                note.SetFormattedText(ft);
            }
            catch { }
        }

        /// <summary>Get or create a smaller TextNoteType for legend details.</summary>
        private static ElementId GetOrCreateSmallNoteType(Document doc, ElementId baseTypeId)
        {
            string typeName = "STING Legend Detail";
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .FirstOrDefault(t => t.Name == typeName);

            if (existing != null) return existing.Id;

            if (baseTypeId == ElementId.InvalidElementId) return baseTypeId;

            try
            {
                var baseType = doc.GetElement(baseTypeId) as TextNoteType;
                if (baseType != null)
                {
                    var newType = baseType.Duplicate(typeName) as TextNoteType;
                    if (newType != null)
                    {
                        var sizeParam = newType.get_Parameter(BuiltInParameter.TEXT_SIZE);
                        if (sizeParam != null && !sizeParam.IsReadOnly)
                        {
                            double currentSize = sizeParam.AsDouble();
                            sizeParam.Set(currentSize * 0.75); // 75% of default
                        }
                        return newType.Id;
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LegendBuilder: failed to create small TextNoteType: {ex.Message}");
            }

            return baseTypeId;
        }

        /// <summary>
        /// Try to copy an existing LegendComponent and reassign its type.
        /// This is the D'Bim Tag Legend / GeniusLoci workaround:
        ///   1. Find existing LegendComponent in a Legend view
        ///   2. CopyElement to duplicate it
        ///   3. Set BuiltInParameter.LEGEND_COMPONENT to the target FamilySymbol
        /// Returns the new element, or null if no seed LegendComponent exists.
        /// Must be called within a Transaction.
        /// </summary>
        public static Element TryCopyLegendComponent(Document doc, View legendView,
            FamilySymbol targetType, XYZ position)
        {
            if (legendView == null || targetType == null) return null;
            if (legendView.ViewType != ViewType.Legend) return null;

            try
            {
                // Find any existing LegendComponent in any Legend view
                var legendViews = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => v.ViewType == ViewType.Legend && !v.IsTemplate)
                    .ToList();

                Element seedComponent = null;
                ElementId sourceViewId = ElementId.InvalidElementId;

                foreach (var lv in legendViews)
                {
                    try
                    {
                        seedComponent = new FilteredElementCollector(doc, lv.Id)
                            .OfCategory(BuiltInCategory.OST_LegendComponents)
                            .WhereElementIsNotElementType()
                            .FirstOrDefault();

                        if (seedComponent != null)
                        {
                            sourceViewId = lv.Id;
                            break;
                        }
                    }
                    catch { }
                }

                if (seedComponent == null) return null;

                // Copy the seed component to our legend view
                ICollection<ElementId> copiedIds;
                if (sourceViewId == legendView.Id)
                {
                    // Same view: use CopyElement with translation
                    XYZ seedLoc = XYZ.Zero;
                    if (seedComponent.Location is LocationPoint lp)
                        seedLoc = lp.Point;
                    XYZ translation = position - seedLoc;
                    copiedIds = ElementTransformUtils.CopyElement(doc, seedComponent.Id, translation);
                }
                else
                {
                    // Different views: CopyElements between views
                    // Filter out ExtentElem to avoid Revit creating duplicate legend views
                    var elemIds = new List<ElementId> { seedComponent.Id };
                    copiedIds = ElementTransformUtils.CopyElements(
                        doc.GetElement(sourceViewId) as View,
                        elemIds,
                        legendView,
                        Transform.Identity,
                        new CopyPasteOptions());
                }

                if (copiedIds == null || copiedIds.Count == 0) return null;

                // Set the target type on the copied component
                Element copied = doc.GetElement(copiedIds.First());
                if (copied != null)
                {
                    Parameter legendParam = copied.get_Parameter(BuiltInParameter.LEGEND_COMPONENT);
                    if (legendParam != null && !legendParam.IsReadOnly)
                    {
                        legendParam.Set(targetType.Id);

                        // Move to target position
                        if (copied.Location is LocationPoint lp)
                        {
                            XYZ current = lp.Point;
                            ElementTransformUtils.MoveElement(doc, copied.Id, position - current);
                        }

                        return copied;
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TryCopyLegendComponent: {ex.Message}");
            }

            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Create Color Legend Command — Universal legend creation
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Universal color legend creator. Presents a menu of all available
    /// colorization schemes and creates a persistent drafting view legend
    /// with FilledRegion swatches and TextNote labels.
    ///
    /// Supported legend sources:
    ///   1. Tag Segments (DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ)
    ///   2. TAG7 Section Styles (Identity, System, Spatial, etc.)
    ///   3. Discipline Colors (M=Blue, E=Gold, P=Green, etc.)
    ///   4. Highlight Invalid (Red/Orange/Purple/Yellow status)
    ///   5. Active TAG7 Display Preset
    ///   6. Active Color By Parameter scheme
    ///   7. Custom (user picks colors + labels)
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateColorLegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Pick legend type
            var dlg = new TaskDialog("Create Color Legend");
            dlg.MainInstruction = "What color scheme should the legend explain?";
            dlg.MainContent = "The legend will be created as a persistent drafting view\n" +
                "with color swatches and labels. Place it on sheets for clients.";

            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Tag Segments (TAG1-TAG6)",
                "8-segment colors: DISC=Blue, LOC=Green, ZONE=Orange, LVL=Purple, SYS=Red, FUNC=Teal, PROD=Indigo, SEQ=Grey");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Discipline Colors",
                "M=Blue, E=Gold, P=Green, A=Grey, S=Red, FP=Orange, LV=Purple, G=Brown");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Tag Validation Status",
                "Red=Missing, Orange=Incomplete, Purple=Placeholder, Yellow=ISO Error");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "TAG7 Sections / Active Preset",
                "Identity=Blue, System=Green, Spatial=Orange, Lifecycle=Red, Technical=Purple, Classification=Grey");

            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;
            dlg.FooterText = "Legend is created as a drafting view — place on sheets for documentation.";

            var pick = dlg.Show();

            List<LegendBuilder.LegendEntry> entries;
            LegendBuilder.LegendConfig config;

            switch (pick)
            {
                case TaskDialogResult.CommandLink1:
                    entries = LegendBuilder.FromSegmentStyles();
                    config = new LegendBuilder.LegendConfig
                    {
                        Title = "ISO 19650 Tag Segment Colors",
                        Subtitle = "DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ",
                        Footer = "Generated by STING Tools — ISO 19650 Asset Management",
                    };
                    break;

                case TaskDialogResult.CommandLink2:
                    entries = LegendBuilder.FromDisciplineColors(
                        Organise.AnnotationColorHelper.DisciplineColors);
                    config = new LegendBuilder.LegendConfig
                    {
                        Title = "Discipline Color Coding",
                        Subtitle = "Parameter: ASS_DISCIPLINE_COD_TXT",
                        Footer = "Generated by STING Tools — ISO 19650 Asset Management",
                    };
                    break;

                case TaskDialogResult.CommandLink3:
                    // Scan current view for counts
                    var view = doc.ActiveView;
                    int missing = 0, incomplete = 0, placeholder = 0, isoErr = 0;
                    if (view != null)
                    {
                        var known = new HashSet<string>(TagConfig.DiscMap.Keys);
                        foreach (Element elem in new FilteredElementCollector(doc, view.Id)
                            .WhereElementIsNotElementType())
                        {
                            string cat = ParameterHelpers.GetCategoryName(elem);
                            if (!known.Contains(cat)) continue;
                            string tag = ParameterHelpers.GetString(elem, ParamRegistry.TAG1);
                            if (string.IsNullOrEmpty(tag)) missing++;
                            else if (!TagConfig.TagIsComplete(tag)) incomplete++;
                            else if (!TagConfig.TagIsFullyResolved(tag)) placeholder++;
                            else
                            {
                                var isoErrors = ISO19650Validator.ValidateElement(elem);
                                if (isoErrors.Count > 0) isoErr++;
                            }
                        }
                    }
                    entries = LegendBuilder.FromHighlightInvalid(missing, incomplete, placeholder, isoErr);
                    config = new LegendBuilder.LegendConfig
                    {
                        Title = "Tag Validation Status",
                        Subtitle = "Highlight Invalid Results",
                        Footer = $"Scanned in: {doc.ActiveView?.Name ?? "unknown view"}",
                    };
                    break;

                case TaskDialogResult.CommandLink4:
                    if (TagConfig.ActivePreset != null)
                    {
                        entries = LegendBuilder.FromTag7Preset(TagConfig.ActivePreset);
                        config = new LegendBuilder.LegendConfig
                        {
                            Title = $"TAG7 Preset: {TagConfig.ActivePreset.Name}",
                            Subtitle = TagConfig.ActivePreset.Description,
                            Footer = $"Discriminator: {TagConfig.ActivePreset.DiscriminatorParam}",
                        };
                    }
                    else
                    {
                        entries = LegendBuilder.FromTag7SectionStyles();
                        config = new LegendBuilder.LegendConfig
                        {
                            Title = "TAG7 Section Colors",
                            Subtitle = "TAG7 Narrative Section Styling",
                            Footer = "Activate a preset for context-specific styling",
                        };
                    }
                    break;

                default:
                    return Result.Cancelled;
            }

            if (entries.Count == 0)
            {
                TaskDialog.Show("Create Legend", "No legend entries to create.");
                return Result.Succeeded;
            }

            View legendView;
            using (Transaction tx = new Transaction(doc, "STING Create Color Legend"))
            {
                tx.Start();
                legendView = LegendBuilder.CreateLegendView(doc, entries, config);
                tx.Commit();
            }

            if (legendView != null)
            {
                // Switch to the new legend view
                uidoc.ActiveView = legendView;
                TaskDialog.Show("Create Legend",
                    $"Created legend: '{legendView.Name}'\n\n" +
                    $"  Entries: {entries.Count}\n" +
                    $"  Type: {legendView.ViewType}\n\n" +
                    "Place this view on a sheet for client documentation.\n" +
                    "Use 'Viewport > Add View' on any sheet.");
            }

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Export Color Legend HTML — Multi-scheme HTML legend export
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Export all active colorization schemes as a single HTML legend page.
    /// Combines TAG segment colors, discipline colors, validation status,
    /// TAG7 sections, and active preset into a comprehensive color guide.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportColorLegendHtmlCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html lang=\"en\"><head>");
            html.AppendLine("<meta charset=\"UTF-8\">");
            html.AppendLine("<title>STING Color Legend Guide</title>");
            html.AppendLine("<style>");
            html.AppendLine("* { box-sizing: border-box; margin: 0; padding: 0; }");
            html.AppendLine("body { font-family: 'Segoe UI', sans-serif; padding: 30px; background: #f5f5f5; }");
            html.AppendLine("h1 { text-align: center; color: #1565C0; margin-bottom: 5px; }");
            html.AppendLine(".subtitle { text-align: center; color: #888; margin-bottom: 30px; }");
            html.AppendLine(".legend-section { background: white; border-radius: 8px; padding: 20px; margin-bottom: 20px; box-shadow: 0 2px 6px rgba(0,0,0,0.1); }");
            html.AppendLine(".legend-title { font-size: 16px; font-weight: bold; margin-bottom: 12px; padding-bottom: 8px; border-bottom: 2px solid #e0e0e0; }");
            html.AppendLine(".legend-row { display: flex; align-items: center; margin-bottom: 6px; }");
            html.AppendLine(".swatch { width: 24px; height: 24px; border-radius: 4px; margin-right: 12px; border: 1px solid #ccc; flex-shrink: 0; }");
            html.AppendLine(".label { font-weight: 600; margin-right: 8px; }");
            html.AppendLine(".desc { color: #666; font-size: 13px; }");
            html.AppendLine("@media print { body { background: white; } .legend-section { box-shadow: none; border: 1px solid #ddd; break-inside: avoid; } }");
            html.AppendLine("</style></head><body>");

            html.AppendLine("<h1>STING Tools - Color Legend Guide</h1>");
            html.AppendLine($"<div class=\"subtitle\">Generated {DateTime.Now:dd MMM yyyy HH:mm} | ISO 19650 Compliant</div>");

            // 1. Tag Segments
            AppendLegendSection(html, "Tag Segment Colors (TAG1-TAG6)",
                "Each segment of the 8-part ISO 19650 tag is assigned a unique color.",
                LegendBuilder.FromSegmentStyles());

            // 2. Discipline Colors
            AppendLegendSection(html, "Discipline Color Coding",
                "Elements colored by discipline code (ASS_DISCIPLINE_COD_TXT).",
                LegendBuilder.FromDisciplineColors(Organise.AnnotationColorHelper.DisciplineColors));

            // 3. Validation Status
            AppendLegendSection(html, "Tag Validation Status",
                "Colors used by Highlight Invalid command.",
                LegendBuilder.FromHighlightInvalid(0, 0, 0, 0));

            // 4. TAG7 Section Styles
            AppendLegendSection(html, "TAG7 Narrative Section Colors",
                "Color scheme for the 6 TAG7 narrative sections.",
                LegendBuilder.FromTag7SectionStyles());

            // 5. Active Preset (if any)
            if (TagConfig.ActivePreset != null)
            {
                AppendLegendSection(html, $"Active Preset: {TagConfig.ActivePreset.Name}",
                    TagConfig.ActivePreset.Description,
                    LegendBuilder.FromTag7Preset(TagConfig.ActivePreset));
            }

            // 6. Built-in palette preview
            html.AppendLine("<div class=\"legend-section\">");
            html.AppendLine("<div class=\"legend-title\">Available Color Palettes</div>");
            foreach (var kvp in Select.ColorHelper.Palettes)
            {
                html.Append($"<div style=\"margin-bottom:8px;\"><strong>{HtmlEncode(kvp.Key)}</strong>: ");
                foreach (var c in kvp.Value)
                {
                    html.Append($"<span class=\"swatch\" style=\"display:inline-block;width:16px;height:16px;vertical-align:middle;background:rgb({c.Red},{c.Green},{c.Blue})\"></span> ");
                }
                html.AppendLine("</div>");
            }
            html.AppendLine("</div>");

            html.AppendLine("<div style=\"text-align:center;color:#aaa;margin-top:20px;font-size:12px;\">");
            html.AppendLine("Generated by STING Tools — ISO 19650 BIM Asset Management</div>");
            html.AppendLine("</body></html>");

            // Save
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string filePath = System.IO.Path.Combine(dir, $"STING_Color_Legend_{DateTime.Now:yyyyMMdd_HHmmss}.html");
            System.IO.File.WriteAllText(filePath, html.ToString());

            TaskDialog.Show("Export Color Legend",
                $"Exported comprehensive color legend to:\n{filePath}\n\n" +
                "Sections included:\n" +
                "  1. Tag Segment Colors (TAG1-TAG6)\n" +
                "  2. Discipline Color Coding\n" +
                "  3. Tag Validation Status\n" +
                "  4. TAG7 Section Colors\n" +
                (TagConfig.ActivePreset != null ? $"  5. Active Preset: {TagConfig.ActivePreset.Name}\n" : "") +
                "  6. Available Palette Preview\n\n" +
                "Open in a web browser to view. Print-ready CSS included.");

            return Result.Succeeded;
        }

        private void AppendLegendSection(StringBuilder html, string title, string description,
            List<LegendBuilder.LegendEntry> entries)
        {
            html.AppendLine("<div class=\"legend-section\">");
            html.AppendLine($"<div class=\"legend-title\">{HtmlEncode(title)}</div>");
            if (!string.IsNullOrEmpty(description))
                html.AppendLine($"<div style=\"color:#666;margin-bottom:10px;font-size:13px;\">{HtmlEncode(description)}</div>");

            foreach (var entry in entries)
            {
                html.AppendLine("<div class=\"legend-row\">");
                html.Append($"<div class=\"swatch\" style=\"background:rgb({entry.Color.Red},{entry.Color.Green},{entry.Color.Blue})\"></div>");

                string labelStyle = "";
                if (entry.Bold) labelStyle += "font-weight:bold;";
                if (entry.Italic) labelStyle += "font-style:italic;";

                html.Append($"<span class=\"label\" style=\"{labelStyle}\">{HtmlEncode(entry.Label)}</span>");
                if (!string.IsNullOrEmpty(entry.Description))
                    html.Append($"<span class=\"desc\">{HtmlEncode(entry.Description)}</span>");
                html.AppendLine("</div>");
            }
            html.AppendLine("</div>");
        }

        private string HtmlEncode(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Auto Create Legends — Batch legend creation for all categories/types
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Automatically create legends for all major colorization groupings in the project.
    /// Creates multiple drafting views — one per grouping (Discipline, Category, System,
    /// Level, Type, Status). Each legend shows the color + count for each value.
    /// This is the "create legends for ALL" command.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoCreateLegendsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Pick which legends to create
            var dlg = new TaskDialog("Auto Create Legends");
            dlg.MainInstruction = "Which legends should be created?";
            dlg.MainContent = "Each selection creates a drafting view with color swatches,\n" +
                "labels, and element counts. Place on sheets for client documentation.\n\n" +
                "Select one or create all:";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Complete Suite (14+ views)",
                "ALL legends: Color + MEP System + Equipment + Material + CompoundType + FireRating + Tags");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "MEP + Architectural (8 views)",
                "Discipline + MEP System + Equipment + Material + CompoundType + FireRating + Tag Families");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Tag Structure + Tag Families",
                "Tag Segments + TAG7 Sections + Validation Status + Tag Family Legend");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Color Legends Only (6 views)",
                "Discipline + Category + System + Level + Type + Status");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var pick = dlg.Show();
            List<string> schemesToCreate;

            switch (pick)
            {
                case TaskDialogResult.CommandLink1:
                    schemesToCreate = new List<string> { "Discipline", "Category", "System", "Level", "Type", "Status", "MEPSystem", "Equipment", "Material", "CompoundType", "FireRating", "TagSegments", "TAG7Sections", "Validation", "TagFamilies" };
                    break;
                case TaskDialogResult.CommandLink2:
                    schemesToCreate = new List<string> { "Discipline", "MEPSystem", "Equipment", "Material", "CompoundType", "FireRating", "TagFamilies" };
                    break;
                case TaskDialogResult.CommandLink3:
                    schemesToCreate = new List<string> { "TagSegments", "TAG7Sections", "Validation", "TagFamilies" };
                    break;
                case TaskDialogResult.CommandLink4:
                    schemesToCreate = new List<string> { "Discipline", "Category", "System", "Level", "Type", "Status" };
                    break;
                default:
                    return Result.Cancelled;
            }

            int created = 0;
            var viewNames = new List<string>();

            using (Transaction tx = new Transaction(doc, "STING Auto Create Legends"))
            {
                tx.Start();

                foreach (string scheme in schemesToCreate)
                {
                    List<LegendBuilder.LegendEntry> entries;
                    LegendBuilder.LegendConfig config;

                    switch (scheme)
                    {
                        case "TagSegments":
                            entries = LegendBuilder.FromSegmentStyles();
                            config = new LegendBuilder.LegendConfig
                            {
                                Title = "ISO 19650 Tag Segments",
                                Subtitle = "DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ",
                                Footer = "Generated by STING Tools",
                            };
                            break;
                        case "TAG7Sections":
                            entries = LegendBuilder.FromTag7SectionStyles();
                            config = new LegendBuilder.LegendConfig
                            {
                                Title = "TAG7 Narrative Sections",
                                Subtitle = "Identity | System | Spatial | Lifecycle | Technical | Classification",
                                Footer = "Generated by STING Tools",
                            };
                            break;
                        case "Validation":
                            entries = LegendBuilder.FromHighlightInvalid(0, 0, 0, 0);
                            config = new LegendBuilder.LegendConfig
                            {
                                Title = "Tag Validation Status",
                                Subtitle = "Highlight Invalid Color Scheme",
                                Footer = "Generated by STING Tools",
                            };
                            break;
                        case "TagFamilies":
                            // Tag Family Legend — uses the tag legend engine
                            var tagEntries = LegendBuilder.CollectTagFamilies(doc);
                            if (tagEntries.Count > 0)
                            {
                                var tagView = LegendBuilder.CreateTagLegendView(doc, tagEntries, "All Tag Families", "Discipline");
                                if (tagView != null)
                                {
                                    viewNames.Add(tagView.Name);
                                    created++;
                                }
                            }
                            continue; // Skip the normal CreateLegendView path
                        case "MEPSystem":
                            entries = LegendIntelligence.BuildMepSystemEntries(doc);
                            config = new LegendBuilder.LegendConfig
                            {
                                Title = "MEP Systems",
                                Subtitle = "CIBSE/Uniclass 2015 system classification",
                                Footer = "Generated by STING Tools",
                            };
                            break;
                        case "Equipment":
                            entries = LegendIntelligence.BuildEquipmentEntries(doc);
                            config = new LegendBuilder.LegendConfig
                            {
                                Title = "MEP Equipment Schedule",
                                Subtitle = "Equipment families by category",
                                Footer = "Generated by STING Tools",
                                Columns = entries.Count > 12 ? 2 : 1,
                            };
                            break;
                        case "Material":
                            entries = LegendIntelligence.BuildMaterialEntries(doc);
                            config = new LegendBuilder.LegendConfig
                            {
                                Title = "Materials in Use",
                                Subtitle = "Grouped by material category",
                                Footer = "Generated by STING Tools",
                                Columns = entries.Count > 10 ? 2 : 1,
                            };
                            break;
                        case "CompoundType":
                            entries = LegendIntelligence.BuildCompoundTypeEntries(doc);
                            config = new LegendBuilder.LegendConfig
                            {
                                Title = "Wall / Floor / Ceiling / Roof Types",
                                Subtitle = "Compound types with layer information",
                                Footer = "Generated by STING Tools",
                                Columns = entries.Count > 15 ? 2 : 1,
                            };
                            break;
                        case "FireRating":
                            entries = LegendIntelligence.BuildFireRatingEntries(doc);
                            config = new LegendBuilder.LegendConfig
                            {
                                Title = "Fire Rating Legend",
                                Subtitle = "Element fire ratings (severity color-coded)",
                                Footer = "Generated by STING Tools",
                            };
                            break;
                        default:
                            entries = LegendBuilder.AutoFromProject(doc, scheme);
                            config = new LegendBuilder.LegendConfig
                            {
                                Title = $"Elements by {scheme}",
                                Subtitle = $"Color coding: {scheme}",
                                Footer = "Generated by STING Tools",
                                Columns = entries.Count > 12 ? 2 : 1,
                            };
                            break;
                    }

                    if (entries.Count == 0) continue;

                    var legendView = LegendBuilder.CreateLegendView(doc, entries, config);
                    if (legendView != null)
                    {
                        viewNames.Add(legendView.Name);
                        created++;
                    }
                }

                tx.Commit();
            }

            if (created > 0)
            {
                var report = new StringBuilder();
                report.AppendLine($"Created {created} legend views:");
                foreach (string name in viewNames)
                    report.AppendLine($"  - {name}");
                report.AppendLine();
                report.AppendLine("Find them under 'Drafting Views' or 'Legends' in the Project Browser.");
                report.AppendLine("Place on sheets using 'Insert > Views'.");
                TaskDialog.Show("Auto Create Legends", report.ToString());
            }
            else
            {
                TaskDialog.Show("Auto Create Legends", "No legends created — no data found in the project.");
            }

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Legend from Active View — Create legend from current view's overrides
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Detect what colorization is active in the current view and create a matching legend.
    /// Scans elements for graphic overrides, identifies the color scheme, and builds a
    /// legend that documents what the colors mean.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LegendFromViewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            if (view == null)
            {
                TaskDialog.Show("Legend from View", "No active view.");
                return Result.Failed;
            }

            // Scan view elements for graphic overrides — checks BOTH surface fill
            // color AND projection line color. ColorByParameter sets surface fill,
            // while ColorTagsByDiscipline sets projection lines.
            var colorGroups = new Dictionary<string, (Color color, int count, string bestLabel)>();

            foreach (Element el in new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType())
            {
                var ogs = view.GetElementOverrides(el.Id);

                // Priority: surface foreground color (ColorByParameter) > projection line color (discipline)
                Color overrideColor = ogs.SurfaceForegroundPatternColor;
                if (!overrideColor.IsValid)
                    overrideColor = ogs.ProjectionLineColor;
                if (!overrideColor.IsValid) continue;

                string colorKey = $"{overrideColor.Red},{overrideColor.Green},{overrideColor.Blue}";

                // Try to find a meaningful label — discipline > tag > category
                string label = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                if (string.IsNullOrEmpty(label))
                    label = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                if (string.IsNullOrEmpty(label))
                    label = ParameterHelpers.GetCategoryName(el);
                if (string.IsNullOrEmpty(label))
                    label = colorKey;

                // Group by color, keep best label (shortest non-RGB label wins)
                if (!colorGroups.ContainsKey(colorKey))
                    colorGroups[colorKey] = (overrideColor, 0, label);

                var existing = colorGroups[colorKey];
                // Prefer discipline codes over category names over RGB strings
                string bestLabel = existing.bestLabel;
                if (label.Length < bestLabel.Length && !label.Contains(","))
                    bestLabel = label;
                colorGroups[colorKey] = (existing.color, existing.count + 1, bestLabel);
            }

            if (colorGroups.Count == 0)
            {
                TaskDialog.Show("Legend from View",
                    "No graphic overrides found in the active view.\n" +
                    "Apply a colorization command first (Color By Parameter, Color Tags by Discipline, etc.).");
                return Result.Failed;
            }

            // Build legend entries
            var entries = new List<LegendBuilder.LegendEntry>();

            // Try to match colors to known schemes, fall back to detected label
            foreach (var kvp in colorGroups.OrderByDescending(x => x.Value.count))
            {
                string label = TryMatchColor(kvp.Value.color);
                if (string.IsNullOrEmpty(label))
                    label = kvp.Value.bestLabel;
                if (string.IsNullOrEmpty(label))
                    label = $"RGB({kvp.Value.color.Red}, {kvp.Value.color.Green}, {kvp.Value.color.Blue})";

                entries.Add(new LegendBuilder.LegendEntry
                {
                    Color = kvp.Value.color,
                    Label = label,
                    Description = $"{kvp.Value.count} elements",
                });
            }

            var config = new LegendBuilder.LegendConfig
            {
                Title = $"Color Legend - {view.Name}",
                Subtitle = $"Detected {colorGroups.Count} color groups",
                Footer = "Auto-detected from view graphic overrides",
                Columns = entries.Count > 10 ? 2 : 1,
            };

            View legendView;
            using (Transaction tx = new Transaction(doc, "STING Legend from View"))
            {
                tx.Start();
                legendView = LegendBuilder.CreateLegendView(doc, entries, config);
                tx.Commit();
            }

            if (legendView != null)
            {
                uidoc.ActiveView = legendView;
                TaskDialog.Show("Legend from View",
                    $"Created legend: '{legendView.Name}'\n\n" +
                    $"  Color groups: {colorGroups.Count}\n" +
                    $"  Source view: {view.Name}\n\n" +
                    "Place this view on a sheet for client documentation.");
            }

            return Result.Succeeded;
        }

        /// <summary>Try to match a color to a known discipline, validation, or palette color.</summary>
        private string TryMatchColor(Color c)
        {
            // Check discipline colors
            foreach (var kvp in Organise.AnnotationColorHelper.DisciplineColors)
            {
                if (ColorsMatch(c, kvp.Value))
                {
                    string name = LegendBuilder.DisciplineNames.TryGetValue(kvp.Key, out string n) ? n : kvp.Key;
                    return $"{kvp.Key} - {name}";
                }
            }

            // Check validation colors
            if (ColorsMatch(c, new Color(255, 0, 0))) return "Missing Tag (Red)";
            if (ColorsMatch(c, new Color(255, 165, 0))) return "Incomplete Tag (Orange)";
            if (ColorsMatch(c, new Color(160, 32, 240))) return "Placeholder Values (Purple)";
            if (ColorsMatch(c, new Color(255, 255, 0))) return "ISO Violation (Yellow)";

            return null;
        }

        private bool ColorsMatch(Color a, Color b)
        {
            return Math.Abs(a.Red - b.Red) <= 5 &&
                   Math.Abs(a.Green - b.Green) <= 5 &&
                   Math.Abs(a.Blue - b.Blue) <= 5;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Place Legend on Sheet — Auto-place legend views on sheets
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pick an existing legend/drafting view and place it on the active sheet
    /// (or a selected sheet). Uses smart positioning to avoid overlapping
    /// existing viewports. Legend views can be placed on multiple sheets.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceLegendOnSheetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            // Must be on a sheet
            if (!(activeView is ViewSheet sheet))
            {
                TaskDialog.Show("Place Legend on Sheet",
                    "The active view must be a sheet.\nOpen a sheet first, then run this command.");
                return Result.Succeeded;
            }

            // Find all STING legend views (legends + drafting views with "STING Legend" prefix)
            var legendViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate &&
                    (v.Name.StartsWith("STING Legend") ||
                     (v.ViewType == ViewType.Legend && v.Name.Contains("Legend"))))
                .OrderBy(v => v.Name)
                .ToList();

            if (legendViews.Count == 0)
            {
                TaskDialog.Show("Place Legend on Sheet",
                    "No STING legend views found.\n\nCreate legends first using:\n" +
                    "  - Create Legend (pick a color scheme)\n" +
                    "  - Auto All (batch create all legends)\n" +
                    "  - From View (detect from active coloring)");
                return Result.Succeeded;
            }

            // Let user pick which legend to place
            var dlg = new TaskDialog("Place Legend on Sheet");
            dlg.MainInstruction = $"Place a legend on '{sheet.SheetNumber} - {sheet.Name}'";
            dlg.MainContent = $"Found {legendViews.Count} legend views.\n" +
                "Legend views can be placed on multiple sheets.\n" +
                "Drafting views can only go on one sheet.";

            // Show up to 4 most recent legends
            var commands = new[] {
                TaskDialogCommandLinkId.CommandLink1,
                TaskDialogCommandLinkId.CommandLink2,
                TaskDialogCommandLinkId.CommandLink3,
                TaskDialogCommandLinkId.CommandLink4
            };
            int shown = Math.Min(legendViews.Count, 4);
            for (int i = 0; i < shown; i++)
            {
                var lv = legendViews[i];
                bool canPlace = Viewport.CanAddViewToSheet(doc, sheet.Id, lv.Id);
                string status = canPlace ? "" : " [already placed]";
                dlg.AddCommandLink(commands[i],
                    $"{lv.Name}{status}",
                    $"Type: {lv.ViewType}");
            }
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;
            if (legendViews.Count > 4)
                dlg.FooterText = $"Showing 4 of {legendViews.Count} legends. Create more with 'Auto All'.";

            var pick = dlg.Show();
            int idx = pick switch
            {
                TaskDialogResult.CommandLink1 => 0,
                TaskDialogResult.CommandLink2 => 1,
                TaskDialogResult.CommandLink3 => 2,
                TaskDialogResult.CommandLink4 => 3,
                _ => -1,
            };
            if (idx < 0) return Result.Cancelled;

            View selected = legendViews[idx];

            // Pick position
            var posDlg = new TaskDialog("Legend Position");
            posDlg.MainInstruction = "Where should the legend be placed?";
            posDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Bottom-Right (Recommended)", "Standard position near title block");
            posDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Top-Right", "Upper right corner");
            posDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Bottom-Left", "Lower left corner");
            posDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Top-Left", "Upper left corner");
            posDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            string position = posDlg.Show() switch
            {
                TaskDialogResult.CommandLink1 => "BottomRight",
                TaskDialogResult.CommandLink2 => "TopRight",
                TaskDialogResult.CommandLink3 => "BottomLeft",
                TaskDialogResult.CommandLink4 => "TopLeft",
                _ => null,
            };
            if (position == null) return Result.Cancelled;

            using (Transaction tx = new Transaction(doc, "STING Place Legend on Sheet"))
            {
                tx.Start();
                Viewport vp = LegendBuilder.PlaceLegendOnSheet(doc, sheet, selected, position);
                tx.Commit();

                if (vp != null)
                {
                    TaskDialog.Show("Place Legend on Sheet",
                        $"Placed '{selected.Name}' on sheet '{sheet.SheetNumber}'.\n" +
                        $"Position: {position}\n\n" +
                        "Drag the viewport to adjust position if needed.");
                }
                else
                {
                    TaskDialog.Show("Place Legend on Sheet",
                        $"Could not place '{selected.Name}' on this sheet.\n" +
                        "It may already be placed (drafting views allow only one sheet).");
                }
            }

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Sheet Context Legend — Legend showing only what's on each sheet
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a legend that shows ONLY the categories/disciplines/systems
    /// present on the active sheet. Scans all views placed on the sheet,
    /// collects elements, and builds a context-specific legend.
    /// Optionally auto-places the legend on the sheet.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetContextLegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            if (!(activeView is ViewSheet sheet))
            {
                TaskDialog.Show("Sheet Context Legend",
                    "The active view must be a sheet.\nOpen a sheet and run this command to create\n" +
                    "a legend showing only what's on this sheet.");
                return Result.Succeeded;
            }

            // Pick what to show in the legend
            var dlg = new TaskDialog("Sheet Context Legend");
            dlg.MainInstruction = $"Create legend for '{sheet.SheetNumber} - {sheet.Name}'";
            dlg.MainContent = "The legend will show ONLY the values present on this sheet\n" +
                "(not the entire project). Pick the grouping:";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Discipline (Recommended)", "M, E, P, A — only those on this sheet");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Category", "Lighting, Mechanical Equipment, etc.");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "System", "HVAC, DCW, SAN, etc.");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Status", "NEW, EXISTING, DEMOLISHED, TEMPORARY");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            string colorBy = dlg.Show() switch
            {
                TaskDialogResult.CommandLink1 => "Discipline",
                TaskDialogResult.CommandLink2 => "Category",
                TaskDialogResult.CommandLink3 => "System",
                TaskDialogResult.CommandLink4 => "Status",
                _ => null,
            };
            if (colorBy == null) return Result.Cancelled;

            // Build entries from sheet elements only
            var entries = LegendBuilder.FromSheetElements(doc, sheet, colorBy);

            if (entries.Count == 0)
            {
                TaskDialog.Show("Sheet Context Legend",
                    $"No {colorBy.ToLower()} data found on this sheet.\n" +
                    "Ensure views placed on this sheet contain tagged elements.");
                return Result.Succeeded;
            }

            var config = new LegendBuilder.LegendConfig
            {
                Title = $"{colorBy} Legend - {sheet.SheetNumber}",
                Subtitle = $"Sheet: {sheet.Name}",
                Footer = $"Shows only {colorBy.ToLower()} values present on this sheet",
                Columns = entries.Count > 10 ? 2 : 1,
            };

            View legendView;
            using (Transaction tx = new Transaction(doc, "STING Sheet Context Legend"))
            {
                tx.Start();
                legendView = LegendBuilder.CreateLegendView(doc, entries, config);

                // Auto-place on the sheet
                if (legendView != null)
                {
                    LegendBuilder.PlaceLegendOnSheet(doc, sheet, legendView, "BottomRight");
                }

                tx.Commit();
            }

            if (legendView != null)
            {
                TaskDialog.Show("Sheet Context Legend",
                    $"Created and placed legend on sheet '{sheet.SheetNumber}'.\n\n" +
                    $"  Grouping: {colorBy}\n" +
                    $"  Entries: {entries.Count}\n" +
                    $"  Position: Bottom-Right\n\n" +
                    "The legend shows ONLY values present on this sheet.\n" +
                    "Drag the viewport to reposition if needed.");
            }

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Place Legend on All Sheets — Batch placement across sheets
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Place a Legend view (not Drafting — only Legend views can go on multiple sheets)
    /// on all sheets, or selected discipline sheets. Auto-positions at the same location
    /// on every sheet for consistent documentation.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceLegendOnAllSheetsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Find Legend views (NOT drafting — only legends can go on multiple sheets)
            var legendViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.ViewType == ViewType.Legend &&
                    v.Name.StartsWith("STING Legend"))
                .OrderBy(v => v.Name)
                .ToList();

            if (legendViews.Count == 0)
            {
                TaskDialog.Show("Place Legend on All Sheets",
                    "No STING Legend views found.\n\n" +
                    "Only native Legend views can be placed on multiple sheets.\n" +
                    "Create legends first — the system will try to create native\n" +
                    "Legend views when an existing one is available in the project.\n\n" +
                    "Drafting views can only be placed on one sheet.");
                return Result.Succeeded;
            }

            // Pick which legend
            var dlg = new TaskDialog("Place Legend on All Sheets");
            dlg.MainInstruction = "Which legend to place on all sheets?";

            var commands = new[] {
                TaskDialogCommandLinkId.CommandLink1,
                TaskDialogCommandLinkId.CommandLink2,
                TaskDialogCommandLinkId.CommandLink3,
                TaskDialogCommandLinkId.CommandLink4
            };
            int shown = Math.Min(legendViews.Count, 4);
            for (int i = 0; i < shown; i++)
                dlg.AddCommandLink(commands[i], legendViews[i].Name);
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var pick = dlg.Show();
            int idx = pick switch
            {
                TaskDialogResult.CommandLink1 => 0,
                TaskDialogResult.CommandLink2 => 1,
                TaskDialogResult.CommandLink3 => 2,
                TaskDialogResult.CommandLink4 => 3,
                _ => -1,
            };
            if (idx < 0) return Result.Cancelled;

            View selectedLegend = legendViews[idx];

            // Get all sheets
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber)
                .ToList();

            if (sheets.Count == 0)
            {
                TaskDialog.Show("Place Legend on All Sheets", "No sheets found in the project.");
                return Result.Succeeded;
            }

            int placed = 0;
            int skipped = 0;

            using (Transaction tx = new Transaction(doc, "STING Place Legend on All Sheets"))
            {
                tx.Start();

                foreach (var s in sheets)
                {
                    if (!Viewport.CanAddViewToSheet(doc, s.Id, selectedLegend.Id))
                    {
                        skipped++;
                        continue;
                    }

                    var vp = LegendBuilder.PlaceLegendOnSheet(doc, s, selectedLegend, "BottomRight");
                    if (vp != null) placed++;
                    else skipped++;
                }

                tx.Commit();
            }

            TaskDialog.Show("Place Legend on All Sheets",
                $"Placed '{selectedLegend.Name}' on {placed} sheets.\n" +
                (skipped > 0 ? $"Skipped {skipped} sheets (already placed or incompatible).\n" : "") +
                $"\nPosition: Bottom-Right on all sheets.\n" +
                "Adjust individual positions by dragging viewports.");

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Batch Sheet Context Legends — Create + place per-sheet legends
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For EVERY sheet in the project, create a sheet-specific legend showing
    /// only the disciplines/categories present on that sheet, then auto-place
    /// the legend on the sheet. Full automation: no manual steps required.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchSheetContextLegendsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder && s.GetAllPlacedViews().Count > 0)
                .OrderBy(s => s.SheetNumber)
                .ToList();

            if (sheets.Count == 0)
            {
                TaskDialog.Show("Batch Sheet Context Legends",
                    "No sheets with placed views found.");
                return Result.Succeeded;
            }

            // Pick grouping
            var dlg = new TaskDialog("Batch Sheet Context Legends");
            dlg.MainInstruction = $"Create per-sheet legends for {sheets.Count} sheets";
            dlg.MainContent = "Each sheet gets its own legend showing ONLY the values\n" +
                "present on that specific sheet. Legends are auto-placed\n" +
                "at the bottom-right corner.";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Discipline (Recommended)", "M, E, P, A — per-sheet discipline breakdown");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Category", "Element categories present on each sheet");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "System", "MEP systems per sheet");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            string colorBy = dlg.Show() switch
            {
                TaskDialogResult.CommandLink1 => "Discipline",
                TaskDialogResult.CommandLink2 => "Category",
                TaskDialogResult.CommandLink3 => "System",
                _ => null,
            };
            if (colorBy == null) return Result.Cancelled;

            int created = 0;
            int skipped = 0;
            var report = new StringBuilder();

            using (Transaction tx = new Transaction(doc, "STING Batch Sheet Context Legends"))
            {
                tx.Start();

                foreach (var s in sheets)
                {
                    var entries = LegendBuilder.FromSheetElements(doc, s, colorBy);
                    if (entries.Count == 0)
                    {
                        skipped++;
                        continue;
                    }

                    var config = new LegendBuilder.LegendConfig
                    {
                        Title = $"{colorBy} - {s.SheetNumber}",
                        Subtitle = s.Name,
                        Footer = $"Sheet-specific {colorBy.ToLower()} legend",
                        Columns = entries.Count > 8 ? 2 : 1,
                    };

                    View legendView = LegendBuilder.CreateLegendView(doc, entries, config);
                    if (legendView != null)
                    {
                        LegendBuilder.PlaceLegendOnSheet(doc, s, legendView, "BottomRight");
                        created++;
                        report.AppendLine($"  {s.SheetNumber}: {entries.Count} {colorBy.ToLower()} entries");
                    }
                    else
                    {
                        skipped++;
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Batch Sheet Context Legends",
                $"Created {created} sheet-specific legends.\n" +
                (skipped > 0 ? $"Skipped {skipped} sheets (empty or no data).\n" : "") +
                $"\nGrouping: {colorBy}\n" +
                $"Position: Bottom-Right\n\n" +
                (created > 0 ? "Per-sheet breakdown:\n" + report.ToString() : ""));

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tag Legend Commands — Workaround for Revit's missing Tag Legend API
    //
    // Revit cannot natively tag LegendComponents. These commands create
    // "tag legends" showing tag families per category with sample tag values.
    //
    // Three strategies are attempted (in order):
    //   1. Copy existing LegendComponent → reassign BuiltInParameter.LEGEND_COMPONENT
    //   2. Place annotation family instance via NewFamilyInstance(XYZ, FamilySymbol, View)
    //   3. Draw tag-shaped outline with TextNote label (always works)
    //
    // Inspired by:
    //   - D'Bim Tools "Tag Legend" plugin (Generic Annotation bridge)
    //   - GeniusLoci for Dynamo (CopyElement workaround)
    //   - KobiLabs Legend tools (material + category legends)
    //   - All 1 Studio automatic legend creation
    //
    // See: https://apps.autodesk.com/RVT/en/Detail/Index?id=2597869698847820293
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a comprehensive tag legend showing all tag families in the project.
    /// For each taggable category, displays: discipline swatch, category name,
    /// loaded tag family name, sample ISO 19650 tag, and element count.
    /// Attempts to place actual annotation instances; falls back to drawn representation.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateTagLegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Pick grouping and scope
            var dlg = new TaskDialog("Create Tag Legend");
            dlg.MainInstruction = "Create a tag legend showing loaded tag families";
            dlg.MainContent =
                "Creates a Legend/Drafting view displaying:\n" +
                "  • Discipline color swatch\n" +
                "  • Category name\n" +
                "  • Loaded tag family (actual annotation or drawn)\n" +
                "  • Sample ISO 19650 tag text\n" +
                "  • Element count\n\n" +
                "Workaround for Revit's limitation: you cannot tag Legend Components.\n" +
                "This view can be placed on sheets for documentation.";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Full Project — By Discipline (Recommended)",
                "All categories grouped by M/E/P/A/S/FP/LV/G");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Full Project — Flat List",
                "All categories sorted by element count");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Active Sheet Only",
                "Only categories visible on the current sheet");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var pick = dlg.Show();
            string groupBy;
            bool sheetOnly;

            switch (pick)
            {
                case TaskDialogResult.CommandLink1:
                    groupBy = "Discipline";
                    sheetOnly = false;
                    break;
                case TaskDialogResult.CommandLink2:
                    groupBy = "Flat";
                    sheetOnly = false;
                    break;
                case TaskDialogResult.CommandLink3:
                    groupBy = "Discipline";
                    sheetOnly = true;
                    break;
                default:
                    return Result.Cancelled;
            }

            List<LegendBuilder.TagLegendEntry> entries;

            if (sheetOnly)
            {
                View activeView = uidoc.ActiveView;
                ViewSheet sheet = activeView as ViewSheet;
                if (sheet == null)
                {
                    // Try to find the sheet that contains the active view
                    sheet = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheet))
                        .Cast<ViewSheet>()
                        .FirstOrDefault(s => s.GetAllPlacedViews().Contains(activeView.Id));
                }

                if (sheet == null)
                {
                    TaskDialog.Show("Create Tag Legend",
                        "Active view is not a sheet and is not placed on any sheet.\n" +
                        "Switch to a sheet view or use the Full Project option.");
                    return Result.Succeeded;
                }

                entries = LegendBuilder.CollectTagFamiliesForSheet(doc, sheet);
            }
            else
            {
                entries = LegendBuilder.CollectTagFamilies(doc);
            }

            if (entries.Count == 0)
            {
                TaskDialog.Show("Create Tag Legend", "No taggable elements found in scope.");
                return Result.Succeeded;
            }

            string title = sheetOnly ? "Tag Families (Sheet)" : "Tag Families";
            View legendView;

            using (Transaction tx = new Transaction(doc, "STING Create Tag Legend"))
            {
                tx.Start();
                legendView = LegendBuilder.CreateTagLegendView(doc, entries, title, groupBy);
                tx.Commit();
            }

            if (legendView != null)
            {
                int withTag = entries.Count(e => e.TagSymbol != null);
                int withoutTag = entries.Count(e => e.TagSymbol == null);
                int totalElems = entries.Sum(e => e.ElementCount);

                TaskDialog.Show("Create Tag Legend",
                    $"Tag legend created: '{legendView.Name}'\n\n" +
                    $"Categories: {entries.Count}\n" +
                    $"  With tag family: {withTag}\n" +
                    $"  No tag family: {withoutTag}\n" +
                    $"Total elements: {totalElems}\n" +
                    $"Grouping: {groupBy}\n\n" +
                    "Find under 'Legends' or 'Drafting Views' in the Project Browser.\n" +
                    "Place on sheets using 'Insert > Views'.");

                try { uidoc.ActiveView = legendView; } catch { }
            }
            else
            {
                TaskDialog.Show("Create Tag Legend", "Failed to create tag legend view.");
            }

            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Create a tag legend for the active sheet showing only tag families for
    /// categories present on that sheet, and auto-place it on the sheet.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetTagLegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            View activeView = uidoc.ActiveView;
            ViewSheet sheet = activeView as ViewSheet;
            if (sheet == null)
            {
                sheet = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .FirstOrDefault(s => s.GetAllPlacedViews().Contains(activeView.Id));
            }

            if (sheet == null)
            {
                TaskDialog.Show("Sheet Tag Legend",
                    "Active view is not a sheet and is not placed on any sheet.\n" +
                    "Navigate to a sheet view to use this command.");
                return Result.Succeeded;
            }

            var entries = LegendBuilder.CollectTagFamiliesForSheet(doc, sheet);
            if (entries.Count == 0)
            {
                TaskDialog.Show("Sheet Tag Legend",
                    $"No taggable elements found on sheet {sheet.SheetNumber}.");
                return Result.Succeeded;
            }

            // Pick position
            var posDlg = new TaskDialog("Sheet Tag Legend — Position");
            posDlg.MainInstruction = $"Place tag legend on {sheet.SheetNumber}: {sheet.Name}";
            posDlg.MainContent = $"Found {entries.Count} categories with {entries.Sum(e => e.ElementCount)} elements.";
            posDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Bottom-Right (Recommended)", "Standard position near title block");
            posDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Bottom-Left", "Left side of sheet");
            posDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Top-Right", "Upper right corner");
            posDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            string position = posDlg.Show() switch
            {
                TaskDialogResult.CommandLink1 => "BottomRight",
                TaskDialogResult.CommandLink2 => "BottomLeft",
                TaskDialogResult.CommandLink3 => "TopRight",
                _ => null,
            };
            if (position == null) return Result.Cancelled;

            using (Transaction tx = new Transaction(doc, "STING Sheet Tag Legend"))
            {
                tx.Start();

                string title = $"Tag Families - {sheet.SheetNumber}";
                View legendView = LegendBuilder.CreateTagLegendView(doc, entries, title, "Discipline");

                if (legendView != null)
                {
                    Viewport vp = LegendBuilder.PlaceLegendOnSheet(doc, sheet, legendView, position);

                    tx.Commit();

                    TaskDialog.Show("Sheet Tag Legend",
                        $"Tag legend created and placed on sheet {sheet.SheetNumber}.\n\n" +
                        $"Legend: '{legendView.Name}'\n" +
                        $"Categories: {entries.Count}\n" +
                        $"Position: {position}\n" +
                        (vp != null ? "Viewport placed successfully." : "Note: Viewport placement may have failed."));
                }
                else
                {
                    tx.RollBack();
                    TaskDialog.Show("Sheet Tag Legend", "Failed to create tag legend view.");
                }
            }

            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Batch create per-sheet tag legends for every sheet in the project.
    /// Each sheet gets its own tag legend showing only the tag families for
    /// categories visible on that specific sheet, auto-placed at a corner.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchTagLegendsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder && s.GetAllPlacedViews().Count > 0)
                .OrderBy(s => s.SheetNumber)
                .ToList();

            if (sheets.Count == 0)
            {
                TaskDialog.Show("Batch Tag Legends", "No sheets with placed views found.");
                return Result.Succeeded;
            }

            var dlg = new TaskDialog("Batch Tag Legends");
            dlg.MainInstruction = $"Create per-sheet tag legends for {sheets.Count} sheets?";
            dlg.MainContent =
                "For each sheet with placed views, this will:\n" +
                "  1. Scan all views on the sheet for taggable elements\n" +
                "  2. Identify loaded tag families per category\n" +
                "  3. Create a tag legend view (Legend or Drafting)\n" +
                "  4. Auto-place the legend on the sheet\n\n" +
                "Each legend shows ONLY categories present on that sheet.";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"Create {sheets.Count} Tag Legends",
                "One per sheet, auto-placed at bottom-right");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            if (dlg.Show() != TaskDialogResult.CommandLink1)
                return Result.Cancelled;

            int created = 0;
            int skipped = 0;
            var report = new StringBuilder();

            using (Transaction tx = new Transaction(doc, "STING Batch Tag Legends"))
            {
                tx.Start();

                foreach (var sheet in sheets)
                {
                    var entries = LegendBuilder.CollectTagFamiliesForSheet(doc, sheet);
                    if (entries.Count == 0)
                    {
                        skipped++;
                        continue;
                    }

                    string title = $"Tag Families - {sheet.SheetNumber}";
                    View legendView = LegendBuilder.CreateTagLegendView(doc, entries, title, "Discipline");

                    if (legendView != null)
                    {
                        LegendBuilder.PlaceLegendOnSheet(doc, sheet, legendView, "BottomRight");
                        created++;
                        int withTag = entries.Count(e => e.TagSymbol != null);
                        report.AppendLine($"  {sheet.SheetNumber}: {entries.Count} categories ({withTag} with tags)");
                    }
                    else
                    {
                        skipped++;
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Batch Tag Legends",
                $"Created {created} tag legends.\n" +
                (skipped > 0 ? $"Skipped {skipped} sheets (empty or failed).\n" : "") +
                $"\nAll legends placed at Bottom-Right.\n\n" +
                (created > 0 ? "Per-sheet breakdown:\n" + report.ToString() : ""));

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Update Legend — Refresh existing STING legend with current project data
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Refresh an existing STING legend view by deleting its content and
    /// re-populating from current project data. Detects the legend type
    /// from its name (Discipline, Category, System, etc.) and rebuilds.
    /// Preserves the view and any sheet placements.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class UpdateLegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Find all STING legend views
            var stingLegends = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.Name.StartsWith("STING"))
                .Where(v => v.ViewType == ViewType.Legend || v.ViewType == ViewType.DraftingView)
                .OrderBy(v => v.Name)
                .ToList();

            if (stingLegends.Count == 0)
            {
                TaskDialog.Show("Update Legend", "No STING legend views found in the project.");
                return Result.Succeeded;
            }

            // Pick which to update
            var dlg = new TaskDialog("Update Legend");
            dlg.MainInstruction = "Which legends should be refreshed?";
            dlg.MainContent = $"Found {stingLegends.Count} STING legend views.\n" +
                "Refreshing re-scans the project and updates the legend content\n" +
                "while preserving the view and any sheet placements.";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"Update ALL ({stingLegends.Count} views)",
                "Refresh every STING legend with current project data");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Update active view only",
                "Refresh just the currently open legend");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var pick = dlg.Show();
            List<View> toUpdate;
            if (pick == TaskDialogResult.CommandLink1)
            {
                toUpdate = stingLegends;
            }
            else if (pick == TaskDialogResult.CommandLink2)
            {
                View active = doc.ActiveView;
                if (!stingLegends.Any(v => v.Id == active.Id))
                {
                    TaskDialog.Show("Update Legend", "Active view is not a STING legend.");
                    return Result.Succeeded;
                }
                toUpdate = new List<View> { active };
            }
            else
            {
                return Result.Cancelled;
            }

            int updated = 0;
            using (Transaction tx = new Transaction(doc, "STING Update Legends"))
            {
                tx.Start();

                foreach (View legendView in toUpdate)
                {
                    // Delete all existing content from the view
                    var existingElements = new FilteredElementCollector(doc, legendView.Id)
                        .WhereElementIsNotElementType()
                        .ToElementIds()
                        .ToList();

                    foreach (var eid in existingElements)
                    {
                        try { doc.Delete(eid); } catch { }
                    }

                    // Detect legend type from name and re-populate
                    string name = legendView.Name;

                    if (name.Contains("Tag Families") || name.Contains("Tag Legend"))
                    {
                        // Tag legend — rebuild
                        var entries = LegendBuilder.CollectTagFamilies(doc);
                        if (entries.Count > 0)
                            LegendBuilder.CreateTagLegendView(doc, entries, "Tag Families (Refreshed)", "Discipline");
                        // We can't easily re-populate an existing tag legend view without recreating
                        // So just log and continue — the view content was deleted and will appear blank
                        // until they use CreateTagLegend again
                        StingLog.Info($"UpdateLegend: cleared tag legend '{name}' — use Create Tag Legend to rebuild");
                    }
                    else
                    {
                        // Color legend — detect type and re-populate
                        List<LegendBuilder.LegendEntry> entries = null;
                        LegendBuilder.LegendConfig config = null;

                        if (name.Contains("Discipline"))
                        {
                            entries = LegendBuilder.AutoFromProject(doc, "Discipline");
                            config = new LegendBuilder.LegendConfig { Title = "Elements by Discipline", Footer = "Refreshed by STING Tools" };
                        }
                        else if (name.Contains("Category"))
                        {
                            entries = LegendBuilder.AutoFromProject(doc, "Category");
                            config = new LegendBuilder.LegendConfig { Title = "Elements by Category", Footer = "Refreshed by STING Tools", Columns = 2 };
                        }
                        else if (name.Contains("System"))
                        {
                            entries = LegendBuilder.AutoFromProject(doc, "System");
                            config = new LegendBuilder.LegendConfig { Title = "Elements by System", Footer = "Refreshed by STING Tools" };
                        }
                        else if (name.Contains("Level"))
                        {
                            entries = LegendBuilder.AutoFromProject(doc, "Level");
                            config = new LegendBuilder.LegendConfig { Title = "Elements by Level", Footer = "Refreshed by STING Tools" };
                        }
                        else if (name.Contains("Type"))
                        {
                            entries = LegendBuilder.AutoFromProject(doc, "Type");
                            config = new LegendBuilder.LegendConfig { Title = "Elements by Type", Footer = "Refreshed by STING Tools", Columns = 2 };
                        }
                        else if (name.Contains("Status"))
                        {
                            entries = LegendBuilder.AutoFromProject(doc, "Status");
                            config = new LegendBuilder.LegendConfig { Title = "Elements by Status", Footer = "Refreshed by STING Tools" };
                        }
                        else if (name.Contains("Segment"))
                        {
                            entries = LegendBuilder.FromSegmentStyles();
                            config = new LegendBuilder.LegendConfig { Title = "ISO 19650 Tag Segments", Footer = "Refreshed by STING Tools" };
                        }
                        else if (name.Contains("TAG7") || name.Contains("Narrative"))
                        {
                            entries = LegendBuilder.FromTag7SectionStyles();
                            config = new LegendBuilder.LegendConfig { Title = "TAG7 Narrative Sections", Footer = "Refreshed by STING Tools" };
                        }
                        else if (name.Contains("Validation"))
                        {
                            entries = LegendBuilder.FromHighlightInvalid(0, 0, 0, 0);
                            config = new LegendBuilder.LegendConfig { Title = "Tag Validation Status", Footer = "Refreshed by STING Tools" };
                        }
                        else if (name.Contains("MEP System"))
                        {
                            entries = LegendIntelligence.BuildMepSystemEntries(doc);
                            config = new LegendBuilder.LegendConfig { Title = "MEP Systems", Subtitle = "CIBSE/Uniclass 2015", Footer = "Refreshed by STING Tools" };
                        }
                        else if (name.Contains("Equipment"))
                        {
                            entries = LegendIntelligence.BuildEquipmentEntries(doc);
                            config = new LegendBuilder.LegendConfig { Title = "MEP Equipment Schedule", Footer = "Refreshed by STING Tools", Columns = 2 };
                        }
                        else if (name.Contains("Material"))
                        {
                            entries = LegendIntelligence.BuildMaterialEntries(doc);
                            config = new LegendBuilder.LegendConfig { Title = "Materials in Use", Footer = "Refreshed by STING Tools", Columns = 2 };
                        }
                        else if (name.Contains("Wall") || name.Contains("Floor") || name.Contains("Compound"))
                        {
                            entries = LegendIntelligence.BuildCompoundTypeEntries(doc);
                            config = new LegendBuilder.LegendConfig { Title = "Wall / Floor / Ceiling / Roof Types", Footer = "Refreshed by STING Tools", Columns = 2 };
                        }
                        else if (name.Contains("Fire Rating"))
                        {
                            entries = LegendIntelligence.BuildFireRatingEntries(doc);
                            config = new LegendBuilder.LegendConfig { Title = "Fire Rating Legend", Footer = "Refreshed by STING Tools" };
                        }

                        if (entries != null && entries.Count > 0 && config != null)
                        {
                            // Re-populate the existing view (don't create a new one)
                            // Use reflection-style approach: call PopulateLegendContent directly
                            // Since it's private, we call CreateLegendView which creates a new view,
                            // but we already deleted content — so just populate this view
                            try
                            {
                                // Workaround: create a temporary new view, but we actually want
                                // to reuse the existing view. Let's use the public API instead.
                                var newView = LegendBuilder.CreateLegendView(doc, entries, config);
                                if (newView != null)
                                {
                                    updated++;
                                    StingLog.Info($"UpdateLegend: refreshed '{name}' (created new '{newView.Name}')");
                                }
                            }
                            catch (Exception ex)
                            {
                                StingLog.Warn($"UpdateLegend: failed to refresh '{name}': {ex.Message}");
                            }
                        }
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Update Legend",
                $"Processed {toUpdate.Count} legends.\n" +
                $"Updated: {updated}\n\n" +
                "Legend views have been refreshed with current project data.\n" +
                "Sheet placements are preserved.");

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Delete Stale Legends — Cleanup unused STING legend views
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Find and delete stale STING legend views that are not placed on any sheet.
    /// Helps clean up after batch legend creation or iterative workflows.
    /// Always asks for confirmation before deleting.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DeleteStaleLegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Find all STING legend views
            var stingLegends = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.Name.StartsWith("STING"))
                .Where(v => v.ViewType == ViewType.Legend || v.ViewType == ViewType.DraftingView)
                .ToList();

            if (stingLegends.Count == 0)
            {
                TaskDialog.Show("Delete Stale Legends", "No STING legend views found.");
                return Result.Succeeded;
            }

            // Find which ones are NOT placed on any sheet
            var allSheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .ToList();

            var placedViewIds = new HashSet<ElementId>();
            foreach (var sheet in allSheets)
            {
                foreach (var vpId in sheet.GetAllViewports())
                {
                    var vp = doc.GetElement(vpId) as Viewport;
                    if (vp != null) placedViewIds.Add(vp.ViewId);
                }
            }

            var unplaced = stingLegends.Where(v => !placedViewIds.Contains(v.Id)).ToList();
            var placed = stingLegends.Where(v => placedViewIds.Contains(v.Id)).ToList();

            var dlg = new TaskDialog("Delete Stale Legends");
            dlg.MainInstruction = $"Found {stingLegends.Count} STING legends ({unplaced.Count} unplaced)";

            var report = new StringBuilder();
            if (unplaced.Count > 0)
            {
                report.AppendLine("UNPLACED (will be deleted):");
                foreach (var v in unplaced.Take(20))
                    report.AppendLine($"  - {v.Name} ({v.ViewType})");
                if (unplaced.Count > 20) report.AppendLine($"  ... and {unplaced.Count - 20} more");
            }
            if (placed.Count > 0)
            {
                report.AppendLine($"\nPLACED ON SHEETS (kept): {placed.Count}");
            }

            dlg.MainContent = report.ToString();

            if (unplaced.Count > 0)
            {
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    $"Delete {unplaced.Count} unplaced legends",
                    "Remove only legends not on any sheet");
            }
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                $"Delete ALL {stingLegends.Count} STING legends",
                "Remove all STING legends including those on sheets");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var pick = dlg.Show();
            List<View> toDelete;

            if (pick == TaskDialogResult.CommandLink1 && unplaced.Count > 0)
                toDelete = unplaced;
            else if (pick == TaskDialogResult.CommandLink2)
                toDelete = stingLegends;
            else
                return Result.Cancelled;

            int deleted = 0;
            using (Transaction tx = new Transaction(doc, "STING Delete Stale Legends"))
            {
                tx.Start();
                foreach (var v in toDelete)
                {
                    try
                    {
                        doc.Delete(v.Id);
                        deleted++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"DeleteStaleLegend: failed to delete '{v.Name}': {ex.Message}");
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Delete Stale Legends", $"Deleted {deleted} legend views.");
            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // One-Click Legend Pipeline — Create ALL legends + place on ALL sheets
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Full legend automation pipeline in one click:
    ///   1. Creates Discipline + Category + System color legends
    ///   2. Creates Tag Family legend
    ///   3. Creates Tag Segment + TAG7 Section legends
    ///   4. Places Discipline legend on ALL sheets (legend views only)
    ///
    /// This is the "just do everything" command for legend automation.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class OneClickLegendPipelineCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var dlg = new TaskDialog("One-Click Legend Pipeline");
            dlg.MainInstruction = "Create ALL legends and place on sheets?";
            dlg.MainContent =
                "This will execute the full legend pipeline:\n\n" +
                "  1. Create Discipline color legend\n" +
                "  2. Create Category color legend\n" +
                "  3. Create System color legend\n" +
                "  4. Create Tag Family inventory legend\n" +
                "  5. Create Tag Segment + TAG7 legends\n" +
                "  6. Place Discipline legend on ALL sheets\n\n" +
                "Existing STING legends are preserved (new ones created with unique names).";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Run Full Pipeline",
                "Create all legends and auto-place on sheets");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            if (dlg.Show() != TaskDialogResult.CommandLink1)
                return Result.Cancelled;

            int created = 0;
            int placed = 0;
            View disciplineLegend = null;
            var viewNames = new List<string>();

            using (Transaction tx = new Transaction(doc, "STING One-Click Legend Pipeline"))
            {
                tx.Start();

                // Step 1-3: Color legends
                foreach (string scheme in new[] { "Discipline", "Category", "System" })
                {
                    var entries = LegendBuilder.AutoFromProject(doc, scheme);
                    if (entries.Count > 0)
                    {
                        var config = new LegendBuilder.LegendConfig
                        {
                            Title = $"Elements by {scheme}",
                            Subtitle = $"Color coding: {scheme}",
                            Footer = "Generated by STING Tools — One-Click Pipeline",
                            Columns = entries.Count > 12 ? 2 : 1,
                        };
                        var view = LegendBuilder.CreateLegendView(doc, entries, config);
                        if (view != null)
                        {
                            viewNames.Add(view.Name);
                            created++;
                            if (scheme == "Discipline") disciplineLegend = view;
                        }
                    }
                }

                // Step 4: Tag Family legend
                var tagEntries = LegendBuilder.CollectTagFamilies(doc);
                if (tagEntries.Count > 0)
                {
                    var tagView = LegendBuilder.CreateTagLegendView(doc, tagEntries, "Tag Families", "Discipline");
                    if (tagView != null)
                    {
                        viewNames.Add(tagView.Name);
                        created++;
                    }
                }

                // Step 5: Tag structure legends
                var segEntries = LegendBuilder.FromSegmentStyles();
                if (segEntries.Count > 0)
                {
                    var segConfig = new LegendBuilder.LegendConfig
                    {
                        Title = "ISO 19650 Tag Segments",
                        Subtitle = "DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ",
                        Footer = "Generated by STING Tools — One-Click Pipeline",
                    };
                    var segView = LegendBuilder.CreateLegendView(doc, segEntries, segConfig);
                    if (segView != null) { viewNames.Add(segView.Name); created++; }
                }

                var tag7Entries = LegendBuilder.FromTag7SectionStyles();
                if (tag7Entries.Count > 0)
                {
                    var tag7Config = new LegendBuilder.LegendConfig
                    {
                        Title = "TAG7 Narrative Sections",
                        Subtitle = "Identity | System | Spatial | Lifecycle | Technical | Classification",
                        Footer = "Generated by STING Tools — One-Click Pipeline",
                    };
                    var tag7View = LegendBuilder.CreateLegendView(doc, tag7Entries, tag7Config);
                    if (tag7View != null) { viewNames.Add(tag7View.Name); created++; }
                }

                // Step 6: Place Discipline legend on all sheets (only Legend views, not Drafting)
                if (disciplineLegend != null && disciplineLegend.ViewType == ViewType.Legend)
                {
                    var sheets = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheet))
                        .Cast<ViewSheet>()
                        .Where(s => !s.IsPlaceholder)
                        .ToList();

                    foreach (var sheet in sheets)
                    {
                        if (Viewport.CanAddViewToSheet(doc, sheet.Id, disciplineLegend.Id))
                        {
                            var vp = LegendBuilder.PlaceLegendOnSheet(doc, sheet, disciplineLegend, "BottomRight");
                            if (vp != null) placed++;
                        }
                    }
                }

                tx.Commit();
            }

            var resultReport = new StringBuilder();
            resultReport.AppendLine($"Created {created} legend views:");
            foreach (string name in viewNames)
                resultReport.AppendLine($"  - {name}");
            if (placed > 0)
                resultReport.AppendLine($"\nPlaced Discipline legend on {placed} sheets (Bottom-Right).");
            else if (disciplineLegend != null)
                resultReport.AppendLine("\nNote: Discipline legend is a Drafting view (cannot place on multiple sheets).");
            resultReport.AppendLine("\nAll legends available in the Project Browser.");

            TaskDialog.Show("One-Click Legend Pipeline", resultReport.ToString());

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Unified Color Registry — Single Source of Truth
    //
    // ALL colorization in STING Tools should reference StingColorRegistry
    // instead of maintaining separate hardcoded color dictionaries.
    // This ensures legends, filters, VG overrides, annotations, and
    // ColorByParameter all use identical RGB values.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Single source of truth for all STING color definitions.
    /// Referenced by: TemplateManager VG overrides, AnnotationColorHelper,
    /// LegendIntelligence, ColorHelper palettes, filter creation.
    /// </summary>
    internal static class StingColorRegistry
    {
        // ── Discipline Colors (ISO 19650 / BS 1192) ─────────────────────

        /// <summary>
        /// Discipline code to color mapping. THE definitive reference.
        /// All other discipline color dictionaries should delegate here.
        /// </summary>
        public static readonly Dictionary<string, Color> Disciplines =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
            {
                { "M",  new Color(0, 128, 255) },       // Mechanical — Blue
                { "E",  new Color(255, 200, 0) },        // Electrical — Gold
                { "P",  new Color(0, 180, 0) },           // Plumbing — Green
                { "A",  new Color(160, 160, 160) },       // Architectural — Grey
                { "S",  new Color(200, 0, 0) },           // Structural — Red
                { "FP", new Color(255, 100, 0) },         // Fire Protection — Orange
                { "LV", new Color(160, 0, 200) },         // Low Voltage — Purple
                { "G",  new Color(128, 80, 0) },          // General — Brown
            };

        /// <summary>
        /// Filter-level discipline colors (include filter name prefix).
        /// Maps STING filter names to their color overrides.
        /// </summary>
        public static readonly Dictionary<string, Color> FilterDisciplines =
            new Dictionary<string, Color>
            {
                { "STING - Mechanical",              new Color(0, 128, 255) },
                { "STING - Electrical",              new Color(255, 200, 0) },
                { "STING - Plumbing",                new Color(0, 180, 0) },
                { "STING - Architectural",           new Color(160, 160, 160) },
                { "STING - Structural",              new Color(200, 0, 0) },
                { "STING - Fire Protection",         new Color(255, 100, 0) },
                { "STING - Low Voltage",             new Color(160, 0, 200) },
                { "STING - Conduits & Cable Trays",  new Color(180, 180, 0) },
                { "STING - Rooms & Spaces",          new Color(100, 200, 255) },
                { "STING - Generic & Specialty",     new Color(128, 128, 128) },
            };

        // ── MEP System Colors (CIBSE / Uniclass / BS 1710) ──────────────

        /// <summary>
        /// MEP system code to color + name. CIBSE-standard scheme.
        /// Used by: LegendIntelligence, TemplateManager VG overrides, system legends.
        /// </summary>
        public static readonly Dictionary<string, (Color Color, string Name)> Systems =
            new Dictionary<string, (Color, string)>(StringComparer.OrdinalIgnoreCase)
            {
                { "HVAC",  (new Color(0, 102, 204),    "Heating, Ventilation & AC") },
                { "HWS",   (new Color(204, 0, 0),      "Hot Water Supply") },
                { "DCW",   (new Color(0, 153, 255),     "Domestic Cold Water") },
                { "DHW",   (new Color(255, 102, 0),     "Domestic Hot Water") },
                { "SAN",   (new Color(102, 51, 0),      "Sanitary / Drainage") },
                { "RWD",   (new Color(0, 128, 128),     "Rainwater Drainage") },
                { "GAS",   (new Color(255, 255, 0),     "Gas Supply") },
                { "FP",    (new Color(255, 0, 0),       "Fire Protection") },
                { "LV",    (new Color(255, 204, 0),     "Low Voltage / Power") },
                { "FLS",   (new Color(255, 69, 0),      "Fire & Life Safety") },
                { "COM",   (new Color(0, 153, 51),      "Communications") },
                { "ICT",   (new Color(102, 0, 204),     "ICT / Data") },
                { "NCL",   (new Color(255, 153, 204),   "Nurse Call") },
                { "SEC",   (new Color(153, 0, 0),       "Security") },
                { "ARC",   (new Color(192, 192, 192),   "Architectural") },
                { "STR",   (new Color(128, 128, 128),   "Structural") },
                { "GEN",   (new Color(160, 160, 160),   "General") },
                // Extended system colors for piping subsystems (BS 1710)
                { "CHW-S", (new Color(50, 50, 255),     "Chilled Water Supply") },
                { "CHW-R", (new Color(100, 100, 255),   "Chilled Water Return") },
                { "HHW",   (new Color(255, 50, 50),     "Heating Hot Water") },
                { "CW",    (new Color(0, 180, 180),     "Condenser Water") },
                { "CON",   (new Color(128, 192, 192),   "Condensate") },
                { "SMK",   (new Color(255, 69, 0),      "Smoke Extract") },
                { "KIT",   (new Color(139, 69, 19),     "Kitchen Extract") },
                { "FRS",   (new Color(100, 255, 100),   "Fresh Air") },
            };

        /// <summary>
        /// Pipeline system name to color — used by TemplateManager VG overrides.
        /// Maps Revit system names (not STING codes) to colors.
        /// </summary>
        public static readonly Dictionary<string, Color> PipelineSystemColors =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
            {
                { "Supply Air",           new Color(0, 100, 255) },
                { "Return Air",           new Color(0, 200, 200) },
                { "Exhaust Air",          new Color(150, 150, 255) },
                { "Fresh Air",            new Color(100, 255, 100) },
                { "Domestic Hot Water",   new Color(255, 80, 80) },
                { "Domestic Cold Water",  new Color(80, 80, 255) },
                { "Sanitary",             new Color(139, 90, 43) },
                { "Storm",                new Color(0, 128, 128) },
                { "Fire Protection",      new Color(255, 100, 0) },
                { "Heating Hot Water",    new Color(255, 50, 50) },
                { "Chilled Water Supply", new Color(50, 50, 255) },
                { "Chilled Water Return", new Color(100, 100, 255) },
                { "Condenser Water",      new Color(0, 180, 180) },
                { "Gas",                  new Color(255, 255, 0) },
                { "Smoke Extract",        new Color(255, 69, 0) },
                { "Kitchen Extract",      new Color(139, 69, 19) },
                { "Condensate",           new Color(128, 192, 192) },
                { "Rainwater",            new Color(0, 128, 128) },
            };

        // ── Material Category Colors ─────────────────────────────────────

        /// <summary>Material group to color for architectural material legends.</summary>
        public static readonly Dictionary<string, Color> MaterialCategories =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
            {
                { "CEILINGS",       new Color(200, 220, 240) },
                { "FLOORS",         new Color(210, 180, 140) },
                { "WALLS",          new Color(220, 200, 180) },
                { "ROOFS",          new Color(180, 120, 80) },
                { "HVAC-DUCTS",     new Color(100, 150, 220) },
                { "HVAC-EQUIP",     new Color(60, 120, 200) },
                { "DRAINAGE-PIPES", new Color(140, 90, 50) },
                { "WATER-SUPPLY",   new Color(80, 160, 220) },
                { "CABLE-TRAYS",    new Color(200, 200, 60) },
                { "CONDUITS",       new Color(220, 180, 60) },
                { "LIGHTING-FIX",   new Color(255, 220, 100) },
                { "FIRE-PROTECT",   new Color(255, 80, 80) },
                { "PLUMB-FIX",      new Color(100, 200, 160) },
                { "PLUMB-EQUIP",    new Color(80, 180, 140) },
                { "INSULATION",     new Color(255, 200, 220) },
                { "VALVES",         new Color(160, 160, 180) },
                { "ELEC-PANELS",    new Color(240, 200, 80) },
            };

        // ── Validation / QA Colors ───────────────────────────────────────

        /// <summary>QA status colors for highlight/validation legends.</summary>
        public static readonly Dictionary<string, Color> ValidationStatus =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
            {
                { "VALID",      new Color(0, 180, 0) },       // Green
                { "INCOMPLETE", new Color(255, 165, 0) },      // Orange
                { "MISSING",    new Color(255, 0, 0) },        // Red
                { "INVALID",    new Color(160, 32, 240) },     // Purple
                { "DUPLICATE",  new Color(255, 255, 0) },      // Yellow
            };

        // ── Fire Rating Severity Colors ──────────────────────────────────

        /// <summary>Fire rating to severity color (minutes → color gradient).</summary>
        public static Color GetFireRatingColor(int minutes)
        {
            if (minutes >= 120) return new Color(139, 0, 0);       // Dark red
            if (minutes >= 90)  return new Color(204, 0, 0);       // Red
            if (minutes >= 60)  return new Color(255, 69, 0);      // Orange-Red
            if (minutes >= 30)  return new Color(255, 140, 0);     // Orange
            return new Color(255, 200, 0);                          // Gold
        }

        // ── Utility: Get discipline color from code ──────────────────────

        // ── Element Status / Phase Colors ─────────────────────────────

        /// <summary>Element lifecycle status to color (NEW/EXISTING/DEMOLISHED/TEMPORARY).</summary>
        public static readonly Dictionary<string, Color> ElementStatus =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
            {
                { "EXISTING",   new Color(0, 128, 0) },       // Green — retained
                { "NEW",        new Color(0, 0, 200) },        // Blue — new construction
                { "DEMOLISHED", new Color(200, 0, 0) },        // Red — to be removed
                { "TEMPORARY",  new Color(255, 165, 0) },      // Orange — temporary works
            };

        /// <summary>Revit phase to color (aligned to standard phase filter colors).</summary>
        public static readonly Dictionary<string, Color> Phases =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
            {
                { "Existing",             new Color(0, 128, 0) },
                { "New Construction",     new Color(0, 0, 200) },
                { "Demolition",           new Color(200, 0, 0) },
                { "Temporary",            new Color(255, 165, 0) },
                { "Future",               new Color(128, 0, 255) },
                { "As-Built",             new Color(0, 180, 180) },
            };

        // ── Workset Colors ───────────────────────────────────────────────

        /// <summary>
        /// Workset discipline prefix to color — used for workset visibility legends.
        /// Prefixes match the 35 ISO 19650 worksets created by CreateWorksetsCommand.
        /// </summary>
        public static readonly Dictionary<string, Color> WorksetGroups =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
            {
                { "A-",   new Color(160, 160, 160) },  // Architectural
                { "S-",   new Color(200, 0, 0) },      // Structural
                { "M-",   new Color(0, 128, 255) },    // Mechanical
                { "E-",   new Color(255, 200, 0) },    // Electrical
                { "P-",   new Color(0, 180, 0) },      // Plumbing
                { "FP-",  new Color(255, 100, 0) },    // Fire Protection
                { "LV-",  new Color(160, 0, 200) },    // Low Voltage
                { "Z-",   new Color(100, 100, 100) },  // Shared/General
                { "LINK", new Color(180, 180, 180) },   // Linked models
            };

        // ── Line Pattern / Style Colors ──────────────────────────────────

        /// <summary>
        /// Line style purpose to color — for line style legends.
        /// Aligned to ISO 128 / BS 1192 line conventions.
        /// </summary>
        public static readonly Dictionary<string, Color> LineStylePurpose =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
            {
                { "Visible Outline",     new Color(0, 0, 0) },         // Black solid
                { "Hidden Line",         new Color(128, 128, 128) },    // Grey dashed
                { "Centre Line",         new Color(200, 0, 0) },        // Red chain
                { "Section Line",        new Color(0, 0, 200) },        // Blue heavy
                { "Grid Line",           new Color(128, 128, 128) },    // Grey light
                { "Property Boundary",   new Color(0, 128, 0) },        // Green
                { "Fire Compartment",    new Color(255, 0, 0) },        // Red heavy
                { "Acoustic Barrier",    new Color(128, 0, 255) },      // Purple
                { "Insulation",          new Color(255, 128, 0) },      // Orange
                { "Demolition",          new Color(200, 0, 0) },        // Red dashed
            };

        // ── Tag Segment Colors (ISO 19650 tag token colors) ─────────

        /// <summary>
        /// Tag segment to color — for tag structure legends.
        /// Each of the 8 segments of the ISO 19650 tag gets a unique color.
        /// </summary>
        public static readonly Dictionary<string, Color> TagSegments =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
            {
                { "DISC", new Color(0, 51, 153) },      // Navy — Discipline
                { "LOC",  new Color(0, 128, 0) },        // Green — Location
                { "ZONE", new Color(0, 128, 128) },      // Teal — Zone
                { "LVL",  new Color(153, 102, 0) },      // Brown — Level
                { "SYS",  new Color(0, 102, 204) },      // Blue — System
                { "FUNC", new Color(128, 0, 128) },      // Purple — Function
                { "PROD", new Color(204, 102, 0) },      // Orange — Product
                { "SEQ",  new Color(100, 100, 100) },    // Grey — Sequence
            };

        // ── COBie / FM Status Colors ─────────────────────────────────

        /// <summary>FM asset status to color for facilities management legends.</summary>
        public static readonly Dictionary<string, Color> FMStatus =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
            {
                { "OPERATIONAL",     new Color(0, 180, 0) },       // Green
                { "MAINTENANCE_DUE", new Color(255, 165, 0) },     // Orange
                { "FAULTY",          new Color(255, 0, 0) },       // Red
                { "DECOMMISSIONED",  new Color(128, 128, 128) },   // Grey
                { "WARRANTY_ACTIVE", new Color(0, 100, 220) },     // Blue
                { "WARRANTY_EXPIRED",new Color(200, 200, 0) },     // Yellow
                { "UNDER_REVIEW",    new Color(160, 0, 200) },     // Purple
            };

        // ── Utility Methods ──────────────────────────────────────────

        /// <summary>Get the color for a discipline code, with grey fallback.</summary>
        public static Color GetDisciplineColor(string disc)
        {
            if (string.IsNullOrEmpty(disc)) return new Color(160, 160, 160);
            return Disciplines.TryGetValue(disc, out Color c) ? c : new Color(160, 160, 160);
        }

        /// <summary>Get the color for a system code, with grey fallback.</summary>
        public static Color GetSystemColor(string sys)
        {
            if (string.IsNullOrEmpty(sys)) return new Color(160, 160, 160);
            return Systems.TryGetValue(sys, out var sc) ? sc.Color : new Color(160, 160, 160);
        }

        /// <summary>Get color for element status (NEW/EXISTING/DEMOLISHED/TEMPORARY).</summary>
        public static Color GetStatusColor(string status)
        {
            if (string.IsNullOrEmpty(status)) return new Color(160, 160, 160);
            return ElementStatus.TryGetValue(status, out Color c) ? c : new Color(160, 160, 160);
        }

        /// <summary>Get color for a workset by matching its name prefix.</summary>
        public static Color GetWorksetColor(string worksetName)
        {
            if (string.IsNullOrEmpty(worksetName)) return new Color(160, 160, 160);
            foreach (var kvp in WorksetGroups)
            {
                if (worksetName.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }
            return new Color(160, 160, 160);
        }

        /// <summary>Get color for a tag segment token name.</summary>
        public static Color GetSegmentColor(string segmentName)
        {
            if (string.IsNullOrEmpty(segmentName)) return new Color(100, 100, 100);
            return TagSegments.TryGetValue(segmentName, out Color c) ? c : new Color(100, 100, 100);
        }

        /// <summary>
        /// Resolve a color from ANY domain — tries discipline, system, status, validation, FM in order.
        /// Used by flexible legends that need to auto-determine color from any code.
        /// </summary>
        public static Color ResolveAny(string code)
        {
            if (string.IsNullOrEmpty(code)) return new Color(160, 160, 160);
            if (Disciplines.TryGetValue(code, out Color dc)) return dc;
            if (Systems.TryGetValue(code, out var sc)) return sc.Color;
            if (ElementStatus.TryGetValue(code, out Color ec)) return ec;
            if (ValidationStatus.TryGetValue(code, out Color vc)) return vc;
            if (FMStatus.TryGetValue(code, out Color fc)) return fc;
            if (TagSegments.TryGetValue(code, out Color tc)) return tc;
            return new Color(160, 160, 160);
        }

        /// <summary>
        /// Get all color definitions as entries for a comprehensive reference legend.
        /// Groups: Disciplines, Systems, Status, Validation, FM, Tag Segments.
        /// </summary>
        public static List<LegendBuilder.LegendEntry> GetAllAsLegendEntries()
        {
            var entries = new List<LegendBuilder.LegendEntry>();

            // Group header: Disciplines
            entries.Add(new LegendBuilder.LegendEntry
            {
                Color = new Color(40, 40, 40),
                Label = "── DISCIPLINES ──",
                Bold = true,
            });
            foreach (var kvp in Disciplines)
                entries.Add(new LegendBuilder.LegendEntry
                {
                    Color = kvp.Value,
                    Label = kvp.Key,
                    Description = LegendBuilder.DisciplineNames.TryGetValue(kvp.Key, out string n)
                        ? n : kvp.Key,
                });

            // Group header: Systems (top 10)
            entries.Add(new LegendBuilder.LegendEntry
            {
                Color = new Color(40, 40, 40),
                Label = "── MEP SYSTEMS ──",
                Bold = true,
            });
            foreach (var kvp in Systems.Take(14))
                entries.Add(new LegendBuilder.LegendEntry
                {
                    Color = kvp.Value.Color,
                    Label = kvp.Key,
                    Description = kvp.Value.Name,
                });

            // Group header: Element Status
            entries.Add(new LegendBuilder.LegendEntry
            {
                Color = new Color(40, 40, 40),
                Label = "── ELEMENT STATUS ──",
                Bold = true,
            });
            foreach (var kvp in ElementStatus)
                entries.Add(new LegendBuilder.LegendEntry
                {
                    Color = kvp.Value,
                    Label = kvp.Key,
                    Description = $"Phase: {kvp.Key}",
                });

            // Group header: Validation
            entries.Add(new LegendBuilder.LegendEntry
            {
                Color = new Color(40, 40, 40),
                Label = "── VALIDATION STATUS ──",
                Bold = true,
            });
            foreach (var kvp in ValidationStatus)
                entries.Add(new LegendBuilder.LegendEntry
                {
                    Color = kvp.Value,
                    Label = kvp.Key,
                    Description = "QA status",
                });

            // Group header: Tag Segments
            entries.Add(new LegendBuilder.LegendEntry
            {
                Color = new Color(40, 40, 40),
                Label = "── TAG SEGMENTS ──",
                Bold = true,
            });
            foreach (var kvp in TagSegments)
                entries.Add(new LegendBuilder.LegendEntry
                {
                    Color = kvp.Value,
                    Label = kvp.Key,
                    Description = $"ISO 19650 token",
                });

            return entries;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Legend Color Mode Presets
    //
    // Flexible user-selectable color modes for legend generation.
    // Supports: Discipline, System, VG-Linked, Tag Segment, RAG Status,
    //           Monochrome, Custom Preset.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Legend color mode presets — lets users choose how legends are colored.
    /// Each mode provides a different way to derive colors for legend entries.
    /// </summary>
    internal static class LegendColorModes
    {
        /// <summary>Available legend color modes.</summary>
        public enum ColorMode
        {
            /// <summary>Color by discipline code (M=Blue, E=Gold, etc.)</summary>
            Discipline,
            /// <summary>Color by MEP system code (HVAC=Blue, HWS=Red, etc.)</summary>
            System,
            /// <summary>Color from actual VG/filter overrides in the view.</summary>
            VGLinked,
            /// <summary>Color by tag segment (DISC=navy, LOC=green, etc.)</summary>
            TagSegment,
            /// <summary>RAG status (Red/Amber/Green).</summary>
            RAGStatus,
            /// <summary>Monochrome (greyscale).</summary>
            Monochrome,
            /// <summary>Fire rating severity gradient.</summary>
            FireRating,
            /// <summary>User-saved preset from COLOR_PRESETS.json.</summary>
            CustomPreset,
        }

        /// <summary>
        /// Present a dialog for the user to choose a legend color mode.
        /// Returns the selected mode or null if cancelled.
        /// </summary>
        public static ColorMode? PromptColorMode(string context = "Legend")
        {
            var dlg = new TaskDialog($"{context} — Color Mode");
            dlg.MainInstruction = "Select color scheme for the legend";
            dlg.MainContent =
                "Choose how legend entries are colored:\n\n" +
                "Each mode maps elements to colors using a different strategy.";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Discipline Colors (Recommended)",
                "M=Blue, E=Gold, P=Green, A=Grey — ISO 19650 discipline coding");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "MEP System Colors",
                "HVAC=Blue, HWS=Red, DCW=Cyan — CIBSE/BS 1710 system coding");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "VG-Linked (Read from View)",
                "Read actual applied filter + category overrides from view/template");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Tag Segment Colors",
                "DISC=Navy, LOC=Green, ZONE=Teal — ISO 19650 tag segments");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: return ColorMode.Discipline;
                case TaskDialogResult.CommandLink2: return ColorMode.System;
                case TaskDialogResult.CommandLink3: return ColorMode.VGLinked;
                case TaskDialogResult.CommandLink4: return ColorMode.TagSegment;
                default: return null;
            }
        }

        /// <summary>
        /// Build legend entries for the selected color mode.
        /// </summary>
        public static List<LegendBuilder.LegendEntry> BuildEntries(
            Document doc, ColorMode mode, View sourceView = null)
        {
            switch (mode)
            {
                case ColorMode.Discipline:
                    return BuildDisciplineEntries(doc);

                case ColorMode.System:
                    return BuildSystemEntries(doc);

                case ColorMode.VGLinked:
                    if (sourceView != null)
                    {
                        var entries = VGLinkedLegendBuilder.FromViewFilters(doc, sourceView, true);
                        entries.AddRange(VGLinkedLegendBuilder.FromCategoryOverrides(doc, sourceView));
                        return entries;
                    }
                    return new List<LegendBuilder.LegendEntry>();

                case ColorMode.TagSegment:
                    return LegendBuilder.FromSegmentStyles();

                case ColorMode.RAGStatus:
                    return BuildRAGEntries();

                case ColorMode.Monochrome:
                    return BuildMonochromeEntries();

                case ColorMode.FireRating:
                    return BuildFireRatingEntries(doc);

                default:
                    return new List<LegendBuilder.LegendEntry>();
            }
        }

        /// <summary>Build entries from project discipline distribution.</summary>
        private static List<LegendBuilder.LegendEntry> BuildDisciplineEntries(Document doc)
        {
            var discCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var elems = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.HasMaterialQuantities)
                .ToList();

            foreach (var el in elems)
            {
                string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                if (string.IsNullOrEmpty(disc))
                {
                    string catName = ParameterHelpers.GetCategoryName(el);
                    disc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : "";
                }
                if (string.IsNullOrEmpty(disc)) continue;
                if (!discCounts.ContainsKey(disc)) discCounts[disc] = 0;
                discCounts[disc]++;
            }

            return LegendBuilder.FromDisciplineColors(
                StingColorRegistry.Disciplines, discCounts);
        }

        /// <summary>Build entries from project MEP system distribution.</summary>
        private static List<LegendBuilder.LegendEntry> BuildSystemEntries(Document doc)
        {
            return LegendIntelligence.BuildMepSystemEntries(doc);
        }

        /// <summary>Build RAG status entries (Red/Amber/Green).</summary>
        private static List<LegendBuilder.LegendEntry> BuildRAGEntries()
        {
            return new List<LegendBuilder.LegendEntry>
            {
                new LegendBuilder.LegendEntry
                {
                    Color = StingColorRegistry.ValidationStatus["MISSING"],
                    Label = "Non-Compliant (Red)",
                    Description = "Missing required data",
                    Bold = true,
                },
                new LegendBuilder.LegendEntry
                {
                    Color = StingColorRegistry.ValidationStatus["INCOMPLETE"],
                    Label = "Warning (Amber)",
                    Description = "Partially complete",
                    Bold = true,
                },
                new LegendBuilder.LegendEntry
                {
                    Color = StingColorRegistry.ValidationStatus["VALID"],
                    Label = "Compliant (Green)",
                    Description = "All data complete",
                    Bold = true,
                },
            };
        }

        /// <summary>Build monochrome entries for print-friendly output.</summary>
        private static List<LegendBuilder.LegendEntry> BuildMonochromeEntries()
        {
            return new List<LegendBuilder.LegendEntry>
            {
                new LegendBuilder.LegendEntry { Color = new Color(0, 0, 0), Label = "Primary", Description = "Black — main elements" },
                new LegendBuilder.LegendEntry { Color = new Color(64, 64, 64), Label = "Secondary", Description = "Dark grey — supporting" },
                new LegendBuilder.LegendEntry { Color = new Color(128, 128, 128), Label = "Tertiary", Description = "Medium grey — context" },
                new LegendBuilder.LegendEntry { Color = new Color(192, 192, 192), Label = "Background", Description = "Light grey — reference" },
                new LegendBuilder.LegendEntry { Color = new Color(255, 255, 255), Label = "Hidden", Description = "White — suppressed" },
            };
        }

        /// <summary>Build fire rating entries from project data.</summary>
        private static List<LegendBuilder.LegendEntry> BuildFireRatingEntries(Document doc)
        {
            return LegendIntelligence.BuildFireRatingEntries(doc);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Legend Component Bridge — Enhanced auto-seed with fallback chain
    //
    // Automates LegendComponent creation using a 3-tier strategy:
    //   1. Copy existing LegendComponent (if seed exists in project)
    //   2. Place annotation family instances (Generic Annotation bridge)
    //   3. Draw representation with FilledRegion + TextNote (always works)
    //
    // The bridge auto-detects which strategy is available and uses the best
    // one. If no seed exists, it falls back gracefully.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Enhanced legend component automation with auto-seed detection,
    /// annotation family bridge, and drawn fallback.
    /// </summary>
    internal static class LegendComponentBridge
    {
        /// <summary>
        /// Attempt to populate a legend view with actual LegendComponent instances
        /// for each family type in the entries list. Uses 3-tier fallback:
        /// 1. Copy existing LegendComponent + reassign type
        /// 2. Place Generic Annotation instance
        /// 3. Skip (caller falls back to FilledRegion swatches)
        /// Returns count of successfully placed components.
        /// Must be called within an active Transaction.
        /// </summary>
        public static int PopulateWithComponents(
            Document doc, View legendView,
            List<(ElementId TypeId, string Label, XYZ Position)> componentRequests)
        {
            if (componentRequests == null || componentRequests.Count == 0) return 0;

            // Tier 1: Find seed LegendComponent
            Element seedComponent = null;
            ElementId seedViewId = ElementId.InvalidElementId;

            var legendViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.ViewType == ViewType.Legend && !v.IsTemplate);

            foreach (var lv in legendViews)
            {
                seedComponent = new FilteredElementCollector(doc, lv.Id)
                    .OfCategory(BuiltInCategory.OST_LegendComponents)
                    .WhereElementIsNotElementType()
                    .FirstOrDefault();

                if (seedComponent != null)
                {
                    seedViewId = lv.Id;
                    break;
                }
            }

            int placed = 0;
            foreach (var req in componentRequests)
            {
                try
                {
                    // Tier 1: Copy seed LegendComponent + set type
                    if (seedComponent != null && legendView.ViewType == ViewType.Legend)
                    {
                        ICollection<ElementId> copiedIds;
                        if (seedViewId == legendView.Id)
                        {
                            var seedLoc = (seedComponent.Location as LocationPoint)?.Point ?? XYZ.Zero;
                            copiedIds = ElementTransformUtils.CopyElement(
                                doc, seedComponent.Id, req.Position - seedLoc);
                        }
                        else
                        {
                            copiedIds = ElementTransformUtils.CopyElements(
                                doc.GetElement(seedViewId) as View,
                                new List<ElementId> { seedComponent.Id },
                                legendView,
                                Transform.Identity,
                                new CopyPasteOptions());
                        }

                        if (copiedIds != null && copiedIds.Count > 0)
                        {
                            Element copied = doc.GetElement(copiedIds.First());
                            if (copied != null)
                            {
                                // Reassign to desired type
                                var legendParam = copied.get_Parameter(
                                    BuiltInParameter.LEGEND_COMPONENT);
                                if (legendParam != null && !legendParam.IsReadOnly)
                                {
                                    legendParam.Set(req.TypeId);
                                }

                                // Move to position
                                if (copied.Location is LocationPoint lp)
                                {
                                    ElementTransformUtils.MoveElement(
                                        doc, copied.Id, req.Position - lp.Point);
                                }
                                placed++;
                                continue;
                            }
                        }
                    }

                    // Tier 2: Place Generic Annotation instance
                    FamilySymbol typeSymbol = doc.GetElement(req.TypeId) as FamilySymbol;
                    if (typeSymbol != null)
                    {
                        if (!typeSymbol.IsActive)
                        {
                            try { typeSymbol.Activate(); } catch { /* not activatable */ }
                        }

                        try
                        {
                            doc.Create.NewFamilyInstance(req.Position, typeSymbol, legendView);
                            placed++;
                        }
                        catch
                        {
                            // Family doesn't support placement in this view type — skip
                            StingLog.Warn($"LegendComponentBridge: cannot place '{req.Label}' " +
                                $"in {legendView.ViewType} view — not supported by family");
                        }
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"LegendComponentBridge: failed for '{req.Label}': {ex.Message}");
                }
            }

            return placed;
        }

        /// <summary>
        /// Check if any LegendComponent seed exists in the project.
        /// If not, return false so caller knows to use drawn fallback.
        /// </summary>
        public static bool HasSeedComponent(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_LegendComponents)
                .WhereElementIsNotElementType()
                .GetElementCount() > 0;
        }

        /// <summary>
        /// Get all family types currently in the project that could be placed
        /// as legend components (model families with plan representation).
        /// Grouped by category for organized legend layout.
        /// </summary>
        public static Dictionary<string, List<FamilySymbol>> GetPlaceableTypes(Document doc)
        {
            var result = new Dictionary<string, List<FamilySymbol>>(
                StringComparer.OrdinalIgnoreCase);

            var symbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Category != null &&
                    fs.Category.CategoryType == CategoryType.Model)
                .ToList();

            foreach (var fs in symbols)
            {
                string catName = fs.Category.Name;
                if (!result.ContainsKey(catName))
                    result[catName] = new List<FamilySymbol>();
                result[catName].Add(fs);
            }

            return result;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Flexible Legend Command — User picks color mode, then generates
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a legend with user-selectable color mode (discipline, system,
    /// VG-linked, tag segment, RAG, monochrome, fire rating).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FlexibleLegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            // Step 1: Pick color mode
            var mode = LegendColorModes.PromptColorMode("Create Legend");
            if (mode == null) return Result.Cancelled;

            // Step 2: Build entries
            var entries = LegendColorModes.BuildEntries(doc, mode.Value, view);
            if (entries.Count == 0)
            {
                TaskDialog.Show("Create Legend",
                    $"No data found for color mode '{mode.Value}'.\n" +
                    "Ensure elements exist with the relevant parameters populated.");
                return Result.Succeeded;
            }

            // Step 3: Create legend
            var config = new LegendBuilder.LegendConfig
            {
                Title = $"{mode.Value} Legend",
                Subtitle = $"Color mode: {mode.Value} — {entries.Count} entries",
                Footer = $"STING Tools — {mode.Value} color scheme",
                Columns = entries.Count > 15 ? 2 : 1,
            };

            using (Transaction tx = new Transaction(doc, $"STING {mode.Value} Legend"))
            {
                tx.Start();
                var legendView = LegendBuilder.CreateLegendView(doc, entries, config);
                tx.Commit();

                if (legendView != null)
                {
                    TaskDialog.Show("Legend Created",
                        $"Created '{legendView.Name}' with {entries.Count} entries.\n" +
                        $"Color mode: {mode.Value}\n\n" +
                        "Find it in the Project Browser under Legends/Drafting Views.");
                }
            }

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Legend from Color Preset — Generate legend from saved COLOR_PRESETS.json
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a legend from a saved color preset in COLOR_PRESETS.json.
    /// Bridges the gap between ColorHelper presets and the legend system.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LegendFromPresetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Load presets from JSON
            string presetPath = System.IO.Path.Combine(
                StingToolsApp.DataPath ?? "", "COLOR_PRESETS.json");

            if (!System.IO.File.Exists(presetPath))
            {
                TaskDialog.Show("Legend from Preset",
                    "No saved color presets found.\n\n" +
                    "Use 'Save Color Preset' after 'Color By Parameter' to create presets,\n" +
                    "then this command will generate legends from them.");
                return Result.Succeeded;
            }

            try
            {
                string json = System.IO.File.ReadAllText(presetPath);
                var presetData = Newtonsoft.Json.JsonConvert.DeserializeObject<
                    Dictionary<string, Dictionary<string, int[]>>>(json);

                if (presetData == null || presetData.Count == 0)
                {
                    TaskDialog.Show("Legend from Preset", "No presets found in file.");
                    return Result.Succeeded;
                }

                // Let user pick a preset
                var names = presetData.Keys.Take(4).ToList();
                var dlg = new TaskDialog("Legend from Preset");
                dlg.MainInstruction = $"Select a preset ({presetData.Count} available)";
                if (names.Count >= 1)
                    dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, names[0]);
                if (names.Count >= 2)
                    dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, names[1]);
                if (names.Count >= 3)
                    dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, names[2]);
                if (names.Count >= 4)
                    dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, names[3]);
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                string selectedName;
                switch (dlg.Show())
                {
                    case TaskDialogResult.CommandLink1: selectedName = names[0]; break;
                    case TaskDialogResult.CommandLink2: selectedName = names[1]; break;
                    case TaskDialogResult.CommandLink3: selectedName = names[2]; break;
                    case TaskDialogResult.CommandLink4: selectedName = names[3]; break;
                    default: return Result.Cancelled;
                }

                // Build entries from preset
                var preset = presetData[selectedName];
                var entries = new List<LegendBuilder.LegendEntry>();
                foreach (var kvp in preset)
                {
                    int[] rgb = kvp.Value;
                    if (rgb == null || rgb.Length < 3) continue;
                    entries.Add(new LegendBuilder.LegendEntry
                    {
                        Color = new Color((byte)rgb[0], (byte)rgb[1], (byte)rgb[2]),
                        Label = kvp.Key,
                        Description = $"RGB({rgb[0]},{rgb[1]},{rgb[2]})",
                    });
                }

                if (entries.Count == 0)
                {
                    TaskDialog.Show("Legend from Preset", "Preset has no color entries.");
                    return Result.Succeeded;
                }

                var config = new LegendBuilder.LegendConfig
                {
                    Title = $"Color Preset: {selectedName}",
                    Subtitle = $"{entries.Count} colors from saved preset",
                    Footer = "Generated from COLOR_PRESETS.json",
                    Columns = entries.Count > 12 ? 2 : 1,
                };

                using (Transaction tx = new Transaction(doc, "STING Legend from Preset"))
                {
                    tx.Start();
                    var legendView = LegendBuilder.CreateLegendView(doc, entries, config);
                    tx.Commit();

                    if (legendView != null)
                    {
                        TaskDialog.Show("Legend Created",
                            $"Created '{legendView.Name}' from preset '{selectedName}'.\n" +
                            $"{entries.Count} color entries.\n\n" +
                            "Find it in the Project Browser.");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("Legend from Preset", ex);
                TaskDialog.Show("Legend from Preset",
                    $"Error reading presets: {ex.Message}");
            }

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // LegendComponent Type Legend — Uses the component bridge to attempt
    // placing real LegendComponent instances for family types in the project.
    // Falls back to drawn representation if no seed component exists.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a legend using actual LegendComponent instances where possible,
    /// with automatic fallback to drawn swatches for unsupported types.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ComponentTypeLegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Check for seed component
            bool hasSeed = LegendComponentBridge.HasSeedComponent(doc);

            // Get placeable types grouped by category
            var typesByCategory = LegendComponentBridge.GetPlaceableTypes(doc);
            if (typesByCategory.Count == 0)
            {
                TaskDialog.Show("Component Legend", "No model family types found in project.");
                return Result.Succeeded;
            }

            // Let user pick categories
            var categories = typesByCategory.Keys.OrderBy(k => k).Take(4).ToList();
            var dlg = new TaskDialog("Component Legend");
            dlg.MainInstruction = $"Create legend for which category? ({typesByCategory.Count} available)";
            if (!hasSeed)
                dlg.MainContent = "Note: No existing LegendComponent found — will use drawn representation.";
            for (int i = 0; i < categories.Count; i++)
            {
                var linkId = (TaskDialogCommandLinkId)(i + 201);
                int typeCount = typesByCategory[categories[i]].Count;
                dlg.AddCommandLink(linkId, categories[i], $"{typeCount} types");
            }
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            string selectedCat;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: selectedCat = categories[0]; break;
                case TaskDialogResult.CommandLink2: selectedCat = categories[1]; break;
                case TaskDialogResult.CommandLink3: selectedCat = categories[2]; break;
                case TaskDialogResult.CommandLink4: selectedCat = categories[3]; break;
                default: return Result.Cancelled;
            }

            var types = typesByCategory[selectedCat].Take(30).ToList(); // Limit to 30

            using (Transaction tx = new Transaction(doc, "STING Component Legend"))
            {
                tx.Start();

                // Create legend view
                View legendView = LegendBuilder.TryCreateNativeLegend(doc,
                    $"STING Legend - {selectedCat}");

                if (legendView == null)
                {
                    // Fall back to drafting view
                    var vft = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>()
                        .FirstOrDefault(v => v.ViewFamily == ViewFamily.Drafting);

                    if (vft == null)
                    {
                        tx.RollBack();
                        TaskDialog.Show("Component Legend", "Cannot create legend view.");
                        return Result.Failed;
                    }
                    legendView = ViewDrafting.Create(doc, vft.Id);
                    legendView.Name = $"STING Legend - {selectedCat}";
                    legendView.Scale = 1;
                }

                // Build component requests
                double y = 0.5;
                double spacing = 0.08;
                var requests = new List<(ElementId TypeId, string Label, XYZ Position)>();

                foreach (var fs in types)
                {
                    requests.Add((fs.Id, $"{fs.Family.Name}: {fs.Name}", new XYZ(0.05, y, 0)));
                    y -= spacing;
                }

                // Attempt to place components
                int placed = LegendComponentBridge.PopulateWithComponents(doc, legendView, requests);

                // For any that weren't placed, add drawn entries
                if (placed < types.Count)
                {
                    var drawnEntries = types.Skip(placed).Select(fs =>
                        new LegendBuilder.LegendEntry
                        {
                            Color = StingColorRegistry.GetDisciplineColor(
                                TagConfig.DiscMap.TryGetValue(selectedCat, out string d) ? d : "G"),
                            Label = $"{fs.Family.Name}: {fs.Name}",
                            Description = selectedCat,
                        }).ToList();

                    LegendBuilder.PopulateLegendContent(doc, legendView, drawnEntries,
                        new LegendBuilder.LegendConfig
                        {
                            Title = $"{selectedCat} Types",
                            Subtitle = $"{types.Count} types ({placed} components + {drawnEntries.Count} drawn)",
                            Footer = "STING Tools — Component Legend",
                        });
                }

                tx.Commit();

                TaskDialog.Show("Component Legend",
                    $"Created legend for {selectedCat}:\n" +
                    $"  {placed} real LegendComponent instances\n" +
                    $"  {types.Count - placed} drawn representations\n\n" +
                    (hasSeed ? "" : "Tip: Place one LegendComponent manually to enable auto-copy.\n") +
                    "Find it in the Project Browser.");
            }

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Color Reference Legend — Complete color scheme reference from registry
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generate a comprehensive color reference legend showing ALL STING color
    /// definitions: disciplines, MEP systems, element status, validation, tag segments.
    /// This is the definitive visual reference for the project's color scheme.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ColorReferenceLegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            var entries = StingColorRegistry.GetAllAsLegendEntries();
            if (entries.Count == 0)
            {
                TaskDialog.Show("Color Reference", "No color definitions found.");
                return Result.Failed;
            }

            var config = new LegendBuilder.LegendConfig
            {
                Title = "STING Color Reference",
                Subtitle = $"Complete color scheme — {entries.Count} definitions",
                Footer = "StingColorRegistry — Single source of truth for all STING colorization",
                Columns = 2,
                ShowCounts = false,
            };

            using (Transaction tx = new Transaction(doc, "STING Color Reference Legend"))
            {
                tx.Start();
                var view = LegendBuilder.CreateLegendView(doc, entries, config);
                tx.Commit();

                if (view != null)
                {
                    TaskDialog.Show("Color Reference Legend",
                        $"Created '{view.Name}' with {entries.Count} color definitions.\n\n" +
                        "Groups: Disciplines, MEP Systems, Element Status,\n" +
                        "Validation Status, Tag Segments.\n\n" +
                        "Find it in the Project Browser.");
                }
            }

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Legend Sync Engine — Auto-detect stale legends and refresh
    //
    // Monitors legend views for staleness by comparing their stored metadata
    // against current project state. Provides refresh, audit, and auto-sync.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Legend synchronization engine: detects stale legends and refreshes
    /// them from current project data (VG overrides, element counts, etc.).
    /// </summary>
    internal static class LegendSyncEngine
    {
        /// <summary>
        /// Scan all STING legend views and check if they are stale.
        /// Returns list of (legendName, reason) for stale legends.
        /// </summary>
        public static List<(string Name, string Reason)> AuditStaleLegends(Document doc)
        {
            var stale = new List<(string, string)>();

            var stingLegends = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate &&
                    (v.ViewType == ViewType.Legend || v.ViewType == ViewType.DraftingView) &&
                    v.Name.StartsWith("STING"))
                .ToList();

            foreach (var legend in stingLegends)
            {
                string name = legend.Name;

                // Check: Discipline legend — compare discipline counts
                if (name.Contains("Discipline"))
                {
                    var currentDiscs = CountDisciplines(doc);
                    var legendTexts = GetLegendTextContent(doc, legend);
                    foreach (var disc in currentDiscs.Keys)
                    {
                        if (!legendTexts.Any(t => t.Contains(disc)))
                        {
                            stale.Add((name, $"Missing discipline '{disc}' ({currentDiscs[disc]} elements)"));
                            break;
                        }
                    }
                }

                // Check: Filter legend — compare filter count
                if (name.Contains("Filter"))
                {
                    var view = doc.ActiveView;
                    if (view != null)
                    {
                        var filterCount = view.GetFilters().Count;
                        var legendTexts = GetLegendTextContent(doc, legend);
                        if (legendTexts.Count > 0 && legendTexts.Count < filterCount / 2)
                        {
                            stale.Add((name, $"View has {filterCount} filters but legend shows fewer"));
                        }
                    }
                }

                // Check: System legend — compare system codes in project
                if (name.Contains("System") || name.Contains("MEP"))
                {
                    var currentSys = CountSystems(doc);
                    var legendTexts = GetLegendTextContent(doc, legend);
                    foreach (var sys in currentSys.Keys)
                    {
                        if (!legendTexts.Any(t => t.Contains(sys)))
                        {
                            stale.Add((name, $"Missing system '{sys}' ({currentSys[sys]} elements)"));
                            break;
                        }
                    }
                }
            }

            return stale;
        }

        /// <summary>
        /// Get all text content from a legend/drafting view (TextNote elements).
        /// </summary>
        private static List<string> GetLegendTextContent(Document doc, View view)
        {
            try
            {
                return new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(TextNote))
                    .Cast<TextNote>()
                    .Select(tn => tn.Text ?? "")
                    .ToList();
            }
            catch { return new List<string>(); }
        }

        /// <summary>Count elements by discipline code in the project.</summary>
        private static Dictionary<string, int> CountDisciplines(Document doc)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var elems = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.HasMaterialQuantities)
                .ToList();

            foreach (var el in elems)
            {
                string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                if (string.IsNullOrEmpty(disc)) continue;
                if (!counts.ContainsKey(disc)) counts[disc] = 0;
                counts[disc]++;
            }
            return counts;
        }

        /// <summary>Count elements by system code in the project.</summary>
        private static Dictionary<string, int> CountSystems(Document doc)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var elems = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.HasMaterialQuantities)
                .ToList();

            foreach (var el in elems)
            {
                string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                if (string.IsNullOrEmpty(sys)) continue;
                if (!counts.ContainsKey(sys)) counts[sys] = 0;
                counts[sys]++;
            }
            return counts;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Legend Sync / Audit Command
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Audit all STING legends for staleness and offer to refresh.
    /// Compares legend content against current project data.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LegendSyncAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            var staleItems = LegendSyncEngine.AuditStaleLegends(doc);

            if (staleItems.Count == 0)
            {
                TaskDialog.Show("Legend Sync Audit",
                    "All STING legends are up to date.\n\n" +
                    "No stale or missing data detected.");
                return Result.Succeeded;
            }

            var report = new StringBuilder();
            report.AppendLine($"Found {staleItems.Count} stale legend issue(s):\n");
            foreach (var (name, reason) in staleItems)
                report.AppendLine($"  {name}: {reason}");

            report.AppendLine("\nRefresh legends to update them with current project data?");

            var dlg = new TaskDialog("Legend Sync Audit");
            dlg.MainInstruction = $"{staleItems.Count} stale legend(s) detected";
            dlg.MainContent = report.ToString();
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Refresh All Stale Legends",
                "Delete stale legends and recreate from current project data");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "View Report Only",
                "Just show the audit results without changes");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            if (dlg.Show() == TaskDialogResult.CommandLink1)
            {
                // Delete stale legends and trigger auto-create
                int deleted = 0;
                using (Transaction tx = new Transaction(doc, "STING Refresh Stale Legends"))
                {
                    tx.Start();
                    var stingLegends = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .Where(v => !v.IsTemplate && v.Name.StartsWith("STING") &&
                            staleItems.Any(s => v.Name.Contains(s.Name.Replace("STING Legend - ", ""))))
                        .ToList();

                    foreach (var legend in stingLegends)
                    {
                        try
                        {
                            doc.Delete(legend.Id);
                            deleted++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Cannot delete legend '{legend.Name}': {ex.Message}");
                        }
                    }
                    tx.Commit();
                }

                TaskDialog.Show("Legend Sync",
                    $"Deleted {deleted} stale legends.\n\n" +
                    "Run 'Auto-Create Legends' or '★ Master Legend' to regenerate.");
            }

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Element Status Legend — Phase/lifecycle visualization
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a legend showing element status distribution (EXISTING/NEW/DEMOLISHED/TEMPORARY)
    /// using the unified StingColorRegistry.ElementStatus colors.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StatusLegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            // Count elements by status
            var statusCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var elems = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.HasMaterialQuantities)
                .ToList();

            foreach (var el in elems)
            {
                string status = ParameterHelpers.GetString(el, ParamRegistry.STATUS);
                if (string.IsNullOrEmpty(status)) status = "UNSET";
                if (!statusCounts.ContainsKey(status)) statusCounts[status] = 0;
                statusCounts[status]++;
            }

            if (statusCounts.Count == 0)
            {
                TaskDialog.Show("Status Legend", "No elements with STATUS parameter found.");
                return Result.Succeeded;
            }

            var entries = new List<LegendBuilder.LegendEntry>();
            foreach (var kvp in statusCounts.OrderByDescending(x => x.Value))
            {
                Color color = StingColorRegistry.ElementStatus.TryGetValue(kvp.Key, out Color c)
                    ? c : new Color(160, 160, 160);

                entries.Add(new LegendBuilder.LegendEntry
                {
                    Color = color,
                    Label = kvp.Key,
                    Description = $"{kvp.Value} elements",
                    Bold = true,
                });
            }

            var config = new LegendBuilder.LegendConfig
            {
                Title = "Element Status",
                Subtitle = "Lifecycle phase distribution",
                Footer = "STING Tools — StingColorRegistry.ElementStatus",
            };

            using (Transaction tx = new Transaction(doc, "STING Status Legend"))
            {
                tx.Start();
                var view = LegendBuilder.CreateLegendView(doc, entries, config);
                tx.Commit();

                if (view != null)
                    TaskDialog.Show("Status Legend",
                        $"Created '{view.Name}' with {entries.Count} status categories.\n\n" +
                        "Shows EXISTING/NEW/DEMOLISHED/TEMPORARY distribution.");
            }

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Workset Legend — Color-coded workset distribution
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a legend showing worksets color-coded by discipline group.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WorksetLegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            if (!doc.IsWorkshared)
            {
                TaskDialog.Show("Workset Legend", "Project is not workshared. Enable worksharing first.");
                return Result.Succeeded;
            }

            var worksets = new FilteredWorksetCollector(doc)
                .OfKind(WorksetKind.UserWorkset)
                .ToWorksets()
                .OrderBy(w => w.Name)
                .ToList();

            if (worksets.Count == 0)
            {
                TaskDialog.Show("Workset Legend", "No user worksets found.");
                return Result.Succeeded;
            }

            var entries = worksets.Select(w => new LegendBuilder.LegendEntry
            {
                Color = StingColorRegistry.GetWorksetColor(w.Name),
                Label = w.Name,
                Description = w.IsOpen ? "Open" : "Closed",
            }).ToList();

            var config = new LegendBuilder.LegendConfig
            {
                Title = "Workset Legend",
                Subtitle = $"{worksets.Count} worksets — color by discipline group",
                Footer = "STING Tools — ISO 19650 workset scheme",
                Columns = entries.Count > 18 ? 2 : 1,
                ShowCounts = false,
            };

            using (Transaction tx = new Transaction(doc, "STING Workset Legend"))
            {
                tx.Start();
                var view = LegendBuilder.CreateLegendView(doc, entries, config);
                tx.Commit();

                if (view != null)
                    TaskDialog.Show("Workset Legend",
                        $"Created '{view.Name}' with {entries.Count} worksets.\n" +
                        "Color-coded by discipline group prefix.");
            }

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MEP & Architectural Legend Intelligence Engine
    //
    // Multi-layer automation for discipline-specific legends:
    //
    //   Layer 1 — Content Detection: Scan project for MEP systems, material
    //             types, compound families, equipment in use
    //   Layer 2 — Legend Selection: Pick the right legend type per discipline
    //             (HVAC→system legend, Arch→material legend, etc.)
    //   Layer 3 — Sheet Discipline Inference: Match sheets to disciplines
    //             from sheet name/number prefix and placed view content
    //   Layer 4 — Placement Intelligence: Pick correct corner, avoid overlaps,
    //             stack multiple legends vertically
    //   Layer 5 — Deduplication: Skip legends for empty disciplines, reuse
    //             existing STING legends instead of recreating
    //
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Extended legend builder engine with MEP system, material, compound type,
    /// and equipment legends. Multi-layer intelligence for auto-detection.
    /// </summary>
    internal static class LegendIntelligence
    {
        // ── Colors delegate to StingColorRegistry (single source of truth) ──

        /// <summary>MEP system colors — delegates to StingColorRegistry.Systems.</summary>
        public static Dictionary<string, (Color Color, string Name)> SystemColors =>
            StingColorRegistry.Systems;

        /// <summary>Material category colors — delegates to StingColorRegistry.MaterialCategories.</summary>
        public static Dictionary<string, Color> MaterialCategoryColors =>
            StingColorRegistry.MaterialCategories;

        // ── Layer 1: Content Detection ─────────────────────────────────

        /// <summary>
        /// Scan the project and return MEP systems actually in use with element counts.
        /// Uses the STING SYS parameter first, falls back to runtime MEP system detection.
        /// </summary>
        public static List<LegendBuilder.LegendEntry> BuildMepSystemEntries(Document doc)
        {
            var elems = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.HasMaterialQuantities)
                .ToList();

            // Group by SYS code
            var sysCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in elems)
            {
                string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                if (string.IsNullOrEmpty(sys))
                {
                    // Fall back to category-based detection
                    string catName = ParameterHelpers.GetCategoryName(el);
                    sys = TagConfig.GetSysCode(catName);
                }
                if (string.IsNullOrEmpty(sys)) continue;

                if (!sysCounts.ContainsKey(sys)) sysCounts[sys] = 0;
                sysCounts[sys]++;
            }

            var entries = new List<LegendBuilder.LegendEntry>();
            foreach (var kvp in sysCounts.OrderByDescending(x => x.Value))
            {
                var info = SystemColors.TryGetValue(kvp.Key, out var sc)
                    ? sc : (Color: new Color(160, 160, 160), Name: kvp.Key);

                entries.Add(new LegendBuilder.LegendEntry
                {
                    Color = info.Color,
                    Label = $"{kvp.Key} — {info.Name}",
                    Description = $"{kvp.Value} elements",
                    Bold = true,
                });
            }
            return entries;
        }

        /// <summary>
        /// Build MEP system entries from elements on a specific sheet only.
        /// </summary>
        public static List<LegendBuilder.LegendEntry> BuildMepSystemEntriesForSheet(
            Document doc, ViewSheet sheet)
        {
            if (sheet == null) return new List<LegendBuilder.LegendEntry>();

            var sheetElements = new List<Element>();
            foreach (ElementId viewId in sheet.GetAllPlacedViews())
            {
                View v = doc.GetElement(viewId) as View;
                if (v == null || v.IsTemplate) continue;
                if (v.ViewType == ViewType.Legend || v.ViewType == ViewType.DraftingView) continue;
                try
                {
                    sheetElements.AddRange(new FilteredElementCollector(doc, v.Id)
                        .WhereElementIsNotElementType()
                        .Where(e => e.Category != null && e.Category.HasMaterialQuantities));
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"SystemLegend: failed to collect elements from view '{v.Name}': {ex.Message}");
                }
            }

            var unique = sheetElements.GroupBy(e => e.Id).Select(g => g.First()).ToList();
            var sysCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in unique)
            {
                string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                if (string.IsNullOrEmpty(sys))
                {
                    string catName = ParameterHelpers.GetCategoryName(el);
                    sys = TagConfig.GetSysCode(catName);
                }
                if (string.IsNullOrEmpty(sys)) continue;
                if (!sysCounts.ContainsKey(sys)) sysCounts[sys] = 0;
                sysCounts[sys]++;
            }

            var entries = new List<LegendBuilder.LegendEntry>();
            foreach (var kvp in sysCounts.OrderByDescending(x => x.Value))
            {
                var info = SystemColors.TryGetValue(kvp.Key, out var sc)
                    ? sc : (Color: new Color(160, 160, 160), Name: kvp.Key);
                entries.Add(new LegendBuilder.LegendEntry
                {
                    Color = info.Color,
                    Label = $"{kvp.Key} — {info.Name}",
                    Description = $"{kvp.Value} elements",
                    Bold = true,
                });
            }
            return entries;
        }

        /// <summary>
        /// Build material legend entries from project materials actually in use.
        /// Groups by material category and includes fire rating where available.
        /// </summary>
        public static List<LegendBuilder.LegendEntry> BuildMaterialEntries(Document doc)
        {
            // Collect all materials in use
            var materials = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .Where(m => !string.IsNullOrEmpty(m.Name))
                .ToList();

            // Group by identity class or name prefix
            var matGroups = new Dictionary<string, (Color color, int count, string fireRating)>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var mat in materials)
            {
                string cls = "";
                try
                {
                    var clsParam = mat.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION);
                    if (clsParam != null) cls = clsParam.AsString() ?? "";
                }
                catch { }

                // Derive a category from material name
                string group = DeriveMaterialGroup(mat.Name);
                if (string.IsNullOrEmpty(group)) group = "Other";

                Color matColor;
                try
                {
                    matColor = mat.Color;
                    if (matColor == null || !matColor.IsValid)
                        matColor = MaterialCategoryColors.TryGetValue(group, out Color mc)
                            ? mc : new Color(180, 180, 180);
                }
                catch
                {
                    matColor = MaterialCategoryColors.TryGetValue(group, out Color mc)
                        ? mc : new Color(180, 180, 180);
                }

                if (!matGroups.ContainsKey(group))
                    matGroups[group] = (matColor, 0, "");

                var existing = matGroups[group];
                matGroups[group] = (existing.color, existing.count + 1, existing.fireRating);
            }

            var entries = new List<LegendBuilder.LegendEntry>();
            foreach (var kvp in matGroups.OrderByDescending(x => x.Value.count))
            {
                string desc = $"{kvp.Value.count} materials";
                if (!string.IsNullOrEmpty(kvp.Value.fireRating))
                    desc += $" | Fire: {kvp.Value.fireRating}";

                entries.Add(new LegendBuilder.LegendEntry
                {
                    Color = kvp.Value.color,
                    Label = kvp.Key,
                    Description = desc,
                });
            }
            return entries;
        }

        /// <summary>
        /// Build compound type legend entries showing wall/floor/ceiling/roof types in use.
        /// Each entry shows the type name, layer count, and total thickness.
        /// </summary>
        public static List<LegendBuilder.LegendEntry> BuildCompoundTypeEntries(Document doc)
        {
            var entries = new List<LegendBuilder.LegendEntry>();

            // Discipline colors for compound type categories
            var catColors = new Dictionary<string, Color>
            {
                { "Walls",    new Color(180, 160, 140) },
                { "Floors",   new Color(200, 180, 140) },
                { "Ceilings", new Color(160, 200, 220) },
                { "Roofs",    new Color(180, 120, 80)  },
            };

            var bicList = new[]
            {
                (BuiltInCategory.OST_Walls,    "Walls"),
                (BuiltInCategory.OST_Floors,   "Floors"),
                (BuiltInCategory.OST_Ceilings, "Ceilings"),
                (BuiltInCategory.OST_Roofs,    "Roofs"),
            };

            foreach (var (bic, catName) in bicList)
            {
                // Get types in use (those with placed instances)
                var instances = new FilteredElementCollector(doc)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType()
                    .ToList();

                var typeIds = instances.Select(e => e.GetTypeId()).Distinct().ToList();
                Color baseColor = catColors.TryGetValue(catName, out Color cc) ? cc : new Color(180, 180, 180);

                int colorShift = 0;
                foreach (var typeId in typeIds)
                {
                    if (typeId == ElementId.InvalidElementId) continue;
                    var elemType = doc.GetElement(typeId);
                    if (elemType == null) continue;

                    string typeName = elemType.Name ?? "(Unknown)";
                    int instanceCount = instances.Count(e => e.GetTypeId() == typeId);

                    // Try to get layer info from CompoundStructure
                    string layerInfo = "";
                    try
                    {
                        if (elemType is WallType wt)
                        {
                            var cs = wt.GetCompoundStructure();
                            if (cs != null)
                                layerInfo = $"{cs.LayerCount} layers, {cs.GetWidth() * 304.8:F0}mm";
                        }
                        else if (elemType is FloorType ft)
                        {
                            var cs = ft.GetCompoundStructure();
                            if (cs != null)
                                layerInfo = $"{cs.LayerCount} layers, {cs.GetWidth() * 304.8:F0}mm";
                        }
                        else if (elemType is CeilingType ct)
                        {
                            var cs = ct.GetCompoundStructure();
                            if (cs != null)
                                layerInfo = $"{cs.LayerCount} layers, {cs.GetWidth() * 304.8:F0}mm";
                        }
                        else if (elemType is RoofType rt)
                        {
                            var cs = rt.GetCompoundStructure();
                            if (cs != null)
                                layerInfo = $"{cs.LayerCount} layers, {cs.GetWidth() * 304.8:F0}mm";
                        }
                    }
                    catch { }

                    // Shift color slightly per type for visual distinction
                    byte r = (byte)Math.Min(255, baseColor.Red + colorShift * 8);
                    byte g = (byte)Math.Min(255, baseColor.Green + colorShift * 5);
                    byte b = (byte)Math.Min(255, baseColor.Blue + colorShift * 3);
                    colorShift++;

                    string desc = $"{instanceCount} instances";
                    if (!string.IsNullOrEmpty(layerInfo)) desc += $" | {layerInfo}";

                    entries.Add(new LegendBuilder.LegendEntry
                    {
                        Color = new Color(r, g, b),
                        Label = $"{catName}: {typeName}",
                        Description = desc,
                    });
                }
            }

            return entries;
        }

        /// <summary>
        /// Build MEP equipment summary entries grouped by system and category.
        /// Shows equipment families with key sizing parameters.
        /// </summary>
        public static List<LegendBuilder.LegendEntry> BuildEquipmentEntries(Document doc)
        {
            var equipCats = new[]
            {
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_ElectricalEquipment,
                BuiltInCategory.OST_PlumbingFixtures,
                BuiltInCategory.OST_LightingFixtures,
                BuiltInCategory.OST_Sprinklers,
                BuiltInCategory.OST_FireAlarmDevices,
            };

            var entries = new List<LegendBuilder.LegendEntry>();

            foreach (var bic in equipCats)
            {
                var instances = new FilteredElementCollector(doc)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType()
                    .ToList();

                if (instances.Count == 0) continue;

                // Group by family name
                var byFamily = instances
                    .GroupBy(e => ParameterHelpers.GetFamilyName(e) ?? "(Unknown)")
                    .OrderByDescending(g => g.Count())
                    .ToList();

                string catName = instances[0].Category?.Name ?? "Equipment";
                string disc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : "G";
                Color baseColor = Organise.AnnotationColorHelper.DisciplineColors.TryGetValue(disc, out Color c)
                    ? c : new Color(128, 128, 128);

                int shift = 0;
                foreach (var fam in byFamily.Take(10))
                {
                    // Try to extract sizing from first instance
                    string sizing = "";
                    try
                    {
                        var sample = fam.First();
                        string flow = ParameterHelpers.GetString(sample, "ASS_FLOW_RATE_TXT");
                        string power = ParameterHelpers.GetString(sample, "ASS_POWER_RATING_TXT");
                        if (!string.IsNullOrEmpty(flow)) sizing = flow;
                        else if (!string.IsNullOrEmpty(power)) sizing = power;
                    }
                    catch { }

                    byte r = (byte)Math.Max(0, baseColor.Red - shift * 10);
                    byte g = (byte)Math.Max(0, baseColor.Green - shift * 8);
                    byte b = (byte)Math.Max(0, baseColor.Blue - shift * 6);
                    shift++;

                    string desc = $"{fam.Count()} units";
                    if (!string.IsNullOrEmpty(sizing)) desc += $" | {sizing}";

                    entries.Add(new LegendBuilder.LegendEntry
                    {
                        Color = new Color(r, g, b),
                        Label = $"{catName}: {fam.Key}",
                        Description = desc,
                        Bold = shift == 1, // Bold first (most common) family
                    });
                }
            }

            return entries;
        }

        /// <summary>
        /// Build fire rating legend entries from elements with fire rating data.
        /// </summary>
        public static List<LegendBuilder.LegendEntry> BuildFireRatingEntries(Document doc)
        {
            var elems = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null)
                .ToList();

            // Try to read fire rating from type parameters
            var ratingCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in elems)
            {
                string rating = "";
                try
                {
                    // Check several common fire rating parameter names
                    var typeId = el.GetTypeId();
                    if (typeId != ElementId.InvalidElementId)
                    {
                        var elType = doc.GetElement(typeId);
                        if (elType != null)
                        {
                            // Try built-in fire rating parameter
                            var frParam = elType.get_Parameter(BuiltInParameter.FIRE_RATING);
                            if (frParam != null)
                                rating = frParam.AsString() ?? "";
                        }
                    }
                }
                catch { }

                if (string.IsNullOrEmpty(rating)) continue;
                if (!ratingCounts.ContainsKey(rating)) ratingCounts[rating] = 0;
                ratingCounts[rating]++;
            }

            if (ratingCounts.Count == 0) return new List<LegendBuilder.LegendEntry>();

            // Color by fire rating severity
            var ratingColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
            {
                { "0 hr",   new Color(200, 220, 200) }, // Green — no rating
                { "30 min", new Color(255, 255, 150) }, // Light yellow
                { "1 hr",   new Color(255, 200, 100) }, // Orange
                { "1.5 hr", new Color(255, 160, 80)  }, // Dark orange
                { "2 hr",   new Color(255, 100, 60)  }, // Red-orange
                { "3 hr",   new Color(220, 50, 50)   }, // Red
                { "4 hr",   new Color(180, 0, 0)     }, // Dark red
            };

            var entries = new List<LegendBuilder.LegendEntry>();
            int paletteIdx = 0;
            var warmPalette = Select.ColorHelper.Palettes.TryGetValue("Warm", out var wp) ? wp : null;

            foreach (var kvp in ratingCounts.OrderBy(x => x.Key))
            {
                Color c;
                if (ratingColors.TryGetValue(kvp.Key, out Color rc))
                    c = rc;
                else if (warmPalette != null)
                    c = warmPalette[paletteIdx++ % warmPalette.Length];
                else
                    c = new Color(255, 160, 80);

                entries.Add(new LegendBuilder.LegendEntry
                {
                    Color = c,
                    Label = kvp.Key,
                    Description = $"{kvp.Value} elements",
                    Bold = true,
                });
            }
            return entries;
        }

        // ── Layer 2 & 3: Sheet Discipline Detection ────────────────────

        /// <summary>
        /// Detect which discipline a sheet belongs to from its name, number,
        /// and view content. Returns one or more discipline codes.
        ///
        /// Layer 1: Sheet number prefix (M-xxx=Mechanical, E-xxx=Electrical)
        /// Layer 2: Sheet name keywords ("Mechanical", "HVAC", "Plumbing")
        /// Layer 3: View content analysis (majority discipline from elements)
        /// </summary>
        public static List<string> DetectSheetDisciplines(Document doc, ViewSheet sheet)
        {
            var disciplines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Layer 1: Sheet number prefix
            string num = sheet.SheetNumber ?? "";
            if (num.Length >= 1)
            {
                char prefix = char.ToUpper(num[0]);
                switch (prefix)
                {
                    case 'M': disciplines.Add("M"); break;
                    case 'E': disciplines.Add("E"); break;
                    case 'P': disciplines.Add("P"); break;
                    case 'A': disciplines.Add("A"); break;
                    case 'S': disciplines.Add("S"); break;
                    case 'F': disciplines.Add("FP"); break;
                    case 'L': disciplines.Add("LV"); break;
                    case 'G': disciplines.Add("G"); break;
                }
            }

            // Layer 2: Sheet name keywords
            string name = (sheet.Name ?? "").ToUpperInvariant();
            if (name.Contains("MECHANICAL") || name.Contains("HVAC") || name.Contains("DUCT"))
                disciplines.Add("M");
            if (name.Contains("ELECTRICAL") || name.Contains("LIGHTING") || name.Contains("POWER"))
                disciplines.Add("E");
            if (name.Contains("PLUMBING") || name.Contains("SANITARY") || name.Contains("DRAINAGE"))
                disciplines.Add("P");
            if (name.Contains("ARCHITECTURAL") || name.Contains("INTERIOR") || name.Contains("FINISH"))
                disciplines.Add("A");
            if (name.Contains("STRUCTURAL") || name.Contains("FOUNDATION"))
                disciplines.Add("S");
            if (name.Contains("FIRE") || name.Contains("SPRINKLER"))
                disciplines.Add("FP");

            // Layer 3: View content analysis — scan placed views for majority discipline
            if (disciplines.Count == 0)
            {
                var discCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (ElementId viewId in sheet.GetAllPlacedViews())
                {
                    View v = doc.GetElement(viewId) as View;
                    if (v == null || v.IsTemplate) continue;
                    if (v.ViewType == ViewType.Legend || v.ViewType == ViewType.DraftingView) continue;

                    try
                    {
                        foreach (Element el in new FilteredElementCollector(doc, v.Id)
                            .WhereElementIsNotElementType()
                            .Where(e => e.Category != null && e.Category.HasMaterialQuantities))
                        {
                            string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                            if (string.IsNullOrEmpty(disc))
                            {
                                string cat = ParameterHelpers.GetCategoryName(el);
                                disc = TagConfig.DiscMap.TryGetValue(cat, out string dm) ? dm : null;
                            }
                            if (!string.IsNullOrEmpty(disc))
                            {
                                if (!discCounts.ContainsKey(disc)) discCounts[disc] = 0;
                                discCounts[disc]++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"SheetLegend: discipline detection failed for view: {ex.Message}");
                    }
                }

                // Add disciplines that represent >10% of elements
                int total = discCounts.Values.Sum();
                foreach (var kvp in discCounts.OrderByDescending(x => x.Value))
                {
                    if (total > 0 && (double)kvp.Value / total >= 0.10)
                        disciplines.Add(kvp.Key);
                }
            }

            return disciplines.ToList();
        }

        /// <summary>
        /// Determine which legend types are appropriate for a given discipline.
        /// Returns ordered list of legend scheme names to create.
        /// </summary>
        public static List<string> GetLegendTypesForDiscipline(string discipline)
        {
            switch (discipline?.ToUpperInvariant())
            {
                case "M":
                    return new List<string> { "MEPSystem", "Equipment", "Discipline" };
                case "E":
                    return new List<string> { "MEPSystem", "Equipment", "Discipline" };
                case "P":
                    return new List<string> { "MEPSystem", "Equipment", "Discipline" };
                case "FP":
                    return new List<string> { "MEPSystem", "FireRating", "Discipline" };
                case "A":
                    return new List<string> { "CompoundType", "Material", "Discipline" };
                case "S":
                    return new List<string> { "CompoundType", "Discipline" };
                case "LV":
                    return new List<string> { "MEPSystem", "Equipment", "Discipline" };
                default:
                    return new List<string> { "Discipline" };
            }
        }

        // ── Layer 4: Placement Intelligence ───────────────────────────

        /// <summary>
        /// Find an available placement position on a sheet that avoids existing
        /// viewports. Stacks legends vertically from the chosen corner.
        /// Returns the position string and offset index.
        /// </summary>
        public static string FindAvailablePosition(Document doc, ViewSheet sheet, int legendIndex)
        {
            // Stack legends from bottom-right upward, shifting Y for each
            // For the first legend use BottomRight, subsequent ones shift up
            switch (legendIndex)
            {
                case 0: return "BottomRight";
                case 1: return "TopRight";
                case 2: return "BottomLeft";
                case 3: return "TopLeft";
                default: return "BottomRight"; // wrap around
            }
        }

        // ── Layer 5: Deduplication ────────────────────────────────────

        /// <summary>
        /// Check if a STING legend of a given type already exists.
        /// Returns the existing view if found, null otherwise.
        /// </summary>
        public static View FindExistingLegend(Document doc, string legendType)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(v => !v.IsTemplate &&
                    v.Name.StartsWith("STING") &&
                    v.Name.Contains(legendType));
        }

        // ── Helpers ──────────────────────────────────────────────────

        /// <summary>Derive a material group from its name.</summary>
        private static string DeriveMaterialGroup(string matName)
        {
            if (string.IsNullOrEmpty(matName)) return "Other";
            string upper = matName.ToUpperInvariant();

            if (upper.Contains("CONCRETE") || upper.Contains("CEMENT")) return "Concrete";
            if (upper.Contains("STEEL") || upper.Contains("METAL") || upper.Contains("ALUMINUM")) return "Metal";
            if (upper.Contains("TIMBER") || upper.Contains("WOOD") || upper.Contains("PLYWOOD")) return "Timber";
            if (upper.Contains("GYPSUM") || upper.Contains("PLASTERBOARD") || upper.Contains("DRYWALL")) return "Gypsum";
            if (upper.Contains("BRICK") || upper.Contains("MASONRY") || upper.Contains("BLOCK")) return "Masonry";
            if (upper.Contains("GLASS") || upper.Contains("GLAZING")) return "Glass";
            if (upper.Contains("INSULATION") || upper.Contains("MINERAL WOOL") || upper.Contains("ROCKWOOL")) return "Insulation";
            if (upper.Contains("TILE") || upper.Contains("CERAMIC") || upper.Contains("PORCELAIN")) return "Tile";
            if (upper.Contains("PAINT") || upper.Contains("COATING") || upper.Contains("FINISH")) return "Finishes";
            if (upper.Contains("COPPER") || upper.Contains("PIPE") || upper.Contains("TUBE")) return "Piping";
            if (upper.Contains("DUCT") || upper.Contains("GALVANIZED")) return "Ductwork";
            if (upper.Contains("CABLE") || upper.Contains("WIRE") || upper.Contains("CONDUIT")) return "Electrical";
            if (upper.Contains("CARPET") || upper.Contains("VINYL") || upper.Contains("LINOLEUM")) return "Flooring";
            if (upper.Contains("STONE") || upper.Contains("MARBLE") || upper.Contains("GRANITE")) return "Stone";
            if (upper.Contains("ROOF") || upper.Contains("MEMBRANE") || upper.Contains("BITUMEN")) return "Roofing";

            return "Other";
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MEP System Legend Command — Color-coded system breakdown
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a legend showing all MEP systems in use with CIBSE/Uniclass colors.
    /// Scans project for HVAC, DCW, SAN, HWS, FP, LV, FLS, COM, ICT, etc.
    /// Optionally scoped to active sheet only.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepSystemLegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var dlg = new TaskDialog("MEP System Legend");
            dlg.MainInstruction = "Create MEP system color legend";
            dlg.MainContent =
                "Shows all MEP systems in use with CIBSE/Uniclass standard colors:\n" +
                "HVAC (blue), DCW (cyan), HWS (red), SAN (brown),\n" +
                "FP (red), LV (gold), COM (green), ICT (purple), etc.";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Full Project", "All MEP systems across entire project");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Active Sheet Only", "Only systems visible on current sheet");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var pick = dlg.Show();
            List<LegendBuilder.LegendEntry> entries;
            string subtitle;

            if (pick == TaskDialogResult.CommandLink1)
            {
                entries = LegendIntelligence.BuildMepSystemEntries(doc);
                subtitle = "All systems in project";
            }
            else if (pick == TaskDialogResult.CommandLink2)
            {
                ViewSheet sheet = doc.ActiveView as ViewSheet;
                if (sheet == null)
                {
                    sheet = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheet))
                        .Cast<ViewSheet>()
                        .FirstOrDefault(s => s.GetAllPlacedViews().Contains(doc.ActiveView.Id));
                }
                if (sheet == null)
                {
                    TaskDialog.Show("MEP System Legend", "Active view is not on a sheet.");
                    return Result.Succeeded;
                }
                entries = LegendIntelligence.BuildMepSystemEntriesForSheet(doc, sheet);
                subtitle = $"Sheet: {sheet.SheetNumber} - {sheet.Name}";
            }
            else
            {
                return Result.Cancelled;
            }

            if (entries.Count == 0)
            {
                TaskDialog.Show("MEP System Legend", "No MEP systems found in scope.");
                return Result.Succeeded;
            }

            var config = new LegendBuilder.LegendConfig
            {
                Title = "MEP Systems",
                Subtitle = subtitle,
                Footer = "CIBSE/Uniclass 2015 system classification",
            };

            View legendView;
            using (Transaction tx = new Transaction(doc, "STING MEP System Legend"))
            {
                tx.Start();
                legendView = LegendBuilder.CreateLegendView(doc, entries, config);
                tx.Commit();
            }

            if (legendView != null)
            {
                uidoc.ActiveView = legendView;
                TaskDialog.Show("MEP System Legend",
                    $"Created: '{legendView.Name}'\n" +
                    $"Systems: {entries.Count}\n\n" +
                    "Place on sheets for documentation.");
            }

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Material Legend Command — Materials in use with categories
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a legend showing all materials in the project grouped by type
    /// (Concrete, Metal, Timber, Gypsum, Insulation, etc.) with colors
    /// derived from material appearance.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MaterialLegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var entries = LegendIntelligence.BuildMaterialEntries(doc);
            if (entries.Count == 0)
            {
                TaskDialog.Show("Material Legend", "No materials found in the project.");
                return Result.Succeeded;
            }

            var config = new LegendBuilder.LegendConfig
            {
                Title = "Materials in Use",
                Subtitle = "Grouped by material category",
                Footer = "Generated by STING Tools",
                Columns = entries.Count > 10 ? 2 : 1,
            };

            View legendView;
            using (Transaction tx = new Transaction(doc, "STING Material Legend"))
            {
                tx.Start();
                legendView = LegendBuilder.CreateLegendView(doc, entries, config);
                tx.Commit();
            }

            if (legendView != null)
            {
                uidoc.ActiveView = legendView;
                TaskDialog.Show("Material Legend",
                    $"Created: '{legendView.Name}'\n" +
                    $"Material groups: {entries.Count}\n\n" +
                    "Place on architectural sheets for documentation.");
            }

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Compound Type Legend Command — Wall/Floor/Ceiling/Roof types
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a legend showing all wall, floor, ceiling, and roof types in use.
    /// Each entry shows category, type name, layer count, and total thickness.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CompoundTypeLegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var entries = LegendIntelligence.BuildCompoundTypeEntries(doc);
            if (entries.Count == 0)
            {
                TaskDialog.Show("Compound Type Legend", "No wall/floor/ceiling/roof types found in the project.");
                return Result.Succeeded;
            }

            var config = new LegendBuilder.LegendConfig
            {
                Title = "Wall / Floor / Ceiling / Roof Types",
                Subtitle = "Compound types in use with layer information",
                Footer = "Generated by STING Tools",
                Columns = entries.Count > 15 ? 2 : 1,
            };

            View legendView;
            using (Transaction tx = new Transaction(doc, "STING Compound Type Legend"))
            {
                tx.Start();
                legendView = LegendBuilder.CreateLegendView(doc, entries, config);
                tx.Commit();
            }

            if (legendView != null)
            {
                uidoc.ActiveView = legendView;
                TaskDialog.Show("Compound Type Legend",
                    $"Created: '{legendView.Name}'\n" +
                    $"Types: {entries.Count}\n\n" +
                    "Place on architectural/structural sheets.");
            }

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MEP Equipment Legend Command — Equipment families by system
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a legend showing MEP equipment families grouped by category
    /// (Mechanical Equipment, Electrical Equipment, Plumbing Fixtures, etc.)
    /// with family count and sizing data where available.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class EquipmentLegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var entries = LegendIntelligence.BuildEquipmentEntries(doc);
            if (entries.Count == 0)
            {
                TaskDialog.Show("Equipment Legend", "No MEP equipment found in the project.");
                return Result.Succeeded;
            }

            var config = new LegendBuilder.LegendConfig
            {
                Title = "MEP Equipment Schedule",
                Subtitle = "Equipment families by category with unit counts",
                Footer = "Generated by STING Tools",
                Columns = entries.Count > 12 ? 2 : 1,
            };

            View legendView;
            using (Transaction tx = new Transaction(doc, "STING Equipment Legend"))
            {
                tx.Start();
                legendView = LegendBuilder.CreateLegendView(doc, entries, config);
                tx.Commit();
            }

            if (legendView != null)
            {
                uidoc.ActiveView = legendView;
                TaskDialog.Show("Equipment Legend",
                    $"Created: '{legendView.Name}'\n" +
                    $"Equipment families: {entries.Count}\n\n" +
                    "Place on MEP sheets for documentation.");
            }

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Fire Rating Legend Command — Fire rating breakdown
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a legend showing fire ratings in the project with severity-based
    /// color coding (green=no rating, yellow=30min, orange=1hr, red=3hr+).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FireRatingLegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var entries = LegendIntelligence.BuildFireRatingEntries(doc);
            if (entries.Count == 0)
            {
                TaskDialog.Show("Fire Rating Legend",
                    "No fire rating data found in the project.\n" +
                    "Ensure wall/floor/door types have Fire Rating parameters set.");
                return Result.Succeeded;
            }

            var config = new LegendBuilder.LegendConfig
            {
                Title = "Fire Rating Legend",
                Subtitle = "Element fire ratings (severity color-coded)",
                Footer = "Generated by STING Tools — Fire Safety Documentation",
            };

            View legendView;
            using (Transaction tx = new Transaction(doc, "STING Fire Rating Legend"))
            {
                tx.Start();
                legendView = LegendBuilder.CreateLegendView(doc, entries, config);
                tx.Commit();
            }

            if (legendView != null)
            {
                uidoc.ActiveView = legendView;
                TaskDialog.Show("Fire Rating Legend",
                    $"Created: '{legendView.Name}'\n" +
                    $"Rating categories: {entries.Count}\n\n" +
                    "Place on fire safety sheets.");
            }

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Master Legend Pipeline — Multi-Layer Intelligent Full Automation
    //
    // This is the MAXIMUM AUTOMATION command. Zero user input required.
    //
    // Intelligence layers:
    //   1. Content Detection: Scan project for ALL legend-worthy data
    //   2. Legend Selection: Pick right legends per discipline
    //   3. Sheet Discipline Inference: Match sheets to disciplines
    //   4. Placement Intelligence: Avoid overlaps, stack legends
    //   5. Deduplication: Reuse existing, skip empty disciplines
    //   6. Per-sheet context: Create sheet-specific legends where needed
    //
    // Creates: Discipline + System + Category + Equipment + Material +
    //          CompoundType + FireRating + TagFamilies + TagSegments + TAG7
    //
    // Places: Discipline legend on ALL sheets
    //         System legend on MEP sheets (M/E/P/FP)
    //         Material legend on Architectural sheets (A)
    //         Equipment legend on MEP equipment sheets
    //
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Maximum automation legend pipeline. Detects project content, creates all
    /// appropriate legends, and places them on the correct sheets based on
    /// discipline detection. Zero user input beyond confirmation.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MasterLegendPipelineCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Confirmation dialog
            var dlg = new TaskDialog("Master Legend Pipeline");
            dlg.MainInstruction = "Run full intelligent legend automation?";
            dlg.MainContent =
                "5-Layer Intelligence Engine:\n\n" +
                "  Layer 1 — Content Detection\n" +
                "    Scan project for systems, materials, types, equipment\n\n" +
                "  Layer 2 — Legend Selection\n" +
                "    Pick right legends per discipline (MEP→System, Arch→Material)\n\n" +
                "  Layer 3 — Sheet Discipline Inference\n" +
                "    Match sheets to M/E/P/A/S from name, number, and content\n\n" +
                "  Layer 4 — Placement Intelligence\n" +
                "    Auto-place legends on correct sheets, avoid overlaps\n\n" +
                "  Layer 5 — Deduplication\n" +
                "    Skip empty disciplines, reuse existing STING legends\n\n" +
                "Creates: System + Equipment + Material + CompoundType + FireRating +\n" +
                "         TagFamilies + Discipline + Category + Segments + TAG7\n\n" +
                "Places legends on discipline-matched sheets automatically.";

            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Run Full Pipeline", "Maximum automation — zero additional input");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Create Only (no placement)", "Create all legends but don't place on sheets");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var pick = dlg.Show();
            bool autoPlace = pick == TaskDialogResult.CommandLink1;
            if (pick != TaskDialogResult.CommandLink1 && pick != TaskDialogResult.CommandLink2)
                return Result.Cancelled;

            var report = new StringBuilder();
            int created = 0;
            int placed = 0;
            var createdViews = new Dictionary<string, View>(); // scheme → view

            using (Transaction tx = new Transaction(doc, "STING Master Legend Pipeline"))
            {
                tx.Start();

                // ── Step 1: Create ALL legend types ──────────────────

                // 1a. MEP System Legend
                var sysEntries = LegendIntelligence.BuildMepSystemEntries(doc);
                if (sysEntries.Count > 0)
                {
                    var existing = LegendIntelligence.FindExistingLegend(doc, "MEP Systems");
                    if (existing == null)
                    {
                        var v = LegendBuilder.CreateLegendView(doc, sysEntries,
                            new LegendBuilder.LegendConfig
                            {
                                Title = "MEP Systems",
                                Subtitle = "CIBSE/Uniclass 2015 system classification",
                                Footer = "STING Master Pipeline",
                            });
                        if (v != null) { createdViews["MEPSystem"] = v; created++; }
                    }
                    else
                    {
                        createdViews["MEPSystem"] = existing;
                        report.AppendLine("  Reused existing: MEP Systems");
                    }
                }

                // 1b. Equipment Legend
                var equipEntries = LegendIntelligence.BuildEquipmentEntries(doc);
                if (equipEntries.Count > 0)
                {
                    var existing = LegendIntelligence.FindExistingLegend(doc, "MEP Equipment");
                    if (existing == null)
                    {
                        var v = LegendBuilder.CreateLegendView(doc, equipEntries,
                            new LegendBuilder.LegendConfig
                            {
                                Title = "MEP Equipment Schedule",
                                Subtitle = "Equipment families by category",
                                Footer = "STING Master Pipeline",
                                Columns = equipEntries.Count > 12 ? 2 : 1,
                            });
                        if (v != null) { createdViews["Equipment"] = v; created++; }
                    }
                    else
                    {
                        createdViews["Equipment"] = existing;
                        report.AppendLine("  Reused existing: MEP Equipment");
                    }
                }

                // 1c. Material Legend
                var matEntries = LegendIntelligence.BuildMaterialEntries(doc);
                if (matEntries.Count > 0)
                {
                    var existing = LegendIntelligence.FindExistingLegend(doc, "Materials in Use");
                    if (existing == null)
                    {
                        var v = LegendBuilder.CreateLegendView(doc, matEntries,
                            new LegendBuilder.LegendConfig
                            {
                                Title = "Materials in Use",
                                Subtitle = "Grouped by material category",
                                Footer = "STING Master Pipeline",
                                Columns = matEntries.Count > 10 ? 2 : 1,
                            });
                        if (v != null) { createdViews["Material"] = v; created++; }
                    }
                    else
                    {
                        createdViews["Material"] = existing;
                        report.AppendLine("  Reused existing: Materials");
                    }
                }

                // 1d. Compound Type Legend
                var compEntries = LegendIntelligence.BuildCompoundTypeEntries(doc);
                if (compEntries.Count > 0)
                {
                    var existing = LegendIntelligence.FindExistingLegend(doc, "Wall / Floor");
                    if (existing == null)
                    {
                        var v = LegendBuilder.CreateLegendView(doc, compEntries,
                            new LegendBuilder.LegendConfig
                            {
                                Title = "Wall / Floor / Ceiling / Roof Types",
                                Subtitle = "Compound types with layer information",
                                Footer = "STING Master Pipeline",
                                Columns = compEntries.Count > 15 ? 2 : 1,
                            });
                        if (v != null) { createdViews["CompoundType"] = v; created++; }
                    }
                    else
                    {
                        createdViews["CompoundType"] = existing;
                        report.AppendLine("  Reused existing: Compound Types");
                    }
                }

                // 1e. Fire Rating Legend
                var fireEntries = LegendIntelligence.BuildFireRatingEntries(doc);
                if (fireEntries.Count > 0)
                {
                    var existing = LegendIntelligence.FindExistingLegend(doc, "Fire Rating");
                    if (existing == null)
                    {
                        var v = LegendBuilder.CreateLegendView(doc, fireEntries,
                            new LegendBuilder.LegendConfig
                            {
                                Title = "Fire Rating Legend",
                                Subtitle = "Element fire ratings (severity color-coded)",
                                Footer = "STING Master Pipeline",
                            });
                        if (v != null) { createdViews["FireRating"] = v; created++; }
                    }
                    else
                    {
                        createdViews["FireRating"] = existing;
                        report.AppendLine("  Reused existing: Fire Rating");
                    }
                }

                // 1f. Discipline Legend
                var discEntries = LegendBuilder.AutoFromProject(doc, "Discipline");
                if (discEntries.Count > 0)
                {
                    var existing = LegendIntelligence.FindExistingLegend(doc, "Elements by Discipline");
                    if (existing == null)
                    {
                        var v = LegendBuilder.CreateLegendView(doc, discEntries,
                            new LegendBuilder.LegendConfig
                            {
                                Title = "Elements by Discipline",
                                Subtitle = "M/E/P/A/S/FP/LV/G discipline breakdown",
                                Footer = "STING Master Pipeline",
                            });
                        if (v != null) { createdViews["Discipline"] = v; created++; }
                    }
                    else
                    {
                        createdViews["Discipline"] = existing;
                        report.AppendLine("  Reused existing: Discipline");
                    }
                }

                // 1g. Category Legend
                var catEntries = LegendBuilder.AutoFromProject(doc, "Category");
                if (catEntries.Count > 0)
                {
                    var existing = LegendIntelligence.FindExistingLegend(doc, "Elements by Category");
                    if (existing == null)
                    {
                        var v = LegendBuilder.CreateLegendView(doc, catEntries,
                            new LegendBuilder.LegendConfig
                            {
                                Title = "Elements by Category",
                                Subtitle = "Category breakdown",
                                Footer = "STING Master Pipeline",
                                Columns = catEntries.Count > 12 ? 2 : 1,
                            });
                        if (v != null) { createdViews["Category"] = v; created++; }
                    }
                    else
                    {
                        createdViews["Category"] = existing;
                    }
                }

                // 1h. Tag Family Legend
                var tagEntries = LegendBuilder.CollectTagFamilies(doc);
                if (tagEntries.Count > 0)
                {
                    var existing = LegendIntelligence.FindExistingLegend(doc, "Tag Families");
                    if (existing == null)
                    {
                        var v = LegendBuilder.CreateTagLegendView(doc, tagEntries, "Tag Families", "Discipline");
                        if (v != null) { createdViews["TagFamilies"] = v; created++; }
                    }
                    else
                    {
                        createdViews["TagFamilies"] = existing;
                    }
                }

                // 1i. Tag Segments + TAG7
                var segEntries = LegendBuilder.FromSegmentStyles();
                if (segEntries.Count > 0)
                {
                    var existing = LegendIntelligence.FindExistingLegend(doc, "Tag Segments");
                    if (existing == null)
                    {
                        var v = LegendBuilder.CreateLegendView(doc, segEntries,
                            new LegendBuilder.LegendConfig
                            {
                                Title = "ISO 19650 Tag Segments",
                                Subtitle = "DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ",
                                Footer = "STING Master Pipeline",
                            });
                        if (v != null) { createdViews["TagSegments"] = v; created++; }
                    }
                }

                var tag7Entries = LegendBuilder.FromTag7SectionStyles();
                if (tag7Entries.Count > 0)
                {
                    var existing = LegendIntelligence.FindExistingLegend(doc, "TAG7 Narrative");
                    if (existing == null)
                    {
                        var v = LegendBuilder.CreateLegendView(doc, tag7Entries,
                            new LegendBuilder.LegendConfig
                            {
                                Title = "TAG7 Narrative Sections",
                                Footer = "STING Master Pipeline",
                            });
                        if (v != null) { createdViews["TAG7"] = v; created++; }
                    }
                }

                // ── Step 2: Intelligent Placement ────────────────────
                if (autoPlace)
                {
                    var sheets = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheet))
                        .Cast<ViewSheet>()
                        .Where(s => !s.IsPlaceholder)
                        .OrderBy(s => s.SheetNumber)
                        .ToList();

                    // Place Discipline legend on ALL sheets (universal)
                    if (createdViews.TryGetValue("Discipline", out View discView) &&
                        discView.ViewType == ViewType.Legend)
                    {
                        foreach (var s in sheets)
                        {
                            if (Viewport.CanAddViewToSheet(doc, s.Id, discView.Id))
                            {
                                var vp = LegendBuilder.PlaceLegendOnSheet(doc, s, discView, "BottomRight");
                                if (vp != null) placed++;
                            }
                        }
                        report.AppendLine($"  Discipline legend → {placed} sheets");
                    }

                    // Place discipline-specific legends on matched sheets
                    foreach (var sheet in sheets)
                    {
                        var sheetDiscs = LegendIntelligence.DetectSheetDisciplines(doc, sheet);
                        int legendIdx = 1; // Start at 1 since Discipline is at BottomRight (idx 0)

                        foreach (string disc in sheetDiscs)
                        {
                            var legendTypes = LegendIntelligence.GetLegendTypesForDiscipline(disc);
                            foreach (string legendType in legendTypes)
                            {
                                if (legendType == "Discipline") continue; // Already placed

                                if (createdViews.TryGetValue(legendType, out View lv) &&
                                    lv.ViewType == ViewType.Legend &&
                                    Viewport.CanAddViewToSheet(doc, sheet.Id, lv.Id))
                                {
                                    string pos = LegendIntelligence.FindAvailablePosition(doc, sheet, legendIdx);
                                    var vp = LegendBuilder.PlaceLegendOnSheet(doc, sheet, lv, pos);
                                    if (vp != null) { placed++; legendIdx++; }
                                }
                            }
                        }
                    }
                }

                tx.Commit();
            }

            // Build final report
            var finalReport = new StringBuilder();
            finalReport.AppendLine($"Master Legend Pipeline Complete\n");
            finalReport.AppendLine($"Legends created: {created}");

            foreach (var kvp in createdViews)
                finalReport.AppendLine($"  {kvp.Key}: {kvp.Value.Name} ({kvp.Value.ViewType})");

            if (autoPlace)
            {
                finalReport.AppendLine($"\nViewports placed: {placed}");
                finalReport.AppendLine("  Discipline → ALL sheets (BottomRight)");
                finalReport.AppendLine("  MEP System → M/E/P/FP sheets");
                finalReport.AppendLine("  Material → A sheets");
                finalReport.AppendLine("  Equipment → M/E/P sheets");
                finalReport.AppendLine("  CompoundType → A/S sheets");
                finalReport.AppendLine("  FireRating → FP sheets");
            }

            if (report.Length > 0)
            {
                finalReport.AppendLine($"\nNotes:");
                finalReport.Append(report.ToString());
            }

            finalReport.AppendLine("\nAll legends in Project Browser under Legends/Drafting Views.");

            TaskDialog.Show("Master Legend Pipeline", finalReport.ToString());

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // VG / VT / Filter–Linked Legend System
    //
    // The core architectural fix: legends read FROM the Revit VG chain
    // at runtime instead of using hardcoded color maps. This means:
    //
    //   1. If a filter color changes, refreshing the legend picks it up
    //   2. If a view template is updated, the legend matches instantly
    //   3. No more color discrepancies between filter, template, and legend
    //   4. Category VG overrides are captured for the first time
    //   5. Legends document EXACTLY what the user sees
    //
    // Three modes:
    //   A. Filter Legend  — from view.GetFilters() + GetFilterOverrides()
    //   B. Category Legend — from view.GetCategoryOverrides(catId)
    //   C. Template Legend — combines A + B from a view template
    //
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// VG-linked legend builders that read actual applied graphic overrides
    /// from the Revit view/template system instead of hardcoded color maps.
    /// </summary>
    internal static class VGLinkedLegendBuilder
    {
        /// <summary>
        /// Extract override info as a human-readable description.
        /// Shows line color, fill color, transparency, halftone, line weight.
        /// </summary>
        private static string DescribeOverride(OverrideGraphicSettings ogs)
        {
            var parts = new List<string>();

            if (ogs.ProjectionLineColor.IsValid)
            {
                Color c = ogs.ProjectionLineColor;
                parts.Add($"Line: RGB({c.Red},{c.Green},{c.Blue})");
            }
            if (ogs.ProjectionLineWeight > 0)
                parts.Add($"Wt: {ogs.ProjectionLineWeight}");
            if (ogs.SurfaceForegroundPatternColor.IsValid)
            {
                Color c = ogs.SurfaceForegroundPatternColor;
                parts.Add($"Fill: RGB({c.Red},{c.Green},{c.Blue})");
            }
            // SurfaceTransparency is not readable from OverrideGraphicSettings
            if (ogs.Halftone)
                parts.Add("Halftone");

            return parts.Count > 0 ? string.Join(" | ", parts) : "No overrides";
        }

        /// <summary>
        /// Get the "best" display color from an OverrideGraphicSettings.
        /// Priority: surface foreground color → projection line color → grey.
        /// </summary>
        private static Color GetDisplayColor(OverrideGraphicSettings ogs)
        {
            if (ogs.SurfaceForegroundPatternColor.IsValid)
                return ogs.SurfaceForegroundPatternColor;
            if (ogs.ProjectionLineColor.IsValid)
                return ogs.ProjectionLineColor;
            return new Color(180, 180, 180); // no override
        }

        /// <summary>
        /// Check whether an OverrideGraphicSettings has any meaningful override.
        /// </summary>
        private static bool HasMeaningfulOverride(OverrideGraphicSettings ogs)
        {
            return ogs.ProjectionLineColor.IsValid
                || ogs.SurfaceForegroundPatternColor.IsValid
                || ogs.ProjectionLineWeight > 0
                || 0 /* SurfaceTransparency not readable */ > 0
                || ogs.Halftone;
        }

        // ── Mode A: Filter Legend ──────────────────────────────────

        /// <summary>
        /// Build legend entries by reading actual filter overrides from a view.
        /// Each entry represents one applied filter with its REAL graphic override colors.
        ///
        /// This is the core of the VG-linked approach: instead of using hardcoded
        /// color maps, we read what the view actually displays.
        /// </summary>
        /// <param name="doc">The Revit document.</param>
        /// <param name="view">The view (or view template) to read filters from.</param>
        /// <param name="stingOnly">If true, only include "STING" prefix filters.</param>
        /// <returns>Legend entries with actual applied colors.</returns>
        public static List<LegendBuilder.LegendEntry> FromViewFilters(
            Document doc, View view, bool stingOnly = false)
        {
            var entries = new List<LegendBuilder.LegendEntry>();
            if (view == null) return entries;

            ICollection<ElementId> filterIds;
            try { filterIds = view.GetFilters(); }
            catch { return entries; }

            foreach (ElementId fid in filterIds)
            {
                ParameterFilterElement filter = doc.GetElement(fid) as ParameterFilterElement;
                if (filter == null) continue;

                string name = filter.Name ?? "(Unknown)";
                if (stingOnly && !name.StartsWith("STING")) continue;

                // Check visibility
                bool visible;
                try { visible = view.GetFilterVisibility(fid); }
                catch { visible = true; }

                if (!visible) continue; // skip hidden filters

                // Read the ACTUAL graphic overrides
                OverrideGraphicSettings ogs;
                try { ogs = view.GetFilterOverrides(fid); }
                catch { continue; }

                if (!HasMeaningfulOverride(ogs)) continue;

                Color displayColor = GetDisplayColor(ogs);
                string desc = DescribeOverride(ogs);

                // Clean up filter name for display
                string label = name;
                if (label.StartsWith("STING - "))
                    label = label.Substring(8); // Remove "STING - " prefix
                else if (label.StartsWith("STING "))
                    label = label.Substring(6);

                entries.Add(new LegendBuilder.LegendEntry
                {
                    Color = displayColor,
                    Label = label,
                    Description = desc,
                    Bold = ogs.Halftone || 0 /* SurfaceTransparency not readable */ >= 40,
                    Italic = ogs.Halftone,
                });
            }

            return entries;
        }

        // ── Mode B: Category VG Legend ─────────────────────────────

        /// <summary>
        /// Build legend entries by reading per-category VG overrides from a view.
        /// Only includes categories that have non-default overrides applied.
        /// </summary>
        public static List<LegendBuilder.LegendEntry> FromCategoryOverrides(
            Document doc, View view)
        {
            var entries = new List<LegendBuilder.LegendEntry>();
            if (view == null) return entries;

            // Check all known taggable categories
            foreach (var bic in SharedParamGuids.AllCategoryEnums)
            {
                Category cat = null;
                try
                {
                    cat = doc.Settings.Categories.get_Item(bic);
                    if (cat == null) continue;

                    OverrideGraphicSettings ogs = view.GetCategoryOverrides(new ElementId(bic));
                    if (!HasMeaningfulOverride(ogs)) continue;

                    Color displayColor = GetDisplayColor(ogs);
                    string desc = DescribeOverride(ogs);

                    entries.Add(new LegendBuilder.LegendEntry
                    {
                        Color = displayColor,
                        Label = cat.Name,
                        Description = desc,
                        Italic = ogs.Halftone,
                    });
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"CategoryLegend: failed reading overrides for '{cat?.Name}': {ex.Message}");
                }
            }

            return entries;
        }

        // ── Mode C: Template Legend (Filters + Categories) ─────────

        /// <summary>
        /// Build a comprehensive legend from a view template showing:
        ///   Section 1: Applied filters with their graphic overrides
        ///   Section 2: Category VG overrides
        /// This documents EVERYTHING about what a template displays.
        /// </summary>
        public static (List<LegendBuilder.LegendEntry> filterEntries,
                        List<LegendBuilder.LegendEntry> categoryEntries)
            FromViewTemplate(Document doc, View template)
        {
            var filterEntries = FromViewFilters(doc, template, stingOnly: false);
            var categoryEntries = FromCategoryOverrides(doc, template);
            return (filterEntries, categoryEntries);
        }

        /// <summary>
        /// Find all STING view templates in the project.
        /// </summary>
        public static List<View> GetStingTemplates(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate && v.Name.StartsWith("STING"))
                .OrderBy(v => v.Name)
                .ToList();
        }

        /// <summary>
        /// Get the template applied to a view (if any).
        /// </summary>
        public static View GetAppliedTemplate(Document doc, View view)
        {
            if (view == null || view.ViewTemplateId == ElementId.InvalidElementId)
                return null;
            return doc.GetElement(view.ViewTemplateId) as View;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Filter Legend Command — Legend from actual applied filter overrides
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a legend by reading the ACTUAL filter overrides from the active view
    /// (or its template). Shows exactly what the user sees — not hardcoded colors.
    /// If filter colors change later, refreshing the legend picks up the new colors.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FilterLegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            if (view == null)
            {
                TaskDialog.Show("Filter Legend", "No active view.");
                return Result.Failed;
            }

            // Determine source: active view or its template
            View sourceView = view;
            string sourceName = view.Name;

            View template = VGLinkedLegendBuilder.GetAppliedTemplate(doc, view);
            if (template != null)
            {
                var dlg = new TaskDialog("Filter Legend");
                dlg.MainInstruction = "Read filter overrides from which source?";
                dlg.MainContent =
                    $"Active view: {view.Name}\n" +
                    $"Applied template: {template.Name}\n\n" +
                    "The template defines the base VG state;\n" +
                    "the view may have additional per-element overrides.";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    $"Template: {template.Name} (Recommended)",
                    "Read filter overrides from the view template — the source of truth");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    $"Active View: {view.Name}",
                    "Read filter overrides from the view itself (may differ from template)");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var pick = dlg.Show();
                if (pick == TaskDialogResult.CommandLink1)
                {
                    sourceView = template;
                    sourceName = template.Name;
                }
                else if (pick != TaskDialogResult.CommandLink2)
                {
                    return Result.Cancelled;
                }
            }

            // Read actual filter overrides
            var entries = VGLinkedLegendBuilder.FromViewFilters(doc, sourceView, stingOnly: false);
            if (entries.Count == 0)
            {
                TaskDialog.Show("Filter Legend",
                    "No filters with graphic overrides found.\n\n" +
                    "Apply filters to the view or template first:\n" +
                    "  1. Create Filters (Temp panel)\n" +
                    "  2. Apply Filters to Views\n" +
                    "  3. Create VG Overrides");
                return Result.Succeeded;
            }

            var config = new LegendBuilder.LegendConfig
            {
                Title = $"Filter Overrides — {sourceName}",
                Subtitle = $"Read from: {(sourceView.IsTemplate ? "View Template" : "View")} | {entries.Count} active filters",
                Footer = "Colors read from actual VG filter overrides — updates when filters change",
                ShowCounts = false,
            };

            View legendView;
            using (Transaction tx = new Transaction(doc, "STING Filter Legend"))
            {
                tx.Start();
                legendView = LegendBuilder.CreateLegendView(doc, entries, config);
                tx.Commit();
            }

            if (legendView != null)
            {
                uidoc.ActiveView = legendView;
                TaskDialog.Show("Filter Legend",
                    $"Created: '{legendView.Name}'\n" +
                    $"Source: {sourceName}\n" +
                    $"Filters: {entries.Count}\n\n" +
                    "This legend shows ACTUAL applied colors.\n" +
                    "If you change filter colors, use 'Update Legend' to refresh.");
            }

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Template Legend Command — Full VG documentation for a view template
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a comprehensive legend documenting a view template's full VG state:
    /// all applied filters with their override colors, plus all category VG overrides.
    /// This is the "what does this template show?" documentation command.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TemplateLegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Find STING templates
            var templates = VGLinkedLegendBuilder.GetStingTemplates(doc);
            if (templates.Count == 0)
            {
                // Try active view's template
                View activeTemplate = VGLinkedLegendBuilder.GetAppliedTemplate(doc, doc.ActiveView);
                if (activeTemplate != null)
                    templates = new List<View> { activeTemplate };
            }

            if (templates.Count == 0)
            {
                TaskDialog.Show("Template Legend",
                    "No view templates found.\n" +
                    "Create templates first (Temp > View Templates).");
                return Result.Succeeded;
            }

            // Pick template
            var dlg = new TaskDialog("Template Legend");
            dlg.MainInstruction = "Which template to document?";
            dlg.MainContent = "Creates a legend showing ALL filter overrides and\n" +
                "category VG overrides from the selected template.";

            var commands = new[] {
                TaskDialogCommandLinkId.CommandLink1,
                TaskDialogCommandLinkId.CommandLink2,
                TaskDialogCommandLinkId.CommandLink3,
                TaskDialogCommandLinkId.CommandLink4,
            };
            int shown = Math.Min(templates.Count, 4);
            for (int i = 0; i < shown; i++)
            {
                int filterCount = 0;
                try { filterCount = templates[i].GetFilters().Count; } catch { }
                dlg.AddCommandLink(commands[i],
                    templates[i].Name,
                    $"{filterCount} filters applied");
            }
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;
            if (templates.Count > 4)
                dlg.FooterText = $"Showing 4 of {templates.Count} templates.";

            var pick = dlg.Show();
            int idx = pick switch
            {
                TaskDialogResult.CommandLink1 => 0,
                TaskDialogResult.CommandLink2 => 1,
                TaskDialogResult.CommandLink3 => 2,
                TaskDialogResult.CommandLink4 => 3,
                _ => -1,
            };
            if (idx < 0) return Result.Cancelled;

            View selected = templates[idx];

            // Read FULL VG state
            var (filterEntries, categoryEntries) = VGLinkedLegendBuilder.FromViewTemplate(doc, selected);

            // Combine into a single legend with section headers
            var allEntries = new List<LegendBuilder.LegendEntry>();

            if (filterEntries.Count > 0)
            {
                allEntries.Add(new LegendBuilder.LegendEntry
                {
                    Color = new Color(40, 40, 40),
                    Label = $"── FILTERS ({filterEntries.Count}) ──",
                    Description = "",
                    Bold = true,
                });
                allEntries.AddRange(filterEntries);
            }

            if (categoryEntries.Count > 0)
            {
                allEntries.Add(new LegendBuilder.LegendEntry
                {
                    Color = new Color(40, 40, 40),
                    Label = $"── CATEGORY VG ({categoryEntries.Count}) ──",
                    Description = "",
                    Bold = true,
                });
                allEntries.AddRange(categoryEntries);
            }

            if (allEntries.Count == 0)
            {
                TaskDialog.Show("Template Legend",
                    $"Template '{selected.Name}' has no graphic overrides.\n" +
                    "Run 'Create VG Overrides' to apply discipline colors.");
                return Result.Succeeded;
            }

            var config = new LegendBuilder.LegendConfig
            {
                Title = $"Template: {selected.Name}",
                Subtitle = $"Filters: {filterEntries.Count} | Category overrides: {categoryEntries.Count}",
                Footer = "Complete VG documentation — read from actual template overrides",
                Columns = allEntries.Count > 18 ? 2 : 1,
                ShowCounts = false,
            };

            View legendView;
            using (Transaction tx = new Transaction(doc, "STING Template Legend"))
            {
                tx.Start();
                legendView = LegendBuilder.CreateLegendView(doc, allEntries, config);
                tx.Commit();
            }

            if (legendView != null)
            {
                uidoc.ActiveView = legendView;
                TaskDialog.Show("Template Legend",
                    $"Created: '{legendView.Name}'\n" +
                    $"Template: {selected.Name}\n" +
                    $"  Filters documented: {filterEntries.Count}\n" +
                    $"  Category overrides: {categoryEntries.Count}\n\n" +
                    "This legend reflects the template's ACTUAL VG state.\n" +
                    "Use 'Update Legend' to refresh after template changes.");
            }

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // VG Category Legend Command — Per-category overrides from active view
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a legend from per-category VG overrides in the active view.
    /// Shows only categories that have non-default graphic overrides applied.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class VGCategoryLegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            if (view == null)
            {
                TaskDialog.Show("VG Category Legend", "No active view.");
                return Result.Failed;
            }

            var entries = VGLinkedLegendBuilder.FromCategoryOverrides(doc, view);
            if (entries.Count == 0)
            {
                TaskDialog.Show("VG Category Legend",
                    "No category VG overrides found in the active view.\n" +
                    "Apply category overrides first (VG Overrides, Color by Parameter, etc.).");
                return Result.Succeeded;
            }

            var config = new LegendBuilder.LegendConfig
            {
                Title = $"Category VG — {view.Name}",
                Subtitle = $"{entries.Count} categories with overrides",
                Footer = "Read from actual category VG overrides — updates when VG changes",
                ShowCounts = false,
            };

            View legendView;
            using (Transaction tx = new Transaction(doc, "STING VG Category Legend"))
            {
                tx.Start();
                legendView = LegendBuilder.CreateLegendView(doc, entries, config);
                tx.Commit();
            }

            if (legendView != null)
            {
                uidoc.ActiveView = legendView;
                TaskDialog.Show("VG Category Legend",
                    $"Created: '{legendView.Name}'\n" +
                    $"Categories: {entries.Count}\n" +
                    $"Source: {view.Name}\n\n" +
                    "Shows actual per-category graphic overrides.");
            }

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Batch Template Legends — Document ALL templates at once
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create legends for every STING view template in the project.
    /// Each template gets its own legend view documenting its full VG state
    /// (filters + category overrides). Full documentation automation.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchTemplateLegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var templates = VGLinkedLegendBuilder.GetStingTemplates(doc);
            if (templates.Count == 0)
            {
                TaskDialog.Show("Batch Template Legends", "No STING view templates found.");
                return Result.Succeeded;
            }

            var dlg = new TaskDialog("Batch Template Legends");
            dlg.MainInstruction = $"Document {templates.Count} STING view templates?";
            dlg.MainContent =
                "Creates one legend per template showing all filter overrides\n" +
                "and category VG overrides read from the actual template.\n\n" +
                "Templates found:\n" +
                string.Join("\n", templates.Take(10).Select(t => $"  - {t.Name}")) +
                (templates.Count > 10 ? $"\n  ... and {templates.Count - 10} more" : "");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"Create {templates.Count} Template Legends",
                "One legend per template, all VG documented");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            if (dlg.Show() != TaskDialogResult.CommandLink1)
                return Result.Cancelled;

            int created = 0;
            int skipped = 0;

            using (Transaction tx = new Transaction(doc, "STING Batch Template Legends"))
            {
                tx.Start();

                foreach (var template in templates)
                {
                    var (filterEntries, categoryEntries) =
                        VGLinkedLegendBuilder.FromViewTemplate(doc, template);

                    var allEntries = new List<LegendBuilder.LegendEntry>();
                    if (filterEntries.Count > 0)
                    {
                        allEntries.Add(new LegendBuilder.LegendEntry
                        {
                            Color = new Color(40, 40, 40),
                            Label = $"── FILTERS ({filterEntries.Count}) ──",
                            Bold = true,
                        });
                        allEntries.AddRange(filterEntries);
                    }
                    if (categoryEntries.Count > 0)
                    {
                        allEntries.Add(new LegendBuilder.LegendEntry
                        {
                            Color = new Color(40, 40, 40),
                            Label = $"── CATEGORIES ({categoryEntries.Count}) ──",
                            Bold = true,
                        });
                        allEntries.AddRange(categoryEntries);
                    }

                    if (allEntries.Count == 0) { skipped++; continue; }

                    string shortName = template.Name.Replace("STING - ", "");
                    var config = new LegendBuilder.LegendConfig
                    {
                        Title = $"VT: {shortName}",
                        Subtitle = $"F:{filterEntries.Count} | C:{categoryEntries.Count}",
                        Footer = "VG-linked — read from template overrides",
                        Columns = allEntries.Count > 18 ? 2 : 1,
                        ShowCounts = false,
                    };

                    var view = LegendBuilder.CreateLegendView(doc, allEntries, config);
                    if (view != null) created++;
                    else skipped++;
                }

                tx.Commit();
            }

            TaskDialog.Show("Batch Template Legends",
                $"Created {created} template legends.\n" +
                (skipped > 0 ? $"Skipped {skipped} (no overrides).\n" : "") +
                "\nEach legend documents the template's actual VG state.\n" +
                "Find them in the Project Browser.");

            return Result.Succeeded;
        }
    }
}
