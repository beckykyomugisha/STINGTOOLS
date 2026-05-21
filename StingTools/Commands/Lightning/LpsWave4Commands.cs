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
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MiniSoftware;
using StingTools.Core;
using StingTools.Core.Lightning;
using StingTools.Core.Fabrication;
using StingTools.UI;

namespace StingTools.Commands.Lightning
{
    // ════════════════════════════════════════════════════════════════
    //  LpsSldGenerateCommand — full graph-aware LPS SLD rendering
    //  (Wave 4 closure: replaces the annotation-only overlay path)
    // ════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LpsSldGenerateCommand : IExternalCommand, IPanelCommand
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
            if (doc == null) { TaskDialog.Show("STING — LPS SLD", "No active document."); return Result.Cancelled; }
            return RunInternal(app, doc);
        }

        private Result RunInternal(UIApplication app, Document doc)
        {
            LpsSldEngine.BuildResult result;
            try
            {
                using (var t = new Transaction(doc, "STING — Generate LPS SLD"))
                {
                    t.Start();
                    result = LpsSldEngine.Build(doc);
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("LpsSldGenerate", ex);
                TaskDialog.Show("STING — LPS SLD", "Generation failed: " + ex.Message);
                return Result.Failed;
            }

            // Activate the view if we built one
            try
            {
                if (result.View != null && app?.ActiveUIDocument != null)
                    app.ActiveUIDocument.ActiveView = result.View;
            }
            catch (Exception ex) { StingLog.Warn($"Activate view: {ex.Message}"); }

            var rp = StingResultPanel.Create("LPS Single Line Diagram");
            rp.SetSubtitle(result.View != null ? $"View: {result.View.Name}" : "No view created");
            rp.AddSection("COMPONENTS")
              .Metric("Air terminals",     result.AirTerminals.ToString())
              .Metric("Down conductors",   result.DownConductors.ToString())
              .Metric("Earth electrodes",  result.EarthElectrodes.ToString())
              .Metric("Surge protectors",  result.SurgeProtectors.ToString())
              .Metric("Detail lines",      result.LinesDrawn.ToString());
            rp.AddSection("NOTES").Text(result.Notes ?? "");
            rp.Show();
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  LpsSldOverlayCommand — annotate the SLD with LPS markers
    //  (retained as the "decorate-existing-electrical-SLD" path; the
    //  Generate command above creates a standalone LPS-only SLD)
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
    //  LpsSpdSpecSheetCommand — render lps_spd_spec.docx per SPD row
    // ════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LpsSpdSpecSheetCommand : IExternalCommand, IPanelCommand
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
            if (doc == null) { TaskDialog.Show("STING — SPD Spec", "No active document."); return Result.Cancelled; }
            return RunInternal(app, doc);
        }

        private Result RunInternal(UIApplication app, Document doc)
        {
            var panel = StingLpsPanel.Instance;
            if (panel == null || panel.SpdRows.Count == 0)
            {
                TaskDialog.Show("STING — SPD Spec",
                    "No SPD rows in the panel grid. Run 'Recommend' or add rows first.");
                return Result.Cancelled;
            }

            // Resolve the template — pre-extracted by EmbeddedTemplates.ExtractIfMissing
            // into _BIM_COORD/templates/.
            string projDir = Path.GetDirectoryName(doc.PathName) ?? "";
            string templatePath = Path.Combine(projDir, "_BIM_COORD", "templates", "lps_spd_spec.docx");
            if (!File.Exists(templatePath))
            {
                // Fallback to the embedded copy via StingToolsApp.FindDataFile
                // for first-run / unsaved projects.
                templatePath = StingToolsApp.FindDataFile("lps_spd_spec.docx");
                if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
                {
                    TaskDialog.Show("STING — SPD Spec",
                        "lps_spd_spec.docx not found. Re-open the project to trigger template extraction.");
                    return Result.Cancelled;
                }
            }

            string outDir = Path.Combine(projDir, "_BIM_COORD", "generated");
            if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

            var snap = StingLpsCommandHandler.Snapshot();
            string projClass = ParameterHelpers.GetString(doc.ProjectInformation, LpsParams.CLASS_TXT);
            if (string.IsNullOrWhiteSpace(projClass)) projClass = snap.LpsClass;
            double minIimp = SpdCoordinator.GetMinIimpKaForClass(projClass);

            int rendered = 0;
            var failed = new List<string>();
            string lastOut = "";

            foreach (var row in panel.SpdRows)
            {
                try
                {
                    var dict = new Dictionary<string, object>
                    {
                        // Doc identity
                        ["doc.number"]    = $"SPD-{(row.Tag ?? "UNK").Replace(' ', '_')}",
                        ["doc.revision"]  = "P01",
                        ["doc.date"]      = DateTime.Today.ToString("yyyy-MM-dd"),
                        ["doc.status"]    = "WIP",

                        // Project
                        ["project.code"]        = ParameterHelpers.GetString(doc.ProjectInformation, "PRJ_ORG_PROJECT_CODE_TXT"),
                        ["project.name"]        = doc.ProjectInformation?.Name ?? "",
                        ["project.client"]      = ParameterHelpers.GetString(doc.ProjectInformation, "PRJ_ORG_CLIENT_NAME_TXT"),
                        ["project.originator"]  = ParameterHelpers.GetString(doc.ProjectInformation, "PRJ_ORG_ORIGINATOR_CODE_TXT"),
                        ["project.lps_class"]   = projClass ?? "—",
                        ["project.min_iimp_ka"] = minIimp.ToString("F1"),
                        ["project.uw_kv"]       = snap.EquipmentWithstandKv.ToString("F2"),

                        // SPD identity
                        ["spd.tag"]              = row.Tag ?? "",
                        ["spd.location_id"]      = row.LocationId ?? "",
                        ["spd.location_label"]   = row.LocationLabel ?? "",
                        ["spd.manufacturer"]     = row.Manufacturer ?? "",
                        ["spd.model"]            = row.Model ?? "",
                        ["spd.datasheet"]        = "",

                        // Electrical performance
                        ["spd.type"]             = row.Type.ToString(),
                        ["spd.iimp_ka"]          = row.IimpKa.ToString("F1"),
                        ["spd.in_ka"]            = row.InKa.ToString("F1"),
                        ["spd.up_kv"]            = row.UpKv.ToString("F2"),
                        ["spd.uc_v"]             = "275",        // typical TN-S 230/400 V
                        ["spd.poles"]            = "TN-S",
                        ["spd.response_ns"]      = "≤ 25",

                        // Installation
                        ["spd.lpz_boundary"]         = row.LocationId ?? "",
                        ["spd.cable_separation_m"]   = row.CableSeparationM.ToString("F1"),
                        ["spd.manufacturer_paired"]  = "No",
                        ["spd.mounting"]             = "DIN rail",

                        // Verdict — derived from the status dot
                        ["spd.verdict"]          = string.Equals(row.StatusDot, "✓", StringComparison.Ordinal) ? "PASS"
                                                 : string.Equals(row.StatusDot, "⚠", StringComparison.Ordinal) ? "WARN"
                                                 : string.Equals(row.StatusDot, "✗", StringComparison.Ordinal) ? "FAIL"
                                                 : "PENDING",

                        ["spd.notes"]            = "",

                        // Sign-off (defaults)
                        ["signoff.prepared_name"] = ParameterHelpers.GetString(doc.ProjectInformation, "PRJ_ORG_COMPANY_NAME_TXT"),
                        ["signoff.prepared_role"] = "LV Design Engineer",
                        ["signoff.prepared_date"] = DateTime.Today.ToString("yyyy-MM-dd"),
                        ["signoff.checked_name"]  = "",
                        ["signoff.checked_role"]  = "Lead Engineer",
                        ["signoff.checked_date"]  = "",
                        ["signoff.approved_name"] = "",
                        ["signoff.approved_role"] = "Discipline Lead",
                        ["signoff.approved_date"] = ""
                    };

                    string safeTag = (row.Tag ?? "UNK").Replace(' ', '_').Replace('/', '_');
                    string outPath = Path.Combine(outDir,
                        $"{DateTime.Now:yyyyMMdd_HHmmss}_LPS_SPD_{safeTag}.docx");

                    MiniWord.SaveAsByTemplate(outPath, templatePath, dict);
                    rendered++;
                    lastOut = outPath;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"SpdSpec render '{row.Tag}': {ex.Message}");
                    failed.Add($"  • {row.Tag}: {ex.Message}");
                }
            }

            string msg = $"Rendered {rendered}/{panel.SpdRows.Count} SPD spec sheet(s) to:\n{outDir}";
            if (rendered > 0) msg += $"\n\nLast file: {Path.GetFileName(lastOut)}";
            if (failed.Count > 0) msg += $"\n\nFailures:\n" + string.Join("\n", failed.Take(5));
            TaskDialog.Show("STING — SPD Spec", msg);
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
