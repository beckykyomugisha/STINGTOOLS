using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// Smart Tag Placement: creates IndependentTag annotations with collision-free positioning.
    /// Uses priority-based placement (8 quadrants), bounding-box overlap detection,
    /// and automatic leader insertion when displacement exceeds threshold.
    ///
    /// Inspired by BIMLOGiQ Smart Annotation and Naviate Tag from Template.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SmartTagPlacementCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            if (view is ViewSheet || view is ViewSchedule || view is ViewDrafting)
            {
                TaskDialog.Show("Smart Tag Placement",
                    "This command works on plan, section, 3D, and elevation views.\n" +
                    "Switch to a model view first.");
                return Result.Failed;
            }

            // Scope
            TaskDialog scopeDlg = new TaskDialog("Smart Tag Placement");
            scopeDlg.MainInstruction = "Place annotation tags with collision avoidance";
            scopeDlg.MainContent =
                "Creates IndependentTag annotations for elements that display\n" +
                "the ISO 19650 tag value. Avoids overlaps automatically.";
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Tag untagged elements in view",
                "Only place tags on elements that don't already have annotation tags");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Tag selected elements",
                $"Place tags on {uidoc.Selection.GetElementIds().Count} selected elements");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Re-tag all elements in view",
                "Place tags on ALL elements (may create duplicates)");
            scopeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            List<Element> targetElements;
            bool skipAlreadyTagged;
            switch (scopeDlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    skipAlreadyTagged = true;
                    targetElements = SmartPlacementEngine.GetUntaggedElements(doc, view);
                    break;
                case TaskDialogResult.CommandLink2:
                    skipAlreadyTagged = false;
                    targetElements = uidoc.Selection.GetElementIds()
                        .Select(id => doc.GetElement(id))
                        .Where(e => e != null && e.Category != null)
                        .ToList();
                    break;
                case TaskDialogResult.CommandLink3:
                    skipAlreadyTagged = false;
                    targetElements = new FilteredElementCollector(doc, view.Id)
                        .WhereElementIsNotElementType()
                        .Where(e => e.Category != null)
                        .ToList();
                    break;
                default:
                    return Result.Cancelled;
            }

            if (targetElements.Count == 0)
            {
                TaskDialog.Show("Smart Tag Placement",
                    skipAlreadyTagged
                        ? "All elements already have annotation tags."
                        : "No elements found in scope.");
                return Result.Cancelled;
            }

            // Leader mode
            TaskDialog leaderDlg = new TaskDialog("Leader Mode");
            leaderDlg.MainInstruction = "Leader line behavior";
            leaderDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Auto (recommended)",
                "Add leader only when tag must be displaced to avoid collision");
            leaderDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Always", "Always add a leader line from tag to element");
            leaderDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Never", "Never add leaders (tag placed at element, may overlap)");
            leaderDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            SmartPlacementEngine.LeaderMode leaderMode;
            switch (leaderDlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    leaderMode = SmartPlacementEngine.LeaderMode.Auto; break;
                case TaskDialogResult.CommandLink2:
                    leaderMode = SmartPlacementEngine.LeaderMode.Always; break;
                case TaskDialogResult.CommandLink3:
                    leaderMode = SmartPlacementEngine.LeaderMode.Never; break;
                default:
                    return Result.Cancelled;
            }

            var sw = Stopwatch.StartNew();
            var result = SmartPlacementEngine.PlaceTags(doc, view,
                targetElements, leaderMode);
            sw.Stop();

            var report = new StringBuilder();
            report.AppendLine("Smart Tag Placement Complete");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Tags placed:     {result.Placed}");
            report.AppendLine($"  With leaders:    {result.WithLeaders}");
            report.AppendLine($"  Collisions avoided: {result.CollisionsAvoided}");
            report.AppendLine($"  Skipped (no tag type): {result.Skipped}");
            report.AppendLine($"  Failed:          {result.Failed}");
            report.AppendLine($"  Duration:        {sw.Elapsed.TotalSeconds:F1}s");

            TaskDialog td = new TaskDialog("Smart Tag Placement");
            td.MainInstruction = $"Placed {result.Placed} annotation tags";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"SmartTagPlacement: placed={result.Placed}, leaders={result.WithLeaders}, " +
                $"collisions={result.CollisionsAvoided}, elapsed={sw.Elapsed.TotalSeconds:F1}s");

            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Arrange existing tags: repositions already-placed tags to resolve overlaps
    /// using the same scoring algorithm as Smart Tag Placement.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ArrangeTagsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            // Collect existing tags in view
            var tags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            if (tags.Count == 0)
            {
                TaskDialog.Show("Arrange Tags",
                    "No annotation tags found in the active view.");
                return Result.Cancelled;
            }

            // Scope
            TaskDialog scopeDlg = new TaskDialog("Arrange Tags");
            scopeDlg.MainInstruction = $"Rearrange {tags.Count} tags to reduce overlaps";
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"All {tags.Count} tags in view",
                "Optimize positions for all annotation tags");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Selected tags only",
                $"Optimize only {uidoc.Selection.GetElementIds().Count} selected");
            scopeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            ICollection<IndependentTag> targetTags;
            switch (scopeDlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    targetTags = tags;
                    break;
                case TaskDialogResult.CommandLink2:
                    var selIds = new HashSet<ElementId>(uidoc.Selection.GetElementIds());
                    targetTags = tags.Where(t => selIds.Contains(t.Id)).ToList();
                    if (targetTags.Count == 0)
                    {
                        TaskDialog.Show("Arrange Tags", "No tags in selection.");
                        return Result.Cancelled;
                    }
                    break;
                default:
                    return Result.Cancelled;
            }

            var sw = Stopwatch.StartNew();
            int moved = 0;

            using (Transaction tx = new Transaction(doc, "STING Arrange Tags"))
            {
                tx.Start();

                // Build occupied regions from ALL tags first
                var occupiedBoxes = new List<BoundingBoxXYZ>();
                foreach (IndependentTag tag in tags)
                {
                    BoundingBoxXYZ bb = tag.get_BoundingBox(view);
                    if (bb != null) occupiedBoxes.Add(bb);
                }

                foreach (IndependentTag tag in targetTags)
                {
                    try
                    {
                        // Get host element location
                        ElementId hostId = tag.TaggedLocalElementId;
                        if (hostId == ElementId.InvalidElementId) continue;

                        Element host = doc.GetElement(hostId);
                        if (host == null) continue;

                        XYZ hostCenter = SmartPlacementEngine.GetElementCenter(host, view);
                        if (hostCenter == null) continue;

                        // Get current tag bounding box
                        BoundingBoxXYZ currentBB = tag.get_BoundingBox(view);
                        if (currentBB == null) continue;

                        double tagWidth = currentBB.Max.X - currentBB.Min.X;
                        double tagHeight = currentBB.Max.Y - currentBB.Min.Y;

                        // Try each candidate position
                        double offset = SmartPlacementEngine.GetScaleAwareOffset(view);
                        XYZ[] candidates = SmartPlacementEngine.GetCandidatePositions(
                            hostCenter, offset);

                        XYZ best = null;
                        double bestScore = double.MinValue;

                        foreach (XYZ candidate in candidates)
                        {
                            double score = SmartPlacementEngine.ScorePosition(
                                candidate, tagWidth, tagHeight, hostCenter,
                                occupiedBoxes, offset);

                            if (score > bestScore)
                            {
                                bestScore = score;
                                best = candidate;
                            }
                        }

                        if (best != null)
                        {
                            XYZ currentPos = tag.TagHeadPosition;
                            double dist = currentPos.DistanceTo(best);

                            // Only move if significantly better (> 0.5 * offset)
                            if (dist > offset * 0.3)
                            {
                                tag.TagHeadPosition = best;
                                moved++;

                                // Update occupied boxes
                                BoundingBoxXYZ newBB = tag.get_BoundingBox(view);
                                if (newBB != null) occupiedBoxes.Add(newBB);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"ArrangeTags: {ex.Message}");
                    }
                }

                tx.Commit();
            }

            sw.Stop();
            TaskDialog.Show("Arrange Tags",
                $"Repositioned {moved} of {targetTags.Count} tags.\n" +
                $"Duration: {sw.Elapsed.TotalSeconds:F1}s");

            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Batch Tag Views: place annotation tags across multiple views.
    /// Processes a list of views with progress reporting.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchTagViewsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            // Collect floor plan views
            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => !v.IsTemplate && v.CanBePrinted)
                .OrderBy(v => v.Name)
                .ToList();

            if (views.Count == 0)
            {
                TaskDialog.Show("Batch Tag Views", "No floor plan views found.");
                return Result.Cancelled;
            }

            TaskDialog confirm = new TaskDialog("Batch Tag Views");
            confirm.MainInstruction = $"Place annotation tags in {views.Count} floor plan views?";
            confirm.MainContent =
                "This will place tags on untagged elements in each view.\n" +
                "Leader lines will be added automatically when needed.\n\n" +
                "This may take several minutes for large projects.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            var sw = Stopwatch.StartNew();
            int totalPlaced = 0;
            int viewsProcessed = 0;
            var perView = new List<(string name, int count)>();

            using (Transaction tx = new Transaction(doc, "STING Batch Tag Views"))
            {
                tx.Start();

                foreach (ViewPlan vp in views)
                {
                    try
                    {
                        var untagged = SmartPlacementEngine.GetUntaggedElements(doc, vp);
                        if (untagged.Count == 0) continue;

                        var result = SmartPlacementEngine.PlaceTagsInTransaction(
                            doc, vp, untagged, SmartPlacementEngine.LeaderMode.Auto);

                        totalPlaced += result.Placed;
                        viewsProcessed++;
                        perView.Add((vp.Name, result.Placed));

                        StingLog.Info($"BatchTagViews: {vp.Name} — {result.Placed} tags placed");
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"BatchTagViews '{vp.Name}': {ex.Message}");
                    }
                }

                tx.Commit();
            }

            sw.Stop();

            var report = new StringBuilder();
            report.AppendLine($"Batch Tag Views Complete — {sw.Elapsed.TotalSeconds:F1}s");
            report.AppendLine($"Views processed: {viewsProcessed} of {views.Count}");
            report.AppendLine($"Total tags placed: {totalPlaced}");
            report.AppendLine();
            foreach (var (name, count) in perView.Take(20))
                report.AppendLine($"  {name}: {count}");
            if (perView.Count > 20)
                report.AppendLine($"  ... and {perView.Count - 20} more");

            TaskDialog.Show("Batch Tag Views", report.ToString());
            return Result.Succeeded;
        }
    }

    // ── Smart Placement Engine ──

    /// <summary>
    /// Core placement engine: priority-based positioning with AABB overlap detection.
    /// Used by SmartTagPlacement, ArrangeTags, and BatchTagViews.
    /// </summary>
    internal static class SmartPlacementEngine
    {
        public enum LeaderMode { Auto, Always, Never }

        public class PlacementResult
        {
            public int Placed;
            public int WithLeaders;
            public int CollisionsAvoided;
            public int Skipped;
            public int Failed;
        }

        /// <summary>Get elements without annotation tags in a view.</summary>
        public static List<Element> GetUntaggedElements(Document doc, View view)
        {
            // Get IDs of already-tagged elements
            var existingTags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            var taggedIds = new HashSet<ElementId>(
                existingTags
                    .Where(t => t.TaggedLocalElementId != ElementId.InvalidElementId)
                    .Select(t => t.TaggedLocalElementId));

            return new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && !taggedIds.Contains(e.Id))
                .ToList();
        }

        /// <summary>Place tags with a new transaction (for single-view use).</summary>
        public static PlacementResult PlaceTags(Document doc, View view,
            List<Element> targetElements, LeaderMode leaderMode)
        {
            var result = new PlacementResult();

            using (Transaction tx = new Transaction(doc, "STING Smart Tag Placement"))
            {
                tx.Start();
                result = PlaceTagsInTransaction(doc, view, targetElements, leaderMode);
                tx.Commit();
            }

            return result;
        }

        /// <summary>Place tags within an existing transaction (for batch use).</summary>
        public static PlacementResult PlaceTagsInTransaction(Document doc, View view,
            List<Element> targetElements, LeaderMode leaderMode)
        {
            var result = new PlacementResult();

            // Build occupied region index from existing tags
            var occupiedBoxes = new List<BoundingBoxXYZ>();
            foreach (IndependentTag existingTag in new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
            {
                BoundingBoxXYZ bb = existingTag.get_BoundingBox(view);
                if (bb != null) occupiedBoxes.Add(bb);
            }

            // Find available tag types (family symbols)
            var tagTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Category != null &&
                    fs.Category.Id.IntegerValue == (int)BuiltInCategory.OST_GenericAnnotation ||
                    fs.Category.Id.IntegerValue == (int)BuiltInCategory.OST_MechanicalEquipmentTags ||
                    fs.Category.Id.IntegerValue == (int)BuiltInCategory.OST_ElectricalEquipmentTags ||
                    fs.Category.Id.IntegerValue == (int)BuiltInCategory.OST_PlumbingFixtureTags ||
                    fs.Category.Id.IntegerValue == (int)BuiltInCategory.OST_LightingFixtureTags ||
                    fs.Category.Id.IntegerValue == (int)BuiltInCategory.OST_FurnitureTags ||
                    fs.Category.Id.IntegerValue == (int)BuiltInCategory.OST_DoorTags ||
                    fs.Category.Id.IntegerValue == (int)BuiltInCategory.OST_WindowTags)
                .ToDictionary(
                    fs => fs.Category?.Id.IntegerValue ?? 0,
                    fs => fs.Id);

            double offset = GetScaleAwareOffset(view);

            foreach (Element el in targetElements)
            {
                try
                {
                    XYZ center = GetElementCenter(el, view);
                    if (center == null)
                    {
                        result.Skipped++;
                        continue;
                    }

                    // Find appropriate tag type for this element's category
                    ElementId tagTypeId = FindTagType(doc, el);
                    if (tagTypeId == ElementId.InvalidElementId)
                    {
                        result.Skipped++;
                        continue;
                    }

                    // Generate candidate positions
                    XYZ[] candidates = GetCandidatePositions(center, offset);

                    // Score each position (approximate tag size)
                    double estWidth = offset * 2.5;
                    double estHeight = offset * 0.8;

                    XYZ bestPos = center;
                    double bestScore = double.MinValue;
                    bool needsLeader = false;

                    foreach (XYZ candidate in candidates)
                    {
                        double score = ScorePosition(candidate, estWidth, estHeight,
                            center, occupiedBoxes, offset);

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestPos = candidate;
                        }
                    }

                    // If best position is displaced, may need leader
                    double displacement = bestPos.DistanceTo(center);
                    needsLeader = displacement > offset * 0.5;

                    bool addLeader = leaderMode switch
                    {
                        LeaderMode.Always => true,
                        LeaderMode.Never => false,
                        _ => needsLeader
                    };

                    if (bestScore < -500)
                    {
                        // All positions have severe overlaps — expand search
                        XYZ[] extended = GetCandidatePositions(center, offset * 2.5);
                        foreach (XYZ candidate in extended)
                        {
                            double score = ScorePosition(candidate, estWidth, estHeight,
                                center, occupiedBoxes, offset);
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestPos = candidate;
                                needsLeader = true;
                            }
                        }

                        if (leaderMode != LeaderMode.Never)
                            addLeader = true;

                        result.CollisionsAvoided++;
                    }

                    // Place the tag
                    Reference elRef = new Reference(el);
                    IndependentTag newTag = IndependentTag.Create(
                        doc, view.Id, elRef, addLeader,
                        TagMode.TM_ADDBY_CATEGORY,
                        TagOrientation.Horizontal,
                        bestPos);

                    if (newTag != null)
                    {
                        result.Placed++;
                        if (addLeader) result.WithLeaders++;

                        // Register placed tag in occupied regions
                        BoundingBoxXYZ newBB = newTag.get_BoundingBox(view);
                        if (newBB != null) occupiedBoxes.Add(newBB);
                    }
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    // No tag type available for this category — skip
                    result.Skipped++;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    StingLog.Warn($"SmartPlacement: {ex.Message}");
                }
            }

            return result;
        }

        /// <summary>Find appropriate tag type for an element category.</summary>
        private static ElementId FindTagType(Document doc, Element el)
        {
            if (el.Category == null)
                return ElementId.InvalidElementId;

            // Try to find a tag for this specific category
            BuiltInCategory hostCat = (BuiltInCategory)el.Category.Id.IntegerValue;

            // Map host category to tag category
            BuiltInCategory? tagCat = hostCat switch
            {
                BuiltInCategory.OST_MechanicalEquipment => BuiltInCategory.OST_MechanicalEquipmentTags,
                BuiltInCategory.OST_ElectricalEquipment => BuiltInCategory.OST_ElectricalEquipmentTags,
                BuiltInCategory.OST_PlumbingFixtures => BuiltInCategory.OST_PlumbingFixtureTags,
                BuiltInCategory.OST_LightingFixtures => BuiltInCategory.OST_LightingFixtureTags,
                BuiltInCategory.OST_Furniture => BuiltInCategory.OST_FurnitureTags,
                BuiltInCategory.OST_Doors => BuiltInCategory.OST_DoorTags,
                BuiltInCategory.OST_Windows => BuiltInCategory.OST_WindowTags,
                BuiltInCategory.OST_Rooms => BuiltInCategory.OST_RoomTags,
                BuiltInCategory.OST_DuctAccessory => BuiltInCategory.OST_DuctAccessoryTags,
                BuiltInCategory.OST_PipeAccessory => BuiltInCategory.OST_PipeAccessoryTags,
                BuiltInCategory.OST_Sprinklers => BuiltInCategory.OST_SprinklerTags,
                BuiltInCategory.OST_FireAlarmDevices => BuiltInCategory.OST_FireAlarmDeviceTags,
                BuiltInCategory.OST_DuctTerminal => BuiltInCategory.OST_DuctTerminalTags,
                _ => null
            };

            if (tagCat == null)
                return ElementId.InvalidElementId;

            // Find first available tag family for this tag category
            var tagType = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs => fs.Category != null &&
                    fs.Category.Id.IntegerValue == (int)tagCat.Value);

            return tagType?.Id ?? ElementId.InvalidElementId;
        }

        /// <summary>Get element center point projected into the view plane.</summary>
        public static XYZ GetElementCenter(Element el, View view)
        {
            try
            {
                // Try bounding box in view first
                BoundingBoxXYZ bb = el.get_BoundingBox(view);
                if (bb != null)
                {
                    return new XYZ(
                        (bb.Min.X + bb.Max.X) / 2.0,
                        (bb.Min.Y + bb.Max.Y) / 2.0,
                        (bb.Min.Z + bb.Max.Z) / 2.0);
                }

                // Fallback: element location
                Location loc = el.Location;
                if (loc is LocationPoint lp) return lp.Point;
                if (loc is LocationCurve lc)
                    return lc.Curve.Evaluate(0.5, true);

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Generate 8 candidate positions around a center point.
        /// Priority order: Above, Right, Below, Left, NE, SE, SW, NW.
        /// </summary>
        public static XYZ[] GetCandidatePositions(XYZ center, double offset)
        {
            return new XYZ[]
            {
                center + new XYZ(0, offset, 0),             // Above (P1)
                center + new XYZ(offset, 0, 0),              // Right (P2)
                center + new XYZ(0, -offset, 0),             // Below (P3)
                center + new XYZ(-offset, 0, 0),             // Left (P4)
                center + new XYZ(offset, offset, 0),         // NE (P5)
                center + new XYZ(offset, -offset, 0),        // SE (P6)
                center + new XYZ(-offset, -offset, 0),       // SW (P7)
                center + new XYZ(-offset, offset, 0),        // NW (P8)
            };
        }

        /// <summary>
        /// Score a candidate tag position. Higher is better.
        /// Score = ProximityBonus - OverlapPenalty + AlignmentBonus - LeaderPenalty
        /// </summary>
        public static double ScorePosition(XYZ candidate, double tagWidth, double tagHeight,
            XYZ elementCenter, List<BoundingBoxXYZ> occupied, double baseOffset)
        {
            double score = 100.0; // base score

            // Proximity bonus: closer to element is better
            double dist = candidate.DistanceTo(elementCenter);
            score += Math.Max(0, 50.0 - dist / baseOffset * 25.0);

            // Overlap penalty: check against all occupied regions
            double halfW = tagWidth / 2.0;
            double halfH = tagHeight / 2.0;

            double candMinX = candidate.X - halfW;
            double candMaxX = candidate.X + halfW;
            double candMinY = candidate.Y - halfH;
            double candMaxY = candidate.Y + halfH;

            foreach (BoundingBoxXYZ box in occupied)
            {
                if (candMinX < box.Max.X && candMaxX > box.Min.X &&
                    candMinY < box.Max.Y && candMaxY > box.Min.Y)
                {
                    // Overlap detected — heavy penalty
                    score -= 1000.0;
                }
            }

            // Preferred side bonus: Above is preferred (P1)
            if (candidate.Y > elementCenter.Y && Math.Abs(candidate.X - elementCenter.X) < baseOffset * 0.1)
                score += 30.0;

            // Leader penalty: penalize positions far from element
            if (dist > baseOffset * 1.5)
                score -= 20.0;

            return score;
        }

        /// <summary>Scale-aware offset for tag placement.</summary>
        public static double GetScaleAwareOffset(View view)
        {
            int scale = 100;
            try { scale = view.Scale; } catch { }

            // ~3mm on paper, scaled to model units
            double baseOffset = 0.01; // feet (~3mm)
            return baseOffset * scale;
        }

        /// <summary>AABB overlap test (2D plan view).</summary>
        public static bool Overlaps(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            return a.Min.X < b.Max.X && a.Max.X > b.Min.X
                && a.Min.Y < b.Max.Y && a.Max.Y > b.Min.Y;
        }
    }
}
