// StingTools v4 MVP — fixture placement engine.
//
// Public entry point: PlaceFixturesInScope(doc, roomIds, rules, dryRun)
// returns a PlacementResult containing placed ElementIds, skipped
// count, per-rule + per-room counts and a warnings list.
//
// The engine never throws: every per-element failure is trapped,
// surfaced as a warning and skipped so a single broken family does
// not abort the entire batch.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;

namespace StingTools.Core.Placement
{
    /// <summary>
    /// Engine result surfaced in StingResultPanel + logged.
    /// </summary>
    public class PlacementResult
    {
        public List<ElementId> PlacedIds { get; } = new List<ElementId>();
        public int SkippedCount { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        public Dictionary<string, int> CountsByRule { get; } = new Dictionary<string, int>();
        public Dictionary<string, int> CountsByRoom { get; } = new Dictionary<string, int>();
        public int RoomsVisited { get; set; }
        public int CandidatesEvaluated { get; set; }
        public bool DryRun { get; set; }

        // Phase 139.2 G — additional fields surfaced from subsystems.
        public List<XYZ> NogginRequiredPoints { get; } = new List<XYZ>();
        public int       TileSnapAdjustments  { get; set; }
    }

    /// <summary>
    /// Stateless engine. Reads the rule library via PlacementRuleLoader
    /// and delegates per-candidate scoring to PlacementScorer.
    /// </summary>
    public static partial class FixturePlacementEngine
    {
        private const double MmToFt = 1.0 / 304.8;

        // Phase 139.12 — coarse phase reporter, polled by the WPF
        // progress dialog so the heartbeat can show "Pre-flight: catalogue" /
        // "Pre-flight: two-phase first-fix" / "Per-room loop" instead of
        // an opaque elapsed-time counter.
        public static volatile string CurrentPhase = "";

        // Phase 139.21 — build stamp. Reads the assembly's PE-header
        // timestamp (the second the DLL was linked) so the Centre title
        // bar + result panel can surface it on every run. If two
        // consecutive runs report the same BuildStamp, the user is on
        // the same DLL — i.e. the build cache hasn't refreshed and the
        // new code isn't loaded. Cached on first read.
        private static string _buildStamp;
        public static string BuildStamp
        {
            get
            {
                if (_buildStamp != null) return _buildStamp;
                try
                {
                    var asm = typeof(FixturePlacementEngine).Assembly;
                    string path = asm.Location;
                    var dt = !string.IsNullOrEmpty(path) && System.IO.File.Exists(path)
                        ? System.IO.File.GetLastWriteTime(path)
                        : DateTime.MinValue;
                    _buildStamp = dt == DateTime.MinValue
                        ? "(unknown)"
                        : dt.ToString("yyyy-MM-dd HH:mm:ss");
                }
                catch { _buildStamp = "(unknown)"; }
                return _buildStamp;
            }
        }

        // Phase 139.21 — phase tag stable for grep / diagnostics.
        public const string PhaseTag = "Phase 139.21";

        /// <summary>
        /// Entry point. If rules is null/empty, loads the default + project
        /// override library. If dryRun is true, returns candidates without
        /// placing anything (the UI shows a preview).
        /// </summary>
        /// <summary>Original 4-arg entry point — preserved for back-compat.
        /// Delegates to the 5-arg overload with a no-op progress callback.</summary>
        public static PlacementResult PlaceFixturesInScope(
            Document doc,
            IList<ElementId> roomIds,
            IList<PlacementRule> rules,
            bool dryRun)
            => PlaceFixturesInScope(doc, roomIds, rules, dryRun, progress: null);

        /// <summary>Long-form entry point with a progress hook. The
        /// callback is invoked once per room with (processed, total) so the
        /// Placement Centre can show a StingProgressDialog and let the user
        /// abort. Returning <c>true</c> from the callback aborts the run
        /// after the current room commits — partial work is kept (still
        /// inside the engine's Transaction so callers can roll back at the
        /// outer TransactionGroup level if they want all-or-nothing).</summary>
        public static PlacementResult PlaceFixturesInScope(
            Document doc,
            IList<ElementId> roomIds,
            IList<PlacementRule> rules,
            bool dryRun,
            Func<int, int, bool> progress)
        {
            var result = new PlacementResult { DryRun = dryRun };
            if (doc == null)
            {
                result.Warnings.Add("FixturePlacementEngine: document is null");
                return result;
            }

            if (rules == null || rules.Count == 0)
            {
                try { rules = PlacementRuleLoader.Load(doc.PathName); }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Rule load failed: {ex.Message}");
                    rules = new List<PlacementRule>();
                }
            }

            if (rules.Count == 0)
            {
                result.Warnings.Add("No placement rules found. Ship STING_PLACEMENT_RULES.json or provide a project override.");
                return result;
            }

            var rooms = CollectRooms(doc, roomIds, result);
            result.RoomsVisited = rooms.Count;
            if (rooms.Count == 0) return result;

            var scorer = new PlacementScorer(doc)
            {
                // Carry the Fixtures-tab RejectInsideWall option through
                // to the scorer so ElementIntersectsSolidFilter runs on
                // every candidate. Default off for fast-path placement
                // in large rooms; on by default in the UI because
                // users expect fixtures not to sit inside walls.
                RejectInsideWall = StingTools.Commands.Placement.PlaceFixturesOptions.RejectInsideWall,
            };
            var perCategorySymbol = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);

            // Rule ordering: higher Priority first so specialised rules
            // win room capacity slots before generic fallbacks.
            var ordered = rules.OrderByDescending(r => r.Priority).ToList();

            // Phase 139.5 Q21 — pre-compile each rule's RoomFilter +
            // ExcludeRoomFilter regex once, so the per-room loop's
            // pre-filter check is regex.IsMatch(roomName) instead of a
            // full RoomMatchesScope evaluation including parameter reads.
            // Rules with empty RoomFilter still iterate every room.
            var roomFilterRx = new Dictionary<string, System.Text.RegularExpressions.Regex>(StringComparer.Ordinal);
            var excludeFilterRx = new Dictionary<string, System.Text.RegularExpressions.Regex>(StringComparer.Ordinal);
            foreach (var r in ordered)
            {
                if (!string.IsNullOrEmpty(r.RoomFilter))
                {
                    try { roomFilterRx[r.MergeKey] = new System.Text.RegularExpressions.Regex(r.RoomFilter, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled); }
                    catch (Exception ex) { result.Warnings.Add($"Rule '{r.MergeKey}' RoomFilter regex: {ex.Message}"); }
                }
                if (!string.IsNullOrEmpty(r.ExcludeRoomFilter))
                {
                    try { excludeFilterRx[r.MergeKey] = new System.Text.RegularExpressions.Regex(r.ExcludeRoomFilter, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled); }
                    catch (Exception ex) { result.Warnings.Add($"Rule '{r.MergeKey}' ExcludeRoomFilter regex: {ex.Message}"); }
                }
            }

            Transaction tx = null;
            if (!dryRun)
            {
                tx = new Transaction(doc, "STING v4 Place Fixtures");
                try { tx.Start(); }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Transaction start failed: {ex.Message}");
                    return result;
                }
            }

            // Phase 139.2 G — Step 0: pre-flight subsystem hooks.
            // Phase 139.12 — instrumented + bounded.  Each phase logs
            // elapsed ms via StingLog and AutoPopulate now runs on a
            // bounded background task so a hung Revit LookupParameter
            // can no longer wedge the whole run.
            CurrentPhase = "Pre-flight starting";
            var swEngine = System.Diagnostics.Stopwatch.StartNew();
            StingLog.Info($"FixturePlacementEngine: run start — {rooms.Count} rooms, {ordered.Count} rules.");
            try
            {
                bool needsCatalogue = ordered.Any(r => r != null
                    && (!string.IsNullOrEmpty(r.ManufacturerCode)
                        || !string.IsNullOrEmpty(r.CatalogueRef)
                        || r.IsClusterMember));
                if (needsCatalogue)
                {
                    CurrentPhase = "Pre-flight: catalogue scan";
                    var swCat = System.Diagnostics.Stopwatch.StartNew();
                    var task = System.Threading.Tasks.Task.Run(() =>
                    {
                        try { ManufacturerCatalogueRegistry.AutoPopulateFromFamilies(doc); }
                        catch (Exception ex) { StingLog.Warn($"AutoPopulate inner: {ex.Message}"); }
                    });
                    if (!task.Wait(TimeSpan.FromSeconds(20)))
                    {
                        result.Warnings.Add("Catalogue auto-populate timed out after 20s — skipped. Manufacturer score component will be 0.5 for unresolved rules.");
                        StingLog.Warn("FixturePlacementEngine: AutoPopulate timed out — proceeding without catalogue refresh.");
                    }
                    else
                    {
                        StingLog.Info($"FixturePlacementEngine: AutoPopulate done in {swCat.ElapsedMilliseconds} ms.");
                    }
                }
                else
                    StingLog.Info("FixturePlacementEngine: skipping AutoPopulate — no rule references manufacturer fields.");
            }
            catch (Exception ex) { result.Warnings.Add($"Catalogue auto-populate: {ex.Message}"); }
            try
            {
                CurrentPhase = "Pre-flight: two-phase shared-param check";
                var swVal = System.Diagnostics.Stopwatch.StartNew();
                TwoPhaseBoxPlacer.ValidateSharedParams(doc, ordered, result.Warnings);
                StingLog.Info($"FixturePlacementEngine: ValidateSharedParams done in {swVal.ElapsedMilliseconds} ms.");
            }
            catch (Exception ex) { result.Warnings.Add($"Two-phase preflight: {ex.Message}"); }

            // Phase 139.2 G — Step 1: place every first-fix box for the
            // TwoPhaseEnabled rules.  These run before per-room iteration
            // so the second-fix matching index covers the whole scope.
            Dictionary<string, XYZ> firstFixIndex = null;
            if (!dryRun)
            {
                bool anyTwoPhase = ordered.Any(r => r != null && r.TwoPhaseEnabled);
                if (!anyTwoPhase)
                {
                    StingLog.Info("FixturePlacementEngine: no TwoPhaseEnabled rule — skipping first-fix pass.");
                }
                else
                {
                    CurrentPhase = "Pre-flight: first-fix box placement";
                    var swFf = System.Diagnostics.Stopwatch.StartNew();
                    try { firstFixIndex = TwoPhaseBoxPlacer.PlaceFirstFixBoxes(doc, roomIds, ordered, result); }
                    catch (Exception ex) { result.Warnings.Add($"Two-phase first-fix: {ex.Message}"); }
                    StingLog.Info($"FixturePlacementEngine: PlaceFirstFixBoxes done in {swFf.ElapsedMilliseconds} ms ({firstFixIndex?.Count ?? 0} boxes).");
                }
            }

            try
            {
                int processed = 0;
                int total = rooms.Count;
                bool cancelled = false;
                StingLog.Info($"FixturePlacementEngine: pre-flight finished in {swEngine.ElapsedMilliseconds} ms — entering per-room loop ({total} rooms).");
                CurrentPhase = "Per-room loop";
                foreach (var room in rooms)
                {
                    // PC-13 — per-room state so dependent rules see predecessors.
                    var roomState = new RoomState();
                    string roomName = SafeRoomName(room);
                    foreach (var rule in ordered)
                    {
                        // Phase 139.5 Q21 — fast filter using pre-compiled regex
                        // before paying the cost of scorer.Score / RoomMatchesScope
                        // (which reads parameters, level, phase, workset).
                        if (roomFilterRx.TryGetValue(rule.MergeKey, out var rfx)
                            && !rfx.IsMatch(roomName ?? "")) continue;
                        if (excludeFilterRx.TryGetValue(rule.MergeKey, out var efx)
                            && efx.IsMatch(roomName ?? "")) continue;

                        try
                        {
                            // PC-13 — ConflictsWith: skip if any conflicting rule already fired in this room.
                            if (RuleHasConflict(rule, roomState))
                            {
                                result.Warnings.Add($"Room {room.Id} / {rule.MergeKey}: skipped — ConflictsWith already fired.");
                                continue;
                            }
                            // PC-13 — DependsOn: skip if predecessor produced no placements yet.
                            if (!string.IsNullOrEmpty(rule.DependsOn) && !roomState.PlacedByRule.ContainsKey(rule.DependsOn))
                            {
                                result.Warnings.Add($"Room {room.Id} / {rule.MergeKey}: skipped — DependsOn '{rule.DependsOn}' has no placement in this room.");
                                continue;
                            }

                            ProcessRoomRule(doc, room, rule, scorer,
                                perCategorySymbol, result, dryRun, roomState);

                            // PC-13 — CoPlaceWith: fire each co-rule at the same XYZ as the primary's last point.
                            if (rule.CoPlaceWith != null && rule.CoPlaceWith.Count > 0
                                && roomState.LastPointByRule.TryGetValue(rule.MergeKey, out var lastPt))
                            {
                                foreach (var coId in rule.CoPlaceWith)
                                {
                                    var coRule = ordered.FirstOrDefault(r => string.Equals(r.MergeKey, coId, StringComparison.OrdinalIgnoreCase));
                                    if (coRule == null) continue;
                                    try
                                    {
                                        ProcessRoomRuleAtPoint(doc, room, coRule, scorer, perCategorySymbol, result, dryRun, roomState, lastPt);
                                    }
                                    catch (Exception cex)
                                    {
                                        result.Warnings.Add($"Co-place {coRule.MergeKey} in room {room.Id}: {cex.Message}");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add($"Room {room.Id} / {rule.MergeKey}: {ex.Message}");
                            result.SkippedCount++;
                        }
                    }
                    processed++;
                    if (progress != null)
                    {
                        try
                        {
                            if (progress(processed, total))
                            {
                                cancelled = true;
                                result.Warnings.Add($"Cancelled after {processed} of {total} room(s).");
                                break;
                            }
                        }
                        catch (Exception pgEx)
                        {
                            result.Warnings.Add($"Progress callback: {pgEx.Message}");
                        }
                    }
                }

                // Phase 139.2 G — Step 3: place every second-fix device
                // by GUID-proximity match to the first-fix index.
                if (!dryRun && firstFixIndex != null && firstFixIndex.Count > 0)
                {
                    try { TwoPhaseBoxPlacer.PlaceSecondFixDevices(doc, roomIds, ordered, firstFixIndex, result); }
                    catch (Exception ex) { result.Warnings.Add($"Two-phase second-fix: {ex.Message}"); }
                }

                if (!dryRun)
                {
                    tx.Commit();
                }
                if (cancelled)
                    StingLog.Info($"FixturePlacementEngine: run cancelled after {processed}/{total} rooms.");
            }
            catch (Exception ex)
            {
                if (tx != null && tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                result.Warnings.Add($"FixturePlacementEngine fatal: {ex.Message}");
            }
            finally
            {
                tx?.Dispose();
            }

            return result;
        }

        private static List<Room> CollectRooms(Document doc, IList<ElementId> roomIds, PlacementResult result)
        {
            var rooms = new List<Room>();
            try
            {
                if (roomIds == null || roomIds.Count == 0)
                {
                    var collector = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType();
                    foreach (var e in collector)
                        if (e is Room r && r.Area > 0.0) rooms.Add(r);
                }
                else
                {
                    foreach (var id in roomIds)
                    {
                        var el = doc.GetElement(id);
                        if (el is Room r && r.Area > 0.0) rooms.Add(r);
                    }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Room collection failed: {ex.Message}");
            }
            return rooms;
        }

        /// <summary>PC-13 per-room state: maps RuleId/MergeKey → list of placed points,
        /// plus a "last point" lookup for CoPlaceWith / RELATIVE_TO.</summary>
        private class RoomState
        {
            public Dictionary<string, List<XYZ>> PlacedByRule { get; }
                = new Dictionary<string, List<XYZ>>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, XYZ> LastPointByRule { get; }
                = new Dictionary<string, XYZ>(StringComparer.OrdinalIgnoreCase);
        }

        private static bool RuleHasConflict(PlacementRule rule, RoomState state)
        {
            if (rule?.ConflictsWith == null || rule.ConflictsWith.Count == 0) return false;
            foreach (var c in rule.ConflictsWith)
                if (!string.IsNullOrEmpty(c) && state.PlacedByRule.ContainsKey(c)) return true;
            return false;
        }

        private static void ProcessRoomRule(
            Document doc,
            Room room,
            PlacementRule rule,
            PlacementScorer scorer,
            Dictionary<string, FamilySymbol> perCategorySymbol,
            PlacementResult result,
            bool dryRun,
            RoomState state)
        {
            string roomKey = $"{room.Id}::{SafeRoomName(room)}";
            int alreadyInRoom = result.CountsByRoom.ContainsKey(roomKey) ? result.CountsByRoom[roomKey] : 0;

            var placedPoints = new List<XYZ>(); // for spacing scoring

            // PC-13 — RELATIVE_TO / EQUIPMENT_PAIR: short-circuit by stamping the
            // predecessor's last point as the only candidate.
            string anchor = (rule.AnchorType ?? "").ToUpperInvariant();
            List<PlacementCandidate> candidates;
            if ((anchor == "RELATIVE_TO" || anchor == "EQUIPMENT_PAIR")
                && !string.IsNullOrEmpty(rule.DependsOn)
                && state.LastPointByRule.TryGetValue(rule.DependsOn, out var prev))
            {
                XYZ pt = new XYZ(prev.X + rule.OffsetXMm / 304.8,
                                 prev.Y + rule.OffsetYMm / 304.8,
                                 prev.Z + rule.OffsetZMm / 304.8);
                candidates = new List<PlacementCandidate>
                {
                    new PlacementCandidate { Position = pt, RoomId = room.Id, Rule = rule, Score = 1.0 }
                };
                result.CandidatesEvaluated += 1;
            }
            else
            {
                candidates = scorer.Score(room, rule, placedPoints, alreadyInRoom);
                result.CandidatesEvaluated += candidates.Count;
            }
            if (candidates.Count == 0) return;

            // PC-12 — derive the count for Density / Linear rules from the room's
            // area, occupancy or perimeter, capped by MaxPerRoom when set.
            int cap = ComputeCap(rule, room, candidates.Count, alreadyInRoom);
            if (cap == 0) return;

            var chosen = candidates.Take(cap).ToList();

            if (dryRun)
            {
                foreach (var c in chosen)
                {
                    result.CountsByRule[rule.MergeKey] = result.CountsByRule.TryGetValue(rule.MergeKey, out var n) ? n + 1 : 1;
                    result.CountsByRoom[roomKey] = result.CountsByRoom.TryGetValue(roomKey, out var m) ? m + 1 : 1;
                    if (state != null)
                    {
                        if (!state.PlacedByRule.TryGetValue(rule.MergeKey, out var lst))
                        {
                            lst = new List<XYZ>();
                            state.PlacedByRule[rule.MergeKey] = lst;
                        }
                        lst.Add(c.Position);
                        state.LastPointByRule[rule.MergeKey] = c.Position;
                    }
                }
                return;
            }

            FamilySymbol symbol = ResolveSymbol(doc, rule.CategoryFilter, rule, perCategorySymbol, result);
            if (symbol == null) return;

            // Phase 139.18 — warn once per (rule, family) when a wall- or
            // ceiling-anchored rule resolves to an un-hosted family. The
            // engine still places it (post-placement wall-snap + rotation
            // does its best), but designers should re-author the family
            // as wall-hosted / ceiling-hosted for proper attachment.
            try
            {
                string anchorPt = (rule.AnchorType ?? "").ToUpperInvariant();
                bool wantsWallHost = anchorPt == "WALL_MIDPOINT" || anchorPt == "WALL_CORNER"
                                  || anchorPt == "WALL_FACE_OFFSET"
                                  || anchorPt.StartsWith("DOOR_")
                                  || anchorPt.StartsWith("WINDOW_");
                bool wantsCeilingHost = anchorPt == "CEILING_CENTRE"
                                  || anchorPt == "CEILING_TILE_CENTRE"
                                  || anchorPt == "LIGHTING_GRID"
                                  || anchorPt == "LUX_GRID";
                var fpt = symbol.Family?.FamilyPlacementType ?? FamilyPlacementType.Invalid;
                bool isHosted = fpt == FamilyPlacementType.OneLevelBasedHosted;
                if ((wantsWallHost || wantsCeilingHost) && !isHosted)
                {
                    string warnKey = "FamilyPlacementType:" + (symbol.Family?.Name ?? "?") + ":" + rule.MergeKey;
                    if (!result.Warnings.Any(w => w.Contains(warnKey)))
                    {
                        result.Warnings.Add(
                            $"{warnKey} — rule '{rule.MergeKey}' uses {anchorPt} but the resolved family " +
                            $"'{symbol.Family?.Name}' is {fpt}, not OneLevelBasedHosted. Engine will place " +
                            $"+ snap + rotate but the family won't attach to the wall/ceiling. " +
                            $"Re-author the family as wall-hosted (or ceiling-hosted) for proper attachment.");
                    }
                }
            }
            catch { }

            if (!symbol.IsActive)
            {
                try { symbol.Activate(); doc.Regenerate(); }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Activate symbol {symbol.Name} failed: {ex.Message}");
                    return;
                }
            }

            // Phase 139.23 — dedup. Revit warns "There are identical instances
            // in the same place" when two of the same symbol land within
            // model-tolerance of each other. The per-room state already has
            // every placement from this run; reject candidates whose XYZ
            // is within rule.ToleranceMm of any existing placement (any
            // rule's, not just this rule's) so adjacent-room overlaps and
            // co-located rules don't double up.
            var existingNearby = new List<XYZ>();
            try
            {
                if (state?.PlacedByRule != null)
                    foreach (var lst in state.PlacedByRule.Values)
                        if (lst != null) existingNearby.AddRange(lst);
            }
            catch { }
            double dedupFt = Math.Max(rule.ToleranceMm, 25.0) * MmToFt;
            double dedupSq = dedupFt * dedupFt;

            foreach (var c in chosen)
            {
                bool tooClose = false;
                foreach (var ex in existingNearby)
                {
                    if (ex == null) continue;
                    double dx = ex.X - c.Position.X;
                    double dy = ex.Y - c.Position.Y;
                    double dz = ex.Z - c.Position.Z;
                    if (dx * dx + dy * dy + dz * dz < dedupSq) { tooClose = true; break; }
                }
                if (tooClose)
                {
                    result.SkippedCount++;
                    continue;
                }
                try
                {
                    // Pre-flight picks the right NewFamilyInstance overload
                    // for the family's FamilyPlacementType (level-based vs
                    // hosted) and locates a host element when the family
                    // template requires one. Falls through to skip + warn
                    // when the placement type isn't supported, instead of
                    // silently creating a host-less ghost instance that
                    // schedules later miss in QTO / COBie.
                    var pf = PlacementHostPreflight.Place(doc, symbol, room, c.Position, rule);
                    FamilyInstance fi = pf.Placed;
                    if (pf.Skipped || fi == null)
                    {
                        result.SkippedCount++;
                        if (!string.IsNullOrEmpty(pf.Reason))
                            result.Warnings.Add(pf.Reason);
                        continue;
                    }

                    WriteAnchorParameters(fi, rule);
                    OrientPlacedInstance(doc, fi, rule, room);
                    // Phase 139.16 diagnostic: log placement XYZ + distance to host
                    // wall (post-snap) so users / I can verify wall-anchored rules
                    // landed flush on the wall rather than in the centre of the room.
                    try
                    {
                        XYZ p = (fi.Location as LocationPoint)?.Point;
                        if (p != null)
                            StingLog.Info($"FixturePlacementEngine: placed '{rule.MergeKey}' at ({p.X:F2},{p.Y:F2},{p.Z:F2}) host={(fi.Host?.GetType().Name ?? "<none>")}.");
                    } catch { }
                    // Pack 123 / Gap E — stamp provenance so BOQ / cleanup /
                    // audit can identify auto-created fixtures. Centre's
                    // "Stamp provenance" checkbox flips PlaceFixturesOptions.
                    if (StingTools.Commands.Placement.PlaceFixturesOptions.StampProvenance)
                    {
                        try
                        {
                            StingTools.Core.Storage.StingProvenanceSchema.Stamp(
                                fi, "FixturePlacementEngine", rule?.MergeKey ?? "");
                        }
                        catch (Exception pvEx) { result.Warnings.Add($"Provenance stamp: {pvEx.Message}"); }
                    }
                    result.PlacedIds.Add(fi.Id);
                    result.CountsByRule[rule.MergeKey] = result.CountsByRule.TryGetValue(rule.MergeKey, out var n) ? n + 1 : 1;
                    result.CountsByRoom[roomKey] = result.CountsByRoom.TryGetValue(roomKey, out var m) ? m + 1 : 1;
                    placedPoints.Add(c.Position);

                    // PC-13 — record placement on per-room state for downstream rules.
                    if (state != null)
                    {
                        if (!state.PlacedByRule.TryGetValue(rule.MergeKey, out var lst))
                        {
                            lst = new List<XYZ>();
                            state.PlacedByRule[rule.MergeKey] = lst;
                        }
                        lst.Add(c.Position);
                        state.LastPointByRule[rule.MergeKey] = c.Position;
                    }

                    // PC-17 — optional post-placement hook: data-tag pipeline + COBie seed.
                    try { PostPlacementHooks.RunFor(fi, rule); }
                    catch (Exception hkEx) { result.Warnings.Add($"PC-17 post-place hook for {fi.Id}: {hkEx.Message}"); }
                }
                catch (Exception ex)
                {
                    result.SkippedCount++;
                    result.Warnings.Add($"Place {rule.CategoryFilter} in {SafeRoomName(room)}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// PC-12 — compute how many candidates this rule should consume in this
        /// room. Point rules use MaxPerRoom; Density rules derive from area or
        /// occupancy; Linear rules from perimeter. MaxPerRoom (when > 0) is a
        /// hard cap regardless of kind.
        /// </summary>
        private static int ComputeCap(PlacementRule rule, Room room, int candidateCount, int alreadyInRoom)
        {
            int cap;
            switch (rule.RuleKind)
            {
                case PlacementRuleKind.Density:
                {
                    int byArea = 0, byOcc = 0;
                    if (rule.PerAreaM2 > 0)
                    {
                        double areaM2 = 0;
                        try { areaM2 = room.Area * 0.3048 * 0.3048; } catch { }
                        if (areaM2 > 0) byArea = Math.Max(1, (int)Math.Ceiling(areaM2 / rule.PerAreaM2));
                    }
                    if (rule.PerOccupant > 0)
                    {
                        int occ = 0;
                        try
                        {
                            var p = room.LookupParameter("STING_OCC_COUNT_INT");
                            if (p != null && p.HasValue && p.StorageType == StorageType.Integer) occ = p.AsInteger();
                        }
                        catch { }
                        if (occ > 0) byOcc = Math.Max(1, (int)Math.Ceiling((double)occ / rule.PerOccupant));
                    }
                    cap = Math.Max(byArea, byOcc);
                    // Phase 139.4 — Density rule with neither PerAreaM2 nor PerOccupant
                    // (or with both = 0) used to fall through with cap=1, then later
                    // collapse to candidateCount once MaxPerRoom = 0. Treat the rule
                    // as misconfigured: place at most one and warn upstream via the
                    // rule-loader validation pass (#39 below).
                    if (cap == 0) cap = 1;
                    break;
                }
                case PlacementRuleKind.Linear:
                {
                    // Phase 139.5 Q15 — Linear cap was always candidateCount
                    // regardless of PerLinearMetre. Now: perimeter (m) ÷
                    // PerLinearMetre = required count, taken from the room's
                    // boundary (or bbox fallback). When PerLinearMetre is 0,
                    // fall through to candidateCount as before.
                    if (rule.PerLinearMetre > 0)
                    {
                        double perimeterM = ComputeRoomPerimeterMetres(room);
                        if (perimeterM > 0)
                            cap = Math.Max(1, (int)Math.Ceiling(perimeterM / rule.PerLinearMetre));
                        else
                            cap = candidateCount;
                    }
                    else cap = candidateCount;
                    break;
                }
                default:
                    cap = rule.MaxPerRoom > 0
                        ? Math.Max(0, rule.MaxPerRoom - alreadyInRoom)
                        : candidateCount;
                    break;
            }

            // Hard cap from MaxPerRoom regardless of kind.
            if (rule.MaxPerRoom > 0)
                cap = Math.Min(cap, Math.Max(0, rule.MaxPerRoom - alreadyInRoom));
            return Math.Min(cap, candidateCount);
        }

        /// <summary>
        /// PC-13 — place a single instance of the supplied rule at an explicit
        /// point. Used by CoPlaceWith / RELATIVE_TO. Skips room-scope filters
        /// because the predecessor already validated the room.
        /// </summary>
        private static void ProcessRoomRuleAtPoint(
            Document doc,
            Room room,
            PlacementRule rule,
            PlacementScorer scorer,
            Dictionary<string, FamilySymbol> perCategorySymbol,
            PlacementResult result,
            bool dryRun,
            RoomState state,
            XYZ pt)
        {
            if (pt == null) return;
            string roomKey = $"{room.Id}::{SafeRoomName(room)}";
            // Apply this rule's offsets relative to the supplied point.
            const double mm = 1.0 / 304.8;
            XYZ at = new XYZ(pt.X + rule.OffsetXMm * mm,
                             pt.Y + rule.OffsetYMm * mm,
                             pt.Z + rule.OffsetZMm * mm);
            if (dryRun)
            {
                result.CountsByRule[rule.MergeKey] = result.CountsByRule.TryGetValue(rule.MergeKey, out var n) ? n + 1 : 1;
                result.CountsByRoom[roomKey] = result.CountsByRoom.TryGetValue(roomKey, out var m) ? m + 1 : 1;
                return;
            }
            var symbol = ResolveSymbol(doc, rule.CategoryFilter, rule, perCategorySymbol, result);
            if (symbol == null) return;
            if (!symbol.IsActive) { try { symbol.Activate(); doc.Regenerate(); } catch { return; } }
            try
            {
                var pf = PlacementHostPreflight.Place(doc, symbol, room, at, rule);
                if (pf.Skipped || pf.Placed == null)
                {
                    result.SkippedCount++;
                    if (!string.IsNullOrEmpty(pf.Reason)) result.Warnings.Add(pf.Reason);
                    return;
                }
                WriteAnchorParameters(pf.Placed, rule);
                OrientPlacedInstance(doc, pf.Placed, rule, room);
                if (StingTools.Commands.Placement.PlaceFixturesOptions.StampProvenance)
                {
                    try { StingTools.Core.Storage.StingProvenanceSchema.Stamp(pf.Placed, "FixturePlacementEngine.CoPlace", rule.MergeKey ?? ""); } catch { }
                }
                result.PlacedIds.Add(pf.Placed.Id);
                result.CountsByRule[rule.MergeKey] = result.CountsByRule.TryGetValue(rule.MergeKey, out var n) ? n + 1 : 1;
                result.CountsByRoom[roomKey] = result.CountsByRoom.TryGetValue(roomKey, out var m) ? m + 1 : 1;
                if (state != null)
                {
                    if (!state.PlacedByRule.TryGetValue(rule.MergeKey, out var lst)) { lst = new List<XYZ>(); state.PlacedByRule[rule.MergeKey] = lst; }
                    lst.Add(at);
                    state.LastPointByRule[rule.MergeKey] = at;
                }
                try { PostPlacementHooks.RunFor(pf.Placed, rule); } catch (Exception hkEx) { result.Warnings.Add($"PC-17 post-place hook (co): {hkEx.Message}"); }
            }
            catch (Exception ex)
            {
                result.SkippedCount++;
                result.Warnings.Add($"Co-place {rule.CategoryFilter} in {SafeRoomName(room)}: {ex.Message}");
            }
        }

        private static FamilySymbol ResolveSymbol(
            Document doc,
            string categoryName,
            PlacementRule rule,
            Dictionary<string, FamilySymbol> cache,
            PlacementResult result)
        {
            // PC-08 — VariantHint is a comma-separated fallback chain
            // (FLUSH,SURFACE,RECESSED) or a single regex (^IP6[5-7]$).
            // FamilyTypeRegex (optional) further refines symbol matching
            // by symbol name.
            string hint  = rule?.VariantHint ?? "";
            string ftrx  = rule?.FamilyTypeRegex ?? "";
            string cacheKey = string.IsNullOrEmpty(hint) && string.IsNullOrEmpty(ftrx)
                ? categoryName
                : $"{categoryName}|{hint}|{ftrx}";
            if (cache.TryGetValue(cacheKey, out var cached)) return cached;

            // Build matcher and ordered fallback chain.
            var chain = SplitVariantChain(hint);
            System.Text.RegularExpressions.Regex variantRx = null;
            if (chain.Count == 0 && IsRegexLike(hint))
            {
                try { variantRx = new System.Text.RegularExpressions.Regex(hint, System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
                catch (Exception ex) { result.Warnings.Add($"VariantHint regex '{hint}': {ex.Message}"); }
            }
            System.Text.RegularExpressions.Regex typeRx = null;
            if (!string.IsNullOrEmpty(ftrx))
            {
                try { typeRx = new System.Text.RegularExpressions.Regex(ftrx, System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
                catch (Exception ex) { result.Warnings.Add($"FamilyTypeRegex '{ftrx}': {ex.Message}"); }
            }

            FamilySymbol picked = null;
            FamilySymbol firstForCategory = null;
            // For chain-mode resolution, remember the best match per chain index so
            // an earlier chain entry always beats a later one.
            int bestChainIndex = int.MaxValue;
            try
            {
                // Phase 139.4 — apply OfCategory before OfClass so the
                // collector pre-filters by category index (Revit's native
                // index lookup) instead of walking every FamilySymbol.
                BuiltInCategory bic = BuiltInCategory.INVALID;
                try { bic = ResolveBuiltInCategoryByName(doc, categoryName); } catch { }
                FilteredElementCollector collector = (bic != BuiltInCategory.INVALID)
                    ? new FilteredElementCollector(doc).OfCategory(bic).OfClass(typeof(FamilySymbol))
                    : new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol));
                foreach (var el in collector)
                {
                    if (!(el is FamilySymbol fs)) continue;
                    if (fs.Category == null) continue;
                    if (!string.Equals(fs.Category.Name, categoryName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // FamilyTypeRegex is an additional gate, applied to symbol name.
                    if (typeRx != null && !typeRx.IsMatch(fs.Name ?? "")) continue;

                    if (firstForCategory == null) firstForCategory = fs;
                    string variant = fs.LookupParameter("STING_FIXTURE_VARIANT_TXT")?.AsString() ?? "";

                    if (chain.Count > 0)
                    {
                        for (int i = 0; i < chain.Count && i < bestChainIndex; i++)
                        {
                            if (string.Equals(variant, chain[i], StringComparison.OrdinalIgnoreCase))
                            {
                                picked = fs;
                                bestChainIndex = i;
                                if (i == 0) goto done;
                                break;
                            }
                        }
                    }
                    else if (variantRx != null)
                    {
                        if (variantRx.IsMatch(variant))
                        {
                            picked = fs;
                            goto done;
                        }
                    }
                    else if (!string.IsNullOrEmpty(hint))
                    {
                        if (string.Equals(variant, hint, StringComparison.OrdinalIgnoreCase))
                        {
                            picked = fs;
                            goto done;
                        }
                    }
                }
            done:
                if (picked == null) picked = firstForCategory;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Resolve symbol for '{categoryName}' (hint='{hint}'): {ex.Message}");
            }

            // PC-16 — auto-load missing families from the on-disk library so a
            // project that doesn't yet contain a sample of the rule's category
            // can still be served by the engine.
            if (picked == null && firstForCategory == null)
            {
                picked = TryAutoLoadFromLibrary(doc, categoryName, hint, result);
                firstForCategory = picked;
            }

            if (picked == null)
                result.Warnings.Add($"No FamilySymbol found for category '{categoryName}' — skipping its rules.");
            else if (!string.IsNullOrEmpty(hint) && firstForCategory != null && picked == firstForCategory && bestChainIndex == int.MaxValue && variantRx == null)
                result.Warnings.Add($"No FamilySymbol matching VariantHint='{hint}' in category '{categoryName}' — using first available symbol.");

            cache[cacheKey] = picked;
            return picked;
        }

        /// <summary>
        /// PC-16 — search the on-disk family library (resolved via
        /// StingToolsApp.AssemblyPath / Families/&lt;normalised category&gt;)
        /// for a .rfa whose top-level Family.OwnerFamily.FamilyCategory.Name
        /// matches the requested category. Loads the first match into the
        /// document and returns its first symbol. Caller already owns a
        /// Transaction (the engine's "STING v4 Place Fixtures" tx).
        /// </summary>
        private static FamilySymbol TryAutoLoadFromLibrary(Document doc, string categoryName, string hint, PlacementResult result)
        {
            try
            {
                string root = StingToolsApp.AssemblyPath;
                if (string.IsNullOrEmpty(root)) return null;
                // Look in Families/ + Families/<discipline>/ — discipline is unknown here, so glob across the tree.
                string famRoot = System.IO.Path.Combine(root, "Families");
                if (!System.IO.Directory.Exists(famRoot))
                    famRoot = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(root) ?? "", "Families");
                if (!System.IO.Directory.Exists(famRoot)) return null;

                // Normalise category for filename matching.
                string token = categoryName.Replace(" ", "").Replace("/", "").Replace("\\", "");
                var rfas = System.IO.Directory.EnumerateFiles(famRoot, "*.rfa", System.IO.SearchOption.AllDirectories)
                    .Where(p =>
                    {
                        string name = System.IO.Path.GetFileNameWithoutExtension(p) ?? "";
                        return name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0
                            || name.IndexOf(categoryName, StringComparison.OrdinalIgnoreCase) >= 0;
                    })
                    .ToList();
                if (rfas.Count == 0) return null;

                foreach (var path in rfas)
                {
                    try
                    {
                        Family loaded;
                        if (!doc.LoadFamily(path, out loaded) || loaded == null) continue;
                        FamilySymbol first = null;
                        foreach (var symId in loaded.GetFamilySymbolIds())
                        {
                            if (doc.GetElement(symId) is FamilySymbol fs)
                            {
                                if (fs.Category != null &&
                                    string.Equals(fs.Category.Name, categoryName, StringComparison.OrdinalIgnoreCase))
                                {
                                    first = fs;
                                    break;
                                }
                            }
                        }
                        if (first != null)
                        {
                            result.Warnings.Add($"PC-16 auto-loaded '{System.IO.Path.GetFileName(path)}' for category '{categoryName}'.");
                            return first;
                        }
                    }
                    catch (Exception lex) { StingLog.Warn($"PC-16 LoadFamily {path}: {lex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"PC-16 TryAutoLoadFromLibrary: {ex.Message}"); }
            return null;
        }

        /// <summary>PC-08 — split a comma/semicolon/pipe-separated variant
        /// fallback chain into trimmed entries. Returns empty when input
        /// is regex-like (^…$) or empty.</summary>
        private static List<string> SplitVariantChain(string hint)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(hint) || IsRegexLike(hint)) return list;
            foreach (var part in hint.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = part.Trim();
                if (!string.IsNullOrEmpty(t)) list.Add(t);
            }
            return list;
        }

        private static bool IsRegexLike(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            return s.StartsWith("^", StringComparison.Ordinal)
                || s.Contains("$")
                || s.Contains("\\d")
                || s.Contains("[");
        }

        private static void WriteAnchorParameters(FamilyInstance fi, PlacementRule rule)
        {
            TrySetString(fi, ParamRegistry.PLACE_ANCHOR, rule.AnchorType);
            TrySetDoubleMm(fi, ParamRegistry.PLACE_OFFSET_X_MM, rule.OffsetXMm);
            TrySetString(fi, ParamRegistry.PLACE_SIDE, rule.SideConstraint);

            // MNT_HGT_MM may be absent on some families; swallow failure.
            TrySetDoubleMm(fi, "MNT_HGT_MM", rule.MountingHeightMm);
        }

        /// <summary>
        /// Phase 139.6 SW-1 — orient a freshly-placed FamilyInstance.
        ///
        ///  1. Apply <c>rule.RotationDeg</c> about Z (in radians) — was a
        ///     no-op before; the field existed but was never honoured.
        ///  2. For wall-hosted families on a wall-anchored rule, ensure the
        ///     family's facing direction points INTO the room. If
        ///     <c>FacingFlipped</c> would put the front face inside the wall,
        ///     <c>flipFacing()</c> turns it around. This is what was making
        ///     switches face up / out of the door instead of into the room.
        /// </summary>
        private static void OrientPlacedInstance(Document doc, FamilyInstance fi, PlacementRule rule, Room room)
        {
            if (fi == null) return;
            try
            {
                // Step 1 — explicit rotation from rule.
                if (Math.Abs(rule.RotationDeg) > 0.001)
                {
                    XYZ origin = (fi.Location as LocationPoint)?.Point;
                    if (origin != null)
                    {
                        var axis = Line.CreateBound(origin, origin + XYZ.BasisZ);
                        ElementTransformUtils.RotateElement(doc, fi.Id, axis, rule.RotationDeg * Math.PI / 180.0);
                    }
                }

                // Step 2 — auto-flip wall-hosted families toward the room.
                string anchor = (rule.AnchorType ?? "").ToUpperInvariant();
                bool wallAnchor = anchor == "WALL_MIDPOINT" || anchor == "WALL_CORNER"
                               || anchor == "WALL_FACE_OFFSET"
                               || anchor == "DOOR_HINGE" || anchor == "DOOR_JAMB"
                               || anchor == "DOOR_HEAD"
                               || anchor == "DOOR_LATCH_SIDE"
                               || anchor == "DOOR_HINGE_SIDE_150"
                               || anchor == "DOOR_STRIKE_SIDE"
                               || anchor == "DOOR_CLOSER_ZONE"
                               || anchor == "WINDOW_SILL" || anchor == "WINDOW_HEAD";
                if (!wallAnchor) return;

                // Phase 139.18 — drop the `fi.Host is Wall` gate. Un-hosted
                // OneLevelBased families also need rotation: they default to
                // world-X facing and end up wrong on north-south walls.
                // For un-hosted families we resolve the wall via
                // FilteredElementCollector below; for hosted families we
                // keep using fi.Host directly.
                Wall hostWall = fi.Host as Wall;
                if (hostWall == null && fi.Location is LocationPoint lpForWall && lpForWall.Point != null)
                {
                    const double findRadiusFt = 600.0 / 304.8;
                    double bestSq = findRadiusFt * findRadiusFt;
                    var bbR = room?.get_BoundingBox(null);
                    if (bbR != null)
                    {
                        var pad = findRadiusFt + 0.5;
                        var outline = new Outline(
                            new XYZ(bbR.Min.X - pad, bbR.Min.Y - pad, bbR.Min.Z - pad),
                            new XYZ(bbR.Max.X + pad, bbR.Max.Y + pad, bbR.Max.Z + pad));
                        var bbf = new BoundingBoxIntersectsFilter(outline);
                        foreach (var el in new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_Walls)
                            .WhereElementIsNotElementType()
                            .WherePasses(bbf))
                        {
                            if (!(el is Wall w) || !(w.Location is LocationCurve lc2) || lc2.Curve == null) continue;
                            var proj = lc2.Curve.Project(lpForWall.Point);
                            if (proj == null) continue;
                            double d = proj.Distance;
                            if (d * d < bestSq) { bestSq = d * d; hostWall = w; }
                        }
                    }
                }
                if (hostWall == null) return;

                // The family's current facing vs the inward room normal at the
                // wall midpoint. Inward = wall.Orientation flipped if the room
                // sits on the other side of the wall.
                XYZ familyFacing = fi.FacingOrientation;
                if (familyFacing == null || familyFacing.IsZeroLength()) return;

                XYZ wallNormal = hostWall.Orientation;
                if (wallNormal == null || wallNormal.IsZeroLength()) return;

                // Determine which side of the wall the room is on.
                XYZ insertion = (fi.Location as LocationPoint)?.Point;
                XYZ probe = insertion + wallNormal.Multiply(0.5);
                bool roomOnPositiveNormal = false;
                try
                {
                    var bb = room?.get_BoundingBox(null);
                    if (bb != null)
                    {
                        roomOnPositiveNormal = probe.X >= bb.Min.X && probe.X <= bb.Max.X
                                            && probe.Y >= bb.Min.Y && probe.Y <= bb.Max.Y;
                    }
                    if (room != null && room.IsPointInRoom(probe)) roomOnPositiveNormal = true;
                }
                catch { }

                XYZ inward = roomOnPositiveNormal ? wallNormal : wallNormal.Negate();

                // If the family's facing vector and the inward normal point
                // away from each other (dot < 0), flip facing.
                if (familyFacing.DotProduct(inward) < -0.1)
                {
                    if (fi.CanFlipFacing)
                    {
                        fi.flipFacing();
                    }
                    else
                    {
                        // Phase 139.18 — un-hosted OneLevelBased families
                        // can't flipFacing(); rotate 180° about Z at the
                        // location point instead. Phase 139.23 — when the
                        // rotation would push the instance into another
                        // element Revit raises "Can't rotate element into
                        // this position". Catch + log; keep the instance
                        // un-rotated rather than aborting the placement.
                        XYZ origin = (fi.Location as LocationPoint)?.Point;
                        if (origin != null)
                        {
                            try
                            {
                                var axis = Line.CreateBound(origin, origin + XYZ.BasisZ);
                                ElementTransformUtils.RotateElement(doc, fi.Id, axis, Math.PI);
                            }
                            catch (Autodesk.Revit.Exceptions.InvalidOperationException ioEx) { StingLog.Warn($"OrientPlacedInstance rotate-180 (cannot rotate at {origin}): {ioEx.Message}"); }
                            catch (Exception rotEx) { StingLog.Warn($"OrientPlacedInstance rotate-180: {rotEx.Message}"); }
                        }
                    }
                }
                else if (Math.Abs(familyFacing.DotProduct(inward)) < 0.5
                         && fi.Host == null
                         && fi.Location is LocationPoint lpRot && lpRot.Point != null)
                {
                    // Phase 139.18 — un-hosted family is roughly perpendicular
                    // to the wall (e.g. a switch on a north-south wall facing
                    // east-west). Rotate so its facing aligns with the inward
                    // wall normal.
                    try
                    {
                        double currentAngle = Math.Atan2(familyFacing.Y, familyFacing.X);
                        double targetAngle  = Math.Atan2(inward.Y, inward.X);
                        double delta = targetAngle - currentAngle;
                        // Normalise to (-π, π]
                        while (delta >  Math.PI) delta -= 2 * Math.PI;
                        while (delta <= -Math.PI) delta += 2 * Math.PI;
                        if (Math.Abs(delta) > 0.05)
                        {
                            var axis = Line.CreateBound(lpRot.Point, lpRot.Point + XYZ.BasisZ);
                            ElementTransformUtils.RotateElement(doc, fi.Id, axis, delta);
                        }
                    }
                    catch (Exception rotEx) { StingLog.Warn($"OrientPlacedInstance align-to-wall: {rotEx.Message}"); }
                }

                // Phase 139.16 — snap the family onto the nearest wall's
                // room-side face. Wall anchors compute an XYZ on the wall
                // CENTERLINE; un-hosted families plonked at that point sit
                // visually inside the wall. Project the location onto the
                // wall's room-side face plane so the family appears flush
                // mounted. Skip when fi is already wall-hosted (Revit
                // already handles that). Tolerance: 600 mm — anything
                // further from a wall stays where it was placed.
                try
                {
                    if (fi.Host == null && fi.Location is LocationPoint lp && lp.Point != null)
                    {
                        const double snapRadiusFt = 600.0 / 304.8;
                        Wall best = null; double bestSq = snapRadiusFt * snapRadiusFt;
                        var bb = room?.get_BoundingBox(null);
                        if (bb != null)
                        {
                            var pad = snapRadiusFt + 0.5;
                            var outline = new Outline(
                                new XYZ(bb.Min.X - pad, bb.Min.Y - pad, bb.Min.Z - pad),
                                new XYZ(bb.Max.X + pad, bb.Max.Y + pad, bb.Max.Z + pad));
                            var bbf = new BoundingBoxIntersectsFilter(outline);
                            foreach (var el in new FilteredElementCollector(doc)
                                .OfCategory(BuiltInCategory.OST_Walls)
                                .WhereElementIsNotElementType()
                                .WherePasses(bbf))
                            {
                                if (!(el is Wall w) || !(w.Location is LocationCurve lc) || lc.Curve == null) continue;
                                var proj = lc.Curve.Project(lp.Point);
                                if (proj == null) continue;
                                double d = proj.Distance;
                                if (d * d < bestSq) { bestSq = d * d; best = w; }
                            }
                        }
                        if (best != null && best.Location is LocationCurve bestLc && bestLc.Curve != null)
                        {
                            var proj2 = bestLc.Curve.Project(lp.Point);
                            if (proj2 != null && proj2.XYZPoint != null)
                            {
                                XYZ snapped = new XYZ(proj2.XYZPoint.X, proj2.XYZPoint.Y, lp.Point.Z);
                                if (!snapped.IsAlmostEqualTo(lp.Point))
                                {
                                    var move = snapped - lp.Point;
                                    ElementTransformUtils.MoveElement(doc, fi.Id, move);
                                }
                            }
                        }
                    }
                }
                catch (Exception snapEx) { StingLog.Warn($"OrientPlacedInstance wall-snap {fi?.Id?.Value}: {snapEx.Message}"); }
            }
            catch (Exception ex) { StingLog.Warn($"OrientPlacedInstance {fi?.Id?.Value}: {ex.Message}"); }
        }

        private static void TrySetString(Element el, string paramName, string value)
        {
            if (string.IsNullOrEmpty(paramName) || value == null) return;
            try
            {
                var p = el.LookupParameter(paramName);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                    p.Set(value);
            }
            catch (Exception ex) { StingLog.Warn($"FixturePlacementEngine: set {paramName}={value} failed: {ex.Message}"); }
        }

        private static void TrySetDoubleMm(Element el, string paramName, double valueMm)
        {
            if (string.IsNullOrEmpty(paramName)) return;
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly) return;
                switch (p.StorageType)
                {
                    case StorageType.Double:  p.Set(valueMm * MmToFt); break;
                    case StorageType.String:  p.Set(valueMm.ToString("F1")); break;
                    case StorageType.Integer: p.Set((int)Math.Round(valueMm)); break;
                }
            }
            catch (Exception ex) { StingLog.Warn($"FixturePlacementEngine: set {paramName}={valueMm}mm failed: {ex.Message}"); }
        }

        // Phase 139.5 Q15 — perimeter from boundary segments (any curve type)
        // with a bbox fallback when the room has no boundary. Result in metres.
        private static double ComputeRoomPerimeterMetres(Room room)
        {
            try
            {
                var opts = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Center,
                };
                var loops = room.GetBoundarySegments(opts);
                double sumFt = 0;
                if (loops != null)
                {
                    foreach (var loop in loops)
                    {
                        foreach (var seg in loop)
                        {
                            var c = seg?.GetCurve();
                            if (c == null) continue;
                            sumFt += c.Length;
                        }
                    }
                }
                if (sumFt > 0) return sumFt * 0.3048;
                var bb = room.get_BoundingBox(null);
                if (bb == null) return 0;
                double w = (bb.Max.X - bb.Min.X) * 0.3048;
                double d = (bb.Max.Y - bb.Min.Y) * 0.3048;
                return 2 * (w + d);
            }
            catch (Exception ex) { StingLog.Warn($"ComputeRoomPerimeterMetres: {ex.Message}"); return 0; }
        }

        private static string SafeRoomName(Room room)
        {
            try
            {
                var p = room.get_Parameter(BuiltInParameter.ROOM_NAME);
                return p?.AsString() ?? room.Name ?? "";
            }
            catch { return ""; }
        }

        // Phase 139.4 — resolve a Document.Settings.Categories entry to its
        // BuiltInCategory enum so FilteredElementCollector.OfCategory can
        // pre-filter family symbols. Cached per document on first hit.
        private static readonly Dictionary<int, Dictionary<string, BuiltInCategory>> _bicByName
            = new Dictionary<int, Dictionary<string, BuiltInCategory>>();
        private static readonly object _bicByNameLock = new object();

        private static BuiltInCategory ResolveBuiltInCategoryByName(Document doc, string categoryName)
        {
            if (doc == null || string.IsNullOrEmpty(categoryName)) return BuiltInCategory.INVALID;
            int key = doc.GetHashCode();
            Dictionary<string, BuiltInCategory> map;
            lock (_bicByNameLock)
            {
                if (!_bicByName.TryGetValue(key, out map))
                {
                    map = new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        foreach (Category c in doc.Settings.Categories)
                        {
                            if (c == null || string.IsNullOrEmpty(c.Name)) continue;
                            try
                            {
                                var bic = (BuiltInCategory)c.Id.Value;
                                if (bic != BuiltInCategory.INVALID)
                                    map[c.Name] = bic;
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"ResolveBuiltInCategoryByName: {ex.Message}"); }
                    _bicByName[key] = map;
                }
            }
            return map.TryGetValue(categoryName, out var hit) ? hit : BuiltInCategory.INVALID;
        }
    }
}
