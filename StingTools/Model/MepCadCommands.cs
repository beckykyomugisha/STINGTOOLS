// ============================================================================
// MepCadCommands.cs — Phase: MEP-from-DWG V1.
//
// MODEL-tab command surface for MEP-from-DWG, peer to the structural StrCAD*
// commands. Both reuse the shared CADToModelEngine extraction core via the
// MepDetectionEngine / MepFixtureBuilder pipeline (Core/Cad/Mep).
//
//   Mep_CadPreview  (ReadOnly) — audit: per-layer counts + which DWG blocks
//                   would place as which fixture vs skip, + straight-run candidates.
//   Mep_CadToModel  (Manual)   — place MEP fixtures from blocks AND straight runs
//                   (Duct/Pipe/Conduit/CableTray) from lines.
//
// V1 placed fixtures from blocks. V2 adds straight runs (this file + MepRunBuilder).
// Fixture host-snapping and the per-layer wizard are the remaining V2 items.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Cad.Mep;

namespace StingTools.Model
{
    internal static class MepCadShared
    {
        /// <summary>Pick the DWG import to convert. Prefers one shown in the active
        /// view; otherwise the first. Returns null + a message when none.</summary>
        public static ImportInstance PickImport(Document doc, out int count)
        {
            var imports = CADToModelEngine.FindImportInstances(doc);
            count = imports.Count;
            if (imports.Count == 0) return null;
            if (doc.ActiveView != null)
            {
                var inView = imports.FirstOrDefault(i => i.OwnerViewId == doc.ActiveView.Id);
                if (inView != null) return inView;
            }
            return imports[0];
        }

        /// <summary>Target level: the active plan's level, else the lowest level.</summary>
        public static Level ResolveLevel(Document doc)
        {
            if (doc.ActiveView is ViewPlan vp && vp.GenLevel != null) return vp.GenLevel;
            return new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).FirstOrDefault();
        }

        /// <summary>True when at least one FamilySymbol of the named category is loaded.</summary>
        public static bool AnySymbol(Document doc, string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName)) return false;
            return new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Any(fs => fs.Category != null &&
                           string.Equals(fs.Category.Name, categoryName, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepCadPreviewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var import = MepCadShared.PickImport(doc, out int importCount);
            if (import == null)
            {
                TaskDialog.Show("MEP CAD Preview",
                    "No DWG/DXF import found. Import (not just link) the MEP drawing first, then re-run.");
                return Result.Succeeded;
            }

            var detection = new MepDetectionEngine(doc).Detect(import);

            var sb = new StringBuilder();
            sb.AppendLine($"DWG imports in document: {importCount} (previewing 1).");
            sb.AppendLine($"Entities: {detection.TotalEntities}   Blocks: {detection.TotalBlocks}   Layers: {detection.LayerCounts.Count}");
            sb.AppendLine();

            int wouldPlace = 0, wouldSkipNoFamily = 0;
            sb.AppendLine("Fixtures recognised by the map (block → category):");
            if (detection.Fixtures.Count == 0)
                sb.AppendLine("   (none — no block names matched STING_DWG_FIXTURE_MAP.json)");
            foreach (var g in detection.ByCategory())
            {
                bool hasFamily = MepCadShared.AnySymbol(doc, g.Key);
                int n = g.Count();
                if (hasFamily) wouldPlace += n; else wouldSkipNoFamily += n;
                sb.AppendLine($"   {g.Key,-22} {n,4}   {(hasFamily ? "→ would place" : "→ SKIP (no family of this category loaded)")}");
            }

            sb.AppendLine();
            sb.AppendLine($"Would place: {wouldPlace}    Would skip (no family loaded): {wouldSkipNoFamily}");

            // V2 — straight-run candidates from lines.
            sb.AppendLine();
            sb.AppendLine($"Straight runs from lines ({detection.Runs.Count} of {detection.TotalLines} lines on MEP layers):");
            if (detection.Runs.Count == 0)
                sb.AppendLine("   (none — no lines on duct/pipe/conduit/tray layers above the 0.5 m floor)");
            foreach (var (kind, count, totalM) in detection.RunsByKind())
                sb.AppendLine($"   {kind,-10} {count,4}   {totalM:F1} m");

            if (detection.UnmatchedBlockCounts.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Unmatched block names ({detection.UnmatchedBlockCounts.Count}) — add rules to _BIM_COORD/dwg_fixture_map.json:");
                foreach (var kv in detection.UnmatchedBlockCounts.OrderByDescending(k => k.Value).Take(12))
                    sb.AppendLine($"   {kv.Value,4}  {kv.Key}");
            }

            new TaskDialog("MEP CAD Preview")
            {
                MainInstruction = $"{detection.Fixtures.Count} fixture(s) · {wouldPlace} placeable · {detection.Runs.Count} run(s)",
                MainContent = sb.ToString()
            }.Show();
            StingLog.Info($"Mep_CadPreview: recognised={detection.Fixtures.Count} placeable={wouldPlace} noFamily={wouldSkipNoFamily} runs={detection.Runs.Count} unmatched={detection.UnmatchedBlockCounts.Count}");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepCadToModelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var import = MepCadShared.PickImport(doc, out int importCount);
            if (import == null)
            {
                TaskDialog.Show("MEP CAD → Model",
                    "No DWG/DXF import found. Import (not just link) the MEP drawing first, then re-run.");
                return Result.Succeeded;
            }

            var level = MepCadShared.ResolveLevel(doc);
            if (level == null) { TaskDialog.Show("MEP CAD → Model", "No levels in the project — create a level first."); return Result.Failed; }

            var detection = new MepDetectionEngine(doc).Detect(import);
            if (detection.Fixtures.Count == 0 && detection.Runs.Count == 0)
            {
                TaskDialog.Show("MEP CAD → Model",
                    "No DWG blocks matched the fixture map and no run lines were found. Run MEP CAD Preview to see " +
                    "unmatched block names + run candidates, then add rules to _BIM_COORD/dwg_fixture_map.json.");
                return Result.Succeeded;
            }

            // Confirm before writing.
            var confirm = new TaskDialog("MEP CAD → Model")
            {
                MainInstruction = $"Place {detection.Fixtures.Count} fixture(s) + {detection.Runs.Count} run(s) on level '{level.Name}'?",
                MainContent = "Fixtures with no matching family loaded are skipped + counted (no geometry synthesised). " +
                              "Runs (Duct/Pipe/Conduit/CableTray) use the first matching type/system in the project, " +
                              "sized from the layer name or a per-kind default, at a per-kind elevation above the level. " +
                              "Everything placed is workset-assigned and ISO 19650 auto-tagged.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                AllowCancellation = true
            };
            confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Place fixtures + runs");
            if (confirm.Show() != TaskDialogResult.CommandLink1) return Result.Cancelled;

            var result = new MepFixtureBuilder(doc).Place(detection, level);
            var runResult = new MepRunBuilder(doc).Build(detection.Runs, level);

            var sb = new StringBuilder();
            sb.AppendLine($"Level: {level.Name}   DWG imports: {importCount}");
            sb.AppendLine();
            sb.AppendLine("FIXTURES");
            sb.AppendLine($"   Placed:              {result.Placed}");
            sb.AppendLine($"   Skipped (no family): {result.SkippedNoSymbol}");
            sb.AppendLine($"   Failed:              {result.Failed}");
            sb.AppendLine($"   Auto-tagged:         {result.Tagged}");
            if (result.ByCategory.Count > 0)
            {
                sb.AppendLine("   By category (placed / skipped-no-family):");
                foreach (var kv in result.ByCategory.OrderByDescending(k => k.Value.placed + k.Value.skipped))
                    sb.AppendLine($"      {kv.Key,-22} {kv.Value.placed} / {kv.Value.skipped}");
            }
            sb.AppendLine();
            sb.AppendLine("RUNS");
            sb.AppendLine($"   Created:             {runResult.Created}");
            sb.AppendLine($"   Failed:              {runResult.Failed}");
            sb.AppendLine($"   Auto-tagged:         {runResult.Tagged}");
            if (runResult.ByKind.Count > 0)
            {
                sb.AppendLine("   By kind:");
                foreach (var kv in runResult.ByKind.OrderByDescending(k => k.Value))
                    sb.AppendLine($"      {kv.Key,-10} {kv.Value}");
            }

            var warns = result.Warnings.Concat(runResult.Warnings).ToList();
            if (warns.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Warnings:");
                foreach (var w in warns.Take(12)) sb.AppendLine($"   {w}");
            }

            new TaskDialog("MEP CAD → Model")
            {
                MainInstruction = $"Placed {result.Placed} fixture(s) + {runResult.Created} run(s)",
                MainContent = sb.ToString()
            }.Show();
            StingLog.Info($"Mep_CadToModel: fixtures placed={result.Placed} skippedNoFamily={result.SkippedNoSymbol} | runs created={runResult.Created} failed={runResult.Failed}");
            return Result.Succeeded;
        }
    }
}
