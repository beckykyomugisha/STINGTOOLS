using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Electrical;

namespace StingTools.Commands.Electrical.Busbar
{
    /// <summary>
    /// Sizes busbar trunking elements modelled as cable trays whose name
    /// contains "Busbar" or "Trunking". Demand is read from the cable
    /// manifest (cables routed through the tray) or, when the tray hasn't
    /// been associated with cables yet, parsed from a numeric prefix in
    /// the tray's name (e.g. "Busbar 400A"). Stamps
    /// ELC_BUSBAR_CSA_MM2 / RATING_A / FILL_PCT and red-overrides any
    /// element above 80 % fill.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BusbarModelingCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var trays = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_CableTray)
                .WhereElementIsNotElementType()
                .OfType<CableTray>()
                .Where(t => MatchesBusbarName(t))
                .ToList();
            if (trays.Count == 0)
            {
                TaskDialog.Show("STING Busbar",
                    "No cable-tray elements with 'Busbar' or 'Trunking' in the name were found.\n\n" +
                    "Model busbar trunking as a cable tray named e.g. 'Busbar 400A' and STING will size it.");
                return Result.Cancelled;
            }

            CableManifest manifest;
            try { manifest = CableManifest.Load(doc); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); manifest = null; }

            int sized = 0;
            using (var tx = new Transaction(doc, "STING Busbar Sizing"))
            {
                tx.Start();
                foreach (var tray in trays)
                {
                    try
                    {
                        double demandA = ResolveDemandA(tray, manifest, doc);
                        if (demandA < 1) continue;

                        double widthMm  = (tray.Width  > 0 ? tray.Width  : 0) * 304.8;
                        double heightMm = (tray.Height > 0 ? tray.Height : 0) * 304.8;
                        double ductAreaMm2 = widthMm * heightMm;

                        var result = BusbarSizerEngine.Size(demandA);
                        double fillPct = ductAreaMm2 > 0
                            ? BusbarSizerEngine.FillPercent(ductAreaMm2, result.CsaMm2, 3)
                            : 0;

                        try { ParameterHelpers.SetString(tray, ParamRegistry.ELC_BUSBAR_CSA, $"{result.CsaMm2:0}", overwrite: true); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                        try { ParameterHelpers.SetString(tray, ParamRegistry.ELC_BUSBAR_RATING, $"{result.RatingA:0}", overwrite: true); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                        try { ParameterHelpers.SetString(tray, ParamRegistry.ELC_BUSBAR_FILL, $"{fillPct:0.0}", overwrite: true); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

                        if (fillPct > 80) ApplyRedOverride(doc, tray);
                        if (!result.Compliant) StingLog.Warn($"Busbar {tray.Name}: {result.Warning}");
                        sized++;
                    }
                    catch (Exception ex) { StingLog.Warn($"BusbarModeling {tray.Name}: {ex.Message}"); }
                }
                tx.Commit();
            }
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            TaskDialog.Show("STING Busbar Sizing",
                $"Busbar trunking sizing complete — {sized} run(s) assessed.\n" +
                "Check ELC_BUSBAR_FILL_PCT for fill compliance — runs > 80 % are highlighted red.");
            return Result.Succeeded;
        }

        private static bool MatchesBusbarName(CableTray t)
        {
            string n = ((t?.Name ?? "") + " " + (t?.GetTypeId() != null
                ? (t.Document.GetElement(t.GetTypeId())?.Name ?? "") : "")).ToLowerInvariant();
            return n.Contains("busbar") || n.Contains("trunking");
        }

        private static double ResolveDemandA(CableTray tray, CableManifest manifest, Document doc)
        {
            // 1. Sum demand of cables routed through this tray.
            if (manifest?.Cables != null)
            {
                double totalKW = 0;
                foreach (var cable in manifest.Cables.Where(c =>
                    c.RouteTrayIds != null && c.RouteTrayIds.Contains(tray.Id.Value)))
                {
                    try
                    {
                        var sys = new FilteredElementCollector(doc)
                            .OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>()
                            .FirstOrDefault(s => string.Equals(
                                s.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER)?.AsString() ?? "",
                                cable.CircuitId, StringComparison.OrdinalIgnoreCase));
                        if (sys != null)
                            totalKW += (sys.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD)?.AsDouble() ?? 0) / 1000.0;
                    }
                    catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                }
                if (totalKW > 0)
                    return totalKW * 1000 / (400.0 * Math.Sqrt(3) * 0.9);
            }
            // 2. Fall back to parsing "Busbar 400A" pattern from the tray / type name.
            string blob = (tray.Name ?? "") + " " + (tray.GetTypeId() != null
                ? (tray.Document.GetElement(tray.GetTypeId())?.Name ?? "") : "");
            var m = Regex.Match(blob, @"(\d+)\s*A", RegexOptions.IgnoreCase);
            if (m.Success && double.TryParse(m.Groups[1].Value, out double a)) return a;
            return 0;
        }

        private static void ApplyRedOverride(Document doc, Element el)
        {
            try
            {
                var view = doc.ActiveView;
                if (view == null) return;
                var solidFill = ParameterHelpers.GetSolidFillPattern(doc);
                var ogs = new OverrideGraphicSettings();
                ogs.SetProjectionLineColor(new Color(244, 67, 54));
                ogs.SetProjectionLineWeight(6);
                if (solidFill != null)
                {
                    ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                    ogs.SetSurfaceForegroundPatternColor(new Color(244, 67, 54));
                }
                view.SetElementOverrides(el.Id, ogs);
            }
            catch (Exception ex) { StingLog.Warn($"Busbar red override: {ex.Message}"); }
        }
    }
}
