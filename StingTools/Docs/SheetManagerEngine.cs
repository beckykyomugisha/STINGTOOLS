using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Docs
{
    // ════════════════════════════════════════════════════════════════════════════
    //  SHEET MANAGER ENGINE
    //  Automated viewport layout, scale calculation, sheet cloning, and
    //  collision-free placement using shelf-packing algorithm.
    //
    //  STING Sheet Manager with auto-arrange and view-to-sheet positioning.
    //
    //  Key Revit API patterns:
    //    Viewport.Create(doc, sheetId, viewId, centerPoint)
    //    Viewport.GetBoxOutline() — sheet-space extents (excluding label)
    //    Viewport.SetBoxCenter(XYZ) — reposition on same sheet
    //    doc.Regenerate() — MUST call before reading outlines after placement
    //    Cannot move viewport between sheets — must delete + recreate
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Represents the drawable area on a sheet (usable region inside title block margins).
    /// </summary>
    internal class DrawableZone
    {
        /// <summary>Bottom-left corner in sheet coordinates (feet).</summary>
        public XYZ Min { get; set; }
        /// <summary>Top-right corner in sheet coordinates (feet).</summary>
        public XYZ Max { get; set; }
        /// <summary>Width in feet.</summary>
        public double Width => Max.X - Min.X;
        /// <summary>Height in feet.</summary>
        public double Height => Max.Y - Min.Y;
        /// <summary>Center point.</summary>
        public XYZ Center => new XYZ((Min.X + Max.X) / 2.0, (Min.Y + Max.Y) / 2.0, 0);
    }

    /// <summary>
    /// Represents a viewport's paper-space dimensions and metadata for layout.
    /// </summary>
    internal class ViewportSlot
    {
        public ElementId ViewId { get; set; }
        public string ViewName { get; set; }
        public double PaperWidth { get; set; }  // feet
        public double PaperHeight { get; set; } // feet
        public int Scale { get; set; }
        public XYZ PlacedCenter { get; set; }   // assigned by packer
        public bool Placed { get; set; }
    }

    /// <summary>
    /// Result of an auto-layout operation.
    /// </summary>
    internal class LayoutResult
    {
        public List<ViewportSlot> PlacedSlots { get; set; } = new List<ViewportSlot>();
        public List<ViewportSlot> OverflowSlots { get; set; } = new List<ViewportSlot>();
        public int SheetsUsed { get; set; } = 1;
        public string Summary { get; set; }
    }

    /// <summary>
    /// Configuration for sheet cloning operations.
    /// </summary>
    internal class SheetCloneConfig
    {
        public string NewSheetNumber { get; set; }
        public string NewSheetName { get; set; }
        public bool CopyViewports { get; set; } = true;
        public bool CopyAnnotations { get; set; } = true;
        public bool CopySchedules { get; set; } = true;
        public bool DuplicateViews { get; set; }
        public ViewDuplicateOption DuplicateMode { get; set; } = ViewDuplicateOption.WithDetailing;
    }

    /// <summary>
    /// Configuration for viewport margins within title block.
    /// Distances in millimetres from sheet edge.
    /// </summary>
    internal class TitleBlockMargins
    {
        public double LeftMm { get; set; } = 15.0;
        public double RightMm { get; set; } = 55.0;  // Title strip typically on right
        public double TopMm { get; set; } = 10.0;
        public double BottomMm { get; set; } = 15.0;
        public double GapMm { get; set; } = 8.0;     // Gap between viewports

        // Common presets
        public static TitleBlockMargins ISO_A1 => new TitleBlockMargins { LeftMm = 20, RightMm = 55, TopMm = 10, BottomMm = 15, GapMm = 10 };
        public static TitleBlockMargins ISO_A3 => new TitleBlockMargins { LeftMm = 15, RightMm = 45, TopMm = 10, BottomMm = 12, GapMm = 8 };
        public static TitleBlockMargins Compact => new TitleBlockMargins { LeftMm = 10, RightMm = 40, TopMm = 8, BottomMm = 10, GapMm = 5 };
        public static TitleBlockMargins Default => new TitleBlockMargins();

        /// <summary>
        /// Creates margins from TagConfig settings (loaded from project_config.json SHEET_MARGINS).
        /// </summary>
        public static TitleBlockMargins FromConfig => new TitleBlockMargins
        {
            LeftMm = Core.TagConfig.SheetMarginLeftMm,
            RightMm = Core.TagConfig.SheetMarginRightMm,
            TopMm = Core.TagConfig.SheetMarginTopMm,
            BottomMm = Core.TagConfig.SheetMarginBottomMm,
            GapMm = Core.TagConfig.SheetMarginGapMm
        };
    }

    /// <summary>
    /// Standard paper sizes used in AEC (mm dimensions).
    /// </summary>
    internal static class PaperSizes
    {
        public static readonly (string Name, double WidthMm, double HeightMm)[] Sizes =
        {
            ("A0",  1189, 841),
            ("A1",   841, 594),
            ("A2",   594, 420),
            ("A3",   420, 297),
            ("A4",   297, 210),
            ("ARCH D", 914.4, 609.6),
            ("ARCH E", 1219.2, 914.4),
            ("ANSI D",  863.6, 558.8),
        };

        public static string Detect(double widthFt, double heightFt)
        {
            double wMm = widthFt * 304.8;
            double hMm = heightFt * 304.8;
            foreach (var s in Sizes)
            {
                if (Math.Abs(wMm - s.WidthMm) < 5 && Math.Abs(hMm - s.HeightMm) < 5)
                    return s.Name;
                if (Math.Abs(wMm - s.HeightMm) < 5 && Math.Abs(hMm - s.WidthMm) < 5)
                    return s.Name + " (Portrait)";
            }
            return $"{wMm:F0} × {hMm:F0} mm";
        }
    }


    /// <summary>
    /// Sheet Manager Engine — core logic for automated viewport layout,
    /// scale calculation, zone detection, and sheet cloning.
    /// </summary>
    internal static class SheetManagerEngine
    {
        private const double MmToFeet = 1.0 / 304.8;

        // ── Standard AEC scales ──────────────────────────────────────────
        internal static readonly int[] StandardScales = { 1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000 };

        // ═══════════════════════════════════════════════════════════════════
        //  1. DRAWABLE ZONE DETECTION
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Detect the drawable area on a sheet by reading the title block
        /// dimensions and applying configurable margins.
        /// Uses SHEET_WIDTH / SHEET_HEIGHT built-in parameters on the title block.
        /// </summary>
        internal static DrawableZone GetDrawableZone(Document doc, ViewSheet sheet, TitleBlockMargins margins = null)
        {
            if (margins == null) margins = TitleBlockMargins.FromConfig;

            // Find title block on this sheet
            var allTitleBlocks = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Cast<FamilyInstance>()
                .ToList();

            FamilyInstance titleBlock = null;
            // Phase 77: Prefer configured title block family if specified
            string preferredFamily = Core.TagConfig.PreferredTitleBlockFamily;
            if (!string.IsNullOrEmpty(preferredFamily) && allTitleBlocks.Count > 0)
            {
                titleBlock = allTitleBlocks.FirstOrDefault(tb =>
                    tb.Symbol?.FamilyName != null &&
                    tb.Symbol.FamilyName.Equals(preferredFamily, StringComparison.OrdinalIgnoreCase));
            }
            // Fall back to first title block if preferred not found
            if (titleBlock == null)
                titleBlock = allTitleBlocks.FirstOrDefault();

            double sheetW, sheetH;

            if (titleBlock != null)
            {
                // Read from built-in parameters (most reliable)
                var wParam = titleBlock.get_Parameter(BuiltInParameter.SHEET_WIDTH);
                var hParam = titleBlock.get_Parameter(BuiltInParameter.SHEET_HEIGHT);

                if (wParam != null && hParam != null)
                {
                    sheetW = wParam.AsDouble(); // already in feet
                    sheetH = hParam.AsDouble();
                }
                else
                {
                    // Fallback: bounding box of title block
                    var bb = titleBlock.get_BoundingBox(sheet);
                    if (bb != null)
                    {
                        sheetW = bb.Max.X - bb.Min.X;
                        sheetH = bb.Max.Y - bb.Min.Y;
                    }
                    else
                    {
                        // Last resort: A1 landscape
                        sheetW = 841 * MmToFeet;
                        sheetH = 594 * MmToFeet;
                    }
                }
            }
            else
            {
                // No title block — use A1 landscape defaults
                sheetW = 841 * MmToFeet;
                sheetH = 594 * MmToFeet;
                StingLog.Warn($"Sheet '{sheet.SheetNumber}' has no title block; using A1 defaults.");
            }

            return new DrawableZone
            {
                Min = new XYZ(margins.LeftMm * MmToFeet, margins.BottomMm * MmToFeet, 0),
                Max = new XYZ(sheetW - margins.RightMm * MmToFeet, sheetH - margins.TopMm * MmToFeet, 0)
            };
        }

        /// <summary>
        /// Get existing viewport outlines on a sheet for collision detection.
        /// IMPORTANT: doc.Regenerate() should be called before this if viewports were just placed.
        /// </summary>
        internal static List<Outline> GetExistingViewportOutlines(Document doc, ViewSheet sheet)
        {
            var outlines = new List<Outline>();
            foreach (var vpId in sheet.GetAllViewports())
            {
                var vp = doc.GetElement(vpId) as Viewport;
                if (vp == null) continue;
                try
                {
                    outlines.Add(vp.GetBoxOutline());
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Could not get outline for viewport {vpId}: {ex.Message}");
                }
            }
            return outlines;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  2. SCALE CALCULATION
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Calculate the optimal scale for a view to fit within a target paper area.
        /// Snaps to the nearest standard AEC scale that ensures the view fits.
        /// </summary>
        /// <param name="view">The view to scale.</param>
        /// <param name="targetWidthFt">Available paper width in feet.</param>
        /// <param name="targetHeightFt">Available paper height in feet.</param>
        /// <returns>The optimal standard scale (e.g. 50 for 1:50), or -1 if view has no crop box.</returns>
        internal static int CalculateOptimalScale(View view, double targetWidthFt, double targetHeightFt)
        {
            BoundingBoxXYZ cropBox = null;
            try { cropBox = view.CropBox; }
            catch (Exception ex) { StingLog.Warn($"Cannot read CropBox for '{view.Name}': {ex.Message}"); }
            if (cropBox == null) return -1;

            double modelWidth = Math.Abs(cropBox.Max.X - cropBox.Min.X);
            double modelHeight = Math.Abs(cropBox.Max.Y - cropBox.Min.Y);

            if (modelWidth < 0.01 || modelHeight < 0.01) return -1;

            // Scale = model size / paper size (both in feet)
            double scaleX = modelWidth / targetWidthFt;
            double scaleY = modelHeight / targetHeightFt;
            double requiredScale = Math.Max(scaleX, scaleY);

            // Snap up to nearest standard scale
            foreach (int s in StandardScales)
            {
                if (s >= requiredScale) return s;
            }
            return StandardScales[StandardScales.Length - 1]; // max available
        }

        /// <summary>
        /// Get paper-space dimensions for a view at a given scale.
        /// </summary>
        internal static (double Width, double Height) GetPaperSize(View view, int scale)
        {
            BoundingBoxXYZ cropBox = null;
            try { cropBox = view.CropBox; }
            catch (Exception ex) { StingLog.Warn($"Sheet operation: {ex.Message}"); }
            if (cropBox == null) return (0, 0);

            double modelWidth = Math.Abs(cropBox.Max.X - cropBox.Min.X);
            double modelHeight = Math.Abs(cropBox.Max.Y - cropBox.Min.Y);

            return (modelWidth / scale, modelHeight / scale);
        }

        /// <summary>
        /// Get paper-space dimensions for a view using its current scale.
        /// </summary>
        internal static (double Width, double Height) GetPaperSize(View view)
        {
            return GetPaperSize(view, view.Scale);
        }


        // ═══════════════════════════════════════════════════════════════════
        //  3. SHELF PACKING ALGORITHM
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Shelf-based bin packing algorithm for viewport layout.
        /// Places viewports in horizontal rows (shelves), moving to the next row
        /// when the current row is full. Top-down placement for natural reading order.
        /// </summary>
        internal class ShelfPacker
        {
            // SM-CRIT-01: Made internal so callers can pre-check oversized viewports
            internal readonly double _areaWidth;
            internal readonly double _areaHeight;
            private readonly XYZ _origin;  // bottom-left of drawable area
            private readonly double _gap;  // gap between viewports (feet)

            private double _shelfY;      // current shelf Y offset from top
            private double _shelfHeight; // tallest item in current shelf
            private double _cursorX;     // current X position in shelf

            public ShelfPacker(DrawableZone zone, double gapFt)
            {
                _origin = zone.Min;
                _areaWidth = zone.Width;
                _areaHeight = zone.Height;
                _gap = gapFt;
                _shelfY = 0;
                _shelfHeight = 0;
                _cursorX = 0;
            }

            /// <summary>
            /// Try to place a viewport of the given paper dimensions.
            /// Returns the center point if placement succeeds, null if no room.
            /// </summary>
            public XYZ TryPlace(double viewWidth, double viewHeight)
            {
                // SHEET-01: Early exit for oversized viewports that can never fit
                // regardless of packing. Prevents infinite overflow sheet creation.
                if (viewWidth > _areaWidth + 0.001 || viewHeight > _areaHeight + 0.001)
                {
                    StingLog.Warn($"ShelfPacker: viewport ({viewWidth:F2} x {viewHeight:F2}) " +
                        $"exceeds drawable zone ({_areaWidth:F2} x {_areaHeight:F2}) — cannot place");
                    return null;
                }

                // Check if fits in current shelf horizontally
                if (_cursorX + viewWidth > _areaWidth + 0.001)
                {
                    // Start new shelf
                    _shelfY += _shelfHeight + _gap;
                    _shelfHeight = 0;
                    _cursorX = 0;
                }

                // Check if fits vertically
                if (_shelfY + viewHeight > _areaHeight + 0.001)
                    return null; // no room on this sheet

                // Compute center point (top-down placement)
                double cx = _origin.X + _cursorX + viewWidth / 2.0;
                double cy = _origin.Y + _areaHeight - _shelfY - viewHeight / 2.0;

                // Advance cursor
                _cursorX += viewWidth + _gap;
                _shelfHeight = Math.Max(_shelfHeight, viewHeight);

                return new XYZ(cx, cy, 0);
            }

            /// <summary>
            /// Reset packer for a new sheet.
            /// </summary>
            public void Reset()
            {
                _shelfY = 0;
                _shelfHeight = 0;
                _cursorX = 0;
            }
        }

        /// <summary>
        /// Run shelf-packing layout on a set of views for a target sheet.
        /// Views are sorted largest-first for optimal packing.
        /// </summary>
        /// <param name="doc">Revit document.</param>
        /// <param name="views">Views to place.</param>
        /// <param name="zone">Drawable area on the sheet.</param>
        /// <param name="gapMm">Gap between viewports in mm.</param>
        /// <param name="autoScale">If true, calculate optimal scale per view; otherwise use current scale.</param>
        /// <returns>Layout result with placed and overflow slots.</returns>
        internal static LayoutResult RunShelfPacking(Document doc, List<View> views,
            DrawableZone zone, double gapMm = 8.0, bool autoScale = false)
        {
            double gapFt = gapMm * MmToFeet;
            var result = new LayoutResult();
            var packer = new ShelfPacker(zone, gapFt);

            // Build slots with paper dimensions
            var slots = new List<ViewportSlot>();
            foreach (var view in views)
            {
                int scale = view.Scale;
                if (autoScale)
                {
                    int optimal = CalculateOptimalScale(view, zone.Width, zone.Height);
                    if (optimal > 0) scale = optimal;
                }

                var (w, h) = GetPaperSize(view, scale);
                if (w < 0.001 || h < 0.001) continue; // skip views without extents

                slots.Add(new ViewportSlot
                {
                    ViewId = view.Id,
                    ViewName = view.Name,
                    PaperWidth = w,
                    PaperHeight = h,
                    Scale = scale
                });
            }

            // Sort largest-first (area descending) for better packing
            slots.Sort((a, b) =>
            {
                double areaA = a.PaperWidth * a.PaperHeight;
                double areaB = b.PaperWidth * b.PaperHeight;
                return areaB.CompareTo(areaA);
            });

            // Place each slot
            // SM-CRIT-01: Distinguish oversized viewports (can NEVER fit) from overflow (no room
            // on THIS sheet). Oversized are skipped entirely to prevent infinite overflow loops.
            int oversizedCount = 0;
            foreach (var slot in slots)
            {
                // Pre-check: viewport larger than drawable zone = can never fit on any sheet
                if (slot.PaperWidth > packer._areaWidth + 0.001 || slot.PaperHeight > packer._areaHeight + 0.001)
                {
                    oversizedCount++;
                    StingLog.Warn($"ShelfPack: viewport '{slot.ViewName}' ({slot.PaperWidth:F2}x{slot.PaperHeight:F2}) " +
                        $"exceeds drawable zone — skipped (not added to overflow)");
                    continue; // Do NOT add to OverflowSlots — prevents infinite loop
                }

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
            string oversizedMsg = oversizedCount > 0 ? $" {oversizedCount} views too large for any sheet." : "";
            result.Summary = overflow > 0
                ? $"Placed {placed} viewports. {overflow} views did not fit — consider additional sheets or smaller scales.{oversizedMsg}"
                : $"Placed {placed} viewports successfully.{oversizedMsg}";

            return result;
        }


        // ═══════════════════════════════════════════════════════════════════
        //  4. VIEWPORT PLACEMENT & COLLISION DETECTION
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Check if two outlines overlap (AABB collision).
        /// </summary>
        internal static bool Overlaps(Outline a, Outline b)
        {
            return a.MinimumPoint.X < b.MaximumPoint.X &&
                   a.MaximumPoint.X > b.MinimumPoint.X &&
                   a.MinimumPoint.Y < b.MaximumPoint.Y &&
                   a.MaximumPoint.Y > b.MinimumPoint.Y;
        }

        /// <summary>
        /// Create an Outline from a center point and width/height.
        /// </summary>
        internal static Outline MakeOutline(XYZ center, double width, double height)
        {
            return new Outline(
                new XYZ(center.X - width / 2.0, center.Y - height / 2.0, 0),
                new XYZ(center.X + width / 2.0, center.Y + height / 2.0, 0));
        }

        /// <summary>
        /// Check if a proposed viewport position collides with any existing viewports.
        /// </summary>
        internal static bool HasCollision(XYZ center, double width, double height, List<Outline> existing)
        {
            var proposed = MakeOutline(center, width, height);
            foreach (var ex in existing)
            {
                if (Overlaps(proposed, ex)) return true;
            }
            return false;
        }

        /// <summary>
        /// Place viewports on a sheet according to a layout result.
        /// Creates Viewport elements in Revit and optionally sets scale.
        /// Must be called within an active Transaction.
        /// </summary>
        /// <param name="doc">Revit document.</param>
        /// <param name="sheet">Target sheet.</param>
        /// <param name="layout">Layout result from shelf packing.</param>
        /// <param name="setScale">If true, set each view's scale to the calculated optimal.</param>
        /// <returns>List of created Viewport ElementIds.</returns>
        internal static List<ElementId> PlaceViewports(Document doc, ViewSheet sheet,
            LayoutResult layout, bool setScale = true)
        {
            var created = new List<ElementId>();

            foreach (var slot in layout.PlacedSlots)
            {
                if (!Viewport.CanAddViewToSheet(doc, sheet.Id, slot.ViewId))
                {
                    StingLog.Warn($"View '{slot.ViewName}' is already on a sheet; skipping.");
                    continue;
                }

                // Set scale before placement if requested
                if (setScale)
                {
                    var view = doc.GetElement(slot.ViewId) as View;
                    if (view != null && view.Scale != slot.Scale)
                    {
                        try { view.Scale = slot.Scale; }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Could not set scale for '{slot.ViewName}': {ex.Message}");
                        }
                    }
                }

                try
                {
                    var vp = Viewport.Create(doc, sheet.Id, slot.ViewId, slot.PlacedCenter);
                    created.Add(vp.Id);
                    StingLog.Info($"Placed viewport '{slot.ViewName}' at ({slot.PlacedCenter.X:F3}, {slot.PlacedCenter.Y:F3}) scale 1:{slot.Scale}");
                }
                catch (Exception ex)
                {
                    StingLog.Error($"Failed to place viewport '{slot.ViewName}'", ex);
                }
            }

            // Regenerate to get accurate outlines, then fine-tune positions
            if (created.Count > 0)
            {
                doc.Regenerate();
            }

            return created;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  5. VIEWPORT REDISTRIBUTION (move between sheets)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Move a viewport to a different sheet. Since the Revit API does not support
        /// reassigning viewports, this deletes the old viewport and creates a new one.
        /// Must be called within an active Transaction.
        /// </summary>
        /// <returns>The new Viewport, or null on failure.</returns>
        internal static Viewport MoveViewportToSheet(Document doc, Viewport viewport,
            ViewSheet targetSheet, XYZ newCenter = null)
        {
            if (viewport == null || targetSheet == null) return null;

            var viewId = viewport.ViewId;
            var typeId = viewport.GetTypeId();
            var center = newCenter ?? viewport.GetBoxCenter();

            // Delete the old viewport FIRST — Revit's CanAddViewToSheet returns false
            // while the view is still placed on the source sheet, so we must remove it
            // before checking or creating on the target.
            doc.Delete(viewport.Id);

            if (!Viewport.CanAddViewToSheet(doc, targetSheet.Id, viewId))
            {
                StingLog.Warn($"Cannot move viewport: view cannot be placed on target sheet '{targetSheet.SheetNumber}'.");
                return null;
            }

            try
            {
                var newVp = Viewport.Create(doc, targetSheet.Id, viewId, center);
                if (newVp == null)
                {
                    StingLog.Warn($"Viewport.Create returned null for sheet '{targetSheet.SheetNumber}'.");
                    return null;
                }
                if (typeId != ElementId.InvalidElementId)
                {
                    try { newVp.ChangeTypeId(typeId); }
                    catch (Exception ex) { StingLog.Warn($"Viewport type: {ex.Message}"); }
                }
                return newVp;
            }
            catch (Exception ex)
            {
                StingLog.Error($"Failed to recreate viewport on sheet '{targetSheet.SheetNumber}'", ex);
                return null;
            }
        }


        // ═══════════════════════════════════════════════════════════════════
        //  6. SHEET CLONING
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Clone a sheet with its title block, annotations, schedules, and optionally viewports.
        /// Viewports can reference the same views or duplicate them.
        /// Must be called within an active Transaction.
        /// </summary>
        /// <returns>The cloned ViewSheet, or null on failure.</returns>
        internal static ViewSheet CloneSheet(Document doc, ViewSheet source, SheetCloneConfig config)
        {
            if (source == null || config == null) return null;

            // Find title block family type used on source
            var sourceTitleBlock = new FilteredElementCollector(doc, source.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Cast<FamilyInstance>()
                .FirstOrDefault();

            ElementId titleBlockTypeId = sourceTitleBlock?.GetTypeId() ?? ElementId.InvalidElementId;

            // Create new sheet
            ViewSheet newSheet;
            if (titleBlockTypeId != ElementId.InvalidElementId)
            {
                newSheet = ViewSheet.Create(doc, titleBlockTypeId);
            }
            else
            {
                newSheet = ViewSheet.Create(doc, ElementId.InvalidElementId);
            }

            // Set sheet number and name
            try { newSheet.SheetNumber = config.NewSheetNumber; }
            catch (Exception ex)
            {
                StingLog.Warn($"Could not set sheet number '{config.NewSheetNumber}': {ex.Message}");
            }

            try { newSheet.Name = config.NewSheetName; }
            catch (Exception ex)
            {
                StingLog.Warn($"Could not set sheet name '{config.NewSheetName}': {ex.Message}");
            }

            // Copy viewports
            if (config.CopyViewports)
            {
                foreach (var vpId in source.GetAllViewports())
                {
                    var vp = doc.GetElement(vpId) as Viewport;
                    if (vp == null) continue;

                    var view = doc.GetElement(vp.ViewId) as View;
                    if (view == null) continue;

                    ElementId viewIdToPlace;

                    if (config.DuplicateViews)
                    {
                        // Duplicate the view
                        try
                        {
                            viewIdToPlace = view.Duplicate(config.DuplicateMode);
                        }
                        catch (Exception ex2)
                        {
                            StingLog.Warn($"Could not duplicate view '{view.Name}': {ex2.Message}");
                            continue;
                        }
                    }
                    else
                    {
                        // Reference the same view (only works if not already on another sheet)
                        viewIdToPlace = vp.ViewId;
                    }

                    if (Viewport.CanAddViewToSheet(doc, newSheet.Id, viewIdToPlace))
                    {
                        try
                        {
                            var newVp = Viewport.Create(doc, newSheet.Id, viewIdToPlace, vp.GetBoxCenter());
                            var typeId = vp.GetTypeId();
                            if (typeId != ElementId.InvalidElementId)
                            {
                                try { newVp.ChangeTypeId(typeId); }
                                catch (Exception ex2) { StingLog.Warn($"Type not available: {ex2.Message}"); }
                            }
                        }
                        catch (Exception ex2)
                        {
                            StingLog.Warn($"Could not place viewport for '{view.Name}' on cloned sheet: {ex.Message}");
                        }
                    }
                }
            }

            // Copy schedule instances (schedules placed on sheets)
            if (config.CopySchedules)
            {
                var scheduleInstances = new FilteredElementCollector(doc, source.Id)
                    .OfClass(typeof(ScheduleSheetInstance))
                    .Cast<ScheduleSheetInstance>()
                    .ToList();

                foreach (var ssi in scheduleInstances)
                {
                    try
                    {
                        ScheduleSheetInstance.Create(doc, newSheet.Id, ssi.ScheduleId, ssi.Point);
                    }
                    catch (Exception ex2)
                    {
                        StingLog.Warn($"Could not copy schedule to cloned sheet: {ex2.Message}");
                    }
                }
            }

            StingLog.Info($"Cloned sheet '{source.SheetNumber}' → '{config.NewSheetNumber} - {config.NewSheetName}'");
            return newSheet;
        }


        // ═══════════════════════════════════════════════════════════════════
        //  7. SHEET NAMING & NUMBERING
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Get the next available sheet number for a discipline prefix.
        /// Scans existing sheets and returns max + 1.
        /// </summary>
        internal static string GetNextSheetNumber(Document doc, string disciplinePrefix)
        {
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .ToList();

            int maxNum = 0;
            string prefix = disciplinePrefix.ToUpperInvariant();

            foreach (var sheet in sheets)
            {
                string num = sheet.SheetNumber;
                if (num.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string rest = num.Substring(prefix.Length).TrimStart('-', '_', ' ');
                    if (int.TryParse(rest, out int n) && n > maxNum)
                        maxNum = n;
                }
            }

            return $"{prefix}-{(maxNum + 1):D3}";
        }

        /// <summary>
        /// Validate a sheet number is unique in the document.
        /// </summary>
        internal static bool IsSheetNumberAvailable(Document doc, string sheetNumber)
        {
            return !new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Any(s => s.SheetNumber.Equals(sheetNumber, StringComparison.OrdinalIgnoreCase));
        }

        // ═══════════════════════════════════════════════════════════════════
        //  8. SHEET & VIEWPORT QUERIES
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Get all sheets grouped by discipline prefix.
        /// </summary>
        internal static Dictionary<string, List<ViewSheet>> GetSheetsByDiscipline(Document doc)
        {
            var result = new Dictionary<string, List<ViewSheet>>(StringComparer.OrdinalIgnoreCase);

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber)
                .ToList();

            foreach (var sheet in sheets)
            {
                string disc = ExtractDisciplinePrefix(sheet.SheetNumber);
                if (!result.ContainsKey(disc))
                    result[disc] = new List<ViewSheet>();
                result[disc].Add(sheet);
            }

            return result;
        }

        /// <summary>
        /// Extract discipline prefix from a sheet number (e.g. "A-101" → "A", "M-301" → "M").
        /// </summary>
        internal static string ExtractDisciplinePrefix(string sheetNumber)
        {
            if (string.IsNullOrWhiteSpace(sheetNumber)) return "?";
            int dashIdx = sheetNumber.IndexOfAny(new[] { '-', '_', ' ' });
            return dashIdx > 0
                ? sheetNumber.Substring(0, dashIdx).ToUpperInvariant()
                : sheetNumber.Substring(0, Math.Min(2, sheetNumber.Length)).ToUpperInvariant();
        }

        /// <summary>
        /// Get all views that are not placed on any sheet.
        /// </summary>
        internal static List<View> GetUnplacedViews(Document doc)
        {
            // Build set of views that ARE on sheets
            var placedViewIds = new HashSet<ElementId>();
            foreach (var sheet in new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
            {
                foreach (var vpId in sheet.GetAllViewports())
                {
                    var vp = doc.GetElement(vpId) as Viewport;
                    if (vp != null)
                        placedViewIds.Add(vp.ViewId);
                }
            }

            // Collect views not in the placed set
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate
                    && !(v is ViewSheet)
                    && !(v is ViewSchedule)
                    && !placedViewIds.Contains(v.Id)
                    && v.CanBePrinted)
                .OrderBy(v => v.Name)
                .ToList();
        }

        /// <summary>
        /// Get sheet info summary for display.
        /// </summary>
        internal static string GetSheetSummary(Document doc, ViewSheet sheet)
        {
            var vpIds = sheet.GetAllViewports().ToList();
            var zone = GetDrawableZone(doc, sheet);
            var titleBlock = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Cast<FamilyInstance>()
                .FirstOrDefault();

            string tbName = titleBlock != null
                ? $"{titleBlock.Symbol.FamilyName}: {titleBlock.Name}"
                : "(no title block)";

            string paperSize = "Unknown";
            if (titleBlock != null)
            {
                var wParam = titleBlock.get_Parameter(BuiltInParameter.SHEET_WIDTH);
                var hParam = titleBlock.get_Parameter(BuiltInParameter.SHEET_HEIGHT);
                if (wParam != null && hParam != null)
                    paperSize = PaperSizes.Detect(wParam.AsDouble(), hParam.AsDouble());
            }

            return $"Sheet: {sheet.SheetNumber} - {sheet.Name}\n" +
                   $"Title Block: {tbName}\n" +
                   $"Paper Size: {paperSize}\n" +
                   $"Viewports: {vpIds.Count}\n" +
                   $"Drawable Area: {zone.Width * 304.8:F0} × {zone.Height * 304.8:F0} mm";
        }

        /// <summary>
        /// Get viewport info summary for display.
        /// </summary>
        internal static string GetViewportSummary(Document doc, Viewport vp)
        {
            var view = doc.GetElement(vp.ViewId) as View;
            if (view == null) return "(invalid viewport)";

            var outline = vp.GetBoxOutline();
            double wMm = (outline.MaximumPoint.X - outline.MinimumPoint.X) * 304.8;
            double hMm = (outline.MaximumPoint.Y - outline.MinimumPoint.Y) * 304.8;

            return $"View: {view.Name}\n" +
                   $"Scale: 1:{view.Scale}\n" +
                   $"Paper Size: {wMm:F0} × {hMm:F0} mm\n" +
                   $"Center: ({vp.GetBoxCenter().X * 304.8:F1}, {vp.GetBoxCenter().Y * 304.8:F1}) mm";
        }

        // ═══════════════════════════════════════════════════════════════════
        //  9. AUTO-ARRANGE EXISTING VIEWPORTS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Re-arrange all existing viewports on a sheet using shelf packing.
        /// Must be called within an active Transaction.
        /// </summary>
        /// <returns>Number of viewports rearranged.</returns>
        internal static int ArrangeViewportsOnSheet(Document doc, ViewSheet sheet,
            TitleBlockMargins margins = null, double gapMm = 8.0)
        {
            var zone = GetDrawableZone(doc, sheet, margins);
            var vpIds = sheet.GetAllViewports().ToList();
            if (vpIds.Count == 0) return 0;

            // Collect viewport data
            doc.Regenerate();
            var vpData = new List<(Viewport Vp, Outline Outline)>();
            foreach (var vpId in vpIds)
            {
                var vp = doc.GetElement(vpId) as Viewport;
                if (vp == null) continue;
                try
                {
                    vpData.Add((vp, vp.GetBoxOutline()));
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Skipping viewport {vpId}: {ex.Message}");
                }
            }

            // Sort by area (largest first)
            vpData.Sort((a, b) =>
            {
                double areaA = (a.Outline.MaximumPoint.X - a.Outline.MinimumPoint.X) *
                               (a.Outline.MaximumPoint.Y - a.Outline.MinimumPoint.Y);
                double areaB = (b.Outline.MaximumPoint.X - b.Outline.MinimumPoint.X) *
                               (b.Outline.MaximumPoint.Y - b.Outline.MinimumPoint.Y);
                return areaB.CompareTo(areaA);
            });

            // Run shelf packing on existing viewports
            double gapFt = gapMm * MmToFeet;
            var packer = new ShelfPacker(zone, gapFt);
            int moved = 0;

            foreach (var (vp, outline) in vpData)
            {
                double w = outline.MaximumPoint.X - outline.MinimumPoint.X;
                double h = outline.MaximumPoint.Y - outline.MinimumPoint.Y;

                var newCenter = packer.TryPlace(w, h);
                if (newCenter != null)
                {
                    vp.SetBoxCenter(newCenter);
                    moved++;
                }
            }

            return moved;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  10. BATCH OPERATIONS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Create sheets and auto-place views using shelf packing.
        /// Groups views by discipline, creates sheets per discipline, and places views.
        /// Must be called within an active Transaction.
        /// </summary>
        internal static (int SheetsCreated, int ViewsPlaced) BatchCreateAndPlace(
            Document doc, List<View> views, ElementId titleBlockTypeId,
            TitleBlockMargins margins = null, double gapMm = 8.0, bool autoScale = false)
        {
            if (margins == null) margins = TitleBlockMargins.Default;

            // Group views by type/discipline
            var groups = views.GroupBy(v =>
            {
                string name = v.Name.ToUpperInvariant();
                if (name.Contains("MECHANICAL") || name.Contains("HVAC")) return "M";
                if (name.Contains("ELECTRICAL")) return "E";
                if (name.Contains("PLUMBING")) return "P";
                if (name.Contains("STRUCTURAL")) return "S";
                if (name.Contains("FIRE")) return "FP";
                return "A"; // default to Architectural
            }).OrderBy(g => g.Key);

            int sheetsCreated = 0;
            int viewsPlaced = 0;

            foreach (var group in groups)
            {
                string disc = group.Key;
                var viewList = group.ToList();

                // Create sheet
                string sheetNum = GetNextSheetNumber(doc, disc);
                var sheet = ViewSheet.Create(doc, titleBlockTypeId);
                try { sheet.SheetNumber = sheetNum; }
                catch (Exception ex) { StingLog.Warn($"Number conflict: {ex.Message}"); }

                string viewTypes = string.Join(", ", viewList.Select(v => v.ViewType.ToString()).Distinct());
                try { sheet.Name = $"{disc} - {viewTypes}"; }
                catch (Exception ex) { StingLog.Warn($"Name conflict: {ex.Message}"); }

                sheetsCreated++;

                // Get drawable zone and run packing
                var zone = GetDrawableZone(doc, sheet, margins);
                var layout = RunShelfPacking(doc, viewList, zone, gapMm, autoScale);

                // Place viewports
                var placed = PlaceViewports(doc, sheet, layout);
                viewsPlaced += placed.Count;

                if (layout.OverflowSlots.Count > 0)
                {
                    StingLog.Info($"Sheet '{sheetNum}': {layout.OverflowSlots.Count} views overflowed.");
                }
            }

            return (sheetsCreated, viewsPlaced);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  11. VIEWPORT TYPE MANAGEMENT
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Get all available viewport types in the document.
        /// </summary>
        internal static List<ElementType> GetViewportTypes(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ElementType))
                .Cast<ElementType>()
                .Where(t => t.FamilyName == "Viewport")
                .OrderBy(t => t.Name)
                .ToList();
        }

        /// <summary>
        /// Batch-change viewport type for all viewports on a sheet.
        /// Must be called within an active Transaction.
        /// </summary>
        internal static int SetViewportType(Document doc, ViewSheet sheet, ElementId viewportTypeId)
        {
            int changed = 0;
            foreach (var vpId in sheet.GetAllViewports())
            {
                var vp = doc.GetElement(vpId) as Viewport;
                if (vp == null) continue;
                try
                {
                    vp.ChangeTypeId(viewportTypeId);
                    changed++;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Could not change viewport type: {ex.Message}");
                }
            }
            return changed;
        }
    }
}

