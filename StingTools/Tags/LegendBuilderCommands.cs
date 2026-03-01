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
        private static void PopulateLegendContent(Document doc, View legendView,
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
            var discNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "M", "Mechanical" }, { "E", "Electrical" }, { "P", "Plumbing" },
                { "A", "Architectural" }, { "S", "Structural" }, { "FP", "Fire Protection" },
                { "LV", "Low Voltage" }, { "G", "General" }
            };

            foreach (var kvp in discColors)
            {
                string name = discNames.TryGetValue(kvp.Key, out string n) ? n : kvp.Key;
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
                            var discNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                {"M","Mechanical"},{"E","Electrical"},{"P","Plumbing"},
                                {"A","Architectural"},{"S","Structural"},{"FP","Fire Protection"},
                                {"LV","Low Voltage"},{"G","General"}
                            };
                            string name = discNames.TryGetValue(g.Key, out string n) ? n : g.Key;
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
                "All Legends (6 views)",
                "Discipline + Category + System + Level + Type + Status");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Discipline + Category + System",
                "Most common BIM legend set");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Category + Type",
                "Family/Type inventory legends");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Tag Structure Legends",
                "Tag Segments + TAG7 Sections + Validation Status");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var pick = dlg.Show();
            List<string> schemesToCreate;

            switch (pick)
            {
                case TaskDialogResult.CommandLink1:
                    schemesToCreate = new List<string> { "Discipline", "Category", "System", "Level", "Type", "Status" };
                    break;
                case TaskDialogResult.CommandLink2:
                    schemesToCreate = new List<string> { "Discipline", "Category", "System" };
                    break;
                case TaskDialogResult.CommandLink3:
                    schemesToCreate = new List<string> { "Category", "Type" };
                    break;
                case TaskDialogResult.CommandLink4:
                    schemesToCreate = new List<string> { "TagSegments", "TAG7Sections", "Validation" };
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

            // Scan view elements for graphic overrides
            var colorGroups = new Dictionary<string, (Color color, int count)>();

            foreach (Element el in new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType())
            {
                var ogs = view.GetElementOverrides(el.Id);

                // Check if element has a projection line color override
                Color lineColor = ogs.ProjectionLineColor;
                if (!lineColor.IsValid) continue;

                string colorKey = $"{lineColor.Red},{lineColor.Green},{lineColor.Blue}";

                // Try to find a meaningful label
                string label = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                if (string.IsNullOrEmpty(label))
                    label = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                if (string.IsNullOrEmpty(label))
                    label = ParameterHelpers.GetCategoryName(el);
                if (string.IsNullOrEmpty(label))
                    label = colorKey;

                // Group by color
                if (!colorGroups.ContainsKey(colorKey))
                    colorGroups[colorKey] = (lineColor, 0);

                var existing = colorGroups[colorKey];
                colorGroups[colorKey] = (existing.color, existing.count + 1);
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

            // Try to match colors to known schemes
            foreach (var kvp in colorGroups.OrderByDescending(x => x.Value.count))
            {
                string label = TryMatchColor(kvp.Value.color);
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
                    var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        {"M","Mechanical"},{"E","Electrical"},{"P","Plumbing"},
                        {"A","Architectural"},{"S","Structural"},{"FP","Fire Protection"},
                        {"LV","Low Voltage"},{"G","General"}
                    };
                    string name = names.TryGetValue(kvp.Key, out string n) ? n : kvp.Key;
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
}
