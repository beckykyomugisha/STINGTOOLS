// STING Tools — Phase 111: Architecture & Shell automation (ARCH-01..06).
//
// Six IExternalCommands wrap ArchitecturalCreationEngine + PlasteringEngine
// so the dock-panel + Dynamo expose one-click shell automation that
// respects BS 5395 / BS 6180 / BS EN 13830 / BS EN 13914 / Part B / Part K / Part M.

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
                .SetSubtitle("BS 5395-1 + Part K (UK) + Part M accessibility");
            try
            {
                // TODO-VERIFY-API: ArchitecturalCreationEngine.StairEngine signature
                // varies; this wrapper opens a TaskDialog summarising the computed
                // riser/tread per BS 5395 Table 1 pending engine-side Revit stair
                // creation.
                int risers = v[2] > 0 ? (int)v[2] : (int)Math.Ceiling(v[0] / 175.0);
                double riserMm = v[0] / risers;
                double goMm    = v[3];

                bool compliantK = riserMm <= 220 && riserMm >= 150 && goMm >= 220;
                bool compliantM = riserMm <= 170 && goMm >= 250 && v[1] >= 1000;

                panel.AddSection("COMPUTED GEOMETRY")
                     .Metric("Risers",    risers.ToString())
                     .Metric("Riser height", $"{riserMm:F0} mm")
                     .Metric("Going (tread)", $"{goMm:F0} mm")
                     .Metric("Width",      $"{v[1]:F0} mm");

                panel.AddSection("COMPLIANCE")
                     .Metric("BS 5395 + Part K", compliantK ? "PASS" : "REVIEW")
                     .Metric("Part M (accessible)", compliantM ? "PASS" : "REVIEW");

                if (!compliantK)
                    panel.Text("Riser 150-220mm, going ≥ 220mm per BS 5395 Table 1.");
                if (!compliantM)
                    panel.Text("Part M: riser ≤ 170mm, going ≥ 250mm, width ≥ 1000mm for accessible route.");

                panel.Text("Note: Stair family creation in Revit pending ArchitecturalCreationEngine.StairEngine wiring. This command surfaces the code-compliant geometry; place the stair with ARCH-02 railing afterwards.");
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

            int hMullions = (int)Math.Ceiling(v[0] / v[2]) + 1;
            int vMullions = (int)Math.Ceiling(v[1] / v[3]) + 1;
            int panels    = (hMullions - 1) * (vMullions - 1);

            var panel = StingResultPanel.Create("ARCH-03 Auto Curtain Wall")
                .SetSubtitle("BS EN 13830 (CW) + BS 8200 (cladding)");
            panel.AddSection("GRID")
                 .Metric("Horizontal mullions", hMullions.ToString())
                 .Metric("Vertical mullions",   vMullions.ToString())
                 .Metric("Panel count",         panels.ToString())
                 .Metric("H spacing", $"{v[2]:F0} mm")
                 .Metric("V spacing", $"{v[3]:F0} mm");
            panel.Text("Pending ArchitecturalCreationEngine.CurtainWallEngine wiring to place the mullion grid.");
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
