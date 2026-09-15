using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Select;   // StingListPicker lives in the Select namespace, not UI
using StingTools.UI;

namespace StingTools.Commands.Rooms
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Place room tags.
    //
    //  STING has been able to CREATE room tag families since TagFamilyCreator
    //  (including the four HBN / HTM clinical ones) and to REPOSITION tags that
    //  already exist (Tagging_RoomTagApply → MoveRoomTags). It could not place
    //  one: NewRoomTag appeared nowhere in the codebase, so the middle step of
    //  the workflow was done by hand in Revit.
    //
    //  Idempotent by construction: a room that already carries a tag IN THAT
    //  VIEW is skipped. Running this twice must not double-tag, because a
    //  stacked duplicate tag is invisible on screen and only shows up when
    //  someone drags the top one months later.
    // ─────────────────────────────────────────────────────────────────────────

    internal static class RoomTagPlacer
    {
        /// <summary>Rooms already tagged in this view, by room id.</summary>
        internal static HashSet<ElementId> AlreadyTagged(Document doc, View view)
        {
            var tagged = new HashSet<ElementId>();
            try
            {
                foreach (var el in new FilteredElementCollector(doc, view.Id)
                             .OfCategory(BuiltInCategory.OST_RoomTags)
                             .WhereElementIsNotElementType())
                {
                    var tag = el as RoomTag;
                    if (tag == null) continue;
                    try
                    {
                        // Room is null for a tag whose room was deleted; such a tag occupies
                        // no room, so it must not suppress a legitimate placement.
                        if (tag.Room != null) tagged.Add(tag.Room.Id);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn("RoomTagPlacer: tag " + tag.Id + " room lookup: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                // Returning an empty set here would look like "nothing is tagged" and
                // double-tag the whole view, so fail loudly instead.
                StingLog.Error("RoomTagPlacer.AlreadyTagged", ex);
                throw;
            }
            return tagged;
        }

        /// <summary>The plan views a room tag can legitimately live in.</summary>
        internal static List<ViewPlan> TaggablePlanViews(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => !v.IsTemplate
                            && (v.ViewType == ViewType.FloorPlan || v.ViewType == ViewType.CeilingPlan
                                || v.ViewType == ViewType.AreaPlan || v.ViewType == ViewType.EngineeringPlan))
                .OrderBy(v => v.Name, StringComparer.Ordinal)
                .ToList();
        }

        internal class PlacementTally
        {
            public int Placed;
            public int AlreadyHadTag;
            public int SkippedUnplaced;
            public List<string> Failures = new List<string>();
        }

        /// <summary>
        /// Place a tag on every visible, placed, enclosed, untagged room in one view.
        /// Caller owns the transaction.
        /// </summary>
        internal static PlacementTally PlaceInView(
            Document doc, View view, RoomTagType tagType, bool withLeader)
        {
            var tally = new PlacementTally();
            var tagged = AlreadyTagged(doc, view);

            // View-scoped: a room cut out by the crop or hidden by a filter is not in this
            // collector, and tagging it would put a tag where nothing is drawn.
            var rooms = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .ToList();

            foreach (var room in rooms)
            {
                if (room.Location == null || room.Area <= 0) { tally.SkippedUnplaced++; continue; }
                if (tagged.Contains(room.Id)) { tally.AlreadyHadTag++; continue; }

                try
                {
                    var lp = room.Location as LocationPoint;
                    if (lp == null) { tally.SkippedUnplaced++; continue; }

                    var uv = new UV(lp.Point.X, lp.Point.Y);
                    RoomTag tag = doc.Create.NewRoomTag(new LinkElementId(room.Id), uv, view.Id);
                    if (tag == null)
                    {
                        tally.Failures.Add(Describe(room) + ": Revit returned no tag.");
                        continue;
                    }

                    if (tagType != null)
                    {
                        try { tag.ChangeTypeId(tagType.Id); }
                        catch (Exception ex)
                        {
                            // The tag exists and is usable; only its type is not what was asked.
                            tally.Failures.Add(Describe(room) +
                                ": tag placed but its type could not be set — " + ex.Message);
                            StingLog.Warn("RoomTagPlacer type " + room.Id + ": " + ex.Message);
                        }
                    }

                    try { tag.HasLeader = withLeader; }
                    catch (Exception ex) { StingLog.Warn("RoomTagPlacer leader " + room.Id + ": " + ex.Message); }

                    tally.Placed++;
                }
                catch (Exception ex)
                {
                    tally.Failures.Add(Describe(room) + ": " + ex.Message);
                    StingLog.Warn("RoomTagPlacer place " + room.Id + ": " + ex.Message);
                }
            }
            return tally;
        }

        private static string Describe(Room room)
        {
            try
            {
                string name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                string num = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
                return (string.IsNullOrWhiteSpace(name) ? "Room" : name) +
                       (string.IsNullOrWhiteSpace(num) ? "" : " (" + num + ")");
            }
            catch (Exception) { return "Room " + room.Id; }
        }
    }

    /// <summary>
    /// Place room tags on untagged rooms. Command tag: <c>Rooms_PlaceTags</c>.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceRoomTagsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var tagTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_RoomTags)
                .Cast<RoomTagType>()
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .ToList();

            if (tagTypes.Count == 0)
            {
                TaskDialog.Show("STING — Place Room Tags",
                    "This project has no room tag family loaded.\n\n" +
                    "Load one, or build the STING room tag families first " +
                    "(CREATE TAGS → Tag Family Creator).");
                return Result.Cancelled;
            }

            // ── Scope ────────────────────────────────────────────────────────
            bool activeIsPlan = ctx.ActiveView is ViewPlan && !ctx.ActiveView.IsTemplate;
            var scopeOptions = new List<string>();
            if (activeIsPlan) scopeOptions.Add("Active view only");
            scopeOptions.Add("Every plan view in the project");

            string scopeChoice = StingListPicker.Show(
                "Place Room Tags — scope",
                activeIsPlan ? "Which views?" : "The active view is not a plan view, so it is not offered.",
                scopeOptions);
            if (string.IsNullOrEmpty(scopeChoice)) return Result.Cancelled;

            List<View> views;
            if (scopeChoice.StartsWith("Active", StringComparison.Ordinal))
                views = new List<View> { ctx.ActiveView };
            else
                views = RoomTagPlacer.TaggablePlanViews(doc).Cast<View>().ToList();

            if (views.Count == 0)
            {
                TaskDialog.Show("STING — Place Room Tags", "No plan views to tag.");
                return Result.Cancelled;
            }

            // ── Tag type ─────────────────────────────────────────────────────
            var typeLabels = tagTypes
                .Select(t => t.FamilyName + " : " + t.Name)
                .ToList();
            string typeChoice = StingListPicker.Show(
                "Place Room Tags — tag type", "Which room tag type?", typeLabels);
            if (string.IsNullOrEmpty(typeChoice)) return Result.Cancelled;
            RoomTagType chosen = tagTypes[typeLabels.IndexOf(typeChoice)];

            // ── Place ────────────────────────────────────────────────────────
            var total = new RoomTagPlacer.PlacementTally();
            var perView = new List<string>();

            using (var tg = new TransactionGroup(doc, "STING Place Room Tags"))
            {
                tg.Start();
                foreach (View view in views)
                {
                    try
                    {
                        using (var t = new Transaction(doc, "STING Place Room Tags — " + view.Name))
                        {
                            t.Start();
                            var tally = RoomTagPlacer.PlaceInView(doc, view, chosen, withLeader: false);
                            t.Commit();

                            total.Placed += tally.Placed;
                            total.AlreadyHadTag += tally.AlreadyHadTag;
                            total.SkippedUnplaced += tally.SkippedUnplaced;
                            total.Failures.AddRange(tally.Failures);

                            if (tally.Placed > 0 || tally.Failures.Count > 0)
                                perView.Add(view.Name + ": placed " + tally.Placed +
                                            (tally.AlreadyHadTag > 0 ? ", kept " + tally.AlreadyHadTag : "") +
                                            (tally.Failures.Count > 0 ? ", FAILED " + tally.Failures.Count : ""));
                        }
                    }
                    catch (Exception ex)
                    {
                        // One bad view must not abandon the rest, but it must be reported.
                        total.Failures.Add("View '" + view.Name + "': " + ex.Message);
                        StingLog.Warn("PlaceRoomTags view " + view.Id + ": " + ex.Message);
                    }
                }
                tg.Assimilate();
            }

            var panel = StingResultPanel.Create("Place Room Tags")
                .SetSubtitle(total.Placed + " tag(s) placed across " + views.Count + " view(s).")
                .AddSection("Result")
                .Metric("Placed", total.Placed.ToString(CultureInfo.InvariantCulture))
                .Metric("Already tagged", total.AlreadyHadTag.ToString(CultureInfo.InvariantCulture),
                        "left alone — running this again is safe");

            if (total.SkippedUnplaced > 0)
                panel.MetricWarn("Unplaced / unbounded",
                    total.SkippedUnplaced.ToString(CultureInfo.InvariantCulture),
                    "no position to tag — run Room Audit");

            if (total.Failures.Count > 0)
            {
                panel.MetricError("Failed", total.Failures.Count.ToString(CultureInfo.InvariantCulture));
                panel.AddSection("Failures");
                foreach (string f in total.Failures.Take(100)) panel.Text(f);
                if (total.Failures.Count > 100)
                    panel.Text("…and " + (total.Failures.Count - 100) + " more. See the STING log.");
            }

            if (perView.Count > 0)
            {
                panel.AddSection("By view");
                foreach (string line in perView.Take(200)) panel.Text(line);
            }

            panel.Show();
            StingLog.Info("Place room tags: placed=" + total.Placed + " kept=" + total.AlreadyHadTag +
                          " failed=" + total.Failures.Count + " views=" + views.Count);
            return Result.Succeeded;
        }
    }
}
