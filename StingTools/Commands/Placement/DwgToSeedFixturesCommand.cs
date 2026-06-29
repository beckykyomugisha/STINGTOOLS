// StingTools — Placement_DwgToSeedFixtures command.
//
// The DWG->seed->swap bridge as a stand-alone command (dock panel / workflow /
// NLP entry point). All logic lives in Core.Placement.DwgFixtureBridge — this is
// a thin shell: resolve the document, run the bridge on the first DWG import,
// render the result. The Placement Centre's Library tab calls the SAME engine
// directly for inline reporting; neither duplicates the other.
//
// Does NOT open a Revit transaction itself — the bridge builds seeds (own
// implicit transactions) then opens "STING Place DWG Fixtures". Model-modifying —
// verify in Revit before merge.

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Placement;
using StingTools.UI;

namespace StingTools.Commands.Placement
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DwgToSeedFixturesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            DwgFixtureBridgeResult res;
            try { res = DwgFixtureBridge.PlaceFromFirstImport(doc, dryRun: false); }
            catch (Exception ex)
            {
                StingLog.Error("DwgToSeedFixturesCommand", ex);
                message = $"DWG fixture bridge failed: {ex.Message}";
                return Result.Failed;
            }

            try { BuildPanel(res).Show(); }
            catch (Exception ex) { StingLog.Warn($"DwgToSeedFixturesCommand panel: {ex.Message}"); }

            try { ActionAuditLog.Record("Placement_DwgToSeedFixtures",
                $"blocks={res.TotalBlocks} placed={res.Placed}"); }
            catch (Exception ex) { StingLog.Warn($"audit: {ex.Message}"); }

            return Result.Succeeded;
        }

        /// <summary>Shared result rendering — used by both the command and the Library tab.</summary>
        public static StingResultPanel.Builder BuildPanel(DwgFixtureBridgeResult res)
        {
            var panel = StingResultPanel.Create("STING — DWG fixtures → STING seeds");
            panel.SetSubtitle(res.DryRun
                ? "Dry run — no model changes. Maps DWG MEP symbols to swap-ready STING seeds."
                : "Placed swap-ready STING seed instances at the DWG MEP symbol locations.");
            panel.AddSection("SUMMARY")
                .Metric("Block inserts detected", res.TotalBlocks.ToString())
                .Metric("Layer points captured",  res.TotalLayerPoints.ToString(),
                        res.IncludedLineClusters ? "incl. experimental line-clusters" : "DWG Points on mapped layers (safe)")
                .Metric("Captured (place-loop input)", res.TotalCaptured.ToString(),
                        "block + layer captures that mapped to a fixture seed")
                .Metric(res.DryRun ? "Would place" : "Placed", res.Placed.ToString())
                .Metric("Skipped — not a fixture / unmapped", res.SkippedNoMapping.ToString())
                .Metric("Skipped — seedless category", res.SkippedSeedless.ToString())
                .Metric("Skipped — seed not built",   res.SkippedNoSymbol.ToString())
                .Metric("Skipped — not hosted",   res.SkippedNotHosted.ToString())
                .Metric("Skipped — mapped, empty layer", res.SkippedExplodedNoPoint.ToString())
                .Metric("De-duped vs block insert", res.DedupedAgainstBlock.ToString());

            if (res.IncludedLineClusters)
                panel.MetricWarn("Capture mode", "EXPERIMENTAL line-cluster ON",
                    "Loose lines/arcs on mapped layers were clustered into points — a heuristic; verify counts before committing.");

            if (res.CapturedByMode.Count > 0)
            {
                panel.AddSection("CAPTURE MODE");
                foreach (var kv in res.CapturedByMode)
                    panel.Metric(kv.Key, kv.Value.ToString(),
                        kv.Key == "cluster" ? "experimental — heuristic centroid" :
                        kv.Key == "point" ? "DWG Point entity (safe)" : "block insert (safe)");
            }

            if (res.PlacedByCategory.Count > 0)
            {
                panel.AddSection(res.DryRun ? "BY CATEGORY (would place)" : "PLACED BY CATEGORY");
                foreach (var kv in res.PlacedByCategory)
                    panel.Metric(kv.Key, kv.Value.ToString());
            }

            if (res.Messages.Count > 0)
            {
                panel.AddSection("DETAIL");
                foreach (var m in res.Messages) panel.Text(m);
            }

            panel.AddSection("NEXT STEPS")
                .Text("These are STING seed instances — run Library › Swap to Manufacturer to swap them to real product families (non-destructive).")
                .Text("Extend Data/Placement/DWG_SYMBOL_MAP.json (or <project>/_BIM_COORD/dwg_symbol_map.json) to map more blocks/variants.");
            return panel;
        }
    }
}
