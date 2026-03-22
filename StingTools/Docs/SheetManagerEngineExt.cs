using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Docs
{
    // ════════════════════════════════════════════════════════════════════════════
    //  SHEET MANAGER ENGINE — PHASE 2 EXTENSIONS
    //  Maximal Rectangles packing, layout presets, viewport type auto-assignment,
    //  sheet set operations, and overflow handling.
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Represents a saved viewport position within a layout preset.
    /// Positions are stored as normalised fractions (0.0 - 1.0) of the drawable area
    /// so presets transfer between different paper sizes.
    /// </summary>
    internal class LayoutSlotPreset
    {
        [JsonProperty("viewType")] public string ViewType { get; set; }
        [JsonProperty("discipline")] public string Discipline { get; set; }
        [JsonProperty("normX")] public double NormX { get; set; }  // 0.0 = left, 1.0 = right
        [JsonProperty("normY")] public double NormY { get; set; }  // 0.0 = bottom, 1.0 = top
        [JsonProperty("normW")] public double NormW { get; set; }  // fraction of drawable width
        [JsonProperty("normH")] public double NormH { get; set; }  // fraction of drawable height
        [JsonProperty("scale")] public int Scale { get; set; }
    }

    /// <summary>
    /// A saved layout preset containing named viewport positions.
    /// </summary>
    internal class LayoutPreset
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("paperSize")] public string PaperSize { get; set; }
        [JsonProperty("margins")] public string Margins { get; set; }
        [JsonProperty("created")] public string Created { get; set; }
        [JsonProperty("slots")] public List<LayoutSlotPreset> Slots { get; set; } = new List<LayoutSlotPreset>();
    }

    /// <summary>
    /// Collection of layout presets saved per project.
    /// </summary>
    internal class LayoutPresetLibrary
    {
        [JsonProperty("version")] public string Version { get; set; } = "1.0";
        [JsonProperty("presets")] public List<LayoutPreset> Presets { get; set; } = new List<LayoutPreset>();
    }

    /// <summary>
    /// Rule for auto-assigning viewport types based on view properties.
    /// </summary>
    internal class ViewportTypeRule
    {
        public string ViewTypeMatch { get; set; }      // e.g. "FloorPlan", "Section"
        public string DisciplineMatch { get; set; }    // e.g. "M", "E", null = any
        public string ViewNameContains { get; set; }   // optional name filter
        public string TargetViewportTypeName { get; set; } // viewport type to assign
    }

    /// <summary>
    /// A free rectangle in the Maximal Rectangles packing algorithm.
    /// </summary>
    internal class FreeRect
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
        public double Area => W * H;
    }

    /// <summary>
    /// Sheet set export row for CSV output.
    /// </summary>
    internal class SheetSetRow
    {
        public string SheetNumber { get; set; }
        public string SheetName { get; set; }
        public string Discipline { get; set; }
        public string PaperSize { get; set; }
        public int ViewportCount { get; set; }
        public string ViewNames { get; set; }
        public string TitleBlock { get; set; }
    }


    /// <summary>
    /// Phase 2 engine extensions: Maximal Rectangles packing, layout presets,
    /// viewport type auto-assignment, sheet set operations.
    /// </summary>
    internal static class SheetManagerEngineExt
    {
        private const double MmToFeet = 1.0 / 304.8;

        // ═══════════════════════════════════════════════════════════════════
        //  1. MAXIMAL RECTANGLES PACKING ALGORITHM
        //  Better packing density than shelf packing by maintaining a list
        //  of all maximal free rectangular regions after each placement.
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Maximal Rectangles bin packer. Maintains free rectangle list and
        /// places items using Best Short Side Fit (BSSF) heuristic.
        /// </summary>
        internal class MaxRectsPacker
        {
            private readonly double _binW;
            private readonly double _binH;
            private readonly XYZ _origin;
            private readonly List<FreeRect> _freeRects;

            public MaxRectsPacker(DrawableZone zone)
            {
                _origin = zone.Min;
                _binW = zone.Width;
                _binH = zone.Height;
                _freeRects = new List<FreeRect>
                {
                    new FreeRect { X = 0, Y = 0, W = _binW, H = _binH }
                };
            }

            /// <summary>
            /// Try to place an item of given width/height.
            /// Returns the center point in sheet coordinates, or null if no fit.
            /// Uses Best Short Side Fit: picks the free rectangle where the shorter
            /// leftover side is minimised.
            /// </summary>
            public XYZ TryPlace(double itemW, double itemH)
            {
                int bestIdx = -1;
                double bestShortSide = double.MaxValue;
                double bestLongSide = double.MaxValue;
                double bestX = 0, bestY = 0;

                for (int i = 0; i < _freeRects.Count; i++)
                {
                    var r = _freeRects[i];
                    if (itemW <= r.W + 0.001 && itemH <= r.H + 0.001)
                    {
                        double leftoverW = r.W - itemW;
                        double leftoverH = r.H - itemH;
                        double shortSide = Math.Min(leftoverW, leftoverH);
                        double longSide = Math.Max(leftoverW, leftoverH);

                        if (shortSide < bestShortSide ||
                            (Math.Abs(shortSide - bestShortSide) < 0.001 && longSide < bestLongSide))
                        {
                            bestIdx = i;
                            bestShortSide = shortSide;
                            bestLongSide = longSide;
                            bestX = r.X;
                            bestY = r.Y;
                        }
                    }
                }

                if (bestIdx < 0) return null; // no fit

                // Place item at top-left of best free rectangle (top-down for visual order)
                double placeX = bestX;
                double placeY = _binH - bestY - itemH; // flip Y for top-down

                // Split free rectangles
                SplitFreeRects(bestX, bestY, itemW, itemH);
                PruneFreeRects();

                // Return center in sheet coordinates
                double cx = _origin.X + placeX + itemW / 2.0;
                double cy = _origin.Y + placeY + itemH / 2.0;
                return new XYZ(cx, cy, 0);
            }

            private void SplitFreeRects(double px, double py, double pw, double ph)
            {
                var placed = new FreeRect { X = px, Y = py, W = pw, H = ph };
                var newRects = new List<FreeRect>();

                for (int i = _freeRects.Count - 1; i >= 0; i--)
                {
                    var r = _freeRects[i];
                    if (!RectsOverlap(r, placed))
                    {
                        continue; // no overlap, keep as-is
                    }

                    // Remove overlapping free rect and generate up to 4 new ones
                    _freeRects.RemoveAt(i);

                    // Left portion
                    if (px > r.X)
                        newRects.Add(new FreeRect { X = r.X, Y = r.Y, W = px - r.X, H = r.H });

                    // Right portion
                    if (px + pw < r.X + r.W)
                        newRects.Add(new FreeRect { X = px + pw, Y = r.Y, W = (r.X + r.W) - (px + pw), H = r.H });

                    // Bottom portion
                    if (py > r.Y)
                        newRects.Add(new FreeRect { X = r.X, Y = r.Y, W = r.W, H = py - r.Y });

                    // Top portion
                    if (py + ph < r.Y + r.H)
                        newRects.Add(new FreeRect { X = r.X, Y = py + ph, W = r.W, H = (r.Y + r.H) - (py + ph) });
                }

                _freeRects.AddRange(newRects);
            }

            private void PruneFreeRects()
            {
                // Remove rectangles that are fully contained within another
                for (int i = _freeRects.Count - 1; i >= 0; i--)
                {
                    for (int j = 0; j < _freeRects.Count; j++)
                    {
                        if (i == j) continue;
                        if (IsContainedIn(_freeRects[i], _freeRects[j]))
                        {
                            _freeRects.RemoveAt(i);
                            break;
                        }
                    }
                }
            }

            private static bool RectsOverlap(FreeRect a, FreeRect b)
            {
                return a.X < b.X + b.W && a.X + a.W > b.X &&
                       a.Y < b.Y + b.H && a.Y + a.H > b.Y;
            }

            private static bool IsContainedIn(FreeRect inner, FreeRect outer)
            {
                return inner.X >= outer.X && inner.Y >= outer.Y &&
                       inner.X + inner.W <= outer.X + outer.W &&
                       inner.Y + inner.H <= outer.Y + outer.H;
            }

            /// <summary>Get remaining free area as percentage.</summary>
            public double FreeAreaPercent()
            {
                double totalFree = 0;
                // Use a simple approximation: sum of non-overlapping free rects
                // (exact calculation would require union of rectangles)
                foreach (var r in _freeRects)
                    totalFree += r.Area;
                // Cap at 100% since overlapping rects can sum > total
                return Math.Min(100.0, totalFree / (_binW * _binH) * 100.0);
            }

            public void Reset()
            {
                _freeRects.Clear();
                _freeRects.Add(new FreeRect { X = 0, Y = 0, W = _binW, H = _binH });
            }
        }


        /// <summary>
        /// Run MaxRects packing layout on a set of views.
        /// Better density than shelf packing for irregular viewport sizes.
        /// </summary>
        internal static LayoutResult RunMaxRectsPacking(Document doc, List<View> views,
            DrawableZone zone, double gapMm = 8.0, bool autoScale = false)
        {
            double gapFt = gapMm * MmToFeet;
            var result = new LayoutResult();
            var packer = new MaxRectsPacker(zone);

            // Build slots
            var slots = new List<ViewportSlot>();
            foreach (var view in views)
            {
                int scale = view.Scale;
                if (autoScale)
                {
                    int optimal = SheetManagerEngine.CalculateOptimalScale(view, zone.Width, zone.Height);
                    if (optimal > 0) scale = optimal;
                }

                var (w, h) = SheetManagerEngine.GetPaperSize(view, scale);
                if (w < 0.001 || h < 0.001) continue;

                slots.Add(new ViewportSlot
                {
                    ViewId = view.Id,
                    ViewName = view.Name,
                    PaperWidth = w + gapFt,  // include gap in item size
                    PaperHeight = h + gapFt,
                    Scale = scale
                });
            }

            // Sort largest-area-first for better packing
            slots.Sort((a, b) =>
            {
                double areaA = a.PaperWidth * a.PaperHeight;
                double areaB = b.PaperWidth * b.PaperHeight;
                return areaB.CompareTo(areaA);
            });

            foreach (var slot in slots)
            {
                var center = packer.TryPlace(slot.PaperWidth, slot.PaperHeight);
                if (center != null)
                {
                    slot.PlacedCenter = center;
                    slot.Placed = true;
                    result.PlacedSlots.Add(slot);
                }
                else
                {
                    result.OverflowSlots.Add(slot);
                }
            }

            int placed = result.PlacedSlots.Count;
            int overflow = result.OverflowSlots.Count;
            double utilisation = 100.0 - packer.FreeAreaPercent();
            result.Summary = overflow > 0
                ? $"Placed {placed} viewports ({utilisation:F0}% utilisation). {overflow} views overflowed."
                : $"Placed {placed} viewports ({utilisation:F0}% utilisation).";

            return result;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  2. LAYOUT PRESETS — Save/Load Viewport Arrangements
        // ═══════════════════════════════════════════════════════════════════

        private static string GetPresetFilePath(Document doc)
        {
            string dir = Path.GetDirectoryName(doc.PathName);
            if (string.IsNullOrEmpty(dir))
                dir = StingToolsApp.DataPath ?? Path.GetTempPath();
            return Path.Combine(dir, ".sting_layout_presets.json");
        }

        /// <summary>
        /// Save the current viewport arrangement on a sheet as a named preset.
        /// Positions are normalised to the drawable area so they transfer between paper sizes.
        /// </summary>
        internal static LayoutPreset SaveLayoutPreset(Document doc, ViewSheet sheet,
            string presetName, string description = null)
        {
            var zone = SheetManagerEngine.GetDrawableZone(doc, sheet);
            var vpIds = sheet.GetAllViewports().ToList();

            doc.Regenerate();

            var preset = new LayoutPreset
            {
                Name = presetName,
                Description = description ?? $"Saved from sheet {sheet.SheetNumber}",
                PaperSize = GetSheetPaperSize(doc, sheet),
                Margins = "Default",
                Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Slots = new List<LayoutSlotPreset>()
            };

            foreach (var vpId in vpIds)
            {
                var vp = doc.GetElement(vpId) as Viewport;
                if (vp == null) continue;
                var view = doc.GetElement(vp.ViewId) as View;
                if (view == null) continue;

                try
                {
                    var outline = vp.GetBoxOutline();
                    double vpW = outline.MaximumPoint.X - outline.MinimumPoint.X;
                    double vpH = outline.MaximumPoint.Y - outline.MinimumPoint.Y;
                    var center = vp.GetBoxCenter();

                    // Normalise to drawable area
                    double normX = (center.X - zone.Min.X) / zone.Width;
                    double normY = (center.Y - zone.Min.Y) / zone.Height;
                    double normW = vpW / zone.Width;
                    double normH = vpH / zone.Height;

                    string disc = "";
                    string viewName = view.Name.ToUpperInvariant();
                    if (viewName.Contains("MECH") || viewName.Contains("HVAC")) disc = "M";
                    else if (viewName.Contains("ELEC")) disc = "E";
                    else if (viewName.Contains("PLUMB")) disc = "P";
                    else if (viewName.Contains("STRUCT")) disc = "S";
                    else disc = "A";

                    preset.Slots.Add(new LayoutSlotPreset
                    {
                        ViewType = view.ViewType.ToString(),
                        Discipline = disc,
                        NormX = Math.Round(normX, 4),
                        NormY = Math.Round(normY, 4),
                        NormW = Math.Round(normW, 4),
                        NormH = Math.Round(normH, 4),
                        Scale = view.Scale
                    });
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Could not read viewport for preset: {ex.Message}");
                }
            }

            // Save to library
            var library = LoadPresetLibrary(doc);
            library.Presets.RemoveAll(p => p.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));
            library.Presets.Add(preset);
            SavePresetLibrary(doc, library);

            StingLog.Info($"Saved layout preset '{presetName}' with {preset.Slots.Count} slots.");
            return preset;
        }

        /// <summary>
        /// Apply a saved layout preset to a sheet, repositioning viewports
        /// to match the normalised positions in the preset.
        /// Must be called within an active Transaction.
        /// </summary>
        /// <returns>Number of viewports repositioned.</returns>
        internal static int ApplyLayoutPreset(Document doc, ViewSheet sheet, LayoutPreset preset)
        {
            var zone = SheetManagerEngine.GetDrawableZone(doc, sheet);
            var vpIds = sheet.GetAllViewports().ToList();
            doc.Regenerate();

            int repositioned = 0;

            // Match viewports to preset slots by view type and discipline
            var viewports = new List<(Viewport Vp, View View)>();
            foreach (var vpId in vpIds)
            {
                var vp = doc.GetElement(vpId) as Viewport;
                if (vp == null) continue;
                var view = doc.GetElement(vp.ViewId) as View;
                if (view != null) viewports.Add((vp, view));
            }

            var usedSlots = new HashSet<int>();

            foreach (var (vp, view) in viewports)
            {
                // Find best matching slot
                int bestSlot = -1;
                int bestScore = -1;

                for (int i = 0; i < preset.Slots.Count; i++)
                {
                    if (usedSlots.Contains(i)) continue;
                    var slot = preset.Slots[i];

                    int score = 0;
                    if (view.ViewType.ToString() == slot.ViewType) score += 10;
                    if (!string.IsNullOrEmpty(slot.Discipline))
                    {
                        string viewName = view.Name.ToUpperInvariant();
                        if (slot.Discipline == "M" && (viewName.Contains("MECH") || viewName.Contains("HVAC"))) score += 5;
                        else if (slot.Discipline == "E" && viewName.Contains("ELEC")) score += 5;
                        else if (slot.Discipline == "P" && viewName.Contains("PLUMB")) score += 5;
                        else if (slot.Discipline == "S" && viewName.Contains("STRUCT")) score += 5;
                        else if (slot.Discipline == "A") score += 2;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestSlot = i;
                    }
                }

                if (bestSlot >= 0)
                {
                    usedSlots.Add(bestSlot);
                    var slot = preset.Slots[bestSlot];

                    // Denormalise position to current drawable area
                    double cx = zone.Min.X + slot.NormX * zone.Width;
                    double cy = zone.Min.Y + slot.NormY * zone.Height;

                    vp.SetBoxCenter(new XYZ(cx, cy, 0));
                    repositioned++;

                    // Optionally set scale
                    if (slot.Scale > 0 && view.Scale != slot.Scale)
                    {
                        try { view.Scale = slot.Scale; }
                        catch (Exception ex) { StingLog.Warn($"Scale locked: {ex.Message}"); }
                    }
                }
            }

            StingLog.Info($"Applied preset '{preset.Name}': repositioned {repositioned} of {viewports.Count} viewports.");
            return repositioned;
        }

        /// <summary>Load the preset library from the project-adjacent JSON file.</summary>
        internal static LayoutPresetLibrary LoadPresetLibrary(Document doc)
        {
            string path = GetPresetFilePath(doc);
            if (!File.Exists(path))
                return new LayoutPresetLibrary();

            try
            {
                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<LayoutPresetLibrary>(json) ?? new LayoutPresetLibrary();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Could not load layout presets: {ex.Message}");
                return new LayoutPresetLibrary();
            }
        }

        /// <summary>Save the preset library to the project-adjacent JSON file.</summary>
        internal static void SavePresetLibrary(Document doc, LayoutPresetLibrary library)
        {
            string path = GetPresetFilePath(doc);
            try
            {
                string json = JsonConvert.SerializeObject(library, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Could not save layout presets: {ex.Message}");
            }
        }

        private static string GetSheetPaperSize(Document doc, ViewSheet sheet)
        {
            var tb = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Cast<FamilyInstance>()
                .FirstOrDefault();

            if (tb == null) return "Unknown";
            var wParam = tb.get_Parameter(BuiltInParameter.SHEET_WIDTH);
            var hParam = tb.get_Parameter(BuiltInParameter.SHEET_HEIGHT);
            if (wParam == null || hParam == null) return "Unknown";
            return PaperSizes.Detect(wParam.AsDouble(), hParam.AsDouble());
        }


        // ═══════════════════════════════════════════════════════════════════
        //  3. VIEWPORT TYPE AUTO-ASSIGNMENT
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Built-in viewport type assignment rules.
        /// Maps view type + discipline to viewport type names.
        /// </summary>
        internal static readonly ViewportTypeRule[] DefaultViewportTypeRules =
        {
            new ViewportTypeRule { ViewTypeMatch = "FloorPlan", DisciplineMatch = null, TargetViewportTypeName = "STING Viewport" },
            new ViewportTypeRule { ViewTypeMatch = "CeilingPlan", DisciplineMatch = null, TargetViewportTypeName = "STING Viewport" },
            new ViewportTypeRule { ViewTypeMatch = "Section", DisciplineMatch = null, TargetViewportTypeName = "STING Section Viewport" },
            new ViewportTypeRule { ViewTypeMatch = "Elevation", DisciplineMatch = null, TargetViewportTypeName = "STING Elevation Viewport" },
            new ViewportTypeRule { ViewTypeMatch = "ThreeD", DisciplineMatch = null, TargetViewportTypeName = "STING 3D Viewport" },
            new ViewportTypeRule { ViewTypeMatch = "Detail", DisciplineMatch = null, TargetViewportTypeName = "STING Detail Viewport" },
            new ViewportTypeRule { ViewTypeMatch = "Legend", DisciplineMatch = null, ViewNameContains = null, TargetViewportTypeName = "STING Legend Viewport" },
        };

        /// <summary>
        /// Auto-assign viewport types to all viewports on a sheet based on rules.
        /// Falls back to first available "Viewport" type if named type not found.
        /// Must be called within an active Transaction.
        /// </summary>
        /// <returns>Number of viewports with type changed.</returns>
        internal static int AutoAssignViewportTypes(Document doc, ViewSheet sheet,
            ViewportTypeRule[] rules = null)
        {
            if (rules == null) rules = DefaultViewportTypeRules;

            // Build type lookup
            var vpTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(ElementType))
                .Cast<ElementType>()
                .Where(t => t.FamilyName == "Viewport")
                .ToDictionary(t => t.Name, t => t.Id, StringComparer.OrdinalIgnoreCase);

            int changed = 0;

            foreach (var vpId in sheet.GetAllViewports())
            {
                var vp = doc.GetElement(vpId) as Viewport;
                if (vp == null) continue;
                var view = doc.GetElement(vp.ViewId) as View;
                if (view == null) continue;

                string viewType = view.ViewType.ToString();
                string viewName = view.Name.ToUpperInvariant();

                // Find matching rule
                ViewportTypeRule bestRule = null;
                foreach (var rule in rules)
                {
                    if (rule.ViewTypeMatch != null &&
                        !viewType.Equals(rule.ViewTypeMatch, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (rule.DisciplineMatch != null)
                    {
                        bool discMatch = rule.DisciplineMatch switch
                        {
                            "M" => viewName.Contains("MECH") || viewName.Contains("HVAC"),
                            "E" => viewName.Contains("ELEC"),
                            "P" => viewName.Contains("PLUMB"),
                            "S" => viewName.Contains("STRUCT"),
                            "A" => viewName.Contains("ARCH"),
                            _ => false
                        };
                        if (!discMatch) continue;
                    }

                    if (rule.ViewNameContains != null &&
                        !viewName.Contains(rule.ViewNameContains.ToUpperInvariant()))
                        continue;

                    bestRule = rule;
                    break;
                }

                if (bestRule == null) continue;

                // Find target type
                if (vpTypes.TryGetValue(bestRule.TargetViewportTypeName, out var typeId))
                {
                    if (vp.GetTypeId() != typeId)
                    {
                        try
                        {
                            vp.ChangeTypeId(typeId);
                            changed++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Could not assign viewport type '{bestRule.TargetViewportTypeName}': {ex.Message}");
                        }
                    }
                }
            }

            return changed;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  4. SHEET SET OPERATIONS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Batch clone multiple sheets at once.
        /// Must be called within an active Transaction.
        /// </summary>
        /// <returns>List of cloned sheets.</returns>
        internal static List<ViewSheet> BatchCloneSheets(Document doc, List<ViewSheet> sourceSheets,
            bool duplicateViews = true, string nameSuffix = " (Copy)")
        {
            var cloned = new List<ViewSheet>();

            foreach (var source in sourceSheets)
            {
                string disc = SheetManagerEngine.ExtractDisciplinePrefix(source.SheetNumber);
                string nextNum = SheetManagerEngine.GetNextSheetNumber(doc, disc);

                var config = new SheetCloneConfig
                {
                    NewSheetNumber = nextNum,
                    NewSheetName = source.Name + nameSuffix,
                    CopyViewports = true,
                    CopyAnnotations = true,
                    CopySchedules = true,
                    DuplicateViews = duplicateViews,
                    DuplicateMode = ViewDuplicateOption.WithDetailing
                };

                var clone = SheetManagerEngine.CloneSheet(doc, source, config);
                if (clone != null)
                    cloned.Add(clone);
            }

            return cloned;
        }

        /// <summary>
        /// Batch renumber sheets within a discipline group.
        /// Renumbers sequentially starting from a given number.
        /// Must be called within an active Transaction.
        /// </summary>
        /// <returns>Number of sheets renumbered.</returns>
        internal static int BatchRenumberSheets(Document doc, string discipline,
            int startNumber = 1, int increment = 1)
        {
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder &&
                    SheetManagerEngine.ExtractDisciplinePrefix(s.SheetNumber)
                        .Equals(discipline, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.SheetNumber)
                .ToList();

            int num = startNumber;
            int renamed = 0;

            // First pass: rename to temporary names to avoid conflicts
            var tempNames = new Dictionary<ElementId, string>();
            foreach (var sheet in sheets)
            {
                string tempNum = $"__TEMP_{sheet.Id.Value}";
                tempNames[sheet.Id] = $"{discipline}-{num:D3}";
                try { sheet.SheetNumber = tempNum; renamed++; }
                catch (Exception ex)
                {
                    StingLog.Warn($"Could not rename sheet {sheet.SheetNumber}: {ex.Message}");
                }
                num += increment;
            }

            // Second pass: apply final names
            foreach (var kv in tempNames)
            {
                var sheet = doc.GetElement(kv.Key) as ViewSheet;
                if (sheet == null) continue;
                try { sheet.SheetNumber = kv.Value; }
                catch (Exception ex)
                {
                    StingLog.Warn($"Could not set final number '{kv.Value}': {ex.Message}");
                }
            }

            return renamed;
        }

        /// <summary>
        /// Export sheet set to CSV file.
        /// </summary>
        internal static string ExportSheetSet(Document doc, string outputPath)
        {
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber)
                .ToList();

            var lines = new List<string>
            {
                "Sheet Number,Sheet Name,Discipline,Paper Size,Viewport Count,View Names,Title Block"
            };

            foreach (var sheet in sheets)
            {
                string disc = SheetManagerEngine.ExtractDisciplinePrefix(sheet.SheetNumber);
                string paperSize = GetSheetPaperSize(doc, sheet);

                var viewNames = new List<string>();
                foreach (var vpId in sheet.GetAllViewports())
                {
                    var vp = doc.GetElement(vpId) as Viewport;
                    if (vp == null) continue;
                    var view = doc.GetElement(vp.ViewId) as View;
                    if (view != null) viewNames.Add(view.Name);
                }

                var tb = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .Cast<FamilyInstance>()
                    .FirstOrDefault();

                string tbName = tb != null ? $"{tb.Symbol.FamilyName}: {tb.Name}" : "";

                lines.Add($"\"{sheet.SheetNumber}\",\"{sheet.Name}\",\"{disc}\",\"{paperSize}\"," +
                    $"{viewNames.Count},\"{string.Join("; ", viewNames)}\",\"{tbName}\"");
            }

            File.WriteAllLines(outputPath, lines);
            StingLog.Info($"Exported sheet set ({sheets.Count} sheets) to {outputPath}");
            return outputPath;
        }


        // ═══════════════════════════════════════════════════════════════════
        //  5. OVERFLOW HANDLING — Auto-Create Continuation Sheets
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Place views on sheets with automatic overflow to new sheets.
        /// When the current sheet fills up, creates a continuation sheet
        /// and continues placing remaining views.
        /// Must be called within an active Transaction.
        /// </summary>
        /// <returns>(sheets created, views placed).</returns>
        internal static (int Sheets, int Placed) PlaceWithOverflow(
            Document doc, List<View> views, ViewSheet firstSheet,
            ElementId titleBlockTypeId, TitleBlockMargins margins = null,
            double gapMm = 8.0, bool useMaxRects = true, bool autoScale = false)
        {
            if (margins == null) margins = TitleBlockMargins.Default;

            int sheetsCreated = 0;
            int totalPlaced = 0;
            var remaining = new List<View>(views);
            var currentSheet = firstSheet;

            while (remaining.Count > 0)
            {
                var zone = SheetManagerEngine.GetDrawableZone(doc, currentSheet, margins);

                LayoutResult layout;
                if (useMaxRects)
                    layout = RunMaxRectsPacking(doc, remaining, zone, gapMm, autoScale);
                else
                    layout = SheetManagerEngine.RunShelfPacking(doc, remaining, zone, gapMm, autoScale);

                // Place the ones that fit
                var placed = SheetManagerEngine.PlaceViewports(doc, currentSheet, layout);
                totalPlaced += placed.Count;

                if (layout.OverflowSlots.Count == 0)
                    break; // all placed

                // Create continuation sheet
                string disc = SheetManagerEngine.ExtractDisciplinePrefix(currentSheet.SheetNumber);
                string nextNum = SheetManagerEngine.GetNextSheetNumber(doc, disc);

                var newSheet = ViewSheet.Create(doc, titleBlockTypeId);
                try { newSheet.SheetNumber = nextNum; }
                catch (Exception ex) { StingLog.Warn($"Number conflict: {ex.Message}"); }
                try { newSheet.Name = currentSheet.Name + " (cont.)"; }
                catch (Exception ex) { StingLog.Warn($"Name conflict: {ex.Message}"); }

                sheetsCreated++;
                currentSheet = newSheet;

                // Filter remaining to only overflow views
                var overflowIds = new HashSet<ElementId>(layout.OverflowSlots.Select(s => s.ViewId));
                remaining = remaining.Where(v => overflowIds.Contains(v.Id)).ToList();

                // Safety: prevent infinite loop if no progress is made
                if (placed.Count == 0)
                {
                    StingLog.Warn($"Overflow: no viewports could be placed on sheet '{currentSheet.SheetNumber}'. " +
                        $"{remaining.Count} views remain unplaced.");
                    break;
                }
            }

            return (sheetsCreated, totalPlaced);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  6. BUILT-IN LAYOUT PRESETS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Get built-in layout presets for common sheet configurations.
        /// </summary>
        internal static List<LayoutPreset> GetBuiltInPresets()
        {
            return new List<LayoutPreset>
            {
                new LayoutPreset
                {
                    Name = "Single View (Centred)",
                    Description = "One view centred on the sheet",
                    PaperSize = "Any",
                    Slots = new List<LayoutSlotPreset>
                    {
                        new LayoutSlotPreset { NormX = 0.5, NormY = 0.5, NormW = 0.8, NormH = 0.8, ViewType = "FloorPlan" }
                    }
                },
                new LayoutPreset
                {
                    Name = "Two Views (Side by Side)",
                    Description = "Two views arranged horizontally",
                    PaperSize = "Any",
                    Slots = new List<LayoutSlotPreset>
                    {
                        new LayoutSlotPreset { NormX = 0.28, NormY = 0.5, NormW = 0.45, NormH = 0.8, ViewType = "FloorPlan" },
                        new LayoutSlotPreset { NormX = 0.73, NormY = 0.5, NormW = 0.45, NormH = 0.8, ViewType = "FloorPlan" }
                    }
                },
                new LayoutPreset
                {
                    Name = "Two Views (Stacked)",
                    Description = "Two views arranged vertically",
                    PaperSize = "Any",
                    Slots = new List<LayoutSlotPreset>
                    {
                        new LayoutSlotPreset { NormX = 0.5, NormY = 0.73, NormW = 0.85, NormH = 0.42, ViewType = "FloorPlan" },
                        new LayoutSlotPreset { NormX = 0.5, NormY = 0.28, NormW = 0.85, NormH = 0.42, ViewType = "FloorPlan" }
                    }
                },
                new LayoutPreset
                {
                    Name = "Plan + 2 Sections",
                    Description = "Large plan with two smaller sections below",
                    PaperSize = "A1",
                    Slots = new List<LayoutSlotPreset>
                    {
                        new LayoutSlotPreset { NormX = 0.5, NormY = 0.65, NormW = 0.85, NormH = 0.55, ViewType = "FloorPlan" },
                        new LayoutSlotPreset { NormX = 0.28, NormY = 0.18, NormW = 0.42, NormH = 0.28, ViewType = "Section" },
                        new LayoutSlotPreset { NormX = 0.73, NormY = 0.18, NormW = 0.42, NormH = 0.28, ViewType = "Section" }
                    }
                },
                new LayoutPreset
                {
                    Name = "4-Up Grid",
                    Description = "Four views in a 2x2 grid",
                    PaperSize = "Any",
                    Slots = new List<LayoutSlotPreset>
                    {
                        new LayoutSlotPreset { NormX = 0.28, NormY = 0.73, NormW = 0.42, NormH = 0.42, ViewType = "FloorPlan" },
                        new LayoutSlotPreset { NormX = 0.73, NormY = 0.73, NormW = 0.42, NormH = 0.42, ViewType = "FloorPlan" },
                        new LayoutSlotPreset { NormX = 0.28, NormY = 0.28, NormW = 0.42, NormH = 0.42, ViewType = "FloorPlan" },
                        new LayoutSlotPreset { NormX = 0.73, NormY = 0.28, NormW = 0.42, NormH = 0.42, ViewType = "FloorPlan" }
                    }
                },
                new LayoutPreset
                {
                    Name = "Plan + Legend + Detail",
                    Description = "Main plan, legend bottom-left, detail bottom-right",
                    PaperSize = "A1",
                    Slots = new List<LayoutSlotPreset>
                    {
                        new LayoutSlotPreset { NormX = 0.5, NormY = 0.65, NormW = 0.85, NormH = 0.55, ViewType = "FloorPlan" },
                        new LayoutSlotPreset { NormX = 0.20, NormY = 0.15, NormW = 0.25, NormH = 0.22, ViewType = "Legend" },
                        new LayoutSlotPreset { NormX = 0.65, NormY = 0.15, NormW = 0.50, NormH = 0.22, ViewType = "Detail" }
                    }
                }
            };
        }
    }
}
