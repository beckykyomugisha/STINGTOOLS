// StingTools v4 MVP — Phase J cable-add + route commands.
//
// Two commands:
//   AddCableCommand   — picks source + destination equipment,
//                       routes via CableRouter, computes voltage
//                       drop via VoltageDropSolver, appends to the
//                       CableManifest JSON.
//   ListCablesCommand — prints the manifest into a result panel.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using StingTools.Core;
using StingTools.Core.Calc;
using StingTools.Core.Electrical;
using StingTools.UI;

namespace StingTools.Commands.Electrical
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AddCableCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc   = ctx.Doc;
            var uidoc = ctx.UIDoc;

            Element src, dst;
            try
            {
                TaskDialog.Show("STING v4 — Add Cable",
                    "Pick the SOURCE equipment (distribution board / consumer unit), " +
                    "then the DESTINATION equipment (outlet / fixture).");
                var refSrc = uidoc.Selection.PickObject(ObjectType.Element,
                    new FixtureFilter(), "Pick source equipment");
                var refDst = uidoc.Selection.PickObject(ObjectType.Element,
                    new FixtureFilter(), "Pick destination equipment");
                src = doc.GetElement(refSrc);
                dst = doc.GetElement(refDst);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }

            var manifest = CableManifest.Load(doc);
            if (!string.IsNullOrEmpty(manifest.LoadError))
            {
                // Adding to an empty stand-in and saving would overwrite every
                // cable already recorded in the unreadable file.
                TaskDialog.Show("STING v4 — Add Cable", manifest.DescribeEmpty());
                return Result.Failed;
            }

            var route = CableRouter.Route(doc, src, dst);

            var cable = new StingCable
            {
                SourceEquipmentId = src.UniqueId,
                DestEquipmentId   = dst.UniqueId,
                PanelName         = src.Name ?? "",
                CircuitId         = BuildCircuitId(src, dst),
                TotalLengthM      = route.LengthM,
                RouteTrayIds      = new List<long>(route.TrayIds),
            };

            // ELC-7: tie the record to its Revit circuit (the destination's
            // circuit fed from the picked source) so auto-route, fill and
            // busbar demand can find it; and take the voltage-drop inputs from
            // that circuit instead of a hardcoded 10 A / 230 V / 1-phase.
            ElectricalSystem sys = null;
            CircuitMatch match;
            try { match = CableCircuitResolver.Build(doc).Resolve(cable, out sys); }
            catch (Exception ex)
            {
                StingLog.Warn($"AddCable circuit resolve: {ex.Message}");
                match = new CircuitMatch { Reason = ex.Message };
            }
            if (match.Found && sys != null) cable.CircuitElementId = sys.Id.Value;

            var inputs = VdInputsFrom(sys);
            manifest.Add(cable);

            var vd = VoltageDropSolver.Solve(new VoltageDropQuery
            {
                CsaMm2 = cable.CsaMm2,
                LoadAmps = inputs.Amps,
                LengthM = Math.Max(0.1, route.LengthM),
                NominalVoltageV = inputs.Volts,
                ThreePhase = inputs.ThreePhase,
                Material = cable.ConductorMaterial,
            });
            cable.VoltageDropPct = vd.VoltDropPct;

            manifest.Save(doc);

            var panel = StingResultPanel.Create("v4 Add Cable");
            panel.SetSubtitle(route.Success ? $"Routed {route.LengthM:F1} m" : "Route failed");
            panel.AddSection("CABLE")
                 .Metric("Sequence",     cable.SequenceNumber.ToString())
                 .Metric("CSA",          cable.CsaMm2.ToString("F1") + " mm²")
                 .Metric("Cores",        cable.CoreCount.ToString())
                 .Metric("Length",       cable.TotalLengthM.ToString("F1") + " m")
                 .Metric("VoltDrop %",   cable.VoltageDropPct.ToString("F2"))
                 .Metric("VD lighting",  vd.LightingPass ? "OK" : "FAIL (>3%)")
                 .Metric("VD power",     vd.PowerPass ? "OK" : "FAIL (>5%)")
                 .Metric("Trays",        route.TrayIds.Count.ToString());
            panel.AddSection("CIRCUIT")
                 .Metric("Circuit", match.Found && sys != null
                     ? $"{SafePanelName(sys)} / {SafeCircuitNumber(sys)} (by {CableCircuitIdentity.Describe(match.Method)})"
                     : "not matched — " + match.Reason);
            var vdSec = panel.AddSection("VOLTAGE-DROP INPUTS")
                 .Metric("Load",    $"{inputs.Amps:0.##} A ({inputs.AmpsSource})")
                 .Metric("Voltage", $"{inputs.Volts:0.#} V ({inputs.VoltsSource})")
                 .Metric("Phases",  $"{(inputs.ThreePhase ? "3-phase" : "1-phase")} ({inputs.PhaseSource})")
                 .Metric("CSA",     $"{cable.CsaMm2:0.##} mm² (manifest default — size the cable to replace it)");
            if (inputs.AnyDefault)
                vdSec.Text("One or more voltage-drop inputs are DEFAULTS, not circuit data — " +
                           "treat the VoltDrop % above as indicative only.");
            if (!route.Success)
            {
                panel.AddSection("DIAGNOSTICS").Text(route.FailureReason);
            }
            panel.Show();
            return Result.Succeeded;
        }

        private static string BuildCircuitId(Element src, Element dst)
        {
            try { return CableCircuitIdentity.BuildLegacyCircuitId(src.Name, dst.Id.Value); }
            catch (Exception ex) { StingLog.Warn($"AddCable BuildCircuitId: {ex.Message}"); return ""; }
        }

        private sealed class VdInputs
        {
            public double Amps = 10.0;   public string AmpsSource  = "DEFAULT 10 A — no circuit load";
            public double Volts = 230.0; public string VoltsSource = "DEFAULT 230 V — no circuit voltage";
            public bool ThreePhase;      public string PhaseSource = "DEFAULT single-phase — no circuit poles";
            public bool AnyDefault => AmpsSource.StartsWith("DEFAULT") || VoltsSource.StartsWith("DEFAULT")
                                      || PhaseSource.StartsWith("DEFAULT");
        }

        /// <summary>
        /// Voltage-drop inputs from the circuit. ElectricalSystem.Voltage is in
        /// Revit internal units (1 V = 10.7639), so it goes through ElecUnits;
        /// ApparentCurrent is plain amps. Each value that cannot be read keeps
        /// its default AND says so.
        /// </summary>
        private static VdInputs VdInputsFrom(ElectricalSystem sys)
        {
            var r = new VdInputs();
            if (sys == null) return r;
            try
            {
                double a = sys.ApparentCurrent;
                if (a > 0) { r.Amps = a; r.AmpsSource = "circuit apparent current"; }
                else r.AmpsSource = "DEFAULT 10 A — circuit reports 0 A";
            }
            catch (Exception ex) { StingLog.Warn($"AddCable ApparentCurrent: {ex.Message}"); }
            try
            {
                double v = ElecUnits.VoltsFromInternal(sys.Voltage);
                if (v > 0) { r.Volts = v; r.VoltsSource = "circuit voltage"; }
                else r.VoltsSource = "DEFAULT 230 V — circuit reports 0 V";
            }
            catch (Exception ex) { StingLog.Warn($"AddCable Voltage: {ex.Message}"); }
            try
            {
                int poles = sys.PolesNumber;
                if (poles > 0) { r.ThreePhase = poles >= 3; r.PhaseSource = $"circuit poles = {poles}"; }
            }
            catch (Exception ex) { StingLog.Warn($"AddCable PolesNumber: {ex.Message}"); }
            return r;
        }

        private static string SafePanelName(ElectricalSystem s)
        {
            try { return string.IsNullOrEmpty(s.PanelName) ? "(no panel)" : s.PanelName; }
            catch (Exception ex) { StingLog.Warn($"AddCable PanelName: {ex.Message}"); return "(no panel)"; }
        }

        private static string SafeCircuitNumber(ElectricalSystem s)
        {
            try { return s.CircuitNumber ?? ""; }
            catch (Exception ex) { StingLog.Warn($"AddCable CircuitNumber: {ex.Message}"); return ""; }
        }
    }

    internal class FixtureFilter : ISelectionFilter
    {
        public bool AllowElement(Element el)
        {
            if (el?.Category == null) return false;
            var bic = (BuiltInCategory)el.Category.Id.Value;
            return bic == BuiltInCategory.OST_ElectricalEquipment
                || bic == BuiltInCategory.OST_ElectricalFixtures
                || bic == BuiltInCategory.OST_LightingFixtures
                || bic == BuiltInCategory.OST_LightingDevices
                || bic == BuiltInCategory.OST_DataDevices
                || bic == BuiltInCategory.OST_CommunicationDevices;
        }
        public bool AllowReference(Reference r, XYZ p) => true;
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ListCablesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var manifest = CableManifest.Load(ctx.Doc);
            var panel = StingResultPanel.Create("v4 Cable Manifest");
            panel.SetSubtitle($"{manifest.Cables.Count} cable(s) on record");
            panel.AddSection("LIST");
            foreach (var c in manifest.Cables.OrderBy(x => x.SequenceNumber))
            {
                panel.Text($"#{c.SequenceNumber:D4} {c.CircuitId}  {c.CsaMm2}×{c.CoreCount} " +
                           $"{c.ConductorMaterial}/{c.InsulationType} {c.Phase}  " +
                           $"L={c.TotalLengthM:F1} m  VD={c.VoltageDropPct:F2}%");
            }
            panel.Show();
            return Result.Succeeded;
        }
    }
}
