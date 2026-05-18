using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Docs
{
    // ════════════════════════════════════════════════════════════════════════════
    //  SHEET TEMPLATE ENGINE — Phase 3
    //  Sheet templates, ISO 19650 compliance checking, viewport grid alignment,
    //  and batch print/export integration.
    // ════════════════════════════════════════════════════════════════════════════

    // ─────────────────────────────────────────────────────────────────────
    //  DATA MODELS
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A sheet template defines a reusable sheet configuration with
    /// pre-defined viewport slots, title block, and naming rules.
    /// </summary>
    internal class SheetTemplate
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("discipline")] public string Discipline { get; set; }
        [JsonProperty("paperSize")] public string PaperSize { get; set; }
        [JsonProperty("titleBlockFamily")] public string TitleBlockFamily { get; set; }
        [JsonProperty("sheetNumberPattern")] public string SheetNumberPattern { get; set; } // e.g. "{DISC}-{SEQ:D3}"
        [JsonProperty("sheetNamePattern")] public string SheetNamePattern { get; set; }   // e.g. "{DISC} {VIEWTYPE} - {LEVEL}"
        [JsonProperty("viewportSlots")] public List<TemplateViewSlot> ViewportSlots { get; set; } = new List<TemplateViewSlot>();
        [JsonProperty("created")] public string Created { get; set; }
    }

    /// <summary>
    /// A slot within a sheet template defining where a view should be placed.
    /// </summary>
    internal class TemplateViewSlot
    {
        [JsonProperty("label")] public string Label { get; set; }           // e.g. "Main Plan", "Section A"
        [JsonProperty("viewType")] public string ViewType { get; set; }     // FloorPlan, Section, Elevation, etc.
        [JsonProperty("normX")] public double NormX { get; set; }
        [JsonProperty("normY")] public double NormY { get; set; }
        [JsonProperty("normW")] public double NormW { get; set; }
        [JsonProperty("normH")] public double NormH { get; set; }
        [JsonProperty("preferredScale")] public int PreferredScale { get; set; }
        [JsonProperty("viewportTypeName")] public string ViewportTypeName { get; set; }
        [JsonProperty("required")] public bool Required { get; set; } = true;
    }

    /// <summary>
    /// Sheet template library stored per project.
    /// </summary>
    internal class SheetTemplateLibrary
    {
        [JsonProperty("version")] public string Version { get; set; } = "1.0";
        [JsonProperty("templates")] public List<SheetTemplate> Templates { get; set; } = new List<SheetTemplate>();
    }

    /// <summary>
    /// Result of an ISO 19650 sheet compliance check.
    /// </summary>
    internal class SheetComplianceResult
    {
        public string SheetNumber { get; set; }
        public string SheetName { get; set; }
        public List<string> Issues { get; set; } = new List<string>();
        public bool IsCompliant => Issues.Count == 0;
        public string Status => IsCompliant ? "PASS" : "FAIL";
    }

    /// <summary>
    /// Viewport alignment grid for snapping viewport positions.
    /// </summary>
    internal class AlignmentGrid
    {
        public double CellWidthFt { get; set; }
        public double CellHeightFt { get; set; }
        public XYZ Origin { get; set; }

        /// <summary>Snap a point to the nearest grid intersection.</summary>
        public XYZ Snap(XYZ point)
        {
            double sx = Math.Round((point.X - Origin.X) / CellWidthFt) * CellWidthFt + Origin.X;
            double sy = Math.Round((point.Y - Origin.Y) / CellHeightFt) * CellHeightFt + Origin.Y;
            return new XYZ(sx, sy, 0);
        }
    }

    /// <summary>
    /// Batch print configuration.
    /// </summary>
    internal class PrintBatchConfig
    {
        public string OutputDirectory { get; set; }
        public string FileNamePattern { get; set; } = "{NUMBER} - {NAME}";
        public bool CombineIntoOne { get; set; }
        public List<string> SheetNumbers { get; set; } = new List<string>();
        public string PrinterName { get; set; }
    }


    /// <summary>
    /// Sheet Template Engine — sheet templates, ISO compliance, grid alignment, print.
    /// </summary>
    internal static class SheetTemplateEngine
    {
        private const double MmToFeet = 1.0 / 304.8;

        // ═══════════════════════════════════════════════════════════════════
        //  1. SHEET TEMPLATE SYSTEM
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Get built-in sheet templates for common AEC sheet types.
        /// </summary>
        internal static List<SheetTemplate> GetBuiltInTemplates()
        {
            return new List<SheetTemplate>
            {
                new SheetTemplate
                {
                    Name = "Single Plan",
                    Description = "One floor plan centred on A1 sheet",
                    Discipline = "A", PaperSize = "A1",
                    SheetNumberPattern = "{DISC}-{SEQ:D3}",
                    SheetNamePattern = "{LEVEL} Plan",
                    ViewportSlots = new List<TemplateViewSlot>
                    {
                        new TemplateViewSlot { Label = "Main Plan", ViewType = "FloorPlan",
                            NormX = 0.47, NormY = 0.52, NormW = 0.80, NormH = 0.82, PreferredScale = 50 }
                    }
                },
                new SheetTemplate
                {
                    Name = "Plan + Sections",
                    Description = "Floor plan with two cross-sections",
                    Discipline = "A", PaperSize = "A1",
                    SheetNumberPattern = "{DISC}-{SEQ:D3}",
                    SheetNamePattern = "{LEVEL} Plan & Sections",
                    ViewportSlots = new List<TemplateViewSlot>
                    {
                        new TemplateViewSlot { Label = "Main Plan", ViewType = "FloorPlan",
                            NormX = 0.47, NormY = 0.65, NormW = 0.80, NormH = 0.55, PreferredScale = 100 },
                        new TemplateViewSlot { Label = "Section A", ViewType = "Section",
                            NormX = 0.27, NormY = 0.18, NormW = 0.38, NormH = 0.28, PreferredScale = 50 },
                        new TemplateViewSlot { Label = "Section B", ViewType = "Section",
                            NormX = 0.72, NormY = 0.18, NormW = 0.38, NormH = 0.28, PreferredScale = 50 }
                    }
                },
                new SheetTemplate
                {
                    Name = "Elevations (4-Up)",
                    Description = "Four elevations in 2x2 grid",
                    Discipline = "A", PaperSize = "A1",
                    SheetNumberPattern = "{DISC}-{SEQ:D3}",
                    SheetNamePattern = "Elevations",
                    ViewportSlots = new List<TemplateViewSlot>
                    {
                        new TemplateViewSlot { Label = "North", ViewType = "Elevation",
                            NormX = 0.27, NormY = 0.73, NormW = 0.40, NormH = 0.40, PreferredScale = 100 },
                        new TemplateViewSlot { Label = "East", ViewType = "Elevation",
                            NormX = 0.72, NormY = 0.73, NormW = 0.40, NormH = 0.40, PreferredScale = 100 },
                        new TemplateViewSlot { Label = "South", ViewType = "Elevation",
                            NormX = 0.27, NormY = 0.27, NormW = 0.40, NormH = 0.40, PreferredScale = 100 },
                        new TemplateViewSlot { Label = "West", ViewType = "Elevation",
                            NormX = 0.72, NormY = 0.27, NormW = 0.40, NormH = 0.40, PreferredScale = 100 }
                    }
                },
                new SheetTemplate
                {
                    Name = "MEP Plan",
                    Description = "Mechanical/Electrical plan with legend",
                    Discipline = "M", PaperSize = "A1",
                    SheetNumberPattern = "{DISC}-{SEQ:D3}",
                    SheetNamePattern = "{DISC} {LEVEL} Plan",
                    ViewportSlots = new List<TemplateViewSlot>
                    {
                        new TemplateViewSlot { Label = "MEP Plan", ViewType = "FloorPlan",
                            NormX = 0.47, NormY = 0.55, NormW = 0.80, NormH = 0.72, PreferredScale = 50 },
                        new TemplateViewSlot { Label = "Legend", ViewType = "Legend",
                            NormX = 0.20, NormY = 0.10, NormW = 0.25, NormH = 0.14, PreferredScale = 1,
                            Required = false }
                    }
                },
                new SheetTemplate
                {
                    Name = "Detail Sheet",
                    Description = "Multiple detail views in grid layout",
                    Discipline = "A", PaperSize = "A3",
                    SheetNumberPattern = "{DISC}-{SEQ:D3}",
                    SheetNamePattern = "Details",
                    ViewportSlots = new List<TemplateViewSlot>
                    {
                        new TemplateViewSlot { Label = "Detail 1", ViewType = "Detail",
                            NormX = 0.27, NormY = 0.73, NormW = 0.40, NormH = 0.40, PreferredScale = 10 },
                        new TemplateViewSlot { Label = "Detail 2", ViewType = "Detail",
                            NormX = 0.72, NormY = 0.73, NormW = 0.40, NormH = 0.40, PreferredScale = 10 },
                        new TemplateViewSlot { Label = "Detail 3", ViewType = "Detail",
                            NormX = 0.27, NormY = 0.27, NormW = 0.40, NormH = 0.40, PreferredScale = 10 },
                        new TemplateViewSlot { Label = "Detail 4", ViewType = "Detail",
                            NormX = 0.72, NormY = 0.27, NormW = 0.40, NormH = 0.40, PreferredScale = 10,
                            Required = false }
                    }
                },
                new SheetTemplate
                {
                    Name = "Coordination Sheet",
                    Description = "3D coordination view with key plan",
                    Discipline = "C", PaperSize = "A1",
                    SheetNumberPattern = "C-{SEQ:D3}",
                    SheetNamePattern = "Coordination - {LEVEL}",
                    ViewportSlots = new List<TemplateViewSlot>
                    {
                        new TemplateViewSlot { Label = "3D View", ViewType = "ThreeD",
                            NormX = 0.50, NormY = 0.55, NormW = 0.82, NormH = 0.72, PreferredScale = 100 },
                        new TemplateViewSlot { Label = "Key Plan", ViewType = "FloorPlan",
                            NormX = 0.82, NormY = 0.10, NormW = 0.20, NormH = 0.16, PreferredScale = 500,
                            Required = false }
                    }
                }
            };
        }

        /// <summary>
        /// Create a sheet from a template, placing matched views into slots.
        /// Must be called within an active Transaction.
        /// </summary>
        /// <returns>The created ViewSheet, or null on failure.</returns>
        internal static ViewSheet CreateSheetFromTemplate(Document doc, SheetTemplate template,
            List<View> viewsToPlace, ElementId titleBlockTypeId)
        {
            // Create sheet
            var sheet = ViewSheet.Create(doc, titleBlockTypeId);

            // Generate sheet number
            string disc = template.Discipline ?? "G";
            string nextNum = SheetManagerEngine.GetNextSheetNumber(doc, disc);
            try { sheet.SheetNumber = nextNum; }
            catch (Exception ex) { StingLog.Warn($"Template conflict: {ex.Message}"); }

            // Generate sheet name
            string name = template.SheetNamePattern ?? template.Name;
            name = name.Replace("{DISC}", disc);
            try { sheet.Name = name; }
            catch (Exception ex) { StingLog.Warn($"Template conflict: {ex.Message}"); }

            // Get drawable zone
            var zone = SheetManagerEngine.GetDrawableZone(doc, sheet);

            // Match views to slots
            var usedViews = new HashSet<ElementId>();

            foreach (var slot in template.ViewportSlots)
            {
                // Find best matching view
                View bestView = null;
                foreach (var view in viewsToPlace)
                {
                    if (usedViews.Contains(view.Id)) continue;
                    if (view.ViewType.ToString().Equals(slot.ViewType, StringComparison.OrdinalIgnoreCase))
                    {
                        bestView = view;
                        break;
                    }
                }

                if (bestView == null)
                {
                    if (slot.Required)
                        StingLog.Warn($"Template '{template.Name}': no view found for required slot '{slot.Label}' (type: {slot.ViewType}).");
                    continue;
                }

                usedViews.Add(bestView.Id);

                if (!Viewport.CanAddViewToSheet(doc, sheet.Id, bestView.Id))
                {
                    StingLog.Warn($"View '{bestView.Name}' already on another sheet.");
                    continue;
                }

                // Set scale
                if (slot.PreferredScale > 0 && bestView.Scale != slot.PreferredScale)
                {
                    try { bestView.Scale = slot.PreferredScale; }
                    catch (Exception ex2) { StingLog.Warn($"Locked by template: {ex2.Message}"); }
                }

                // Denormalise position
                double cx = zone.Min.X + slot.NormX * zone.Width;
                double cy = zone.Min.Y + slot.NormY * zone.Height;

                try
                {
                    var vp = Viewport.Create(doc, sheet.Id, bestView.Id, new XYZ(cx, cy, 0));

                    // Set viewport type if specified
                    if (!string.IsNullOrEmpty(slot.ViewportTypeName))
                    {
                        var vpType = new FilteredElementCollector(doc)
                            .OfClass(typeof(ElementType))
                            .Cast<ElementType>()
                            .FirstOrDefault(t => t.FamilyName == "Viewport" &&
                                t.Name.Equals(slot.ViewportTypeName, StringComparison.OrdinalIgnoreCase));
                        if (vpType != null)
                        {
                            try { vp.ChangeTypeId(vpType.Id); }
                            catch (Exception ex2) { StingLog.Warn($"Type not available: {ex2.Message}"); }
                        }
                    }
                }
                catch (Exception ex2)
                {
                    StingLog.Warn($"Could not place view '{bestView.Name}' in slot '{slot.Label}': {ex2.Message}");
                }
            }

            StingLog.Info($"Created sheet '{sheet.SheetNumber}' from template '{template.Name}' with {usedViews.Count} views.");
            return sheet;
        }

        /// <summary>
        /// Save a sheet template from the current sheet arrangement.
        /// </summary>
        internal static SheetTemplate SaveTemplateFromSheet(Document doc, ViewSheet sheet,
            string templateName, string discipline = null)
        {
            var zone = SheetManagerEngine.GetDrawableZone(doc, sheet);
            doc.Regenerate();

            var template = new SheetTemplate
            {
                Name = templateName,
                Description = $"Created from sheet {sheet.SheetNumber}",
                Discipline = discipline ?? SheetManagerEngine.ExtractDisciplinePrefix(sheet.SheetNumber),
                PaperSize = GetPaperSizeLabel(doc, sheet),
                SheetNumberPattern = "{DISC}-{SEQ:D3}",
                SheetNamePattern = sheet.Name,
                Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
            };

            foreach (var vpId in sheet.GetAllViewports())
            {
                var vp = doc.GetElement(vpId) as Viewport;
                if (vp == null) continue;
                var view = doc.GetElement(vp.ViewId) as View;
                if (view == null) continue;

                try
                {
                    var center = vp.GetBoxCenter();
                    var outline = vp.GetBoxOutline();
                    double vpW = outline.MaximumPoint.X - outline.MinimumPoint.X;
                    double vpH = outline.MaximumPoint.Y - outline.MinimumPoint.Y;

                    template.ViewportSlots.Add(new TemplateViewSlot
                    {
                        Label = view.Name,
                        ViewType = view.ViewType.ToString(),
                        NormX = Math.Round((center.X - zone.Min.X) / zone.Width, 4),
                        NormY = Math.Round((center.Y - zone.Min.Y) / zone.Height, 4),
                        NormW = Math.Round(vpW / zone.Width, 4),
                        NormH = Math.Round(vpH / zone.Height, 4),
                        PreferredScale = view.Scale
                    });
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Could not read viewport for template: {ex.Message}");
                }
            }

            // Save to library
            var library = LoadTemplateLibrary(doc);
            library.Templates.RemoveAll(t => t.Name.Equals(templateName, StringComparison.OrdinalIgnoreCase));
            library.Templates.Add(template);
            SaveTemplateLibrary(doc, library);

            return template;
        }

        private static string GetTemplateFilePath(Document doc)
        {
            try
            {
                string p = StingTools.Core.ProjectFolderEngine.GetDataPath(doc, "sheet_templates.json");
                if (!string.IsNullOrEmpty(p)) return p;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            string dir = Path.GetDirectoryName(doc.PathName);
            if (string.IsNullOrEmpty(dir))
                dir = StingToolsApp.DataPath ?? Path.GetTempPath();
            return Path.Combine(dir, ".sting_sheet_templates.json");
        }

        internal static SheetTemplateLibrary LoadTemplateLibrary(Document doc)
        {
            string path = GetTemplateFilePath(doc);
            if (!File.Exists(path)) return new SheetTemplateLibrary();
            try
            {
                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<SheetTemplateLibrary>(json) ?? new SheetTemplateLibrary();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Could not load sheet templates: {ex.Message}");
                return new SheetTemplateLibrary();
            }
        }

        internal static void SaveTemplateLibrary(Document doc, SheetTemplateLibrary library)
        {
            string path = GetTemplateFilePath(doc);
            try
            {
                string json = JsonConvert.SerializeObject(library, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Could not save sheet templates: {ex.Message}");
            }
        }

        private static string GetPaperSizeLabel(Document doc, ViewSheet sheet)
        {
            var tb = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Cast<FamilyInstance>().FirstOrDefault();
            if (tb == null) return "Unknown";
            var w = tb.get_Parameter(BuiltInParameter.SHEET_WIDTH);
            var h = tb.get_Parameter(BuiltInParameter.SHEET_HEIGHT);
            return (w != null && h != null) ? PaperSizes.Detect(w.AsDouble(), h.AsDouble()) : "Unknown";
        }


        // ═══════════════════════════════════════════════════════════════════
        //  2. ISO 19650 SHEET COMPLIANCE CHECKING
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// ISO 19650-2 sheet naming rules.
        /// Format: {Project}-{Originator}-{Volume}-{Level}-{Type}-{Discipline}-{Number}
        /// Simplified check: discipline prefix, sequential numbering, name consistency.
        /// </summary>
        internal static List<SheetComplianceResult> CheckCompliance(Document doc)
        {
            var results = new List<SheetComplianceResult>();

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber)
                .ToList();

            var numberSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var nameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var sheet in sheets)
            {
                var result = new SheetComplianceResult
                {
                    SheetNumber = sheet.SheetNumber,
                    SheetName = sheet.Name
                };

                // Check 1: Sheet number not empty
                if (string.IsNullOrWhiteSpace(sheet.SheetNumber))
                    result.Issues.Add("Sheet number is empty");

                // Check 2: Sheet name not empty
                if (string.IsNullOrWhiteSpace(sheet.Name))
                    result.Issues.Add("Sheet name is empty");

                // Check 3: Duplicate sheet number
                if (!numberSet.Add(sheet.SheetNumber))
                    result.Issues.Add($"Duplicate sheet number: {sheet.SheetNumber}");

                // Check 4: Duplicate sheet name
                if (!nameSet.Add(sheet.Name))
                    result.Issues.Add($"Duplicate sheet name: {sheet.Name}");

                // Check 5: Number format (should contain discipline prefix and digits)
                string num = sheet.SheetNumber;
                if (num.Length > 0 && !char.IsLetter(num[0]))
                    result.Issues.Add("Sheet number should start with discipline letter (A, S, M, E, P, etc.)");

                bool hasDigits = num.Any(char.IsDigit);
                if (!hasDigits)
                    result.Issues.Add("Sheet number should contain a numeric sequence");

                // Check 6: Separator consistency (should use - or .)
                if (num.Contains(" ") && !num.Contains("-") && !num.Contains("."))
                    result.Issues.Add("Sheet number uses spaces — prefer hyphens for ISO 19650 compliance");

                // Check 7: Has title block
                var tb = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .Cast<FamilyInstance>()
                    .FirstOrDefault();
                if (tb == null)
                    result.Issues.Add("No title block placed on sheet");

                // Check 8: Has viewports (empty sheets are suspicious)
                if (sheet.GetAllViewports().Count == 0)
                    result.Issues.Add("Sheet has no viewports — may be a placeholder");

                // Check 9: Name case consistency (should be Title Case or UPPER)
                if (sheet.Name.Length > 0)
                {
                    bool isAllLower = sheet.Name == sheet.Name.ToLowerInvariant();
                    if (isAllLower)
                        result.Issues.Add("Sheet name is all lowercase — consider Title Case for formal documents");
                }

                // Check 10: Special characters in name
                char[] badChars = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };
                if (sheet.Name.IndexOfAny(badChars) >= 0)
                    result.Issues.Add("Sheet name contains characters invalid for file export");

                results.Add(result);
            }

            return results;
        }

        /// <summary>
        /// Generate a compliance summary report string.
        /// </summary>
        internal static string BuildComplianceReport(List<SheetComplianceResult> results)
        {
            int pass = results.Count(r => r.IsCompliant);
            int fail = results.Count - pass;

            var report = new System.Text.StringBuilder();
            report.AppendLine("=== ISO 19650 Sheet Compliance Report ===\n");
            report.AppendLine($"Total Sheets: {results.Count}");
            report.AppendLine($"Compliant: {pass}  |  Non-Compliant: {fail}");
            report.AppendLine($"Compliance: {(results.Count > 0 ? pass * 100.0 / results.Count : 0):F0}%\n");

            if (fail > 0)
            {
                // Group issues by type
                var issueGroups = results
                    .SelectMany(r => r.Issues.Select(i => (Issue: i, Sheet: r.SheetNumber)))
                    .GroupBy(x => x.Issue.Split(':')[0].Trim())
                    .OrderByDescending(g => g.Count());

                report.AppendLine("--- Issue Summary ---");
                foreach (var group in issueGroups)
                {
                    report.AppendLine($"  [{group.Count()}x] {group.Key}");
                }

                report.AppendLine($"\n--- Failed Sheets (top 15) ---");
                foreach (var r in results.Where(r => !r.IsCompliant).Take(15))
                {
                    report.AppendLine($"  {r.SheetNumber}: {string.Join("; ", r.Issues)}");
                }
            }

            return report.ToString();
        }


        // ═══════════════════════════════════════════════════════════════════
        //  3. VIEWPORT GRID ALIGNMENT ENGINE
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Build an alignment grid based on the sheet's drawable zone.
        /// Grid cells are proportional to the drawable area for consistent snapping.
        /// </summary>
        internal static AlignmentGrid BuildAlignmentGrid(DrawableZone zone, int columns = 12, int rows = 8)
        {
            double cellW = zone.Width / columns;
            double cellH = zone.Height / rows;

            return new AlignmentGrid
            {
                CellWidthFt = cellW,
                CellHeightFt = cellH,
                Origin = zone.Min
            };
        }

        /// <summary>
        /// Snap all viewport centres on a sheet to the nearest grid intersection.
        /// Must be called within an active Transaction.
        /// </summary>
        /// <returns>Number of viewports adjusted.</returns>
        internal static int SnapViewportsToGrid(Document doc, ViewSheet sheet, AlignmentGrid grid)
        {
            int adjusted = 0;
            doc.Regenerate();

            foreach (var vpId in sheet.GetAllViewports())
            {
                var vp = doc.GetElement(vpId) as Viewport;
                if (vp == null) continue;

                try
                {
                    XYZ current = vp.GetBoxCenter();
                    XYZ snapped = grid.Snap(current);

                    double dx = Math.Abs(current.X - snapped.X);
                    double dy = Math.Abs(current.Y - snapped.Y);

                    // Only move if displacement is significant (> 1mm)
                    if (dx > 0.003 || dy > 0.003)
                    {
                        vp.SetBoxCenter(snapped);
                        adjusted++;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Could not snap viewport: {ex.Message}");
                }
            }

            return adjusted;
        }

        /// <summary>
        /// Align viewport edges to a common line (left, right, top, or bottom).
        /// Picks the most common edge position (mode) and snaps all viewports to it.
        /// Must be called within an active Transaction.
        /// </summary>
        internal static int AlignViewportEdges(Document doc, ViewSheet sheet, string edge)
        {
            doc.Regenerate();
            var vpIds = sheet.GetAllViewports();
            if (vpIds.Count < 2) return 0;

            var vpData = new List<(Viewport vp, XYZ center, double minX, double maxX, double minY, double maxY)>();
            foreach (var vpId in vpIds)
            {
                var vp = doc.GetElement(vpId) as Viewport;
                if (vp == null) continue;
                try
                {
                    var center = vp.GetBoxCenter();
                    var outline = vp.GetBoxOutline();
                    vpData.Add((vp, center,
                        outline.MinimumPoint.X, outline.MaximumPoint.X,
                        outline.MinimumPoint.Y, outline.MaximumPoint.Y));
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Could not read viewport outline for alignment: {ex.Message}");
                }
            }

            if (vpData.Count < 2) return 0;

            int moved = 0;
            switch (edge.ToUpperInvariant())
            {
                case "LEFT":
                {
                    double targetX = vpData.Select(v => v.minX).OrderBy(x => x)
                        .GroupBy(x => Math.Round(x, 2)).OrderByDescending(g => g.Count()).First().Key;
                    foreach (var (vp, center, minX, _, _, _) in vpData)
                    {
                        double shift = targetX - minX;
                        if (Math.Abs(shift) > 0.003)
                        {
                            vp.SetBoxCenter(new XYZ(center.X + shift, center.Y, 0));
                            moved++;
                        }
                    }
                    break;
                }
                case "RIGHT":
                {
                    double targetX = vpData.Select(v => v.maxX).OrderByDescending(x => x)
                        .GroupBy(x => Math.Round(x, 2)).OrderByDescending(g => g.Count()).First().Key;
                    foreach (var (vp, center, _, maxX, _, _) in vpData)
                    {
                        double shift = targetX - maxX;
                        if (Math.Abs(shift) > 0.003)
                        {
                            vp.SetBoxCenter(new XYZ(center.X + shift, center.Y, 0));
                            moved++;
                        }
                    }
                    break;
                }
                case "TOP":
                {
                    double targetY = vpData.Select(v => v.maxY).OrderByDescending(y => y)
                        .GroupBy(y => Math.Round(y, 2)).OrderByDescending(g => g.Count()).First().Key;
                    foreach (var (vp, center, _, _, _, maxY) in vpData)
                    {
                        double shift = targetY - maxY;
                        if (Math.Abs(shift) > 0.003)
                        {
                            vp.SetBoxCenter(new XYZ(center.X, center.Y + shift, 0));
                            moved++;
                        }
                    }
                    break;
                }
                case "BOTTOM":
                {
                    double targetY = vpData.Select(v => v.minY).OrderBy(y => y)
                        .GroupBy(y => Math.Round(y, 2)).OrderByDescending(g => g.Count()).First().Key;
                    foreach (var (vp, center, _, _, minY, _) in vpData)
                    {
                        double shift = targetY - minY;
                        if (Math.Abs(shift) > 0.003)
                        {
                            vp.SetBoxCenter(new XYZ(center.X, center.Y + shift, 0));
                            moved++;
                        }
                    }
                    break;
                }
                case "CENTER_H":
                {
                    double avgCx = vpData.Average(v => v.center.X);
                    foreach (var (vp, center, _, _, _, _) in vpData)
                    {
                        double shift = avgCx - center.X;
                        if (Math.Abs(shift) > 0.003)
                        {
                            vp.SetBoxCenter(new XYZ(avgCx, center.Y, 0));
                            moved++;
                        }
                    }
                    break;
                }
                case "CENTER_V":
                {
                    double avgCy = vpData.Average(v => v.center.Y);
                    foreach (var (vp, center, _, _, _, _) in vpData)
                    {
                        double shift = avgCy - center.Y;
                        if (Math.Abs(shift) > 0.003)
                        {
                            vp.SetBoxCenter(new XYZ(center.X, avgCy, 0));
                            moved++;
                        }
                    }
                    break;
                }
            }

            return moved;
        }

        /// <summary>
        /// Distribute viewports evenly across the drawable zone
        /// with equal spacing between them.
        /// Must be called within an active Transaction.
        /// </summary>
        internal static int DistributeViewports(Document doc, ViewSheet sheet, bool horizontal)
        {
            doc.Regenerate();
            var vpIds = sheet.GetAllViewports();
            if (vpIds.Count < 3) return 0;

            var zone = SheetManagerEngine.GetDrawableZone(doc, sheet);
            var vpList = new List<(Viewport vp, XYZ center, double halfW, double halfH)>();

            foreach (var vpId in vpIds)
            {
                var vp = doc.GetElement(vpId) as Viewport;
                if (vp == null) continue;
                try
                {
                    var center = vp.GetBoxCenter();
                    var outline = vp.GetBoxOutline();
                    double hw = (outline.MaximumPoint.X - outline.MinimumPoint.X) / 2.0;
                    double hh = (outline.MaximumPoint.Y - outline.MinimumPoint.Y) / 2.0;
                    vpList.Add((vp, center, hw, hh));
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Could not read viewport for distribution: {ex.Message}");
                }
            }

            if (vpList.Count < 3) return 0;

            int moved = 0;
            if (horizontal)
            {
                vpList = vpList.OrderBy(v => v.center.X).ToList();
                double totalW = vpList.Sum(v => v.halfW * 2);
                double spacing = (zone.Width - totalW) / (vpList.Count - 1);
                double curX = zone.Min.X + vpList[0].halfW;

                foreach (var (vp, center, halfW, _) in vpList)
                {
                    if (Math.Abs(curX - center.X) > 0.003)
                    {
                        vp.SetBoxCenter(new XYZ(curX, center.Y, 0));
                        moved++;
                    }
                    curX += halfW * 2 + spacing;
                }
            }
            else
            {
                vpList = vpList.OrderByDescending(v => v.center.Y).ToList();
                double totalH = vpList.Sum(v => v.halfH * 2);
                double spacing = (zone.Height - totalH) / (vpList.Count - 1);
                double curY = zone.Min.Y + zone.Height - vpList[0].halfH;

                foreach (var (vp, center, _, halfH) in vpList)
                {
                    if (Math.Abs(curY - center.Y) > 0.003)
                    {
                        vp.SetBoxCenter(new XYZ(center.X, curY, 0));
                        moved++;
                    }
                    curY -= halfH * 2 + spacing;
                }
            }

            return moved;
        }


        // ═══════════════════════════════════════════════════════════════════
        //  4. BATCH PRINT / EXPORT INTEGRATION
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Prepare a list of ViewSheets for batch PDF export via Revit's
        /// built-in PDF printer or Export → PDF API (Revit 2022+).
        /// Returns the list of sheets prepared, or empty on failure.
        /// </summary>
        internal static List<ViewSheet> PreparePrintBatch(Document doc, PrintBatchConfig config)
        {
            var sheets = new List<ViewSheet>();

            foreach (string sheetNum in config.SheetNumbers)
            {
                var sheet = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .FirstOrDefault(s => s.SheetNumber.Equals(sheetNum, StringComparison.OrdinalIgnoreCase));

                if (sheet != null)
                    sheets.Add(sheet);
                else
                    StingLog.Warn($"Print batch: sheet '{sheetNum}' not found.");
            }

            return sheets;
        }

        /// <summary>
        /// Export selected sheets to PDF using Revit 2022+ PDF export API.
        /// Must be called outside of a Transaction.
        /// </summary>
        /// <returns>Number of sheets exported.</returns>
        internal static int ExportSheetsToPDF(Document doc, List<ViewSheet> sheets,
            string outputDir, string fileNamePattern = null)
        {
            if (sheets.Count == 0) return 0;

            if (!Directory.Exists(outputDir))
            {
                try { Directory.CreateDirectory(outputDir); }
                catch (Exception ex)
                {
                    StingLog.Error($"Could not create output directory: {ex.Message}");
                    return 0;
                }
            }

            int exported = 0;
            string pattern = fileNamePattern ?? "{NUMBER} - {NAME}";

            // Use Revit PDF export (2022+ API)
            foreach (var sheet in sheets)
            {
                try
                {
                    string fileName = pattern
                        .Replace("{NUMBER}", sheet.SheetNumber)
                        .Replace("{NAME}", sheet.Name)
                        .Replace("{DATE}", DateTime.Now.ToString("yyyy-MM-dd"));

                    // Sanitise filename
                    foreach (char c in Path.GetInvalidFileNameChars())
                        fileName = fileName.Replace(c, '_');

                    var viewIds = new List<ElementId> { sheet.Id };

                    var pdfOpts = new PDFExportOptions
                    {
                        FileName = fileName,
                        Combine = false,
                        AlwaysUseRaster = false
                    };

                    bool ok = doc.Export(outputDir, viewIds, pdfOpts);
                    if (ok)
                    {
                        exported++;
                        StingLog.Info($"Exported: {fileName}.pdf");
                    }
                    else
                    {
                        StingLog.Warn($"PDF export returned false for sheet {sheet.SheetNumber}");
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"PDF export failed for {sheet.SheetNumber}: {ex.Message}");
                }
            }

            return exported;
        }

        /// <summary>
        /// Export all sheets matching a discipline prefix to PDF.
        /// </summary>
        internal static int ExportDisciplineToPDF(Document doc, string discipline, string outputDir)
        {
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder &&
                    SheetManagerEngine.ExtractDisciplinePrefix(s.SheetNumber)
                        .Equals(discipline, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.SheetNumber)
                .ToList();

            if (sheets.Count == 0)
            {
                StingLog.Info($"No sheets found for discipline '{discipline}'.");
                return 0;
            }

            return ExportSheetsToPDF(doc, sheets, outputDir);
        }

        /// <summary>
        /// Export a comprehensive sheet register to CSV.
        /// Includes sheet number, name, title block, viewport count, discipline,
        /// compliance status, scale, and paper size.
        /// </summary>
        internal static string ExportSheetRegister(Document doc, string outputPath)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Sheet Number,Sheet Name,Discipline,Title Block,Paper Size,Viewport Count,Scales,Compliance,SHT_TAG_1,SHT_DISC,SHT_FORM,SHT_LEVEL,SHT_REV");

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber)
                .ToList();

            var complianceResults = CheckCompliance(doc);
            var complianceMap = complianceResults.ToDictionary(r => r.SheetNumber, StringComparer.OrdinalIgnoreCase);

            foreach (var sheet in sheets)
            {
                string disc = SheetManagerEngine.ExtractDisciplinePrefix(sheet.SheetNumber);
                string paperSize = GetPaperSizeLabel(doc, sheet);

                // Get title block name
                var tb = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .Cast<FamilyInstance>().FirstOrDefault();
                string tbName = tb != null ? $"{tb.Symbol.FamilyName}: {tb.Symbol.Name}" : "None";

                // Get viewport scales
                var vpIds = sheet.GetAllViewports();
                var scales = new List<string>();
                foreach (var vpId in vpIds)
                {
                    var vp = doc.GetElement(vpId) as Viewport;
                    if (vp == null) continue;
                    var view = doc.GetElement(vp.ViewId) as View;
                    if (view != null) scales.Add($"1:{view.Scale}");
                }
                string scaleStr = scales.Count > 0 ? string.Join("; ", scales.Distinct()) : "N/A";

                string compliance = complianceMap.TryGetValue(sheet.SheetNumber, out var cr) ? cr.Status : "N/A";

                // Phase 39: Include SHT_ tag data in register export
                string shtTag1 = ParameterHelpers.GetString(sheet, ParamRegistry.SHT_TAG_1);
                string shtDisc = ParameterHelpers.GetString(sheet, ParamRegistry.SHT_DISC);
                string shtForm = ParameterHelpers.GetString(sheet, ParamRegistry.SHT_FORM);
                string shtLevel = ParameterHelpers.GetString(sheet, ParamRegistry.SHT_LEVEL);
                string shtRev = ParameterHelpers.GetString(sheet, ParamRegistry.SHT_REV);

                sb.AppendLine($"\"{sheet.SheetNumber}\",\"{sheet.Name}\",\"{disc}\",\"{tbName}\",\"{paperSize}\",{vpIds.Count},\"{scaleStr}\",\"{compliance}\",\"{shtTag1}\",\"{shtDisc}\",\"{shtForm}\",\"{shtLevel}\",\"{shtRev}\"");
            }

            try
            {
                File.WriteAllText(outputPath, sb.ToString());
                StingLog.Info($"Sheet register exported to: {outputPath}");
            }
            catch (Exception ex)
            {
                StingLog.Error($"Could not write sheet register: {ex.Message}");
            }

            return sb.ToString();
        }
    }
}
