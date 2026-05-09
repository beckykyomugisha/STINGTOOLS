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

        // Phase 139.27 (I-03) — per-rule diagnostics. The Centre's result
        // panel now shows a row per rule answering: did it fire? how many
        // candidates? how many were rejected and why? Without this users
        // who tick "Place Electrical Fixtures" and get zero placements
        // can't tell whether the rule was filtered out by a room name,
        // rejected on score, or simply had no matching family symbol.
        public Dictionary<string, RuleDiagnostic> Diagnostics { get; }
            = new Dictionary<string, RuleDiagnostic>(StringComparer.OrdinalIgnoreCase);

        internal RuleDiagnostic Diag(string mergeKey)
        {
            if (string.IsNullOrEmpty(mergeKey)) return null;
            if (!Diagnostics.TryGetValue(mergeKey, out var d))
            {
                d = new RuleDiagnostic { MergeKey = mergeKey };
                Diagnostics[mergeKey] = d;
            }
            return d;
        }
    }

    /// <summary>
    /// Phase 139.27 (I-03) — one row of per-rule placement diagnostics.
    /// Filled by FixturePlacementEngine + ProcessRoomRule, surfaced by
    /// PlaceFixturesCommand.ShowResult.
    /// </summary>
    public class RuleDiagnostic
    {
        public string MergeKey { get; set; } = "";
        public int RoomsConsidered { get; set; }
        public int RoomsFilteredByName { get; set; }
        public int RoomsFilteredByExclude { get; set; }
        public int RoomsBlockedByConflict { get; set; }
        public int RoomsBlockedByDependsOn { get; set; }
        public int CandidatesGenerated { get; set; }
        public int CandidatesRejectedDedup { get; set; }
        public int CandidatesPlaced { get; set; }
        public int SkippedNoSymbol { get; set; }
        public int SkippedHostPreflight { get; set; }
        public int ManufacturerMisses { get; set; }
        public string FirstSkipReason { get; set; } = "";

        public string OneLineSummary()
        {
            return $"{MergeKey}: rooms={RoomsConsidered}/-{RoomsFilteredByName}/-{RoomsFilteredByExclude} " +
                   $"cand={CandidatesGenerated} placed={CandidatesPlaced} " +
                   $"skip(host={SkippedHostPreflight}, sym={SkippedNoSymbol}, dedup={CandidatesRejectedDedup}, " +
                   $"conflict={RoomsBlockedByConflict}, dep={RoomsBlockedByDependsOn}, mfr={ManufacturerMisses})" +
                   (string.IsNullOrEmpty(FirstSkipReason) ? "" : $" • first: {FirstSkipReason}");
        }
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
        public const string PhaseTag = "Phase 139.26";

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

            // Phase 139.27 (M-05) — surface validation warnings up front.
            // PlacementRuleLoader.LastValidationWarnings is populated by
            // every Load/MergeRules call; without this loop they only
            // landed in the .log file.
            try
            {
                var vw = PlacementRuleLoader.LastValidationWarnings;
                if (vw != null)
                    foreach (var w in vw)
                        if (!string.IsNullOrEmpty(w)) result.Warnings.Add(w);
            }
            catch { }

            // Phase 139.27 (N-05) — accessibility / mounting-height validation
            // against STING_HEIGHT_STANDARDS.json. Silent until 139.27.
            try
            {
                var hsw = HeightStandardsTable.ValidateRulesAgainstStandards(rules);
                if (hsw != null)
                    foreach (var w in hsw)
                        if (!string.IsNullOrEmpty(w)) result.Warnings.Add(w);
            }
            catch (Exception hex) { StingLog.Warn($"HeightStandards validation: {hex.Message}"); }

            if (rules.Count == 0)
            {
                result.Warnings.Add("No placement rules found. Ship STING_PLACEMENT_RULES.json or provide a project override.");
                return result;
            }

            // Phase 139.27 (C-03) — surface a one-shot warning at start
            // of run when the user enabled "Tag every placement" but
            // the reflection target is missing. RunFor() warns once per
            // session via StingLog; mirroring it into result.Warnings
            // means the run report shows it too.
            if (PostPlacementHooks.RunDataTagPipeline && PostPlacementHooks.TagPipelineMissing)
                result.Warnings.Add("Post-placement: 'Tag every placement' is on but TagPipelineHelper.RunFullPipeline could not be resolved by reflection — placed instances will NOT be tagged this session.");

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
                // Phase 139.25 — install IFailuresPreprocessor so the
                // engine's predictable failures (cannot-rotate, identical
                // instances, origin-not-on-face) are dismissed in-process
                // instead of bubbling to a modal Revit dialog that blocks
                // the commit. Without this the transaction reports
                // "31 placed" but zero appear in the model.
                try
                {
                    var failOpts = tx.GetFailureHandlingOptions();
                    failOpts.SetFailuresPreprocessor(new PlacementFailuresPreprocessor());
                    failOpts.SetClearAfterRollback(true);
                    failOpts.SetForcedModalHandling(false);
                    tx.SetFailureHandlingOptions(failOpts);
                }
                catch (Exception fEx) { StingLog.Warn($"FailuresPreprocessor wire: {fEx.Message}"); }
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
                        var diag = result.Diag(rule.MergeKey);
                        if (diag != null) diag.RoomsConsidered++;

                        // Phase 139.5 Q21 — fast filter using pre-compiled regex
                        // before paying the cost of scorer.Score / RoomMatchesScope
                        // (which reads parameters, level, phase, workset).
                        if (roomFilterRx.TryGetValue(rule.MergeKey, out var rfx)
                            && !rfx.IsMatch(roomName ?? ""))
                        {
                            if (diag != null) diag.RoomsFilteredByName++;
                            continue;
                        }
                        if (excludeFilterRx.TryGetValue(rule.MergeKey, out var efx)
                            && efx.IsMatch(roomName ?? ""))
                        {
                            if (diag != null) diag.RoomsFilteredByExclude++;
                            continue;
                        }

                        try
                        {
                            // PC-13 — ConflictsWith: skip if any conflicting rule already fired in this room.
                            if (RuleHasConflict(rule, roomState))
                            {
                                result.Warnings.Add($"Room {room.Id} / {rule.MergeKey}: skipped — ConflictsWith already fired.");
                                if (diag != null) diag.RoomsBlockedByConflict++;
                                continue;
                            }
                            // PC-13 — DependsOn: skip if predecessor produced no placements yet.
                            if (!string.IsNullOrEmpty(rule.DependsOn) && !roomState.PlacedByRule.ContainsKey(rule.DependsOn))
                            {
                                result.Warnings.Add($"Room {room.Id} / {rule.MergeKey}: skipped — DependsOn '{rule.DependsOn}' has no placement in this room.");
                                if (diag != null) diag.RoomsBlockedByDependsOn++;
                                continue;
                            }

                            ProcessRoomRule(doc, room, rule, scorer,
                                perCategorySymbol, result, dryRun, roomState);

                            // PC-13 / Phase 139.27 (M-06) — CoPlaceWith fires at
                            // EVERY placement point of the primary rule, not just
                            // the last. Pre-139.27 only the last point of a 5-fixture
                            // primary triggered the co-rule; the other 4 silently
                            // missed their dependent device (smoke detector under
                            // luminaire, double-pole isolator next to fan, etc.).
                            if (rule.CoPlaceWith != null && rule.CoPlaceWith.Count > 0
                                && roomState.PlacedByRule.TryGetValue(rule.MergeKey, out var primaryPoints)
                                && primaryPoints != null && primaryPoints.Count > 0)
                            {
                                foreach (var coId in rule.CoPlaceWith)
                                {
                                    var coRule = ordered.FirstOrDefault(r => string.Equals(r.MergeKey, coId, StringComparison.OrdinalIgnoreCase));
                                    if (coRule == null) continue;
                                    foreach (var primaryPt in primaryPoints)
                                    {
                                        try
                                        {
                                            ProcessRoomRuleAtPoint(doc, room, coRule, scorer, perCategorySymbol, result, dryRun, roomState, primaryPt);
                                        }
                                        catch (Exception cex)
                                        {
                                            result.Warnings.Add($"Co-place {coRule.MergeKey} in room {room.Id}: {cex.Message}");
                                        }
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

                // Phase 139.27 (M-02 partial) — stamp STING_NOGGIN_REQUIRED on
                // every placed instance whose XY matches a NogginRequiredPoint
                // from the scorer's lighting-grid cache. Pre-139.27 these
                // points were computed but discarded; the export command
                // (NogginRequirementExportCommand) had no parameter to read.
                if (!dryRun)
                {
                    try
                    {
                        StampNogginRequirementsFromGrid(doc, scorer, result);
                    }
                    catch (Exception nx) { result.Warnings.Add($"Noggin stamp: {nx.Message}"); }

                    // Phase 139.27 (M-02 deep) — structural-awareness audit.
                    // Walk each placed instance and warn when the location
                    // sits inside a beam-junction or column clearance zone
                    // — drilling there would breach structure. Currently
                    // advisory-only (no auto-relocation); the warning lands
                    // in PlacementResult.Warnings so the BIM coordinator
                    // can resolve before issuing.
                    try
                    {
                        AuditStructuralClearance(doc, ordered, result);
                    }
                    catch (Exception sx) { result.Warnings.Add($"Structural audit: {sx.Message}"); }

                    // Phase 139.27 (X-03) — best-effort MEP connector auto-join.
                    // Walks placed FamilyInstances with MEP connectors and
                    // tries to connect each open connector to the closest
                    // unconnected connector on a neighbouring placed
                    // instance whose system classification matches. Skipped
                    // when no rule sets RouteSegmentCategory (i.e. not a
                    // routing rule). All exceptions captured per instance.
                    try
                    {
                        AutoJoinMepConnectors(doc, ordered, result);
                    }
                    catch (Exception mx) { result.Warnings.Add($"MEP connector join: {mx.Message}"); }

                    // Phase 139.27 (X-04) — cable / conduit bundle advisory.
                    // For rules with CableBundleAdvisoryCount > 0, count how
                    // many same-category instances landed within bundle
                    // clearance (300 mm default) and warn when the count
                    // exceeds the BS 7671 Table 4 derating thresholds
                    // (no auto-resize; advisory only).
                    try
                    {
                        AuditCableBundleDerating(doc, ordered, result);
                    }
                    catch (Exception cx) { result.Warnings.Add($"Cable bundle audit: {cx.Message}"); }

                    // Phase 139.30 (X-05) — door swing envelope vs switch
                    // placement. Switches anchored at DOOR_HINGE_SIDE_*
                    // can sit inside the door's swept arc; opening the
                    // door hits the switch with the handle. Walks every
                    // placed switch / electrical fixture and checks
                    // against every door's swept envelope.
                    try
                    {
                        AuditDoorSwingClearance(doc, ordered, result);
                    }
                    catch (Exception dx) { result.Warnings.Add($"Door swing audit: {dx.Message}"); }

                    // Phase 139.30 (X-01) — circuit-aware electrical pass.
                    // Groups placed sockets / electrical fixtures into
                    // BS 7671 ring / radial circuits and stamps RCD
                    // group + circuit ID per element.
                    try
                    {
                        StingTools.Core.Calc.CircuitTopologyEngine.AssignCircuits(doc, result);
                    }
                    catch (Exception ex) { result.Warnings.Add($"Circuit topology: {ex.Message}"); }
                }

                if (!dryRun)
                {
                    tx.Commit();

                    // Phase 139.26 — POST-COMMIT VERIFICATION.
                    // The IFailuresPreprocessor catches predictable
                    // warnings, but Revit can still silently roll back
                    // individual placements during commit (e.g. failed
                    // host association on regen).  Walk every PlacedId
                    // and confirm doc.GetElement returns a live element.
                    // Drop ghosts from PlacedIds and emit a per-category
                    // audit so the user can verify what's actually in
                    // the model versus what the engine THINKS it placed.
                    try
                    {
                        var alive = new List<ElementId>();
                        var rolledBack = 0;
                        var perCategoryAlive = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        foreach (var id in result.PlacedIds)
                        {
                            Element el = null;
                            try { el = doc.GetElement(id); } catch { }
                            if (el == null) { rolledBack++; continue; }
                            alive.Add(id);
                            string cat = "(uncategorised)";
                            try { cat = el.Category?.Name ?? "(uncategorised)"; } catch { }
                            perCategoryAlive[cat] = perCategoryAlive.TryGetValue(cat, out var n) ? n + 1 : 1;
                        }
                        if (rolledBack > 0)
                        {
                            result.Warnings.Add(
                                $"POST-COMMIT VERIFICATION: {rolledBack} of {result.PlacedIds.Count} placements " +
                                $"were silently rolled back by Revit during commit. The engine reported them " +
                                $"as placed but they are NOT in the model. Add their failure description to " +
                                $"PlacementFailuresPreprocessor.SuppressGuids if this recurs.");
                            StingLog.Warn($"FixturePlacementEngine: {rolledBack}/{result.PlacedIds.Count} placements rolled back post-commit.");
                        }
                        result.PlacedIds.Clear();
                        result.PlacedIds.AddRange(alive);
                        // Audit summary so the user can compare what categories
                        // they ticked vs. what categories actually landed.
                        if (perCategoryAlive.Count > 0)
                        {
                            string audit = string.Join(", ",
                                perCategoryAlive.OrderByDescending(kv => kv.Value)
                                                .Select(kv => $"{kv.Key}={kv.Value}"));
                            StingLog.Info($"FixturePlacementEngine: post-commit category audit — {audit}");
                            result.Warnings.Add($"POST-COMMIT category audit: {audit}");
                        }
                    }
                    catch (Exception vex)
                    {
                        StingLog.Warn($"FixturePlacementEngine post-commit verify: {vex.Message}");
                    }
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
            var diagRoom = result.Diag(rule.MergeKey);
            if (diagRoom != null) diagRoom.CandidatesGenerated += candidates.Count;
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
                    if (diagRoom != null) diagRoom.CandidatesPlaced++;
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

                // Phase 139.27 (I-01) — dry-run preview of post-placement
                // hooks. The hooks themselves cannot run in dry-run (no
                // FamilyInstance to operate on), but the user needs to know
                // BEFORE they commit whether tagging / COBie seed / MEP
                // join are configured and resolvable. Surfaced only once
                // per rule per session, gated by toggles.
                if (PostPlacementHooks.RunDataTagPipeline && PostPlacementHooks.TagPipelineMissing)
                {
                    string warnKey = $"DryRun:{rule.MergeKey}:tagging-missing";
                    if (!result.Warnings.Any(w => w.Contains(warnKey)))
                        result.Warnings.Add($"{warnKey} — would-be-tagged in live run, but TagPipelineHelper not resolvable; tagging will silently no-op.");
                }
                return;
            }

            FamilySymbol symbol = ResolveSymbol(doc, rule.CategoryFilter, rule, perCategorySymbol, result);
            if (symbol == null)
            {
                if (diagRoom != null)
                {
                    diagRoom.SkippedNoSymbol++;
                    if (string.IsNullOrEmpty(diagRoom.FirstSkipReason))
                        diagRoom.FirstSkipReason = $"No FamilySymbol for category '{rule.CategoryFilter}'";
                }
                return;
            }

            // Phase 139.27 (M-07) — manufacturer-miss penalty surfaced as a
            // diagnostic count. PlacementScorer's catalogue scoring is the
            // proper home for the 0.5 penalty (see PlacementScorer.cs); here
            // we just record that a rule referenced a catalogue entry the
            // registry didn't resolve. Surfaced in the result panel so users
            // know the placement landed without manufacturer-side validation.
            try
            {
                if (!string.IsNullOrEmpty(rule.CatalogueRef) || !string.IsNullOrEmpty(rule.ManufacturerCode))
                {
                    bool resolved = false;
                    try { resolved = ManufacturerCatalogueRegistry.GetForRule(rule) != null; } catch { }
                    if (!resolved && diagRoom != null) diagRoom.ManufacturerMisses++;
                }
            }
            catch { }

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
                    if (diagRoom != null) diagRoom.CandidatesRejectedDedup++;
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
                        if (diagRoom != null)
                        {
                            diagRoom.SkippedHostPreflight++;
                            if (string.IsNullOrEmpty(diagRoom.FirstSkipReason))
                                diagRoom.FirstSkipReason = pf.Reason ?? "host preflight skipped";
                        }
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
                    if (diagRoom != null) diagRoom.CandidatesPlaced++;
                    placedPoints.Add(c.Position);
                    existingNearby.Add(c.Position); // Phase 139.25 — live dedup

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
                    int byArea = 0, byOcc = 0, byBed = 0, byCubicle = 0, byPupil = 0, byWs = 0;
                    if (rule.PerAreaM2 > 0)
                    {
                        double areaM2 = 0;
                        try { areaM2 = room.Area * 0.3048 * 0.3048; } catch { }
                        if (areaM2 > 0) byArea = Math.Max(1, (int)Math.Ceiling(areaM2 / rule.PerAreaM2));
                    }
                    if (rule.PerOccupant > 0)
                    {
                        int occ = ReadRoomIntParam(room,
                            string.IsNullOrWhiteSpace(rule.OccupancyParamName) ? "STING_OCC_COUNT_INT" : rule.OccupancyParamName);
                        if (occ > 0) byOcc = Math.Max(1, (int)Math.Ceiling((double)occ / rule.PerOccupant));
                    }
                    // Phase 178a hardening — PerBed / PerToiletCubicle / PerPupil
                    // / PerWorkstation were declared on PlacementRule but ignored
                    // here, so healthcare/sanitary/education/office density rules
                    // fell through to cap=1.
                    if (rule.PerBed > 0)
                    {
                        int beds = ReadRoomIntParam(room, "STING_BED_COUNT_INT");
                        if (beds > 0) byBed = Math.Max(1, (int)Math.Ceiling((double)beds / rule.PerBed));
                    }
                    if (rule.PerToiletCubicle > 0)
                    {
                        int cubicles = ReadRoomIntParam(room, "STING_CUBICLE_COUNT_INT");
                        if (cubicles > 0) byCubicle = Math.Max(1, (int)Math.Ceiling((double)cubicles / rule.PerToiletCubicle));
                    }
                    if (rule.PerPupil > 0)
                    {
                        int pupils = ReadRoomIntParam(room, "STING_PUPIL_COUNT_INT");
                        if (pupils > 0) byPupil = Math.Max(1, (int)Math.Ceiling((double)pupils / rule.PerPupil));
                    }
                    if (rule.PerWorkstation > 0)
                    {
                        int ws = ReadRoomIntParam(room, "STING_WORKSTATION_COUNT_INT");
                        if (ws > 0) byWs = Math.Max(1, (int)Math.Ceiling((double)ws / rule.PerWorkstation));
                    }
                    cap = Math.Max(Math.Max(byArea, byOcc),
                          Math.Max(Math.Max(byBed, byCubicle), Math.Max(byPupil, byWs)));
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

        private static int ReadRoomIntParam(Room room, string paramName)
        {
            if (room == null || string.IsNullOrWhiteSpace(paramName)) return 0;
            try
            {
                var p = room.LookupParameter(paramName);
                if (p == null || !p.HasValue) return 0;
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                if (p.StorageType == StorageType.Double)  return (int)Math.Round(p.AsDouble());
                if (p.StorageType == StorageType.String)
                {
                    var s = p.AsString();
                    if (int.TryParse(s, out int v)) return v;
                }
            }
            catch { }
            return 0;
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
        // Phase 139.27 (N-03) — key by PathName|Title not GetHashCode(),
        // same hash-collision concern as PlacementHostPreflight.ResolveView3D.
        private static readonly Dictionary<string, Dictionary<string, BuiltInCategory>> _bicByName
            = new Dictionary<string, Dictionary<string, BuiltInCategory>>(StringComparer.Ordinal);
        private static readonly object _bicByNameLock = new object();

        private static BuiltInCategory ResolveBuiltInCategoryByName(Document doc, string categoryName)
        {
            if (doc == null || string.IsNullOrEmpty(categoryName)) return BuiltInCategory.INVALID;
            string path = "", title = "";
            try { path = doc.PathName ?? ""; } catch { }
            try { title = doc.Title ?? ""; } catch { }
            string key = path + "|" + title;
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

        /// <summary>
        /// Phase 139.27 (M-02 partial) — flow noggin-required points from
        /// the lighting-grid cache onto placed instances, so structural
        /// coordination can read them via NogginRequirementExportCommand.
        ///
        /// Match radius: 200mm XY. The scorer's grid points are tile-snapped,
        /// so a placed instance shouldn't be more than half a tile (300mm)
        /// from its source point — 200mm is a safety buffer.
        /// </summary>
        private static void StampNogginRequirementsFromGrid(
            Document doc, PlacementScorer scorer, PlacementResult result)
        {
            if (doc == null || scorer == null || result == null) return;
            var grids = scorer.GridResults;
            if (grids == null || grids.Count == 0) return;

            const double matchRadiusFt = 200.0 / 304.8;
            double matchSq = matchRadiusFt * matchRadiusFt;
            int stamped = 0;

            // Collect every noggin point across all (room, rule) pairs.
            var noggins = new List<XYZ>();
            foreach (var kv in grids)
            {
                if (kv.Value?.NogginRequiredPoints == null) continue;
                foreach (var p in kv.Value.NogginRequiredPoints)
                {
                    if (p != null) noggins.Add(p);
                    result.NogginRequiredPoints.Add(p);
                }
            }
            if (noggins.Count == 0) return;

            foreach (var id in result.PlacedIds)
            {
                FamilyInstance fi = null;
                try { fi = doc.GetElement(id) as FamilyInstance; } catch { }
                if (fi == null) continue;
                XYZ p = (fi.Location as LocationPoint)?.Point;
                if (p == null) continue;

                bool nearNoggin = false;
                foreach (var n in noggins)
                {
                    double dx = n.X - p.X, dy = n.Y - p.Y;
                    if (dx * dx + dy * dy <= matchSq) { nearNoggin = true; break; }
                }
                if (!nearNoggin) continue;

                try
                {
                    var pNog = fi.LookupParameter(ParamRegistry.NOGGIN_REQUIRED);
                    if (pNog == null || pNog.IsReadOnly) continue;
                    if (pNog.StorageType == StorageType.Integer && pNog.AsInteger() != 1)
                    {
                        pNog.Set(1);
                        stamped++;
                    }
                    else if (pNog.StorageType == StorageType.Double && Math.Abs(pNog.AsDouble() - 1) > 1e-6)
                    {
                        pNog.Set(1.0);
                        stamped++;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"StampNogginRequirements {id.Value}: {ex.Message}"); }
            }

            if (stamped > 0)
            {
                result.Warnings.Add($"Stamped STING_NOGGIN_REQUIRED on {stamped} placed fixture(s) — run Placement_NogginExport to dump the structural fixing schedule.");
                StingLog.Info($"FixturePlacementEngine: stamped STING_NOGGIN_REQUIRED on {stamped} fixture(s).");
            }
        }

        /// <summary>
        /// Phase 139.27 (M-02 deep) — post-placement structural-clearance
        /// audit. Each placed instance is checked against
        /// <see cref="StructuralAwareness.IsNearJunction"/> with a 300mm
        /// clearance; matches surface as warnings so the coordinator can
        /// review before drilling. Cheap because StructuralAwareness
        /// pre-builds the junction set once per document.
        /// </summary>
        private static void AuditStructuralClearance(
            Document doc, IList<PlacementRule> rules, PlacementResult result)
        {
            if (doc == null || result == null || result.PlacedIds.Count == 0) return;
            // Only audit rules whose anchor implies a structural penetration
            // (ceiling-mounted, soffit-mounted, wall-deep). Surface-mounted
            // wall fixtures rarely conflict with structure.
            var auditRuleKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (rules != null)
            {
                foreach (var r in rules)
                {
                    if (r == null) continue;
                    string anchor = (r.AnchorType ?? "").ToUpperInvariant();
                    bool ceilingAnchor = anchor == "CEILING_CENTRE" || anchor == "CEILING_TILE_CENTRE"
                                       || anchor == "LIGHTING_GRID" || anchor == "LUX_GRID";
                    bool soffitAnchor = anchor == "SOFFIT_CENTRE" || anchor == "SOFFIT_BESA"
                                       || anchor == "STRUCTURAL_SOFFIT";
                    bool deepWallAnchor = (anchor == "WALL_FACE_OFFSET" && r.OffsetXMm > 50.0);
                    if (ceilingAnchor || soffitAnchor || deepWallAnchor)
                        auditRuleKeys.Add(r.MergeKey);
                }
            }
            if (auditRuleKeys.Count == 0) return;

            StructuralAwareness sa;
            try { sa = new StructuralAwareness(doc); }
            catch (Exception ex)
            {
                StingLog.Warn($"AuditStructuralClearance init: {ex.Message}");
                return;
            }

            const double clearanceFt = 300.0 / 304.8;
            int conflicts = 0;
            foreach (var id in result.PlacedIds)
            {
                FamilyInstance fi = null;
                try { fi = doc.GetElement(id) as FamilyInstance; } catch { }
                if (fi == null) continue;
                XYZ p = (fi.Location as LocationPoint)?.Point;
                if (p == null) continue;
                bool nearStructure = false;
                try { nearStructure = sa.IsNearJunction(p, clearanceFt); } catch { }
                if (!nearStructure) continue;
                conflicts++;
                if (conflicts <= 10)
                    result.Warnings.Add(
                        $"Structural clearance: placed instance {id.Value} at ({p.X:F2},{p.Y:F2}) " +
                        $"sits within 300mm of a beam-junction or column. Drilling may breach structure — " +
                        $"verify with the structural BIM coordinator before issuing.");
            }
            if (conflicts > 10)
                result.Warnings.Add($"Structural clearance: {conflicts - 10} additional conflicts truncated — see StingLog.");
            if (conflicts > 0)
                StingLog.Warn($"FixturePlacementEngine.AuditStructuralClearance: {conflicts} placed instance(s) within 300mm of structure.");
        }

        /// <summary>
        /// Phase 139.27 (X-03) — best-effort MEP connector auto-join.
        /// For each placed FamilyInstance with MEP connectors:
        ///   1. enumerate unconnected connectors;
        ///   2. find the closest unconnected connector on another placed
        ///      instance of the same SystemClassification within 600mm;
        ///   3. attempt a direct connection (no fitting insertion).
        /// Failures are tallied and surfaced as a single advisory rather
        /// than per-instance noise. Pre-139.27 placed instances had zero
        /// connector joins and shipped to COBie / schedules as orphaned.
        /// </summary>
        private static void AutoJoinMepConnectors(
            Document doc, IList<PlacementRule> rules, PlacementResult result)
        {
            if (doc == null || result == null || result.PlacedIds.Count == 0) return;
            // Only run when at least one rule declares a routing target.
            bool anyRouting = false;
            if (rules != null)
            {
                foreach (var r in rules)
                {
                    if (r == null) continue;
                    if (!string.IsNullOrEmpty(r.RoutingMode)
                        && !string.Equals(r.RoutingMode, "NONE", StringComparison.OrdinalIgnoreCase))
                    { anyRouting = true; break; }
                }
            }
            if (!anyRouting) return;

            const double joinRadiusFt = 600.0 / 304.8;
            double radSq = joinRadiusFt * joinRadiusFt;

            // Index connectors per system classification.
            var openConns = new Dictionary<string, List<(Connector c, ElementId id)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in result.PlacedIds)
            {
                FamilyInstance fi = null;
                try { fi = doc.GetElement(id) as FamilyInstance; } catch { }
                if (fi == null) continue;
                var mgr = fi.MEPModel?.ConnectorManager;
                if (mgr == null) continue;
                foreach (Connector c in mgr.Connectors)
                {
                    if (c == null || c.IsConnected) continue;
                    string sysKey = "?";
                    try { sysKey = c.Domain.ToString() + ":" + (c.MEPSystem?.Name ?? c.Description ?? c.Owner?.Category?.Name ?? ""); } catch { }
                    if (!openConns.TryGetValue(sysKey, out var list))
                    {
                        list = new List<(Connector, ElementId)>();
                        openConns[sysKey] = list;
                    }
                    list.Add((c, id));
                }
            }

            int joined = 0, failed = 0;
            int fittingsElbow = 0, fittingsUnion = 0, fittingsTransition = 0, fittingsTee = 0, directJoins = 0;
            foreach (var kv in openConns)
            {
                var list = kv.Value;
                for (int i = 0; i < list.Count; i++)
                {
                    var (a, aid) = list[i];
                    if (a.IsConnected) continue;
                    XYZ pa = a.Origin;
                    if (pa == null) continue;
                    int bestJ = -1; double bestSq = radSq;
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        var (b, bid) = list[j];
                        if (b == null || b.IsConnected || bid == aid) continue;
                        XYZ pb = b.Origin;
                        if (pb == null) continue;
                        double dx = pa.X - pb.X, dy = pa.Y - pb.Y, dz = pa.Z - pb.Z;
                        double sq = dx * dx + dy * dy + dz * dz;
                        if (sq < bestSq) { bestSq = sq; bestJ = j; }
                    }
                    if (bestJ < 0) continue;
                    var (target, tid) = list[bestJ];
                    try
                    {
                        if (a.IsConnectedTo(target)) continue;
                        // Phase 139.29 — prefer fitting insertion over direct
                        // ConnectTo. Geometry decides the fitting kind:
                        //  · same axis, same size  → Union fitting
                        //  · same axis, ≠ size     → Transition fitting
                        //  · different axis        → Elbow / Tee fitting
                        // Falls through to ConnectTo when no fitting matches
                        // (rare — happens with cable-tray which Revit doesn't
                        // ship a Union for).
                        bool insertedFitting = TryInsertFitting(doc, a, target, out int kind);
                        if (insertedFitting)
                        {
                            joined++;
                            switch (kind)
                            {
                                case 1: fittingsElbow++;       break;
                                case 2: fittingsUnion++;       break;
                                case 3: fittingsTransition++;  break;
                                case 4: fittingsTee++;         break;
                            }
                        }
                        else
                        {
                            a.ConnectTo(target);
                            if (a.IsConnected) { joined++; directJoins++; } else failed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        StingLog.Warn($"AutoJoinMepConnectors {aid.Value}->{tid.Value}: {ex.Message}");
                    }
                }
            }

            if (joined > 0)
            {
                var detail = new List<string>();
                if (fittingsElbow      > 0) detail.Add($"{fittingsElbow} elbow");
                if (fittingsUnion      > 0) detail.Add($"{fittingsUnion} union");
                if (fittingsTransition > 0) detail.Add($"{fittingsTransition} transition");
                if (fittingsTee        > 0) detail.Add($"{fittingsTee} tee");
                if (directJoins        > 0) detail.Add($"{directJoins} direct");
                string detailTxt = detail.Count > 0 ? " (" + string.Join(", ", detail) + ")" : "";
                result.Warnings.Add($"MEP auto-join: connected {joined} pair(s){detailTxt}. Verify fitting orientation in Revit before issue.");
            }
            if (failed > 0)
                result.Warnings.Add($"MEP auto-join: {failed} connector pair(s) within 600mm could not be joined (mismatched flow direction, incompatible fitting, or missing fitting in family library). See StingLog.");
        }

        /// <summary>
        /// Phase 139.29 — try to insert the right fitting between two
        /// unconnected connectors. Returns true on success and outputs
        /// the fitting kind (1=elbow, 2=union, 3=transition, 4=tee).
        /// Falls through quietly when the API call fails so the caller
        /// can fall back to direct ConnectTo.
        /// </summary>
        private static bool TryInsertFitting(Document doc, Connector a, Connector b, out int kind)
        {
            kind = 0;
            if (doc == null || a == null || b == null) return false;
            try
            {
                XYZ pa = a.Origin, pb = b.Origin;
                if (pa == null || pb == null) return false;
                XYZ da = (a.CoordinateSystem?.BasisZ) ?? XYZ.BasisZ;
                XYZ db = (b.CoordinateSystem?.BasisZ) ?? XYZ.BasisZ;
                bool sameAxis = Math.Abs(Math.Abs(da.DotProduct(db)) - 1.0) < 0.05;
                bool sameSize = SameSize(a, b);

                if (sameAxis && sameSize)
                {
                    var fi = doc.Create.NewUnionFitting(a, b);
                    if (fi != null) { kind = 2; return true; }
                }
                if (sameAxis && !sameSize)
                {
                    var fi = doc.Create.NewTransitionFitting(a, b);
                    if (fi != null) { kind = 3; return true; }
                }
                if (!sameAxis)
                {
                    var fi = doc.Create.NewElbowFitting(a, b);
                    if (fi != null) { kind = 1; return true; }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TryInsertFitting: {ex.Message}");
            }
            return false;
        }

        private static bool SameSize(Connector a, Connector b)
        {
            try
            {
                // Connector.Radius is set on round connectors;
                // Connector.Width / Height on rectangular. Use the larger
                // dimension when one is rect and the other round (rare).
                double sa = a.Shape == ConnectorProfileType.Round ? a.Radius * 2 : Math.Max(a.Width, a.Height);
                double sb = b.Shape == ConnectorProfileType.Round ? b.Radius * 2 : Math.Max(b.Width, b.Height);
                return Math.Abs(sa - sb) < 1e-3;
            }
            catch { return true; }
        }

        /// <summary>
        /// Phase 139.30 (X-05) — door swing envelope vs switch placement.
        /// For every placed switch / socket / electrical fixture whose
        /// rule's AnchorType starts with DOOR_, locate the nearest door
        /// and check whether the placement falls inside the door's
        /// swept arc (90° default). When a hit is found, surfaces a
        /// per-instance warning with a re-anchor recommendation
        /// (DOOR_LATCH_SIDE / DOOR_STRIKE_SIDE).
        /// </summary>
        private static void AuditDoorSwingClearance(
            Document doc, IList<PlacementRule> rules, PlacementResult result)
        {
            if (doc == null || result == null || result.PlacedIds.Count == 0) return;

            // Collect rules that placed switch-like fixtures near doors —
            // these are the ones at risk.
            var doorRiskRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (rules != null)
            {
                foreach (var r in rules)
                {
                    if (r == null) continue;
                    string anchor = (r.AnchorType ?? "").ToUpperInvariant();
                    if (anchor.StartsWith("DOOR_")
                        || anchor == "WALL_MIDPOINT"
                        || anchor == "WALL_FACE_OFFSET")
                        doorRiskRules.Add(r.MergeKey);
                }
            }
            if (doorRiskRules.Count == 0) return;

            // Build a door index: location point + facing + width per door.
            var doors = new List<DoorEnvelope>();
            try
            {
                foreach (var el in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Doors)
                    .WhereElementIsNotElementType())
                {
                    if (!(el is FamilyInstance fi)) continue;
                    var loc = (fi.Location as LocationPoint)?.Point;
                    if (loc == null) continue;
                    XYZ facing = fi.FacingOrientation ?? XYZ.BasisX;
                    XYZ hand = fi.HandOrientation   ?? XYZ.BasisY;
                    double widthFt = 0.85; // ~ 26" door fallback
                    try
                    {
                        var sym = fi.Symbol;
                        var pW = sym?.LookupParameter("Width") ?? sym?.get_Parameter(BuiltInParameter.DOOR_WIDTH);
                        if (pW != null && pW.StorageType == StorageType.Double) widthFt = Math.Max(0.5, pW.AsDouble());
                    }
                    catch { }
                    doors.Add(new DoorEnvelope
                    {
                        Id      = fi.Id,
                        Loc     = loc,
                        Facing  = facing,
                        Hand    = hand,
                        WidthFt = widthFt,
                        HandFlipped   = fi.HandFlipped,
                        FacingFlipped = fi.FacingFlipped,
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"AuditDoorSwingClearance enum doors: {ex.Message}"); }
            if (doors.Count == 0) return;

            int conflicts = 0;
            foreach (var id in result.PlacedIds)
            {
                FamilyInstance fi = null;
                try { fi = doc.GetElement(id) as FamilyInstance; } catch { }
                if (fi == null) continue;
                XYZ p = (fi.Location as LocationPoint)?.Point;
                if (p == null) continue;

                DoorEnvelope nearest = null;
                double bestSq = double.MaxValue;
                foreach (var d in doors)
                {
                    double dx = d.Loc.X - p.X, dy = d.Loc.Y - p.Y;
                    double sq = dx * dx + dy * dy;
                    double maxDistFt = d.WidthFt + (300.0 / 304.8);
                    if (sq < maxDistFt * maxDistFt && sq < bestSq) { bestSq = sq; nearest = d; }
                }
                if (nearest == null) continue;

                if (PointInsideSweptArc(nearest, p))
                {
                    conflicts++;
                    if (conflicts <= 10)
                    {
                        result.Warnings.Add(
                            $"Door swing: placed instance {id.Value} at ({p.X:F2},{p.Y:F2}) sits inside the " +
                            $"swept arc of door {nearest.Id.Value} ({nearest.WidthFt * 304.8:F0}mm). " +
                            $"The door handle will hit the switch on opening — re-anchor the rule to " +
                            $"DOOR_LATCH_SIDE or DOOR_STRIKE_SIDE, or move the switch ≥ {nearest.WidthFt * 304.8:F0}mm from the hinge.");
                    }
                }
            }
            if (conflicts > 10)
                result.Warnings.Add($"Door swing: {conflicts - 10} additional conflicts truncated — see StingLog.");
            if (conflicts > 0)
                StingLog.Warn($"AuditDoorSwingClearance: {conflicts} placed instance(s) inside door swept arc.");
        }

        private class DoorEnvelope
        {
            public ElementId Id;
            public XYZ       Loc;
            public XYZ       Facing;
            public XYZ       Hand;
            public double    WidthFt;
            public bool      HandFlipped;
            public bool      FacingFlipped;
        }

        /// <summary>
        /// Approximate "inside swept arc" test. The door hinges on one
        /// jamb; the leaf swings 90° from closed (along Hand axis) to
        /// open (along Facing axis, into the room). A switch placed in
        /// the quarter-circle between those two axes, within door-width
        /// of the hinge, sits in the swept envelope and will be hit by
        /// the handle on opening.
        /// </summary>
        private static bool PointInsideSweptArc(DoorEnvelope door, XYZ p)
        {
            try
            {
                double half = door.WidthFt * 0.5;
                XYZ hinge = door.HandFlipped
                    ? door.Loc + door.Hand.Multiply(half)
                    : door.Loc - door.Hand.Multiply(half);
                XYZ v = p - hinge; v = new XYZ(v.X, v.Y, 0);
                double r = v.GetLength();
                if (r < 1e-3) return true;
                if (r > door.WidthFt + 0.05) return false;
                XYZ closedDir = door.HandFlipped ? door.Hand.Negate() : door.Hand;
                XYZ openDir   = door.FacingFlipped ? door.Facing.Negate() : door.Facing;
                double cosToClosed = v.Normalize().DotProduct(closedDir.Normalize());
                double cosToOpen   = v.Normalize().DotProduct(openDir.Normalize());
                return cosToClosed > 0 && cosToOpen > 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// Phase 139.27 (X-04) — BS 7671 Table 4 cable / conduit bundle
        /// advisory. For rules whose <c>CableBundleAdvisoryCount</c> > 0,
        /// count placed instances of the same category within 300mm and
        /// warn when the count crosses the standard derating thresholds
        /// (4 → 0.80×, 6 → 0.69×, 9+ → 0.50×). Advisory only — does not
        /// auto-resize cables, this is a check the electrical engineer
        /// owns. Designed to flag before issue.
        /// </summary>
        private static void AuditCableBundleDerating(
            Document doc, IList<PlacementRule> rules, PlacementResult result)
        {
            if (doc == null || rules == null || result == null) return;
            const double bundleRadiusFt = 300.0 / 304.8;
            double radSq = bundleRadiusFt * bundleRadiusFt;

            // Group placed ids by rule MergeKey (via CountsByRule diag mapping
            // is not enough — we need positions). Use document categories
            // as a proxy.
            var byCategory = new Dictionary<string, List<(ElementId id, XYZ p)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in result.PlacedIds)
            {
                FamilyInstance fi = null;
                try { fi = doc.GetElement(id) as FamilyInstance; } catch { }
                if (fi == null) continue;
                XYZ p = (fi.Location as LocationPoint)?.Point;
                if (p == null) continue;
                string cat = "(uncategorised)";
                try { cat = fi.Category?.Name ?? "(uncategorised)"; } catch { }
                if (!byCategory.TryGetValue(cat, out var list))
                {
                    list = new List<(ElementId, XYZ)>();
                    byCategory[cat] = list;
                }
                list.Add((id, p));
            }

            // Apply advisory per rule; rules without the field don't trigger.
            foreach (var rule in rules)
            {
                if (rule == null || rule.CableBundleAdvisoryCount <= 0) continue;
                if (!byCategory.TryGetValue(rule.CategoryFilter ?? "", out var list)) continue;
                int worstBundle = 0;
                foreach (var (id, p) in list)
                {
                    int near = 0;
                    foreach (var (id2, p2) in list)
                    {
                        if (id == id2) continue;
                        double dx = p.X - p2.X, dy = p.Y - p2.Y, dz = p.Z - p2.Z;
                        if (dx * dx + dy * dy + dz * dz <= radSq) near++;
                    }
                    if (near > worstBundle) worstBundle = near;
                }
                int total = worstBundle + 1; // include self
                if (total < rule.CableBundleAdvisoryCount) continue;

                double derate = total >= 9 ? 0.50 : total >= 6 ? 0.69 : total >= 4 ? 0.80 : 1.00;
                result.Warnings.Add(
                    $"Cable derating: rule '{rule.MergeKey}' has up to {total} same-category instances within 300mm — " +
                    $"BS 7671 Table 4 derating ≈ {derate:F2}×. Verify cable size with the electrical engineer before issue.");
            }
        }
    }
}
