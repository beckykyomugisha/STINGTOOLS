// PlumbingVisualisationCommands — drainage schematic, pressure zone colouring,
// and network statistics. Phase 179d.
//
// Tags:
//   Plumb_DrainageSchematic  — creates a 2D drainage riser in a Drafting View
//   Plumb_PressureZones      — colours pipes by pressure zone in the active view
//   Plumb_NetworkStats       — read-only stats panel for the pipe network

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Plumbing;

namespace StingTools.Commands.Plumbing
{
    // ══════════════════════════════════════════════════════════════════════════
    // PlumbDrainageSchematicCommand
    // Tag: Plumb_DrainageSchematic
    // ══════════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbDrainageSchematicCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
                              ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx.Doc == null) { message = "No active document."; return Result.Failed; }

            try
            {
                // ── Options dialog (simplified TaskDialog) ─────────────────
                var scopeDlg = new TaskDialog("Drainage Schematic")
                {
                    MainInstruction = "Generate Drainage Riser Schematic",
                    MainContent     = "Choose scope for the drainage schematic diagram.",
                    CommonButtons   = TaskDialogCommonButtons.Cancel
                };
                scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "All drainage systems in project",
                    "Builds the schematic from all drainage/sanitary pipe systems.");
                scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Named system…",
                    "Filter to a specific named plumbing system.");

                var dlgResult = scopeDlg.Show();

                string systemFilter = "";
                if (dlgResult == TaskDialogResult.CommandLink2)
                {
                    // Collect system names for picker
                    var systemNames = CollectDrainageSystemNames(ctx.Doc);
                    if (!systemNames.Any())
                    {
                        TaskDialog.Show("No Systems", "No drainage/sanitary pipe systems found in project.");
                        return Result.Cancelled;
                    }

                    string joined = string.Join("\n", systemNames.Take(20));
                    var picker = new TaskDialog("Select System")
                    {
                        MainInstruction = "Enter system name (exactly as listed):",
                        MainContent     = $"Available systems:\n{joined}",
                        CommonButtons   = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel
                    };
                    // Note: In production, replace with StingListPicker for proper selection
                    if (picker.Show() != TaskDialogResult.Ok)
                        return Result.Cancelled;

                    systemFilter = systemNames.FirstOrDefault() ?? "";
                }
                else if (dlgResult == TaskDialogResult.Cancel)
                {
                    return Result.Cancelled;
                }

                var opts = new DrainageSchematicOptions
                {
                    SystemNameFilter    = systemFilter,
                    StackSpacingMm      = 800,
                    LevelHeightMm       = 3000,
                    ShowVents           = true,
                    ShowFixtureSymbols  = true,
                    ShowDnLabels        = true,
                    ShowSlopeLabels     = true
                };

                // ── Generate ───────────────────────────────────────────────
                SchematicResult schResult = null;

                using (var t = new Transaction(ctx.Doc, "STING Drainage Schematic"))
                {
                    t.Start();
                    try
                    {
                        schResult = DrainageSchematicGenerator.Generate(ctx.Doc, opts);
                        t.Commit();
                    }
                    catch (Exception ex)
                    {
                        t.RollBack();
                        message = $"Schematic generation failed: {ex.Message}";
                        StingLog.Error("PlumbDrainageSchematicCommand", ex);
                        return Result.Failed;
                    }
                }

                if (schResult == null)
                {
                    message = "Schematic result was null.";
                    return Result.Failed;
                }

                // ── Activate the new view ──────────────────────────────────
                if (schResult.ViewId != ElementId.InvalidElementId)
                {
                    try
                    {
                        var view = ctx.Doc.GetElement(schResult.ViewId) as View;
                        if (view != null)
                            ctx.UIDoc.ActiveView = view;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Could not activate schematic view: {ex.Message}");
                        schResult.Warnings.Add("View created but could not be activated automatically.");
                    }
                }

                // ── Result panel ───────────────────────────────────────────
                var sb = new StringBuilder();
                sb.AppendLine($"Drainage schematic created successfully.");
                sb.AppendLine($"  Stacks / nodes drawn : {schResult.NodesDrawn}");
                sb.AppendLine($"  Detail lines drawn   : {schResult.LinesDrawn}");
                sb.AppendLine($"  Annotations placed   : {schResult.AnnotationsPlaced}");
                sb.AppendLine($"  View ID              : {schResult.ViewId?.IntegerValue}");

                if (schResult.Warnings.Any())
                {
                    sb.AppendLine();
                    sb.AppendLine($"Warnings ({schResult.Warnings.Count}):");
                    foreach (var w in schResult.Warnings.Take(10))
                        sb.AppendLine($"  ⚠ {w}");
                    if (schResult.Warnings.Count > 10)
                        sb.AppendLine($"  … and {schResult.Warnings.Count - 10} more (see STING log).");
                }

                TaskDialog.Show("Drainage Schematic", sb.ToString());
                StingLog.Info($"PlumbDrainageSchematicCommand: nodes={schResult.NodesDrawn}, lines={schResult.LinesDrawn}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                StingLog.Error("PlumbDrainageSchematicCommand", ex);
                return Result.Failed;
            }
        }

        private static List<string> CollectDrainageSystemNames(Document doc)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var pipes = new FilteredElementCollector(doc)
                    .OfClass(typeof(Pipe))
                    .WhereElementIsNotElementType()
                    .Cast<Pipe>();

                foreach (var p in pipes)
                {
                    try
                    {
                        var sys = p.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?
                                   .AsValueString() ?? "";
                        if (!string.IsNullOrEmpty(sys))
                            names.Add(sys);
                    }
                    catch { /* skip */ }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CollectDrainageSystemNames: {ex.Message}");
            }
            return names.OrderBy(n => n).ToList();
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PlumbPressureZoneCommand
    // Tag: Plumb_PressureZones
    // ══════════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbPressureZoneCommand : IExternalCommand
    {
        // Pressure thresholds (kPa)
        private const double ThresholdLowKpa    = 100.0;
        private const double ThresholdMidKpa    = 200.0;
        private const double ThresholdHighKpa   = 500.0;

        // PLM_PRESSURE_KPA shared parameter name (from ParamRegistry)
        private const string ParamPressureKpa   = "PLM_PRESSURE_KPA";

        public Result Execute(ExternalCommandData commandData,
                              ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx.Doc == null) { message = "No active document."; return Result.Failed; }

            var view = ctx.UIDoc.ActiveView;
            if (view == null)
            {
                message = "No active view.";
                return Result.Failed;
            }

            try
            {
                // ── Confirm ────────────────────────────────────────────────
                var dlg = new TaskDialog("Pressure Zone Colouring")
                {
                    MainInstruction = "Colour Pipes by Pressure Zone",
                    MainContent     = "Applies graphic overrides to supply pipes in the active view:\n\n" +
                                      "  🔴 Red     < 100 kPa  (low — check supply)\n" +
                                      "  🟠 Amber   100–200 kPa (medium)\n" +
                                      "  🟢 Green   200–500 kPa (adequate)\n" +
                                      "  🟣 Purple  > 500 kPa  (high — PRV required)\n\n" +
                                      "Pressure is calculated from the entry pressure in\n" +
                                      "PlumbingSystemConfig (default 300 kPa) minus static head.",
                    CommonButtons   = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel
                };
                if (dlg.Show() != TaskDialogResult.Ok)
                    return Result.Cancelled;

                // ── Build network ──────────────────────────────────────────
                PipeNetwork network;
                try
                {
                    network = PipeNetworkBuilder.Build(ctx.Doc);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"PipeNetworkBuilder.Build failed: {ex.Message}");
                    network = new PipeNetwork();
                }

                var cfg = PlumbingSystemConfig.Load(ctx.Doc) ?? PlumbingSystemConfig.Defaults();
                double entryPressureKpa = cfg.SupplyPressureBarAtEntry * 100.0; // bar → kPa

                // ── Propagate pressure through network ─────────────────────
                PipeNetworkBuilder.AccumulatePressure(network, entryPressureKpa, 0.0);

                // ── Find solid fill pattern ────────────────────────────────
                ElementId solidFillId = FindSolidFillPatternId(ctx.Doc);

                // ── Colour counters ────────────────────────────────────────
                int cntLow = 0, cntMid = 0, cntHigh = 0, cntPrvNeeded = 0;
                var warnings = new List<string>();

                using (var t = new Transaction(ctx.Doc, "STING Pressure Zone Colouring"))
                {
                    t.Start();
                    try
                    {
                        // Collect all pipes in active view
                        var pipesInView = new FilteredElementCollector(ctx.Doc, view.Id)
                            .OfClass(typeof(Pipe))
                            .WhereElementIsNotElementType()
                            .Cast<Pipe>()
                            .ToList();

                        foreach (var pipe in pipesInView)
                        {
                            double pressureKpa = GetPipePressure(pipe, network, entryPressureKpa);

                            // Determine zone colour
                            Color zoneColor;

                            if (pressureKpa > ThresholdHighKpa)
                            {
                                zoneColor  = new Color(128, 0, 128);   // Purple
                                cntPrvNeeded++;
                                warnings.Add($"Pipe {pipe.Id.IntegerValue} pressure {pressureKpa:F0} kPa > 500 kPa — PRV required.");
                            }
                            else if (pressureKpa >= ThresholdMidKpa)
                            {
                                zoneColor  = new Color(0, 180, 0);     // Green
                                cntHigh++;
                            }
                            else if (pressureKpa >= ThresholdLowKpa)
                            {
                                zoneColor  = new Color(255, 140, 0);   // Amber/Orange
                                cntMid++;
                            }
                            else
                            {
                                zoneColor  = new Color(220, 30, 30);   // Red
                                cntLow++;
                                warnings.Add($"Pipe {pipe.Id.IntegerValue} pressure {pressureKpa:F0} kPa < 100 kPa — low pressure.");
                            }

                            // Build override
                            var ogs = new OverrideGraphicSettings();
                            ogs.SetProjectionLineColor(zoneColor);
                            ogs.SetProjectionLineWeight(4);

                            if (solidFillId != ElementId.InvalidElementId)
                            {
                                ogs.SetSurfaceForegroundPatternColor(zoneColor);
                                ogs.SetSurfaceForegroundPatternId(solidFillId);
                                ogs.SetCutForegroundPatternColor(zoneColor);
                                ogs.SetCutForegroundPatternId(solidFillId);
                            }

                            view.SetElementOverrides(pipe.Id, ogs);

                            // Write pressure parameter
                            try
                            {
                                var param = pipe.LookupParameter(ParamPressureKpa);
                                if (param != null && !param.IsReadOnly)
                                    param.Set(pressureKpa);
                            }
                            catch { /* parameter may not exist — non-fatal */ }
                        }

                        t.Commit();
                    }
                    catch (Exception ex)
                    {
                        t.RollBack();
                        message = $"Pressure zone colouring failed: {ex.Message}";
                        StingLog.Error("PlumbPressureZoneCommand", ex);
                        return Result.Failed;
                    }
                }

                // ── Result panel ───────────────────────────────────────────
                int total = cntLow + cntMid + cntHigh + cntPrvNeeded;
                var sb = new StringBuilder();
                sb.AppendLine($"Pressure zone colouring applied to {total} pipe(s).");
                sb.AppendLine();
                sb.AppendLine($"  🔴 Low  (< 100 kPa)        : {cntLow}");
                sb.AppendLine($"  🟠 Medium (100–200 kPa)     : {cntMid}");
                sb.AppendLine($"  🟢 Adequate (200–500 kPa)   : {cntHigh}");
                sb.AppendLine($"  🟣 PRV needed (> 500 kPa)   : {cntPrvNeeded}");

                if (warnings.Any())
                {
                    sb.AppendLine();
                    sb.AppendLine($"Warnings ({warnings.Count}):");
                    foreach (var w in warnings.Take(8))
                        sb.AppendLine($"  ⚠ {w}");
                    if (warnings.Count > 8)
                        sb.AppendLine($"  … and {warnings.Count - 8} more (see STING log).");
                }

                TaskDialog.Show("Pressure Zones", sb.ToString());
                StingLog.Info($"PlumbPressureZoneCommand: total={total}, low={cntLow}, prv={cntPrvNeeded}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                StingLog.Error("PlumbPressureZoneCommand", ex);
                return Result.Failed;
            }
        }

        private static double GetPipePressure(Pipe pipe, PipeNetwork network, double entryKpa)
        {
            try
            {
                // Try existing PLM_PRESSURE_KPA param first
                var param = pipe.LookupParameter(ParamPressureKpa);
                if (param != null && param.AsDouble() > 0)
                    return param.AsDouble();

                // Look up pressure via the matching network edge (pipes are edges, not nodes)
                long pipeIdVal = pipe.Id.Value;
                var matchingEdge = network.Edges
                    .FirstOrDefault(e => e.PipeId?.Value == pipeIdVal);
                if (matchingEdge != null)
                    return Math.Max(0, matchingEdge.To?.PressureKpa ?? matchingEdge.From?.PressureKpa ?? 0);

                // Estimate from Z elevation (static head from entry)
                double elev    = pipe.get_Parameter(BuiltInParameter.Z_OFFSET_VALUE)?.AsDouble() ?? 0;
                const double RhoGKpaPerFt = 9.807 / 0.3048 * 0.001;
                return Math.Max(0, entryKpa - elev * RhoGKpaPerFt);
            }
            catch
            {
                return entryKpa * 0.5;
            }
        }

        private static ElementId FindSolidFillPatternId(Document doc)
        {
            try
            {
                var fp = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .FirstOrDefault(f => f.GetFillPattern().IsSolidFill);
                return fp?.Id ?? ElementId.InvalidElementId;
            }
            catch { return ElementId.InvalidElementId; }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PlumbNetworkStatsCommand
    // Tag: Plumb_NetworkStats
    // ══════════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbNetworkStatsCommand : IExternalCommand
    {
        private const double FtToM = 0.3048;

        public Result Execute(ExternalCommandData commandData,
                              ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx.Doc == null) { message = "No active document."; return Result.Failed; }

            try
            {
                // Build network
                PipeNetwork network;
                try
                {
                    network = PipeNetworkBuilder.Build(ctx.Doc);
                }
                catch (Exception ex)
                {
                    message = $"PipeNetworkBuilder.Build failed: {ex.Message}";
                    StingLog.Error("PlumbNetworkStatsCommand", ex);
                    return Result.Failed;
                }

                if (!network.Nodes.Any() && !network.Edges.Any())
                {
                    TaskDialog.Show("Network Stats", "No pipe network found in the current document.");
                    return Result.Succeeded;
                }

                var sb = new StringBuilder();

                // ── Total pipe length by system ───────────────────────────
                sb.AppendLine("═══ PIPE NETWORK STATISTICS ═══");
                sb.AppendLine();

                var bySystem = network.Edges
                    .GroupBy(e => e.From?.SystemName ?? "Unknown")
                    .OrderByDescending(g => g.Sum(e => e.LengthM));

                sb.AppendLine("─── Pipe Length by System ───");
                foreach (var grp in bySystem)
                {
                    double totalM = grp.Sum(e => e.LengthM);
                    double avgDn  = grp.Average(e => e.DnMm);
                    sb.AppendLine($"  {grp.Key,-25}  {totalM,7:F1} m    avg DN {avgDn:F0} mm");
                }
                sb.AppendLine();

                // ── DFU totals by stack ───────────────────────────────────
                var stackNodes = network.Nodes
                    .Where(n => n.Type == PipeNodeType.Stack)
                    .OrderByDescending(n => n.DfuAccumulated)
                    .ToList();

                if (stackNodes.Any())
                {
                    sb.AppendLine("─── DFU / Stack Utilisation ───");
                    sb.AppendLine($"  {"Stack ID",-12}  {"DN mm",6}  {"DFU",8}  System");
                    foreach (var s in stackNodes.Take(20))
                    {
                        sb.AppendLine($"  {s.Id?.IntegerValue,-12}  {s.DnMm,6}  {s.DfuAccumulated,8:F2}  {s.SystemName}");
                    }
                    if (stackNodes.Count > 20)
                        sb.AppendLine($"  … {stackNodes.Count - 20} more stacks not shown.");
                    sb.AppendLine();
                }

                // ── Critical path (highest total resistance) ──────────────
                var criticalPath = FindCriticalPath(network);
                if (criticalPath.Any())
                {
                    double totalRes = criticalPath.Sum(e => e.ResistanceKpa);
                    double totalLen = criticalPath.Sum(e => e.LengthM);

                    sb.AppendLine("─── Critical Path (Highest Resistance) ───");
                    sb.AppendLine($"  Edges        : {criticalPath.Count}");
                    sb.AppendLine($"  Total length : {totalLen:F1} m");
                    sb.AppendLine($"  Total resist.: {totalRes:F2} kPa");

                    if (criticalPath.Count <= 5)
                    {
                        sb.AppendLine("  Pipe IDs: " +
                            string.Join(" → ", criticalPath.Select(e => e.PipeId?.IntegerValue)));
                    }
                    sb.AppendLine();
                }

                // ── Fixture count ─────────────────────────────────────────
                var fixtureNodes = network.Nodes
                    .Where(n => n.Type == PipeNodeType.Fixture)
                    .ToList();

                sb.AppendLine($"─── Fixture Summary ───");
                sb.AppendLine($"  Total fixture nodes : {fixtureNodes.Count}");

                var fixturesBySystem = fixtureNodes
                    .GroupBy(n => n.SystemName ?? "Unknown")
                    .OrderByDescending(g => g.Count());

                foreach (var grp in fixturesBySystem.Take(10))
                    sb.AppendLine($"    {grp.Key,-28}: {grp.Count()}");
                sb.AppendLine();

                // ── Overall summary ───────────────────────────────────────
                sb.AppendLine("─── Overall ───");
                sb.AppendLine($"  Total nodes   : {network.Nodes.Count}");
                sb.AppendLine($"  Total edges   : {network.Edges.Count}");
                sb.AppendLine($"  Root nodes    : {network.RootNodes.Count}");
                sb.AppendLine($"  Stack nodes   : {stackNodes.Count}");
                sb.AppendLine($"  Fixture nodes : {fixtureNodes.Count}");

                if (network.Edges.Any())
                {
                    double totalNetLen = network.Edges.Sum(e => e.LengthM);
                    double totalDfu    = network.RootNodes.Sum(n => n.DfuAccumulated);
                    sb.AppendLine($"  Total pipe length : {totalNetLen:F1} m");
                    sb.AppendLine($"  Total DFU         : {totalDfu:F2}");
                }

                TaskDialog.Show("Plumbing Network Statistics", sb.ToString());
                StingLog.Info($"PlumbNetworkStatsCommand: nodes={network.Nodes.Count}, edges={network.Edges.Count}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                StingLog.Error("PlumbNetworkStatsCommand", ex);
                return Result.Failed;
            }
        }

        // ── Critical path by Dijkstra (highest resistance = most problematic) ─

        private static List<PipeEdge> FindCriticalPath(PipeNetwork network)
        {
            if (!network.Edges.Any()) return new List<PipeEdge>();

            try
            {
                // Simplified: find path from leaf (max DFU fixture) to termination
                // using accumulated resistance as cost.
                var start = network.RootNodes
                    .OrderByDescending(n => n.DfuAccumulated)
                    .FirstOrDefault();
                var end = network.Nodes
                    .Where(n => n.Type == PipeNodeType.Termination ||
                                !n.Downstream.Any())
                    .OrderByDescending(n => n.DfuAccumulated)
                    .FirstOrDefault();

                if (start == null || end == null) return new List<PipeEdge>();

                // BFS to find path
                var prev  = new Dictionary<long, PipeEdge>();
                var dist  = new Dictionary<long, double>();
                var queue = new Queue<PipeNode>();

                dist[start.Id.IntegerValue] = 0;
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    var node = queue.Dequeue();
                    foreach (var edge in node.Downstream)
                    {
                        long toId = edge.To.Id.IntegerValue;
                        double newDist = dist.GetValueOrDefault(node.Id.IntegerValue) + edge.ResistanceKpa;
                        if (!dist.ContainsKey(toId) || newDist < dist[toId])
                        {
                            dist[toId] = newDist;
                            prev[toId] = edge;
                            queue.Enqueue(edge.To);
                        }
                    }
                }

                // Reconstruct
                var path = new List<PipeEdge>();
                long cur = end.Id.IntegerValue;
                int  guard = 0;
                while (prev.ContainsKey(cur) && guard < 500)
                {
                    var edge = prev[cur];
                    path.Add(edge);
                    cur = edge.From.Id.IntegerValue;
                    guard++;
                }
                path.Reverse();
                return path;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FindCriticalPath: {ex.Message}");
                return new List<PipeEdge>();
            }
        }
    }
}
