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

            var route = CableRouter.Route(doc, src, dst);

            var manifest = CableManifest.Load(doc);
            var cable = new StingCable
            {
                SourceEquipmentId = src.UniqueId,
                DestEquipmentId   = dst.UniqueId,
                PanelName         = src.Name ?? "",
                CircuitId         = BuildCircuitId(src, dst),
                TotalLengthM      = route.LengthM,
                RouteTrayIds      = new List<long>(route.TrayIds),
            };
            manifest.Add(cable);

            // Voltage drop based on assumed 10 A load + 230 V single phase
            // — these are defaults; the real project loads come from
            // ProDesign / EasyPower integration in Phase K.
            var vd = VoltageDropSolver.Solve(new VoltageDropQuery
            {
                CsaMm2 = cable.CsaMm2,
                LoadAmps = 10.0,
                LengthM = Math.Max(0.1, route.LengthM),
                NominalVoltageV = 230.0,
                ThreePhase = false,
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
            if (!route.Success)
            {
                panel.AddSection("DIAGNOSTICS").Text(route.FailureReason);
            }
            panel.Show();
            return Result.Succeeded;
        }

        private static string BuildCircuitId(Element src, Element dst)
        {
            try { return $"{src.Name?.Replace(' ', '_')}-{dst.Id.Value}"; } catch { return ""; }
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
