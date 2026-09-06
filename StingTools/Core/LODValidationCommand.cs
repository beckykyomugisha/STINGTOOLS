// ─────────────────────────────────────────────────────────────────────────────
// Phase 108m — LOD validation.  Phase 195 — delegated to the one LOD engine.
//
// This command used to run a SECOND, competing LOD system: it scored elements
// with its own parameter-presence heuristic against RIBA-stage minimums in
// LOD_REQUIREMENTS.json, while Commands/Validation/LodVerifyCommand scored the
// same elements against deliverable milestones in STING_LOD_MATRIX.json. Two
// matrices, two scoring rules, two answers to "is this model at LOD 300?".
//
// The button ("LOD Check", StingDockPanel.xaml → Tag="LODValidation") is live,
// so the command stays — but the LOD judgement now comes from
// LodVerificationEngine, the single source of truth. LOD_REQUIREMENTS.json and
// the ScoreElementLOD heuristic are gone.
//
// What this command still owns that LOD_Verify does not: the STING_LOD_*_VISIBLE
// switch audit. That is a family-visibility concern (the Automation Presentation
// Pack writes those three type parameters), not a maturity concern, so it has no
// place in the matrix and is kept here.
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Commands.Validation;
using StingTools.Core.Validation;
using StingTools.UI;

namespace StingTools.Core
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LODValidationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;
                var doc = ctx.Doc;

                var matrix = LodMatrixRegistry.Get(doc);
                if (matrix.Milestones == null || matrix.Milestones.Count == 0)
                {
                    TaskDialog.Show("LOD Check",
                        "No LOD matrix found.\n\nShip STING_LOD_MATRIX.json in data/ or add a project " +
                        "overlay at <project>/_BIM_COORD/lod_matrix.json.");
                    return Result.Succeeded;
                }

                var ms = LodScope.PickMilestone(doc, "Check");
                if (ms == null) return Result.Cancelled;

                var scope = LodScope.Collect(ctx.UIDoc, doc, out var scopeReport);
                var r = LodVerificationEngine.Verify(doc, ms.Id, scope);

                // Type-level pass auditing the LOD visibility switches the Automation
                // Presentation Pack injects. Reported once per family type because
                // InjectAutomationPresentationPack writes the three params at type level.
                int switchBearingTypes = 0, switchMismatchTypes = 0, switchAllOff = 0;
                var switchIssues = new List<string>();
                foreach (var t in new FilteredElementCollector(doc).WhereElementIsElementType())
                {
                    int? c = ReadLodSwitch(t, "STING_LOD_COARSE_VISIBLE");
                    int? m = ReadLodSwitch(t, "STING_LOD_MEDIUM_VISIBLE");
                    int? f = ReadLodSwitch(t, "STING_LOD_FINE_VISIBLE");
                    if (c == null && m == null && f == null) continue;
                    switchBearingTypes++;

                    if ((c ?? 0) == 0 && (m ?? 0) == 0 && (f ?? 0) == 0)
                    {
                        switchAllOff++;
                        if (switchIssues.Count < 10)
                            switchIssues.Add($"• {t.Category?.Name} type '{t.Name}' [{t.Id}] — all LOD switches OFF, type is invisible at every detail level");
                    }
                    else if (c == null || m == null || f == null)
                    {
                        switchMismatchTypes++;
                        if (switchIssues.Count < 10)
                            switchIssues.Add($"• {t.Category?.Name} type '{t.Name}' [{t.Id}] — partial LOD-switch set (coarse={FmtBool(c)} medium={FmtBool(m)} fine={FmtBool(f)})");
                    }
                }

                var rp = StingResultPanel.Create("LOD Check")
                    .SetSubtitle($"{r.MilestoneName} → LOD {r.Lod}   ({scopeReport.Label})")
                    .AddSection("COVERAGE")
                    .Metric("Passed", r.Passed.ToString())
                    .Metric("Failed", r.Failed.ToString())
                    .Metric("Pass rate", $"{r.OverallPct:F1}%");

                rp.AddSection("SCOPE");
                foreach (var line in scopeReport.DisclosureLines()) rp.Text(line);

                rp.Text("Parameter / naming / geometry-presence maturity proxy — not a geometric");
                rp.Text("survey. STING does not verify dimensional accuracy.");
                if (r.ClashCheckRequestedButNotVerifiable)
                    rp.Text("A rule requested clash verification — not API-verifiable here; run the clash kernel separately.");

                if (r.ByDiscipline.Count > 0)
                {
                    rp.AddSection("BY DISCIPLINE");
                    foreach (var kv in r.ByDiscipline.OrderBy(k => k.Key))
                        rp.Metric(kv.Key, $"{kv.Value.pass}/{kv.Value.total}");
                }

                var worst = r.ByCategory.Where(kv => kv.Value.pass < kv.Value.total)
                                        .OrderBy(kv => kv.Value.total > 0 ? (double)kv.Value.pass / kv.Value.total : 1.0)
                                        .ToList();
                if (worst.Count > 0)
                {
                    rp.AddSection("FAILURES BY CATEGORY (worst first)");
                    foreach (var kv in worst.Take(15))
                        rp.Metric(kv.Key, $"{kv.Value.total - kv.Value.pass} of {kv.Value.total}");
                }

                var fails = r.Elements.Where(e => !e.Pass).Take(20).ToList();
                if (fails.Count > 0)
                {
                    rp.AddSection("FAILURES (first 20)");
                    foreach (var f in fails)
                        rp.Text($"• {f.Category} [{f.ElementId}] — {string.Join("; ", f.Reasons)}");
                }

                if (switchBearingTypes > 0)
                {
                    rp.AddSection("LOD SWITCHES (STING_LOD_*_VISIBLE)")
                      .Metric("Types carrying switches", switchBearingTypes.ToString())
                      .Metric("All-off (invisible)", switchAllOff.ToString())
                      .Metric("Partial (incomplete set)", switchMismatchTypes.ToString());
                    foreach (var msg in switchIssues) rp.Text(msg);
                }

                rp.Show();
                StingLog.Info($"LODValidation: {r.MilestoneId} {r.Passed}/{r.Total} pass ({scopeReport.Label}), " +
                              $"{switchBearingTypes} switch-bearing type(s)");
                return r.Failed == 0 ? Result.Succeeded : Result.Failed;
            }
            catch (Exception ex) { StingLog.Error("LODValidationCommand", ex); message = ex.Message; return Result.Failed; }
        }

        /// <summary>
        /// Reads one of the STING_LOD_*_VISIBLE YesNo type parameters.
        /// Returns null when the parameter is absent (family was never processed
        /// by InjectAutomationPresentationPack), 0/1 otherwise.
        /// </summary>
        private static int? ReadLodSwitch(Element type, string paramName)
        {
            if (type == null) return null;
            try
            {
                var p = type.LookupParameter(paramName);
                if (p == null) return null;
                if (p.StorageType == StorageType.Integer) return p.AsInteger() == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LODValidationCommand.ReadLodSwitch({paramName}) {type?.Id}: {ex.Message}");
            }
            return null;
        }

        private static string FmtBool(int? v) => v == null ? "—" : (v.Value == 0 ? "off" : "on");
    }
}
