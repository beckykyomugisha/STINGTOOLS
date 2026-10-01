// StingTools — Drawing Template Manager · spatial tags (DTW-83)
//
// Rooms, MEP spaces and areas are tagged with a SpatialElementTag
// (doc.Create.NewRoomTag / NewSpaceTag / NewAreaTag), not an IndependentTag.
// TagByRules routes the three categories here; everything else still goes
// through TagCategory. See SpatialTagRouting for the Revit-free half.
//
// Idempotency: the shared "already tagged" index (BuildTaggedElementIndex)
// now reads room / space / area tags as well as IndependentTags, so a room
// that already carries a room tag in this view is skipped — the same rule
// Rooms_PlaceTags (RoomTagPlacer) follows, and it places room tags through
// RoomTagPlacer.PlaceOne. NOT VERIFIED IN REVIT.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using StingTools.Commands.Rooms;

namespace StingTools.Core.Drawing
{
    public static partial class AnnotationRunner
    {
        /// <summary>Host categories whose tags are SpatialElementTags, with their tag categories.</summary>
        private static readonly BuiltInCategory[] SpatialTagCategories =
        {
            BuiltInCategory.OST_RoomTags, BuiltInCategory.OST_MEPSpaceTags, BuiltInCategory.OST_AreaTags,
        };

        /// <summary>
        /// Add every room / space / area tag in the view to the tagged-host index,
        /// keyed by the room / space / area it tags (a linked room by link instance
        /// and linked id). IndependentTag.GetTaggedLocalElementIds never sees these,
        /// so before DTW-83 an existing room tag was invisible to the runner.
        /// </summary>
        private static void AddSpatialTagsToIndex(Document doc, View view, Dictionary<string, List<string>> index)
        {
            foreach (var tagCat in SpatialTagCategories)
            {
                foreach (var el in new FilteredElementCollector(doc, view.Id)
                    .OfCategory(tagCat)
                    .WhereElementIsNotElementType())
                {
                    if (!(el is SpatialElementTag tag)) continue;
                    try
                    {
                        string key = null;
                        if (tag is RoomTag rt)
                        {
                            var id = rt.TaggedRoomId;
                            if (id != null)
                                key = TaggedHostKey.From(id.HostElementId.Value, id.LinkInstanceId.Value, id.LinkedElementId.Value);
                        }
                        else if (tag is SpaceTag st && st.Space != null) key = TaggedHostKey.Local(st.Space.Id.Value);
                        else if (tag is AreaTag at && at.Area != null) key = TaggedHostKey.Local(at.Area.Id.Value);
                        // A tag whose room was deleted tags nothing and must not
                        // suppress a legitimate placement (RoomTagPlacer's rule).
                        if (key == null || key == TaggedHostKey.Local(-1)) continue;

                        string fam = (doc.GetElement(tag.GetTypeId()) as FamilySymbol)?.FamilyName ?? "";
                        if (!index.TryGetValue(key, out var fams)) index[key] = fams = new List<string>();
                        fams.Add(fam);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"AddSpatialTagsToIndex: tag {tag.Id} — {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Tag every visible, placed room / space / area in the view that the rule
        /// covers and that is not already tagged. Placement at the element's
        /// location point, which Revit keeps inside the room.
        /// </summary>
        private static void TagSpatialCategory(Document doc, View view, AnnotationRulePack pack,
            BuiltInCategory bic, SpatialTagKind kind, string catKey, AnnotationRunStats stats,
            AutoAnnotationRule rule, Dictionary<string, List<string>> alreadyTagged,
            DrawingType drawingType, ISet<string> specialistFamilies)
        {
            string refusal = SpatialTagRouting.ViewRefusal(kind, view.ViewType.ToString());
            if (refusal != null)
            {
                stats.Warnings.Add($"Tag rule '{rule?.RuleType}' ({catKey}) on '{view.Name}': {refusal} — nothing placed.");
                return;
            }

            var elements = new FilteredElementCollector(doc, view.Id)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .OfType<SpatialElement>()
                .ToList();

            ElementId tagTypeId = ResolveSpatialTagTypeId(doc, view, pack, bic, catKey, rule, stats);
            if (drawingType != null && tagTypeId != ElementId.InvalidElementId)
                tagTypeId = ApplyTagSizeVariant(doc, tagTypeId, drawingType, catKey, stats);
            string placedFamily = (doc.GetElement(tagTypeId) as FamilySymbol)?.FamilyName ?? "";
            bool isSpecialistRule = !string.IsNullOrWhiteSpace(rule?.TagFamily);
            bool skipIfTagged = rule?.SkipIfTagged ?? true;

            var leader = TagLeader.Parse(rule?.LeaderStyle);
            if (leader == TagLeaderMode.Unrecognised)
            {
                stats.Warnings.Add($"Rule leaderStyle '{rule.LeaderStyle}' for {catKey} is not one of " +
                                   "NoLeader / Attached / Free — tagging without a leader.");
                leader = TagLeaderMode.None;
            }
            bool withLeader = leader == TagLeaderMode.Attached || leader == TagLeaderMode.Free;
            var orientation = ResolveSpatialOrientation(rule, catKey, stats);
            if (!string.IsNullOrWhiteSpace(rule?.FamilyMatch))
                stats.Warnings.Add($"{catKey}: familyMatch '{rule.FamilyMatch}' ignored — rooms, spaces and areas have no family.");

            int unplaced = 0, failed = 0, placed = 0, defaultType = 0;
            string firstFailure = null, firstTypeProblem = null;
            foreach (var se in elements)
            {
                try
                {
                    if (skipIfTagged && alreadyTagged != null
                        && alreadyTagged.TryGetValue(TaggedHostKey.Local(se.Id.Value), out var onElement)
                        && TagRuleIdentity.ShouldSkip(onElement, isSpecialistRule, placedFamily, specialistFamilies))
                    {
                        stats.Skipped++;
                        continue;
                    }

                    var pt = SpatialTagPoint(se);
                    if (pt == null) { unplaced++; stats.Skipped++; continue; }

                    var tag = PlaceSpatialTag(doc, view, se, kind, new LinkElementId(se.Id), pt, tagTypeId,
                        withLeader, orientation, out string problem);
                    if (tag != null)
                    {
                        placed++;
                        stats.TagsPlaced++;
                        if (alreadyTagged != null)
                        {
                            var key = TaggedHostKey.Local(se.Id.Value);
                            if (!alreadyTagged.TryGetValue(key, out var fams)) alreadyTagged[key] = fams = new List<string>();
                            fams.Add(placedFamily);
                        }
                    }
                    if (tag == null)
                    {
                        failed++;
                        firstFailure = firstFailure ?? $"{se.Id}: {problem}";
                    }
                    else if (problem != null)
                    {
                        defaultType++;
                        firstTypeProblem = firstTypeProblem ?? $"{se.Id}: {problem}";
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    firstFailure = firstFailure ?? $"{se.Id}: {ex.Message}";
                }
            }

            // DTW-85: rooms in loaded links; spaces / areas in links are reported.
            TagLinkedSpatial(doc, view, bic, kind, catKey, stats, alreadyTagged, skipIfTagged, isSpecialistRule,
                placedFamily, specialistFamilies, tagTypeId, withLeader, orientation);

            if (unplaced > 0)
                stats.Warnings.Add($"{catKey}: {unplaced} unplaced or unbounded element(s) have no position to tag — run Room Audit.");
            if (failed > 0)
                stats.Warnings.Add($"{catKey}: {failed} tag(s) could not be placed — first: {firstFailure}");
            if (defaultType > 0)
                stats.Warnings.Add($"{catKey}: {defaultType} tag(s) placed but not with the chosen type — first: {firstTypeProblem}");
            StingLog.Info($"AnnotationRunner spatial {catKey} in '{view.Name}': placed {placed}, failed {failed}, unplaced {unplaced}.");
        }

        /// <summary>
        /// Create one room / space / area tag. <paramref name="id"/> is the element as
        /// the view sees it (a LinkElementId for a linked room); <paramref name="point"/>
        /// is in host coordinates. Returns null with a reason on failure; a non-null
        /// tag with a reason means it was placed with the default type.
        /// </summary>
        private static SpatialElementTag PlaceSpatialTag(Document doc, View view, SpatialElement host,
            SpatialTagKind kind, LinkElementId id, XYZ point, ElementId tagTypeId, bool withLeader,
            SpatialElementTagOrientation? orientation, out string problem)
        {
            problem = null;
            SpatialElementTag tag;
            if (kind == SpatialTagKind.Room)
            {
                tag = RoomTagPlacer.PlaceOne(doc, view, id, point, tagTypeId, withLeader, out problem);
            }
            else
            {
                var uv = new UV(point.X, point.Y);
                if (kind == SpatialTagKind.Space && host is Space space)
                    tag = doc.Create.NewSpaceTag(space, uv, view);
                else if (kind == SpatialTagKind.Area && host is Area area && view is ViewPlan areaPlan)
                    tag = doc.Create.NewAreaTag(areaPlan, area, uv);
                else { problem = $"no {kind} tag path for element {host?.Id}"; return null; }
                if (tag == null) { problem = "Revit returned no tag."; return null; }

                if (tagTypeId != ElementId.InvalidElementId && tag.GetTypeId() != tagTypeId)
                {
                    try { tag.ChangeTypeId(tagTypeId); }
                    catch (Exception ex) { problem = "placed with the default tag type — " + ex.Message; }
                }
                try { tag.HasLeader = withLeader; }
                catch (Exception ex) { StingLog.Warn($"Spatial tag leader {tag.Id}: {ex.Message}"); }
            }
            if (tag != null && orientation.HasValue)
            {
                try { tag.TagOrientation = orientation.Value; }
                catch (Exception ex) { StingLog.Warn($"Spatial tag orientation {tag.Id}: {ex.Message}"); }
            }
            return tag;
        }

        /// <summary>Where a spatial element's tag goes: its location point (Revit keeps
        /// it inside the room), else the centre of its bounding box. Null for an
        /// unplaced or unbounded element.</summary>
        private static XYZ SpatialTagPoint(SpatialElement se)
        {
            try
            {
                if (se == null || se.Location == null || se.Area <= 0) return null;
                if (se.Location is LocationPoint lp && lp.Point != null) return lp.Point;
                var bb = se.get_BoundingBox(null);
                if (bb?.Min != null && bb.Max != null) return (bb.Min + bb.Max) * 0.5;
            }
            catch (Exception ex) { StingLog.Warn($"SpatialTagPoint({se?.Id}): {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// The tag type for a spatial rule: the rule's family / type if loaded in the
        /// right tag category, else the pack's resolution when it lands in that
        /// category, else the project's default type for the tag category.
        /// InvalidElementId = let Revit use its own default.
        /// </summary>
        private static ElementId ResolveSpatialTagTypeId(Document doc, View view, AnnotationRulePack pack,
            BuiltInCategory hostBic, string catKey, AutoAnnotationRule rule, AnnotationRunStats stats)
        {
            long tagCat = (long)TagCategoryFor(hostBic);
            var index = SymbolIndexFor(doc);
            if (!string.IsNullOrWhiteSpace(rule?.TagFamily))
            {
                var wanted = rule.TagFamily.Trim();
                var m = index.InCategory(tagCat).FirstOrDefault(x =>
                            string.Equals(x.FamilyName, wanted, StringComparison.OrdinalIgnoreCase))
                        ?? index.InCategory(tagCat).FirstOrDefault(x =>
                            string.Equals(x.Name, wanted, StringComparison.OrdinalIgnoreCase));
                if (m != null) return m.Id;
                stats.Warnings.Add($"Rule tag family '{wanted}' for {catKey} is not loaded as a " +
                                   $"{TagCategoryFor(hostBic)} family; using the pack / project default.");
            }

            var resolved = ResolveTagTypeId(doc, view, pack, catKey, hostBic, stats);
            if (resolved != ElementId.InvalidElementId)
            {
                var sym = doc.GetElement(resolved) as FamilySymbol;
                if (sym?.Category?.Id.Value == tagCat) return resolved;
                stats.Warnings.Add($"{catKey}: tag '{sym?.FamilyName}' is not a {TagCategoryFor(hostBic)} family — " +
                                   "using the project default instead.");
            }

            try
            {
                var def = doc.GetDefaultFamilyTypeId(new ElementId(tagCat));
                if (def != null && def != ElementId.InvalidElementId) return def;
            }
            catch (Exception ex) { StingLog.Warn($"Default tag type for {catKey}: {ex.Message}"); }
            return ElementId.InvalidElementId;
        }

        /// <summary>The rule's orientation as a spatial-tag orientation; null = leave
        /// Revit's default (horizontal).</summary>
        private static SpatialElementTagOrientation? ResolveSpatialOrientation(
            AutoAnnotationRule rule, string catKey, AnnotationRunStats stats)
        {
            var declared = rule?.Orientation;
            if (string.IsNullOrWhiteSpace(declared)) return null;
            switch (declared.Trim().ToLowerInvariant())
            {
                case "horizontal": return SpatialElementTagOrientation.Horizontal;
                case "vertical":   return SpatialElementTagOrientation.Vertical;
                case "model":
                case "anymodeldirection": return SpatialElementTagOrientation.Model;
                default:
                    stats?.Warnings.Add($"Rule orientation '{declared}' for {catKey} is not one of " +
                                        "Horizontal / Vertical / Model — tagging horizontally.");
                    return null;
            }
        }
    }
}
