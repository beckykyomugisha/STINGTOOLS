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
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Structure;
using StingTools.Core;
using StingTools.Core.Routing;
using System.Text.RegularExpressions;

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
        public int CandidatesRejectedWetZone { get; set; }
        public int CandidatesPlaced { get; set; }
        public int SkippedNoSymbol { get; set; }
        public int SkippedHostPreflight { get; set; }
        public int ManufacturerMisses { get; set; }
        public string FirstSkipReason { get; set; } = "";

        // A11 (anchor-miss diagnostics) — count of rooms where this rule's
        // anchor generator fell back to the room centre (no doors / windows /
        // boundary segments / unknown anchor), plus the first reason seen.
        public int AnchorMissRooms { get; set; }
        public string FirstAnchorMiss { get; set; } = "";

        // A14 (under-fill diagnostics) — count of rooms where the derived cap
        // (Density / Linear / MaxPerRoom) exceeded the number of candidates
        // actually generated, so the rule silently placed fewer than asked.
        public int UnderFilledRooms { get; set; }
        public int UnderFillShortfall { get; set; }
        public string FirstUnderFill { get; set; } = "";

        public string OneLineSummary()
        {
            return $"{MergeKey}: rooms={RoomsConsidered}/-{RoomsFilteredByName}/-{RoomsFilteredByExclude} " +
                   $"cand={CandidatesGenerated} placed={CandidatesPlaced} " +
                   $"skip(host={SkippedHostPreflight}, sym={SkippedNoSymbol}, dedup={CandidatesRejectedDedup}, wetzone={CandidatesRejectedWetZone}, " +
                   $"conflict={RoomsBlockedByConflict}, dep={RoomsBlockedByDependsOn}, mfr={ManufacturerMisses})" +
                   (AnchorMissRooms > 0 ? $" • anchorMiss={AnchorMissRooms} ({FirstAnchorMiss})" : "") +
                   (UnderFilledRooms > 0 ? $" • underFill={UnderFilledRooms} (short {UnderFillShortfall}; {FirstUnderFill})" : "") +
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

        // Per-run wet-zone exclusion state, set from the active building
        // profile's "Wet-zone checks" toggle. ProcessRoomRule filters
        // candidates through _wetZoneChecker when enabled and the rule
        // declares a WetZoneExclusion level.
        private static bool _wetZoneEnabled;
        private static WetZoneExclusionChecker _wetZoneChecker;

        // Per-run coverage-guarantee gate, set from the active building
        // profile's "Coverage guarantee" toggle. When on, rules with
        // GuaranteeCoverage=true route through CoverageGridGenerator.
        private static bool _coverageEnabled;

        // Per-run index of positions of fixtures placed by PREVIOUS STING runs
        // (carry a non-empty ASS_PLACEMENT_RULE_TXT). Seeded into the per-room
        // dedup so re-running the same rules is idempotent — coincident
        // placements are skipped instead of doubled.
        private static List<XYZ> _priorPlaced = new List<XYZ>();

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
                catch (Exception ex) { StingLog.Warn($"[FixturePlacementEngine] Read assembly build stamp: {ex.Message}"); _buildStamp = "(unknown)"; }
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
            catch (Exception ex) { StingLog.Warn($"[FixturePlacementEngine] Collect rule-loader validation warnings: {ex.Message}"); }

            // Resolve the active building profile once (in-session UI selection
            // wins over the on-disk file so the gate works WITHOUT a Save first).
            // Used by the accessibility / wet-zone / coverage toggles below and
            // by the rule filter further down.
            ProjectBuildingProfile activeProfile = null;
            try
            {
                activeProfile = StingTools.Commands.Placement.PlaceFixturesOptions.SessionProfile
                                ?? ProjectBuildingProfileIO.Load(doc.PathName);
            }
            catch (Exception ex) { StingLog.Warn($"FixturePlacementEngine: load building profile: {ex.Message}"); }

            // Wet-zone exclusion state for ProcessRoomRule. Default ON unless the
            // profile explicitly disables it.
            _wetZoneEnabled = activeProfile == null || activeProfile.EnableWetZoneChecks;
            _wetZoneChecker = _wetZoneEnabled ? new WetZoneExclusionChecker(doc) : null;

            // Coverage-guarantee gate (default ON unless the profile disables it).
            _coverageEnabled = activeProfile == null || activeProfile.EnableCoverageGuarantee;

            // Accessibility / mounting-height validation against
            // STING_HEIGHT_STANDARDS.json — gated by the profile's
            // "Accessibility checks" toggle (default ON).
            if (activeProfile == null || activeProfile.EnableAccessibilityChecks)
            {
                try
                {
                    var hsw = HeightStandardsTable.ValidateRulesAgainstStandards(rules);
                    if (hsw != null)
                        foreach (var w in hsw)
                            if (!string.IsNullOrEmpty(w)) result.Warnings.Add(w);
                }
                catch (Exception hex) { StingLog.Warn($"HeightStandards validation: {hex.Message}"); }
            }

            if (rules.Count == 0)
            {
                result.Warnings.Add("No placement rules found. Ship STING_PLACEMENT_RULES.json or provide a project override.");
                return result;
            }

            // Phase 188 (review fix #3b) — apply the project building profile so
            // a run matches what the Placement Centre displays. FilterByProfile
            // existed and the UI mirrored it, but the engine ran every rule
            // regardless of building type. No-op when no
            // _BIM_COORD/placement_profile.json is configured (empty BuildingType
            // + ActiveStandards → FilterByProfile returns the set unchanged).
            try
            {
                // Use the profile resolved above (in-session UI selection wins
                // over the on-disk file so the gate works WITHOUT a Save first).
                var profile = activeProfile;
                bool profileActive = profile != null &&
                    (!string.IsNullOrEmpty(profile.BuildingType) ||
                     (profile.ActiveStandards != null && profile.ActiveStandards.Length > 0));
                if (profileActive)
                {
                    // The standards gate matches a rule's structured
                    // ApplicableStandards, falling back to its free-text
                    // StandardRef. A rule carrying neither is untagged and passes
                    // the gate unconditionally — by design, but worth reporting so
                    // "the standards filter didn't remove anything" is explicable.
                    if (profile.ActiveStandards != null && profile.ActiveStandards.Length > 0)
                    {
                        // Count over non-null rules only, so a null list entry is
                        // not miscounted as an untagged rule in the warning.
                        int total   = rules.Count(r => r != null);
                        int tagged  = rules.Count(r => r != null
                            && (!string.IsNullOrEmpty(r.ApplicableStandards) || !string.IsNullOrEmpty(r.StandardRef)));
                        int untagged = total - tagged;

                        if (tagged == 0)
                        {
                            // Fallback path retained: with nothing tagged the gate
                            // cannot discriminate at all.
                            result.Warnings.Add("Building profile declares ActiveStandards but no rule carries ApplicableStandards or StandardRef — the standards filter is inert. Tag rules with standards to enable standards-based filtering.");
                        }
                        else if (untagged > 0)
                        {
                            result.Warnings.Add($"Standards gate: {untagged} of {total} rule(s) carry neither " +
                                                "ApplicableStandards nor StandardRef, so they bypass the profile's " +
                                                "ActiveStandards filter and are always included. Tag them to bring them " +
                                                "under the gate.");
                        }
                    }

                    int before = rules.Count;
                    rules = PlacementRuleLoader.FilterByProfile(new List<PlacementRule>(rules), profile);
                    int removed = before - rules.Count;
                    StingLog.Info($"FixturePlacementEngine: building-profile filter kept {rules.Count} of {before} rules (BuildingType='{profile.BuildingType}').");
                    if (removed > 0)
                        result.Warnings.Add($"Building profile '{profile.BuildingType}' filtered out {removed} of {before} rule(s) not matching the active building type / standards.");
                    if (rules.Count == 0)
                    {
                        result.Warnings.Add($"Building profile '{profile.BuildingType}' filtered out ALL rules — nothing to place. Check the profile's BuildingType / ActiveStandards against your rule set.");
                        return result;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"FixturePlacementEngine: building-profile filter: {ex.Message}"); }

            // Reset per-run post-placement hook state (tag-pipeline context
            // cache + MEP-connect counters + TagPipelineMissing flag) so
            // consecutive runs are independent and pick up manual edits made
            // between runs.
            PostPlacementHooks.BeginRun();

            // Idempotency: index the positions of fixtures placed by previous
            // STING runs (they carry a non-empty ASS_PLACEMENT_RULE_TXT). Seeded
            // into the per-room dedup so re-running the same rules doesn't double
            // fixtures. Skipped for dry-run previews (nothing is committed).
            _priorPlaced = dryRun ? new List<XYZ>() : BuildPriorPlacedIndex(doc, ResolvePriorPlacedCategories(doc, rules));
            if (!dryRun && _priorPlaced.Count > 0)
                result.Warnings.Add($"Idempotency: found {_priorPlaced.Count} fixture(s) from previous STING placement runs — coincident positions will be skipped so this re-run won't duplicate them. Use 'Undo last run' first if you intend to replace them.");

            var roomCtx = new Dictionary<SpatialElement, (Document src, Transform toHost)>();
            var rooms = CollectRooms(doc, roomIds, result, roomCtx);
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
            // One scorer per source document — host rooms use the host scorer;
            // linked rooms get a scorer bound to their link document so room +
            // wall / door geometry is read in the same coordinate space.
            bool rejectInsideWall = StingTools.Commands.Placement.PlaceFixturesOptions.RejectInsideWall;
            var scorerByDoc = new Dictionary<Document, PlacementScorer> { [doc] = scorer };
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
                    catch (Exception ex2) { result.Warnings.Add($"Rule '{r.MergeKey}' RoomFilter regex: {ex2.Message}"); }
                }
                if (!string.IsNullOrEmpty(r.ExcludeRoomFilter))
                {
                    try { excludeFilterRx[r.MergeKey] = new System.Text.RegularExpressions.Regex(r.ExcludeRoomFilter, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled); }
                    catch (Exception ex2) { result.Warnings.Add($"Rule '{r.MergeKey}' ExcludeRoomFilter regex: {ex2.Message}"); }
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
                catch (Exception ex2)
                {
                    result.Warnings.Add($"Transaction start failed: {ex2.Message}");
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
                    // Phase 139.5 Q21 — room name derived once per room for fast pre-filter.
                    string roomName = SafeRoomName(room);

                    // PC-13 — per-room state so dependent rules see predecessors.
                    var roomState = new RoomState();

                    // Resolve the room's source document + host transform. Linked
                    // rooms use a link-bound scorer + the simpler linked path.
                    var rc = roomCtx.TryGetValue(room, out var rcv) ? rcv : (src: doc, toHost: Transform.Identity);
                    bool roomLinked = rc.toHost != null && !rc.toHost.IsIdentity;
                    PlacementScorer roomScorer = scorer;
                    if (rc.src != null && rc.src != doc)
                    {
                        if (!scorerByDoc.TryGetValue(rc.src, out roomScorer))
                        {
                            roomScorer = new PlacementScorer(rc.src) { RejectInsideWall = rejectInsideWall };
                            scorerByDoc[rc.src] = roomScorer;
                        }
                    }

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
                                continue;
                            }
                            // PC-13 — DependsOn: skip if predecessor produced no placements yet.
                            if (!string.IsNullOrEmpty(rule.DependsOn) && !roomState.PlacedByRule.ContainsKey(rule.DependsOn))
                            {
                                result.Warnings.Add($"Room {room.Id} / {rule.MergeKey}: skipped — DependsOn '{rule.DependsOn}' has no placement in this room.");
                                continue;
                            }

                            if (roomLinked)
                            {
                                // Linked-architecture room: score in link coords,
                                // place non-hosted on the nearest host level at the
                                // transformed point. (CoPlaceWith / RELATIVE_TO /
                                // wet-zone are host-path-only in v1.)
                                ProcessLinkedRoomRule(doc, room, rule, roomScorer, rc.toHost,
                                    perCategorySymbol, result, dryRun);
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
                        catch (Exception ex2)
                        {
                            result.Warnings.Add($"Room {room.Id} / {rule.MergeKey}: {ex2.Message}");
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
                            try { el = doc.GetElement(id); } catch (Exception ex2) { StingLog.Warn($"[FixturePlacementEngine] Post-commit GetElement({id.Value}): {ex2.Message}"); }
                            if (el == null) { rolledBack++; continue; }
                            alive.Add(id);
                            string cat = "(uncategorised)";
                            try { cat = el.Category?.Name ?? "(uncategorised)"; } catch (Exception ex3) { StingLog.Warn($"[FixturePlacementEngine] Read placed element category: {ex3.Message}"); }
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

            // Surface post-placement hook outcomes in the run report.
            if (!dryRun)
            {
                if (PostPlacementHooks.RunDataTagPipeline && PostPlacementHooks.TagPipelineMissing)
                    result.Warnings.Add("Post-placement: 'Run data-tag pipeline' is on but the tag population context was invalid (rooms / levels / shared parameters missing) — placed instances were NOT tagged this run.");
                if (PostPlacementHooks.AssignMepSystem &&
                    (PostPlacementHooks.MepConnectedCount > 0 || PostPlacementHooks.MepLeftOpenCount > 0))
                    result.Warnings.Add($"Post-placement: MEP connect joined {PostPlacementHooks.MepConnectedCount} connector(s); {PostPlacementHooks.MepLeftOpenCount} left open (no coincident compatible connector within 600 mm).");
            }

            // Advisory pass — report specification fields that are set on a rule
            // but cannot take effect given that rule's configuration (e.g.
            // MinSlopePercent on a rule with RoutingMode NONE). Advisory only:
            // never blocks the run, never edits the model. Capped so a large
            // mis-configured rule set can't flood the result panel.
            try
            {
                var advisories = PlacementAdvisoryValidator.Validate(rules, result);
                const int MaxAdvisories = 25;
                foreach (var a in advisories.Take(MaxAdvisories)) result.Warnings.Add(a);
                if (advisories.Count > MaxAdvisories)
                    result.Warnings.Add($"Rule-field advisories: {advisories.Count - MaxAdvisories} more truncated — see StingLog.");
                if (advisories.Count > 0)
                    StingLog.Info($"FixturePlacementEngine: {advisories.Count} rule-field advisory/ies:{Environment.NewLine}" +
                                  string.Join(Environment.NewLine, advisories));
            }
            catch (Exception ex) { StingLog.Warn($"FixturePlacementEngine: advisory validator: {ex.Message}"); }

            return result;
        }

        // Both Rooms (architecture) and MEP Spaces are SpatialElements sharing the
        // boundary / bbox / level / area API the engine uses, so it places into
        // either. MEP models commonly carry Spaces (rooms live in a linked arch
        // model) — supporting Spaces lets those models place without a host Room.
        private static bool IsPlaceableSpatial(Element el)
            => (el is Room r && r.Area > 0.0) || (el is Space sp && sp.Area > 0.0);

        /// <summary>
        /// Point-in-spatial test. Exact for Rooms (IsPointInRoom); for MEP Spaces
        /// (no public point test) falls back to plan bounding-box containment.
        /// Used by the coverage grid + lighting grid so they clip to either.
        /// </summary>
        public static bool PointInSpatial(SpatialElement se, XYZ p)
        {
            if (se == null || p == null) return false;
            try { if (se is Room rm) return rm.IsPointInRoom(p); } catch { }
            try
            {
                var bb = se.get_BoundingBox(null);
                if (bb == null) return true;
                return p.X >= bb.Min.X && p.X <= bb.Max.X && p.Y >= bb.Min.Y && p.Y <= bb.Max.Y;
            }
            catch { return true; }
        }

        // Per-room source context: which document the room's geometry lives in
        // and the transform from that document to host coordinates. Host rooms
        // map to (host, Identity); linked rooms to (linkDoc, linkTransform).
        private static List<SpatialElement> CollectRooms(
            Document doc, IList<ElementId> roomIds, PlacementResult result,
            Dictionary<SpatialElement, (Document src, Transform toHost)> ctx)
        {
            var rooms = new List<SpatialElement>();
            try
            {
                if (roomIds == null || roomIds.Count == 0)
                {
                    foreach (var bic in new[] { BuiltInCategory.OST_Rooms, BuiltInCategory.OST_MEPSpaces })
                        foreach (var e in new FilteredElementCollector(doc)
                            .OfCategory(bic).WhereElementIsNotElementType())
                            if (IsPlaceableSpatial(e))
                            {
                                rooms.Add((SpatialElement)e);
                                if (ctx != null) ctx[(SpatialElement)e] = (doc, Transform.Identity);
                            }

                    // Linked architecture: read Rooms/Spaces from each loaded link
                    // and remember its transform so placement maps back to host
                    // coordinates. Project-scope only (active-view/selection ids
                    // are host ids and can't reference linked elements).
                    CollectLinkedRooms(doc, rooms, ctx, result);
                }
                else
                {
                    foreach (var id in roomIds)
                    {
                        var el = doc.GetElement(id);
                        if (IsPlaceableSpatial(el))
                        {
                            rooms.Add((SpatialElement)el);
                            if (ctx != null) ctx[(SpatialElement)el] = (doc, Transform.Identity);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Room/Space collection failed: {ex.Message}");
            }
            return rooms;
        }

        /// <summary>Read Rooms/Spaces from every loaded Revit link and record each
        /// one's source document + host transform.</summary>
        private static void CollectLinkedRooms(
            Document doc, List<SpatialElement> rooms,
            Dictionary<SpatialElement, (Document src, Transform toHost)> ctx,
            PlacementResult result)
        {
            if (ctx == null) return;
            try
            {
                int linkedCount = 0;
                foreach (var li in new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
                {
                    Document ld = null;
                    try { ld = li.GetLinkDocument(); } catch { }
                    if (ld == null) continue;                 // link unloaded
                    Transform tf = Transform.Identity;
                    try { tf = li.GetTotalTransform() ?? Transform.Identity; } catch { }
                    foreach (var bic in new[] { BuiltInCategory.OST_Rooms, BuiltInCategory.OST_MEPSpaces })
                        foreach (var e in new FilteredElementCollector(ld)
                            .OfCategory(bic).WhereElementIsNotElementType())
                            if (IsPlaceableSpatial(e))
                            {
                                var se = (SpatialElement)e;
                                rooms.Add(se);
                                ctx[se] = (ld, tf);
                                linkedCount++;
                            }
                }
                if (linkedCount > 0)
                    result.Warnings.Add($"Linked architecture: read {linkedCount} room(s)/space(s) from loaded links. Fixtures place non-hosted on the nearest host level at the transformed location (orientation + face-hosting from a linked host are not applied — v1).");
            }
            catch (Exception ex) { StingLog.Warn($"CollectLinkedRooms: {ex.Message}"); }
        }

        /// <summary>Nearest host-document Level to a Z elevation (in host feet).</summary>
        private static Level NearestHostLevel(Document hostDoc, double zFt)
        {
            Level best = null; double bestD = double.MaxValue;
            try
            {
                foreach (var lv in new FilteredElementCollector(hostDoc)
                    .OfClass(typeof(Level)).Cast<Level>())
                {
                    double d = Math.Abs(lv.Elevation - zFt);
                    if (d < bestD) { bestD = d; best = lv; }
                }
            }
            catch (Exception ex) { StingLog.Warn($"NearestHostLevel: {ex.Message}"); }
            return best;
        }

        /// <summary>PC-13 per-room state: maps RuleId/MergeKey → list of placed points,
        /// plus a "last point" lookup for CoPlaceWith / RELATIVE_TO.</summary>
        private class RoomState
        {
            public Dictionary<string, List<XYZ>> PlacedByRule { get; }
                = new Dictionary<string, List<XYZ>>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, XYZ> LastPointByRule { get; }
                = new Dictionary<string, XYZ>(StringComparer.OrdinalIgnoreCase);
            // Phase 195 — every placement in this room indexed by CATEGORY, so
            // overlapping rules from different packs (e.g. 22 "Lighting Devices"
            // rules) can't crowd the same spot. A candidate is rejected when a
            // same-category fixture already sits within the rule's MinSpacingMm.
            public Dictionary<string, List<XYZ>> PlacedByCategory { get; }
                = new Dictionary<string, List<XYZ>>(StringComparer.OrdinalIgnoreCase);
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
            SpatialElement room,
            PlacementRule rule,
            PlacementScorer scorer,
            Dictionary<string, FamilySymbol> perCategorySymbol,
            PlacementResult result,
            bool dryRun,
            RoomState state)
        {
            string roomKey = $"{room.Id}::{SafeRoomName(room)}";
            int alreadyInRoom = result.CountsByRoom.ContainsKey(roomKey) ? result.CountsByRoom[roomKey] : 0;
            // Phase 139.27 — per-rule diagnostic entry for this room+rule pair.
            var diagRoom = result.Diag(rule.MergeKey);

            // Phase 185 (footprint-aware spacing): when the rule opts in via
            // FamilyBboxAware, resolve the family symbol BEFORE scoring and
            // rebuild the rule with spacing fields scaled to the family's
            // real footprint. Lets one rule serve 150 mm switches and
            // 1200 mm AHUs without per-vendor JSON edits. No-op when the
            // flag is false (legacy behaviour preserved).
            PlacementRule effRule = rule;
            if (rule.FamilyBboxAware && !dryRun)
            {
                var preSym = ResolveSymbol(doc, rule.CategoryFilter, rule, perCategorySymbol, result);
                if (preSym != null)
                {
                    effRule = ScaleByFootprint(rule, preSym, result);
                }
            }

            var placedPoints = new List<XYZ>(); // for spacing scoring

            // PC-13 — RELATIVE_TO / EQUIPMENT_PAIR: short-circuit by stamping the
            // predecessor's last point as the only candidate.
            string anchor = (effRule.AnchorType ?? "").ToUpperInvariant();

            // Coverage guarantee: when the rule sets GuaranteeCoverage=true (and
            // the building profile's "Coverage guarantee" toggle is on), fill the
            // room with a √2-spaced grid via CoverageGridGenerator so every floor
            // point lies within CoverageRadiusMm of a device. The generator
            // honours CoverageRadiusMm / MaxSpacingMm / MinSpacingMm /
            // WallClearanceMm / ObstructionClearanceMm itself, so these points
            // bypass the score-rank + spacing re-selection below.
            bool coverageMode = _coverageEnabled && effRule.GuaranteeCoverage
                                && effRule.CoverageRadiusMm > 0
                                && anchor != "RELATIVE_TO" && anchor != "EQUIPMENT_PAIR";

            List<PlacementCandidate> candidates;
            if (coverageMode)
            {
                double anchorZ = scorer.ResolveAnchorZForRoom(room, effRule);
                var cov = new CoverageGridGenerator(doc).Generate(room, effRule, anchorZ);
                candidates = new List<PlacementCandidate>(cov.Points.Count);
                foreach (var p in cov.Points)
                    candidates.Add(new PlacementCandidate { Position = p, RoomId = room.Id, Rule = effRule, Score = 1.0 });
                result.CandidatesEvaluated += candidates.Count;
                foreach (var w in cov.Warnings)
                    if (!string.IsNullOrEmpty(w)) result.Warnings.Add($"[{effRule.RuleId}] {w}");
            }
            else if ((anchor == "RELATIVE_TO" || anchor == "EQUIPMENT_PAIR")
                && !string.IsNullOrEmpty(effRule.DependsOn)
                && state.LastPointByRule.TryGetValue(effRule.DependsOn, out var prev))
            {
                XYZ pt = new XYZ(prev.X + effRule.OffsetXMm / 304.8,
                                 prev.Y + effRule.OffsetYMm / 304.8,
                                 prev.Z + effRule.OffsetZMm / 304.8);
                candidates = new List<PlacementCandidate>
                {
                    new PlacementCandidate { Position = pt, RoomId = room.Id, Rule = effRule, Score = 1.0 }
                };
                result.CandidatesEvaluated += 1;
            }
            else
            {
                candidates = scorer.Score(room, effRule, placedPoints, alreadyInRoom);
                result.CandidatesEvaluated += candidates.Count;

                // A11 (anchor-miss diagnostics) — drain the scorer's per-Score()
                // anchor-fallback list right after the call (it's reset on every
                // Score, so this must read it now). Surface as a per-rule count +
                // one-shot warning so a door-anchored rule in a doorless room is
                // visible instead of silently landing at the room centre.
                var misses = scorer.LastAnchorMisses;
                if (misses != null && misses.Count > 0 && diagRoom != null)
                {
                    diagRoom.AnchorMissRooms++;
                    if (string.IsNullOrEmpty(diagRoom.FirstAnchorMiss))
                        diagRoom.FirstAnchorMiss = misses[0];
                    string amKey = $"AnchorMiss:{effRule.MergeKey}";
                    if (!result.Warnings.Any(w => w.StartsWith(amKey, StringComparison.Ordinal)))
                    {
                        result.Warnings.Add($"{amKey} — rule '{effRule.MergeKey}' anchor '{effRule.AnchorType}' " +
                            $"fell back to room centre ({misses[0]}); more rooms may be affected. " +
                            $"Devices land at the centroid, not the intended feature.");
                        StingLog.Warn($"Placement A11: rule '{effRule.MergeKey}' anchor fallback in room {room.Id}: {misses[0]}");
                    }
                }
            }
            if (candidates.Count == 0) return;

            // Wet-zone exclusion (BS 7671 / IEC 60364-7-701) — gated by the
            // building profile's "Wet-zone checks" toggle and the rule's
            // WetZoneExclusion level. Drops candidates that fall inside a
            // bath/shower/basin exclusion volume. No-op when the toggle is off
            // or the rule sets no exclusion level.
            if (_wetZoneEnabled && _wetZoneChecker != null
                && !string.IsNullOrEmpty(effRule.WetZoneExclusion)
                && !effRule.WetZoneExclusion.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            {
                var keep = new List<PlacementCandidate>(candidates.Count);
                foreach (var c in candidates)
                {
                    bool rejected = false;
                    try { rejected = _wetZoneChecker.Check(room, c.Position, effRule.WetZoneExclusion).Rejected; }
                    catch (Exception wzEx) { StingLog.Warn($"WetZone check ({effRule.RuleId}): {wzEx.Message}"); }
                    if (!rejected) keep.Add(c);
                }
                int dropped = candidates.Count - keep.Count;
                if (dropped > 0)
                {
                    StingLog.Info($"WetZone: rule '{effRule.RuleId}' dropped {dropped} candidate(s) in room {room.Id} (exclusion {effRule.WetZoneExclusion}).");
                    diagRoom.CandidatesRejectedWetZone += dropped;
                }
                candidates = keep;
                if (candidates.Count == 0) return;
            }

            // PC-12 — derive the count for Density / Linear rules from the room's
            // area, occupancy or perimeter, capped by MaxPerRoom when set.
            // Coverage mode wants ALL generated points placed (that IS the
            // guarantee), limited only by an explicit MaxPerRoom.
            int cap;
            if (coverageMode)
            {
                cap = effRule.MaxPerRoom > 0 ? Math.Min(effRule.MaxPerRoom, candidates.Count) : candidates.Count;
            }
            else
            {
                cap = ComputeCap(effRule, room, candidates.Count, alreadyInRoom, out int desiredCap);
                // A14 (under-fill diagnostics) — the rule wanted more than the
                // anchor generator could produce (e.g. WALL_MIDPOINT emits one
                // point per segment but a Linear rule asked for 10). Surface it
                // so the silent shortfall is visible in the run report.
                if (desiredCap > candidates.Count && diagRoom != null)
                {
                    diagRoom.UnderFilledRooms++;
                    diagRoom.UnderFillShortfall += (desiredCap - candidates.Count);
                    if (string.IsNullOrEmpty(diagRoom.FirstUnderFill))
                        diagRoom.FirstUnderFill = $"cap {desiredCap} vs {candidates.Count} candidate(s)";
                    string ufKey = $"UnderFill:{effRule.MergeKey}";
                    if (!result.Warnings.Any(w => w.StartsWith(ufKey, StringComparison.Ordinal)))
                    {
                        result.Warnings.Add($"{ufKey} — rule '{effRule.MergeKey}' wanted {desiredCap} but only " +
                            $"{candidates.Count} candidate(s) were generated (anchor '{effRule.AnchorType}'); " +
                            $"placed {candidates.Count}. For 'one every N m' along walls use a Linear rule with " +
                            $"PerLinearMetre (auto-densifies WALL_MIDPOINT) or the LINEAR_WALL anchor.");
                        StingLog.Warn($"Placement A14: rule '{effRule.MergeKey}' under-fill in room {room.Id}: " +
                            $"desired {desiredCap} vs candidates {candidates.Count}.");
                    }
                }
            }
            if (cap == 0) return;

            // Phase 188 (review pass-2 #3) — enforce intra-rule MinSpacingMm at
            // selection. The scorer scores every candidate against an EMPTY
            // already-placed list (placedPoints is filled only after placement),
            // so SpacingScore is always 1.0 and a plain Take(cap) could pick
            // adjacent candidates closer than MinSpacingMm (e.g. CEILING_TILE_CORNER
            // emits points every 600 mm but the rule asks for 1000 mm). Greedily
            // accept ranked candidates that clear MinSpacingMm from the ones
            // already accepted. MinSpacingMm <= 0 ⇒ legacy Take(cap).
            // Coverage points are already √2-spaced + MinSpacing-floored by the
            // generator, so take them straight (preserving grid order). Other
            // rules go through the score-rank + greedy MinSpacing selection.
            var chosen = coverageMode
                ? candidates.GetRange(0, Math.Min(cap, candidates.Count))
                : SelectWithSpacing(candidates, cap, effRule.MinSpacingMm);

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

            FamilySymbol symbol = ResolveSymbol(doc, effRule.CategoryFilter, effRule, perCategorySymbol, result);
            if (symbol == null)
            {
                // Make the silent "no symbol → 0 placements" case visible.
                // Count every candidate this rule would have placed as skipped
                // and surface a one-shot per-(rule, category, variant) warning so
                // the user knows why the rule placed nothing.
                if (diagRoom != null)
                {
                    diagRoom.SkippedNoSymbol += chosen.Count;
                    if (string.IsNullOrEmpty(diagRoom.FirstSkipReason))
                        diagRoom.FirstSkipReason = $"no family symbol resolved for category '{effRule.CategoryFilter}'" +
                            (string.IsNullOrEmpty(effRule.VariantHint) ? "" : $" / variant '{effRule.VariantHint}'");
                }
                result.SkippedCount += chosen.Count;
                string symWarnKey = $"NoSymbol:{effRule.CategoryFilter}:{effRule.VariantHint}:{effRule.MergeKey}";
                if (!result.Warnings.Any(w => w.Contains(symWarnKey)))
                    result.Warnings.Add($"{symWarnKey} — rule '{effRule.MergeKey}' resolved no family symbol for category " +
                        $"'{effRule.CategoryFilter}'" +
                        (string.IsNullOrEmpty(effRule.VariantHint) ? "" : $" / variant '{effRule.VariantHint}'") +
                        $"; {chosen.Count} candidate(s) skipped. Load a matching family or adjust FamilyTypeRegex / VariantHint.");
                return;
            }

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
                // Idempotency: also dedup against fixtures from previous runs so
                // re-running the same rules doesn't double up.
                if (_priorPlaced != null && _priorPlaced.Count > 0)
                    existingNearby.AddRange(_priorPlaced);
            }
            catch (Exception ex) { StingLog.Warn($"[FixturePlacementEngine] Collect existing PlacedByRule for dedup: {ex.Message}"); }
            double dedupFt = Math.Max(effRule.ToleranceMm, 25.0) * MmToFt;
            double dedupSq = dedupFt * dedupFt;

            // Phase 195 — same-category cross-rule crowding guard. Fetch (or create)
            // this category's running placement list for the room so a candidate can
            // be rejected when a same-category fixture (placed by ANY rule this run)
            // already sits within the rule's MinSpacingMm. This collapses the
            // overlapping pack rules (e.g. 22 "Lighting Devices" rules) that were
            // each placing a full set, into one fixture per spot.
            string catKey = effRule.CategoryFilter ?? "";
            List<XYZ> catPlaced = null;
            if (state != null && !string.IsNullOrEmpty(catKey))
            {
                if (!state.PlacedByCategory.TryGetValue(catKey, out catPlaced))
                { catPlaced = new List<XYZ>(); state.PlacedByCategory[catKey] = catPlaced; }
            }
            // ~290 of the shipped rules leave MinSpacingMm = 0, which would
            // disable the crowding guard and let overlapping pack rules stack
            // (the audit's #1 finding). When a rule sets no MinSpacing, fall back
            // to an anchor-appropriate crowding FLOOR so cross-rule stacking is
            // still prevented. Ceiling/grid fixtures (lights, diffusers,
            // sprinklers) use 1000 mm — overlapping lux grids must collapse. Wall/
            // point devices use a 150 mm PHYSICAL-OVERLAP floor only: two ~86 mm
            // faceplates can't sit closer than that, but legitimately-distinct
            // adjacent devices (e.g. a nurse-call socket next to a power socket
            // ~200 mm apart) survive — a 250 mm floor wrongly rejected those.
            // An explicit MinSpacingMm always wins.
            bool ceilGrid = anchor == "CEILING_CENTRE" || anchor == "LIGHTING_GRID"
                         || anchor == "LUX_GRID" || anchor.StartsWith("CEILING_TILE");
            double crowdMm = effRule.MinSpacingMm > 0 ? effRule.MinSpacingMm
                                                      : (ceilGrid ? 1000.0 : 150.0);
            double catMergeFt = crowdMm * MmToFt;
            double catMergeSq = catMergeFt * catMergeFt;

            // Tier 1 — collect placed instances and orient them AFTER one
            // doc.Regenerate() (below the loop). Reading FacingOrientation before
            // a regen returns zero/default on a freshly-created non-hosted
            // instance, so the post-hoc wall alignment no-opped and fixtures
            // stayed diagonal. One regen per rule-room (not per instance) keeps
            // it cheap while making orientation reliable.
            var toOrient = new List<FamilyInstance>();

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
                // Phase 195 — same-category crowding: reject when a fixture of this
                // category already sits within MinSpacing horizontally (any rule).
                if (catMergeSq > 0 && catPlaced != null && catPlaced.Count > 0)
                {
                    bool crowded = false;
                    foreach (var ex in catPlaced)
                    {
                        if (ex == null) continue;
                        double dx = ex.X - c.Position.X, dy = ex.Y - c.Position.Y;
                        if (dx * dx + dy * dy < catMergeSq) { crowded = true; break; }
                    }
                    if (crowded)
                    {
                        result.SkippedCount++;
                        if (diagRoom != null) diagRoom.CandidatesRejectedDedup++;
                        continue;
                    }
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
                    var pf = PlacementHostPreflight.Place(doc, symbol, room, c.Position, effRule);
                    FamilyInstance fi = pf.Placed;
                    if (pf.Skipped || fi == null)
                    {
                        result.SkippedCount++;
                        if (!string.IsNullOrEmpty(pf.Reason))
                            result.Warnings.Add(pf.Reason);
                        continue;
                    }

                    WriteAnchorParameters(fi, effRule);
                    // Tier 1 — defer orientation. OrientPlacedInstance applies
                    // RotationDeg, flips the family to face INTO the room, and
                    // snaps it onto the wall face — but it needs a valid
                    // FacingOrientation, which only exists after a regen. Collect
                    // here; orient all after one regen below the loop.
                    toOrient.Add(fi);
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
                    catPlaced?.Add(c.Position);     // Phase 195 — same-category crowding index

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
                    try { PostPlacementHooks.RunFor(fi, effRule); }
                    catch (Exception hkEx) { result.Warnings.Add($"Post-placement hook for {fi.Id}: {hkEx.Message}"); }
                }
                catch (Exception ex2)
                {
                    result.SkippedCount++;
                    result.Warnings.Add($"Place {rule.CategoryFilter} in {SafeRoomName(room)}: {ex2.Message}");
                }
            }

            // Tier 1 — orient every just-placed instance after ONE regen so
            // FamilyInstance.FacingOrientation is valid. Previously orientation
            // ran inline on a freshly-created instance whose facing was still
            // zero/default, so the wall-alignment in OrientPlacedInstance no-opped
            // and fixtures stayed diagonal. One regen per rule-room is cheap.
            if (toOrient.Count > 0)
            {
                try { doc.Regenerate(); }
                catch (Exception rex) { StingLog.Warn($"Regenerate before orient: {rex.Message}"); }
                foreach (var ofi in toOrient)
                {
                    if (ofi == null || !ofi.IsValidObject) continue;
                    try { OrientPlacedInstance(doc, ofi, effRule, room); }
                    catch (Exception oex) { result.Warnings.Add($"Orient {rule.CategoryFilter} in {SafeRoomName(room)}: {oex.Message}"); }
                }
            }
        }

        /// <summary>
        /// PC-12 — compute how many candidates this rule should consume in this
        /// room. Point rules use MaxPerRoom; Density rules derive from area or
        /// occupancy; Linear rules from perimeter. MaxPerRoom (when > 0) is a
        /// hard cap regardless of kind.
        /// </summary>
        private static int ComputeCap(PlacementRule rule, SpatialElement room, int candidateCount, int alreadyInRoom)
            => ComputeCap(rule, room, candidateCount, alreadyInRoom, out _);

        /// <summary>
        /// Overload that also reports the <paramref name="desiredCap"/> — the
        /// count the rule wanted (after the MaxPerRoom hard cap) BEFORE it was
        /// clamped to the number of candidates available. A14 (under-fill
        /// diagnostics) compares desiredCap against candidateCount so the run
        /// report can flag "wanted N, only M candidates generated".
        /// </summary>
        private static int ComputeCap(PlacementRule rule, SpatialElement room, int candidateCount, int alreadyInRoom, out int desiredCap)
        {
            int cap;
            switch (rule.RuleKind)
            {
                case PlacementRuleKind.Density:
                {
                    // Phase 188 (review fix #2) — derive the count from EVERY
                    // declared density rate, not just PerAreaM2 / PerOccupant.
                    // PerBed / PerWorkstation / PerPupil / PerToiletCubicle are
                    // first-class fields validated by the loader and surfaced in
                    // the Centre + Excel export, but the engine never read them —
                    // so healthcare / education / office density rules silently
                    // collapsed to one fixture per room. cap = max across all
                    // populated rates (a rule may set several; the binding one
                    // wins).
                    int byArea = 0;
                    if (rule.PerAreaM2 > 0)
                    {
                        double areaM2 = 0;
                        try { areaM2 = room.Area * 0.3048 * 0.3048; } catch { }
                        if (areaM2 > 0) byArea = Math.Max(1, (int)Math.Ceiling(areaM2 / rule.PerAreaM2));
                    }

                    string occParam = string.IsNullOrEmpty(rule.OccupancyParamName)
                        ? "STING_OCC_COUNT_INT" : rule.OccupancyParamName;
                    int byOcc    = CountFromRoomRate(room, occParam,                     rule.PerOccupant);
                    int byBed    = CountFromRoomRate(room, "STING_BED_COUNT_INT",            rule.PerBed);
                    int byWs     = CountFromRoomRate(room, "STING_WS_COUNT_INT",             rule.PerWorkstation);
                    int byPupil  = CountFromRoomRate(room, "STING_PUPIL_COUNT_INT",          rule.PerPupil);
                    int byCubicle= CountFromRoomRate(room, "STING_TOILET_CUBICLE_COUNT_INT", rule.PerToiletCubicle);

                    cap = Math.Max(byArea,
                          Math.Max(byOcc,
                          Math.Max(byBed,
                          Math.Max(byWs,
                          Math.Max(byPupil, byCubicle)))));

                    // Phase 139.4 — Density rule with no derivable rate falls
                    // through with cap=1; the rule-loader validation pass warns
                    // the user the rule is misconfigured.
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
            // desiredCap = how many we wanted before candidate availability clamps it.
            desiredCap = cap;
            return Math.Min(cap, candidateCount);
        }

        /// <summary>
        /// Phase 188 (review pass-2 #3) — greedy spacing-aware selection. Walks
        /// the ranked candidates and accepts up to <paramref name="cap"/> whose
        /// position clears <paramref name="minSpacingMm"/> centre-to-centre from
        /// every already-accepted candidate. minSpacingMm &lt;= 0 ⇒ plain Take(cap).
        /// </summary>
        private static List<PlacementCandidate> SelectWithSpacing(
            List<PlacementCandidate> ranked, int cap, double minSpacingMm)
        {
            if (ranked == null || cap <= 0) return new List<PlacementCandidate>();
            if (minSpacingMm <= 0) return ranked.Take(cap).ToList();

            double minFt = minSpacingMm * MmToFt;
            double minSq = minFt * minFt;
            var accepted = new List<PlacementCandidate>(Math.Min(cap, ranked.Count));
            foreach (var c in ranked)
            {
                if (accepted.Count >= cap) break;
                if (c?.Position == null) continue;
                bool tooClose = false;
                foreach (var a in accepted)
                {
                    double dx = a.Position.X - c.Position.X;
                    double dy = a.Position.Y - c.Position.Y;
                    double dz = a.Position.Z - c.Position.Z;
                    if (dx * dx + dy * dy + dz * dz < minSq) { tooClose = true; break; }
                }
                if (!tooClose) accepted.Add(c);
            }
            return accepted;
        }

        /// <summary>
        /// Phase 188 (review fix #2) — read an integer room parameter and
        /// derive a density count = ceil(count / rate). Returns 0 when the
        /// rate is unset (≤ 0), the parameter is missing/empty, or the value
        /// is ≤ 0 — so it contributes nothing to the Math.Max chain.
        /// </summary>
        private static int CountFromRoomRate(SpatialElement room, string paramName, double ratePerUnit)
        {
            if (ratePerUnit <= 0 || room == null || string.IsNullOrEmpty(paramName)) return 0;
            int count = 0;
            try
            {
                var p = room.LookupParameter(paramName);
                if (p != null && p.HasValue && p.StorageType == StorageType.Integer) count = p.AsInteger();
            }
            catch (Exception ex) { StingLog.Warn($"ComputeCap: read room param '{paramName}': {ex.Message}"); }
            return count > 0 ? Math.Max(1, (int)Math.Ceiling(count / ratePerUnit)) : 0;
        }

        /// <summary>
        /// PC-13 — place a single instance of the supplied rule at an explicit
        /// point. Used by CoPlaceWith / RELATIVE_TO. Skips room-scope filters
        /// because the predecessor already validated the room.
        /// </summary>
        // Linked-architecture room path (v1). Scores in the link document's
        // coordinates (so room + wall/door geometry is correct), then transforms
        // each chosen point to host coordinates and places a non-hosted instance
        // on the nearest host level. CoPlaceWith / RELATIVE_TO / wet-zone /
        // orientation / face-hosting are host-path-only here.
        private static void ProcessLinkedRoomRule(
            Document hostDoc, SpatialElement room, PlacementRule rule,
            PlacementScorer linkScorer, Transform toHost,
            Dictionary<string, FamilySymbol> perCategorySymbol,
            PlacementResult result, bool dryRun)
        {
            if (toHost == null) return;
            var diagRoom = result.Diag(rule.MergeKey);
            string roomKey = $"{room.Id}::{SafeRoomName(room)}";

            List<PlacementCandidate> candidates;
            try { candidates = linkScorer.Score(room, rule, new List<XYZ>(), 0); }
            catch (Exception ex) { result.Warnings.Add($"[linked {SafeRoomName(room)}] score: {ex.Message}"); return; }
            if (candidates == null || candidates.Count == 0) return;
            result.CandidatesEvaluated += candidates.Count;

            // A11 — surface anchor fallbacks on the linked path too.
            var lmiss = linkScorer.LastAnchorMisses;
            if (lmiss != null && lmiss.Count > 0 && diagRoom != null)
            {
                diagRoom.AnchorMissRooms++;
                if (string.IsNullOrEmpty(diagRoom.FirstAnchorMiss)) diagRoom.FirstAnchorMiss = lmiss[0];
            }

            int cap = ComputeCap(rule, room, candidates.Count, 0, out int linkDesired);
            if (cap == 0) return;
            if (linkDesired > candidates.Count && diagRoom != null)
            {
                diagRoom.UnderFilledRooms++;
                diagRoom.UnderFillShortfall += (linkDesired - candidates.Count);
                if (string.IsNullOrEmpty(diagRoom.FirstUnderFill))
                    diagRoom.FirstUnderFill = $"cap {linkDesired} vs {candidates.Count} candidate(s)";
            }
            var chosen = SelectWithSpacing(candidates, cap, rule.MinSpacingMm);
            if (chosen.Count == 0) return;

            if (dryRun)
            {
                result.CountsByRule[rule.MergeKey] =
                    result.CountsByRule.TryGetValue(rule.MergeKey, out var dn) ? dn + chosen.Count : chosen.Count;
                return;
            }

            var symbol = ResolveSymbol(hostDoc, rule.CategoryFilter, rule, perCategorySymbol, result);
            if (symbol == null) return;
            try { if (!symbol.IsActive) { symbol.Activate(); hostDoc.Regenerate(); } }
            catch (Exception ex) { result.Warnings.Add($"[linked] activate {symbol.Name}: {ex.Message}"); return; }

            var placedHost = new List<XYZ>();
            double dedupFt = Math.Max(rule.ToleranceMm, 25.0) * MmToFt;
            double dedupSq = dedupFt * dedupFt;

            foreach (var c in chosen)
            {
                if (c?.Position == null) continue;
                XYZ hostPos = toHost.OfPoint(c.Position);

                bool tooClose = false;
                foreach (var p in placedHost)
                {
                    double dx = p.X - hostPos.X, dy = p.Y - hostPos.Y, dz = p.Z - hostPos.Z;
                    if (dx * dx + dy * dy + dz * dz < dedupSq) { tooClose = true; break; }
                }
                if (!tooClose && _priorPlaced != null)
                    foreach (var p in _priorPlaced)
                    {
                        if (p == null) continue;
                        double dx = p.X - hostPos.X, dy = p.Y - hostPos.Y, dz = p.Z - hostPos.Z;
                        if (dx * dx + dy * dy + dz * dz < dedupSq) { tooClose = true; break; }
                    }
                if (tooClose) { result.SkippedCount++; if (diagRoom != null) diagRoom.CandidatesRejectedDedup++; continue; }

                Level lvl = NearestHostLevel(hostDoc, hostPos.Z);
                if (lvl == null) { result.Warnings.Add("[linked] no host Level found — cannot place."); result.SkippedCount++; continue; }

                try
                {
                    var fi = hostDoc.Create.NewFamilyInstance(
                        hostPos, symbol, lvl, StructuralType.NonStructural);
                    if (fi == null) { result.SkippedCount++; continue; }
                    WriteAnchorParameters(fi, rule);
                    if (StingTools.Commands.Placement.PlaceFixturesOptions.StampProvenance)
                        try { StingTools.Core.Storage.StingProvenanceSchema.Stamp(fi, "FixturePlacementEngine", rule?.MergeKey ?? ""); }
                        catch (Exception pvEx) { result.Warnings.Add($"[linked] provenance: {pvEx.Message}"); }
                    result.PlacedIds.Add(fi.Id);
                    result.CountsByRule[rule.MergeKey] = result.CountsByRule.TryGetValue(rule.MergeKey, out var n) ? n + 1 : 1;
                    result.CountsByRoom[roomKey] = result.CountsByRoom.TryGetValue(roomKey, out var m) ? m + 1 : 1;
                    if (diagRoom != null) diagRoom.CandidatesPlaced++;
                    placedHost.Add(hostPos);
                    try { PostPlacementHooks.RunFor(fi, rule); }
                    catch (Exception hkEx) { result.Warnings.Add($"[linked] post-hook {fi.Id}: {hkEx.Message}"); }
                }
                catch (Exception ex) { result.Warnings.Add($"[linked place {rule.CategoryFilter}] {ex.Message}"); result.SkippedCount++; }
            }
        }

        private static void ProcessRoomRuleAtPoint(
            Document doc,
            SpatialElement room,
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
                // Tier 1 — regen so FacingOrientation is valid before aligning.
                try { doc.Regenerate(); } catch { }
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
                try { PostPlacementHooks.RunFor(pf.Placed, rule); } catch (Exception hkEx) { result.Warnings.Add($"Post-placement hook (co-place): {hkEx.Message}"); }
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
            string bicHint = rule?.CategoryBic ?? "";
            string cacheKey = string.IsNullOrEmpty(hint) && string.IsNullOrEmpty(ftrx) && string.IsNullOrEmpty(bicHint)
                ? categoryName
                : $"{categoryName}|{hint}|{ftrx}|{bicHint}";
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
                // Phase 188 (review fix #5b) — when the rule sets CategoryBic,
                // resolve the BuiltInCategory directly and match on the category
                // id (locale-robust) instead of the localized Category.Name.
                BuiltInCategory bic = BuiltInCategory.INVALID;
                bool useBic = false;
                if (!string.IsNullOrEmpty(bicHint)
                    && Enum.TryParse<BuiltInCategory>(bicHint, true, out var parsedBic)
                    && parsedBic != BuiltInCategory.INVALID)
                {
                    bic = parsedBic;
                    useBic = true;
                }
                else
                {
                    try { bic = ResolveBuiltInCategoryByName(doc, categoryName); } catch { }
                }
                FilteredElementCollector collector = (bic != BuiltInCategory.INVALID)
                    ? new FilteredElementCollector(doc).OfCategory(bic).OfClass(typeof(FamilySymbol))
                    : new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol));
                foreach (var el in collector)
                {
                    if (!(el is FamilySymbol fs)) continue;
                    if (fs.Category == null) continue;
                    if (useBic)
                    {
                        if ((BuiltInCategory)fs.Category.Id.Value != bic) continue;
                    }
                    else if (!string.Equals(fs.Category.Name, categoryName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // FamilyTypeRegex is an additional gate, applied to symbol name.
                    if (typeRx != null && !typeRx.IsMatch(fs.Name ?? "")) continue;

                    if (firstForCategory == null) firstForCategory = fs;
                    // VariantHint resolves against the STING_FIXTURE_VARIANT_TXT
                    // param when present AND against the TYPE NAME. Seed-minted
                    // variants name the type after the variant (SOCKET_1G) but do
                    // NOT bind that param (it isn't a registered shared parameter),
                    // so name-matching is what actually makes VariantHint resolve
                    // against seed families — without this, every VariantHint-only
                    // rule fell through to the first symbol (wrong type).
                    string variant = fs.LookupParameter("STING_FIXTURE_VARIANT_TXT")?.AsString() ?? "";
                    string variantName = fs.Name ?? "";

                    if (chain.Count > 0)
                    {
                        for (int i = 0; i < chain.Count && i < bestChainIndex; i++)
                        {
                            if (string.Equals(variant, chain[i], StringComparison.OrdinalIgnoreCase)
                                || string.Equals(variantName, chain[i], StringComparison.OrdinalIgnoreCase))
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
                        if (variantRx.IsMatch(variant) || variantRx.IsMatch(variantName))
                        {
                            picked = fs;
                            goto done;
                        }
                    }
                    else if (!string.IsNullOrEmpty(hint))
                    {
                        if (string.Equals(variant, hint, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(variantName, hint, StringComparison.OrdinalIgnoreCase))
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
            // can still be served by the engine. Phase 185: when the rule
            // sets TypeCatalogKey, the loader only mints the matching type
            // (avoids loading 200-type fitting libraries).
            if (picked == null && firstForCategory == null)
            {
                picked = TryAutoLoadFromLibrary(doc, categoryName, hint, result, rule?.TypeCatalogKey ?? "");
                firstForCategory = picked;
            }

            // Item 1 — seed tier. When neither a loaded family nor the on-disk
            // library produced a symbol, fall back to the rule's category→seed
            // mapping (STING_CATEGORY_TO_SEED_MAP). The EnsureSeeds pre-pass
            // normally builds+loads the seed before the engine runs (so the
            // loaded-family tier above already found it); this tier additionally
            // loads a seed .rfa that exists on disk but isn't loaded yet (e.g.
            // a command path that skipped the pre-pass). Building a seed from
            // JSON is intentionally NOT done here — that belongs in the pre-pass,
            // outside the engine's transaction.
            if (picked == null)
            {
                try
                {
                    string seedId = CategoryToSeedRegistry.Resolve(doc, categoryName);
                    if (!string.IsNullOrWhiteSpace(seedId))
                    {
                        picked = TryResolveSeedSymbol(doc, categoryName, seedId, result);
                        if (picked != null) firstForCategory = picked;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"ResolveSymbol seed tier '{categoryName}': {ex.Message}"); }
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
        private static FamilySymbol TryAutoLoadFromLibrary(
            Document doc, string categoryName, string hint, PlacementResult result, string typeCatalogKey = "")
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
                        // Phase 185 — if the rule specifies a type-catalog key
                        // AND a .txt sidecar exists, try the catalog path first
                        // so only the matching type loads (avoids 200-type bloat).
                        if (!string.IsNullOrEmpty(typeCatalogKey))
                        {
                            var catSym = TryLoadFromCatalog(doc, path, typeCatalogKey, categoryName, result);
                            if (catSym != null) return catSym;
                            // fall through to bulk LoadFamily — catalog miss is non-fatal
                        }

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
                            result.Warnings.Add($"Auto-loaded '{System.IO.Path.GetFileName(path)}' for category '{categoryName}'.");
                            return first;
                        }
                    }
                    catch (Exception lex) { StingLog.Warn($"PC-16 LoadFamily {path}: {lex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"PC-16 TryAutoLoadFromLibrary: {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// Item 1 — resolve a STING seed family for the rule's category. First
        /// looks for an already-loaded family named <paramref name="seedId"/>
        /// (handles the case where a FamilyTypeRegex filtered the tier-1 search
        /// to nothing); otherwise loads the on-disk seed
        /// <c>&lt;project&gt;/_BIM_COORD/Families/Seeds/&lt;seedId&gt;.rfa</c> if
        /// it exists (transaction-safe — the engine already owns a transaction).
        /// Returns null (with a build-me warning) when the seed has not been
        /// built — the caller then surfaces the normal SkippedNoSymbol path.
        /// Never builds a seed from JSON here (that is the pre-pass's job).
        /// </summary>
        private static FamilySymbol TryResolveSeedSymbol(
            Document doc, string categoryName, string seedId, PlacementResult result)
        {
            // 1. Already-loaded seed family (by family name == seedId).
            try
            {
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)))
                {
                    if (!(el is FamilySymbol fs) || fs.Category == null) continue;
                    if (!string.Equals(fs.Category.Name, categoryName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(fs.Family?.Name, seedId, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Warnings.Add($"Used STING seed '{seedId}' for category '{categoryName}' — swap to a manufacturer family later (Placement › Swap to Manufacturer).");
                        return fs;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"TryResolveSeedSymbol scan '{seedId}': {ex.Message}"); }

            // 2. Load the seed .rfa from disk if it has been built.
            try
            {
                string seedPath = System.IO.Path.Combine(
                    SeedEnsurer.ResolveSeedOutputFolder(doc), seedId + ".rfa");
                if (System.IO.File.Exists(seedPath))
                {
                    if (doc.LoadFamily(seedPath, out var fam) && fam != null)
                    {
                        foreach (var symId in fam.GetFamilySymbolIds())
                        {
                            if (doc.GetElement(symId) is FamilySymbol fs && fs.Category != null
                                && string.Equals(fs.Category.Name, categoryName, StringComparison.OrdinalIgnoreCase))
                            {
                                result.Warnings.Add($"Loaded STING seed '{seedId}' from disk for category '{categoryName}' — swap to a manufacturer family later.");
                                return fs;
                            }
                        }
                        // family loaded but no symbol of the rule's category — fall through
                    }
                }
                else
                {
                    string key = $"SeedNotBuilt:{seedId}";
                    if (!result.Warnings.Any(w => w.StartsWith(key, StringComparison.Ordinal)))
                        result.Warnings.Add($"{key} — category '{categoryName}' maps to seed '{seedId}' but it isn't built. Run Placement › Ensure Seeds (or Build Seed Families) to place defaults when no manufacturer family is loaded.");
                }
            }
            catch (Exception ex) { StingLog.Warn($"TryResolveSeedSymbol load '{seedId}': {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// Phase 185 — footprint-aware spacing. When a rule sets
        /// <see cref="PlacementRule.FamilyBboxAware"/>, read the resolved
        /// symbol's bounding-box footprint (max of X / Y extent in mm)
        /// and rebuild the rule with spacing fields scaled to it.
        /// scale = clamp( max(footprintMm, MinSymbolFootprintMm) /
        ///                 ReferenceFootprintMm,
        ///                1.0, MaxFootprintScale ).
        /// Lower bound is 1.0 — bbox-aware never *shrinks* spacings below
        /// the rule's authored value, only grows them when the real
        /// family is bigger than the reference.
        /// </summary>
        private static PlacementRule ScaleByFootprint(
            PlacementRule rule,
            FamilySymbol symbol,
            PlacementResult result)
        {
            if (rule == null || symbol == null) return rule;
            try
            {
                double footprintMm = MeasureSymbolFootprintMm(symbol);
                double floorMm = Math.Max(1.0, rule.MinSymbolFootprintMm);
                double refMm   = Math.Max(1.0, rule.ReferenceFootprintMm);
                double cap     = Math.Max(1.0, rule.MaxFootprintScale);
                double scale = Math.Max(footprintMm, floorMm) / refMm;
                if (scale < 1.0) scale = 1.0;           // never shrink
                if (scale > cap) scale = cap;           // never explode
                if (Math.Abs(scale - 1.0) < 1e-6) return rule; // nothing to do

                var scaled = rule.Clone();
                scaled.MinSpacingMm           *= scale;
                scaled.CoverageRadiusMm       *= scale;
                scaled.ObstructionClearanceMm *= scale;
                scaled.WallClearanceMm        *= scale;
                // Offsets scale too — a 1200 mm AHU placed with a 50 mm
                // wall offset is wrong; the offset should also be ~8× larger.
                scaled.OffsetXMm *= scale;
                scaled.OffsetYMm *= scale;
                // OffsetZMm is intentionally NOT scaled — mounting height
                // is set by MountingHeightMm and MountingReference, not
                // by the family footprint.

                result.Warnings.Add(
                    $"Phase 185 footprint-aware: rule '{rule.RuleId}' / category " +
                    $"'{rule.CategoryFilter}' scaled spacings ×{scale:F2} " +
                    $"(footprint {footprintMm:F0} mm vs reference {refMm:F0} mm).");
                return scaled;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Phase 185 footprint scale for '{rule?.RuleId}': {ex.Message}");
                return rule;
            }
        }

        /// <summary>
        /// Max of the symbol's plan bounding-box extent (mm). Falls back to
        /// the family's own bbox via Symbol-then-Family probing.
        /// </summary>
        private static double MeasureSymbolFootprintMm(FamilySymbol symbol)
        {
            try
            {
                BoundingBoxXYZ bb = symbol?.get_BoundingBox(null);
                if (bb != null && bb.Max != null && bb.Min != null)
                {
                    double dx = Math.Abs(bb.Max.X - bb.Min.X);
                    double dy = Math.Abs(bb.Max.Y - bb.Min.Y);
                    double footFt = Math.Max(dx, dy);
                    return footFt * 304.8; // ft → mm
                }
            }
            catch { }
            return 0.0;
        }

        /// <summary>
        /// Phase 185 — type-catalog loader. When a rule sets
        /// <see cref="PlacementRule.TypeCatalogKey"/> and a `.txt` sidecar
        /// exists next to the `.rfa`, only the matching type is loaded —
        /// avoids bloating the project with 200-type valve / fittings
        /// libraries.
        ///
        /// Returns the loaded FamilySymbol, or null when:
        /// - TypeCatalogKey is empty (caller falls through to bulk LoadFamily)
        /// - No `.txt` sidecar exists (caller falls through to bulk LoadFamily)
        /// - No catalog row matches (caller falls through, with a warning)
        ///
        /// Catalog format (Revit standard, comma-delimited):
        ///   ,Param1##type##unit,Param2##type##unit
        ///   Type-A,value1,value2
        ///   Type-B,value3,value4
        /// First column = type name. Header row identified by leading comma.
        /// </summary>
        private static FamilySymbol TryLoadFromCatalog(
            Document doc,
            string rfaPath,
            string typeCatalogKey,
            string categoryName,
            PlacementResult result)
        {
            if (string.IsNullOrEmpty(typeCatalogKey)) return null;
            if (string.IsNullOrEmpty(rfaPath)) return null;
            string txtPath;
            try
            {
                string dir  = Path.GetDirectoryName(rfaPath) ?? "";
                string name = Path.GetFileNameWithoutExtension(rfaPath) ?? "";
                txtPath = Path.Combine(dir, name + ".txt");
            }
            catch { return null; }
            if (!File.Exists(txtPath)) return null;

            // Resolve the type name to load.
            string matchedType = ResolveCatalogType(txtPath, typeCatalogKey, result);
            if (string.IsNullOrEmpty(matchedType))
            {
                result.Warnings.Add(
                    $"Type catalog '{Path.GetFileName(txtPath)}': no type matches " +
                    $"TypeCatalogKey='{typeCatalogKey}' — falling back to bulk LoadFamily.");
                return null;
            }

            try
            {
                FamilySymbol loaded;
                if (doc.LoadFamilySymbol(rfaPath, matchedType, out loaded) && loaded != null)
                {
                    if (loaded.Category != null &&
                        !string.IsNullOrEmpty(categoryName) &&
                        !string.Equals(loaded.Category.Name, categoryName, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Warnings.Add(
                            $"Type catalog load: '{matchedType}' from '{Path.GetFileName(rfaPath)}' " +
                            $"resolved category '{loaded.Category.Name}', expected '{categoryName}'.");
                    }
                    result.Warnings.Add(
                        $"Phase 185 type-catalog: loaded '{matchedType}' from " +
                        $"'{Path.GetFileName(rfaPath)}' (key='{typeCatalogKey}').");
                    return loaded;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Phase 185 LoadFamilySymbol '{matchedType}' from '{rfaPath}': {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Read the type-catalog .txt and return the first type-name (first
        /// column of a non-header row) whose name matches the key — either
        /// exact case-insensitive, or via regex when the key looks regex-like.
        /// </summary>
        private static string ResolveCatalogType(string txtPath, string key, PlacementResult result)
        {
            try
            {
                bool regexMode = IsRegexLike(key);
                System.Text.RegularExpressions.Regex rx = null;
                if (regexMode)
                {
                    try { rx = new System.Text.RegularExpressions.Regex(key, System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
                    catch (Exception ex) { result?.Warnings.Add($"TypeCatalogKey regex '{key}': {ex.Message}"); return null; }
                }
                foreach (var raw in File.ReadAllLines(txtPath))
                {
                    if (string.IsNullOrEmpty(raw)) continue;
                    // Header row starts with comma (no type name).
                    if (raw.StartsWith(",")) continue;
                    // First field = type name.
                    int comma = raw.IndexOf(',');
                    string typeName = (comma >= 0 ? raw.Substring(0, comma) : raw).Trim();
                    if (string.IsNullOrEmpty(typeName)) continue;
                    if (regexMode)
                    {
                        if (rx != null && rx.IsMatch(typeName)) return typeName;
                    }
                    else if (string.Equals(typeName, key, StringComparison.OrdinalIgnoreCase))
                    {
                        return typeName;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveCatalogType '{txtPath}': {ex.Message}"); }
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
            // Phase 188 (review pass-2 #5) — tightened so literal variant/type
            // names aren't misclassified as regex. A lone '$' or an unbalanced
            // '[' (e.g. a literal type "A[1") no longer trips regex mode; we now
            // require a real anchor / escape / quantifier / balanced char-class.
            return s.StartsWith("^", StringComparison.Ordinal)
                || s.EndsWith("$", StringComparison.Ordinal)
                || s.Contains("\\")                                   // escape (\d, \w, \., …)
                || s.Contains(".*") || s.Contains(".+") || s.Contains(".?")
                || (s.Contains("[") && s.Contains("]"))               // balanced char-class
                || (s.Contains("(") && s.Contains(")"));              // group
        }

        private static void WriteAnchorParameters(FamilyInstance fi, PlacementRule rule)
        {
            TrySetString(fi, ParamRegistry.PLACE_ANCHOR, rule.AnchorType);
            TrySetDoubleMm(fi, ParamRegistry.PLACE_OFFSET_X_MM, rule.OffsetXMm);
            TrySetString(fi, ParamRegistry.PLACE_SIDE, rule.SideConstraint);

            // MNT_HGT_MM may be absent on some families; swallow failure.
            TrySetDoubleMm(fi, "MNT_HGT_MM", rule.MountingHeightMm);

            // Phase 188 (review pass-2 #6) — persist the rest of the placement
            // intent so audit / round-trip captures the full transform, not just
            // X-offset. These shared params may not be bound (no registry
            // constant); TrySet* no-ops gracefully when the param is absent, and
            // activates automatically once a project binds them.
            TrySetDoubleMm(fi, "ASS_PLACE_OFFSET_Y_MM", rule.OffsetYMm);
            TrySetDoubleMm(fi, "ASS_PLACE_OFFSET_Z_MM", rule.OffsetZMm);
            TrySetDoubleMm(fi, "ASS_PLACE_ROTATION_DEG", rule.RotationDeg);
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
        private static void OrientPlacedInstance(Document doc, FamilyInstance fi, PlacementRule rule, SpatialElement room)
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

                // Phase 195 — ceiling / grid fixtures: when the rule didn't pin a
                // RotationDeg, snap the instance's facing to the nearest 90° so
                // luminaires sit orthogonal to an axis-aligned room instead of at
                // the family's default diagonal angle (the "placed anyhow" look).
                // Cosmetic + undo-safe; only moves genuinely off-axis fixtures.
                bool ceilingAnchor = anchor == "CEILING_CENTRE" || anchor == "LIGHTING_GRID"
                                  || anchor == "LUX_GRID" || anchor.StartsWith("CEILING_TILE")
                                  || anchor == "RAISED_FLOOR_TILE_EDGE";
                if (ceilingAnchor && Math.Abs(rule.RotationDeg) < 0.001
                    && fi.Location is LocationPoint lpCeil && lpCeil.Point != null)
                {
                    try
                    {
                        XYZ f = fi.FacingOrientation;
                        if (f != null && !f.IsZeroLength())
                        {
                            double ang = Math.Atan2(f.Y, f.X);
                            double quarter = Math.PI / 2.0;
                            double snapped = Math.Round(ang / quarter) * quarter;
                            double delta = snapped - ang;
                            while (delta > Math.PI) delta -= 2 * Math.PI;
                            while (delta <= -Math.PI) delta += 2 * Math.PI;
                            if (Math.Abs(delta) > 0.02)
                            {
                                var axis = Line.CreateBound(lpCeil.Point, lpCeil.Point + XYZ.BasisZ);
                                ElementTransformUtils.RotateElement(doc, fi.Id, axis, delta);
                            }
                        }
                    }
                    catch (Exception ceilEx) { StingLog.Warn($"OrientPlacedInstance ceiling-snap {fi.Id}: {ceilEx.Message}"); }
                }

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
                    if (room != null && PointInSpatial(room, probe)) roomOnPositiveNormal = true;
                }
                catch (Exception ex) { StingLog.Warn($"[FixturePlacementEngine] Room-side test for facing flip: {ex.Message}"); }

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
        private static double ComputeRoomPerimeterMetres(SpatialElement room)
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

        private static int ReadRoomIntParam(SpatialElement room, string paramName)
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
            catch (Exception ex) { StingLog.Warn($"[FixturePlacementEngine] ReadRoomIntParam: {ex.Message}"); }
            return 0;
        }

        private static string SafeRoomName(SpatialElement room)
        {
            try
            {
                var p = room.get_Parameter(BuiltInParameter.ROOM_NAME);
                return p?.AsString() ?? room.Name ?? "";
            }
            catch (Exception ex) { StingLog.Warn($"[FixturePlacementEngine] SafeRoomName lookup: {ex.Message}"); return ""; }
        }

        // Phase 139.4 — resolve a Document.Settings.Categories entry to its
        // BuiltInCategory enum so FilteredElementCollector.OfCategory can
        // pre-filter family symbols. Cached per document on first hit.
        // Phase 139.27 (N-03) — key by PathName|Title not GetHashCode(),
        // same hash-collision concern as PlacementHostPreflight.ResolveView3D.
        private static readonly Dictionary<string, Dictionary<string, BuiltInCategory>> _bicByName
            = new Dictionary<string, Dictionary<string, BuiltInCategory>>(StringComparer.Ordinal);
        private static readonly object _bicByNameLock = new object();

        // Phase 188 (review fix #1b) — promoted from private to internal so
        // PlacementScorer can reuse the cached category-name → BuiltInCategory
        // resolution for its sample-instance prefilter.
        internal static BuiltInCategory ResolveBuiltInCategoryByName(Document doc, string categoryName)
        {
            if (doc == null || string.IsNullOrEmpty(categoryName)) return BuiltInCategory.INVALID;
            string path = "", title = "";
            try { path = doc.PathName ?? ""; } catch (Exception ex) { StingLog.Warn($"[FixturePlacementEngine] Read doc.PathName for BIC cache key: {ex.Message}"); }
            try { title = doc.Title ?? ""; } catch (Exception ex) { StingLog.Warn($"[FixturePlacementEngine] Read doc.Title for BIC cache key: {ex.Message}"); }
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
                            catch (Exception ex2) { StingLog.Warn($"[FixturePlacementEngine] Category.Id -> BuiltInCategory cast: {ex2.Message}"); }
                        }
                    }
                    catch (Exception ex2) { StingLog.Warn($"ResolveBuiltInCategoryByName: {ex2.Message}"); }
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
                try { fi = doc.GetElement(id) as FamilyInstance; } catch (Exception ex) { StingLog.Warn($"[FixturePlacementEngine] StampNoggin GetElement({id.Value}): {ex.Message}"); }
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
                try { fi = doc.GetElement(id) as FamilyInstance; } catch (Exception ex2) { StingLog.Warn($"[FixturePlacementEngine] StructuralAudit GetElement({id.Value}): {ex2.Message}"); }
                if (fi == null) continue;
                XYZ p = (fi.Location as LocationPoint)?.Point;
                if (p == null) continue;
                bool nearStructure = false;
                try { nearStructure = sa.IsNearJunction(p, clearanceFt); } catch (Exception ex3) { StingLog.Warn($"[FixturePlacementEngine] StructuralAwareness.IsNearJunction: {ex3.Message}"); }
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
        /// <summary>
        /// Bridge to the drop-routing engines: per rule, run the engine that
        /// matches RoutingMode — AutoConduitDrop (AUTO_CONDUIT), AutoPipeDrop
        /// (AUTO_PIPE) or AutoDuctDrop (AUTO_DUCT) — across every fixture
        /// placed under that rule. Returns the number of elements the drop
        /// engines successfully created. Errors are surfaced into
        /// result.Warnings so the placement-result panel still renders
        /// — a routing failure must never abort the placement run.
        ///
        /// The routing options are read from the rule:
        ///   - InstallMethod:  CHASED / CLIPPED / TRAY / EMBEDDED / IN-FLOOR
        ///                     (drives the drop engine's UseChaseRouting)
        ///   - NominalDiameterMm: feeds the chase-depth check
        /// </summary>
        private static int RouteAfterPlacement(
            Document doc, IList<PlacementRule> autoConduitRules, PlacementResult result)
        {
            if (doc == null || autoConduitRules == null || autoConduitRules.Count == 0) return 0;
            int totalRouted = 0;

            // Resolve placed fixtures back to their rule via Diagnostics.
            // Two passes: per-rule we know how many fixtures fired, then
            // we hand the rule's full placed-id list to the drop engine.
            foreach (var rule in autoConduitRules)
            {
                List<Element> fixtures = new List<Element>();
                foreach (var id in result.PlacedIds)
                {
                    var el = doc.GetElement(id);
                    if (el == null) continue;
                    // Tie the fixture back to its rule. Provenance (stamped with
                    // rule.MergeKey) is the authoritative link; fall back to the
                    // ASS_PLACEMENT_RULE_TXT param when a project stamps it.
                    // NOTE: the previous code matched the never-written
                    // ASS_PLACEMENT_RULE_TXT param against rule.RuleId, so AUTO
                    // routing matched zero fixtures.
                    string provRule = null;
                    try { provRule = StingTools.Core.Storage.StingProvenanceSchema.Read(el)?.RuleId; }
                    catch { }
                    string fxRule = ParameterHelpers.GetString(el, "ASS_PLACEMENT_RULE_TXT");
                    if (string.Equals(provRule, rule.MergeKey, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(fxRule, rule.RuleId, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(fxRule, rule.MergeKey, StringComparison.OrdinalIgnoreCase))
                        fixtures.Add(el);
                }
                if (fixtures.Count == 0) continue;

                string rawMode = (rule.RoutingMode ?? "").ToUpperInvariant();
                string mode = EffectiveRoutingMode(rule);
                if (mode == "NONE") continue;
                if (rawMode != mode)
                    result.Warnings.Add($"[{rule.RuleId}] RoutingMode '{rawMode}' has no dedicated follower engine — routed via {mode} (drop) instead. Set RoutingMode to {mode} to silence this notice.");
                try
                {
                    StingTools.Core.Routing.DropResult dr;
                    switch (mode)
                    {
                        case "AUTO_PIPE":
                            dr = new StingTools.Core.Routing.AutoPipeDrop(doc).Execute(fixtures);
                            break;
                        case "AUTO_DUCT":
                            dr = new StingTools.Core.Routing.AutoDuctDrop(doc).Execute(fixtures);
                            break;
                        case "AUTO_CONDUIT":
                        default:
                            bool isChased = string.Equals(rule.MountingContext, "CHASED",
                                StringComparison.OrdinalIgnoreCase);
                            dr = new StingTools.Core.Routing.AutoConduitDrop(doc)
                            {
                                ServiceId = "ELC_PWR",
                                InstallMethod = isChased ? "CHASED" : "CLIPPED",
                                UseChaseRoutingWhenAvailable = isChased,
                                UsePathfinder = false,    // opt-in flag; placement bridge stays
                                                          // on the safe L/Z path until the host
                                                          // project explicitly requests A*.
                            }.Execute(fixtures);
                            break;
                    }
                    totalRouted += dr.CreatedIds.Count;
                    foreach (var w in dr.Warnings)
                        result.Warnings.Add($"[{rule.RuleId}] {w}");
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"[{rule.RuleId}] auto-route ({mode}): {ex.Message}");
                }
            }

            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"[FixturePlacementEngine] ComplianceScan.InvalidateCache: {ex.Message}"); }
            return totalRouted;
        }

        /// <summary>
        /// Scan the document for fixtures placed by previous STING runs and
        /// return their plan positions. Detection is via the STING provenance
        /// entity (authoritative, binding-independent) with the
        /// ASS_PLACEMENT_RULE_TXT param as a secondary signal. Used to make
        /// re-runs idempotent.
        /// </summary>
        private static List<XYZ> BuildPriorPlacedIndex(Document doc, ICollection<BuiltInCategory> cats)
        {
            var pts = new List<XYZ>();
            if (doc == null) return pts;
            try
            {
                // Narrow the scan to the categories the active rules place into so a
                // large model isn't walked in full every run. Falls back to all
                // family instances when no category could be resolved, or if the
                // category set isn't a valid multicategory filter.
                FilteredElementCollector col = null;
                if (cats != null && cats.Count > 0)
                {
                    try
                    {
                        col = new FilteredElementCollector(doc)
                            .WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(cats)))
                            .WhereElementIsNotElementType();
                    }
                    catch (Exception fex) { StingLog.Warn($"BuildPriorPlacedIndex multicat filter: {fex.Message}"); col = null; }
                }
                if (col == null)
                    col = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilyInstance))
                        .WhereElementIsNotElementType();
                foreach (var el in col)
                {
                    bool sting = false;
                    try { sting = StingTools.Core.Storage.StingProvenanceSchema.IsAutoCreated(el); }
                    catch { }
                    if (!sting && string.IsNullOrEmpty(ParameterHelpers.GetString(el, "ASS_PLACEMENT_RULE_TXT")))
                        continue;
                    if ((el.Location as LocationPoint)?.Point is XYZ p) { pts.Add(p); continue; }
                    // Face / work-plane-hosted instances have no LocationPoint —
                    // fall back to the instance transform origin so they're still
                    // de-duped on re-run.
                    if (el is FamilyInstance fiHosted)
                    {
                        try { var t = fiHosted.GetTransform(); if (t?.Origin != null) pts.Add(t.Origin); }
                        catch { }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildPriorPlacedIndex: {ex.Message}"); }
            return pts;
        }

        /// <summary>
        /// Distinct BuiltInCategories the active rules place into, resolved from
        /// each rule's CategoryBic (enum name) or CategoryFilter (display name).
        /// Used to scope the idempotency scan; empty ⇒ caller scans all instances.
        /// </summary>
        private static List<BuiltInCategory> ResolvePriorPlacedCategories(Document doc, IList<PlacementRule> rules)
        {
            var set = new HashSet<BuiltInCategory>();
            if (rules == null) return new List<BuiltInCategory>();
            foreach (var r in rules)
            {
                if (r == null) continue;
                BuiltInCategory bic = BuiltInCategory.INVALID;
                if (!string.IsNullOrEmpty(r.CategoryBic)
                    && Enum.TryParse<BuiltInCategory>(r.CategoryBic, true, out var parsed)
                    && parsed != BuiltInCategory.INVALID)
                    bic = parsed;
                else if (!string.IsNullOrEmpty(r.CategoryFilter))
                    try { bic = ResolveBuiltInCategoryByName(doc, r.CategoryFilter); } catch { }
                if (bic != BuiltInCategory.INVALID) set.Add(bic);
            }
            return new List<BuiltInCategory>(set);
        }

        /// <summary>
        /// Resolve a rule's RoutingMode to one of the three modes the engine
        /// can actually execute (AUTO_CONDUIT / AUTO_PIPE / AUTO_DUCT) or NONE.
        /// Legacy follow tokens (WALL_FOLLOW / CEILING_FOLLOW / FLOOR_FOLLOW /
        /// CONDUIT_RUN / TRAY_RUN) shipped in older rule packs have no dedicated
        /// follower router, so they map to the drop engine matching the rule's
        /// RouteSegmentCategory — a real route rather than a silent no-op.
        /// </summary>
        private static string EffectiveRoutingMode(PlacementRule r)
        {
            string m = (r?.RoutingMode ?? "").ToUpperInvariant();
            switch (m)
            {
                case "AUTO_CONDUIT":
                case "AUTO_PIPE":
                case "AUTO_DUCT":
                    return m;
                case "":
                case "NONE":
                    return "NONE";
                default:
                    // Legacy follow / run tokens → drop engine by segment category.
                    string cat = (r?.RouteSegmentCategory ?? "").ToUpperInvariant();
                    if (cat.Contains("PIPE")) return "AUTO_PIPE";
                    if (cat.Contains("DUCT")) return "AUTO_DUCT";
                    return "AUTO_CONDUIT"; // Conduit / CableTray / unspecified
            }
        }

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

            // Bridge placement → routing. Any rule whose RoutingMode is one of
            // the three real auto-route modes (AUTO_CONDUIT / AUTO_PIPE /
            // AUTO_DUCT) is dispatched to the matching Core/Routing drop engine
            // (AutoConduitDrop / AutoPipeDrop / AutoDuctDrop). NONE is a no-op.
            // The UI dropdown is constrained to exactly these tokens so no
            // visible mode is a silent no-op. The 600 mm connector-join pass
            // below still runs afterwards as a secondary stitch for every
            // routed rule.
            try
            {
                var autoRoutedRules = new List<PlacementRule>();
                if (rules != null)
                {
                    foreach (var r in rules)
                    {
                        if (r == null) continue;
                        if (EffectiveRoutingMode(r) != "NONE")
                            autoRoutedRules.Add(r);
                    }
                }
                if (autoRoutedRules.Count > 0)
                {
                    int routed = RouteAfterPlacement(doc, autoRoutedRules, result);
                    if (routed > 0)
                        result.Warnings.Add($"Auto-routed drops for {routed} placed fixture(s).");
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"RouteAfterPlacement: {ex.Message}");
                result.Warnings.Add($"Auto-route failed: {ex.Message}");
            }

            const double joinRadiusFt = 600.0 / 304.8;
            double radSq = joinRadiusFt * joinRadiusFt;

            // Index connectors per system classification.
            var openConns = new Dictionary<string, List<(Connector c, ElementId id)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in result.PlacedIds)
            {
                FamilyInstance fi = null;
                try { fi = doc.GetElement(id) as FamilyInstance; } catch (Exception ex2) { StingLog.Warn($"[FixturePlacementEngine] AutoJoin GetElement({id.Value}): {ex2.Message}"); }
                if (fi == null) continue;
                var mgr = fi.MEPModel?.ConnectorManager;
                if (mgr == null) continue;
                foreach (Connector c in mgr.Connectors)
                {
                    if (c == null || c.IsConnected) continue;
                    string sysKey = "?";
                    try { sysKey = c.Domain.ToString() + ":" + (c.MEPSystem?.Name ?? c.Description ?? c.Owner?.Category?.Name ?? ""); } catch (Exception ex3) { StingLog.Warn($"[FixturePlacementEngine] Build connector sysKey: {ex3.Message}"); }
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
                    catch (Exception ex2)
                    {
                        failed++;
                        StingLog.Warn($"AutoJoinMepConnectors {aid.Value}->{tid.Value}: {ex2.Message}");
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
            catch (Exception ex) { StingLog.Warn($"[FixturePlacementEngine] SameSize connector compare: {ex.Message}"); return true; }
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
                    catch (Exception ex) { StingLog.Warn($"[FixturePlacementEngine] Read door Width parameter: {ex.Message}"); }
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
                try { fi = doc.GetElement(id) as FamilyInstance; } catch (Exception ex2) { StingLog.Warn($"[FixturePlacementEngine] DoorSwingAudit GetElement({id.Value}): {ex2.Message}"); }
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
            catch (Exception ex) { StingLog.Warn($"[FixturePlacementEngine] PointInsideSweptArc geometry: {ex.Message}"); return false; }
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
                try { fi = doc.GetElement(id) as FamilyInstance; } catch (Exception ex) { StingLog.Warn($"[FixturePlacementEngine] CableBundleAudit GetElement({id.Value}): {ex.Message}"); }
                if (fi == null) continue;
                XYZ p = (fi.Location as LocationPoint)?.Point;
                if (p == null) continue;
                string cat = "(uncategorised)";
                try { cat = fi.Category?.Name ?? "(uncategorised)"; } catch (Exception ex) { StingLog.Warn($"[FixturePlacementEngine] CableBundleAudit category read: {ex.Message}"); }
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

        // Phase 139.4 — resolve a Document.Settings.Categories entry to its
        // BuiltInCategory enum so FilteredElementCollector.OfCategory can
        // pre-filter family symbols. Cached per document on first hit.
    }
}
