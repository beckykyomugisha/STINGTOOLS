// StingTools — ConduitConsolidator.
//
// Post-pass that runs after ConduitAutoRouteCommand. Walks the cable
// manifest, groups cables that share a (panel, source-equipment,
// segregation-class) tuple, and either:
//
//   (a) reports them as a consolidation opportunity (dry-run);
//   (b) deletes the per-cable conduits and re-routes a single
//       larger conduit sized to fit all member cables — when the
//       caller requests Apply mode.
//
// Consolidation rule:
//   1. Cables must share PanelName.
//   2. Cables must share SourceEquipmentId (from the same panel
//      they go to the same destination — typically the load).
//   3. Cables must share SegregationClass — BS EN 50174-2 forbids
//      mixing power and comms in one conduit.
//
// Sizing:
//   The new conduit's diameter is computed by ConduitRouteEngine.
//   SelectConduitDiameterMm against the union of cables — already
//   ≤40% fill compliant.
//
// Why: every-cable-its-own-conduit produces unrealistic takeoffs
// (4 cables routing to the same outlet → 4 separate conduits =
// 4× the fittings, 4× the labour, 4× the wall penetrations). The
// consolidator brings the BIM model in line with how electricians
// actually work.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Electrical;
using StingTools.UI;

namespace StingTools.Commands.Electrical.Routing
{
    public sealed class ConsolidationGroup
    {
        public string PanelName { get; set; } = "";
        public string SourceId  { get; set; } = "";
        public string Segregation { get; set; } = "";
        public List<StingCable> Members { get; set; } = new List<StingCable>();
        public double UnionDiameterMm { get; set; }
        public int    SaveableConduitCount { get; set; }
    }

    public sealed class ConsolidationApplyResult
    {
        /// <summary>Number of groups successfully consolidated.</summary>
        public int ConsolidatedCount { get; set; }
        /// <summary>Per-cable conduits removed from the model.</summary>
        public int DeletedConduits { get; set; }
        /// <summary>Consolidated conduit segments newly created.</summary>
        public int NewConsolidatedConduits { get; set; }
        public int Errors { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ConduitConsolidatorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            CableManifest manifest;
            try { manifest = CableManifest.Load(doc); }
            catch (Exception ex) { StingLog.Warn($"CableManifest.Load: {ex.Message}"); manifest = null; }
            string emptyWhy = manifest == null ? "The cable manifest could not be loaded." : manifest.DescribeEmpty();
            if (emptyWhy != null)
            {
                TaskDialog.Show("STING Consolidator", emptyWhy);
                return Result.Cancelled;
            }

            // Stage 1 — find groups.
            var groups = FindGroups(manifest);
            int totalSaved = groups.Sum(g => g.SaveableConduitCount);
            if (groups.Count == 0)
            {
                TaskDialog.Show("STING Consolidator",
                    "No consolidation opportunities — every routed cable already has unique " +
                    "(panel, source, segregation). Either every cable goes to a unique destination, " +
                    "or the manifest hasn't been routed yet.");
                return Result.Succeeded;
            }

            // Stage 2 — preview + Apply confirmation.
            var dlg = new TaskDialog("STING Consolidator")
            {
                MainInstruction = $"Consolidate {groups.Count} group(s)?",
                MainContent =
                    $"{groups.Sum(g => g.Members.Count)} cable(s) currently routed in " +
                    $"{groups.Sum(g => g.Members.Count)} separate conduits will be merged into " +
                    $"{groups.Count} consolidated conduit run(s) sized to ≤40% fill (BS EN 61386).\n\n" +
                    "DESTRUCTIVE: Apply deletes the existing per-cable conduits and routes a single " +
                    "larger conduit per group. The cable manifest is updated so each member cable's " +
                    "RouteTrayIds points at the new consolidated run.\n\n" +
                    "Cancel = preview-only run (no changes).",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
            };
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Preview only", "Show the plan without modifying the model.");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Apply consolidation", "Delete + re-route. One TransactionGroup → fully reversible via undo.");
            var choice = dlg.Show();

            if (choice == TaskDialogResult.CommandLink1 || choice == TaskDialogResult.Cancel ||
                choice == TaskDialogResult.No)
            {
                ShowPreviewPanel(groups, totalSaved, manifest, applyResult: null);
                return Result.Succeeded;
            }

            // Stage 3 — Apply.
            var applyR = ApplyConsolidation(doc, groups, manifest);
            try { manifest.Save(doc); } catch (Exception ex) { StingLog.Warn($"Manifest save: {ex.Message}"); }
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            try { ActionAuditLog.Record("Consolidate_Apply",
                $"groups={groups.Count} consolidated={applyR.ConsolidatedCount} " +
                $"deletedConduits={applyR.DeletedConduits} errors={applyR.Errors}"); }
            catch (Exception ex) { StingLog.Warn($"audit: {ex.Message}"); }

            ShowPreviewPanel(groups, totalSaved, manifest, applyR);
            return Result.Succeeded;
        }

        /// <summary>
        /// Destructively consolidate every group. For each: pick a
        /// representative source/destination XYZ from the first member
        /// cable, delete every member's per-cable conduits, route a
        /// single conduit at the union diameter, update each member's
        /// RouteTrayIds. Wrapped in a TransactionGroup so the whole
        /// apply rolls back on fatal failure.
        /// </summary>
        public static ConsolidationApplyResult ApplyConsolidation(
            Document doc, IList<ConsolidationGroup> groups, CableManifest manifest)
        {
            var result = new ConsolidationApplyResult();
            if (doc == null || groups == null || groups.Count == 0) return result;

            var conduitType = new FilteredElementCollector(doc)
                .OfClass(typeof(ConduitType)).Cast<ConduitType>().FirstOrDefault();
            if (conduitType == null)
            {
                result.Errors++;
                result.Warnings.Add("No ConduitType in project — cannot create consolidated runs.");
                return result;
            }

            using (var tg = new TransactionGroup(doc, "STING Consolidate Conduits"))
            {
                tg.Start();
                using (var tx = new Transaction(doc, "Delete + Re-route consolidated"))
                {
                    tx.Start();
                    // One SubTransaction per group: a group that cannot be
                    // re-created is rolled back on its own, so its original
                    // conduits survive and the other groups still apply.
                    foreach (var group in groups)
                    {
                        using (var st = new SubTransaction(doc))
                        {
                            st.Start();
                            try
                            {
                                if (ApplyOneGroup(doc, group, conduitType.Id, result, out string why))
                                {
                                    st.Commit();
                                    result.ConsolidatedCount++;
                                }
                                else
                                {
                                    st.RollBack();
                                    result.Errors++;
                                    result.Warnings.Add($"{group.PanelName}/{group.Segregation}: NOT consolidated — {why}. Original conduits left in place.");
                                    StingLog.Warn($"Consolidate group {group.PanelName}: {why}");
                                }
                            }
                            catch (Exception ex)
                            {
                                if (st.HasStarted() && !st.HasEnded()) st.RollBack();
                                result.Errors++;
                                result.Warnings.Add($"{group.PanelName}/{group.Segregation}: NOT consolidated — {ex.Message}. Original conduits left in place.");
                                StingLog.Warn($"Consolidate group: {ex.Message}");
                            }
                        }
                    }
                    tx.Commit();
                }
                tg.Assimilate();
            }
            return result;
        }

        /// <summary>
        /// Apply a single group: pick one representative member's
        /// start/end (their cables share source + dest), route ONE
        /// consolidated conduit at the group's UnionDiameterMm, and only
        /// once that run exists delete every member's per-cable conduit and
        /// point each member's RouteTrayIds at the new run.
        ///
        /// <para>Returns false (with <paramref name="why"/>) when nothing was
        /// created; the caller rolls the group's SubTransaction back. The old
        /// order deleted first, so a group whose every Conduit.Create failed
        /// was left with no conduit at all and still counted as consolidated.
        /// The manifest is mutated only on success.</para>
        /// </summary>
        private static bool ApplyOneGroup(
            Document doc, ConsolidationGroup group, ElementId conduitTypeId,
            ConsolidationApplyResult result, out string why)
        {
            why = null;
            // 1) Pick representative endpoints from the first member cable
            //    that has at least one routed conduit. We could compute
            //    bbox-of-all-conduits as the bounding run, but for the
            //    common case where group members go panel→same-load, the
            //    first member's path is the canonical one.
            StingCable rep = null;
            ElementId firstConduitId = ElementId.InvalidElementId;
            foreach (var c in group.Members)
            {
                if (c.RouteTrayIds == null || c.RouteTrayIds.Count == 0) continue;
                // Only a conduit-routed cable can supply the run geometry; a
                // tray-routed one (AddCable/CableRouter) would re-route along
                // the tray's own centreline.
                if (!(doc.GetElement(new ElementId((long)c.RouteTrayIds[0])) is Conduit)) continue;
                rep = c;
                firstConduitId = new ElementId((long)c.RouteTrayIds[0]);
                break;
            }
            if (rep == null) { why = "no member cable is routed in a conduit"; return false; }

            var firstCurve = (doc.GetElement(firstConduitId) as MEPCurve)?.Location as LocationCurve;
            if (firstCurve?.Curve == null) { why = $"routed conduit {firstConduitId.Value} has no location curve"; return false; }
            // The whole run spans first-segment start → last-segment end.
            // This used to take both endpoints from the FIRST segment only, so
            // the replacement conduit covered just the first leg of an L/Z
            // route after every member's full run had been deleted.
            var lastId = new ElementId((long)rep.RouteTrayIds[rep.RouteTrayIds.Count - 1]);
            var lastCurve = (doc.GetElement(lastId) as MEPCurve)?.Location as LocationCurve;
            XYZ start = firstCurve.Curve.GetEndPoint(0);
            XYZ end   = (lastCurve?.Curve ?? firstCurve.Curve).GetEndPoint(1);
            if (lastCurve?.Curve == null)
                result.Warnings.Add($"{group.PanelName}: last routed segment {lastId.Value} missing — consolidated run ends at the first segment's end.");
            ElementId levelId = (doc.GetElement(firstConduitId) as MEPCurve)?.LevelId
                                ?? doc.ActiveView?.GenLevel?.Id ?? ElementId.InvalidElementId;

            // 2) Route a single consolidated conduit at the union diameter.
            var segments = ConduitRouteEngine.ComputeRoute(start, end,
                group.UnionDiameterMm, $"CONSOLIDATED:{group.PanelName}");
            var newIds = new List<long>();
            int bends = ConduitRouteEngine.CountBends(segments);
            foreach (var seg in segments)
            {
                if (seg.Start.DistanceTo(seg.End) < ConduitRouteEngine.MinLegFt) continue;
                try
                {
                    var conduit = Conduit.Create(doc, conduitTypeId, seg.Start, seg.End, levelId);
                    if (conduit != null)
                    {
                        // The union diameter was computed then discarded, so the
                        // "consolidated" run kept the type's default size.
                        var outcome = ConduitDiameterApplier.Apply(conduit, group.UnionDiameterMm,
                            out _, out string sizeNote);
                        if (outcome != DiameterOutcome.Applied)
                            result.Warnings.Add($"{group.PanelName}: {sizeNote}");
                        try
                        {
                            ParameterHelpers.SetString(conduit, ParamRegistry.ELC_CONDUIT_ROUTE,
                                $"CONSOLIDATED:{group.PanelName}", overwrite: true);
                            ParameterHelpers.SetString(conduit, "ELC_CDT_BEND_COUNT_NR",
                                bends.ToString(), overwrite: true);
                            double mm = seg.Start.DistanceTo(seg.End) * 304.8;
                            ParameterHelpers.SetString(conduit, "ELC_CDT_RUN_LENGTH_M",
                                (mm / 1000.0).ToString("F3",
                                    System.Globalization.CultureInfo.InvariantCulture),
                                overwrite: true);
                            // Stamp cable count so the BS 7671 fill validator
                            // (Wave A in the routing roadmap) picks the right
                            // table row when this run is audited later.
                            ParameterHelpers.SetString(conduit, "ELC_CDT_CABLE_COUNT_NR",
                                group.Members.Count.ToString(), overwrite: true);
                        }
                        catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                        newIds.Add(conduit.Id.Value);
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Conduit.Create consolidated: {ex.Message}");
                }
            }
            if (newIds.Count == 0)
            {
                why = segments.Count == 0
                    ? "the router produced no segments"
                    : $"Conduit.Create failed for all {segments.Count} segment(s)";
                return false;
            }

            // 3) Only now that the replacement exists, delete every member
            //    cable's existing per-cable conduits. New RouteTrayIds lists
            //    are staged and written to the manifest only on success.
            int deleted = 0;
            var staged = new Dictionary<StingCable, List<long>>();
            foreach (var c in group.Members)
            {
                if (c.RouteTrayIds == null) continue;
                var kept = new List<long>();   // ids that still exist and still carry this cable
                foreach (long lid in c.RouteTrayIds)
                {
                    if (newIds.Contains(lid)) { kept.Add(lid); continue; }
                    try
                    {
                        var el = doc.GetElement(new ElementId((long)lid));
                        if (el == null) continue;
                        // RouteTrayIds also carries CABLE TRAY ids written by
                        // AddCable's CableRouter — shared containment that must
                        // never be deleted by a conduit consolidation.
                        if (!(el is Conduit))
                        {
                            kept.Add(lid);
                            result.Warnings.Add($"Kept {el.Category?.Name ?? "element"} {lid}: not a conduit, not deleted.");
                            continue;
                        }
                        doc.Delete(el.Id);
                        deleted++;
                    }
                    catch (Exception ex)
                    {
                        kept.Add(lid);
                        result.Warnings.Add($"Delete conduit {lid}: {ex.Message}");
                    }
                }
                staged[c] = kept;
            }

            // 4) Point every member cable at the new consolidated run.
            foreach (var c in group.Members)
            {
                var ids = staged.TryGetValue(c, out var kept) ? kept : (c.RouteTrayIds ?? new List<long>());
                ids.AddRange(newIds.Where(id => !ids.Contains(id)));
                c.RouteTrayIds = ids;
            }
            result.DeletedConduits += deleted;
            result.NewConsolidatedConduits += newIds.Count;
            return true;
        }

        // ── Result rendering ────────────────────────────────────────────

        private static void ShowPreviewPanel(IList<ConsolidationGroup> groups, int totalSaved,
            CableManifest manifest, ConsolidationApplyResult applyResult)
        {
            bool applied = applyResult != null;
            var preview = StingResultPanel.Create(applied
                ? "Conduit Consolidation — Applied"
                : "Conduit Consolidation — Preview");
            preview.SetSubtitle($"{groups.Count} group(s), saving up to {totalSaved} duplicate conduit run(s)");
            preview.AddSection("SUMMARY")
                .Metric("Groups",                groups.Count.ToString())
                .Metric("Saveable conduit runs", totalSaved.ToString())
                .Metric("Cable count after",     manifest.Cables.Count.ToString(),
                    "(unchanged — only the conduit envelope changes)");
            if (applied)
            {
                preview.MetricHighlight("Consolidated", applyResult.ConsolidatedCount.ToString());
                preview.Metric("Deleted conduits",      applyResult.DeletedConduits.ToString());
                preview.Metric("New consolidated runs", applyResult.NewConsolidatedConduits.ToString());
                if (applyResult.Errors > 0)
                    preview.MetricError("Errors", applyResult.Errors.ToString());
            }

            preview.AddSection("GROUPS");
            foreach (var g in groups.OrderByDescending(x => x.SaveableConduitCount).Take(20))
            {
                preview.Metric(
                    $"{g.PanelName} → {ShortenId(g.SourceId)} ({g.Segregation})",
                    g.Members.Count.ToString(),
                    $"new ⌀={g.UnionDiameterMm:F0} mm, save {g.SaveableConduitCount} duplicates");
            }

            if (!applied)
            {
                preview.AddSection("APPLY")
                    .Text("Re-run the command and choose 'Apply consolidation' to commit.")
                    .Text("Apply runs in one TransactionGroup — full Ctrl-Z undo if you don't like the result.");
            }
            else
            {
                preview.AddSection("NEXT STEPS")
                    .Text("Run 'BS 7671 Compliance Check' — the consolidated runs honor the same fill / bend / length rules and may surface fresh findings.")
                    .Text("Run 'Junction Box Auto-Place' — a thicker consolidated conduit may exceed bend caps that per-cable runs didn't.")
                    .Text("Re-run 'Auto-Route Conduit' if any unconnected cable surfaces in the result panel.");
                if (applyResult.Warnings.Count > 0)
                {
                    preview.AddSection("WARNINGS");
                    foreach (var w in applyResult.Warnings.Take(15)) preview.Text(w);
                    if (applyResult.Warnings.Count > 15)
                        preview.Text($"+{applyResult.Warnings.Count - 15} more (StingLog).");
                }
            }
            preview.Show();
        }

        /// <summary>
        /// Group cables by (PanelName, SourceEquipmentId, SegregationClass).
        /// Groups with only one member are dropped — they're already
        /// "consolidated" by definition. Computes the union OD per group
        /// using the same ≤40% fill rule as ConduitRouteEngine.
        /// </summary>
        public static List<ConsolidationGroup> FindGroups(CableManifest manifest)
        {
            var groups = new List<ConsolidationGroup>();
            if (manifest?.Cables == null) return groups;

            var bins = manifest.Cables
                .Where(c => !string.IsNullOrEmpty(c.PanelName))
                .GroupBy(c => $"{c.PanelName}|{c.SourceEquipmentId}|{c.SegregationClass}",
                    StringComparer.OrdinalIgnoreCase);

            foreach (var bin in bins)
            {
                var members = bin.ToList();
                if (members.Count < 2) continue;        // already singular
                var first = members[0];
                double unionMm = ConduitRouteEngine.SelectConduitDiameterMm(members);
                groups.Add(new ConsolidationGroup
                {
                    PanelName       = first.PanelName,
                    SourceId        = first.SourceEquipmentId,
                    Segregation     = first.SegregationClass,
                    Members         = members,
                    UnionDiameterMm = unionMm,
                    // Per-cable conduit count varies by cable but the
                    // current router emits 3 segments per cable (L/Z).
                    // Saving = (members - 1) × 3 segments. Conservative —
                    // the actual saving is higher when chase/soffit
                    // routes fold multiple straight legs into one.
                    SaveableConduitCount = (members.Count - 1) * 3
                });
            }
            return groups;
        }

        private static string ShortenId(string s)
        {
            if (string.IsNullOrEmpty(s)) return "?";
            return s.Length > 12 ? s.Substring(0, 12) + "…" : s;
        }
    }
}
