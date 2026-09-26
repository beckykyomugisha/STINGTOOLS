// Gas_SizePipes — low-pressure gas installation sizing (Pole's formula).
//
// Select ONE element at the start of the installation: the meter, the
// emergency control valve or the first pipe after it. The command walks the
// connected pipework, takes every connected appliance (anything that is not
// a pipe, fitting or valve) as a load, reads its heat input from the
// parameters listed in STING_GAS_DESIGN.json, and then either checks the
// modelled sizes or chooses sizes from a pipe series — optionally writing
// the chosen nominal sizes back to the pipes.
//
// Every source→appliance path is held to the gas's drop budget (natural
// gas: 1 mbar meter outlet to appliance, BS 6891). Appliances with no heat
// input are listed, not guessed.

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
using StingTools.Core.Gas;
using StingTools.Core.Mep.Networks;
using StingTools.UI;

namespace StingTools.Commands.Gas
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class GasPipeSizingCommand : IExternalCommand
    {
        private const string Title = "STING Gas — Pipe Sizing";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;

                var picked = ctx.UIDoc?.Selection?.GetElementIds()?.Select(id => doc.GetElement(id))
                                 .Where(e => e != null).ToList() ?? new List<Element>();
                if (picked.Count != 1)
                {
                    TaskDialog.Show(Title, "Select exactly one element at the start of the gas installation — the meter, " +
                                           "the emergency control valve, or the first pipe after it — then run again.");
                    return Result.Cancelled;
                }
                var source = picked[0];

                var warnings = new List<string>();
                GasDesignData data;
                try { data = MepDesignDataLoader.Gas(doc, warnings); }
                catch (Exception ex) { TaskDialog.Show(Title, $"Gas design data could not be read: {ex.Message}"); return Result.Failed; }
                var problems = data.Validate();
                if (problems.Count > 0)
                {
                    TaskDialog.Show(Title, "Gas design data is invalid:\n\n" + string.Join("\n", problems.Take(12)));
                    return Result.Failed;
                }

                int gasIdx = Math.Max(0, data.Gases.FindIndex(g => string.Equals(g.Id, data.DefaultGasId, StringComparison.OrdinalIgnoreCase)));
                int serIdx = Math.Max(0, data.PipeSeries.FindIndex(s => string.Equals(s.Id, data.DefaultSeriesId, StringComparison.OrdinalIgnoreCase)));
                var form = new StingFormDialog(Title, "Gas installation",
                        "Heat inputs are read from the appliances. Sizing picks the smallest bore in the series that keeps " +
                        "every appliance within the drop allowance.")
                    .Choice("gas", "Gas", data.Gases.Select(g =>
                        $"{g.Id} — {g.Label} (s {g.RelativeDensity:0.##}, CV {g.CalorificValueMJm3:0.#} MJ/m³, {g.MaxDropMbar:0.##} mbar)" +
                        (data.GasVerify.TryGetValue(g.Id, out var v) && v ? " (verify)" : "")), gasIdx)
                    .Choice("series", "Pipe series", data.PipeSeries.Select(s => s.Label), serIdx)
                    .Choice("mode", "Action", new[]
                    {
                        "Check the modelled sizes",
                        "Size and report only",
                        "Size and apply the sizes to the pipes"
                    }, 1)
                    .Number("allowance", "Drop allowance override (mbar, 0 = gas default)", 0, 0, 50);
                if (form.ShowDialog() != true) return Result.Cancelled;

                var gasBase = data.Gases[Math.Max(0, form.ChoiceIndex("gas"))];
                var gas = new GasProperties
                {
                    Id = gasBase.Id, Label = gasBase.Label, RelativeDensity = gasBase.RelativeDensity,
                    CalorificValueMJm3 = gasBase.CalorificValueMJm3, Source = gasBase.Source,
                    MaxDropMbar = form.Get("allowance") > 0 ? form.Get("allowance") : gasBase.MaxDropMbar
                };
                var series = data.PipeSeries[Math.Max(0, form.ChoiceIndex("series"))];
                int mode = form.ChoiceIndex("mode");

                var opts = new FlowTreeBuildOptions
                {
                    TerminalPredicate = IsAppliance,
                    ReadTerminal = (el, node) => node.LoadKw = ReadLoadKw(el, data),
                    EquivalentBores = data.EquivalentBores
                };
                var tree = RevitFlowTreeBuilder.Build(doc, source, opts);
                warnings.AddRange(tree.Warnings);
                if (tree.Root == null || tree.Root.Descendants().All(n => !n.IsTerminal))
                {
                    TaskDialog.Show(Title, "No appliance is connected to the selected element through pipework.\n\n" +
                                           string.Join("\n", warnings.Take(8)));
                    return Result.Failed;
                }

                var res = mode == 0 ? GasPipeSizer.Check(tree.Root, gas) : GasPipeSizer.Size(tree.Root, gas, series.Sizes);
                warnings.AddRange(res.Warnings);
                if (res.Nodes.Count == 0)
                {
                    TaskDialog.Show(Title, "The calculation could not run:\n\n" + string.Join("\n", warnings.Take(12)));
                    return Result.Failed;
                }

                int applied = 0, applyFailed = 0;
                if (mode == 2)
                    (applied, applyFailed) = Apply(doc, res, tree, series, warnings);

                string csv = ExportCsv(doc, res, tree, gas, warnings);
                Report(res, gas, series, mode, applied, applyFailed, data, warnings, csv);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("GasPipeSizingCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>An appliance is any connected family instance that is not pipework.</summary>
        private static bool IsAppliance(Element e)
        {
            if (!(e is FamilyInstance)) return false;
            var bic = (BuiltInCategory)(e.Category?.Id.Value ?? 0);
            return bic != BuiltInCategory.OST_PipeFitting && bic != BuiltInCategory.OST_PipeAccessory;
        }

        private static double ReadLoadKw(Element el, GasDesignData data)
        {
            var fi = el as FamilyInstance;
            foreach (var name in data.ApplianceLoadParameters)
            {
                try
                {
                    var p = el.LookupParameter(name) ?? fi?.Symbol?.LookupParameter(name);
                    if (p == null || !p.HasValue) continue;
                    double v = 0;
                    if (p.StorageType == StorageType.Double)
                    {
                        // A power-typed parameter is stored in internal units (W-based);
                        // a plain number is taken as kW, which is what the name says.
                        var spec = p.Definition?.GetDataType();
                        v = spec != null && spec == SpecTypeId.HvacPower
                            ? UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Kilowatts)
                            : p.AsDouble();
                    }
                    else if (p.StorageType == StorageType.Integer) v = p.AsInteger();
                    else double.TryParse(p.AsString(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
                    if (v > 0) return v;
                }
                catch (Exception ex) { StingLog.Warn($"Gas load {el.Id} '{name}': {ex.Message}"); }
            }
            return 0;
        }

        private static (int ok, int failed) Apply(Document doc, GasSizingResult res, FlowTreeBuildResult tree,
            GasPipeSeries series, List<string> warnings)
        {
            int ok = 0, failed = 0;
            var changes = res.Nodes.Values.Where(n => n.Node.Kind == FlowNodeKind.Pipe && n.Changed).ToList();
            if (changes.Count == 0) return (0, 0);
            using (var tx = new Transaction(doc, "STING Gas Pipe Sizing"))
            {
                tx.Start();
                foreach (var n in changes)
                {
                    if (!tree.ElementIdByNode.TryGetValue(n.Node, out var id)) continue;
                    var size = series.Sizes.FirstOrDefault(s => Math.Abs(s.BoreMm - n.BoreMm) < 0.01);
                    var pipe = doc.GetElement(id) as Pipe;
                    var p = pipe?.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                    if (size == null || p == null || p.IsReadOnly) { failed++; continue; }
                    try
                    {
                        p.Set(size.NominalMm / 304.8);
                        double got = pipe.Diameter * 304.8;
                        if (Math.Abs(got - size.NominalMm) > 0.5)
                        {
                            failed++;
                            warnings.Add($"Pipe {id.Value}: pipe type has no {size.Label} size (got {got:F0} mm).");
                        }
                        else ok++;
                    }
                    catch (Exception ex) { failed++; warnings.Add($"Pipe {id.Value}: {ex.Message}"); }
                }
                tx.Commit();
            }
            return (ok, failed);
        }

        private static string ExportCsv(Document doc, GasSizingResult res, FlowTreeBuildResult tree, GasProperties gas, List<string> warnings)
        {
            try
            {
                string path = StingPaths.ExportFile(doc, "Schedule", "GasPipeSizing_" + gas.Id, ".csv");
                var sb = new StringBuilder();
                sb.AppendLine("ElementId,Kind,Label,Flow_m3h,Bore_mm,Size,ModelledBore_mm,Drop_mbar,Velocity_ms,Length_m,EquivLength_m,Load_kW");
                foreach (var n in res.Nodes.Values)
                {
                    tree.ElementIdByNode.TryGetValue(n.Node, out var id);
                    sb.AppendLine(string.Join(",", new[]
                    {
                        id?.Value.ToString(CultureInfo.InvariantCulture) ?? "", n.Node.Kind.ToString(),
                        "\"" + (n.Node.Label ?? "").Replace("\"", "\"\"") + "\"",
                        F(n.FlowM3h, "0.000"), F(n.BoreMm), "\"" + n.SizeLabel + "\"", F(n.ModelledBoreMm),
                        F(n.DropMbar, "0.000"), F(n.VelocityMs, "0.00"), F(n.Node.LengthM, "0.00"), F(n.Node.EquivLengthM, "0.00"),
                        n.Node.IsTerminal ? F(n.Node.LoadKw) : ""
                    }));
                }
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                return path;
            }
            catch (Exception ex) { warnings.Add($"CSV export failed: {ex.Message}"); return null; }
        }

        private static string F(double v, string fmt = "0.0") => v.ToString(fmt, CultureInfo.InvariantCulture);

        private static void Report(GasSizingResult res, GasProperties gas, GasPipeSeries series, int mode,
            int applied, int applyFailed, GasDesignData data, List<string> warnings, string csv)
        {
            var panel = StingResultPanel.Create("Gas Pipe Sizing");
            panel.SetSubtitle($"{gas.Label} · {series.Label} · Pole's formula · allowance {gas.MaxDropMbar:0.##} mbar");
            var s = panel.AddSection("RESULT")
                 .Metric("Total heat input", $"{res.TotalLoadKw:F1} kW")
                 .Metric("Total flow", $"{res.TotalFlowM3h:F2} m³/h (no diversity)")
                 .Metric("Appliances", res.Nodes.Keys.Count(n => n.IsTerminal).ToString());
            if (res.Ok) s.MetricHighlight("Worst appliance", $"{res.WorstPathDropMbar:F2} mbar — within allowance", res.WorstAppliance?.Label);
            else s.MetricError("Worst appliance", $"{res.WorstPathDropMbar:F2} mbar — OVER {gas.MaxDropMbar:0.##} mbar", res.WorstAppliance?.Label);
            if (mode == 2) s.Metric("Pipes resized", $"{applied}" + (applyFailed > 0 ? $" ({applyFailed} could not be set)" : ""));

            var rows = res.Nodes.Values.Where(n => n.Node.Kind == FlowNodeKind.Pipe)
                .OrderByDescending(n => n.FlowM3h).Take(40)
                .Select(n => new[]
                {
                    n.Node.Label, $"{n.FlowM3h:F2}",
                    string.IsNullOrEmpty(n.SizeLabel) ? $"{n.BoreMm:F1} bore" : n.SizeLabel,
                    n.ModelledBoreMm > 0 ? $"{n.ModelledBoreMm:F1}" : "—", $"{n.DropMbar:F3}", $"{n.VelocityMs:F1}"
                }).ToList();
            panel.AddSection(mode == 0 ? "PIPES (as modelled)" : "PIPES (sized)")
                 .Table(new[] { "Pipe", "m³/h", "Size", "Modelled bore", "mbar", "m/s" }, rows);

            var apps = res.Nodes.Values.Where(n => n.Node.IsTerminal).OrderBy(n => n.Node.LoadKw)
                .Select(n => new[] { n.Node.Label, n.Node.LoadKw > 0 ? $"{n.Node.LoadKw:F1}" : "NO LOAD", $"{n.FlowM3h:F2}" }).ToList();
            panel.AddSection("APPLIANCES").Table(new[] { "Appliance", "kW", "m³/h" }, apps);

            var basis = panel.AddSection("BASIS");
            basis.Text("h = s·L·(Q/0.0071)²/d⁵ (Pole's formula), Q = kW × 3.6 / CV. Fittings and valves by equivalent length in bores.");
            basis.Text("No diversity is applied — every appliance is taken at full heat input.");
            if (data.GasVerify.TryGetValue(gas.Id, out var v) && v) basis.Text($"Gas '{gas.Id}' figures are marked verify — confirm with the supplier.");
            foreach (var src in data.Sources) basis.Text("Data: " + src);
            if (!string.IsNullOrEmpty(csv)) basis.Text("CSV: " + csv);

            if (warnings.Count > 0)
            {
                var w = panel.AddSection("WARNINGS");
                foreach (var line in warnings.Distinct().Take(20)) w.Text(line);
            }
            panel.Show();
        }
    }
}
