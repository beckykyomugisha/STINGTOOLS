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
using StingTools.UI;

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
                // In a workflow preset nobody can answer it: all drainage systems.
                var dlgResult = TaskDialogResult.CommandLink1;
                if (!PresetDialog.Quiet)
                {
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
                    dlgResult = scopeDlg.Show();
                }

                var opts = new DrainageSchematicOptions
                {
                    StackSpacingMm      = 2000,
                    ShowVents           = true,
                    ShowFixtureSymbols  = true,
                    ShowDnLabels        = true,
                    ShowSlopeLabels     = true
                };

                string systemFilter = "";
                if (dlgResult == TaskDialogResult.CommandLink2)
                {
                    // Collect system names for picker
                    var systemNames = CollectDrainageSystemNames(ctx.Doc, opts.Classifications);
                    if (!systemNames.Any())
                    {
                        TaskDialog.Show("No Systems", "No drainage/sanitary pipe systems found in project.");
                        return Result.Cancelled;
                    }

                    // A real picker: the old "Select System" box only had OK / Cancel and then
                    // used the FIRST system whatever was chosen.
                    var picked = StingTools.Select.StingListPicker.Show(
                        "Drainage Schematic — system", "Pick the drainage / sanitary system to draw.",
                        systemNames.ToList());
                    if (string.IsNullOrEmpty(picked))
                        return Result.Cancelled;

                    systemFilter = picked;
                }
                else if (dlgResult == TaskDialogResult.Cancel)
                {
                    return Result.Cancelled;
                }

                opts.SystemNameFilter = systemFilter;

                // DTW-120: fit the drawing to the slot of the sheet it goes on.
                if (StingTools.Core.Drawing.SchematicViewFactory.TryGetSlotPaperSize(ctx.Doc,
                        StingTools.Core.Drawing.DrawingRouteRequests.DrainageSchematic,
                        out double slotW, out double slotH, out int typeScale, out string slotNote))
                {
                    opts.SlotWidthMm = slotW;
                    opts.SlotHeightMm = slotH;
                }
                else StingLog.Warn($"PlumbDrainageSchematicCommand: no slot size ({slotNote}) — drawn without a fit check.");
                if (typeScale > 0) opts.MinScale = typeScale;

                // ── Generate ───────────────────────────────────────────────
                SchematicResult schResult = null;

                using (var t = new Transaction(ctx.Doc, "STING Drainage Schematic"))
                {
                    t.Start();
                    try
                    {
                        schResult = DrainageSchematicGenerator.Generate(ctx.Doc, opts);
                        // Nothing real drawn: keep no empty drafting view, say why.
                        if (schResult == null || schResult.ViewId == ElementId.InvalidElementId || schResult.NodesDrawn == 0)
                        {
                            t.RollBack();
                            string why = schResult?.Warnings.FirstOrDefault() ?? "no drainage stack was found to draw.";
                            PresetDialog.Show("Drainage Schematic",
                                "No drainage schematic was drawn: " + why +
                                "\n\nModel the sanitary / vent pipework (systems classified Sanitary or Vent) and re-run.",
                                ref message);
                            return Result.Cancelled;
                        }
                        // DTW-124: a rolled-back commit (a failure handler, a regeneration
                        // error) used to be reported as "created successfully".
                        var status = t.Commit();
                        if (status != TransactionStatus.Committed)
                        {
                            message = $"The drainage schematic was not saved: the transaction ended {status}.";
                            StingLog.Warn($"PlumbDrainageSchematicCommand: commit returned {status}.");
                            return Result.Failed;
                        }
                    }
                    catch (Exception ex)
                    {
                        t.RollBack();
                        message = $"Schematic generation failed: {ex.Message}";
                        StingLog.Error("PlumbDrainageSchematicCommand", ex);
                        return Result.Failed;
                    }
                }

                var schView = ctx.Doc.GetElement(schResult.ViewId) as View;

                // Onto the sheet of the drawing type routing gives P / DRAINAGE_SCHEMATIC
                // (stamped; a re-run's diagram replaces this one there).
                string sheetLine = StingTools.Core.SLD.SldSheetPlacement.Place(ctx.Doc,
                    StingTools.Core.Drawing.DrawingRouteRequests.DrainageSchematic, schView);

                // ── Activate the new view ──────────────────────────────────
                if (schView != null && !PresetDialog.Quiet)
                {
                    try { ctx.UIDoc.ActiveView = schView; }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Could not activate schematic view: {ex.Message}");
                        schResult.Warnings.Add("View created but could not be activated automatically.");
                    }
                }

                // ── Result panel ───────────────────────────────────────────
                var sb = new StringBuilder();
                sb.AppendLine($"Drainage schematic created successfully.");
                sb.AppendLine($"  {sheetLine}");
                sb.AppendLine($"  Scale                : 1:{schResult.Scale}");
                sb.AppendLine($"  Stacks drawn         : {schResult.NodesDrawn}");
                sb.AppendLine($"  Branches drawn       : {schResult.BranchesDrawn}");
                sb.AppendLine($"  Vents drawn          : {schResult.VentsDrawn}");
                sb.AppendLine($"  Levels labelled      : {schResult.LevelsLabelled}");
                sb.AppendLine($"  Detail lines drawn   : {schResult.LinesDrawn}");
                sb.AppendLine($"  Annotations placed   : {schResult.AnnotationsPlaced}");
                sb.AppendLine($"  View ID              : {schResult.ViewId?.Value}");

                if (schResult.Warnings.Any())
                {
                    sb.AppendLine();
                    sb.AppendLine($"Warnings ({schResult.Warnings.Count}):");
                    foreach (var w in schResult.Warnings.Take(10))
                        sb.AppendLine($"  ⚠ {w}");
                    if (schResult.Warnings.Count > 10)
                        sb.AppendLine($"  … and {schResult.Warnings.Count - 10} more (see STING log).");
                }

                PresetDialog.Show("Drainage Schematic", sb.ToString(), ref message);
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

        /// <summary>
        /// Names of the PipingSystem instances classified as drainage (Sanitary). These
        /// are the names the schematic's system filter matches — exactly — against each
        /// pipe's MEPSystem. (The picker used to list piping-system TYPE names of every
        /// pipe, cold water included, one parameter read per pipe; a type name never
        /// equals an instance name, so a pick could draw nothing.) Vent systems are not
        /// offered: the schematic draws stacks, and finds each stack's vent itself.
        /// </summary>
        private static List<string> CollectDrainageSystemNames(Document doc, ICollection<PipeSystemType> classifications)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var systems = new FilteredElementCollector(doc)
                    .OfClass(typeof(PipingSystem))
                    .WhereElementIsNotElementType()
                    .Cast<PipingSystem>();

                foreach (var ps in systems)
                {
                    try
                    {
                        if (ps.SystemType == PipeSystemType.Vent) continue;
                        if (classifications != null && classifications.Count > 0 && !classifications.Contains(ps.SystemType))
                            continue;
                        if (!string.IsNullOrWhiteSpace(ps.Name))
                            names.Add(ps.Name.Trim());
                    }
                    catch (Exception ex) { StingLog.Warn($"CollectDrainageSystemNames: system {ps.Id}: {ex.Message}"); }
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

                        string pressureParam = ParamRegistry.PLM_PRESSURE_KPA;
                        foreach (var pipe in pipesInView)
                        {
                            double pressureKpa = GetPipePressure(pipe, network, entryPressureKpa);

                            // Determine zone colour
                            Color zoneColor;

                            if (pressureKpa > ThresholdHighKpa)
                            {
                                zoneColor  = new Color(128, 0, 128);   // Purple
                                cntPrvNeeded++;
                                warnings.Add($"Pipe {pipe.Id.Value} pressure {pressureKpa:F0} kPa > 500 kPa — PRV required.");
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
                                warnings.Add($"Pipe {pipe.Id.Value} pressure {pressureKpa:F0} kPa < 100 kPa — low pressure.");
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

                            // Write pressure parameter — one lookup, storage-aware. PLM_PRESSURE_KPA
                            // is TEXT in MR_PARAMETERS.txt, so Set(double) always threw and the
                            // stamp was never written.
                            try
                            {
                                var param = pipe.LookupParameter(pressureParam);
                                if (param != null && !param.IsReadOnly)
                                {
                                    if (param.StorageType == StorageType.String)
                                        param.Set(pressureKpa.ToString("F0", System.Globalization.CultureInfo.InvariantCulture));
                                    else if (param.StorageType == StorageType.Double)
                                        param.Set(pressureKpa);
                                    else if (param.StorageType == StorageType.Integer)
                                        param.Set((int)Math.Round(pressureKpa));
                                }
                            }
                            catch (Exception ex) { StingLog.Warn($"PlumbPressureZone: stamp pipe {pipe.Id}: {ex.Message}"); }
                        }

                        t.Commit();
                    }
                    catch (Exception ex2)
                    {
                        t.RollBack();
                        message = $"Pressure zone colouring failed: {ex2.Message}";
                        StingLog.Error("PlumbPressureZoneCommand", ex2);
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
                // The pipe's own network node — an O(1) lookup in the network's id index
                // (this was a linear scan of every edge per pipe). The PLM_PRESSURE_KPA
                // stamp is not read back: it is this command's own output, and reading it
                // first would freeze the first run's values forever.
                if (network.ById.TryGetValue(pipe.Id.Value, out var node)
                    && (node.Upstream.Count + node.Downstream.Count) > 0)
                    return Math.Max(0, node.PressureKpa);

                // Estimate from Z elevation (static head from entry)
                double elev    = pipe.get_Parameter(BuiltInParameter.Z_OFFSET_VALUE)?.AsDouble() ?? 0;
                // ρg = 9.807 kPa per metre of head = 9.807 × 0.3048 kPa per foot
                // (was 9.807 / 0.3048 × 0.001 — about 93× too small).
                const double RhoGKpaPerFt = 9.807 * 0.3048;
                return Math.Max(0, entryKpa - elev * RhoGKpaPerFt);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlumbPressureZone: pressure of pipe {pipe?.Id}: {ex.Message}");
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
                        sb.AppendLine($"  {s.Id?.Value,-12}  {s.DnMm,6}  {s.DfuAccumulated,8:F2}  {s.SystemName}");
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
                            string.Join(" → ", criticalPath.Select(e => e.PipeId?.Value)));
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

                dist[start.Id.Value] = 0;
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    var node = queue.Dequeue();
                    foreach (var edge in node.Downstream)
                    {
                        long toId = edge.To.Id.Value;
                        double newDist = dist.GetValueOrDefault(node.Id.Value) + edge.ResistanceKpa;
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
                long cur = end.Id.Value;
                int  guard = 0;
                while (prev.ContainsKey(cur) && guard < 500)
                {
                    var edge = prev[cur];
                    path.Add(edge);
                    cur = edge.From.Id.Value;
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

    // ══════════════════════════════════════════════════════════════════════════
    // PlumbSupplySchematicCommand
    // Tag: Plumb_SupplySchematic
    // Phase 187 — supply-side companion to Plumb_DrainageSchematic, borrowed
    // from Plumber (HidraSoftware). Generates an index-leg riser with PRV /
    // water-meter / pump symbols, kPa labels at each fixture, optional DXF.
    // ══════════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbSupplySchematicCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
                              ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.Doc == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // Scope picker — in a workflow preset nobody can answer it: schematic only.
            bool exportDxf = false;
            if (!PresetDialog.Quiet)
            {
                var scopeDlg = new TaskDialog("Supply Schematic")
                {
                    MainInstruction = "Generate Cold Water Supply Schematic",
                    MainContent     = "Draws the domestic cold water network (pipes on systems classified " +
                                      "Domestic Cold Water) and places it on the DCW schematic sheet.",
                    CommonButtons   = TaskDialogCommonButtons.Cancel
                };
                scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Schematic only (drafting view)");
                scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Schematic + export DXF",
                    "Drops a .dxf into the project's DXF export folder alongside the view.");
                var pick = scopeDlg.Show();
                if (pick == TaskDialogResult.Cancel) return Result.Cancelled;
                exportDxf = pick == TaskDialogResult.CommandLink2;
            }

            // Read inlet pressure + DXF target version from project config
            var cfg = PlumbingSystemConfig.Load(doc);
            double inletKpa = Math.Max(0, cfg.SupplyPressureBarAtEntry) * 100.0;
            // The inlet pressure counts as set only when the project has a saved plumbing
            // configuration; otherwise it is the class default and must not be printed as
            // a modelled pressure.
            bool pressureConfigured = false;
            try
            {
                string cfgPath = PlumbingSystemConfig.ProjectConfigPath(doc);
                pressureConfigured = !string.IsNullOrEmpty(cfgPath) && System.IO.File.Exists(cfgPath) && inletKpa > 0;
            }
            catch (Exception ex) { StingLog.Warn($"PlumbSupplySchematic: plumbing config path: {ex.Message}"); }

            var opts = new SupplySchematicOptions
            {
                SystemNameFilter     = "",            // all supply systems
                InletPressureKpa     = inletKpa,
                InletPressureConfigured = pressureConfigured,
                ExportDxf            = exportDxf,
                DxfAutoCadVersion    = string.IsNullOrWhiteSpace(cfg.DxfAutoCadVersion)
                                          ? "R2010" : cfg.DxfAutoCadVersion,
                ShowDnLabels         = true,
                ShowPressureLabels   = true,
                ShowAccessorySymbols = true
            };

            // DTW-120: fit the drawing to the slot of the sheet it goes on.
            if (StingTools.Core.Drawing.SchematicViewFactory.TryGetSlotPaperSize(doc,
                    StingTools.Core.Drawing.DrawingRouteRequests.DcwSchematic,
                    out double slotW, out double slotH, out int typeScale, out string slotNote))
            {
                opts.SlotWidthMm = slotW;
                opts.SlotHeightMm = slotH;
            }
            else StingLog.Warn($"PlumbSupplySchematic: no slot size ({slotNote}) — drawn without a fit check.");
            if (typeScale > 0) opts.MinScale = typeScale;

            SupplySchematicResult result;
            try
            {
                using (var tx = new Transaction(doc, "STING Supply Schematic"))
                {
                    tx.Start();
                    result = SupplySchematicGenerator.Generate(doc, opts);
                    // Nothing real drawn: keep no empty drafting view, say why.
                    if (result.ViewId == null || result.ViewId == ElementId.InvalidElementId || result.PipesDrawn == 0)
                    {
                        tx.RollBack();
                        string why = result.Warnings.FirstOrDefault() ?? "no cold water pipe could be laid out from an inlet.";
                        PresetDialog.Show("Supply Schematic",
                            "No cold water supply schematic was drawn: " + why +
                            "\n\nModel the cold water pipework on a system classified Domestic Cold Water and re-run.",
                            ref message);
                        return Result.Cancelled;
                    }
                    // DTW-124: report a rolled-back commit, not a schematic that is not there.
                    var status = tx.Commit();
                    if (status != TransactionStatus.Committed)
                    {
                        message = $"The supply schematic was not saved: the transaction ended {status}.";
                        StingLog.Warn($"PlumbSupplySchematic: commit returned {status}.");
                        return Result.Failed;
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("PlumbSupplySchematic", ex);
                message = "Supply schematic failed: " + ex.Message;
                return Result.Failed;
            }

            var schView = doc.GetElement(result.ViewId) as View;
            // Onto the sheet of the drawing type routing gives P / DCW_SCHEMATIC
            // (stamped; a re-run's diagram replaces this one there).
            string sheetLine = StingTools.Core.SLD.SldSheetPlacement.Place(doc,
                StingTools.Core.Drawing.DrawingRouteRequests.DcwSchematic, schView);

            var panel = StingResultPanel.Create("Supply Schematic (DCW)");
            panel.SetSubtitle(opts.InletPressureConfigured
                ? $"Inlet pressure: {opts.InletPressureKpa:F0} kPa (plumbing config){(result.SourceAssumed ? " — source assumed, pressures indicative" : "")}"
                : "Inlet pressure: not configured — pressures not shown");
            panel.AddSection("SOURCE").Text(string.IsNullOrEmpty(result.SourceDescription) ? "—" : result.SourceDescription);
            panel.AddSection("SHEET").Text(sheetLine);
            panel.AddSection("SUMMARY")
                 .Metric("Scale",             $"1:{result.Scale}")
                 .Metric("Pipes drawn",       result.PipesDrawn.ToString())
                 .Metric("Accessories drawn", result.AccessoriesDrawn.ToString())
                 .Metric("Fixtures drawn",    result.FixturesDrawn.ToString());
            if (!string.IsNullOrEmpty(result.DxfPath))
                panel.AddSection("EXPORT").Text("DXF: " + result.DxfPath);
            if (result.Warnings.Count > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (var w in result.Warnings.Take(20)) panel.Text("⚠ " + w);
            }
            PresetDialog.Show(panel, ref message);

            if (schView != null && !PresetDialog.Quiet)
            {
                try { ctx.UIDoc.ActiveView = schView; }
                catch (Exception ex) { StingLog.Warn($"ActivateView: {ex.Message}"); }
            }
            return Result.Succeeded;
        }
    }
}
