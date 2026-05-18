// PVArrayPlacementCommand.cs — STING Phase 179 F4
//
// Places a grid of PV panel family instances over every roof element in the
// active document.  Panels are sized to a standard 60-cell module
// (1 722 mm × 1 134 mm) with 50 mm portrait gap and 800 mm row-spacing for
// maintenance access.  If no PV-named family symbol is loaded the command
// reports what family to load and returns Succeeded without placing anything.
//
// Workflow tag: Placement_PVArray

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Placement
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PVArrayPlacementCommand : IExternalCommand
    {
        // Standard 60-cell monocrystalline module footprint (mm)
        private const double PanelWidthMm = 1722.0;
        private const double PanelHeightMm = 1134.0;
        private const double GapPortraitMm = 50.0;   // between columns
        private const double GapRowMm      = 800.0;  // row spacing (maintenance)
        private const double ElevOffsetFt  = 0.033;  // ~10 mm clearance above roof

        // ft conversion helpers
        private static double MmToFt(double mm) => mm / 304.8;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            var doc   = ctx.Doc;
            var uidoc = ctx.UIDoc;

            // Collect all roof elements.
            var roofs = new FilteredElementCollector(doc)
                .OfClass(typeof(RoofBase))
                .WhereElementIsNotElementType()
                .Cast<RoofBase>()
                .ToList();

            if (roofs.Count == 0)
            {
                TaskDialog.Show("STING PV Array",
                    "No roof elements found in the active document.\n" +
                    "Place at least one roof before running this command.");
                return Result.Cancelled;
            }

            // Read project-level PV system type if stamped.
            var projInfo  = doc.ProjectInformation;
            string pvType = projInfo?.LookupParameter("PV_SYSTEM_TYPE_TXT")?.AsString()
                            ?? "Monocrystalline";
            double panelKwp = 0.400; // default 400 Wp

            // Find a PV panel family symbol by common naming conventions.
            var pvSym = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s =>
                    s.Name.IndexOf("PV",          StringComparison.OrdinalIgnoreCase) >= 0 ||
                    s.Name.IndexOf("Solar",       StringComparison.OrdinalIgnoreCase) >= 0 ||
                    s.Name.IndexOf("PhotoVoltaic",StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (s.Family?.Name ?? "").IndexOf("PV",          StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (s.Family?.Name ?? "").IndexOf("Solar",       StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (s.Family?.Name ?? "").IndexOf("PhotoVoltaic",StringComparison.OrdinalIgnoreCase) >= 0);

            int placed   = 0;
            int warnings = 0;
            var warningMessages = new List<string>();

            // Panel dimensions in Revit internal feet.
            double panelW = MmToFt(PanelWidthMm);
            double panelH = MmToFt(PanelHeightMm);
            double gapW   = MmToFt(GapPortraitMm);
            double gapH   = MmToFt(GapRowMm);

            using (var tx = new Transaction(doc, "STING Place PV Array"))
            {
                tx.Start();

                if (pvSym != null && !pvSym.IsActive)
                {
                    try { pvSym.Activate(); } catch { /* non-fatal */ }
                }

                foreach (var roof in roofs)
                {
                    try
                    {
                        var bb = roof.get_BoundingBox(null);
                        if (bb == null)
                        {
                            warningMessages.Add(
                                $"Roof {(roof.Name ?? roof.Id.ToString())}: no bounding box — skipped.");
                            warnings++;
                            continue;
                        }

                        var levelId = roof.LevelId;
                        if (levelId == ElementId.InvalidElementId)
                        {
                            warningMessages.Add(
                                $"Roof {(roof.Name ?? roof.Id.ToString())}: no associated level — skipped.");
                            warnings++;
                            continue;
                        }

                        var level = doc.GetElement(levelId) as Level;
                        if (level == null)
                        {
                            warningMessages.Add(
                                $"Roof {(roof.Name ?? roof.Id.ToString())}: level element missing — skipped.");
                            warnings++;
                            continue;
                        }

                        if (pvSym == null)
                        {
                            warningMessages.Add(
                                $"Roof {(roof.Name ?? roof.Id.ToString())}: no PV panel family loaded — skipped.");
                            warnings++;
                            continue;
                        }

                        // Grid extents: leave a half-panel margin around the
                        // bounding box edge so the first/last panel fits.
                        double xStart = bb.Min.X + panelW / 2.0;
                        double yStart = bb.Min.Y + panelH / 2.0;
                        double xEnd   = bb.Max.X - panelW / 2.0;
                        double yEnd   = bb.Max.Y - panelH / 2.0;

                        if (xEnd < xStart || yEnd < yStart)
                        {
                            warningMessages.Add(
                                $"Roof {(roof.Name ?? roof.Id.ToString())}: too small for one panel — skipped.");
                            warnings++;
                            continue;
                        }

                        double roofTopZ = bb.Max.Z + ElevOffsetFt;

                        for (double px = xStart; px <= xEnd; px += panelW + gapW)
                        {
                            for (double py = yStart; py <= yEnd; py += panelH + gapH)
                            {
                                try
                                {
                                    var pt   = new XYZ(px, py, roofTopZ);
                                    var inst = doc.Create.NewFamilyInstance(
                                        pt, pvSym, level,
                                        Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                                    if (inst != null)
                                    {
                                        ParameterHelpers.SetString(inst, "PV_SYSTEM_TYPE_TXT",
                                            pvType, true);
                                        ParameterHelpers.SetString(inst, "PV_PANEL_KWP",
                                            panelKwp.ToString("F3"), true);
                                        placed++;
                                    }
                                }
                                catch (Exception exInner)
                                {
                                    StingLog.Warn(
                                        $"PVArray: panel at ({px:F2},{py:F2}) failed — {exInner.Message}");
                                    warnings++;
                                }
                            }
                        }
                    }
                    catch (Exception exOuter)
                    {
                        StingLog.Warn($"PVArray roof {roof.Id}: {exOuter.Message}");
                        warnings++;
                    }
                }

                tx.Commit();
            }

            // Build result message.
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"PV panels placed : {placed}");
            if (warnings > 0)
                sb.AppendLine($"Warnings         : {warnings}");

            if (pvSym == null)
            {
                sb.AppendLine();
                sb.AppendLine("No PV panel family was found in the project.");
                sb.AppendLine("Load a family whose name contains 'PV Panel', 'Solar Panel',");
                sb.AppendLine("or 'PhotoVoltaic Panel' and re-run this command.");
            }

            if (warningMessages.Count > 0)
            {
                sb.AppendLine();
                foreach (var w in warningMessages.Take(8))
                    sb.AppendLine("• " + w);
                if (warningMessages.Count > 8)
                    sb.AppendLine($"  … and {warningMessages.Count - 8} more (see StingTools.log).");
            }

            TaskDialog.Show("STING PV Array Placement", sb.ToString().TrimEnd());
            return Result.Succeeded;
        }
    }
}
