// StingTools — HVAC fan static-pressure / index-run report (gap 2.3).
//
// Closes the "no index-run total-static report" gap: an engineer could not
// get "fan external static pressure" out of STING. From a selected AHU /
// mechanical equipment or a selected duct, this command walks the duct
// connector graph to the highest-total-pressure-drop path (the INDEX RUN),
// sums straight friction (DuctFrictionSolver / Darcy-Weisbach) + fitting
// losses (SMACNA / manufacturer C from the sizing rules) + fixed component
// allowances (coils / filters / terminal from the sizing rules), and reports
// the total External Static Pressure (Pa) with a per-segment breakdown.
//
// Output: a StingResultPanel breakdown, a TaskDialog summary, a WorkflowGrid
// row on the HVAC panel, and an offer to export the index-run CSV via the
// standard OutputLocationHelper.
//
// Read-only: no model writes. Air density comes from the HVAC header
// Snapshot (climate-aware) so altitude/temperature is respected.
//
// INDEX-RUN ALGORITHM
//   1. Resolve the fan source: the selected AHU/equipment (mechanical
//      equipment carrying a duct connector), or — if a duct is selected —
//      the upstream-most equipment reached from that duct's system.
//   2. BFS outward over the duct graph from the source connector. For each
//      duct segment we record the *cumulative* total-pressure drop to reach
//      its downstream end = predecessor cumulative + this segment's straight
//      friction + the loss of the fitting(s) crossed on the hop into it.
//   3. Terminal nodes are air terminals or dead-end ducts (no further
//      downstream duct). The index run is the terminal with the MAXIMUM
//      cumulative drop; we backtrace via the predecessor map to list it.
//   4. Add the fixed component allowances (coil + filter + terminal) to the
//      index-run friction total to give the fan External Static Pressure.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Calc;
using StingTools.Core.Mep;
using StingTools.UI;

using DuctShapeCalc = StingTools.Core.Calc.DuctShape;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacFanStaticReportCommand : IExternalCommand
    {
        private const double FtToM   = 0.3048;
        private const double Ft3ToM3  = 0.028316846592;
        private const double MmPerFt  = 304.8;
        private const int    MaxNodes = 5000;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc   = ctx.Doc;
            var uidoc = ctx.UIDoc;

            try
            {
                // Air density from the HVAC header snapshot (climate-aware).
                double rho = 1.204;
                try
                {
                    var snap = StingHvacCommandHandler.Snapshot();
                    if (snap.AirDensityKgM3 > 0) rho = snap.AirDensityKgM3;
                }
                catch (Exception ex) { StingLog.Warn($"FanStatic: header snapshot: {ex.Message}"); }

                var rules = MepSizingRegistry.Get(doc);

                // 1. Resolve the fan source from the selection.
                var source = ResolveSource(doc, uidoc);
                if (source == null)
                {
                    TaskDialog.Show("STING HVAC — Fan Static",
                        "Select a mechanical equipment element (AHU / fan) or a duct on the " +
                        "system, then re-run.\n\n" +
                        "The command walks the duct network to the index (highest-ΔP) run, " +
                        "sums Darcy-Weisbach friction + fitting + coil/filter/terminal " +
                        "allowances, and reports the fan external static pressure.");
                    return Result.Cancelled;
                }

                // 2/3. Walk to the index run.
                var walk = WalkIndexRun(doc, source, rho, rules);
                if (walk.IndexPath.Count == 0)
                {
                    TaskDialog.Show("STING HVAC — Fan Static",
                        "No connected ductwork was reachable from the selected source. " +
                        "Confirm the AHU/duct is connected in the model and carries an HVAC system.");
                    return Result.Cancelled;
                }

                // 4. Component allowances (coil + filter + terminal), from rules with a prompt.
                var allowances = PromptComponentAllowances(rules);
                double allowancePa = allowances.Sum(a => a.Pa);

                double frictionPa = walk.IndexPath.Sum(s => s.SegmentDropPa);
                double totalStaticPa = frictionPa + allowancePa;

                // ── Report ──────────────────────────────────────────────────
                string csvPath = WriteCsv(doc, source, walk, allowances, frictionPa, allowancePa, totalStaticPa, rho);

                var panel = StingResultPanel.Create("HVAC — Fan Static / Index Run");
                panel.SetSubtitle(
                    $"Source: {SourceLabel(source)} · ρ={rho:F3} kg/m³ · " +
                    $"{walk.IndexPath.Count} index segment(s) · {walk.SegmentsWalked} walked");

                panel.AddSection("FAN EXTERNAL STATIC")
                     .MetricHighlight("Total ESP",         $"{totalStaticPa:F0} Pa  ({totalStaticPa / 1000.0:F3} kPa)")
                     .Metric("Index-run friction+fittings", $"{frictionPa:F0} Pa")
                     .Metric("Component allowances",         $"{allowancePa:F0} Pa")
                     .Metric("Index-run length",             $"{walk.IndexPath.Sum(s => s.LengthM):F1} m")
                     .Metric("Index terminal",               walk.IndexTerminalLabel);

                panel.AddSection("COMPONENT ALLOWANCES");
                foreach (var a in allowances)
                    panel.Text($"{a.Name,-16} {a.Pa,6:F0} Pa");
                if (allowances.Count == 0) panel.Text("(none selected)");

                panel.AddSection("INDEX RUN — PER SEGMENT");
                panel.Text($"{"#",-3} {"Element",-10} {"Size",-12} {"L(m)",6} {"Q(m³/s)",8} {"v(m/s)",7} {"ΔPseg(Pa)",10} {"ΣPa",7}");
                int i = 1; double cum = 0;
                foreach (var s in walk.IndexPath)
                {
                    cum += s.SegmentDropPa;
                    panel.Text(
                        $"{i,-3} #{s.DuctId,-9} {s.SizeLabel,-12} {s.LengthM,6:F1} {s.FlowM3S,8:F2} " +
                        $"{s.VelocityMs,7:F1} {s.SegmentDropPa,10:F1} {cum,7:F0}");
                    if (!string.IsNullOrEmpty(s.FittingNote))
                        panel.Text($"      ↳ fittings: {s.FittingNote}");
                    i++;
                }

                if (walk.Warnings.Count > 0)
                {
                    panel.AddSection("WARNINGS");
                    foreach (var w in walk.Warnings.Take(30)) panel.Text("⚠ " + w);
                }

                panel.AddSection("METHOD")
                     .Text("Straight friction: Darcy-Weisbach (Swamee-Jain f), galvanised roughness, " +
                           "air density from the HVAC header (climate-aware). Fitting losses: SMACNA / " +
                           "manufacturer C from STING_MEP_SIZING_RULES.json (ΔP = C·½ρv²). Component " +
                           "allowances: duct.componentAllowancesPa in the sizing rules. Index run = the " +
                           "reachable terminal with the maximum cumulative total-pressure drop.");
                if (csvPath != null)
                    panel.Text($"CSV: {csvPath}");
                panel.Show();

                // TaskDialog summary.
                new TaskDialog("STING HVAC — Fan Static")
                {
                    MainInstruction = $"Fan External Static Pressure ≈ {totalStaticPa:F0} Pa ({totalStaticPa / 1000.0:F2} kPa)",
                    MainContent =
                        $"Index run: {walk.IndexPath.Count} duct segment(s), " +
                        $"{walk.IndexPath.Sum(s => s.LengthM):F1} m to {walk.IndexTerminalLabel}.\n" +
                        $"Friction + fittings {frictionPa:F0} Pa + component allowances {allowancePa:F0} Pa.\n\n" +
                        (csvPath != null ? $"CSV written:\n{csvPath}" : "")
                }.Show();

                // Panel run row.
                try
                {
                    StingHvacPanel.Instance?.PushRunRow(
                        $"Fan static → {totalStaticPa:F0} Pa ({walk.IndexPath.Count} seg)", "⬤");
                }
                catch (Exception ex) { StingLog.Warn($"FanStatic panel push: {ex.Message}"); }

                StingLog.Info($"Hvac_FanStaticReport: ESP {totalStaticPa:F0} Pa, index {walk.IndexPath.Count} segments, source {SourceLabel(source)}");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacFanStaticReportCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── Source resolution ────────────────────────────────────────────────

        private static Element ResolveSource(Document doc, Autodesk.Revit.UI.UIDocument uidoc)
        {
            try
            {
                var sel = uidoc.Selection.GetElementIds()
                    .Select(id => doc.GetElement(id))
                    .Where(e => e != null)
                    .ToList();

                // Preference 1: a selected mechanical-equipment element with a duct connector.
                var equip = sel.OfType<FamilyInstance>()
                    .FirstOrDefault(fi => IsMechanicalEquipment(fi) && HasDuctConnector(fi));
                if (equip != null) return equip;

                // Preference 2: a selected duct → the upstream-most equipment on its system.
                var duct = sel.OfType<Duct>().FirstOrDefault();
                if (duct != null)
                {
                    var eq = FindSystemEquipment(doc, duct);
                    if (eq != null) return eq;
                    return duct; // no equipment found — walk from the duct itself.
                }
            }
            catch (Exception ex) { StingLog.Warn($"FanStatic ResolveSource: {ex.Message}"); }
            return null;
        }

        private static Element FindSystemEquipment(Document doc, Duct duct)
        {
            try
            {
                foreach (Connector c in DuctConnectors(duct))
                {
                    var sys = c.MEPSystem as MechanicalSystem;
                    if (sys == null) continue;
                    // MechanicalSystem.BaseEquipment is the air handler / fan.
                    var be = sys.BaseEquipment;
                    if (be != null) return be;
                    // Else the first mechanical-equipment member.
                    foreach (Element el in sys.Elements)
                        if (el is FamilyInstance fi && IsMechanicalEquipment(fi) && HasDuctConnector(fi))
                            return fi;
                }
            }
            catch (Exception ex) { StingLog.Warn($"FanStatic FindSystemEquipment: {ex.Message}"); }
            return null;
        }

        // ── Index-run walk ───────────────────────────────────────────────────

        private class SegmentRow
        {
            public long   DuctId;
            public string SizeLabel   = "";
            public double LengthM;
            public double FlowM3S;
            public double VelocityMs;
            public double SegmentDropPa;   // straight friction + fittings crossed into this segment
            public string FittingNote = "";
        }

        private class WalkResult
        {
            public readonly List<SegmentRow> IndexPath = new List<SegmentRow>();
            public string IndexTerminalLabel = "(dead-end)";
            public int SegmentsWalked;
            public readonly List<string> Warnings = new List<string>();
        }

        private WalkResult WalkIndexRun(Document doc, Element source, double rho, MepSizingRules rules)
        {
            var res = new WalkResult();

            // BFS state: per-duct cumulative drop + predecessor duct + the row.
            var cumDrop   = new Dictionary<ElementId, double>();
            var predecessor = new Dictionary<ElementId, ElementId>();
            var rowById   = new Dictionary<ElementId, SegmentRow>();
            var visited   = new HashSet<ElementId> { source.Id };

            // Seed queue with the source's downstream duct connectors.
            var queue = new Queue<(Connector from, ElementId predDuct, double cumIn)>();
            foreach (var c in ConnectorsOf(source))
                if (c.Domain == Domain.DomainHvac)
                    queue.Enqueue((c, ElementId.InvalidElementId, 0.0));

            ElementId bestTerminal = ElementId.InvalidElementId;
            double bestCum = -1;
            string bestTerminalLabel = "(dead-end)";

            int guard = 0;
            while (queue.Count > 0 && guard++ < MaxNodes)
            {
                var (fromConn, predDuct, cumIn) = queue.Dequeue();
                var refs = SafeAllRefs(fromConn);
                if (refs == null) continue;

                // Accumulate fitting loss crossed at this junction (fittings are
                // FamilyInstances whose duct connectors we pass through).
                foreach (Connector other in refs)
                {
                    var owner = other?.Owner;
                    if (owner == null) continue;

                    if (owner is Duct duct)
                    {
                        if (!visited.Add(duct.Id)) continue;
                        res.SegmentsWalked++;

                        var seg = BuildSegment(duct, rho);
                        double newCum = cumIn + seg.SegmentDropPa;
                        cumDrop[duct.Id] = newCum;
                        predecessor[duct.Id] = predDuct;
                        rowById[duct.Id] = seg;

                        // Enumerate this duct's *other* connectors to continue downstream.
                        bool hasDownstream = false;
                        foreach (var oc in DuctConnectors(duct))
                        {
                            if (oc.Id == other.Id) continue;
                            // A connector that touches an air terminal ⇒ terminal end.
                            if (TouchesAirTerminal(oc, out string termLabel))
                            {
                                double termCum = newCum;
                                if (termCum > bestCum)
                                {
                                    bestCum = termCum; bestTerminal = duct.Id; bestTerminalLabel = termLabel;
                                }
                                continue;
                            }
                            if (HasDownstreamDuctOrFitting(oc))
                            {
                                hasDownstream = true;
                                queue.Enqueue((oc, duct.Id, newCum));
                            }
                        }

                        // Dead-end duct (no downstream duct/fitting/terminal) is a terminal too.
                        if (!hasDownstream && newCum > bestCum)
                        {
                            bestCum = newCum; bestTerminal = duct.Id; bestTerminalLabel = $"dead-end #{duct.Id.Value}";
                        }
                    }
                    else if (owner is FamilyInstance fi && IsDuctFittingOrAccessory(fi))
                    {
                        if (!visited.Add(fi.Id)) continue;
                        // Charge the fitting loss to the *next* segment: carry it forward
                        // by pushing this fitting's other connectors with an increased cumIn.
                        double fitPa = FittingLossPa(fi, other, rho, rules, out string fitName);
                        foreach (var oc in ConnectorsOf(fi))
                        {
                            if (oc.Id == other.Id) continue;
                            if (oc.Domain != Domain.DomainHvac) continue;
                            queue.Enqueue((oc, predDuct, cumIn + fitPa));
                        }
                        // Annotate the predecessor segment's fitting note if we have one.
                        if (predDuct != ElementId.InvalidElementId && rowById.TryGetValue(predDuct, out var prow))
                            prow.FittingNote = AppendNote(prow.FittingNote, $"{fitName} {fitPa:F1}Pa");
                    }
                }
            }

            if (guard >= MaxNodes)
                res.Warnings.Add($"Traversal capped at {MaxNodes} nodes — very large network; index run may be partial.");

            // Backtrace the index run.
            if (bestTerminal != ElementId.InvalidElementId)
            {
                var chain = new List<SegmentRow>();
                var cur = bestTerminal;
                var safety = new HashSet<ElementId>();
                while (cur != ElementId.InvalidElementId && rowById.TryGetValue(cur, out var row))
                {
                    if (!safety.Add(cur)) break; // guard against a cycle in predecessor map
                    chain.Add(row);
                    cur = predecessor.TryGetValue(cur, out var p) ? p : ElementId.InvalidElementId;
                }
                chain.Reverse();
                res.IndexPath.AddRange(chain);
                res.IndexTerminalLabel = bestTerminalLabel;
            }
            else if (rowById.Count > 0)
            {
                // No terminal detected — fall back to the single highest-cumulative duct.
                var top = cumDrop.OrderByDescending(kv => kv.Value).First();
                var chain = new List<SegmentRow>();
                var cur = top.Key;
                var safety = new HashSet<ElementId>();
                while (cur != ElementId.InvalidElementId && rowById.TryGetValue(cur, out var row))
                {
                    if (!safety.Add(cur)) break;
                    chain.Add(row);
                    cur = predecessor.TryGetValue(cur, out var p) ? p : ElementId.InvalidElementId;
                }
                chain.Reverse();
                res.IndexPath.AddRange(chain);
                res.IndexTerminalLabel = $"max-ΔP end #{top.Key.Value}";
                res.Warnings.Add("No air terminal reached — index run ends at the highest-ΔP duct.");
            }

            return res;
        }

        private static SegmentRow BuildSegment(Duct duct, double rho)
        {
            var seg = new SegmentRow { DuctId = duct.Id.Value };
            try
            {
                DuctShapeCalc shape; double sideA, sideB;
                var profile = duct.DuctType?.Shape ?? ConnectorProfileType.Invalid;
                if (profile == ConnectorProfileType.Round)
                {
                    shape = DuctShapeCalc.Round;
                    sideA = duct.Diameter * MmPerFt; sideB = 0;
                    seg.SizeLabel = $"Ø{sideA:F0}";
                }
                else
                {
                    shape = DuctShapeCalc.Rectangular;
                    sideA = duct.Width * MmPerFt; sideB = duct.Height * MmPerFt;
                    seg.SizeLabel = $"{sideA:F0}×{sideB:F0}";
                }

                seg.LengthM = ((duct.Location as LocationCurve)?.Curve?.Length ?? 0) * FtToM;
                seg.FlowM3S = ReadFlowM3S(duct);

                var fr = DuctFrictionSolver.Solve(shape, sideA, sideB, seg.LengthM, seg.FlowM3S, null,
                    DuctFrictionSolver.GalvRoughnessM, rho);
                seg.VelocityMs   = fr.VelocityMs;
                seg.SegmentDropPa = fr.StraightDropPa;   // fittings added separately at junctions
            }
            catch (Exception ex) { StingLog.Warn($"FanStatic BuildSegment #{duct?.Id}: {ex.Message}"); }
            return seg;
        }

        // ── Fitting loss ────────────────────────────────────────────────────

        private static double FittingLossPa(FamilyInstance fitting, Connector inConn,
            double rho, MepSizingRules rules, out string name)
        {
            name = "fitting";
            try
            {
                // Velocity at the inbound connector, if available.
                double v = ConnectorVelocity(inConn);
                if (v <= 0) v = 3.0; // conservative default when connector flow/size unavailable

                string key = ClassifyFittingKey(fitting);
                name = key;

                // Manufacturer C first (via MEP_PROD_REF_TXT), else the SMACNA/registry table.
                double c = 0;
                string prodRef = ParameterHelpers.GetString(fitting, "MEP_PROD_REF_TXT");
                if (!string.IsNullOrEmpty(prodRef))
                {
                    foreach (var brand in rules.ManufacturerFittings.Keys)
                    {
                        double mc = rules.GetManufacturerC(brand, prodRef);
                        if (mc > 0) { c = mc; name = $"{brand}:{prodRef}"; break; }
                    }
                }
                if (c <= 0) c = DuctFrictionSolver.LookupC(key, rules.DuctFittingLossK);
                if (c <= 0) return 0;

                return c * 0.5 * rho * v * v;
            }
            catch (Exception ex) { StingLog.Warn($"FanStatic FittingLossPa: {ex.Message}"); return 0; }
        }

        private static string ClassifyFittingKey(FamilyInstance fitting)
        {
            try
            {
                if (fitting.MEPModel is MechanicalFitting mf)
                {
                    switch (mf.PartType)
                    {
                        case PartType.Elbow:       return "ELBOW_90_SMOOTH";
                        case PartType.Tee:         return "TEE_BRANCH_90";
                        case PartType.Cross:       return "TEE_BRANCH_90";
                        case PartType.Transition:  return "EXPANSION_45";
                        case PartType.TapAdjustable:
                        case PartType.TapPerpendicular:
                        case PartType.SpudAdjustable:
                        case PartType.SpudPerpendicular: return "TEE_BRANCH_90";
                        default: break;
                    }
                }
                // Duct accessory (damper) — treat as an open damper by default.
                var cat = fitting.Category != null ? (BuiltInCategory)fitting.Category.Id.Value : BuiltInCategory.INVALID;
                if (cat == BuiltInCategory.OST_DuctAccessory) return "DAMPER_OPEN";
            }
            catch { }
            return "ELBOW_90_SMOOTH";
        }

        private static double ConnectorVelocity(Connector c)
        {
            try
            {
                double flow = c.Flow;                       // ft³/s
                if (flow <= 0) return 0;
                double areaFt2 = 0;
                if (c.Shape == ConnectorProfileType.Round)
                {
                    double r = c.Radius;                    // ft
                    areaFt2 = Math.PI * r * r;
                }
                else
                {
                    areaFt2 = c.Width * c.Height;            // ft²
                }
                if (areaFt2 <= 0) return 0;
                double vFtS = flow / areaFt2;
                return vFtS * FtToM;                        // m/s
            }
            catch { return 0; }
        }

        // ── Component allowance prompt ───────────────────────────────────────

        private class Allowance { public string Name; public double Pa; }

        private static List<Allowance> PromptComponentAllowances(MepSizingRules rules)
        {
            var chosen = new List<Allowance>();
            // Defaults from rules (or hardcoded sensible values if the block is absent).
            double coil   = GetAllowance(rules, "coil_cooling", 120.0);
            double filter = GetAllowance(rules, "filter_bag",   150.0);
            double term   = GetAllowance(rules, "terminal",      30.0);

            var td = new TaskDialog("STING HVAC — Component Allowances")
            {
                MainInstruction = "Add AHU component allowances to the index-run friction?",
                MainContent =
                    $"Fixed external-static allowances (Pa) from the sizing rules:\n" +
                    $"  cooling coil  {coil:F0} Pa\n" +
                    $"  bag filter    {filter:F0} Pa\n" +
                    $"  terminal      {term:F0} Pa\n\n" +
                    "Choose a set to add to the fan external static pressure.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.CommandLink1
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Coil + filter + terminal",
                $"Add all three ({coil + filter + term:F0} Pa)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Terminal only",
                $"Add {term:F0} Pa (ductwork-only ESP)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "None",
                "Report duct friction + fittings only");

            var r = td.Show();
            if (r == TaskDialogResult.CommandLink1)
            {
                chosen.Add(new Allowance { Name = "cooling coil", Pa = coil });
                chosen.Add(new Allowance { Name = "bag filter",   Pa = filter });
                chosen.Add(new Allowance { Name = "terminal",     Pa = term });
            }
            else if (r == TaskDialogResult.CommandLink2)
            {
                chosen.Add(new Allowance { Name = "terminal", Pa = term });
            }
            // CommandLink3 / Cancel → none.
            return chosen;
        }

        private static double GetAllowance(MepSizingRules rules, string key, double fallback)
        {
            try
            {
                if (rules?.DuctComponentAllowancesPa != null &&
                    rules.DuctComponentAllowancesPa.TryGetValue(key, out double v) && v > 0)
                    return v;
            }
            catch { }
            return fallback;
        }

        // ── CSV ──────────────────────────────────────────────────────────────

        private static string WriteCsv(Document doc, Element source, WalkResult walk,
            List<Allowance> allowances, double frictionPa, double allowancePa,
            double totalStaticPa, double rho)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("STING HVAC — Fan Static / Index Run Report");
                sb.AppendLine($"Generated,{DateTime.Now:yyyy-MM-dd HH:mm}");
                sb.AppendLine($"Source,{SourceLabel(source)}");
                sb.AppendLine($"Air density kg/m3,{rho:F3}");
                sb.AppendLine($"Index terminal,{Csv(walk.IndexTerminalLabel)}");
                sb.AppendLine();
                sb.AppendLine("Seq,DuctId,Size,Length_m,Flow_m3s,Velocity_ms,SegmentDrop_Pa,Cumulative_Pa,Fittings");
                int i = 1; double cum = 0;
                foreach (var s in walk.IndexPath)
                {
                    cum += s.SegmentDropPa;
                    sb.AppendLine(string.Join(",",
                        i, s.DuctId, Csv(s.SizeLabel),
                        s.LengthM.ToString("F2", CultureInfo.InvariantCulture),
                        s.FlowM3S.ToString("F3", CultureInfo.InvariantCulture),
                        s.VelocityMs.ToString("F2", CultureInfo.InvariantCulture),
                        s.SegmentDropPa.ToString("F1", CultureInfo.InvariantCulture),
                        cum.ToString("F1", CultureInfo.InvariantCulture),
                        Csv(s.FittingNote)));
                    i++;
                }
                sb.AppendLine();
                sb.AppendLine("Component allowances");
                foreach (var a in allowances)
                    sb.AppendLine($"{Csv(a.Name)},{a.Pa.ToString("F1", CultureInfo.InvariantCulture)}");
                sb.AppendLine();
                sb.AppendLine($"Index-run friction+fittings Pa,{frictionPa.ToString("F1", CultureInfo.InvariantCulture)}");
                sb.AppendLine($"Component allowances Pa,{allowancePa.ToString("F1", CultureInfo.InvariantCulture)}");
                sb.AppendLine($"Fan External Static Pressure Pa,{totalStaticPa.ToString("F1", CultureInfo.InvariantCulture)}");

                string path = OutputLocationHelper.GetOutputPath(doc,
                    $"STING_HVAC_FanStatic_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                File.WriteAllText(path, sb.ToString());
                return path;
            }
            catch (Exception ex) { StingLog.Warn($"FanStatic WriteCsv: {ex.Message}"); return null; }
        }

        // ── Small helpers ────────────────────────────────────────────────────

        private static bool IsMechanicalEquipment(Element el)
        {
            try { return el?.Category != null && (BuiltInCategory)el.Category.Id.Value == BuiltInCategory.OST_MechanicalEquipment; }
            catch { return false; }
        }

        private static bool IsDuctFittingOrAccessory(FamilyInstance fi)
        {
            try
            {
                var cat = fi?.Category; if (cat == null) return false;
                var bic = (BuiltInCategory)cat.Id.Value;
                return bic == BuiltInCategory.OST_DuctFitting || bic == BuiltInCategory.OST_DuctAccessory;
            }
            catch { return false; }
        }

        private static bool HasDuctConnector(FamilyInstance fi)
        {
            foreach (var c in ConnectorsOf(fi)) if (c.Domain == Domain.DomainHvac) return true;
            return false;
        }

        private static bool TouchesAirTerminal(Connector c, out string label)
        {
            label = "";
            try
            {
                var refs = SafeAllRefs(c);
                if (refs == null) return false;
                foreach (Connector other in refs)
                {
                    var cat = other?.Owner?.Category;
                    if (cat == null) continue;
                    if ((BuiltInCategory)cat.Id.Value == BuiltInCategory.OST_DuctTerminal)
                    {
                        label = $"terminal #{other.Owner.Id.Value}";
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool HasDownstreamDuctOrFitting(Connector c)
        {
            try
            {
                var refs = SafeAllRefs(c);
                if (refs == null) return false;
                foreach (Connector other in refs)
                {
                    if (other?.Owner is Duct) return true;
                    if (other?.Owner is FamilyInstance fi && IsDuctFittingOrAccessory(fi)) return true;
                }
            }
            catch { }
            return false;
        }

        private static double ReadFlowM3S(Duct d)
        {
            try
            {
                var p = d.get_Parameter(BuiltInParameter.RBS_DUCT_FLOW_PARAM);
                if (p != null) return p.AsDouble() * Ft3ToM3;
            }
            catch { }
            return 0;
        }

        private static IEnumerable<Connector> ConnectorsOf(Element el)
        {
            ConnectorSet set = null;
            try
            {
                if (el is FamilyInstance fi) set = fi.MEPModel?.ConnectorManager?.Connectors;
                else if (el is MEPCurve mc)  set = mc.ConnectorManager?.Connectors;
            }
            catch { }
            if (set == null) yield break;
            foreach (Connector c in set) yield return c;
        }

        private static IEnumerable<Connector> DuctConnectors(Duct d)
        {
            ConnectorSet set = null;
            try { set = d?.ConnectorManager?.Connectors; } catch { }
            if (set == null) yield break;
            foreach (Connector c in set) yield return c;
        }

        private static IEnumerable<Connector> SafeAllRefs(Connector c)
        {
            ConnectorSet set = null;
            try { set = c?.AllRefs; } catch { }
            if (set == null) yield break;
            foreach (Connector r in set) yield return r;
        }

        private static string SourceLabel(Element el)
        {
            try
            {
                if (el is FamilyInstance fi)
                    return $"{fi.Symbol?.Family?.Name}/{fi.Symbol?.Name} #{fi.Id.Value}";
                return $"{el.Category?.Name} #{el.Id.Value}";
            }
            catch { return $"#{el?.Id.Value}"; }
        }

        private static string AppendNote(string existing, string add)
            => string.IsNullOrEmpty(existing) ? add : existing + " + " + add;

        private static string Csv(string s)
        {
            s = s ?? "";
            if (s.Contains(",") || s.Contains("\"")) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
