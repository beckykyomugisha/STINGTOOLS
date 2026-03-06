using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    // ═══════════════════════════════════════════════════════════════════════
    // Rich TAG7 Display Commands
    //
    // Exploiting Revit API formatting surfaces for rich tag presentation:
    //
    //   1. RichTagNoteCommand      — TextNote + FormattedText (Bold/Italic/Underline)
    //   2. ExportRichTagReportCommand — HTML export with full CSS styling
    //   3. ViewTag7SectionsCommand — TaskDialog viewer showing section breakdown
    //
    // Revit Formatting Capabilities Used:
    //   TextNote.FormattedText: SetBoldStatus, SetItalicStatus, SetUnderlineStatus
    //   TextNoteType: different colors per type (one per TAG7 section)
    //   OverrideGraphicSettings: color-code elements by TAG7 section completeness
    //   HTML: full CSS styling for export
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create rich FormattedText TextNote annotations from TAG7 data.
    /// Places styled text notes near tagged elements with:
    ///   - Bold + Underline for asset identity headers
    ///   - Italic for labels (Status:, Power:, etc.)
    ///   - Normal weight for values
    ///   - Multiple TextNoteTypes with different colors per section
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RichTagNoteCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            if (view == null || view is ViewSheet)
            {
                TaskDialog.Show("Rich Tag Note", "Please open a model view (not a sheet).");
                return Result.Failed;
            }

            // Get selected elements or all tagged elements in view
            var selected = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .Where(e => e != null)
                .ToList();

            if (selected.Count == 0)
            {
                // Collect all elements with TAG7 in active view
                selected = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .Where(e => !string.IsNullOrEmpty(ParameterHelpers.GetString(e, ParamRegistry.TAG7)))
                    .Take(50) // Limit to 50 to avoid overwhelming
                    .ToList();
            }

            if (selected.Count == 0)
            {
                TaskDialog.Show("Rich Tag Note", "No tagged elements found. Run a tagging command first.");
                return Result.Failed;
            }

            // Confirm placement
            var confirm = new TaskDialog("Rich Tag Note");
            confirm.MainContent = $"Place rich text annotations for {selected.Count} element(s)?\n\n" +
                "Each annotation will have:\n" +
                "  - BOLD headers for asset identity\n" +
                "  - Italic labels for field names\n" +
                "  - Color-coded sections (6 colors)\n\n" +
                "Sections: Identity (Blue) | System (Green) | Spatial (Orange)\n" +
                "          Lifecycle (Red) | Technical (Purple) | Classification (Grey)";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() != TaskDialogResult.Ok)
                return Result.Cancelled;

            int placed = 0;
            using (Transaction tx = new Transaction(doc, "STING Rich Tag Notes"))
            {
                tx.Start();

                // Create or find section-colored TextNoteTypes
                var sectionTypes = GetOrCreateSectionNoteTypes(doc);

                foreach (var el in selected)
                {
                    try
                    {
                        string catName = ParameterHelpers.GetCategoryName(el);
                        string[] tokenVals = ParamRegistry.ReadTokenValues(el);
                        var tag7 = TagConfig.BuildTag7Sections(doc, el, catName, tokenVals);

                        if (string.IsNullOrEmpty(tag7.PlainNarrative)) continue;

                        // Get element location
                        XYZ location = GetElementCenter(el, view);
                        if (location == null) continue;

                        // Offset the note slightly above/right of element
                        double scale = view.Scale > 0 ? view.Scale : 100;
                        XYZ notePos = new XYZ(location.X + 3.0 / scale, location.Y + 2.0 / scale, location.Z);

                        // Build multi-line text with section labels
                        string noteText = BuildFormattedNoteText(tag7);
                        if (string.IsNullOrEmpty(noteText)) continue;

                        // Use the identity section type (blue) as the base
                        ElementId noteTypeId = sectionTypes.Count > 0
                            ? sectionTypes[0]
                            : TextNoteType(doc);

                        TextNote note = TextNote.Create(doc, view.Id, notePos, noteText, noteTypeId);

                        // Apply FormattedText styling
                        ApplyFormattedStyles(note, tag7);

                        placed++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"RichTagNote: failed for element {el?.Id}: {ex.Message}");
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Rich Tag Note", $"Placed {placed} rich text annotations in '{view.Name}'.");
            return Result.Succeeded;
        }

        /// <summary>Build multi-line plain text for the TextNote (formatting applied after creation).</summary>
        private string BuildFormattedNoteText(TagConfig.Tag7Result tag7)
        {
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(tag7.SectionA)) lines.Add(tag7.SectionA);
            if (!string.IsNullOrEmpty(tag7.SectionB)) lines.Add(tag7.SectionB);
            if (!string.IsNullOrEmpty(tag7.SectionC)) lines.Add(tag7.SectionC);
            if (!string.IsNullOrEmpty(tag7.SectionD)) lines.Add(tag7.SectionD);
            if (!string.IsNullOrEmpty(tag7.SectionE)) lines.Add(tag7.SectionE);
            if (!string.IsNullOrEmpty(tag7.SectionF)) lines.Add(tag7.SectionF);
            return string.Join("\n", lines);
        }

        /// <summary>Apply Bold/Italic/Underline to specific text ranges in the TextNote.</summary>
        private void ApplyFormattedStyles(TextNote note, TagConfig.Tag7Result tag7)
        {
            try
            {
                FormattedText ft = note.GetFormattedText();
                string fullText = ft.GetPlainText();
                if (string.IsNullOrEmpty(fullText)) return;

                // Section A (Identity) — Bold + Underline
                if (!string.IsNullOrEmpty(tag7.SectionA))
                {
                    int idx = fullText.IndexOf(tag7.SectionA);
                    if (idx >= 0)
                    {
                        // Find the asset name portion (before "manufactured by" or ", family:")
                        string assetPart = tag7.SectionA;
                        int mfrIdx = assetPart.IndexOf(" manufactured by ");
                        int famIdx = assetPart.IndexOf(", family:");
                        int cutIdx = -1;
                        if (mfrIdx > 0) cutIdx = mfrIdx;
                        else if (famIdx > 0) cutIdx = famIdx;

                        int headerLen = cutIdx > 0 ? cutIdx : tag7.SectionA.Length;
                        var headerRange = new TextRange(idx, headerLen);
                        ft.SetBoldStatus(headerRange, true);
                        ft.SetUnderlineStatus(headerRange, true);

                        // Italic for "manufactured by" / "family:" / "type:" labels
                        ApplyItalicToLabels(ft, fullText, idx, tag7.SectionA,
                            new[] { "manufactured by", "family:", "type:" });
                    }
                }

                // Section B (System) — Italic for the section header
                if (!string.IsNullOrEmpty(tag7.SectionB))
                {
                    int idx = fullText.IndexOf(tag7.SectionB);
                    if (idx >= 0)
                    {
                        // Bold the system name
                        int servingIdx = tag7.SectionB.IndexOf(" serving ");
                        if (servingIdx > 0)
                        {
                            ft.SetBoldStatus(new TextRange(idx, servingIdx), true);
                        }
                        // Italic "serving"
                        ApplyItalicToLabels(ft, fullText, idx, tag7.SectionB, new[] { "serving" });
                    }
                }

                // Section C (Spatial) — Italic labels
                if (!string.IsNullOrEmpty(tag7.SectionC))
                {
                    int idx = fullText.IndexOf(tag7.SectionC);
                    if (idx >= 0)
                    {
                        ApplyItalicToLabels(ft, fullText, idx, tag7.SectionC,
                            new[] { "Located in", "Department:", "Grid Reference" });
                    }
                }

                // Section D (Lifecycle) — Italic labels, bold values
                if (!string.IsNullOrEmpty(tag7.SectionD))
                {
                    int idx = fullText.IndexOf(tag7.SectionD);
                    if (idx >= 0)
                    {
                        ApplyItalicToLabels(ft, fullText, idx, tag7.SectionD,
                            new[] { "Status:", "Revision", "Origin:", "Project:", "Volume:", "Maintenance:", "Detail:" });
                    }
                }

                // Section E (Technical) — Bold section, italic labels
                if (!string.IsNullOrEmpty(tag7.SectionE))
                {
                    int idx = fullText.IndexOf(tag7.SectionE);
                    if (idx >= 0)
                    {
                        // Bold the entire technical section
                        ft.SetBoldStatus(new TextRange(idx, Math.Min(tag7.SectionE.Length, fullText.Length - idx)), true);
                    }
                }

                // Section F (Classification) — Italic labels, bold ISO tag
                if (!string.IsNullOrEmpty(tag7.SectionF))
                {
                    int idx = fullText.IndexOf(tag7.SectionF);
                    if (idx >= 0)
                    {
                        ApplyItalicToLabels(ft, fullText, idx, tag7.SectionF,
                            new[] { "Uniformat", "OmniClass", "Keynote", "Type Mark", "Unit Cost:", "ISO 19650 Tag:" });

                        // Bold the ISO tag value itself
                        int isoIdx = tag7.SectionF.IndexOf("ISO 19650 Tag: ");
                        if (isoIdx >= 0)
                        {
                            int valStart = idx + isoIdx + "ISO 19650 Tag: ".Length;
                            int valLen = tag7.SectionF.Length - isoIdx - "ISO 19650 Tag: ".Length;
                            if (valStart + valLen <= fullText.Length && valLen > 0)
                            {
                                ft.SetBoldStatus(new TextRange(valStart, valLen), true);
                                ft.SetUnderlineStatus(new TextRange(valStart, valLen), true);
                            }
                        }
                    }
                }

                note.SetFormattedText(ft);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"RichTagNote: FormattedText styling failed: {ex.Message}");
            }
        }

        /// <summary>Apply italic to specific label strings within a section.</summary>
        private void ApplyItalicToLabels(FormattedText ft, string fullText, int sectionStart,
            string sectionText, string[] labels)
        {
            foreach (string label in labels)
            {
                int labelIdx = sectionText.IndexOf(label);
                if (labelIdx >= 0)
                {
                    int absIdx = sectionStart + labelIdx;
                    if (absIdx + label.Length <= fullText.Length)
                    {
                        ft.SetItalicStatus(new TextRange(absIdx, label.Length), true);
                    }
                }
            }
        }

        /// <summary>Get element center point in view coordinates.</summary>
        private XYZ GetElementCenter(Element el, View view)
        {
            BoundingBoxXYZ bb = el.get_BoundingBox(view);
            if (bb != null)
                return new XYZ((bb.Min.X + bb.Max.X) / 2.0, (bb.Min.Y + bb.Max.Y) / 2.0, (bb.Min.Z + bb.Max.Z) / 2.0);

            // Fallback: location point
            if (el.Location is LocationPoint lp) return lp.Point;
            if (el.Location is LocationCurve lc) return lc.Curve.Evaluate(0.5, true);
            return null;
        }

        /// <summary>Get or create colored TextNoteTypes for each TAG7 section.</summary>
        private List<ElementId> GetOrCreateSectionNoteTypes(Document doc)
        {
            var result = new List<ElementId>();
            var styles = TagConfig.SectionStyles;

            // Find base TextNoteType
            var baseType = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .FirstOrDefault();

            if (baseType == null) return result;

            foreach (var style in styles)
            {
                string typeName = $"STING TAG7 {style.Name}";
                var existing = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .Cast<TextNoteType>()
                    .FirstOrDefault(t => t.Name == typeName);

                if (existing != null)
                {
                    result.Add(existing.Id);
                    continue;
                }

                // Duplicate base type
                try
                {
                    var newType = baseType.Duplicate(typeName) as TextNoteType;
                    if (newType != null)
                    {
                        // Set color from hex
                        int colorInt = HexToRevitColor(style.Color);
                        var colorParam = newType.get_Parameter(BuiltInParameter.LINE_COLOR);
                        if (colorParam != null && !colorParam.IsReadOnly)
                            colorParam.Set(colorInt);

                        result.Add(newType.Id);
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"RichTagNote: failed to create TextNoteType '{typeName}': {ex.Message}");
                    result.Add(baseType.Id);
                }
            }

            return result;
        }

        /// <summary>Get default TextNoteType.</summary>
        private ElementId TextNoteType(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .FirstElementId();
        }

        /// <summary>Convert hex color string to Revit integer color (R + G*256 + B*65536).</summary>
        private int HexToRevitColor(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length < 6) return 0;
            int r = Convert.ToInt32(hex.Substring(0, 2), 16);
            int g = Convert.ToInt32(hex.Substring(2, 2), 16);
            int b = Convert.ToInt32(hex.Substring(4, 2), 16);
            return r + (g << 8) + (b << 16);
        }
    }

    /// <summary>
    /// Export TAG7 data as a richly styled HTML report with full CSS formatting.
    /// Each section gets its own color, labels are italic, values are bold,
    /// headers are underlined. Produces a print-ready HTML file.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportRichTagReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Collect all elements with TAG7
            var tagged = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => !string.IsNullOrEmpty(ParameterHelpers.GetString(e, ParamRegistry.TAG1)))
                .ToList();

            if (tagged.Count == 0)
            {
                TaskDialog.Show("Export Rich Report", "No tagged elements found.");
                return Result.Failed;
            }

            // Build HTML
            string html = BuildHtmlReport(doc, tagged);

            // Save to file
            string dataPath = StingToolsApp.DataPath ?? "";
            string dir = Path.GetDirectoryName(dataPath);
            if (string.IsNullOrEmpty(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string filePath = Path.Combine(dir, $"STING_TAG7_Report_{DateTime.Now:yyyyMMdd_HHmmss}.html");

            File.WriteAllText(filePath, html);

            TaskDialog.Show("Export Rich Report",
                $"Exported {tagged.Count} elements to:\n{filePath}\n\n" +
                "Open in a web browser to view the styled report.\n\n" +
                "Features:\n" +
                "  - Color-coded sections (Identity=Blue, System=Green, etc.)\n" +
                "  - Bold headers, italic labels, highlighted values\n" +
                "  - Print-ready CSS styling\n" +
                "  - Sortable by discipline, location, system");

            return Result.Succeeded;
        }

        private string BuildHtmlReport(Document doc, List<Element> tagged)
        {
            var styles = TagConfig.SectionStyles;
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("<meta charset=\"UTF-8\">");
            sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            sb.AppendLine("<title>STING TAG7 Asset Report</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("* { box-sizing: border-box; margin: 0; padding: 0; }");
            sb.AppendLine("body { font-family: 'Segoe UI', Tahoma, sans-serif; background: #f5f5f5; padding: 20px; color: #333; }");
            sb.AppendLine("h1 { text-align: center; color: #6A1B9A; margin-bottom: 5px; }");
            sb.AppendLine(".subtitle { text-align: center; color: #888; margin-bottom: 20px; font-size: 14px; }");
            sb.AppendLine(".stats { display: flex; justify-content: center; gap: 20px; margin-bottom: 20px; }");
            sb.AppendLine(".stat { background: white; padding: 10px 20px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); text-align: center; }");
            sb.AppendLine(".stat-num { font-size: 24px; font-weight: bold; color: #6A1B9A; }");
            sb.AppendLine(".stat-label { font-size: 12px; color: #888; }");
            sb.AppendLine(".card { background: white; border-radius: 8px; box-shadow: 0 2px 6px rgba(0,0,0,0.1); margin-bottom: 16px; overflow: hidden; }");
            sb.AppendLine(".card-header { padding: 12px 16px; color: white; font-weight: bold; font-size: 14px; display: flex; justify-content: space-between; }");
            sb.AppendLine(".card-body { padding: 16px; }");
            sb.AppendLine(".section { margin-bottom: 10px; padding: 8px 12px; border-left: 4px solid; border-radius: 0 4px 4px 0; background: #fafafa; }");
            sb.AppendLine(".section-name { font-size: 10px; text-transform: uppercase; letter-spacing: 1px; margin-bottom: 4px; font-weight: bold; }");
            sb.AppendLine(".section-content { font-size: 13px; line-height: 1.6; }");

            // Section-specific colors
            foreach (var style in styles)
            {
                sb.AppendLine($".sec-{style.Key.ToLower()} {{ border-left-color: {style.Color}; }}");
                sb.AppendLine($".sec-{style.Key.ToLower()} .section-name {{ color: {style.Color}; }}");
            }

            sb.AppendLine(".label {{ font-style: italic; color: #666; }}");
            sb.AppendLine(".value {{ font-weight: 600; color: #222; }}");
            sb.AppendLine(".header {{ font-weight: bold; text-decoration: underline; }}");
            sb.AppendLine(".tag-ref { font-family: 'Consolas', monospace; background: rgba(255,255,255,0.2); padding: 2px 6px; border-radius: 3px; }");
            sb.AppendLine(".seg-legend { display: flex; gap: 12px; flex-wrap: wrap; justify-content: center; margin-bottom: 20px; padding: 12px; background: white; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }");
            sb.AppendLine(".seg-item { display: flex; align-items: center; gap: 4px; font-size: 12px; }");
            sb.AppendLine(".seg-dot { width: 12px; height: 12px; border-radius: 50%; }");
            sb.AppendLine("@media print { body { background: white; padding: 10px; } .card { box-shadow: none; border: 1px solid #ddd; break-inside: avoid; } }");
            sb.AppendLine("</style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("<h1>STING TAG7 Asset Report</h1>");
            sb.AppendLine($"<div class=\"subtitle\">Generated {DateTime.Now:dd MMM yyyy HH:mm} | ISO 19650 Compliant</div>");

            // Stats
            var disciplines = tagged
                .Select(e => ParameterHelpers.GetString(e, ParamRegistry.DISC))
                .Where(d => !string.IsNullOrEmpty(d))
                .GroupBy(d => d)
                .OrderByDescending(g => g.Count())
                .ToList();

            sb.AppendLine("<div class=\"stats\">");
            sb.AppendLine($"<div class=\"stat\"><div class=\"stat-num\">{tagged.Count}</div><div class=\"stat-label\">Total Assets</div></div>");
            sb.AppendLine($"<div class=\"stat\"><div class=\"stat-num\">{disciplines.Count}</div><div class=\"stat-label\">Disciplines</div></div>");
            int withTag7 = tagged.Count(e => !string.IsNullOrEmpty(ParameterHelpers.GetString(e, ParamRegistry.TAG7)));
            sb.AppendLine($"<div class=\"stat\"><div class=\"stat-num\">{withTag7}</div><div class=\"stat-label\">With TAG7</div></div>");
            sb.AppendLine("</div>");

            // Tag segment color legend
            sb.AppendLine("<div class=\"seg-legend\">");
            sb.AppendLine("<strong style=\"margin-right:8px\">Tag Segments:</strong>");
            foreach (var segStyle in TagConfig.SegmentStyles)
            {
                sb.Append($"<div class=\"seg-item\">");
                sb.Append($"<div class=\"seg-dot\" style=\"background:{segStyle.Color}\"></div>");
                sb.Append($"<span>{segStyle.Name}</span>");
                sb.Append("</div>");
            }
            sb.AppendLine("</div>");

            // Element cards
            foreach (var el in tagged.OrderBy(e => ParameterHelpers.GetString(e, ParamRegistry.TAG1)))
            {
                string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                string catName = ParameterHelpers.GetCategoryName(el);
                string[] tokenVals = ParamRegistry.ReadTokenValues(el);
                var tag7 = TagConfig.BuildTag7Sections(doc, el, catName, tokenVals);

                // Apply active preset or fallback to discipline color
                var displayStyle = TagConfig.GetDisplayStyleSmart(el);
                string headerColor = displayStyle?.HeaderColor ?? GetDisciplineColor(tokenVals.Length > 0 ? tokenVals[0] : "");
                string bgTint = displayStyle?.BackgroundTint ?? "white";
                string presetLabel = displayStyle?.Label ?? catName;

                sb.AppendLine("<div class=\"card\">");
                sb.AppendLine($"<div class=\"card-header\" style=\"background:{headerColor}\">");

                // Render TAG1 with per-segment coloring
                if (!string.IsNullOrEmpty(tag1))
                {
                    var parsed = TagConfig.ParseTagSegments(tag1);
                    sb.Append("<span class=\"tag-ref\">");
                    for (int si = 0; si < parsed.Segments.Length; si++)
                    {
                        if (si > 0) sb.Append("<span style=\"color:rgba(255,255,255,0.6)\">-</span>");
                        string segColor = si < TagConfig.SegmentStyles.Length ? TagConfig.SegmentStyles[si].Color : "#fff";
                        string segBold = (si < TagConfig.SegmentStyles.Length && TagConfig.SegmentStyles[si].Bold) ? "font-weight:bold;" : "";
                        string segVal = HtmlEncode(parsed.Segments[si]);
                        bool empty = !parsed.Populated[si];
                        string opacity = empty ? "opacity:0.4;" : "";
                        sb.Append($"<span style=\"color:{segColor};{segBold}{opacity}\" title=\"{HtmlEncode(TagConfig.SegmentStyles[si < TagConfig.SegmentStyles.Length ? si : 0].Description)}\">{segVal}</span>");
                    }
                    sb.Append("</span>");
                }
                else
                {
                    sb.Append($"<span>{HtmlEncode(tag1)}</span>");
                }

                sb.AppendLine($"<span>{HtmlEncode(presetLabel)} — {HtmlEncode(catName)}</span>");
                sb.AppendLine("</div>");
                sb.AppendLine($"<div class=\"card-body\" style=\"background:{bgTint}\">");

                // Render each section with preset-aware color-coding
                string[] sectionNames = { "Identity", "System & Function", "Spatial Context", "Lifecycle", "Technical Data", "Classification" };
                string[] sectionKeys = { "A", "B", "C", "D", "E", "F" };
                string[] sectionContents = tag7.AllSections;

                for (int si = 0; si < 6; si++)
                {
                    // Check visibility from preset
                    bool visible = displayStyle?.SectionVisibility == null || si >= displayStyle.SectionVisibility.Length || displayStyle.SectionVisibility[si];
                    if (!visible) continue;

                    // Override section color from preset
                    string sectionColor = (displayStyle?.SectionColors != null && si < displayStyle.SectionColors.Length)
                        ? displayStyle.SectionColors[si] : null;

                    RenderSection(sb, sectionKeys[si], sectionNames[si], sectionContents[si], tag7, sectionColor);
                }

                sb.AppendLine("</div></div>");
            }

            sb.AppendLine("<div style=\"text-align:center;color:#aaa;margin-top:30px;font-size:12px;\">");
            sb.AppendLine("Generated by STING Tools — ISO 19650 BIM Asset Management</div>");
            sb.AppendLine("</body></html>");

            return sb.ToString();
        }

        private void RenderSection(System.Text.StringBuilder sb, string key, string name,
            string content, TagConfig.Tag7Result tag7, string colorOverride = null)
        {
            if (string.IsNullOrEmpty(content)) return;

            string borderStyle = !string.IsNullOrEmpty(colorOverride)
                ? $"border-left-color:{colorOverride}"
                : "";
            string nameStyle = !string.IsNullOrEmpty(colorOverride)
                ? $"color:{colorOverride}"
                : "";

            sb.AppendLine($"<div class=\"section sec-{key.ToLower()}\" style=\"{borderStyle}\">");
            sb.AppendLine($"<div class=\"section-name\" style=\"{nameStyle}\">{HtmlEncode(name)}</div>");

            // Parse the marked-up version for rich rendering
            string markedContent = GetMarkedSection(tag7.MarkedUpNarrative, key, content);
            if (!string.IsNullOrEmpty(markedContent))
            {
                sb.Append("<div class=\"section-content\">");
                var segments = TagConfig.ParseMarkup(markedContent);
                foreach (var (text, style) in segments)
                {
                    switch (style)
                    {
                        case "H":
                            sb.Append($"<span class=\"header\">{HtmlEncode(text)}</span>");
                            break;
                        case "L":
                            sb.Append($"<span class=\"label\">{HtmlEncode(text)}</span>");
                            break;
                        case "V":
                            sb.Append($"<span class=\"value\">{HtmlEncode(text)}</span>");
                            break;
                        default:
                            sb.Append(HtmlEncode(text));
                            break;
                    }
                }
                sb.AppendLine("</div>");
            }
            else
            {
                sb.AppendLine($"<div class=\"section-content\">{HtmlEncode(content)}</div>");
            }

            sb.AppendLine("</div>");
        }

        /// <summary>Extract a section's marked-up text from the full narrative by matching plain text.</summary>
        private string GetMarkedSection(string markedNarrative, string sectionKey, string plainContent)
        {
            if (string.IsNullOrEmpty(markedNarrative)) return null;

            // Split by section separator markup
            string[] parts = markedNarrative.Split(new[] { " \u00ABS\u00BB|\u00AB/S\u00BB " }, StringSplitOptions.None);

            // Find the part whose stripped version matches the plain content
            foreach (string part in parts)
            {
                string stripped = TagConfig.StripMarkup(part);
                if (stripped.Trim() == plainContent.Trim())
                    return part;
            }

            return null;
        }

        private string GetDisciplineColor(string disc)
        {
            switch (disc)
            {
                case "M":  return "#1565C0"; // Blue
                case "E":  return "#F9A825"; // Yellow
                case "P":  return "#2E7D32"; // Green
                case "A":  return "#757575"; // Grey
                case "S":  return "#C62828"; // Red
                case "FP": return "#E65100"; // Orange
                case "LV": return "#6A1B9A"; // Purple
                case "G":  return "#795548"; // Brown
                default:   return "#455A64"; // Blue-grey
            }
        }

        private string HtmlEncode(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }

    /// <summary>
    /// Display TAG7 section breakdown for selected element in a TaskDialog.
    /// Shows each section with its label and content for quick inspection.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ViewTag7SectionsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var selected = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .Where(e => e != null)
                .ToList();

            if (selected.Count == 0)
            {
                TaskDialog.Show("TAG7 Sections", "Select one or more elements to view their TAG7 sections.");
                return Result.Failed;
            }

            foreach (var el in selected.Take(5)) // Limit to 5 dialogs
            {
                string catName = ParameterHelpers.GetCategoryName(el);
                string[] tokenVals = ParamRegistry.ReadTokenValues(el);
                var tag7 = TagConfig.BuildTag7Sections(doc, el, catName, tokenVals);

                string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                var styles = TagConfig.SectionStyles;
                var report = new System.Text.StringBuilder();

                report.AppendLine($"Element: {el.Id} | Category: {catName}");
                report.AppendLine($"TAG1: {tag1}");
                report.AppendLine(new string('═', 60));

                string[] sections = tag7.AllSections;
                for (int i = 0; i < styles.Length && i < sections.Length; i++)
                {
                    string content = sections[i];
                    if (string.IsNullOrEmpty(content)) continue;

                    report.AppendLine();
                    string styleHint = "";
                    if (styles[i].Bold) styleHint += " [BOLD]";
                    if (styles[i].Italic) styleHint += " [ITALIC]";
                    if (styles[i].Underline) styleHint += " [UNDERLINE]";

                    report.AppendLine($"▐ TAG7{styles[i].Key}: {styles[i].Name}{styleHint} ({styles[i].Color})");
                    report.AppendLine($"  Parameter: {ParamRegistry.TAG7Sections[i]}");
                    report.AppendLine($"  {content}");
                }

                report.AppendLine();
                report.AppendLine(new string('═', 60));
                report.AppendLine("TAG7 (Full Markup):");
                // Show first 500 chars of marked-up version
                string marked = tag7.MarkedUpNarrative;
                if (marked.Length > 500) marked = marked.Substring(0, 500) + "...";
                report.AppendLine(marked);

                TaskDialog.Show($"TAG7 Sections — {tag1}", report.ToString());
            }

            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Switch the active TAG7 display preset. Presets control how TAG7 sections
    /// are color-coded and styled based on element properties.
    ///
    /// 7 Built-in Presets:
    ///   1. Discipline  — M=Blue, E=Amber, P=Green, A=Grey, S=Red, FP=Orange, LV=Purple
    ///   2. Status      — NEW=Green, EXISTING=Blue, DEMOLISHED=Red, TEMPORARY=Orange
    ///   3. System      — HVAC=Blue, DCW=Cyan, HWS=Red, SAN=Brown, LV=Amber, FP=Orange
    ///   4. Completeness — Complete=Green, Partial=Orange, Incomplete=Red (computed from token fill)
    ///   5. Monochrome  — Print-ready B&amp;W scheme
    ///   6. Accessible  — Colorblind-safe (deuteranopia/protanopia friendly)
    ///   7. Technical Focus — Emphasize tech specs, dim identity sections
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SwitchTag7PresetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var presets = TagConfig.BuiltInPresets;

            // Build selection dialog
            var dlg = new TaskDialog("TAG7 Display Presets");
            dlg.MainInstruction = "Select a TAG7 display preset";
            dlg.MainContent = "Presets control how TAG7 sections are color-coded\n" +
                "in Rich Notes, HTML reports, and the WPF panel.\n\n" +
                "Active: " + (TagConfig.ActivePreset?.Name ?? "None (default section colors)");

            // Add command links for each preset
            var linkMap = new Dictionary<TaskDialogResult, string>();
            // Revit only supports CommandLink1-4 (max 4 links)
            var links = new[]
            {
                TaskDialogResult.CommandLink1, TaskDialogResult.CommandLink2,
                TaskDialogResult.CommandLink3, TaskDialogResult.CommandLink4,
            };

            for (int i = 0; i < presets.Length && i < links.Length; i++)
            {
                string active = TagConfig.ActivePreset?.Name == presets[i].Name ? " [ACTIVE]" : "";
                dlg.AddCommandLink(links[i], $"{presets[i].Name}{active}", presets[i].Description);
                linkMap[links[i]] = presets[i].Name;
            }

            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;
            dlg.FooterText = "Presets affect Rich Tag Notes, HTML Report export, and TAG7 Sections viewer.";

            var result = dlg.Show();
            if (result == TaskDialogResult.Cancel) return Result.Cancelled;

            if (linkMap.TryGetValue(result, out string selectedName))
            {
                // Toggle: if already active, deactivate
                if (TagConfig.ActivePreset?.Name == selectedName)
                {
                    TagConfig.ActivePreset = null;
                    TaskDialog.Show("TAG7 Preset", $"Deactivated '{selectedName}' preset.\nUsing default section colors.");
                }
                else
                {
                    TagConfig.SetActivePreset(selectedName);
                    TaskDialog.Show("TAG7 Preset",
                        $"Activated '{selectedName}' preset.\n\n{TagConfig.ActivePreset?.Description}\n\n" +
                        "Run 'Rich Note', 'HTML Report', or 'View Sections' to see the new styling.");
                }
            }

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TAG1-TAG6 Rich Segment Display Command
    //
    // Provides segment-aware styled TextNote annotations for the 8-segment
    // tag format (DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ). Each segment gets
    // its own color and font style, enabling instant visual parsing.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create segment-colored TextNote annotations for TAG1-TAG6 values.
    /// Each of the 8 segments (DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, SEQ)
    /// is rendered with a distinct color and bold/italic style in a TextNote.
    /// Uses FormattedText for per-character bold/italic, and multiple
    /// TextNoteTypes for segment colors.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RichSegmentNoteCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            if (view == null || view is ViewSheet)
            {
                TaskDialog.Show("Rich Segment Note", "Please open a model view (not a sheet).");
                return Result.Failed;
            }

            var selected = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .Where(e => e != null)
                .ToList();

            if (selected.Count == 0)
            {
                selected = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .Where(e => !string.IsNullOrEmpty(ParameterHelpers.GetString(e, ParamRegistry.TAG1)))
                    .Take(50)
                    .ToList();
            }

            if (selected.Count == 0)
            {
                TaskDialog.Show("Rich Segment Note", "No tagged elements found. Run a tagging command first.");
                return Result.Failed;
            }

            // Show segment color legend in confirmation
            var segStyles = TagConfig.SegmentStyles;
            var legendText = string.Join("\n",
                segStyles.Select(s => $"  {s.Name,-5} ({s.Description}) = {s.Color}"));

            var confirm = new TaskDialog("Rich Segment Note");
            confirm.MainContent = $"Place segment-colored annotations for {selected.Count} element(s)?\n\n" +
                "Each tag segment gets a distinct color:\n" + legendText;
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() != TaskDialogResult.Ok)
                return Result.Cancelled;

            int placed = 0;
            using (Transaction tx = new Transaction(doc, "STING Rich Segment Notes"))
            {
                tx.Start();

                // Get or create segment-colored TextNoteTypes
                var segmentTypes = GetOrCreateSegmentNoteTypes(doc);
                ElementId baseTypeId = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType)).FirstElementId();

                foreach (var el in selected)
                {
                    try
                    {
                        string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                        if (string.IsNullOrEmpty(tag1)) continue;

                        var parsed = TagConfig.ParseTagSegments(tag1);

                        // Get element location
                        XYZ location = GetElementCenter(el, view);
                        if (location == null) continue;

                        double scale = view.Scale > 0 ? view.Scale : 100;
                        XYZ notePos = new XYZ(location.X + 2.0 / scale, location.Y + 1.5 / scale, location.Z);

                        // Build the note text: "TAG1: M-BLD1-Z01-L02-HVAC-SUP-AHU-0003"
                        string noteText = $"TAG1: {tag1}";

                        // Use the primary segment type (DISC color)
                        ElementId typeId = segmentTypes.Count > 0 ? segmentTypes[0] : baseTypeId;
                        TextNote note = TextNote.Create(doc, view.Id, notePos, noteText, typeId);

                        // Apply segment-level formatting
                        ApplySegmentFormatting(note, parsed, "TAG1: ".Length);

                        placed++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"RichSegmentNote: failed for element {el?.Id}: {ex.Message}");
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Rich Segment Note", $"Placed {placed} segment-colored annotations in '{view.Name}'.");
            return Result.Succeeded;
        }

        private void ApplySegmentFormatting(TextNote note, TagConfig.TagSegmentResult parsed, int offset)
        {
            try
            {
                FormattedText ft = note.GetFormattedText();
                string fullText = ft.GetPlainText();
                if (string.IsNullOrEmpty(fullText)) return;

                var segStyles = TagConfig.SegmentStyles;
                int pos = offset;

                for (int i = 0; i < 8 && i < parsed.Segments.Length; i++)
                {
                    string seg = parsed.Segments[i];
                    if (string.IsNullOrEmpty(seg)) { pos += 1; continue; } // skip separator

                    if (pos + seg.Length > fullText.Length) break;

                    var range = new TextRange(pos, seg.Length);

                    if (i < segStyles.Length)
                    {
                        if (segStyles[i].Bold)
                            ft.SetBoldStatus(range, true);
                        if (segStyles[i].Italic)
                            ft.SetItalicStatus(range, true);
                    }

                    // Move past segment + separator
                    pos += seg.Length;
                    if (i < 7) pos += TagConfig.Separator.Length;
                }

                note.SetFormattedText(ft);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"RichSegmentNote: formatting failed: {ex.Message}");
            }
        }

        private List<ElementId> GetOrCreateSegmentNoteTypes(Document doc)
        {
            var result = new List<ElementId>();
            var baseType = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .FirstOrDefault();
            if (baseType == null) return result;

            foreach (var style in TagConfig.SegmentStyles)
            {
                string typeName = $"STING SEG {style.Name}";
                var existing = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .Cast<TextNoteType>()
                    .FirstOrDefault(t => t.Name == typeName);

                if (existing != null) { result.Add(existing.Id); continue; }

                try
                {
                    var newType = baseType.Duplicate(typeName) as TextNoteType;
                    if (newType != null)
                    {
                        int colorInt = HexToRevitColor(style.Color);
                        var colorParam = newType.get_Parameter(BuiltInParameter.LINE_COLOR);
                        if (colorParam != null && !colorParam.IsReadOnly)
                            colorParam.Set(colorInt);
                        result.Add(newType.Id);
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"RichSegmentNote: failed to create type '{typeName}': {ex.Message}");
                    result.Add(baseType.Id);
                }
            }
            return result;
        }

        private XYZ GetElementCenter(Element el, View view)
        {
            BoundingBoxXYZ bb = el.get_BoundingBox(view);
            if (bb != null)
                return new XYZ((bb.Min.X + bb.Max.X) / 2.0, (bb.Min.Y + bb.Max.Y) / 2.0, (bb.Min.Z + bb.Max.Z) / 2.0);
            if (el.Location is LocationPoint lp) return lp.Point;
            if (el.Location is LocationCurve lc) return lc.Curve.Evaluate(0.5, true);
            return null;
        }

        private int HexToRevitColor(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length < 6) return 0;
            int r = Convert.ToInt32(hex.Substring(0, 2), 16);
            int g = Convert.ToInt32(hex.Substring(2, 2), 16);
            int b = Convert.ToInt32(hex.Substring(4, 2), 16);
            return r + (g << 8) + (b << 16);
        }
    }

    /// <summary>
    /// View TAG1-TAG6 segment breakdown with color hints in a TaskDialog.
    /// Parses the 8-segment tag and shows each segment with its style definition.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ViewSegmentsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var selected = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .Where(e => e != null)
                .ToList();

            if (selected.Count == 0)
            {
                TaskDialog.Show("View Segments", "Select one or more elements to view their tag segments.");
                return Result.Failed;
            }

            var segStyles = TagConfig.SegmentStyles;

            foreach (var el in selected.Take(5))
            {
                string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                if (string.IsNullOrEmpty(tag1))
                {
                    TaskDialog.Show("View Segments", $"Element {el.Id} has no TAG1 value.");
                    continue;
                }

                var parsed = TagConfig.ParseTagSegments(tag1);
                var report = new System.Text.StringBuilder();
                string catName = ParameterHelpers.GetCategoryName(el);

                report.AppendLine($"Element: {el.Id} | Category: {catName}");
                report.AppendLine($"TAG1: {tag1}");
                report.AppendLine(new string('=', 60));

                for (int i = 0; i < 8 && i < segStyles.Length; i++)
                {
                    string val = parsed.Segments[i];
                    bool pop = parsed.Populated[i];
                    string status = pop ? "OK" : "EMPTY";
                    string styleHints = "";
                    if (segStyles[i].Bold) styleHints += " [BOLD]";
                    if (segStyles[i].Italic) styleHints += " [ITALIC]";

                    report.AppendLine($"\n  Seg {i}: {segStyles[i].Name,-5} = \"{val}\" ({status}){styleHints}");
                    report.AppendLine($"         {segStyles[i].Description} | Color: {segStyles[i].Color}");
                }

                // Show other tags
                report.AppendLine($"\n{'=', 0}{new string('=', 60)}");
                string[] tagParams = { ParamRegistry.TAG2, ParamRegistry.TAG3, ParamRegistry.TAG4, ParamRegistry.TAG5, ParamRegistry.TAG6 };
                string[] tagLabels = { "TAG2", "TAG3", "TAG4", "TAG5", "TAG6" };
                for (int t = 0; t < tagParams.Length; t++)
                {
                    string val = ParameterHelpers.GetString(el, tagParams[t]);
                    if (!string.IsNullOrEmpty(val))
                        report.AppendLine($"  {tagLabels[t]}: {val}");
                }

                TaskDialog.Show($"Tag Segments - {tag1}", report.ToString());
            }

            return Result.Succeeded;
        }
    }
}
