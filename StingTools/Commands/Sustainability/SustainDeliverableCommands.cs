// StingTools — Sustainability deliverable + per-option comparison (WS I12).
//
//   Sustain_GenerateDeliverable  EDGE/LEED summary as a drawing-set artefact (a
//                                drafting view on a sheet) + a BEP deliverable note.
//   Sustain_CompareOptions       runs the carbon comparison per Revit Design Option
//                                (reusing OptionCostCarbonCalculator) so users can
//                                pick the greenest option.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.DesignOptions;
using StingTools.Core.Sustainability;
using StingTools.UI;

namespace StingTools.Commands.Sustainability
{
    // ── Sustain_GenerateDeliverable ──────────────────────────────────────────
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SustainGenerateDeliverableCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var doc = SustainCmdHelper.Doc(cmd);
            if (doc == null) { TaskDialog.Show("STING Sustainability", "No document open."); return Result.Failed; }

            var setup = SustainCmdHelper.EffectiveSetup(doc);
            var res = SustainabilityEngine.Run(doc, setup);
            var edge = res.Schemes.FirstOrDefault(s => s.SchemeId == "EDGE");

            var lines = new List<string>
            {
                "STING SUSTAINABILITY SUMMARY (EDGE / LEED — indicative)",
                "",
                $"Building use:   {(res.ResolvedUse?.Found == true ? res.ResolvedUse.Use : "not set")}",
                $"Climate zone:   {(string.IsNullOrWhiteSpace(setup.ClimateZone) ? res.Baseline?.MatchedKey : setup.ClimateZone)}",
                $"Grid factor:    {res.GridCarbon?.Factor:0.00} kgCO2e/kWh ({res.GridCarbon?.Source})",
                "",
                $"Energy EUI:     {res.Energy?.DesignEuiKwhM2Yr:0.0} kWh/m2.yr (baseline {res.Energy?.BaselineEuiKwhM2Yr:0.0})",
                $"Water:          {res.Water?.DesignLPersonDay:0.0} L/person.day",
                $"Embodied carbon:{res.Materials?.CarbonIntensityKgM2:0.0} kgCO2e/m2  (coverage {res.Materials?.CoverageSummary})",
                $"Whole-life carbon: {res.WholeLife?.WholeLifeKgM2:0} kgCO2e/m2 over {res.WholeLife?.StudyPeriodYears} yr",
                "",
                $"EDGE level:     {edge?.AchievedLevel ?? "None"}",
                res.Readiness != null && !res.Readiness.Ready
                    ? "*** " + res.Readiness.Banner + " ***"
                    : "Indicative figures — EDGE app owns the certified number.",
            };
            string summary = string.Join("\n", lines);

            string sheetInfo = "drafting view";
            try
            {
                using (var t = new Transaction(doc, "STING Sustainability — Deliverable"))
                {
                    t.Start();
                    var draft = CreateDraftingView(doc, "STING - EDGE-LEED Summary");
                    if (draft != null)
                    {
                        var tnType = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
                        if (tnType != ElementId.InvalidElementId)
                            TextNote.Create(doc, draft.Id, new XYZ(0, 0, 0), summary, tnType);

                        var sheet = CreateSheet(doc, "EDGE / LEED Sustainability Summary");
                        if (sheet != null)
                        {
                            try { Viewport.Create(doc, sheet.Id, draft.Id, new XYZ(0.5, 0.4, 0)); } catch { }
                            sheetInfo = $"sheet {sheet.SheetNumber} — {sheet.Name}";
                        }
                    }
                    t.Commit();
                }
            }
            catch (Exception ex) { StingLog.Warn($"Sustain deliverable sheet: {ex.Message}"); }

            int bep = FeedBep(doc, res, edge, summary);

            StingLog.Info($"Sustain_GenerateDeliverable: {sheetInfo}, BEP note {(bep > 0 ? "written" : "skipped")}.");
            TaskDialog.Show("STING Sustainability",
                $"EDGE/LEED summary deliverable created ({sheetInfo}).\n" +
                (bep > 0 ? "A deliverable note was added to the BEP register." : "") +
                "\n\nFigures are indicative — the EDGE app owns the certified number.");
            return Result.Succeeded;
        }

        private static ViewDrafting CreateDraftingView(Document doc, string name)
        {
            try
            {
                var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>().FirstOrDefault(v => v.ViewFamily == ViewFamily.Drafting);
                if (vft == null) return null;
                var v = ViewDrafting.Create(doc, vft.Id);
                try { v.Name = UniqueViewName(doc, name); } catch { }
                return v;
            }
            catch (Exception ex) { StingLog.Warn($"Sustain CreateDraftingView: {ex.Message}"); return null; }
        }

        private static string UniqueViewName(Document doc, string baseName)
        {
            var existing = new FilteredElementCollector(doc).OfClass(typeof(View))
                .Cast<View>().Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!existing.Contains(baseName)) return baseName;
            for (int i = 2; i < 100; i++) if (!existing.Contains($"{baseName} ({i})")) return $"{baseName} ({i})";
            return $"{baseName} {Guid.NewGuid():N}".Substring(0, baseName.Length + 6);
        }

        private static ViewSheet CreateSheet(Document doc, string name)
        {
            try
            {
                var tb = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsElementType().FirstElementId();
                var sheet = ViewSheet.Create(doc, tb ?? ElementId.InvalidElementId);
                try { sheet.Name = name; } catch { }
                return sheet;
            }
            catch (Exception ex) { StingLog.Warn($"Sustain CreateSheet: {ex.Message}"); return null; }
        }

        /// <summary>Feed the BEP: append a deliverable record to the project's
        /// sustainability deliverables log (a real artefact the BEP register reads).</summary>
        private static int FeedBep(Document doc, SustainabilityRunResult res, SchemeResult edge, string summary)
        {
            try
            {
                string dir = SustainabilityRegistries.ProjectDir(doc);
                if (string.IsNullOrEmpty(dir)) return 0;
                string folder = Path.Combine(dir, "_BIM_COORD", "sustainability");
                Directory.CreateDirectory(folder);
                var rec = new
                {
                    deliverable = "EDGE/LEED Sustainability Summary",
                    edgeLevel = edge?.AchievedLevel ?? "None",
                    ready = res.Readiness?.Ready ?? false,
                    energyEui = Math.Round(res.Energy?.DesignEuiKwhM2Yr ?? 0, 1),
                    embodiedCarbonKgM2 = Math.Round(res.Materials?.CarbonIntensityKgM2 ?? 0, 1),
                    wholeLifeKgM2 = Math.Round(res.WholeLife?.WholeLifeKgM2 ?? 0, 0)
                };
                File.AppendAllText(Path.Combine(folder, "edge_deliverables.jsonl"),
                    Newtonsoft.Json.JsonConvert.SerializeObject(rec) + Environment.NewLine);
                return 1;
            }
            catch (Exception ex) { StingLog.Warn($"Sustain FeedBep: {ex.Message}"); return 0; }
        }
    }

    // ── Sustain_TargetSeeker — least-cost measure set to hit the EDGE level (WS I14) ─
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SustainTargetSeekerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var doc = SustainCmdHelper.Doc(cmd);
            if (doc == null) { TaskDialog.Show("STING Sustainability", "No document open."); return Result.Failed; }

            var setup = SustainCmdHelper.EffectiveSetup(doc);
            var res = SustainabilityEngine.Run(doc, setup);
            var edge = res.Schemes.FirstOrDefault(s => s.SchemeId == "EDGE");
            if (edge == null) { TaskDialog.Show("STING Sustainability", "No EDGE scheme selected in Setup."); return Result.Cancelled; }

            // Per-gate targets from the selected EDGE level + the design's current %.
            var targets = new List<GateTarget>();
            foreach (var g in edge.Gates)
            {
                string gate = GateOf(g.Metric);
                if (gate == null || g.Threshold <= 0) continue;
                double current = gate == "energy" ? (res.Energy?.EnergySavingsPct ?? 0)
                              : gate == "water" ? (res.Water?.WaterSavingsInclAltPct ?? 0)
                              : (res.Materials?.EmbodiedEnergySavingsPct ?? 0);
                targets.Add(new GateTarget { Gate = gate, TargetPct = g.Threshold, CurrentPct = current });
            }

            // Candidate measures from the registry, sized for capex.
            var ctx = new MeasureQuantityContext
            {
                PvKwp = setup.Supply?.PvKwp ?? 0,
                FloorAreaM2 = setup.TotalFloorAreaM2 > 0 ? setup.TotalFloorAreaM2 : (res.Energy?.FloorAreaM2 ?? 0),
                Occupancy = setup.TotalOccupancy,
                GlazingAreaM2 = 0, FixtureCount = 0, CoolingKw = 0
            };
            var candidates = new List<OptimizerMeasure>();
            foreach (var m in SustainabilityRegistries.Measures(doc).All)
            {
                double gain = (m.Savings?.Kind ?? "").ToLowerInvariant().Contains("pct") ? m.Savings.Value : 0;
                if (gain <= 0) continue;
                candidates.Add(new OptimizerMeasure
                {
                    Id = m.Id, Name = m.Name, Gate = m.Gate, GainPct = gain,
                    Capex = SustainMeasureCapex.Compute(m, ctx).Capex
                });
            }

            var opt = SustainMeasureOptimizer.Reach(candidates, targets);

            var b = new StingResultPanel.Builder()
                .SetTitle("STING Sustainability — Measure Target-Seeker")
                .SetSubtitle($"Least-cost route to EDGE {edge.TargetLevel} · {(opt.AllGatesMet ? "all gates reachable" : "gaps remain")}");
            if (opt.Selected.Count > 0)
                b.AddSection("Recommended measures")
                 .Table(new[] { "Measure", "Gate", "%-gain", "Capex" },
                    opt.Selected.Select(s => new[] { s.Name, s.Gate, $"{s.GainPct:0.#}%", $"{s.Capex:N0}" }).ToList());
            var gates = b.AddSection("Per-gate result");
            foreach (var t in targets)
            {
                double ach = opt.AchievedByGate.TryGetValue(t.Gate, out var a) ? a : t.CurrentPct;
                double gap = opt.ResidualGapByGate.TryGetValue(t.Gate, out var r) ? r : 0;
                bool met = opt.GateMet.TryGetValue(t.Gate, out var mt) && mt;
                string v = $"{ach:0.#}% / target {t.TargetPct:0.#}%" + (gap > 0 ? $" — gap {gap:0.#}%" : "");
                if (met) gates.MetricHighlight(t.Gate, v, "reachable"); else gates.MetricWarn(t.Gate, v, "measures fall short");
            }
            b.AddSection("Totals").Metric("Total measure capex", $"{opt.TotalCapex:N0}");
            if (candidates.Count == 0)
                b.AddSection("Note").Info("No %-gain measures in the registry — add measures with a *Pct savings model to STING_GREEN_MEASURES.json.");
            b.Show();

            StingLog.Info($"Sustain_TargetSeeker: {opt.Selected.Count} measures, capex {opt.TotalCapex:0}, allGatesMet={opt.AllGatesMet}.");
            return Result.Succeeded;
        }

        private static string GateOf(string metric)
        {
            switch ((metric ?? "").Trim().ToLowerInvariant())
            {
                case "energy_savings_pct":          return "energy";
                case "water_savings_pct":           return "water";
                case "embodied_energy_savings_pct": return "materials";
                default:                            return null;
            }
        }
    }

    // ── Sustain_CompareOptions ───────────────────────────────────────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SustainCompareOptionsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var doc = SustainCmdHelper.Doc(cmd);
            if (doc == null) { TaskDialog.Show("STING Sustainability", "No document open."); return Result.Failed; }

            List<OptionRollupRow> rows;
            try { rows = OptionCostCarbonCalculator.Build(doc); }
            catch (Exception ex) { StingLog.Warn($"Sustain CompareOptions build: {ex.Message}"); rows = new List<OptionRollupRow>(); }

            if (rows == null || rows.Count == 0)
            {
                TaskDialog.Show("STING Sustainability",
                    "No Revit Design Options found — add design options (different fabric/structure) to compare their carbon.");
                return Result.Cancelled;
            }

            var metrics = rows.Select(r => new OptionMetric
            {
                Set = r.SetName, Option = r.OptionName, IsPrimary = r.IsPrimary,
                TotalCarbonKg = r.TotalCarbonKg, AreaM2 = r.TotalAreaM2
            });
            var cmp = SustainOptionComparison.ByCarbon(metrics);

            var table = cmp.Ranked.Select(o => new[]
            {
                $"{o.Set} / {o.Option}{(o.IsPrimary ? " (primary)" : "")}",
                $"{o.TotalCarbonKg:N0}",
                $"{o.CarbonIntensityKgM2:0.0}",
                cmp.Greenest != null && ReferenceEquals(o, cmp.Greenest) ? "← greenest" : ""
            }).ToList();

            var b = new StingResultPanel.Builder()
                .SetTitle("STING Sustainability — Design Option Comparison")
                .SetSubtitle("Embodied carbon per Design Option (reuses the BOQ option carbon calculator)");
            b.AddSection("Per-option embodied carbon")
             .Table(new[] { "Set / Option", "Total kgCO2e", "kgCO2e/m²", "" }, table);
            if (cmp.Greenest != null)
            {
                string vs = cmp.Primary != null && !ReferenceEquals(cmp.Primary, cmp.Greenest)
                    ? $" ({cmp.Greenest.CarbonIntensityKgM2:0.0} vs primary {cmp.Primary.CarbonIntensityKgM2:0.0} kgCO2e/m²)"
                    : "";
                b.AddSection("Greenest option")
                 .MetricHighlight($"{cmp.Greenest.Set} / {cmp.Greenest.Option}", $"{cmp.Greenest.CarbonIntensityKgM2:0.0} kgCO2e/m²", "lowest embodied carbon" + vs);
            }
            b.AddSection("Note").Info("Energy EUI + water are whole-building (run the dashboard); design options " +
                "typically vary fabric/structure, so embodied carbon is the differentiator compared here.");
            b.Show();

            StingLog.Info($"Sustain_CompareOptions: {rows.Count} option row(s), greenest {cmp.Greenest?.Option ?? "—"}.");
            return Result.Succeeded;
        }
    }
}
