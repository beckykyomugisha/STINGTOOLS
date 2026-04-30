// StingTools — Phase 168 — Match-line commands
//
// Four IExternalCommand entry points wired to the MatchLineEngine:
//
//   MatchLine_Generate  — full-project sweep. Walks the scope-box
//                         adjacency graph, places paired DetailCurves
//                         on every adjacent (view, view) pair, stamps
//                         STING_MATCH_REF / GUID / DIR. Idempotent —
//                         re-runs find existing pairs and update in
//                         place. Prunes orphans by default.
//
//   MatchLine_Sync      — same as Generate but ForceRestamp = true so
//                         every pair re-resolves its sheet ref. Use
//                         after BatchRenumberSheetsCommand.
//
//   MatchLine_Validate  — read-only audit. Reports orphan curves,
//                         broken sheet refs, missing pairs.
//
//   MatchLine_Inspect   — read-only diagnostic. Lists every adjacency
//                         edge the engine sees, with direction +
//                         scope-box names — useful for verifying the
//                         scope-box graph before generation.

using System;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MatchLineGenerateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var r = MatchLineEngine.Run(ctx.Doc, new MatchLineRunOptions
            {
                PruneOrphans = true,
                ForceRestamp = false,
            });

            var sb = new StringBuilder();
            sb.AppendLine("Match-line sweep complete.");
            sb.AppendLine();
            sb.AppendLine($"Scope boxes scanned     : {r.ScopeBoxesScanned}");
            sb.AppendLine($"Adjacency edges found   : {r.AdjacencyEdgesFound}");
            sb.AppendLine($"Pairs created           : {r.PairsCreated}");
            sb.AppendLine($"Pairs updated           : {r.PairsUpdated}");
            sb.AppendLine($"Pairs skipped (current) : {r.PairsSkipped}");
            sb.AppendLine($"Tip captions placed     : {r.TipCaptionsPlaced}");
            if (r.Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Warnings ({r.Warnings.Count}):");
                foreach (var w in r.Warnings.Take(20)) sb.AppendLine("  · " + w);
                if (r.Warnings.Count > 20) sb.AppendLine($"  … +{r.Warnings.Count - 20} more (StingTools.log)");
            }
            if (r.Errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Errors ({r.Errors.Count}):");
                foreach (var e in r.Errors.Take(10)) sb.AppendLine("  ✗ " + e);
            }

            foreach (var w in r.Warnings) StingLog.Warn($"MatchLineGenerate: {w}");
            foreach (var e in r.Errors)   StingLog.Error($"MatchLineGenerate: {e}");

            TaskDialog.Show("STING — Match Lines", sb.ToString());
            return r.Errors.Count == 0 ? Result.Succeeded : Result.Failed;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MatchLineSyncCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var r = MatchLineEngine.Sync(ctx.Doc, new MatchLineRunOptions
            {
                PruneOrphans = true,
                ForceRestamp = true,
            });

            var sb = new StringBuilder();
            sb.AppendLine("Match-line force-restamp complete.");
            sb.AppendLine();
            sb.AppendLine($"Pairs created           : {r.PairsCreated}");
            sb.AppendLine($"Pairs updated           : {r.PairsUpdated}");
            sb.AppendLine($"Pairs skipped           : {r.PairsSkipped}");
            sb.AppendLine($"Captions placed         : {r.TipCaptionsPlaced}");
            sb.AppendLine();
            sb.AppendLine("Use this after BatchRenumberSheetsCommand /");
            sb.AppendLine("BatchCloneSheetsCommand to refresh every");
            sb.AppendLine("STING_MATCH_REF stamp against the latest sheet refs.");
            if (r.Warnings.Count > 0)
                sb.AppendLine($"\n{r.Warnings.Count} warnings — see StingTools.log");
            foreach (var w in r.Warnings) StingLog.Warn($"MatchLineSync: {w}");
            foreach (var e in r.Errors)   StingLog.Error($"MatchLineSync: {e}");

            TaskDialog.Show("STING — Match Line Sync", sb.ToString());
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MatchLineValidateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var rep = MatchLineEngine.Validate(ctx.Doc);

            var sb = new StringBuilder();
            sb.AppendLine("Match-line validation report.");
            sb.AppendLine();
            sb.AppendLine($"Pairs total             : {rep.PairsTotal}");
            sb.AppendLine($"Pairs with valid ref    : {rep.PairsWithMatchingRef}");
            sb.AppendLine($"Pairs with broken ref   : {rep.PairsWithBrokenRef}");
            sb.AppendLine($"Adjacent scope-box pairs: {rep.ScopeBoxesAdjacent}");
            int expected = rep.ScopeBoxesAdjacent;
            int placed   = rep.PairsTotal;
            if (expected > placed)
                sb.AppendLine($"\n⚠  {expected - placed} adjacency edge(s) missing a placed pair — run 'Match Lines: Generate'.");
            else if (placed > expected)
                sb.AppendLine($"\n⚠  {placed - expected} placed pair(s) have no live adjacency — run 'Match Lines: Generate' (it prunes by default).");
            else
                sb.AppendLine("\n✓  Pair count matches adjacency edge count.");

            if (rep.Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Issues ({rep.Warnings.Count}):");
                foreach (var w in rep.Warnings.Take(25)) sb.AppendLine("  · " + w);
                if (rep.Warnings.Count > 25) sb.AppendLine($"  … +{rep.Warnings.Count - 25} more (StingTools.log)");
            }

            foreach (var w in rep.Warnings) StingLog.Warn($"MatchLineValidate: {w}");
            TaskDialog.Show("STING — Match Line Validate", sb.ToString());
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MatchLineInspectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var edges = MatchLineEngine.InspectAdjacency(ctx.Doc);
            var sb = new StringBuilder();
            sb.AppendLine($"Match-line adjacency inspection.");
            sb.AppendLine($"Edges discovered: {edges.Count}");
            sb.AppendLine();
            int n = 0;
            foreach (var e in edges.Take(40))
            {
                n++;
                sb.AppendLine($"{n,3}. [{e.Direction,-10}] {e.ScopeBoxA?.Name}  ↔  {e.ScopeBoxB?.Name}");
                sb.AppendLine($"     pair-GUID  {e.PairGuid}");
                sb.AppendLine($"     line       ({e.LineStart.X:F2}, {e.LineStart.Y:F2}) → ({e.LineEnd.X:F2}, {e.LineEnd.Y:F2})  (ft)");
            }
            if (edges.Count > 40) sb.AppendLine($"… +{edges.Count - 40} more.");

            sb.AppendLine();
            sb.AppendLine("Tolerance config (mm): coplanar 1.0, min overlap 100.0,");
            sb.AppendLine("extension 25.0 beyond crop. Edit STING_MATCH_LINES.json");
            sb.AppendLine("to tune; project override at <project>/_BIM_COORD/match_lines.json.");

            TaskDialog.Show("STING — Match Line Inspect", sb.ToString());
            return Result.Succeeded;
        }
    }
}
