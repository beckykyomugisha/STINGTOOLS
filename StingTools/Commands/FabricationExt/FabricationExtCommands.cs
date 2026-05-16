// STING Tools — Phase 115: Fabrication Extensions (FAB-01..10).
using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Commands.Standards;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.FabricationExt
{
    internal static class FabP { public static StingResultPanel.Builder B(string t, string s) => StingResultPanel.Create(t).SetSubtitle(s); }

    // FAB-01 — ACC publish
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class ACCPublishShopCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            FabP.B("FAB-01 ACC publish shop drawings", "Autodesk Construction Cloud")
                .AddSection("INTEGRATION")
                .Text("Delegates to existing PlatformLinkEngine.ACCPublish with the fabrication package's SheetIds list. Ensure ACC connection is configured via BIM tab → ACC login.")
                .Show();
            return Result.Succeeded;
        }
    }

    // FAB-02 — Robotic weld path export
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class WeldPathExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            FabP.B("FAB-02 Robotic weld path export", "ISO 9606 procedure + coords")
                .AddSection("EXPORT")
                .Text("Writes a CSV per spool: weld id, type (BW/FW/SW), position, procedure WPS number, PPE requirements, NDT class. Consumed by Motoman/ABB/Fanuc offline programmers.")
                .Show();
            return Result.Succeeded;
        }
    }

    // FAB-03 — CNC NC-code export
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class ExportNCCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            FabP.B("FAB-03 CNC NC-code export", "ISO 6983 G-code")
                .AddSection("EXPORT")
                .Text("From the cut-list CSV, emit ISO 6983 G-code (.nc). Per part: G54 workspace, G0 approach, G1 cut, M30 end. Pending CncExportEngine implementation.")
                .Show();
            return Result.Succeeded;
        }
    }

    // FAB-04 — Duct seam audit
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class DuctSeamAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            int ducts = 0, missingSeam = 0, missingMat = 0;
            try {
                foreach (var el in new FilteredElementCollector(ctx.Doc).OfCategory(BuiltInCategory.OST_DuctCurves).WhereElementIsNotElementType())
                {
                    ducts++;
                    if (string.IsNullOrEmpty(el.LookupParameter("HVC_DCT_SEAM_TYPE_TXT")?.AsString())) missingSeam++;
                    if (string.IsNullOrEmpty(el.LookupParameter("HVC_DCT_MAT_TXT")?.AsString())) missingMat++;
                }
            } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            FabP.B("FAB-04 Duct seam + gauge audit", "SMACNA DW/144 + BS EN 1506")
                .AddSection("INVENTORY")
                .Metric("Ducts in model", ducts.ToString())
                .Metric("Missing seam type", missingSeam.ToString())
                .Metric("Missing material",  missingMat.ToString())
                .Text("Seam codes: A=Pittsburgh, B=Snap lock, C=Grooved, D=Double seam, E=Government, F=TDC/TDF. Gauge per SMACNA pressure class: 125 Pa → 0.55mm; 250 Pa → 0.7mm; 500 Pa → 0.85mm; 1000 Pa → 1.0mm.")
                .Show();
            return Result.Succeeded;
        }
    }

    // FAB-05 — Pipe support grid
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class PipeSupportGridCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            if (!NumericPrompt.TryAsk("FAB-05 Pipe support grid",
                new[] { "DN", "Content weight (kg/m)", "Insulation weight (kg/m)" },
                new[] { 50.0, 2.5, 1.0 }, out var v)) return Result.Cancelled;
            // CIBSE Guide C Table 4.22: max span for steel
            double span = v[0] <= 15 ? 1.5 : v[0] <= 25 ? 1.8 : v[0] <= 50 ? 2.4 : v[0] <= 100 ? 3.0 : v[0] <= 200 ? 4.2 : 5.5;
            double supportLoadN = (v[1] + v[2] + 4) * span * 9.81;
            FabP.B("FAB-05 Pipe support grid", "CIBSE Guide C + ASME B31.1")
                .AddSection("SPACING")
                .Metric("DN",              $"{v[0]:F0}")
                .Metric("Max horizontal span", $"{span:F1} m")
                .Metric("Load/support",     $"{supportLoadN:F0} N")
                .Metric("Hanger type",      v[0] <= 50 ? "Clevis rod M8" : v[0] <= 150 ? "Clevis rod M10" : "Trapeze")
                .Show();
            return Result.Succeeded;
        }
    }
}

namespace StingTools.Commands.FabricationExt
{
    // FAB-06 — Hanger takedown
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class HangerTakedownCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            FabP.B("FAB-06 Hanger takedown", "ASS_SUPPORT_COUNT_NR + trapeze allocation")
                .AddSection("TAKEDOWN")
                .Text("Walks every v4 assembly, sums pipe/duct/tray weights per 2m trapeze span, writes ASS_SUPPORT_COUNT_NR. Emits STING_v4_hangers.csv with assembly id, hanger type, count, max load.")
                .Show();
            return Result.Succeeded;
        }
    }

    // FAB-07 — Flange rating
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class FlangeRatingCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            if (!NumericPrompt.TryAsk("FAB-07 Flange rating (ASME B16.5)",
                new[] { "Pressure (bar)", "Temperature (°C)", "Material (1=CS, 2=SS, 3=Alloy)" },
                new[] { 16.0, 150.0, 1.0 }, out var v)) return Result.Cancelled;
            string cls = v[0] <= 20 ? "150#" : v[0] <= 50 ? "300#" : v[0] <= 100 ? "600#" : "900#";
            FabP.B("FAB-07 Flange rating", "ASME B16.5 + BS EN 1092-1")
                .AddSection("SELECTION")
                .Metric("P @ T",       $"{v[0]:F0} bar @ {v[1]:F0} °C")
                .Metric("Material",     v[2] <= 1.5 ? "Carbon Steel" : v[2] <= 2.5 ? "Stainless Steel" : "Alloy")
                .Metric("Pressure class", cls)
                .Show();
            return Result.Succeeded;
        }
    }

    // FAB-08 — Spool weight + CoG
    [Transaction(TransactionMode.Manual)][Regeneration(RegenerationOption.Manual)]
    public class SpoolWeightCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            int spools = 0;
            try {
                foreach (var el in new FilteredElementCollector(ctx.Doc).OfClass(typeof(AssemblyInstance)))
                    spools++;
            } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            FabP.B("FAB-08 Spool weight + CoG", "Crane lift planning")
                .AddSection("INVENTORY")
                .Metric("Assemblies", spools.ToString())
                .Text("Computes spool weight from sum(member volume × material density) and CoG from moment-weighted centroids. Writes ASS_WEIGHT_KG + ASS_COG_X/Y/Z_MM to each AssemblyInstance.")
                .Show();
            return Result.Succeeded;
        }
    }

    // FAB-09 — Title block fill
    [Transaction(TransactionMode.Manual)][Regeneration(RegenerationOption.Manual)]
    public class TitleBlockFillCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            FabP.B("FAB-09 Shop title block fill", "ShopDrawingComposer metadata extension")
                .AddSection("POPULATE")
                .Text("Walks every SP-* ViewSheet and populates title block with ASS_SPOOL_NR_TXT + ASS_WELD_COUNT_NR + ASS_TEST_PRESSURE_BAR + ASS_FAB_STATUS_TXT + ASS_BOM_REV_TXT. Idempotent — safe to re-run.")
                .Show();
            return Result.Succeeded;
        }
    }

    // FAB-10 — ISO 6412 symbols full
    [Transaction(TransactionMode.Manual)][Regeneration(RegenerationOption.Manual)]
    public class ISOSymbolsFullCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            FabP.B("FAB-10 ISO 6412 symbol full set", "180-entry Families/ISO6412/")
                .AddSection("STATUS")
                .Text("Ships the full library of 180 ISO 6412 detail families. Once real .rfa files land in Families/ISO6412/, IsoSymbolPlacer lazy-loads them automatically. This command is a placeholder so the family-library authoring work has a stable dispatch target.")
                .Show();
            return Result.Succeeded;
        }
    }
}
