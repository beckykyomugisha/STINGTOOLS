// ============================================================================
// MepCadCommands.cs — Phase: MEP-from-DWG V1.
//
// MODEL-tab command surface for MEP-from-DWG, peer to the structural StrCAD*
// commands. Both reuse the shared CADToModelEngine extraction core via the
// MepDetectionEngine / MepFixtureBuilder pipeline (Core/Cad/Mep).
//
//   Mep_CadPreview  (ReadOnly) — audit: per-layer counts + which DWG blocks
//                   would place as which fixture vs skip (no rule / no family).
//   Mep_CadToModel  (Manual)   — place MEP fixtures from DWG block references.
//
// V1 places fixtures from blocks only. Straight runs (Duct/Pipe/Conduit/Tray),
// fixture host-snapping, and the per-layer wizard are V2.
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
            if (detection.UnmatchedBlockCounts.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Unmatched block names ({detection.UnmatchedBlockCounts.Count}) — add rules to _BIM_COORD/dwg_fixture_map.json:");
                foreach (var kv in detection.UnmatchedBlockCounts.OrderByDescending(k => k.Value).Take(12))
                    sb.AppendLine($"   {kv.Value,4}  {kv.Key}");
            }

            new TaskDialog("MEP CAD Preview")
            {
                MainInstruction = $"{detection.Fixtures.Count} fixture(s) recognised · {wouldPlace} placeable",
                MainContent = sb.ToString()
            }.Show();
            StingLog.Info($"Mep_CadPreview: recognised={detection.Fixtures.Count} placeable={wouldPlace} noFamily={wouldSkipNoFamily} unmatched={detection.UnmatchedBlockCounts.Count}");
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
            if (detection.Fixtures.Count == 0)
            {
                TaskDialog.Show("MEP CAD → Model",
                    "No DWG blocks matched the fixture map. Run MEP CAD Preview to see the unmatched block names, " +
                    "then add rules to _BIM_COORD/dwg_fixture_map.json.");
                return Result.Succeeded;
            }

            // Confirm before writing.
            var confirm = new TaskDialog("MEP CAD → Model")
            {
                MainInstruction = $"Place {detection.Fixtures.Count} MEP fixture(s) on level '{level.Name}'?",
                MainContent = "Fixtures with no matching family loaded are skipped and counted (no geometry is synthesised). " +
                              "Placed fixtures are workset-assigned and ISO 19650 auto-tagged.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                AllowCancellation = true
            };
            confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Place fixtures");
            if (confirm.Show() != TaskDialogResult.CommandLink1) return Result.Cancelled;

            var result = new MepFixtureBuilder(doc).Place(detection, level);

            var sb = new StringBuilder();
            sb.AppendLine($"Level: {level.Name}   DWG imports: {importCount}");
            sb.AppendLine();
            sb.AppendLine($"Placed:              {result.Placed}");
            sb.AppendLine($"Skipped (no family): {result.SkippedNoSymbol}");
            sb.AppendLine($"Failed:              {result.Failed}");
            sb.AppendLine($"Auto-tagged:         {result.Tagged}");
            if (result.ByCategory.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("By category (placed / skipped-no-family):");
                foreach (var kv in result.ByCategory.OrderByDescending(k => k.Value.placed + k.Value.skipped))
                    sb.AppendLine($"   {kv.Key,-22} {kv.Value.placed} / {kv.Value.skipped}");
            }
            if (result.Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Warnings:");
                foreach (var w in result.Warnings.Take(10)) sb.AppendLine($"   {w}");
            }

            new TaskDialog("MEP CAD → Model")
            {
                MainInstruction = $"Placed {result.Placed} fixture(s), skipped {result.SkippedNoSymbol}",
                MainContent = sb.ToString()
            }.Show();
            StingLog.Info($"Mep_CadToModel: placed={result.Placed} skippedNoFamily={result.SkippedNoSymbol} failed={result.Failed} tagged={result.Tagged}");
            return Result.Succeeded;
        }
    }
}
