// Fire_SprinklerHydraulics — tree hydraulic calculation for a sprinkler
// design area.
//
// Select the sprinkler heads in the design area plus ONE other element on
// the same network to act as the source (the installation valve set, the
// riser pipe or the pump). The command walks the connectors from the source
// to the heads, asks for the hazard class, and reports the flow and pressure
// the supply must deliver at the source, the most remote head, per-pipe
// velocity and friction, and whether an available supply pressure meets it.
// Nothing is written to the model; a CSV of every node is exported.
//
// Design data: STING_SPRINKLER_DESIGN.json (+ _BIM_COORD/sprinkler_design.json).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Fire;
using StingTools.Core.Mep.Networks;
using StingTools.UI;

namespace StingTools.Commands.Fire
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SprinklerHydraulicsCommand : IExternalCommand
    {
        private const string Title = "STING Fire — Sprinkler Hydraulics";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;

                var picked = ctx.UIDoc?.Selection?.GetElementIds()?.Select(id => doc.GetElement(id))
                                 .Where(e => e != null).ToList() ?? new List<Element>();
                var heads = picked.Where(IsSprinkler).ToList();
                var others = picked.Where(e => !IsSprinkler(e)).ToList();
                if (heads.Count == 0 || others.Count != 1)
                {
                    TaskDialog.Show(Title,
                        "Select the sprinkler heads in the design area AND exactly one other element on the same " +
                        "network as the source (installation valve set, riser pipe or pump), then run again.\n\n" +
                        $"Selected: {heads.Count} head(s), {others.Count} other element(s).");
                    return Result.Cancelled;
                }
                var source = others[0];

                var warnings = new List<string>();
                SprinklerDesignData data;
                try { data = MepDesignDataLoader.Sprinkler(doc, warnings); }
                catch (Exception ex) { TaskDialog.Show(Title, $"Sprinkler design data could not be read: {ex.Message}"); return Result.Failed; }
                var problems = data.Validate();
                if (problems.Count > 0)
                {
                    TaskDialog.Show(Title, "Sprinkler design data is invalid:\n\n" + string.Join("\n", problems.Take(12)));
                    return Result.Failed;
                }

                int defIdx = Math.Max(0, data.Hazards.FindIndex(h => string.Equals(h.Id, data.DefaultHazardId, StringComparison.OrdinalIgnoreCase)));
                var form = new StingFormDialog(Title, "Design basis",
                        $"{heads.Count} head(s) selected. Area per head 0 = design area ÷ heads. " +
                        "Hazard figures come from the design data file and are marked (verify) until checked.")
                    .Choice("hazard", "Hazard class",
                        data.Hazards.Select(h => $"{h.Id} — {h.Label}: {h.DensityMmMin:0.##} mm/min over {h.DesignAreaM2:0} m²{(h.Verify ? " (verify)" : "")}"), defIdx)
                    .Number("area", "Area per head (m²)", 0, 0, 100)
                    .Number("supplyP", "Supply pressure at the source at the demand flow (bar, 0 = don't check)", 0, 0, 50,
                        "Read it off the flow-test or pump curve at the demand flow the calculation reports.");
                if (form.ShowDialog() != true) return Result.Cancelled;
                var hazard = data.Hazards[Math.Max(0, form.ChoiceIndex("hazard"))];

                var opts = new FlowTreeBuildOptions
                {
                    ReadTerminal = (el, node) => node.KFactor = ReadKFactor(el, data, warnings),
                    EquivalentBores = data.EquivalentBores,
                    HazenWilliamsC = pipe => PipeC(pipe, data)
                };
                foreach (var h in heads) opts.TerminalIds.Add(h.Id.Value);
                var tree = RevitFlowTreeBuilder.Build(doc, source, opts);
                warnings.AddRange(tree.Warnings);
                if (tree.Root == null || tree.Root.Descendants().All(n => !n.IsTerminal))
                {
                    TaskDialog.Show(Title, "None of the selected heads is connected to the source through pipework.\n\n" +
                                           string.Join("\n", warnings.Take(8)));
                    return Result.Failed;
                }

                var criteria = data.Criteria(hazard);
                criteria.AreaPerHeadM2 = form.Get("area");
                if (criteria.AreaPerHeadM2 > hazard.MaxAreaPerHeadM2 && hazard.MaxAreaPerHeadM2 > 0)
                    warnings.Add($"Area per head {criteria.AreaPerHeadM2:F1} m² exceeds the {hazard.MaxAreaPerHeadM2:F0} m² maximum for {hazard.Id}.");
                var res = SprinklerHydraulics.Solve(tree.Root, criteria);
                warnings.AddRange(res.Warnings);
                if (!res.Ok)
                {
                    TaskDialog.Show(Title, "The calculation could not run:\n\n" + string.Join("\n", warnings.Take(12)));
                    return Result.Failed;
                }
                if (res.AreaPerHeadM2 > hazard.MaxAreaPerHeadM2 && hazard.MaxAreaPerHeadM2 > 0 && criteria.AreaPerHeadM2 <= 0)
                    warnings.Add($"Design area ÷ heads = {res.AreaPerHeadM2:F1} m² per head, above the {hazard.MaxAreaPerHeadM2:F0} m² maximum — " +
                                 "the selection has too few heads for the area of operation.");

                string csv = ExportCsv(doc, res, tree, hazard, warnings);
                Report(res, hazard, form.Get("supplyP"), tree, data, warnings, csv);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SprinklerHydraulicsCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static bool IsSprinkler(Element e)
            => e?.Category?.Id.Value == (long)BuiltInCategory.OST_Sprinklers;

        private static double ReadKFactor(Element el, SprinklerDesignData data, List<string> warnings)
        {
            double k = 0;
            try
            {
                var fi = el as FamilyInstance;
                Parameter p = el.get_Parameter(BuiltInParameter.RBS_FP_SPRINKLER_K_FACTOR_PARAM)
                           ?? fi?.Symbol?.get_Parameter(BuiltInParameter.RBS_FP_SPRINKLER_K_FACTOR_PARAM);
                if (p != null && p.HasValue && p.StorageType == StorageType.Double) k = p.AsDouble();
                if (k <= 0)
                    foreach (var name in data.KFactorParameters)
                    {
                        var q = el.LookupParameter(name) ?? fi?.Symbol?.LookupParameter(name);
                        if (q == null || !q.HasValue) continue;
                        k = q.StorageType == StorageType.Double ? q.AsDouble()
                          : q.StorageType == StorageType.Integer ? q.AsInteger()
                          : double.TryParse(q.AsString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
                        if (k > 0) break;
                    }
            }
            catch (Exception ex) { StingLog.Warn($"Sprinkler K-factor {el.Id}: {ex.Message}"); }
            return data.KFactorToSi(k);
        }

        private static double PipeC(Pipe pipe, SprinklerDesignData data)
        {
            try
            {
                string text = ((pipe.PipeType?.Name ?? "") + " " +
                               (pipe.get_Parameter(BuiltInParameter.RBS_PIPE_MATERIAL_PARAM)?.AsValueString() ?? "")).ToLowerInvariant();
                foreach (var kv in data.HazenWilliamsC)
                    if (kv.Key != "default" && text.Contains(kv.Key.ToLowerInvariant())) return kv.Value;
            }
            catch (Exception ex) { StingLog.Warn($"Sprinkler pipe C {pipe.Id}: {ex.Message}"); }
            return data.DefaultC;
        }

        private static string ExportCsv(Document doc, SprinklerHydraulicResult res, FlowTreeBuildResult tree,
            SprinklerHazard hazard, List<string> warnings)
        {
            try
            {
                string path = StingPaths.ExportFile(doc, "Schedule", "SprinklerHydraulics_" + hazard.Id, ".csv");
                var sb = new StringBuilder();
                sb.AppendLine("ElementId,Kind,Label,Flow_Lpm,InletPressure_bar,Friction_bar,Velocity_ms,Bore_mm,Length_m,EquivLength_m,Elevation_m,K,OverVelocity");
                foreach (var n in res.Nodes.Values.OrderByDescending(v => v.InletPressureBar))
                {
                    var node = n.Node;
                    tree.ElementIdByNode.TryGetValue(node, out var id);
                    sb.AppendLine(string.Join(",", new[]
                    {
                        id?.Value.ToString(CultureInfo.InvariantCulture) ?? "", node.Kind.ToString(), Csv(node.Label),
                        F(n.FlowLpm), F(n.InletPressureBar, "0.000"), F(n.FrictionBar, "0.0000"), F(n.VelocityMs, "0.00"),
                        F(node.DiameterMm), F(node.LengthM, "0.00"), F(node.EquivLengthM, "0.00"), F(node.ElevationM, "0.00"),
                        node.IsTerminal ? F(node.KFactor) : "", n.OverVelocity ? "YES" : ""
                    }));
                }
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                return path;
            }
            catch (Exception ex) { warnings.Add($"CSV export failed: {ex.Message}"); return null; }
        }

        private static string F(double v, string fmt = "0.0") => v.ToString(fmt, CultureInfo.InvariantCulture);
        private static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

        private static void Report(SprinklerHydraulicResult res, SprinklerHazard hazard, double supplyP,
            FlowTreeBuildResult tree, SprinklerDesignData data, List<string> warnings, string csv)
        {
            var panel = StingResultPanel.Create("Sprinkler Hydraulics");
            panel.SetSubtitle($"{hazard.Id} {hazard.Label} · {hazard.DensityMmMin:0.##} mm/min · {res.HeadCount} heads · tree method");
            panel.AddSection("DEMAND AT SOURCE")
                 .MetricHighlight("Flow", $"{res.SourceFlowLpm:F0} L/min ({res.SourceFlowLpm / 60.0:F1} L/s)")
                 .MetricHighlight("Pressure", $"{res.SourcePressureBar:F2} bar")
                 .Metric("Min flow per head", $"{res.MinHeadFlowLpm:F1} L/min ({res.AreaPerHeadM2:F1} m² × {hazard.DensityMmMin:0.##} mm/min)")
                 .Metric("Min head pressure", $"{hazard.MinHeadPressureBar:F2} bar")
                 .Metric("Most remote head", res.MostRemoteHead?.Label ?? "—");

            if (supplyP > 0)
            {
                double pAtDemand = supplyP;
                string basis = $"supply pressure entered for {res.SourceFlowLpm:F0} L/min";
                bool ok = pAtDemand >= res.SourcePressureBar;
                var s = panel.AddSection("SUPPLY CHECK");
                if (ok) s.Metric("Available vs required", $"{pAtDemand:F2} ≥ {res.SourcePressureBar:F2} bar — adequate", basis);
                else s.MetricError("Available vs required", $"{pAtDemand:F2} < {res.SourcePressureBar:F2} bar — INADEQUATE", basis);
            }

            var rows = res.Heads.OrderBy(h => h.InletPressureBar).Take(40)
                .Select(h => new[] { h.Node.Label, $"{h.Node.KFactor:F0}", $"{h.InletPressureBar:F2}", $"{h.FlowLpm:F1}" }).ToList();
            panel.AddSection("HEADS (lowest pressure first)").Table(new[] { "Head", "K", "bar", "L/min" }, rows);

            var pipes = res.Nodes.Values.Where(n => n.Node.Kind == FlowNodeKind.Pipe || n.Node.Kind == FlowNodeKind.Accessory)
                .OrderByDescending(n => n.VelocityMs).Take(15)
                .Select(n => new[] { n.Node.Label, $"{n.Node.DiameterMm:F0}", $"{n.FlowLpm:F0}", $"{n.VelocityMs:F2}", $"{n.FrictionBar:F3}" }).ToList();
            panel.AddSection("FASTEST PIPES").Table(new[] { "Element", "Bore mm", "L/min", "m/s", "Δp bar" }, pipes);

            var basisSec = panel.AddSection("BASIS");
            basisSec.Text("Hazen-Williams p = 6.05×10⁵·Q^1.85·L/(C^1.85·d^4.87); static 0.0981 bar/m; junctions balanced by equivalent K.");
            basisSec.Text("Fittings and valves by equivalent length in bores (approximation — see the data file).");
            if (hazard.Verify) basisSec.Text($"Hazard {hazard.Id} figures are marked verify — check against the standard in force before relying on the result.");
            foreach (var src in data.Sources) basisSec.Text("Data: " + src);
            if (!string.IsNullOrEmpty(csv)) basisSec.Text("CSV: " + csv);

            if (warnings.Count > 0)
            {
                var w = panel.AddSection("WARNINGS");
                foreach (var line in warnings.Distinct().Take(20)) w.Text(line);
            }
            panel.Show();
        }
    }
}
