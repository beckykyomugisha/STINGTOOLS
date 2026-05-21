// StingTools — LpsWave4Commands.cs
//
// Two minimal opportunistic-scope commands closing Wave 4 of the LPS
// review backlog:
//
//   • LpsSldOverlayCommand — annotates the active electrical single-line
//     diagram view (or the next-most-recently-active SLD-named view)
//     with LPS markers: ★ at each SPD location and ⏚ at the main earth
//     bar. Pure annotation — doesn't modify the SLD topology.
//
//   • LpsCarbonReportCommand — totals embodied carbon for LPS copper
//     conductors using the same kg-CO2-eq/kg factor SustainabilityEngine
//     uses (3.0 kg-CO2-eq/kg for primary copper rod / strip per ICE v3).
//     Reports per-element + project totals via StingResultPanel.
//
// Both are deliberately light — Wave 4 is "opportunistic" scope and
// these lay the foundation for fuller integrations (a real SLD engine
// for LPS would need clash-aware routing; a real carbon model would
// need conductor cross-section × length × material density resolved
// per-element). They demonstrate the wiring and give the user a
// working artefact today.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Lightning;
using StingTools.Core.Fabrication;
using StingTools.UI;

namespace StingTools.Commands.Lightning
{
    // ════════════════════════════════════════════════════════════════
    //  LpsSldOverlayCommand — annotate the SLD with LPS markers
    // ════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LpsSldOverlayCommand : IExternalCommand, IPanelCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            return RunInternal(ctx.App, ctx.Doc);
        }

        public Result Execute(UIApplication app)
        {
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { TaskDialog.Show("STING — LPS SLD Overlay", "No active document."); return Result.Cancelled; }
            return RunInternal(app, doc);
        }

        private Result RunInternal(UIApplication app, Document doc)
        {
            // Find an SLD-named drafting view. The existing
            // SLDRiserDiagramCommand creates views with "SLD" / "Single
            // Line" / "Riser" in the name.
            var sld = new FilteredElementCollector(doc).OfClass(typeof(ViewDrafting))
                .Cast<ViewDrafting>()
                .FirstOrDefault(v =>
                    v.Name.IndexOf("SLD",         StringComparison.OrdinalIgnoreCase) >= 0 ||
                    v.Name.IndexOf("Single Line", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    v.Name.IndexOf("Riser",       StringComparison.OrdinalIgnoreCase) >= 0);
            if (sld == null)
            {
                TaskDialog.Show("STING — LPS SLD Overlay",
                    "No SLD view found. Run SLD_RiserDiagram first to create one, then re-run this overlay.");
                return Result.Cancelled;
            }

            // Collect LPS SPDs + earth bars from the model
            var spds   = LpsEngine.CollectLpsFamily(doc, "SPD", "Surge");
            var earths = LpsEngine.CollectLpsFamily(doc, "Earth", "Ground Rod", "GroundRod");
            if (spds.Count + earths.Count == 0)
            {
                TaskDialog.Show("STING — LPS SLD Overlay", "No SPDs or earth electrodes found.");
                return Result.Cancelled;
            }

            var textType = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>().FirstOrDefault();
            if (textType == null)
            {
                TaskDialog.Show("STING — LPS SLD Overlay", "No TextNoteType in document.");
                return Result.Cancelled;
            }

            int placed = 0;
            try
            {
                using (var t = new Transaction(doc, "STING — LPS SLD Overlay"))
                {
                    t.Start();

                    // Sweep right along a band at Y = 0 for SPDs, and
                    // a band at Y = -10ft for earths. Caller can drag
                    // each label after if topology placement is needed.
                    double xSpd = 0;
                    foreach (var spd in spds)
                    {
                        string tag = ParameterHelpers.GetString(spd, LpsParams.SPD_TAG_TXT);
                        if (string.IsNullOrEmpty(tag)) tag = ParameterHelpers.GetString(spd, ParamRegistry.SEQ);
                        if (string.IsNullOrEmpty(tag)) tag = "SPD";
                        try
                        {
                            TextNote.Create(doc, sld.Id, new XYZ(xSpd, 0, 0),
                                $"★ {tag}", textType.Id);
                            placed++;
                            xSpd += 1.5; // 1.5 ft band spacing
                        }
                        catch (Exception ex) { StingLog.Warn($"SLD SPD note: {ex.Message}"); }
                    }
                    double xEarth = 0;
                    foreach (var e in earths)
                    {
                        try
                        {
                            TextNote.Create(doc, sld.Id, new XYZ(xEarth, -10.0, 0),
                                "⏚ EARTH", textType.Id);
                            placed++;
                            xEarth += 1.5;
                        }
                        catch (Exception ex) { StingLog.Warn($"SLD earth note: {ex.Message}"); }
                    }
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("LpsSldOverlay", ex);
                TaskDialog.Show("STING — LPS SLD Overlay", "Overlay failed: " + ex.Message);
                return Result.Failed;
            }

            TaskDialog.Show("STING — LPS SLD Overlay",
                $"Added {placed} LPS marker(s) to '{sld.Name}'.\n" +
                $"  ★ SPD count: {spds.Count}\n" +
                $"  ⏚ Earth count: {earths.Count}\n\n" +
                "Drag the markers to position on the SLD topology. Wave 4 stub — full LPS-aware SLD routing is a future phase.");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  LpsCarbonReportCommand — embodied carbon for LPS conductors
    // ════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LpsCarbonReportCommand : IExternalCommand, IPanelCommand
    {
        // ICE Database v3.0 default factors (kg-CO2-eq / kg).
        // Copper primary (rod / strip): 3.0
        // Aluminium primary:            8.24
        // Steel galvanised:             1.46
        // Stainless steel:              6.15
        private static readonly Dictionary<string, double> CarbonFactorKgCo2PerKg
            = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["COPPER"]    = 3.0,
            ["ALUMINIUM"] = 8.24,
            ["STEEL"]     = 1.46,
            ["STAINLESS"] = 6.15
        };

        // Material densities in kg / m³ for cross-sectional → mass.
        private static readonly Dictionary<string, double> DensityKgPerM3
            = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["COPPER"]    = 8960.0,
            ["ALUMINIUM"] = 2700.0,
            ["STEEL"]     = 7850.0,
            ["STAINLESS"] = 7850.0
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            return RunInternal(ctx.App, ctx.Doc);
        }

        public Result Execute(UIApplication app)
        {
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { TaskDialog.Show("STING — LPS Carbon", "No active document."); return Result.Cancelled; }
            return RunInternal(app, doc);
        }

        private Result RunInternal(UIApplication app, Document doc)
        {
            var conductors = LpsEngine.CollectLpsFamily(doc, "Down Conductor", "Down_Conductor", "DownConductor");
            if (conductors.Count == 0)
            {
                TaskDialog.Show("STING — LPS Carbon",
                    "No down conductors found in the model. Place LPS down-conductor families first.");
                return Result.Cancelled;
            }

            double totalMassKg   = 0;
            double totalCo2Kg    = 0;
            int unset = 0;
            var rows = new List<string[]>();

            foreach (var dc in conductors)
            {
                double lengthM = LpsEngine.GetConductorLengthM(dc);
                string mat = ParameterHelpers.GetString(dc, LpsParams.CONDUCTOR_MATERIAL_TXT);
                if (string.IsNullOrWhiteSpace(mat)) mat = "COPPER";
                double crossSectMm2 = LpsEngine.GetDoubleParam(dc, LpsParams.CONDUCTOR_CROSS_SECT_MM2);
                if (crossSectMm2 <= 0)
                {
                    unset++;
                    // BS EN 62305-3 Table 6 class II default: 50 mm² Cu / 70 mm² Al
                    crossSectMm2 = string.Equals(mat, "ALUMINIUM", StringComparison.OrdinalIgnoreCase) ? 70 : 50;
                }

                double crossSectM2 = crossSectMm2 * 1e-6;
                double volumeM3 = lengthM * crossSectM2;
                double density = DensityKgPerM3.TryGetValue(mat, out double d) ? d : 8960.0;
                double massKg = volumeM3 * density;
                double factor = CarbonFactorKgCo2PerKg.TryGetValue(mat, out double f) ? f : 3.0;
                double co2Kg = massKg * factor;

                totalMassKg += massKg;
                totalCo2Kg  += co2Kg;

                rows.Add(new[]
                {
                    dc.Id.Value.ToString(),
                    mat,
                    $"{lengthM:F2}",
                    $"{crossSectMm2:F0}",
                    $"{massKg:F1}",
                    $"{co2Kg:F2}"
                });
            }

            var rp = StingResultPanel.Create("LPS Embodied Carbon (ICE Database v3.0)");
            rp.SetSubtitle($"{conductors.Count} down conductor(s) — copper density 8960 kg/m³ × 3.0 kg-CO₂-eq/kg");
            rp.AddSection("TOTALS")
              .Metric("Conductor count", conductors.Count.ToString())
              .Metric("Total mass",      $"{totalMassKg:F1} kg")
              .Metric("Total CO₂-eq",    $"{totalCo2Kg:F1} kg")
              .Metric("Avg per conductor", conductors.Count > 0 ? $"{totalCo2Kg / conductors.Count:F2} kg-CO₂-eq" : "—");
            if (unset > 0)
                rp.AddSection("WARNINGS")
                  .Text($"⚠ {unset} conductor(s) had no ELC_LPS_CONDUCTOR_CROSS_SECT_MM2 — defaulted to class II minimum (50 mm² Cu / 70 mm² Al).");
            rp.AddSection("DETAIL")
              .Table(new[] { "ElemId", "Material", "Length (m)", "CS (mm²)", "Mass (kg)", "CO₂-eq (kg)" },
                     rows.Take(50).ToList());
            if (rows.Count > 50)
                rp.Text($"(+{rows.Count - 50} more — see CSV via Wave 4 follow-up)");
            rp.Show();
            return Result.Succeeded;
        }
    }
}
