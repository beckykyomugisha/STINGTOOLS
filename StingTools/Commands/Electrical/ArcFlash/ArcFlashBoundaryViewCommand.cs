using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Electrical.ArcFlash
{
    /// <summary>
    /// Drops a detail-circle annotation at every panel's footprint on the
    /// active plan view, sized to its arc-flash boundary distance
    /// (ELC_ARC_FLASH_BOUNDARY_MM parameter, populated by ArcFlashCommand
    /// — accessed via the ParamRegistry alias for canonical resolution).
    /// Colour-codes red/orange/yellow/green by PPE category for instant
    /// safety-zone awareness on installation drawings.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ArcFlashBoundaryViewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            var view = doc.ActiveView;
            if (view == null || view.IsTemplate || view.ViewType != ViewType.FloorPlan)
            {
                TaskDialog.Show("STING Arc Flash Boundary",
                    "Activate a floor plan view first — boundary circles are drawn on the active plan.");
                return Result.Cancelled;
            }

            var panels = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType().OfType<FamilyInstance>()
                .ToList();
            if (panels.Count == 0)
            {
                TaskDialog.Show("STING Arc Flash Boundary", "No electrical equipment found.");
                return Result.Cancelled;
            }

            int drawn = 0, skipped = 0;
            using (var tx = new Transaction(doc, "STING Arc Flash Boundary Circles"))
            {
                tx.Start();
                foreach (var panel in panels)
                {
                    try
                    {
                        // Canonical via ParamRegistry: ELC_ARC_FLASH_BOUNDARY_MM
                        // and ELC_ARC_FLASH_PPE_CAT. ParamRegistry.ELC_ARC_FLASH_BD /
                        // _PPE alias these so the lookup matches whichever schema
                        // version the project ships.
                        double bdMm = ParseDouble(panel.LookupParameter(ParamRegistry.ELC_ARC_FLASH_BD)?.AsString());
                        if (bdMm <= 0) { skipped++; continue; }
                        int ppe = (int)ParseDouble(panel.LookupParameter(ParamRegistry.ELC_ARC_FLASH_PPE)?.AsString());
                        XYZ origin = (panel.Location as LocationPoint)?.Point;
                        if (origin == null) { skipped++; continue; }
                        double bdFt = bdMm / 304.8;

                        // Draw circle as a SketchPlane-bound DetailCircle equivalent —
                        // Revit doesn't have a native DetailCircle so we draw an arc
                        // with start = end at angle 0/2π using ModelArc on a sketch
                        // plane at the view's level.
                        SketchPlane sp = SketchPlane.Create(doc, view.SketchPlane?.Id ?? CreateSketchPlaneAtView(doc, view));
                        Plane plane = Plane.CreateByNormalAndOrigin(view.ViewDirection.Normalize(), origin);
                        sp = SketchPlane.Create(doc, plane);
                        Arc arc = Arc.Create(plane, bdFt, 0, 2 * Math.PI);
                        var circle = doc.Create.NewDetailCurve(view, arc);
                        // Colour the curve by PPE category via override
                        var ogs = new OverrideGraphicSettings();
                        ogs.SetProjectionLineColor(PpeColor(ppe));
                        ogs.SetProjectionLineWeight(5);
                        view.SetElementOverrides(circle.Id, ogs);
                        drawn++;
                    }
                    catch (Exception ex) { StingLog.Warn($"AF boundary {panel.Name}: {ex.Message}"); skipped++; }
                }
                tx.Commit();
            }

            TaskDialog.Show("STING Arc Flash Boundary",
                $"Drew {drawn} boundary circle(s) on {view.Name}.\n" +
                $"Skipped {skipped} (no boundary value or location).\n\n" +
                "Run Elec_ClearOverrides on this view to remove the colour overrides; " +
                "delete the detail curves manually if you want to clear the geometry.");
            return Result.Succeeded;
        }

        private static ElementId CreateSketchPlaneAtView(Document doc, View view)
        {
            var lvl = doc.GetElement(view.GenLevel?.Id ?? ElementId.InvalidElementId) as Level
                       ?? new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault();
            if (lvl == null) return ElementId.InvalidElementId;
            return SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, lvl.Elevation))).Id;
        }

        private static Color PpeColor(int ppe) => ppe switch
        {
            >= 4 => new Color(244, 67, 54),    // red
            3    => new Color(255, 87, 34),    // deep orange
            2    => new Color(255, 152, 0),    // orange
            1    => new Color(255, 235, 59),   // yellow
            _    => new Color(76, 175, 80)     // green
        };

        private static double ParseDouble(string s) =>
            double.TryParse(s, out double v) ? v : 0;
    }
}
