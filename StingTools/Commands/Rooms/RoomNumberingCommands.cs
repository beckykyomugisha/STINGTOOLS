using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Select;   // StingListPicker lives in the Select namespace, not UI
using StingTools.Core.Rooms;
using StingTools.UI;

namespace StingTools.Commands.Rooms
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Room renumbering — the Revit half.
    //
    //  This file harvests, previews and writes. Every DECISION is made by
    //  RoomNumberPlanner, which is Revit-free and unit-tested
    //  (StingTools.Rooms.Tests). The split is the point: renumbering is easy to
    //  get subtly wrong and impossible to eyeball afterwards, because a wrong
    //  number looks exactly like a right one.
    // ─────────────────────────────────────────────────────────────────────────

    internal static class RoomNumberingEngine
    {
        /// <summary>Revit internal length is decimal feet; the planner works in millimetres.</summary>
        private const double FeetToMm = 304.8;

        /// <summary>
        /// Load the corporate baseline with the project override layered on top.
        /// Paths resolve through StingPaths — never by hand, per the path-discipline gate.
        /// </summary>
        internal static RoomNumberingLibrary LoadLibrary(Document doc, IList<string> warnings)
        {
            RoomNumberingLibrary baseline = new RoomNumberingLibrary();
            try
            {
                string baselinePath = StingToolsApp.FindDataFile(RoomNumberingStore.BaselineFileName);
                if (!string.IsNullOrEmpty(baselinePath) && File.Exists(baselinePath))
                    baseline = RoomNumberingStore.LoadFile(baselinePath, warnings);
                else
                    warnings.Add("The corporate baseline " + RoomNumberingStore.BaselineFileName +
                                 " was not found beside the plugin. Only project schemes are available.");
            }
            catch (Exception ex)
            {
                StingLog.Warn("RoomNumberingEngine.LoadLibrary baseline: " + ex.Message);
                warnings.Add("Could not read the corporate numbering baseline: " + ex.Message);
            }

            RoomNumberingLibrary project = new RoomNumberingLibrary();
            try
            {
                string projectPath = StingPaths.MetaFile(doc, "_BIM_COORD", RoomNumberingStore.ProjectFileName);
                project = RoomNumberingStore.LoadFile(projectPath, warnings);
            }
            catch (Exception ex)
            {
                StingLog.Warn("RoomNumberingEngine.LoadLibrary project: " + ex.Message);
                warnings.Add("Could not read the project numbering override: " + ex.Message);
            }

            return RoomNumberingStore.Merge(baseline, project, warnings);
        }

        /// <summary>Rooms that are placed AND enclosed. An unplaced or unbounded room has
        /// no position, so it has no place in a positional walk — it is reported, not guessed at.</summary>
        internal static List<Room> HarvestRooms(Document doc, View scopeView, out int skippedUnplaced)
        {
            skippedUnplaced = 0;
            var collector = scopeView != null
                ? new FilteredElementCollector(doc, scopeView.Id)
                : new FilteredElementCollector(doc);

            var rooms = new List<Room>();
            foreach (var el in collector.OfCategory(BuiltInCategory.OST_Rooms)
                                        .WhereElementIsNotElementType())
            {
                var room = el as Room;
                if (room == null) continue;

                if (room.Location == null || room.Area <= 0) { skippedUnplaced++; continue; }
                rooms.Add(room);
            }
            return rooms;
        }

        /// <summary>Turn Revit rooms into the planner's plain seeds.</summary>
        internal static List<RoomSeed> ToSeeds(Document doc, IEnumerable<Room> rooms)
        {
            var seeds = new List<RoomSeed>();
            foreach (var room in rooms)
            {
                double xMm = 0, yMm = 0, elevMm = 0;
                try
                {
                    var lp = room.Location as LocationPoint;
                    if (lp != null) { xMm = lp.Point.X * FeetToMm; yMm = lp.Point.Y * FeetToMm; }
                }
                catch (Exception ex) { StingLog.Warn("Room " + room.Id + " location: " + ex.Message); }

                try { if (room.Level != null) elevMm = room.Level.Elevation * FeetToMm; }
                catch (Exception ex) { StingLog.Warn("Room " + room.Id + " level: " + ex.Message); }

                string dept = "";
                try { dept = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString() ?? ""; }
                catch (Exception ex) { StingLog.Warn("Room " + room.Id + " department: " + ex.Message); }

                string name = "";
                try { name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? ""; }
                catch (Exception ex) { StingLog.Warn("Room " + room.Id + " name: " + ex.Message); }

                string number = "";
                try { number = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? ""; }
                catch (Exception ex) { StingLog.Warn("Room " + room.Id + " number: " + ex.Message); }

                seeds.Add(new RoomSeed
                {
                    Key = room.Id.Value.ToString(CultureInfo.InvariantCulture),
                    XMm = xMm,
                    YMm = yMm,
                    LevelElevationMm = elevMm,
                    // The same level code the tag pipeline uses, so a room number and the
                    // LVL token of everything inside it cannot disagree.
                    LevelCode = ParameterHelpers.GetLevelCode(doc, room) ?? "",
                    Department = dept,
                    Name = string.IsNullOrWhiteSpace(name) ? ("Room " + room.Id) : name,
                    ExistingNumber = number,
                });
            }
            return seeds;
        }

        /// <summary>
        /// Write the plan. Two passes, deliberately.
        ///
        /// Revit rejects a duplicate room number at the moment of the write, so renumbering
        /// A→B while some room still holds B fails even when the FINAL state is conflict-free
        /// (any cycle or shift does this: 01→02, 02→03). Pass 1 parks every changing room on
        /// a number nothing can hold; pass 2 writes the real ones into the space that frees up.
        /// </summary>
        internal static int Apply(Document doc, RoomNumberPlanResult plan, out List<string> failures)
        {
            failures = new List<string>();
            var changing = plan.Assignments.Where(a => a.Changed).ToList();
            if (changing.Count == 0) return 0;

            string parkPrefix = "~STING" + DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture) + "-";
            var parked = new List<Tuple<Room, RoomNumberAssignment>>();

            // ── Pass 1: park ──────────────────────────────────────────────────
            int park = 0;
            foreach (var a in changing)
            {
                Room room = Resolve(doc, a.Key);
                if (room == null)
                {
                    failures.Add(a.Name + ": the room was deleted between planning and applying.");
                    continue;
                }
                var p = room.get_Parameter(BuiltInParameter.ROOM_NUMBER);
                if (p == null || p.IsReadOnly)
                {
                    failures.Add(a.Name + ": room number is read-only (a linked or workset-locked room?).");
                    continue;
                }
                try
                {
                    p.Set(parkPrefix + (park++).ToString(CultureInfo.InvariantCulture));
                    parked.Add(Tuple.Create(room, a));
                }
                catch (Exception ex)
                {
                    failures.Add(a.Name + ": could not park the old number — " + ex.Message);
                    StingLog.Warn("RoomNumbering park " + room.Id + ": " + ex.Message);
                }
            }

            // ── Pass 2: write ─────────────────────────────────────────────────
            int written = 0;
            foreach (var pair in parked)
            {
                try
                {
                    pair.Item1.get_Parameter(BuiltInParameter.ROOM_NUMBER).Set(pair.Item2.NewNumber);
                    written++;
                }
                catch (Exception ex)
                {
                    // Leaving a room parked on a ~STING number would be worse than failing:
                    // it looks like a number. Put the old one back and say so.
                    failures.Add(pair.Item2.Name + ": could not set '" + pair.Item2.NewNumber +
                                 "' — " + ex.Message);
                    StingLog.Warn("RoomNumbering write " + pair.Item1.Id + ": " + ex.Message);
                    try { pair.Item1.get_Parameter(BuiltInParameter.ROOM_NUMBER).Set(pair.Item2.OldNumber ?? ""); }
                    catch (Exception restoreEx)
                    {
                        failures.Add(pair.Item2.Name + ": AND the old number '" +
                                     (pair.Item2.OldNumber ?? "") + "' could not be restored — " +
                                     restoreEx.Message + ". This room is left on a ~STING placeholder.");
                        StingLog.Error("RoomNumbering restore " + pair.Item1.Id, restoreEx);
                    }
                }
            }
            return written;
        }

        private static Room Resolve(Document doc, string key)
        {
            long id;
            if (!long.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) return null;
            try { return doc.GetElement(new ElementId(id)) as Room; }
            catch (Exception ex) { StingLog.Warn("RoomNumbering resolve " + key + ": " + ex.Message); return null; }
        }
    }

    /// <summary>
    /// Renumber rooms by position — plan, preview, confirm, then write.
    /// Command tag: <c>Rooms_Renumber</c>.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RoomRenumberCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var warnings = new List<string>();
            var library = RoomNumberingEngine.LoadLibrary(doc, warnings);
            if (library.Schemes.Count == 0)
            {
                TaskDialog.Show("STING — Room Renumber",
                    "No numbering schemes are available.\n\n" +
                    "Expected " + RoomNumberingStore.BaselineFileName + " beside the plugin, or " +
                    RoomNumberingStore.ProjectFileName + " in the project's _BIM_COORD folder.");
                return Result.Failed;
            }

            // ── Scope ────────────────────────────────────────────────────────
            string scopeChoice = StingListPicker.Show(
                "Room Renumber — scope",
                "Which rooms should be renumbered?",
                new List<string> { "Active view only", "Whole project" });
            if (string.IsNullOrEmpty(scopeChoice)) return Result.Cancelled;

            bool activeViewOnly = scopeChoice.StartsWith("Active", StringComparison.Ordinal);
            View scopeView = activeViewOnly ? ctx.ActiveView : null;
            if (activeViewOnly && scopeView == null)
            {
                TaskDialog.Show("STING — Room Renumber", "There is no active view to scope to.");
                return Result.Cancelled;
            }

            // ── Scheme ───────────────────────────────────────────────────────
            var labels = library.Schemes.Select(s => s.Id + "  —  " + s.Name).ToList();
            string schemeChoice = StingListPicker.Show(
                "Room Renumber — scheme",
                "Corporate baseline plus this project's overrides. Default: " +
                (library.DefaultSchemeId ?? "(none)"),
                labels);
            if (string.IsNullOrEmpty(schemeChoice)) return Result.Cancelled;

            var scheme = library.Schemes[labels.IndexOf(schemeChoice)];

            // ── Harvest + plan. Nothing is written yet. ──────────────────────
            int skippedUnplaced;
            var rooms = RoomNumberingEngine.HarvestRooms(doc, scopeView, out skippedUnplaced);
            var seeds = RoomNumberingEngine.ToSeeds(doc, rooms);
            var plan = RoomNumberPlanner.Plan(seeds, scheme);

            if (!plan.CanApply)
            {
                var blocked = StingResultPanel.Create("Room Renumber — blocked")
                    .SetSubtitle("Nothing was written. " + plan.Blockers.Count + " blocker(s).")
                    .AddSection("Blockers");
                foreach (string b in plan.Blockers) blocked.Text(b);
                if (warnings.Count > 0)
                {
                    blocked.AddSection("Warnings");
                    foreach (string w in warnings) blocked.Text(w);
                }
                blocked.Show();
                return Result.Cancelled;
            }

            if (plan.Assignments.Count == 0)
            {
                TaskDialog.Show("STING — Room Renumber",
                    "No placed, enclosed rooms in scope." +
                    (skippedUnplaced > 0
                        ? "\n\n" + skippedUnplaced + " room(s) are unplaced or unbounded and were " +
                          "skipped — they have no position to walk. Run Room Audit to find them."
                        : ""));
                return Result.Succeeded;
            }

            // ── Preview ──────────────────────────────────────────────────────
            var preview = StingResultPanel.Create("Room Renumber — preview")
                .SetSubtitle("Nothing has been written yet. Scheme: " + scheme.Name)
                .AddSection("Summary")
                .Metric("Rooms in scope", plan.Assignments.Count.ToString(CultureInfo.InvariantCulture))
                .Metric("Will change", plan.ChangedCount.ToString(CultureInfo.InvariantCulture))
                .Metric("Kept (already numbered)", plan.PreservedCount.ToString(CultureInfo.InvariantCulture),
                        scheme.PreserveExistingNumbers ? "PreserveExistingNumbers is on" : null);

            if (skippedUnplaced > 0)
                preview.MetricWarn("Skipped", skippedUnplaced.ToString(CultureInfo.InvariantCulture),
                                   "unplaced or unbounded — no position to walk");

            if (warnings.Count > 0)
            {
                preview.AddSection("Warnings");
                foreach (string w in warnings) preview.Text(w);
            }

            var changes = plan.Assignments.Where(a => a.Changed).ToList();
            if (changes.Count == 0)
            {
                preview.AddSection("Result").Text("Every room already carries the number this scheme would give it.");
                preview.Show();
                return Result.Succeeded;
            }

            preview.AddSection("Changes (" + changes.Count + ")");
            foreach (var a in changes.Take(300))
            {
                long id;
                long.TryParse(a.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
                preview.Finding(
                    (string.IsNullOrEmpty(a.OldNumber) ? "(no number)" : a.OldNumber) +
                    "  →  " + a.NewNumber + "   " + a.Name, id);
            }
            if (changes.Count > 300)
                preview.Text("…and " + (changes.Count - 300) + " more.");
            preview.Show();

            // ── Confirm ──────────────────────────────────────────────────────
            var confirm = new TaskDialog("STING — Room Renumber")
            {
                MainInstruction = "Renumber " + changes.Count + " room(s)?",
                MainContent =
                    "A room number is referenced by drawings, schedules, RFIs, snags and O&M " +
                    "records that STING cannot see. Renumbering after an issue silently " +
                    "invalidates every one of them.\n\n" +
                    "The preview window lists every change. This can be undone with Ctrl+Z.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
            };
            if (confirm.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            // ── Apply ────────────────────────────────────────────────────────
            int written;
            List<string> failures;
            using (var t = new Transaction(doc, "STING Renumber Rooms"))
            {
                t.Start();
                written = RoomNumberingEngine.Apply(doc, plan, out failures);
                t.Commit();
            }

            // A room number feeds LOC/ZONE derivation, so the cached index is now stale.
            SpatialAutoDetect.ForceRefresh();

            var done = StingResultPanel.Create("Room Renumber — done")
                .SetSubtitle(written + " of " + changes.Count + " room(s) renumbered.")
                .AddSection("Result")
                .Metric("Renumbered", written.ToString(CultureInfo.InvariantCulture));

            if (failures.Count > 0)
            {
                done.MetricError("Failed", failures.Count.ToString(CultureInfo.InvariantCulture));
                done.AddSection("Failures");
                foreach (string f in failures.Take(100)) done.Text(f);
            }
            done.Show();

            StingLog.Info("Room renumber: scheme=" + scheme.Id + " written=" + written +
                          " failed=" + failures.Count + " skipped=" + skippedUnplaced);
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Read-only: which numbering schemes are available, where they came from, and what
    /// the active one would produce. Command tag: <c>Rooms_NumberingInspect</c>.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class RoomNumberingInspectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var warnings = new List<string>();
            var library = RoomNumberingEngine.LoadLibrary(ctx.Doc, warnings);

            string projectPath = "(unresolved)";
            try { projectPath = StingPaths.MetaFile(ctx.Doc, "_BIM_COORD", RoomNumberingStore.ProjectFileName); }
            catch (Exception ex) { StingLog.Warn("RoomNumberingInspect path: " + ex.Message); }

            var panel = StingResultPanel.Create("Room Numbering — schemes")
                .SetSubtitle(library.Schemes.Count + " scheme(s). Default: " +
                             (library.DefaultSchemeId ?? "(none)"))
                .AddSection("Where these come from")
                .Text("Corporate baseline: " + RoomNumberingStore.BaselineFileName + " (beside the plugin)")
                .Text("Project override: " + projectPath +
                      (File.Exists(projectPath) ? "  [present]" : "  [not created]"))
                .Text("Project schemes win by id; a project defaultSchemeId wins outright.");

            if (warnings.Count > 0)
            {
                panel.AddSection("Warnings");
                foreach (string w in warnings) panel.Text(w);
            }

            panel.AddSection("Schemes");
            foreach (var s in library.Schemes)
            {
                bool isDefault = string.Equals(s.Id, library.DefaultSchemeId, StringComparison.OrdinalIgnoreCase);
                panel.Metric(s.Id + (isDefault ? "  (default)" : ""), s.Pattern,
                             s.Order + ", rows " + s.RowBandMm.ToString("F0", CultureInfo.InvariantCulture) +
                             " mm, from " + s.StartAt + " step " + s.Step +
                             (s.PreserveExistingNumbers ? ", keeps existing numbers" : ", RENUMBERS EVERYTHING"));
            }

            // Show what the default would actually produce, rather than only describing it.
            var scheme = RoomNumberingStore.Resolve(library, null);
            if (scheme != null)
            {
                int skipped;
                var rooms = RoomNumberingEngine.HarvestRooms(ctx.Doc, null, out skipped);
                var plan = RoomNumberPlanner.Plan(RoomNumberingEngine.ToSeeds(ctx.Doc, rooms), scheme);

                panel.AddSection("What '" + scheme.Id + "' would do to this model");
                if (!plan.CanApply)
                    foreach (string b in plan.Blockers) panel.Text(b);
                else
                {
                    panel.Metric("Rooms in scope", plan.Assignments.Count.ToString(CultureInfo.InvariantCulture));
                    panel.Metric("Would change", plan.ChangedCount.ToString(CultureInfo.InvariantCulture));
                    if (skipped > 0)
                        panel.MetricWarn("Unplaced / unbounded", skipped.ToString(CultureInfo.InvariantCulture),
                                         "skipped — run Room Audit");
                }
            }

            panel.Show();
            return Result.Succeeded;
        }
    }
}
