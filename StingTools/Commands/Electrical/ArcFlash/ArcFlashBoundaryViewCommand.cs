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
    /// Colours each circle by its incident-energy band (STING_ARC_FLASH_PPE.json
    /// energyBands — presentation, not a PPE category; DSCH-25). When that file does not
    /// load, the circles are drawn uncoloured and the dialog says why. Boundaries are
    /// IEEE 1584-2018 values (<see cref="ArcFlashEngine.Basis"/>).
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

            var presentation = ArcFlashPresentation.Current;
            int drawn = 0, skipped = 0;
            using (var tx = new Transaction(doc, "STING Arc Flash Boundary Circles"))
            {
                tx.Start();
                foreach (var panel in panels)
                {
                    try
                    {
                        // Canonical via ParamRegistry: ELC_ARC_FLASH_BOUNDARY_MM and
                        // ELC_ARC_FLASH_IE_CAL_CM2. The band is taken from the incident
                        // energy, not from ELC_ARC_FLASH_PPE_CAT (which holds the band's
                        // label text since DSCH-25).
                        double bdMm = ParseDouble(panel.LookupParameter(ParamRegistry.ELC_ARC_FLASH_BD)?.AsString());
                        if (bdMm <= 0) { skipped++; continue; }   // "N/A" = not calculated -> no circle
                        double ieCal = ParseDouble(panel.LookupParameter(ParamRegistry.ELC_ARC_FLASH_IE)?.AsString());
                        XYZ origin = (panel.Location as LocationPoint)?.Point;
                        if (origin == null) { skipped++; continue; }
                        double bdFt = bdMm / 304.8;

                        // A detail curve must lie in the view plane. Project the panel
                        // location onto the plane through view.Origin normal to
                        // view.ViewDirection and draw the arc there. (The old code
                        // passed an ElementId to SketchPlane.Create and then built a
                        // plane at the panel's own elevation - neither is the view plane.)
                        XYZ n = view.ViewDirection.Normalize();
                        XYZ centre = origin - n.Multiply(n.DotProduct(origin - view.Origin));
                        Plane plane = Plane.CreateByNormalAndOrigin(n, centre);
                        Arc arc = Arc.Create(plane, bdFt, 0, 2 * Math.PI);
                        var circle = doc.Create.NewDetailCurve(view, arc);
                        // Colour the curve by incident-energy band via override.
                        var band = ieCal > 0 ? presentation.BandFor(ieCal) : null;
                        var ogs = new OverrideGraphicSettings();
                        if (band != null)
                            ogs.SetProjectionLineColor(new Color(band.ViewColour.R, band.ViewColour.G, band.ViewColour.B));
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
                $"Skipped {skipped} (not calculated, no boundary value or no location).\n" +
                $"Boundaries are {ArcFlashEngine.BasisShort} - verify with a licensed study.\n" +
                (presentation.Loaded
                    ? "Colour = incident-energy band (presentation only, not a PPE category).\n\n"
                    : $"CIRCLES NOT COLOURED: {presentation.LoadError}\n\n") +
                "Run Elec_ClearOverrides on this view to remove the colour overrides; " +
                "delete the detail curves manually if you want to clear the geometry.");
            return Result.Succeeded;
        }

        private static double ParseDouble(string s) =>
            StingTools.Core.NumberText.ParseOr(s);
    }
}
