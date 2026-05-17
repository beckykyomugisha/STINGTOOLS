// PlumbingNetworkCommands — Phase 179f NETWORK tab.
//
// Plumb_BuildNetwork      — build pipe network graph, accumulate DFU + pressure.
// Plumb_SlopeAutomation   — apply BS EN 12056-2 minimum slopes (or custom) to drainage pipes.
// Plumb_CreateVents       — design and create vent pipes / AAVs from VentDesigner output.
// Plumb_NetworkPressure   — colour pipes by pressure zone, write PLM_PRESSURE_KPA.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Plumbing;
using StingTools.Select;
using StingTools.UI;

namespace StingTools.Commands.Plumbing
{
    // ─────────────────────────────────────────────────────────────────────────
    // Plumb_BuildNetwork
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbBuildNetworkCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            PipeNetwork net;
            DfuMapResult dfuMap;
            PlumbingSystemConfig cfg;
            List<PipeEdge> criticalPipes;
            List<PipeNode> stackNodes;
            try
            {
                cfg    = PlumbingSystemConfig.Load(doc);
                net    = PipeNetworkBuilder.Build(doc);
                dfuMap = FixtureUnitAggregator.BuildDfuMap(doc);
                PipeNetworkBuilder.AccumulateDfu(net, dfuMap.PipeDfu);
                PipeNetworkBuilder.AccumulatePressure(net,
                    cfg.SupplyPressureBarAtEntry * 100.0, // bar → kPa
                    0.0);
                criticalPipes = PipeNetworkBuilder.FindCriticalPath(net);
                stackNodes    = PipeNetworkBuilder.FindStacks(net);
            }
            catch (Exception ex)
            {
                StingLog.Error("PlumbBuildNetwork", ex);
                message = "Network build failed: " + ex.Message;
                return Result.Failed;
            }

            double critLenM  = criticalPipes.Sum(e => e.LengthM);

            // ── low-pressure nodes (<1 bar = 100 kPa) ───────────────────────
            var lowPressure = net.Nodes
                .Where(n => n.PressureKpa > 0 && n.PressureKpa < 100.0)
                .ToList();

            var panel = StingResultPanel.Create("Pipe Network — Build");
            panel.SetSubtitle($"Pressure entry: {cfg.SupplyPressureBarAtEntry:F2} bar");

            panel.AddSection("NETWORK SUMMARY")
                 .Metric("Nodes",                     net.Nodes.Count.ToString())
                 .Metric("Edges (pipes + fittings)",  net.Edges.Count.ToString())
                 .Metric("Fixtures",                  dfuMap.FixturesScanned.ToString())
                 .Metric("Stack nodes",               stackNodes.Count.ToString())
                 .Metric("Critical path length (m)",  critLenM.ToString("F1"))
                 .Metric("Low-pressure nodes (<1 bar)", lowPressure.Count.ToString());

            if (stackNodes.Count > 0)
            {
                panel.AddSection("STACK NODES");
                foreach (var st in stackNodes.Take(20))
                    panel.Text($"Node {st.Id.Value}  DN{st.DnMm:F0}  DFU accum {st.DfuAccumulated:F1}  p {st.PressureKpa:F0} kPa");
            }

            if (criticalPipes.Count > 0)
            {
                panel.AddSection("CRITICAL PATH PIPES");
                foreach (var e in criticalPipes.Take(20))
                    panel.Text($"Pipe {e.PipeId.Value}  DN{e.DnMm:F0}  L {e.LengthM:F1} m");
            }

            if (lowPressure.Count > 0)
            {
                panel.AddSection("LOW-PRESSURE NODES");
                foreach (var n in lowPressure.Take(20))
                    panel.Text($"Node {n.Id.Value}  p {n.PressureKpa:F0} kPa  DN{n.DnMm:F0}  DFU {n.DfuAccumulated:F1}");
                panel.Text("NOTE: Consider booster pump or PRV zone rearrangement.");
            }

            panel.Show();
            return Result.Succeeded;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Plumb_SlopeAutomation
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbSlopeAutomationCommand : IExternalCommand
    {
        // BS EN 12056-2 minimum slopes by nominal diameter (mm → pct).
        private static double BsMinSlopePct(int dnMm)
        {
            if (dnMm <= 50)  return 2.0;
            if (dnMm <= 75)  return 1.25;
            if (dnMm <= 100) return 1.0;
            return 1.0; // DN125+
        }

        private static bool IsSanitarySystem(string sysName)
        {
            if (string.IsNullOrEmpty(sysName)) return false;
            var u = sysName.ToUpperInvariant();
            return u.Contains("SANIT") || u.Contains("DRAIN") || u.Contains("WASTE")
                || u.Contains("FOUL")  || u.Contains("SOIL")  || u.Contains("SAN")
                || u.Contains("SWR")   || u.Contains("SEWER");
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // ── user choice ──────────────────────────────────────────────────
            var td = new TaskDialog("Plumb_SlopeAutomation")
            {
                MainInstruction = "Slope Automation — drainage pipes",
                MainContent     = "Select action:",
                CommonButtons   = TaskDialogCommonButtons.Cancel,
                DefaultButton   = TaskDialogResult.Cancel
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Apply BS EN 12056-2 minimum slopes");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Apply custom slope %");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Preview only (no changes)");
            var pick = td.Show();
            if (pick == TaskDialogResult.Cancel) return Result.Cancelled;

            bool previewOnly   = pick == TaskDialogResult.CommandLink3;
            bool customSlope   = pick == TaskDialogResult.CommandLink2;
            double customPct   = 1.0;

            if (customSlope)
            {
                var inputDlg = new TaskDialog("Slope %")
                {
                    MainInstruction = "Enter custom slope percentage (e.g. 1.5):",
                    MainContent     = "Applied uniformly to all drainage pipes regardless of diameter."
                };
                // Revit TaskDialog has no text input — use a placeholder default and inform user.
                inputDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Use 1.0 %");
                inputDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Use 1.5 %");
                inputDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Use 2.0 %");
                inputDlg.CommonButtons = TaskDialogCommonButtons.Cancel;
                var ipick = inputDlg.Show();
                if (ipick == TaskDialogResult.Cancel) return Result.Cancelled;
                customPct = ipick == TaskDialogResult.CommandLink1 ? 1.0
                          : ipick == TaskDialogResult.CommandLink2 ? 1.5
                          : 2.0;
            }

            // ── collect drainage pipes ───────────────────────────────────────
            var pipes = new FilteredElementCollector(doc)
                .OfClass(typeof(Pipe))
                .Cast<Pipe>()
                .Where(p => IsSanitarySystem(p.MEPSystem?.Name ?? ""))
                .ToList();

            if (pipes.Count == 0)
            {
                TaskDialog.Show("STING Plumbing — Slope Automation",
                    "No drainage/sanitary pipes found in the active document.");
                return Result.Succeeded;
            }

            int adjusted = 0, alreadyOk = 0, skipped = 0;
            var log = new List<string>();

            using (var tx = new Transaction(doc, "STING Plumbing Slope Automation"))
            {
                if (!previewOnly) tx.Start();

                foreach (var pipe in pipes)
                {
                    try
                    {
                        var lc = pipe.Location as LocationCurve;
                        if (lc == null) { skipped++; continue; }

                        var line = lc.Curve as Line;
                        if (line == null) { skipped++; continue; } // curved pipe — skip

                        // Check connectivity — skip if both ends are connected
                        int connCount = 0;
                        foreach (Connector c in pipe.ConnectorManager.Connectors)
                            if (c.IsConnected) connCount++;
                        if (connCount >= 2) { skipped++; continue; }

                        // DN from diameter (internal Revit units = feet)
                        int dnMm = (int)Math.Round(pipe.Diameter * 304.8); // feet → mm
                        double targetSlopePct = customSlope ? customPct : BsMinSlopePct(dnMm);

                        // Try to read an explicitly set slope first
                        try
                        {
                            var slopeParam = pipe.LookupParameter(ParamRegistry.PLM_CALC_SLOPE);
                            if (slopeParam != null && slopeParam.HasValue
                                && slopeParam.StorageType == StorageType.String)
                            {
                                string sv = slopeParam.AsString();
                                if (double.TryParse(sv, System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out double calcSlope)
                                    && calcSlope > 0) targetSlopePct = calcSlope;
                            }
                        }
                        catch { /* ignore */ }

                        XYZ startPt = line.GetEndPoint(0);
                        XYZ endPt   = line.GetEndPoint(1);

                        // Identify upstream end (higher Z = upstream for gravity drainage)
                        bool startIsUpstream = startPt.Z >= endPt.Z;
                        XYZ upstream   = startIsUpstream ? startPt : endPt;
                        XYZ downstream = startIsUpstream ? endPt   : startPt;

                        double hLen = Math.Sqrt(
                            Math.Pow(upstream.X - downstream.X, 2) +
                            Math.Pow(upstream.Y - downstream.Y, 2)); // ft (horizontal)

                        if (hLen < 0.001) { skipped++; continue; } // vertical pipe

                        double targetDropFt = hLen * (targetSlopePct / 100.0);
                        double currentDropFt = Math.Abs(upstream.Z - downstream.Z);
                        double currentSlopePct = hLen > 0 ? (currentDropFt / hLen) * 100.0 : 0;

                        if (Math.Abs(currentSlopePct - targetSlopePct) < 0.05)
                        {
                            alreadyOk++;
                            continue;
                        }

                        if (!previewOnly)
                        {
                            XYZ newDownstream = new XYZ(downstream.X, downstream.Y,
                                upstream.Z - targetDropFt);
                            XYZ newStart = startIsUpstream ? upstream : newDownstream;
                            XYZ newEnd   = startIsUpstream ? newDownstream : upstream;
                            lc.Curve = Line.CreateBound(newStart, newEnd);
                        }
                        adjusted++;
                        log.Add($"DN{dnMm} · was {currentSlopePct:F2}% → {targetSlopePct:F2}% · L {hLen * 0.3048:F1} m");
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"PlumbSlopeAutomation pipe {pipe.Id}: {ex.Message}");
                        skipped++;
                    }
                }

                if (!previewOnly) tx.Commit();
            }

            var panel = StingResultPanel.Create(previewOnly ? "Slope Automation — PREVIEW" : "Slope Automation Applied");
            panel.SetSubtitle(customSlope
                ? $"Custom slope {customPct:F2}%"
                : "BS EN 12056-2 minimum slopes");
            panel.AddSection("SUMMARY")
                 .Metric("Pipes scanned",   pipes.Count.ToString())
                 .Metric(previewOnly ? "Would adjust" : "Adjusted",
                                          adjusted.ToString())
                 .Metric("Already OK",     alreadyOk.ToString())
                 .Metric("Skipped",        skipped.ToString());

            if (log.Count > 0)
            {
                panel.AddSection(previewOnly ? "WOULD ADJUST (first 40)" : "ADJUSTED (first 40)");
                foreach (var l in log.Take(40)) panel.Text(l);
            }
            panel.Show();
            return Result.Succeeded;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Plumb_CreateVents
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbCreateVentsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            DfuMapResult dfuMap;
            List<VentRequirement> vents;
            try
            {
                dfuMap = FixtureUnitAggregator.BuildDfuMap(doc);
                vents  = VentDesigner.DesignVents(doc, dfuMap.PipeDfu);
            }
            catch (Exception ex)
            {
                StingLog.Error("PlumbCreateVents — design phase", ex);
                message = "Vent design failed: " + ex.Message;
                return Result.Failed;
            }

            if (vents.Count == 0)
            {
                TaskDialog.Show("STING Plumbing — Create Vents",
                    "Vent designer found no drainage pipes needing vents.\n" +
                    $"Fixtures scanned: {dfuMap.FixturesScanned}, pipes with DFU: {dfuMap.PipeDfu.Count}");
                return Result.Succeeded;
            }

            int aavCount  = vents.Count(v => v.RequiresAav);
            int pipeCount = vents.Count - aavCount;

            var preview = new TaskDialog("Plumb_CreateVents")
            {
                MainInstruction = "Create vent pipes?",
                MainContent     = $"Vent designer proposes:\n" +
                                  $"  • {pipeCount} vent pipe(s)\n" +
                                  $"  • {aavCount} AAV(s)\n\n" +
                                  $"Code: {(vents.FirstOrDefault()?.CodeUsed ?? "BS-UK")}",
                CommonButtons   = TaskDialogCommonButtons.Cancel,
                DefaultButton   = TaskDialogResult.Cancel
            };
            preview.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Create vents");
            if (preview.Show() != TaskDialogResult.CommandLink1) return Result.Cancelled;

            VentCreationResult result;
            try
            {
                using (var tx = new Transaction(doc, "STING Plumbing Create Vents"))
                {
                    tx.Start();
                    result = VentCreationEngine.CreateVents(doc, vents, new VentCreationOptions());
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("PlumbCreateVents — creation phase", ex);
                message = "Vent creation failed: " + ex.Message;
                return Result.Failed;
            }

            var panel = StingResultPanel.Create("Create Vents");
            panel.SetSubtitle($"Code: {(vents.FirstOrDefault()?.CodeUsed ?? "BS-UK")} · {dfuMap.FixturesScanned} fixtures");
            panel.AddSection("SUMMARY")
                 .Metric("Vent pipes created", result.VentsCreated.ToString())
                 .Metric("AAVs placed",         result.AavsPlaced.ToString())
                 .Metric("Skipped",             result.VentsSkipped.ToString());

            if (result.Warnings?.Count > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (var w in result.Warnings.Take(40)) panel.Text(w);
            }
            panel.Show();
            return Result.Succeeded;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Plumb_NetworkPressure
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbNetworkPressureCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc  = ctx.Doc;
            var view = ctx.UIDoc.ActiveView;

            PipeNetwork net;
            PlumbingSystemConfig cfg;
            try
            {
                cfg = PlumbingSystemConfig.Load(doc);
                net = PipeNetworkBuilder.Build(doc);
                PipeNetworkBuilder.AccumulatePressure(net,
                    cfg.SupplyPressureBarAtEntry * 100.0,
                    0.0);
            }
            catch (Exception ex)
            {
                StingLog.Error("PlumbNetworkPressure", ex);
                message = "Network pressure analysis failed: " + ex.Message;
                return Result.Failed;
            }

            // Build a map: PipeId → residual pressure kPa from the downstream node of each edge
            var pressureMap = new Dictionary<long, double>();
            if (net.Edges != null)
            {
                foreach (var edge in net.Edges)
                {
                    double kpa = edge.To?.PressureKpa ?? edge.From?.PressureKpa ?? 0.0;
                    pressureMap[edge.PipeId.Value] = kpa;
                }
            }

            // Find solid fill for colour overrides
            FillPatternElement solidFill = ColorHelper.FindSolidFill(doc);

            int below100 = 0, between100and200 = 0, above200 = 0;
            int written  = 0;
            bool pumpFlag = false;

            using (var tx = new Transaction(doc, "STING Plumbing Pressure Colour"))
            {
                tx.Start();

                var pipes = new FilteredElementCollector(doc)
                    .OfClass(typeof(Pipe))
                    .Cast<Pipe>()
                    .ToList();

                foreach (var pipe in pipes)
                {
                    try
                    {
                        double kpa = pressureMap.TryGetValue(pipe.Id.Value, out double p) ? p : 0.0;

                        // Write PLM_PRESSURE_KPA if the parameter exists
                        try
                        {
                            var prm = pipe.LookupParameter("PLM_PRESSURE_KPA");
                            if (prm == null) prm = pipe.LookupParameter("PLM_PRESSURE_KPA_NR");
                            if (prm != null && !prm.IsReadOnly && prm.StorageType == StorageType.Double)
                            {
                                prm.Set(kpa);
                                written++;
                            }
                        }
                        catch { /* parameter may not be bound */ }

                        // Determine colour zone
                        Color color;
                        if (kpa < 100.0)
                        {
                            color = new Color(220, 50, 50);   // red
                            below100++;
                            if (kpa < 50.0) pumpFlag = true;
                        }
                        else if (kpa < 200.0)
                        {
                            color = new Color(230, 160, 30);  // amber
                            between100and200++;
                        }
                        else
                        {
                            color = new Color(50, 160, 70);   // green
                            above200++;
                        }

                        var ogs = ColorHelper.BuildOverride(color, solidFill);
                        view.SetElementOverrides(pipe.Id, ogs);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"PlumbNetworkPressure pipe {pipe.Id}: {ex.Message}");
                    }
                }

                tx.Commit();
            }

            // Critical path
            var critPipes = PipeNetworkBuilder.FindCriticalPath(net) ?? new List<PipeEdge>();
            double critLenM = critPipes.Sum(e => e.LengthM);

            var panel = StingResultPanel.Create("Network Pressure Analysis");
            panel.SetSubtitle($"Entry {cfg.SupplyPressureBarAtEntry:F2} bar · BS 8558 min 1.0 bar at fitting");
            panel.AddSection("COLOUR KEY")
                 .Metric("Red  (< 100 kPa)",       below100.ToString())
                 .Metric("Amber (100–200 kPa)",     between100and200.ToString())
                 .Metric("Green (> 200 kPa)",       above200.ToString())
                 .Metric("PLM_PRESSURE_KPA written",written.ToString());

            panel.AddSection("CRITICAL PATH")
                 .Metric("Critical path pipes",   critPipes.Count.ToString())
                 .Metric("Critical path length (m)", critLenM.ToString("F1"));

            if (pumpFlag)
            {
                panel.AddSection("RECOMMENDATION")
                     .Text("⚠ Pressure below 50 kPa detected on one or more pipes.")
                     .Text("Booster pump or pressure zone rearrangement is recommended.")
                     .Text("Use Plumb_PumpSelect to size a suitable booster pump.");
            }
            panel.Show();
            return Result.Succeeded;
        }
    }
}
