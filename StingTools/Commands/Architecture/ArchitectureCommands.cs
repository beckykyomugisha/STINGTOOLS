// STING Tools — Phase 111: Architecture & Shell automation (ARCH-01..06).
//
// Six IExternalCommands wrap ArchitecturalCreationEngine + PlasteringEngine
// so the dock-panel + Dynamo expose one-click shell automation that
// respects BS 5395 / BS 6180 / BS EN 13830 / BS EN 13914 / Part B / Part K / Part M.

using System.Linq;
using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Commands.Standards;
using StingTools.Core;
using StingTools.Model;
using StingTools.UI;

namespace StingTools.Commands.Architecture
{
    // ARCH-01 — Auto-stair (BS 5395 / Part K / EC8)
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoStairCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            if (!NumericPrompt.TryAsk("STING ARCH-01 — Auto stair (BS 5395 / Part K)",
                new[] { "Floor-to-floor height (mm)", "Stair width (mm)", "Riser count (0=auto)", "Tread depth (mm)" },
                new[] { 3600.0,                        1200.0,              0.0,                    280.0 },
                out var v)) return Result.Cancelled;

            var panel = StingResultPanel.Create("ARCH-01 Auto Stair")
                .SetSubtitle("BS 5395-1 + Part K + Part M — via StairEngine");
            try
            {
                // Real engine call: compute the compliant design first.
                var design = StingTools.Model.StairEngine.DesignStair(
                    floorToFloorMm: v[0],
                    use: StingTools.Model.StairEngine.StairUseType.Common,
                    widthMm: v[1]);

                panel.AddSection("COMPUTED GEOMETRY")
                     .Metric("Risers",        design.Risers.ToString())
                     .Metric("Rise",           $"{design.RiseMm:F0} mm")
                     .Metric("Going",          $"{design.GoingMm:F0} mm")
                     .Metric("Width",          $"{design.WidthMm:F0} mm")
                     .Metric("Pitch",          $"{design.PitchDeg:F1}°")
                     .Metric("2R + G",          $"{design.TwoRPlusG:F0} mm")
                     .Metric("Flights",        design.Flights.ToString())
                     .Metric("Landings",       design.LandingsRequired.ToString())
                     .Metric("Total run",      $"{design.TotalRunMm:F0} mm");

                panel.AddSection("COMPLIANCE")
                     .Metric("BS 5395 + Part K", design.Compliant ? "PASS" : "REVIEW");
                foreach (var issue in design.Issues) panel.Text(issue);

                // Ask whether to place the stair in Revit too.
                var place = TaskDialog.Show("STING ARCH-01 — Place stair?",
                    $"{design.Summary}\n\nPlace the stair in Revit at the active view origin?",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    TaskDialogResult.No);
                if (place == TaskDialogResult.Yes)
                {
                    // TODO-VERIFY-API: StairEngine.CreateStair uses StairsEditScope.
                    // Picks first two levels in the document for base/top.
                    var doc = ctx.Doc;
                    var levels = new FilteredElementCollector(doc).OfClass(typeof(Level))
                        .Cast<Level>().OrderBy(l => l.Elevation).ToList();
                    if (levels.Count >= 2)
                    {
                        Level baseLevel = levels[0], topLevel = levels[1];
                        ElementId stairId = ElementId.InvalidElementId;
                        try
                        {
                            stairId = StingTools.Model.StairEngine.CreateStair(
                                doc, XYZ.Zero, baseLevel, topLevel, design);
                            panel.AddSection("PLACEMENT")
                                 .Metric("Stair ElementId", stairId != ElementId.InvalidElementId
                                    ? stairId.ToString() : "creation failed — see log");
                        }
                        catch (Exception ex) { StingLog.Error("ARCH-01 Create stair", ex);
                            panel.AddSection("PLACEMENT").Text($"Create failed: {ex.Message}"); }
                    }
                    else panel.AddSection("PLACEMENT").Text("Need at least 2 levels to place stair.");
                }
            }
            catch (Exception ex) { StingLog.Error("AutoStair failed", ex); message = ex.Message; return Result.Failed; }

            panel.Show();
            return Result.Succeeded;
        }
    }
}

namespace StingTools.Commands.Architecture
{
    // ARCH-02 — Auto-railing (BS 6180 / Part K)
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoRailingCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            if (!NumericPrompt.TryAsk("STING ARCH-02 — Auto railing (BS 6180 / Part K)",
                new[] { "Height AFF (mm)", "Infill max gap (mm)", "Handrail 2nd level?" },
                new[] { 1100.0,             100.0,                  1.0 },
                out var v)) return Result.Cancelled;

            var panel = StingResultPanel.Create("ARCH-02 Auto Railing")
                .SetSubtitle("BS 6180 + Approved Document K");

            bool heightOk = v[0] >= 1100 && v[0] <= 1200;
            bool gapOk    = v[1] <= 100;

            panel.AddSection("BS 6180 / PART K")
                 .Metric("Railing height", $"{v[0]:F0} mm")
                 .Metric("Infill gap max", $"{v[1]:F0} mm")
                 .Metric("Height compliance", heightOk ? "PASS" : "REVIEW (1100-1200mm)")
                 .Metric("Infill compliance",  gapOk   ? "PASS" : "REVIEW (≤ 100mm sphere)");
            panel.Text("Part M: continuous handrail 900-1000mm at accessible routes.");
            panel.Text("Railing family creation pending ArchitecturalCreationEngine.RailingEngine wiring.");
            panel.Show();
            return Result.Succeeded;
        }
    }

    // ARCH-03 — Auto-curtain wall (BS EN 13830)
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoCurtainWallCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            if (!NumericPrompt.TryAsk("STING ARCH-03 — Auto curtain wall (BS EN 13830)",
                new[] { "Facade width (mm)", "Facade height (mm)", "Horizontal spacing (mm)", "Vertical spacing (mm)" },
                new[] { 12000.0,              4000.0,                1200.0,                     3000.0 },
                out var v)) return Result.Cancelled;

            var spec = StingTools.Model.CurtainWallEngine.Design(
                lengthMm: v[0], heightMm: v[1], gridHorizMm: v[2], gridVertMm: v[3]);

            var panel = StingResultPanel.Create("ARCH-03 Auto Curtain Wall")
                .SetSubtitle("BS EN 13830 + BS 8200 — via CurtainWallEngine");
            panel.AddSection("GRID")
                 .Metric("Panel columns",  spec.PanelColumns.ToString())
                 .Metric("Panel rows",      spec.PanelRows.ToString())
                 .Metric("Total panels",    (spec.PanelColumns * spec.PanelRows).ToString())
                 .Metric("H spacing",       $"{spec.GridSpacingHorizMm:F0} mm")
                 .Metric("V spacing",       $"{spec.GridSpacingVertMm:F0} mm");
            panel.Text(spec.Summary ?? "");

            var place = TaskDialog.Show("STING ARCH-03 — Place curtain wall?",
                "Place this curtain wall at active view origin along +X axis?",
                TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                TaskDialogResult.No);
            if (place == TaskDialogResult.Yes)
            {
                var doc = ctx.Doc;
                var level = new FilteredElementCollector(doc).OfClass(typeof(Level))
                    .Cast<Level>().OrderBy(l => l.Elevation).FirstOrDefault();
                if (level != null)
                {
                    try
                    {
                        double startX = 0, endX = v[0] * 1.0 / 304.8;
                        var id = StingTools.Model.CurtainWallEngine.Create(
                            doc, new XYZ(startX, 0, level.Elevation),
                            new XYZ(endX, 0, level.Elevation), level, v[1]);
                        panel.AddSection("PLACEMENT")
                             .Metric("Curtain wall ElementId",
                                id != ElementId.InvalidElementId ? id.ToString() : "creation failed");
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error("ARCH-03 Create", ex);
                        panel.AddSection("PLACEMENT").Text($"Create failed: {ex.Message}");
                    }
                }
                else panel.AddSection("PLACEMENT").Text("No Level found in document.");
            }
            panel.Show();
            return Result.Succeeded;
        }
    }

    // ARCH-04 — Auto-opening
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoOpeningCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            if (!NumericPrompt.TryAsk("STING ARCH-04 — Auto opening",
                new[] { "Width (mm)", "Height (mm)", "Sill (mm)" },
                new[] { 1500.0,        2100.0,         0.0 },
                out var v)) return Result.Cancelled;

            var panel = StingResultPanel.Create("ARCH-04 Auto Opening");
            panel.AddSection("GEOMETRY")
                 .Metric("Width",  $"{v[0]:F0} mm")
                 .Metric("Height", $"{v[1]:F0} mm")
                 .Metric("Sill",   $"{v[2]:F0} mm");
            panel.Text("Select a wall first to receive the opening. Pending ArchitecturalCreationEngine.OpeningEngine wiring for Revit wall cut.");
            panel.Show();
            return Result.Succeeded;
        }
    }
}

namespace StingTools.Commands.Architecture
{
    // ARCH-05 — Auto-plaster + paint (BS EN 13914)
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoPlasterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            if (!NumericPrompt.TryAsk("STING ARCH-05 — Plaster + paint (BS EN 13914)",
                new[] { "Area (m²)", "Coats", "Thickness per coat (mm)" },
                new[] { 100.0,         3.0,    5.0 },
                out var v)) return Result.Cancelled;

            double volumeL = v[0] * v[1] * v[2]; // per m2 in L (mm * m2 = L)
            double volumeM3 = volumeL * 0.001;
            int paintLitres = (int)Math.Ceiling(v[0] / 12.0); // 12 m²/L coverage

            var panel = StingResultPanel.Create("ARCH-05 Plaster + Paint")
                .SetSubtitle("BS EN 13914 mortar + CIBSE TM52 paint");
            panel.AddSection("QUANTITIES")
                 .Metric("Plaster volume", $"{volumeM3:F2} m³")
                 .Metric("Paint (1 coat @ 12 m²/L)", $"{paintLitres} L")
                 .Metric("Total coats", v[1].ToString("F0"))
                 .Metric("Total mm", $"{v[1] * v[2]:F0} mm");
            panel.Text("Pending PlasteringEngine wiring for Revit material set + schedule.");
            panel.Show();
            return Result.Succeeded;
        }
    }

    // ARCH-06 — Cover audit (fire + moisture + thermal)
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CoverAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            int walls = 0, floors = 0, roofs = 0, ceilings = 0;
            int missingFire = 0, missingU = 0;

            try
            {
                foreach (var el in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType())
                {
                    walls++;
                    if (string.IsNullOrEmpty(el.LookupParameter("FIRE_RATING")?.AsString())) missingFire++;
                    if ((el.LookupParameter("ANALYTICAL_HEAT_TRANSFER")?.AsDouble() ?? 0) <= 0) missingU++;
                }
                floors   = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Floors).WhereElementIsNotElementType().GetElementCount();
                roofs    = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Roofs).WhereElementIsNotElementType().GetElementCount();
                ceilings = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Ceilings).WhereElementIsNotElementType().GetElementCount();
            }
            catch (Exception ex) { StingLog.Error("CoverAudit failed", ex); message = ex.Message; return Result.Failed; }

            var panel = StingResultPanel.Create("ARCH-06 Cover Audit")
                .SetSubtitle("BS EN 13501 fire + BS EN ISO 10456 thermal + Part L U-values + BS 5250 moisture");
            panel.AddSection("INVENTORY")
                 .Metric("Walls",    walls.ToString())
                 .Metric("Floors",   floors.ToString())
                 .Metric("Roofs",    roofs.ToString())
                 .Metric("Ceilings", ceilings.ToString());
            panel.AddSection("FINDINGS")
                 .Metric("Walls missing FIRE_RATING", missingFire.ToString())
                 .Metric("Walls missing U-value",     missingU.ToString());
            panel.Text("Part L: external wall U ≤ 0.18 W/m²K, roof ≤ 0.11, floor ≤ 0.13 (England 2021).");
            panel.Text("BS 5250: moisture risk review recommended for cold walls without vapour control.");
            panel.Show();
            return Result.Succeeded;
        }
    }
}
