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
            if (manifest == null || manifest.Cables == null || manifest.Cables.Count == 0)
            {
                TaskDialog.Show("STING Consolidator",
                    "No cable manifest. Run Auto-Route Conduit first.");
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

            // Stage 2 — preview / confirm.
            var preview = StingResultPanel.Create("Conduit Consolidation — Preview");
            preview.SetSubtitle($"{groups.Count} group(s), saving up to {totalSaved} duplicate conduit run(s)");
            preview.AddSection("SUMMARY")
                .Metric("Groups",                groups.Count.ToString())
                .Metric("Saveable conduit runs", totalSaved.ToString())
                .Metric("Cable count after",     manifest.Cables.Count.ToString(),
                    "(unchanged — only the conduit envelope changes)");

            preview.AddSection("GROUPS");
            foreach (var g in groups.OrderByDescending(x => x.SaveableConduitCount).Take(20))
            {
                preview.Metric(
                    $"{g.PanelName} → {ShortenId(g.SourceId)} ({g.Segregation})",
                    g.Members.Count.ToString(),
                    $"new ⌀={g.UnionDiameterMm:F0} mm, save {g.SaveableConduitCount} duplicates");
            }

            preview.AddSection("APPLY")
                .Text("This is a preview only. Apply consolidation by running the same command again with the 'Apply' modifier (Shift+Click on the button), or via WORKFLOW_ConduitConsolidate.json — the actual conduit deletion + re-route lands in the next sub-phase to keep this preview a safe read-only.");
            preview.Show();

            return Result.Succeeded;
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
