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
    /// <summary>
    /// Smart Tag Placement engine — priority-based annotation placement with collision avoidance.
    /// Inspired by BIMLOGiQ Smart Annotation and Naviate Tag from Template.
    /// Creates IndependentTag annotations in views displaying parameter values.
    /// </summary>
    internal static class TagPlacementEngine
    {
        // ── Candidate position generation ──────────────────────────────

        /// <summary>Candidate offsets: N, NE, E, SE, S, SW, W, NW (priority order).</summary>
        public static XYZ[] GetCandidateOffsets(double offset)
        {
            return new[]
            {
                new XYZ(0, offset, 0),             // Above (P1 — preferred)
                new XYZ(offset, 0, 0),              // Right (P2)
                new XYZ(0, -offset, 0),             // Below (P3)
                new XYZ(-offset, 0, 0),             // Left (P4)
                new XYZ(offset, offset, 0),         // NE (P5)
                new XYZ(offset, -offset, 0),        // SE (P6)
                new XYZ(-offset, -offset, 0),       // SW (P7)
                new XYZ(-offset, offset, 0),        // NW (P8)
            };
        }

        /// <summary>Scale-aware offset: baseOffset × viewScale gives consistent paper-space distance.</summary>
        public static double GetModelOffset(View view, double baseOffset = 0.01)
        {
            int viewScale = view.Scale > 0 ? view.Scale : 100;
            return baseOffset * viewScale;
        }

        /// <summary>Get element center point in view coordinates.</summary>
        public static XYZ GetElementCenter(Element elem, View view)
        {
            BoundingBoxXYZ bb = elem.get_BoundingBox(view);
            if (bb != null)
                return (bb.Min + bb.Max) / 2.0;

            // Fallback: location point
            if (elem.Location is LocationPoint lp)
                return lp.Point;
            if (elem.Location is LocationCurve lc)
                return lc.Curve.Evaluate(0.5, true);

            return XYZ.Zero;
        }

        // ── Collision detection (AABB 2D) ──────────────────────────────

        /// <summary>2D bounding box for collision detection in plan view.</summary>
        public struct Box2D
        {
            public double MinX, MinY, MaxX, MaxY;

            public Box2D(double minX, double minY, double maxX, double maxY)
            {
                MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
            }

            public bool Overlaps(Box2D other)
            {
                return MinX < other.MaxX && MaxX > other.MinX
                    && MinY < other.MaxY && MaxY > other.MinY;
            }

            public static Box2D FromBoundingBox(BoundingBoxXYZ bb)
            {
                if (bb == null) return new Box2D(0, 0, 0, 0);
                return new Box2D(bb.Min.X, bb.Min.Y, bb.Max.X, bb.Max.Y);
            }

            /// <summary>Create an estimated box for a tag at a given position.</summary>
            public static Box2D EstimateTag(XYZ position, double width, double height)
            {
                double hw = width / 2.0;
                double hh = height / 2.0;
                return new Box2D(
                    position.X - hw, position.Y - hh,
                    position.X + hw, position.Y + hh);
            }
        }

        /// <summary>Check if a candidate position overlaps with any existing boxes.</summary>
        public static bool HasOverlap(Box2D candidate, List<Box2D> occupied)
        {
            foreach (var box in occupied)
            {
                if (candidate.Overlaps(box))
                    return true;
            }
            return false;
        }

        // ── Scoring function ───────────────────────────────────────────

        /// <summary>Score a candidate position (higher = better).</summary>
        public static double ScoreCandidate(XYZ candidate, XYZ elementCenter,
            Box2D candidateBox, List<Box2D> occupied, int preferredSide)
        {
            double score = 100.0;

            // Proximity bonus: closer to element is better
            double dist = candidate.DistanceTo(elementCenter);
            score -= dist * 10.0;

            // Overlap penalty
            foreach (var box in occupied)
            {
                if (candidateBox.Overlaps(box))
                    score -= 1000.0;
            }

            // Preferred side bonus (0=above, 1=right, 2=below, 3=left)
            XYZ diff = candidate - elementCenter;
            if (preferredSide == 0 && diff.Y > 0) score += 30;
            else if (preferredSide == 1 && diff.X > 0) score += 30;
            else if (preferredSide == 2 && diff.Y < 0) score += 30;
            else if (preferredSide == 3 && diff.X < 0) score += 30;

            return score;
        }

        /// <summary>Get preferred placement side for a category (0=above, 1=right, 2=below, 3=left).</summary>
        public static int GetPreferredSide(string categoryName)
        {
            if (categoryName == null) return 0;
            string upper = categoryName.ToUpperInvariant();
            if (upper.Contains("DUCT")) return 0;   // above
            if (upper.Contains("PIPE")) return 3;    // left
            if (upper.Contains("EQUIPMENT")) return 1; // right
            if (upper.Contains("FIXTURE")) return 0; // above
            if (upper.Contains("TERMINAL")) return 0; // above
            return 0; // default: above
        }

        // ── Tag type finder ────────────────────────────────────────────

        /// <summary>Find a tag family type for the given element category.</summary>
        public static FamilySymbol FindTagType(Document doc, Category elementCategory)
        {
            if (elementCategory == null) return null;

            // Look for loaded tag types that can tag this category
            var tagTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Category != null &&
                    fs.Category.CategoryType == CategoryType.Annotation)
                .ToList();

            // Try to find a tag that matches the element's category
            foreach (var tagType in tagTypes)
            {
                try
                {
                    // Check if the tag family is designed for this category
                    Family fam = tagType.Family;
                    if (fam == null) continue;

                    // Generic tags work for most categories
                    string famName = fam.Name.ToUpperInvariant();
                    if (famName.Contains("MULTI") || famName.Contains("GENERIC") ||
                        famName.Contains("TAG"))
                    {
                        return tagType;
                    }
                }
                catch { /* skip invalid tag types */ }
            }

            return null;
        }

        // ── Batch placement ────────────────────────────────────────────

        /// <summary>
        /// Place tags for all untagged elements in a view with collision avoidance.
        /// Returns (placed, skipped, collisions).
        /// </summary>
        public static (int placed, int skipped, int collisions) PlaceTagsInView(
            Document doc, View view, bool addLeaders, bool tagOnlyUntagged)
        {
            int placed = 0;
            int skipped = 0;
            int collisions = 0;

            // Get existing tags to identify already-tagged elements
            var existingTags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            var taggedIds = new HashSet<ElementId>();
            foreach (var tag in existingTags)
            {
                try
                {
                    // Use GetTaggedLocalElements for Revit 2022+
                    var refs = tag.GetTaggedLocalElements();
                    foreach (var elem in refs)
                        taggedIds.Add(elem.Id);
                }
                catch
                {
                    // Fallback for older API
#pragma warning disable CS0618
                    var id = tag.TaggedLocalElementId;
#pragma warning restore CS0618
                    if (id != ElementId.InvalidElementId)
                        taggedIds.Add(id);
                }
            }

            // Collect taggable elements
            var elements = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null &&
                    e.Category.HasMaterialQuantities &&
                    e.Category.AllowsBoundParameters)
                .ToList();

            if (tagOnlyUntagged)
                elements = elements.Where(e => !taggedIds.Contains(e.Id)).ToList();

            if (elements.Count == 0) return (0, 0, 0);

            // Build occupied boxes from existing tags
            var occupied = new List<Box2D>();
            foreach (var tag in existingTags)
            {
                BoundingBoxXYZ bb = tag.get_BoundingBox(view);
                if (bb != null)
                    occupied.Add(Box2D.FromBoundingBox(bb));
            }

            double offset = GetModelOffset(view);
            // Estimated tag dimensions (in model units)
            double tagWidth = offset * 3.0;
            double tagHeight = offset * 1.0;

            foreach (Element elem in elements)
            {
                XYZ center = GetElementCenter(elem, view);
                if (center.IsAlmostEqualTo(XYZ.Zero))
                {
                    skipped++;
                    continue;
                }

                string catName = elem.Category?.Name ?? "";
                int preferred = GetPreferredSide(catName);
                var offsets = GetCandidateOffsets(offset);

                // Score all candidates and pick the best
                XYZ bestPos = null;
                double bestScore = double.MinValue;
                bool needsLeader = false;

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    double scale = 1.0 + attempt * 0.5; // Expand search radius
                    foreach (XYZ off in offsets)
                    {
                        XYZ candidate = center + off * scale;
                        var candBox = Box2D.EstimateTag(candidate, tagWidth, tagHeight);
                        double score = ScoreCandidate(
                            candidate, center, candBox, occupied, preferred);

                        if (attempt > 0) score -= 20; // Penalty for being farther
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestPos = candidate;
                            needsLeader = attempt > 0;
                        }
                    }

                    // If best score is above threshold, use it
                    if (bestScore > 0) break;
                }

                if (bestPos == null)
                {
                    bestPos = center + new XYZ(0, offset, 0); // Fallback: above
                    needsLeader = true;
                }

                // Check for overlap at final position
                var finalBox = Box2D.EstimateTag(bestPos, tagWidth, tagHeight);
                if (HasOverlap(finalBox, occupied))
                    collisions++;

                try
                {
                    var reference = new Reference(elem);
                    bool useLeader = addLeaders || needsLeader;

                    IndependentTag tag = IndependentTag.Create(
                        doc, view.Id, reference, useLeader,
                        TagMode.TM_ADDBY_CATEGORY,
                        TagOrientation.Horizontal,
                        bestPos);

                    if (tag != null)
                    {
                        // Register the tag box for future collision checks
                        BoundingBoxXYZ tagBB = tag.get_BoundingBox(view);
                        if (tagBB != null)
                            occupied.Add(Box2D.FromBoundingBox(tagBB));
                        else
                            occupied.Add(finalBox);
                        placed++;
                    }
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    // No tag loaded for this category
                    skipped++;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Tag placement failed for {elem.Id}: {ex.Message}");
                    skipped++;
                }
            }

            return (placed, skipped, collisions);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Smart Place Tags — single view
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Place IndependentTag annotations in active view with smart collision avoidance.
    /// Uses 8-position scoring with proximity, overlap penalty, and category-specific preferences.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SmartPlaceTagsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            if (view is ViewSheet)
            {
                TaskDialog.Show("Smart Place Tags", "Cannot tag on a sheet view.\nOpen a floor plan or section.");
                return Result.Succeeded;
            }

            // Options dialog
            TaskDialog optDlg = new TaskDialog("Smart Place Tags");
            optDlg.MainInstruction = "Place annotation tags in active view";
            optDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Tag untagged elements only",
                "Skip elements that already have an annotation tag in this view");
            optDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Tag ALL elements",
                "Place tags on every taggable element (may create duplicates)");
            optDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Tag selected elements only",
                $"Tag {uidoc.Selection.GetElementIds().Count} selected elements");
            optDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            bool tagUntaggedOnly;
            bool selectedOnly = false;
            switch (optDlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    tagUntaggedOnly = true; break;
                case TaskDialogResult.CommandLink2:
                    tagUntaggedOnly = false; break;
                case TaskDialogResult.CommandLink3:
                    tagUntaggedOnly = false; selectedOnly = true; break;
                default:
                    return Result.Cancelled;
            }

            // Leader option
            TaskDialog leaderDlg = new TaskDialog("Smart Place Tags — Leaders");
            leaderDlg.MainInstruction = "Leader line mode";
            leaderDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Auto (recommended)",
                "Add leaders only when tag must be placed far from element");
            leaderDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Always show leaders",
                "Add leader lines to all placed tags");
            leaderDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "No leaders",
                "Never add leader lines");
            leaderDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            bool addLeaders;
            switch (leaderDlg.Show())
            {
                case TaskDialogResult.CommandLink1: addLeaders = false; break; // auto in engine
                case TaskDialogResult.CommandLink2: addLeaders = true; break;
                case TaskDialogResult.CommandLink3: addLeaders = false; break;
                default: return Result.Cancelled;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();

            int placed, skipped, collisions;

            if (selectedOnly)
            {
                // Tag only selected elements
                var selectedIds = uidoc.Selection.GetElementIds();
                if (selectedIds.Count == 0)
                {
                    TaskDialog.Show("Smart Place Tags", "No elements selected.");
                    return Result.Succeeded;
                }

                placed = 0; skipped = 0; collisions = 0;
                double offset = TagPlacementEngine.GetModelOffset(view);

                var occupied = new List<TagPlacementEngine.Box2D>();
                // Collect existing tag boxes
                foreach (var tag in new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag))
                    .Cast<IndependentTag>())
                {
                    BoundingBoxXYZ bb = tag.get_BoundingBox(view);
                    if (bb != null)
                        occupied.Add(TagPlacementEngine.Box2D.FromBoundingBox(bb));
                }

                using (Transaction tx = new Transaction(doc, "STING Smart Place Tags (Selected)"))
                {
                    tx.Start();
                    foreach (ElementId id in selectedIds)
                    {
                        Element elem = doc.GetElement(id);
                        if (elem?.Category == null)
                        {
                            skipped++;
                            continue;
                        }

                        XYZ center = TagPlacementEngine.GetElementCenter(elem, view);
                        var offsets = TagPlacementEngine.GetCandidateOffsets(offset);
                        string catName = elem.Category?.Name ?? "";
                        int preferred = TagPlacementEngine.GetPreferredSide(catName);

                        XYZ bestPos = center + new XYZ(0, offset, 0);
                        double bestScore = double.MinValue;
                        double tagW = offset * 3.0, tagH = offset * 1.0;

                        foreach (XYZ off in offsets)
                        {
                            XYZ cand = center + off;
                            var cb = TagPlacementEngine.Box2D.EstimateTag(cand, tagW, tagH);
                            double sc = TagPlacementEngine.ScoreCandidate(
                                cand, center, cb, occupied, preferred);
                            if (sc > bestScore) { bestScore = sc; bestPos = cand; }
                        }

                        try
                        {
                            var tag = IndependentTag.Create(
                                doc, view.Id, new Reference(elem), addLeaders,
                                TagMode.TM_ADDBY_CATEGORY,
                                TagOrientation.Horizontal, bestPos);

                            if (tag != null)
                            {
                                BoundingBoxXYZ tagBB = tag.get_BoundingBox(view);
                                if (tagBB != null)
                                    occupied.Add(TagPlacementEngine.Box2D.FromBoundingBox(tagBB));
                                placed++;
                            }
                        }
                        catch { skipped++; }
                    }
                    tx.Commit();
                }
            }
            else
            {
                using (Transaction tx = new Transaction(doc, "STING Smart Place Tags"))
                {
                    tx.Start();
                    (placed, skipped, collisions) =
                        TagPlacementEngine.PlaceTagsInView(doc, view, addLeaders, tagUntaggedOnly);
                    tx.Commit();
                }
            }

            sw.Stop();
            var report = new StringBuilder();
            report.AppendLine($"Placed: {placed} annotation tags");
            if (skipped > 0) report.AppendLine($"Skipped: {skipped} (no tag family or invalid)");
            if (collisions > 0) report.AppendLine($"Overlaps: {collisions} (best effort placement)");
            report.AppendLine($"Time: {sw.Elapsed.TotalSeconds:F1}s");

            TaskDialog.Show("Smart Place Tags", report.ToString());
            StingLog.Info($"SmartPlaceTags: placed={placed}, skipped={skipped}, " +
                $"collisions={collisions}, time={sw.Elapsed.TotalSeconds:F1}s");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Arrange Tags — reposition existing tags to resolve overlaps
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reposition existing annotation tags in the active view to minimize overlaps.
    /// Uses the same scoring algorithm as SmartPlaceTags but on existing tags.
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

            var tags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            if (tags.Count == 0)
            {
                TaskDialog.Show("Arrange Tags", "No annotation tags in active view.");
                return Result.Succeeded;
            }

            // Detect overlapping tags
            var tagBoxes = new List<(IndependentTag tag, TagPlacementEngine.Box2D box)>();
            foreach (var tag in tags)
            {
                BoundingBoxXYZ bb = tag.get_BoundingBox(view);
                if (bb != null)
                    tagBoxes.Add((tag, TagPlacementEngine.Box2D.FromBoundingBox(bb)));
            }

            int overlapCount = 0;
            for (int i = 0; i < tagBoxes.Count; i++)
            {
                for (int j = i + 1; j < tagBoxes.Count; j++)
                {
                    if (tagBoxes[i].box.Overlaps(tagBoxes[j].box))
                        overlapCount++;
                }
            }

            TaskDialog confirm = new TaskDialog("Arrange Tags");
            confirm.MainInstruction = $"Arrange {tags.Count} tags in active view";
            confirm.MainContent = $"Found {overlapCount} overlapping tag pairs.\n\n" +
                "Tags will be repositioned to minimize overlaps while staying " +
                "close to their host elements.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            double offset = TagPlacementEngine.GetModelOffset(view);
            int moved = 0;
            int resolved = 0;

            using (Transaction tx = new Transaction(doc, "STING Arrange Tags"))
            {
                tx.Start();

                // Rebuild occupied list progressively
                var occupied = new List<TagPlacementEngine.Box2D>();

                foreach (var (tag, _) in tagBoxes)
                {
                    // Get host element center
                    XYZ hostCenter = null;
                    try
                    {
                        var hostElems = tag.GetTaggedLocalElements();
                        if (hostElems.Any())
                        {
                            Element host = hostElems.First();
                            hostCenter = TagPlacementEngine.GetElementCenter(host, view);
                        }
                    }
                    catch
                    {
#pragma warning disable CS0618
                        var hostId = tag.TaggedLocalElementId;
#pragma warning restore CS0618
                        if (hostId != ElementId.InvalidElementId)
                        {
                            Element host = doc.GetElement(hostId);
                            if (host != null)
                                hostCenter = TagPlacementEngine.GetElementCenter(host, view);
                        }
                    }

                    if (hostCenter == null || hostCenter.IsAlmostEqualTo(XYZ.Zero))
                    {
                        // Keep tag in place, register box
                        BoundingBoxXYZ bb = tag.get_BoundingBox(view);
                        if (bb != null)
                            occupied.Add(TagPlacementEngine.Box2D.FromBoundingBox(bb));
                        continue;
                    }

                    // Score candidate positions
                    var offsets = TagPlacementEngine.GetCandidateOffsets(offset);
                    double tagW = offset * 3.0, tagH = offset * 1.0;

                    XYZ bestPos = null;
                    double bestScore = double.MinValue;

                    foreach (XYZ off in offsets)
                    {
                        XYZ cand = hostCenter + off;
                        var cb = TagPlacementEngine.Box2D.EstimateTag(cand, tagW, tagH);
                        double sc = TagPlacementEngine.ScoreCandidate(
                            cand, hostCenter, cb, occupied, 0);
                        if (sc > bestScore)
                        {
                            bestScore = sc;
                            bestPos = cand;
                        }
                    }

                    if (bestPos != null)
                    {
                        XYZ oldPos = tag.TagHeadPosition;
                        bool wasBad = false;

                        // Check if old position overlapped
                        var oldBox = TagPlacementEngine.Box2D.EstimateTag(oldPos, tagW, tagH);
                        wasBad = TagPlacementEngine.HasOverlap(oldBox, occupied);

                        tag.TagHeadPosition = bestPos;
                        moved++;

                        if (wasBad)
                        {
                            var newBox = TagPlacementEngine.Box2D.EstimateTag(bestPos, tagW, tagH);
                            if (!TagPlacementEngine.HasOverlap(newBox, occupied))
                                resolved++;
                        }
                    }

                    // Register final position
                    BoundingBoxXYZ finalBB = tag.get_BoundingBox(view);
                    if (finalBB != null)
                        occupied.Add(TagPlacementEngine.Box2D.FromBoundingBox(finalBB));
                }

                tx.Commit();
            }

            TaskDialog.Show("Arrange Tags",
                $"Repositioned {moved} of {tags.Count} tags.\n" +
                $"Overlaps resolved: {resolved} of {overlapCount}.");
            StingLog.Info($"ArrangeTags: moved={moved}, resolved={resolved}/{overlapCount}");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Remove Annotation Tags
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Remove IndependentTag annotations from the active view.
    /// Does NOT affect data tags (parameter values) — only removes visual annotations.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RemoveAnnotationTagsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            // Check selection first
            var selectedIds = uidoc.Selection.GetElementIds();
            var selectedTags = selectedIds
                .Select(id => doc.GetElement(id) as IndependentTag)
                .Where(t => t != null)
                .ToList();

            List<IndependentTag> tagsToRemove;
            string scope;

            if (selectedTags.Count > 0)
            {
                tagsToRemove = selectedTags;
                scope = $"{selectedTags.Count} selected tags";
            }
            else
            {
                tagsToRemove = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag))
                    .Cast<IndependentTag>()
                    .ToList();
                scope = $"all {tagsToRemove.Count} tags in active view";
            }

            if (tagsToRemove.Count == 0)
            {
                TaskDialog.Show("Remove Annotation Tags", "No annotation tags found.");
                return Result.Succeeded;
            }

            TaskDialog confirm = new TaskDialog("Remove Annotation Tags");
            confirm.MainInstruction = $"Remove {tagsToRemove.Count} annotation tags?";
            confirm.MainContent = $"Scope: {scope}\n\n" +
                "This removes VISUAL tag annotations only.\n" +
                "Data tags (parameter values like ASS_TAG_1) are NOT affected.\n" +
                "Undo with Ctrl+Z.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            int removed = 0;
            using (Transaction tx = new Transaction(doc, "STING Remove Annotation Tags"))
            {
                tx.Start();
                foreach (var tag in tagsToRemove)
                {
                    try
                    {
                        doc.Delete(tag.Id);
                        removed++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Could not delete tag {tag.Id}: {ex.Message}");
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Remove Annotation Tags",
                $"Removed {removed} of {tagsToRemove.Count} annotation tags.");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Batch Place Tags — multiple views
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Place annotation tags across multiple views (floor plans + RCPs).
    /// Reports per-view results with progress.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchPlaceTagsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            // Collect floor plan and RCP views
            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.CanBePrinted &&
                    (v.ViewType == ViewType.FloorPlan ||
                     v.ViewType == ViewType.CeilingPlan ||
                     v.ViewType == ViewType.Section ||
                     v.ViewType == ViewType.Elevation))
                .OrderBy(v => v.ViewType.ToString())
                .ThenBy(v => v.Name)
                .ToList();

            if (views.Count == 0)
            {
                TaskDialog.Show("Batch Place Tags", "No suitable views found.");
                return Result.Succeeded;
            }

            // Scope selection
            TaskDialog scopeDlg = new TaskDialog("Batch Place Tags");
            scopeDlg.MainInstruction = $"Place tags across {views.Count} views?";

            int floorPlans = views.Count(v => v.ViewType == ViewType.FloorPlan);
            int ceilings = views.Count(v => v.ViewType == ViewType.CeilingPlan);
            int sections = views.Count(v => v.ViewType == ViewType.Section);
            int elevations = views.Count(v => v.ViewType == ViewType.Elevation);

            scopeDlg.MainContent =
                $"Floor Plans: {floorPlans}\nCeiling Plans: {ceilings}\n" +
                $"Sections: {sections}\nElevations: {elevations}\n\n" +
                "Only untagged elements will receive tags.\n" +
                "Tags are placed per-view with collision avoidance.";
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Floor Plans only", $"Tag {floorPlans} floor plan views");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "All views", $"Tag all {views.Count} views");
            scopeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            List<View> targetViews;
            switch (scopeDlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    targetViews = views.Where(v => v.ViewType == ViewType.FloorPlan).ToList();
                    break;
                case TaskDialogResult.CommandLink2:
                    targetViews = views;
                    break;
                default:
                    return Result.Cancelled;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int totalPlaced = 0, totalSkipped = 0, totalCollisions = 0;
            int viewsProcessed = 0;
            var perView = new List<(string name, int placed, int skipped)>();

            using (TransactionGroup tg = new TransactionGroup(doc, "STING Batch Place Tags"))
            {
                tg.Start();

                foreach (View v in targetViews)
                {
                    using (Transaction tx = new Transaction(doc,
                        $"STING Tag {v.Name}"))
                    {
                        tx.Start();
                        var (p, s, c) = TagPlacementEngine.PlaceTagsInView(
                            doc, v, addLeaders: false, tagOnlyUntagged: true);
                        tx.Commit();

                        totalPlaced += p;
                        totalSkipped += s;
                        totalCollisions += c;
                        perView.Add((v.Name, p, s));
                    }

                    viewsProcessed++;
                    if (viewsProcessed % 10 == 0)
                        StingLog.Info($"BatchPlaceTags: {viewsProcessed}/{targetViews.Count} views done");
                }

                tg.Assimilate();
            }

            sw.Stop();

            var report = new StringBuilder();
            report.AppendLine($"Batch Tag Placement Complete");
            report.AppendLine($"Views processed: {viewsProcessed}");
            report.AppendLine($"Tags placed: {totalPlaced}");
            report.AppendLine($"Skipped: {totalSkipped}");
            report.AppendLine($"Overlaps: {totalCollisions}");
            report.AppendLine($"Time: {sw.Elapsed.TotalSeconds:F1}s");
            report.AppendLine();
            report.AppendLine("── Per-View Summary ──");
            foreach (var (name, p, s) in perView.Where(x => x.placed > 0).Take(20))
                report.AppendLine($"  {name,-35} +{p} tags ({s} skipped)");
            if (perView.Count(x => x.placed > 0) > 20)
                report.AppendLine($"  ... and {perView.Count(x => x.placed > 0) - 20} more");

            TaskDialog.Show("Batch Place Tags", report.ToString());
            StingLog.Info($"BatchPlaceTags: views={viewsProcessed}, placed={totalPlaced}, " +
                $"time={sw.Elapsed.TotalSeconds:F1}s");
            return Result.Succeeded;
        }
    }
}
