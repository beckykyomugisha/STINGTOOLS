// StingTools — LpsPanelCommands.cs
//
// Three net-new commands wired into the STING Lightning Protection inline
// panel: inline risk assessment (reads RISK-tab inputs from the panel
// instance, not a modal wizard), SPD coordination check (reads SPD-tab
// grid + header class against IEC 62305-4 rules), SPD product recommend
// (auto-fills empty SPD slots from the catalogue), SPD BOM export.
//
// Each command is a thin orchestration layer over LpsEngine / SpdCoordinator.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Lightning;
using StingTools.UI;

namespace StingTools.Commands.Lightning
{
    // ════════════════════════════════════════════════════════════════
    //  Inline Risk Assessment (reads from panel grid + header)
    // ════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LpsRiskAssessmentInlineCommand : IExternalCommand, IPanelCommand
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
            if (doc == null) { TaskDialog.Show("STING — LPS Risk", "No active document."); return Result.Cancelled; }
            return RunInternal(app, doc);
        }

        private Result RunInternal(UIApplication app, Document doc)
        {
            var panel = StingLpsPanel.Instance;
            if (panel == null)
            {
                TaskDialog.Show("STING — LPS Risk",
                    "Open the STING LPS panel first (RISK tab) to set inputs.");
                return Result.Cancelled;
            }

            var snap = StingLpsCommandHandler.Snapshot();

            // Build LpsRiskInput from the panel's RISK-tab fields.
            // Wave A #5 — feed AeOverrideM2 so non-rectangular buildings
            // bypass the Annex A.2 formula when the user has computed Ae
            // externally.
            var input = new LpsRiskInput
            {
                RegionId          = snap.Region,
                GroundFlashDensity = panel.RiskNgFlashDensity > 0 ? panel.RiskNgFlashDensity
                                                                   : LpsEngine.GetEffectiveFlashDensity(doc, snap.Region),
                PlanLengthM       = panel.RiskPlanLengthM,
                PlanWidthM        = panel.RiskPlanWidthM,
                HeightM           = panel.RiskHeightM,
                AeOverrideM2      = panel.RiskAeOverrideM2,
                BuildingTypeCb    = panel.RiskBuildingTypeCb,
                InternalContentCc = panel.RiskInternalContentCc,
                OccupantHazardCd  = panel.RiskOccupantHazardCd,
                ConsequenceCe     = panel.RiskConsequenceCe,
                LocationFactorCd  = panel.RiskLocationFactorCd,
                TolerableRisk     = panel.RiskTolerableRisk
            };

            var result = LpsEngine.RunRiskAssessment(input);

            // Stamp results into the panel for live display
            panel.SetRiskResult(result);

            // Build a result panel mirroring HVAC's StingResultPanel pattern.
            var rp = StingResultPanel.Create("LPS Risk Assessment (BS EN 62305-2)");
            rp.SetSubtitle(
                $"Region: {snap.Region}  •  Class header: {snap.LpsClass}  •  Recommended: {result.RecommendedClass ?? "—"}");

            var s = rp.AddSection("RISK COMPONENTS");
            s.Metric("Collection area Ae", $"{result.CollectionAreaM2:F0} m²");
            s.Metric("Annual strikes Nd",  $"{result.AnnualStrikeFrequency:F4} /yr");
            foreach (var kv in result.RiskComponents)
                s.Metric(kv.Key, kv.Value.ToString("E2"));
            s.Metric("Tolerable risk Rt",  result.TolerableRisk.ToString("E2"));
            if (result.RequiresLps)
                s.MetricError("LPS required?", "YES");
            else
                s.MetricHighlight("LPS required?", "NO");

            if (result.ResidualRiskByClass != null && result.ResidualRiskByClass.Count > 0)
            {
                var rs = rp.AddSection("RESIDUAL RISK BY CLASS");
                foreach (var kv in result.ResidualRiskByClass.OrderBy(k => k.Key))
                {
                    bool ok = kv.Value <= result.TolerableRisk;
                    if (ok) rs.MetricHighlight($"Class {kv.Key}", kv.Value.ToString("E2"));
                    else    rs.MetricWarn($"Class {kv.Key}", kv.Value.ToString("E2"));
                }
            }

            rp.AddSection("NOTES").Text(result.Notes ?? "");

            // Optional: stamp the recommended class onto ProjectInformation
            // (only when the panel's auto-apply checkbox is set, set by SetRiskResult).
            if (panel.RiskAutoApplyClass && !string.IsNullOrWhiteSpace(result.RecommendedClass) &&
                result.RecommendedClass != "NONE")
            {
                try
                {
                    using (var t = new Transaction(doc, "STING — Stamp Recommended LPS Class"))
                    {
                        t.Start();
                        ParameterHelpers.SetString(doc.ProjectInformation,
                            StingTools.Core.Fabrication.LpsParams.CLASS_TXT,
                            result.RecommendedClass, true);
                        t.Commit();
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Stamp class: {ex.Message}"); }
            }

            rp.Show();
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  SPD Coordination Check
    // ════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SpdCoordinationCheckCommand : IExternalCommand, IPanelCommand
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
            if (doc == null) { TaskDialog.Show("STING — SPD Coordination", "No active document."); return Result.Cancelled; }
            return RunInternal(app, doc);
        }

        private Result RunInternal(UIApplication app, Document doc)
        {
            var panel = StingLpsPanel.Instance;
            if (panel == null)
            {
                TaskDialog.Show("STING — SPD Coordination",
                    "Open the STING LPS panel first (SPD tab).");
                return Result.Cancelled;
            }

            var snap = StingLpsCommandHandler.Snapshot();
            var installed = panel.SpdRows.Select(r => new SpdInstance
            {
                Tag          = r.Tag,
                LocationId   = r.LocationId,
                Type         = r.Type,
                IimpKa       = r.IimpKa,
                InKa         = r.InKa,
                UpKv         = r.UpKv,
                Manufacturer = r.Manufacturer,
                Model        = r.Model,
                CableSeparationFromUpstreamM = r.CableSeparationM
            }).ToList();

            var items = SpdCoordinator.Coordinate(installed, snap.LpsClass, snap.EquipmentWithstandKv);

            // Push status dots back into the SPD grid
            try { panel.UpdateSpdStatus(items); }
            catch (Exception ex) { StingLog.Warn($"UpdateSpdStatus: {ex.Message}"); }

            int pass = items.Count(i => i.Severity == LpsSeverity.Pass);
            int warn = items.Count(i => i.Severity == LpsSeverity.Warn);
            int fail = items.Count(i => i.Severity == LpsSeverity.Fail);

            var rp = StingResultPanel.Create("SPD Coordination (IEC 62305-4 / IEC 61643-11)");
            rp.SetSubtitle($"Class {snap.LpsClass}  •  Uw = {snap.EquipmentWithstandKv:F2} kV  •  {installed.Count} SPD(s) installed");
            rp.AddSection("SUMMARY")
              .Metric("Rules evaluated", items.Count.ToString())
              .MetricHighlight("Pass", pass.ToString())
              .MetricWarn("Warn", warn.ToString())
              .MetricError("Fail", fail.ToString());

            if (fail > 0)
            {
                var sec = rp.AddSection("FAIL");
                foreach (var i in items.Where(x => x.Severity == LpsSeverity.Fail))
                    sec.Text($"  ▸ [{i.CheckName}] {i.Message}");
            }
            if (warn > 0)
            {
                var sec = rp.AddSection("WARN");
                foreach (var i in items.Where(x => x.Severity == LpsSeverity.Warn))
                    sec.Text($"  ▸ [{i.CheckName}] {i.Message}");
            }
            var sec2 = rp.AddSection("PASS");
            foreach (var i in items.Where(x => x.Severity == LpsSeverity.Pass))
                sec2.Text($"  ✓ [{i.CheckName}] {i.Message}");

            rp.Show();
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  SPD Recommend (fills empty locations from the catalogue)
    // ════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SpdRecommendCommand : IExternalCommand, IPanelCommand
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
            if (doc == null) { TaskDialog.Show("STING — SPD Recommend", "No active document."); return Result.Cancelled; }
            return RunInternal(app, doc);
        }

        private Result RunInternal(UIApplication app, Document doc)
        {
            var panel = StingLpsPanel.Instance;
            if (panel == null)
            {
                TaskDialog.Show("STING — SPD Recommend",
                    "Open the STING LPS panel first (SPD tab).");
                return Result.Cancelled;
            }

            var snap = StingLpsCommandHandler.Snapshot();
            int added = 0;

            foreach (var loc in SpdCoordinator.AllLocations(doc))
            {
                bool present = panel.SpdRows.Any(r =>
                    string.Equals(r.LocationId, loc.Id, StringComparison.OrdinalIgnoreCase));
                if (present) continue;

                var p = SpdCoordinator.Recommend(loc.Id, snap.LpsClass, doc);
                if (p == null) continue;

                panel.AddSpdRow(new SpdRowVm
                {
                    Tag          = $"SPD-{loc.Id}-1",
                    LocationId   = loc.Id,
                    LocationLabel = loc.Label,
                    Type         = p.Type,
                    IimpKa       = p.IimpKa,
                    InKa         = p.InKa,
                    UpKv         = p.UpKv,
                    Manufacturer = p.Manufacturer,
                    Model        = p.Model,
                    StatusDot    = "•"
                });
                added++;
            }

            TaskDialog.Show("STING — SPD Recommend",
                $"Added {added} recommended SPD(s) to the grid.\n\n" +
                "Adjust manufacturer / model / Uw to suit your project, then click 'Coordinate'.");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Load From Model — walks the doc once and populates every grid
    //  on the panel (AT / DC / earth / bonding / zones / inspection /
    //  SPD). Closes the "grids seeded empty" caveat from Phase 1.
    // ════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LpsLoadFromModelCommand : IExternalCommand, IPanelCommand
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
            if (doc == null) { TaskDialog.Show("STING — LPS Load Model", "No active document."); return Result.Cancelled; }
            return RunInternal(app, doc);
        }

        private Result RunInternal(UIApplication app, Document doc)
        {
            var panel = StingLpsPanel.Instance;
            if (panel == null)
            {
                TaskDialog.Show("STING — LPS Load Model",
                    "Open the STING LPS panel first.");
                return Result.Cancelled;
            }
            panel.LoadAllFromDoc(doc);
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  SPD Save Override — write the current SPD grid to
    //  <project>/_BIM_COORD/lps_spd_catalogue.json. Reloaded on the
    //  next coordinate / recommend run.
    // ════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SpdSaveOverrideCommand : IExternalCommand, IPanelCommand
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
            if (doc == null) { TaskDialog.Show("STING — SPD Override", "No active document."); return Result.Cancelled; }
            return RunInternal(app, doc);
        }

        private Result RunInternal(UIApplication app, Document doc)
        {
            var panel = StingLpsPanel.Instance;
            if (panel == null || panel.SpdRows.Count == 0)
            {
                TaskDialog.Show("STING — SPD Override", "No SPD rows in the panel grid.");
                return Result.Cancelled;
            }
            if (string.IsNullOrEmpty(doc.PathName))
            {
                TaskDialog.Show("STING — SPD Override",
                    "Save the Revit project first — the override path is derived from the .rvt location.");
                return Result.Cancelled;
            }

            var rows = panel.SpdRows.Select(r => new SpdInstance
            {
                Tag          = r.Tag,
                LocationId   = r.LocationId,
                Type         = r.Type,
                IimpKa       = r.IimpKa,
                InKa         = r.InKa,
                UpKv         = r.UpKv,
                Manufacturer = r.Manufacturer,
                Model        = r.Model,
                CableSeparationFromUpstreamM = r.CableSeparationM
            }).ToList();

            string written = SpdCoordinator.SaveProjectOverride(doc, rows);
            if (string.IsNullOrEmpty(written))
            {
                TaskDialog.Show("STING — SPD Override", "Save failed — see STING log.");
                return Result.Failed;
            }
            TaskDialog.Show("STING — SPD Override",
                $"SPD project override written to:\n{written}\n\n" +
                $"{rows.Count} product(s) saved. Future Recommend / Coordinate runs will layer this over the corporate baseline.");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  SPD BOM Export — writes the grid to CSV
    // ════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SpdExportBomCommand : IExternalCommand, IPanelCommand
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
            if (doc == null) { TaskDialog.Show("STING — SPD BOM", "No active document."); return Result.Cancelled; }
            return RunInternal(app, doc);
        }

        private Result RunInternal(UIApplication app, Document doc)
        {
            var panel = StingLpsPanel.Instance;
            if (panel == null || panel.SpdRows.Count == 0)
            {
                TaskDialog.Show("STING — SPD BOM", "No SPD rows in the panel grid.");
                return Result.Cancelled;
            }

            string outPath = OutputLocationHelper.GetTimestampedPath(doc, "STING_LPS_SPD_BOM", ".csv");
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Tag,Location,Type,Iimp(kA),In(kA),Up(kV),Manufacturer,Model,CableSep(m),Status");
                foreach (var r in panel.SpdRows)
                {
                    sb.AppendLine(string.Join(",", new[]
                    {
                        Csv(r.Tag), Csv(r.LocationLabel), r.Type.ToString(),
                        r.IimpKa.ToString("F1"), r.InKa.ToString("F1"), r.UpKv.ToString("F2"),
                        Csv(r.Manufacturer), Csv(r.Model),
                        r.CableSeparationM.ToString("F1"), Csv(r.StatusDot)
                    }));
                }
                File.WriteAllText(outPath, sb.ToString());
                TaskDialog.Show("STING — SPD BOM", $"SPD BOM written to:\n{outPath}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SpdExportBom failed", ex);
                TaskDialog.Show("STING — SPD BOM", "Export failed: " + ex.Message);
                return Result.Failed;
            }
        }

        private static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
