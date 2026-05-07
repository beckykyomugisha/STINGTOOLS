using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Commands.Electrical.Coordination;
using StingTools.Commands.Electrical.FaultCurrent;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Electrical.ArcFlash
{
    public class ArcFlashRow
    {
        public string PanelName              { get; set; } = "";
        public double FaultKa                { get; set; }
        public double IncidentEnergy_CalCm2  { get; set; }
        public double BoundaryMm             { get; set; }
        public int    PpeCategory            { get; set; }
        public double WorkDistMm             { get; set; }
        public string LabelText              { get; set; } = "";
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ArcFlashCommand : IExternalCommand
    {
        public static List<ArcFlashRow> LastResults { get; private set; } = new List<ArcFlashRow>();

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var faultResults = FaultCurrentCommand.LastResults;
            if (faultResults == null || faultResults.Count == 0)
            {
                TaskDialog.Show("STING Arc Flash",
                    "Run Fault Current Propagation (Phase 178) first.\n" +
                    "Arc-flash calculation requires a fault level at each panel.");
                return Result.Cancelled;
            }

            // Inline options dialog — simpler than a separate Window subclass.
            // Bus gap is resolved per-panel from voltage class via
            // ArcFlashEngine.DefaultBusGapMm() so we don't need a global value.
            double clearingMs = 100.0;
            var optDlg = new TaskDialog("STING Arc Flash — Options")
            {
                MainInstruction = "Clearing time source",
                MainContent = "Choose the upstream-breaker clearing time used in the IEEE 1584 simplified formula.",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            optDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Fixed 100 ms (conservative LV default)");
            optDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "From STING_TCC_DATABASE.json (per-panel)");
            var sel = optDlg.Show();
            bool useFixed;
            if (sel == TaskDialogResult.CommandLink1) useFixed = true;
            else if (sel == TaskDialogResult.CommandLink2) useFixed = false;
            else return Result.Cancelled;

            var panels = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .ToList();

            var byPanelId = faultResults
                .Where(r => r.PanelId is ElementId)
                .ToDictionary(r => ((ElementId)r.PanelId).Value, r => r);

            var results = new List<ArcFlashRow>();
            using (var tx = new Transaction(doc, "STING Arc Flash"))
            {
                tx.Start();
                foreach (var panel in panels)
                {
                    if (!byPanelId.TryGetValue(panel.Id.Value, out var fr)) continue;

                    double voltageV = ParseVoltage(fr.Voltage, panel);
                    // Phase 186 — honour a per-equipment working-distance override.
                    // Reuses the canonical ELC_ARC_FLASH_WORK_DIST_MM (Phase 179):
                    // when the engineer pre-stamps a non-zero value (e.g. for MV
                    // switchgear or NEMA 4 enclosures that need a non-standard
                    // clearance), it wins over the IEEE 1584 default. Zero / blank
                    // = use the default.
                    double overrideMm = 0;
                    try
                    {
                        var p = panel.LookupParameter("ELC_ARC_FLASH_WORK_DIST_MM");
                        if (p != null)
                        {
                            if (p.StorageType == StorageType.Double) overrideMm = p.AsDouble();
                            else if (p.StorageType == StorageType.String && double.TryParse(p.AsString(), out double ov)) overrideMm = ov;
                        }
                    }
                    catch { }
                    double workMm = overrideMm > 0
                        ? overrideMm
                        : ArcFlashEngine.DefaultWorkingDistanceMm(voltageV);
                    double gapMm = ArcFlashEngine.DefaultBusGapMm(voltageV);

                    double clearMs = useFixed
                        ? clearingMs
                        : ResolveClearingTime(panel, fr.FaultKa);

                    double ie = ArcFlashEngine.IncidentEnergy_CalCm2(fr.FaultKa, clearMs, voltageV, workMm, gapMm);
                    double bd = ArcFlashEngine.ArcFlashBoundaryMm(fr.FaultKa, clearMs, voltageV, gapMm);
                    int ppe   = ArcFlashEngine.PpeCategory(ie);
                    string lbl = ArcFlashEngine.FormatLabel(panel.Name, ie, ppe, bd, workMm, voltageV);

                    try
                    {
                        ParameterHelpers.SetString(panel, ParamRegistry.ELC_ARC_FLASH_IE,    $"{ie:0.00}",  overwrite: true);
                        ParameterHelpers.SetString(panel, ParamRegistry.ELC_ARC_FLASH_BD,    $"{bd:0}",     overwrite: true);
                        ParameterHelpers.SetString(panel, ParamRegistry.ELC_ARC_FLASH_PPE,   $"{ppe}",      overwrite: true);
                        ParameterHelpers.SetString(panel, ParamRegistry.ELC_ARC_FLASH_WD,    $"{workMm:0}", overwrite: true);
                        ParameterHelpers.SetString(panel, ParamRegistry.ELC_ARC_FLASH_LABEL, lbl,           overwrite: true);
                    }
                    catch (Exception ex) { StingLog.Warn($"Stamp arc flash on {panel.Name}: {ex.Message}"); }

                    ApplyPpeColorOverride(doc, panel, ppe);
                    results.Add(new ArcFlashRow
                    {
                        PanelName = panel.Name, FaultKa = fr.FaultKa,
                        IncidentEnergy_CalCm2 = ie, BoundaryMm = bd, PpeCategory = ppe,
                        WorkDistMm = workMm, LabelText = lbl
                    });
                }
                tx.Commit();
            }
            LastResults = results;
            try { ComplianceScan.InvalidateCache(); } catch { }

            int dangerous = results.Count(r => r.PpeCategory < 0);
            int cat4 = results.Count(r => r.PpeCategory == 4);
            TaskDialog.Show("STING Arc Flash",
                $"Arc-flash analysis complete — {results.Count} panel(s) assessed.\n" +
                $"⚠ {dangerous} exceed Category 4 (>40 cal/cm²).\n" +
                $"⚠ {cat4} are Category 4.\n\n" +
                "Run 'Arc Flash Labels' to generate the warning-label drafting view.");
            return Result.Succeeded;
        }

        private static double ParseVoltage(string voltageStr, FamilyInstance panel)
        {
            if (!string.IsNullOrEmpty(voltageStr))
            {
                string digits = "";
                foreach (char ch in voltageStr)
                {
                    if (char.IsDigit(ch) || ch == '.') digits += ch;
                    else if (digits.Length > 0) break;
                }
                if (double.TryParse(digits, out double v) && v > 0) return v;
            }
            try
            {
                double native = panel.get_Parameter(BuiltInParameter.RBS_ELEC_VOLTAGE)?.AsDouble() ?? 0;
                if (native > 0) return native;
            }
            catch { }
            return 240.0;
        }

        private static double ResolveClearingTime(FamilyInstance panel, double faultKa)
        {
            try
            {
                string mainBrk = ParameterHelpers.GetString(panel, ParamRegistry.ELC_MAIN_BRK);
                if (!string.IsNullOrEmpty(mainBrk))
                {
                    string label = mainBrk.Trim();
                    if (!label.EndsWith("A", StringComparison.OrdinalIgnoreCase)) label += "A";
                    var tcc = TccDatabaseLoader.Resolve(label, faultKa);
                    if (tcc != null) return tcc.ClearingTimeMs(faultKa);
                }
            }
            catch (Exception ex) { StingLog.Info($"ResolveClearingTime: {ex.Message}"); }
            return 100.0;
        }

        private static void ApplyPpeColorOverride(Document doc, Element el, int ppe)
        {
            try
            {
                var view = doc.ActiveView;
                if (view == null) return;
                var solidFill = ParameterHelpers.GetSolidFillPattern(doc);
                if (solidFill == null) return;
                Color c = ppe switch
                {
                    < 0 => new Color(180, 0, 0),
                    4   => new Color(255, 64, 64),
                    3   => new Color(255, 140, 0),
                    2   => new Color(255, 210, 0),
                    _   => new Color(0, 200, 80)
                };
                var ogs = new OverrideGraphicSettings();
                ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                ogs.SetSurfaceForegroundPatternColor(c);
                view.SetElementOverrides(el.Id, ogs);
            }
            catch (Exception ex) { StingLog.Warn($"ArcFlash colour override: {ex.Message}"); }
        }
    }
}
