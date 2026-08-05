// ClashRunCommand.cs — rec-7. End-to-end headless clash run:
//   MeshExtractor.Extract
//     → ClashKernel.BuildIndexes + Run
//     → ElementFacts hydration (category / system / workset)
//     → ClashMatrix.Match              (rec-6 pre-filter)
//     → ClashRuleEngine.Classify
//     → ClashExclusions.IsExcluded
//     → ClashIdentity.Compute
//     → ClashHistory.MergeWithPrior
//     → ClashGrouper.Group
//     → ResolutionHeuristics.Suggest
//     → ClashPersistence.Save to {output}/clashes.json
//
// Stage 5 acceptance gate: this is the command dispatched by the BCC
// Clash tab's "Run Clash" button and by ClashScheduler's hourly timer.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Planscape.Shared.BCF;
using StingTools.Core;
using StingTools.V6;
using System.Text.RegularExpressions;

namespace StingTools.Core.Clash
{
    // C4: Manual transaction mode so the post-run LiveClashFlag.Apply
    //     write succeeds. Prior ReadOnly mode blocked any nested
    //     Transaction, breaking cold-init flag writes.
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class ClashRunCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiDoc = commandData?.Application?.ActiveUIDocument;
            var doc = uiDoc?.Document;
            if (doc == null) { message = "No active document."; return Result.Failed; }
            try { return ExecuteOnDocument(doc, silent: false, out _); }
            catch (Exception ex)
            {
                StingLog.Error("ClashRunCommand.Execute", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// B3: Headless entry point used by the scheduler's ExternalEvent.
        /// Runs the full pipeline silently (no TaskDialog) on the active
        /// document and returns the run record. Returns null when no active
        /// document, no 3D view, or no tessellatable geometry.
        /// </summary>
        public static ClashRunRecord RunHeadless(UIApplication app)
        {
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) return null;
            try
            {
                ExecuteOnDocument(doc, silent: true, out var run);
                return run;
            }
            catch (Exception ex)
            {
                StingLog.Error("ClashRunCommand.RunHeadless", ex);
                return null;
            }
        }

        private static Result ExecuteOnDocument(Document doc, bool silent, out ClashRunRecord runOut)
        {
            runOut = null;
            try
            {
                if (!(doc.ActiveView is View3D view3d) || view3d.IsTemplate)
                {
                    // Prefer the broadest non-template 3D view: views with no
                    // active section box come first (they see the whole model);
                    // among those with section boxes, pick the largest by
                    // bounding-box volume. Stable secondary order on Name so the
                    // fallback is deterministic. Prior code took the collector's
                    // first match, which on projects with several coordination
                    // views could land on a tiny single-room slice and silently
                    // produce an empty clash run.
                    var fallback = new FilteredElementCollector(doc)
                        .OfClass(typeof(View3D))
                        .Cast<View3D>()
                        .Where(v => !v.IsTemplate)
                        .OrderBy(v => v.IsSectionBoxActive ? 1 : 0)
                        .ThenByDescending(SectionBoxVolumeFt3)
                        .ThenBy(v => v.Name, StringComparer.Ordinal)
                        .FirstOrDefault();
                    if (fallback == null)
                    {
                        if (!silent)
                            TaskDialog.Show("STING Clash",
                                "No 3D view available. Open or create a 3D view to run clash detection.");
                        return Result.Cancelled;
                    }
                    view3d = fallback;
                    if (!silent)
                        StingLog.Info($"ClashRunCommand: active view not 3D, fell back to '{view3d.Name}' (sectionBoxActive={view3d.IsSectionBoxActive})");
                }

                var overall = Stopwatch.StartNew();

                // ── Config / output paths ──
                string outDir = OutputLocationHelper.GetOutputDirectory(doc) ?? Path.GetTempPath();
                Directory.CreateDirectory(outDir);
                string clashesJson  = Path.Combine(outDir, "clashes.json");
                string matrixJson   = FindDataFile("default_clash_matrix.json");
                string rulesJson    = FindDataFile("default_clash_rules.json");
                string exclusionsJson = Path.Combine(outDir, "clash_exclusions.json");

                var matrix = ClashMatrix.LoadOrDefault(matrixJson);
                var rules = ClashRuleLibrary.LoadAugmented(rulesJson);
                var ruleEngine = new ClashRuleEngine(rules);
                var exclusions = ClashExclusions.Load(exclusionsJson);

                // ── Stage 1: extract meshes + build indices + run broad+narrow ──
                var meshes = MeshExtractor.Extract(doc, view3d);
                if (meshes.Count == 0)
                {
                    if (!silent)
                        TaskDialog.Show("STING Clash",
                            "No tessellatable geometry found in the active 3D view.");
                    return Result.Cancelled;
                }

                var kernel = new ClashKernel();
                kernel.BuildIndexes(meshes.Values);
                var hits = kernel.Run();

                // ── rec-6: pre-hydrate ElementFacts so matrix/rule-engine runs O(1) per pair ──
                var factsByKey = BuildFactsByKey(doc, meshes);

                // ── Stage 2: classify, exclude, identity-hash ──
                var run = new ClashRunRecord
                {
                    RunId = Guid.NewGuid().ToString("N").Substring(0, 12),
                    MatrixFile = matrixJson,
                    RulesFile = rulesJson,
                    ExclusionsFile = exclusionsJson,
                };

                int filtered = 0, excluded = 0;
                foreach (var h in hits)
                {
                    factsByKey.TryGetValue(h.A, out var fa);
                    factsByKey.TryGetValue(h.B, out var fb);
                    if (fa == null || fb == null) continue;

                    // rec-6: match the pair to a matrix cell. A pair that doesn't match any
                    // enabled cell is outside the project's configured coordination scope.
                    var cell = matrix.Match(fa, fb);
                    if (cell == null) { filtered++; continue; }

                    var classified = ruleEngine.Classify(h, fa, fb, cell);
                    if (classified.Verdict != ClashVerdict.Keep) { filtered++; continue; }

                    string identity = ClashIdentity.Compute(h.A, h.B, cell.PairId, h.Centroid);
                    // F8: Audited variant logs every exclusion hit to a JSONL
                    //     so ISO 19650 stage gates can show evidence of why a
                    //     clash was suppressed.
                    if (exclusions.IsExcludedAudited(identity, cell.PairId, run.RunId)) { excluded++; continue; }

                    run.Clashes.Add(new ClashRecord
                    {
                        Identity = identity,
                        MatrixPairId = cell.PairId,
                        Severity = cell.Severity ?? "MED",
                        Tolerance = cell.Tolerance ?? "HARD",
                        ElementA = ToRecord(h.A, fa),
                        ElementB = ToRecord(h.B, fb),
                        VolumeMm3 = h.VolumeMm3,
                        AabbMin = new[] { h.AabbMin.X, h.AabbMin.Y, h.AabbMin.Z },
                        AabbMax = new[] { h.AabbMax.X, h.AabbMax.Y, h.AabbMax.Z },
                        Centroid = new[] { h.Centroid.X, h.Centroid.Y, h.Centroid.Z },
                    });
                }

                run.Stats.Raw = hits.Count;
                run.Stats.Tier1Filtered = filtered;
                run.Stats.Excluded = excluded;

                // ── Stage 2: history diff FIRST (preserves Id for matched identity) ──
                // Intentionally before ID mint so id stability is rooted in
                // identity hash, not run sequence. (G1 fix — prior code assigned
                // ids before merge, producing duplicates on same-day re-runs.)
                var prior = ClashPersistence.Load(clashesJson);
                ClashHistory.MergeWithPrior(run, prior);

                // ── A3: Re-evaluate clearance vs hard intersection ──
                // The kernel returns Kind="hard" for every triangle-overlap
                // pair, but a matrix cell with CLEARANCE_50 / CLEARANCE_100
                // tolerates that magnitude of overlap. After merge (so
                // promotion travels with the identity), inspect each kept
                // clash whose cell tolerance starts with "CLEARANCE_":
                //   - parse the suffix as mm,
                //   - read the AABB overlap depth (min of the three extent
                //     components in feet, converted to mm),
                //   - if depth ≤ tolerance, the hit is within clearance —
                //     drop hard tessellation slivers, otherwise promote
                //     Kind to "clearance".
                ApplyClearanceTolerance(run);

                // Seed the today-sequence AFTER merge so we only skip over ids
                // already taken in the archive (prior Resolved clashes) or carried
                // forward by merge onto this run's matching-identity records.
                // Prevents same-day-re-run duplicate CLH-X-NNNNN.
                int seq = SeedSequenceForToday(DateTime.UtcNow, run, prior);

                // Now mint ids for any clash that MergeWithPrior left empty
                // (new-in-this-run). Matching-identity clashes retain their
                // pre-existing Id from the prior run.
                foreach (var c in run.Clashes)
                {
                    if (string.IsNullOrEmpty(c.Id))
                        c.Id = ClashIdentity.NewClashId(DateTime.UtcNow, seq++);
                }

                // ── Stage 4: group + resolution hints ──
                run.Groups = ClashGrouper.Group(run.Clashes);
                run.Stats.Groups = run.Groups.Count;
                foreach (var c in run.Clashes)
                    c.ResolutionHint = ResolutionHeuristics.Suggest(c);

                // ── C1: Stage 5 — triage scoring via ClashTriageEngine ──
                // History-aware: RecurrenceCount counts identity matches in
                // the prior run archive; DismissCount counts state transitions
                // that landed on "Void". Clashes are sorted by TriageScore
                // descending so coordinators see the worst items first.
                ApplyTriageScores(run, prior);

                // ── C2: discipline-owner enrichment — runs after issue
                //        creation in the BCC dispatcher today, but we capture
                //        owner names on the run record now so downstream
                //        consumers (BCC tab, Excel exports) display the same
                //        assignee. The CoordIssue list is created lazily by
                //        ClashSlaIntegration.CreateIssues; the run.Groups
                //        already carry the matrix-cell default at this point.
                EnrichGroupAssignees(doc, run);

                run.DurationMs = overall.ElapsedMilliseconds;

                // ── Persist ──
                ClashPersistence.Save(run, clashesJson);

                // H2: Sync the live ClashSession's flag set with the persisted
                // run so the BCC Clash tab and the in-authoring warning triangles
                // agree. Without this, the headless run writes clashes.json but
                // LiveClashHandler keeps stale _flaggedIds from the last
                // RefreshElement — user sees different counts in the tab vs.
                // the flag parameters. Raises ClashSession.OnRunCompleted so
                // subscribed UI can refresh.
                try { ClashSession.ForDocument(doc).SeedFromRun(run); }
                catch (Exception seedEx) { StingLog.Warn($"ClashSession.SeedFromRun: {seedEx.Message}"); }

                // C4: After SeedFromRun, write CLASH_LIVE_FLAG = 1 on every
                // host element flagged by the persisted run so resumed
                // sessions show warning triangles immediately (rather than
                // waiting for the next dirty edit). Also clear flags on any
                // host element no longer in the kept set. Best-effort — any
                // workshared / read-only failure logs and continues.
                try { WriteColdInitLiveFlags(doc, run); }
                catch (Exception flagEx) { StingLog.Warn($"WriteColdInitLiveFlags: {flagEx.Message}"); }

                // C6: Auto-export critical / high-severity new or reintroduced
                // clashes to a BCF zip when default_clash_matrix.json sets
                // "AutoBcfOnCritical": true. No-op when disabled or no
                // qualifying clashes. Output goes alongside clashes.json.
                try
                {
                    if (matrix.AutoBcfOnCritical)
                    {
                        var critical = run.Clashes
                            .Where(c => (c.Severity == "CRITICAL" || c.Severity == "HIGH")
                                     && (c.State == "New" || c.State == "Reintroduced"))
                            .ToList();
                        if (critical.Count > 0)
                        {
                            string bcfPath = ClashBcfExportCommand.ExportToBcf(doc, critical, outDir);
                            if (!string.IsNullOrEmpty(bcfPath))
                                StingLog.Info($"ClashRunCommand: auto-BCF exported {critical.Count} critical/high clashes → {bcfPath}");
                        }
                    }
                }
                catch (Exception bcfEx) { StingLog.Warn($"ClashRunCommand auto-BCF: {bcfEx.Message}"); }

                // F10: Append notification events for CRITICAL/HIGH new +
                //      reintroduced + severity-escalated clashes to a sidecar
                //      JSONL. Local-first — a future server-side adapter can
                //      tail and forward to FCM / SignalR / Slack without any
                //      coupling here.
                try
                {
                    var notifications = ClashNotifications.BuildFromRun(run, doc.ProjectInformation?.UniqueId);
                    if (notifications.Count > 0)
                        ClashNotifications.Append(outDir, notifications);
                }
                catch (Exception nEx) { StingLog.Warn($"ClashRunCommand notifications: {nEx.Message}"); }

                StingLog.Info($"ClashRun: raw={run.Stats.Raw} filtered={run.Stats.Tier1Filtered} " +
                    $"excluded={run.Stats.Excluded} kept={run.Clashes.Count} " +
                    $"groups={run.Stats.Groups} new={run.Stats.New} active={run.Stats.Active} " +
                    $"resolved={run.Stats.Resolved} reintro={run.Stats.Reintroduced} " +
                    $"{run.DurationMs}ms → {clashesJson}");

                if (!silent)
                {
                    TaskDialog.Show("STING Clash",
                        $"Run complete in {run.DurationMs} ms.\n\n" +
                        $"Raw hits: {run.Stats.Raw}\n" +
                        $"Filtered (matrix/rule): {run.Stats.Tier1Filtered}\n" +
                        $"Excluded: {run.Stats.Excluded}\n" +
                        $"Kept: {run.Clashes.Count}\n" +
                        $"Groups: {run.Stats.Groups}\n\n" +
                        $"New: {run.Stats.New}   Active: {run.Stats.Active}   " +
                        $"Resolved: {run.Stats.Resolved}   Reintroduced: {run.Stats.Reintroduced}\n\n" +
                        $"Saved: {clashesJson}");
                }
                runOut = run;

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ClashRunCommand.ExecuteOnDocument", ex);
                return Result.Failed;
            }
        }

        /// <summary>
        /// rec-6: Hydrate ElementFacts once per mesh so the matrix matcher doesn't
        /// re-read Revit element params per candidate pair. Category + workset are
        /// from the live element; system is from MEP system property when present.
        ///
        /// G5: Linked-doc elements now resolve through a docByGuid map so
        /// System/Workset facts populate for federated models. Prior code
        /// returned empty System for all linked-doc elements, silently losing
        /// any matrix cell that filtered on System=... for federated ducts.
        /// </summary>
        private static Dictionary<ClashElementKey, ElementFacts> BuildFactsByKey(Document doc,
            Dictionary<ClashElementKey, ClashMeshBuffer> meshes)
        {
            // G5: Build doc-guid → Document once so linked-doc lookups are O(1).
            // Reuses the same derivation MeshExtractor.BuildLinkedDocumentMap uses
            // so keys match the guids stamped onto ClashElementKey.DocGuid.
            var docByGuid = MeshExtractor.BuildLinkedDocumentMap(doc);

            // D2: Bulk-load elements by document instead of one
            //     doc.GetElement(...) per mesh. Prior code paid ~50k Revit
            //     API calls on a 50k model (3-5 s). Now: group mesh keys by
            //     owning document, run a single FilteredElementCollector
            //     scoped to those ids per doc, build a lookup, then read
            //     facts off the lookup.
            var sw = Stopwatch.StartNew();
            var elementByKey = new Dictionary<ClashElementKey, Element>(meshes.Count);
            var keysByDoc = new Dictionary<Document, List<ClashElementKey>>();
            foreach (var key in meshes.Keys)
            {
                var owningDoc = doc;
                if (docByGuid.TryGetValue(key.DocGuid ?? "", out var resolvedDoc))
                    owningDoc = resolvedDoc;
                if (owningDoc == null) continue;
                if (!keysByDoc.TryGetValue(owningDoc, out var lst))
                {
                    lst = new List<ClashElementKey>();
                    keysByDoc[owningDoc] = lst;
                }
                lst.Add(key);
            }
            foreach (var kv in keysByDoc)
            {
                var owningDoc = kv.Key;
                var keys = kv.Value;
                try
                {
                    // Build the ElementId list once. Revit 2024+: ElementId(long).
                    var ids = new List<ElementId>(keys.Count);
                    foreach (var k in keys) ids.Add(new ElementId((long)k.ElementId));
                    // Single-collector pass scoped to these ids; constructs
                    // an Id → Element map without per-mesh API hits.
                    var byId = new Dictionary<long, Element>(keys.Count);
                    if (ids.Count > 0)
                    {
                        var coll = new FilteredElementCollector(owningDoc, ids).WhereElementIsNotElementType();
                        foreach (var el in coll)
                        {
                            if (el?.Id == null) continue;
                            byId[el.Id.Value] = el;
                        }
                    }
                    foreach (var k in keys)
                    {
                        if (byId.TryGetValue(k.ElementId, out var el))
                            elementByKey[k] = el;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"BuildFactsByKey bulk-load: {ex.Message}"); }
            }

            var map = new Dictionary<ClashElementKey, ElementFacts>(meshes.Count);
            foreach (var kv in meshes)
            {
                var key = kv.Key;
                var mesh = kv.Value;
                elementByKey.TryGetValue(key, out var el);
                Document owningDoc = doc;
                if (docByGuid.TryGetValue(key.DocGuid ?? "", out var resolvedDoc)) owningDoc = resolvedDoc;
                map[key] = new ElementFacts
                {
                    Category = mesh.Category ?? "",
                    System = ReadSystem(el),
                    Classification = "",
                    Workset = ReadWorkset(owningDoc, el),
                };
            }
            if (meshes.Count > 500)
                StingLog.Info($"BuildFactsByKey: {meshes.Count} meshes hydrated in {sw.ElapsedMilliseconds}ms (D2 bulk-load)");
            return map;
        }

        private static string ReadSystem(Element el)
        {
            if (el == null) return "";
            try
            {
                // MEP curves expose MEPSystem directly. H4: removed the dead
                // `el is Space sp → return ""` branch — Space isn't in any
                // category LiveClashUpdater watches, and the same empty
                // string is returned anyway by the bottom fall-through.
                if (el is MEPCurve mc && mc.MEPSystem != null) return mc.MEPSystem.Name ?? "";
                // FamilyInstance MEP elements expose the system via the
                // "System Name" instance parameter.
                var p = el.LookupParameter("System Name");
                if (p != null) return p.AsString() ?? "";
            }
            catch (Exception ex) { StingLog.Warn($"ReadSystem {el?.Id}: {ex.Message}"); }
            return "";
        }

        private static string ReadWorkset(Document doc, Element el)
        {
            try
            {
                if (doc == null || el == null || !doc.IsWorkshared) return "";
                var ws = doc.GetWorksetTable().GetWorkset(el.WorksetId);
                return ws?.Name ?? "";
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ""; }
        }

        /// <summary>
        /// G1: Find the next unused CLH sequence for the given day by scanning
        /// every ClashRecord.Id in the merged current run plus the full prior
        /// run archive. Returns max(existing CLH-<day>-NNNNN) + 1, or 1 when
        /// no prior ids exist for that date. Prevents duplicate CLH-<day>-NNNNN
        /// on same-day re-runs.
        /// </summary>
        private static int SeedSequenceForToday(DateTime utcNow, ClashRunRecord current, ClashRunRecord prior)
        {
            string prefix = $"CLH-{utcNow:yyyyMMdd}-";
            int maxSeq = 0;

            void Scan(ClashRunRecord rec)
            {
                if (rec?.Clashes == null) return;
                foreach (var c in rec.Clashes)
                {
                    if (string.IsNullOrEmpty(c.Id)) continue;
                    if (!c.Id.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    var tail = c.Id.Substring(prefix.Length);
                    if (int.TryParse(tail, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int n))
                    {
                        if (n > maxSeq) maxSeq = n;
                    }
                }
            }
            Scan(current);
            Scan(prior);
            return maxSeq + 1;
        }

        private static ClashElementRecord ToRecord(ClashElementKey key, ElementFacts facts)
        {
            return new ClashElementRecord
            {
                IfcGuid = key.IfcGuid,
                UniqueId = key.UniqueId,
                ElementId = key.ElementId,
                DocGuid = key.DocGuid,
                LinkInstanceId = key.LinkInstanceElementId,
                Category = facts?.Category ?? "",
                System = facts?.System ?? "",
            };
        }

        /// <summary>
        /// A3: Promote / drop clashes whose matched cell carries a clearance
        /// tolerance. Reads CLEARANCE_xx mm from cell.Tolerance, computes the
        /// minimum AABB extent (in mm), then either:
        ///   - drops the record if the overlap is within tolerance and the
        ///     kernel reported Kind == "hard" only because tessellation
        ///     leaves a near-zero-volume intersection (treated as a sliver),
        ///   - or promotes Kind to "clearance" so downstream UI / BCF can
        ///     show "100 mm clearance breach" rather than mislabel as hard.
        /// HARD-tolerance cells leave Kind unchanged.
        /// </summary>
        private static void ApplyClearanceTolerance(ClashRunRecord run)
        {
            if (run?.Clashes == null || run.Clashes.Count == 0) return;
            int promoted = 0, dropped = 0;
            // Iterate in reverse so we can RemoveAt safely on near-zero overlap.
            for (int i = run.Clashes.Count - 1; i >= 0; i--)
            {
                var c = run.Clashes[i];
                if (string.IsNullOrEmpty(c.Tolerance)) continue;
                if (!c.Tolerance.StartsWith("CLEARANCE_", StringComparison.OrdinalIgnoreCase)) continue;
                string suffix = c.Tolerance.Substring("CLEARANCE_".Length);
                if (!double.TryParse(suffix, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out double tolMm)) continue;

                // E7: Better overlap depth proxy. Min component of the AABB
                //     intersection underestimates oblique penetration (a duct
                //     piercing a wall at 45° has a tiny min-extent on the
                //     diagonal axis even though the actual penetration depth
                //     is large). Use the *median* of the three intersection
                //     extents — for box-aligned hits it equals the original
                //     min-extent; for oblique hits it captures the dominant
                //     penetration direction. Falls back to min-extent when
                //     AABB data is missing.
                if (c.AabbMin == null || c.AabbMax == null || c.AabbMin.Length < 3 || c.AabbMax.Length < 3) continue;
                double dx = Math.Max(0, c.AabbMax[0] - c.AabbMin[0]);
                double dy = Math.Max(0, c.AabbMax[1] - c.AabbMin[1]);
                double dz = Math.Max(0, c.AabbMax[2] - c.AabbMin[2]);
                // Median-of-three: sort, take middle.
                double e0 = dx, e1 = dy, e2 = dz;
                if (e0 > e1) { var t = e0; e0 = e1; e1 = t; }
                if (e1 > e2) { var t = e1; e1 = e2; e2 = t; }
                if (e0 > e1) { var t = e0; e0 = e1; e1 = t; }
                double overlapMm = e1 * 304.8;

                if (overlapMm <= tolMm)
                {
                    // Within tolerance — drop the record. The kernel labelled
                    // this "hard" because triangles overlap, but the matrix
                    // says ≤ tolMm of overlap is acceptable. Treat as a
                    // tessellation sliver and remove.
                    run.Clashes.RemoveAt(i);
                    dropped++;
                }
                else
                {
                    // Beyond tolerance — surface as a clearance breach.
                    if (string.IsNullOrEmpty(c.Kind) || string.Equals(c.Kind, "hard", StringComparison.OrdinalIgnoreCase))
                    {
                        c.Kind = "clearance";
                        promoted++;
                    }
                }
            }
            // Tag remaining (HARD-tolerance) records explicitly so consumers
            // can rely on Kind being non-empty after Stage 2.
            foreach (var c in run.Clashes) if (string.IsNullOrEmpty(c.Kind)) c.Kind = "hard";
            if (promoted > 0 || dropped > 0)
                StingLog.Info($"ApplyClearanceTolerance: promoted={promoted} dropped={dropped} remaining={run.Clashes.Count}");
        }

        /// <summary>
        /// C1: Score every kept clash via ClashTriageEngine and stash the
        /// score on ClashRecord.TriageScore. Sorts run.Clashes by score
        /// descending so coordinators see the worst items first.
        /// History inputs:
        ///   RecurrenceCount = number of times this Identity appeared in the
        ///                     prior run archive (only the immediately-prior
        ///                     run is loaded; richer history would need a
        ///                     full archive walk).
        ///   DismissCount    = number of "Void" transitions in StateHistory.
        /// </summary>
        private static void ApplyTriageScores(ClashRunRecord run, ClashRunRecord prior)
        {
            if (run?.Clashes == null || run.Clashes.Count == 0) return;

            var inputs = new List<ClashInput>(run.Clashes.Count);
            foreach (var c in run.Clashes)
            {
                int dismissCount = 0;
                if (c.StateHistory != null)
                {
                    foreach (var h in c.StateHistory)
                        if (string.Equals(h.To, "Void", StringComparison.OrdinalIgnoreCase)) dismissCount++;
                }
                inputs.Add(new ClashInput
                {
                    ClashId = c.Id ?? "",
                    ElementAId = c.ElementA?.ElementId ?? 0,
                    ElementBId = c.ElementB?.ElementId ?? 0,
                    CategoryA = c.ElementA?.Category ?? "",
                    CategoryB = c.ElementB?.Category ?? "",
                    PenetrationMm = ComputeOverlapMm(c),
                    EstCostUsd = null,   // ResolutionHeuristics has no cost API today
                    PhaseInstalled = false,   // ElementFacts.Phase isn't populated yet
                    // E10: read from the persisted ClashRecord.RecurrenceCount,
                    //      maintained by ClashHistory.MergeWithPrior. Replaces the
                    //      0/1 prior-run-presence stand-in — a clash reintroduced
                    //      4× now scores correctly (4) instead of capping at 1.
                    RecurrenceCount = c.RecurrenceCount,
                    DismissCount = dismissCount,
                });
            }

            List<ScoredClash> scored;
            // F5: TriageAll scores every clash so the persisted ClashRecord.TriageScore
            //     is meaningful across the full run. Triage(inputs) used to silently
            //     truncate to top-N (default 20) — the other 480 stayed at score=0.
            try { scored = ClashTriageEngine.TriageAll(inputs); }
            catch (Exception ex)
            {
                StingLog.Warn($"ClashTriageEngine.TriageAll: {ex.Message}");
                return;
            }
            var byClashId = new Dictionary<string, ScoredClash>(StringComparer.Ordinal);
            foreach (var s in scored)
                if (!string.IsNullOrEmpty(s.ClashId)) byClashId[s.ClashId] = s;

            foreach (var c in run.Clashes)
            {
                if (!string.IsNullOrEmpty(c.Id) && byClashId.TryGetValue(c.Id, out var s))
                    c.TriageScore = s.Score;
                else
                    c.TriageScore = 0.0;
            }

            // Stable sort: TriageScore desc, then by Severity rank, then by Id
            // so equal-score records have a deterministic order.
            run.Clashes.Sort((x, y) =>
            {
                int cmp = y.TriageScore.CompareTo(x.TriageScore);
                if (cmp != 0) return cmp;
                cmp = SeverityRank(y.Severity).CompareTo(SeverityRank(x.Severity));
                if (cmp != 0) return cmp;
                return string.CompareOrdinal(x.Id ?? "", y.Id ?? "");
            });
        }

        private static int SeverityRank(string sev)
        {
            switch ((sev ?? "").ToUpperInvariant())
            {
                case "CRITICAL": return 4;
                case "HIGH": return 3;
                case "MED":
                case "MEDIUM": return 2;
                case "LOW": return 1;
                default: return 0;
            }
        }

        private static double ComputeOverlapMm(ClashRecord c)
        {
            if (c?.AabbMin == null || c.AabbMax == null) return 0;
            if (c.AabbMin.Length < 3 || c.AabbMax.Length < 3) return 0;
            double dx = Math.Max(0, c.AabbMax[0] - c.AabbMin[0]);
            double dy = Math.Max(0, c.AabbMax[1] - c.AabbMin[1]);
            double dz = Math.Max(0, c.AabbMax[2] - c.AabbMin[2]);
            return Math.Min(dx, Math.Min(dy, dz)) * 304.8;
        }

        /// <summary>
        /// C2: Resolve workset owner names and overlay them on group
        /// assignees when the workset name maps to a known discipline
        /// prefix. Best-effort: any failure logs and leaves the matrix-
        /// cell default in place. Synthesises a one-off CoordIssue list
        /// only so we can re-use ClashSlaIntegration.EnrichAssignees
        /// without duplicating the resolution logic.
        /// </summary>
        private static void EnrichGroupAssignees(Document doc, ClashRunRecord run)
        {
            if (doc == null || run?.Groups == null || run.Groups.Count == 0) return;
            if (!doc.IsWorkshared) return;
            try
            {
                // One placeholder issue per group so EnrichAssignees can read
                // each group's representative ClashRecord and copy the owner
                // back onto the group itself.
                var stub = new List<CoordIssue>();
                for (int i = 0; i < run.Groups.Count; i++)
                {
                    stub.Add(new CoordIssue { Assignee = run.Groups[i].Assignee ?? "" });
                }
                ClashSlaIntegration.EnrichAssignees(doc, stub, run);
            }
            catch (Exception ex) { StingLog.Warn($"EnrichGroupAssignees: {ex.Message}"); }
        }

        /// <summary>
        /// C4: Cold-init flag write. Walk every kept ClashRecord, gather host
        /// (LinkInstanceId == -1) element ids on either side that are not
        /// resolved/void, and call LiveClashFlag.Apply with the set so the
        /// in-authoring CLASH_LIVE_FLAG parameters reflect persisted state on
        /// session resume. Cleared ids are not enumerated here — the live
        /// session's SeedFromRun raised OnElementFlagChanged for those.
        /// </summary>
        private static void WriteColdInitLiveFlags(Document doc, ClashRunRecord run)
        {
            if (doc == null || run?.Clashes == null) return;
            var flagged = new HashSet<int>();
            foreach (var c in run.Clashes)
            {
                if (c.State == "Resolved" || c.State == "Void") continue;
                if (c.ElementA != null && c.ElementA.LinkInstanceId == -1)
                    flagged.Add(c.ElementA.ElementId);
                if (c.ElementB != null && c.ElementB.LinkInstanceId == -1)
                    flagged.Add(c.ElementB.ElementId);
            }
            if (flagged.Count == 0) return;
            // Empty cleared list — clearing is owned by the live session's
            // RefreshElement / RemoveElement and was already handled by
            // SeedFromRun's diff. We only need to assert flag=1 here.
            LiveClashFlag.Apply(doc, flagged, Array.Empty<int>());
            StingLog.Info($"WriteColdInitLiveFlags: wrote CLASH_LIVE_FLAG=1 on {flagged.Count} elements");
        }

        /// <summary>
        /// Section-box volume in ft³, or 0 when the section box is inactive or
        /// throws (very old views can return null bounds). Used by the View3D
        /// fallback ordering so the largest visible-extent view wins.
        /// </summary>
        private static double SectionBoxVolumeFt3(View3D v)
        {
            try
            {
                if (v == null || !v.IsSectionBoxActive) return 0;
                var bb = v.GetSectionBox();
                if (bb == null) return 0;
                double dx = Math.Max(0, bb.Max.X - bb.Min.X);
                double dy = Math.Max(0, bb.Max.Y - bb.Min.Y);
                double dz = Math.Max(0, bb.Max.Z - bb.Min.Z);
                return dx * dy * dz;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; }
        }

        /// <summary>
        /// Find a seed data file in either the project-output directory (user
        /// edits) or the plugin's data\clash\ directory (ships with the DLL).
        /// </summary>
        private static string FindDataFile(string fileName)
        {
            try
            {
                string dll = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string dllDir = Path.GetDirectoryName(dll) ?? "";
                string[] candidates =
                {
                    Path.Combine(dllDir, "data", "clash", fileName),
                    Path.Combine(dllDir, "data", fileName),
                    Path.Combine(dllDir, fileName),
                };
                foreach (var c in candidates) if (File.Exists(c)) return c;
            }
            // H9: Reflection / Path.Combine failures here shouldn't crash the
            // whole clash run — the built-in default matrix still fires. Log
            // so the fallback is visible in StingTools.log.
            catch (Exception ex) { StingLog.Warn($"ClashRunCommand.FindDataFile({fileName}): {ex.Message}"); }
            return fileName;   // let LoadOrDefault handle missing-file fallback
        }
    }
}
