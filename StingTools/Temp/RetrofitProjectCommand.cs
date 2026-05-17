using System;
using System.Diagnostics;
using System.Linq;          // CS1061 — Cast<T> + Count<T> extension methods
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Temp
{
    // ════════════════════════════════════════════════════════════════════════════
    //  RETROFIT PROJECT COMMAND
    //
    //  Chained wizard that converts an existing non-STING Revit project into a
    //  STING-aligned project without the fresh-project assumptions that
    //  MasterSetupCommand makes. Each phase commits independently so the user
    //  can stop between phases and resume later — unlike MasterSetup which
    //  bails the whole workflow on a critical failure.
    //
    //  Phases (A → D, ordered by coverage vs. risk):
    //    A. Shared parameters    — LoadSharedParamsCommand
    //    B. Loaded families      — ConfigureLoadedFamiliesCommand (scope prompt, purge prompt)
    //    C. Placed elements      — BatchTagCommand (populates tokens + writes tag containers)
    //    D. Visual standards     — filters, worksets, templates, VG (from MasterSetup phases 9-16)
    //
    //  A is safe on any project. B opens every loaded family in-memory and
    //  re-injects STING params; takes minutes on a large model. C touches every
    //  placed element, so is rolled up into its own TransactionGroup by
    //  BatchTagCommand. D is cosmetic — skip-if-exists.
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Retrofit an existing non-STING Revit project to the STING schema.
    /// Presents four phases (shared params / loaded families / placed elements / visual standards)
    /// each of which the user can run, skip, or stop after. Safer than <see cref="MasterSetupCommand"/>
    /// on live projects because each phase is independently committed.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RetrofitProjectCommand : IExternalCommand
    {
        private enum PhaseChoice { Run, Skip, Stop }

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.Doc == null || ctx.Doc.IsFamilyDocument)
            {
                TaskDialog.Show("STING — Retrofit Project",
                    "Open a Revit project (.rvt) first. Retrofit cannot run inside the Family Editor.");
                return Result.Failed;
            }

            // Dry-run summary so the user sees roughly what each phase will touch
            // before committing.
            var doc = ctx.Doc;
            var countFams = CountEditableFamilies(doc);
            var countElts = CountTaggableElements(doc);

            var intro = new TaskDialog("STING — Retrofit Project");
            intro.MainInstruction = $"Retrofit '{doc.Title}' to the STING schema?";
            intro.MainContent =
                "Four phases — you choose Run / Skip / Stop at each. Each phase commits " +
                "independently, so stopping mid-way leaves the completed phases applied.\n\n" +
                $"A. Shared parameters   — bind the STING parameter registry to categories.\n" +
                $"B. Loaded families     — inject STING params into ~{countFams} editable families.\n" +
                $"C. Placed elements     — populate tokens + write tag containers on ~{countElts} elements.\n" +
                $"D. Visual standards    — filters, worksets, view templates, VG overrides.\n\n" +
                "Recommendation for a first retrofit: A + B + C. D is cosmetic and can be layered later.\n\n" +
                "Worksharing note: phases B and C require you to own every workset you intend to change. " +
                "Check out worksets or synchronize with central before starting.";
            intro.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (intro.Show() != TaskDialogResult.Ok) return Result.Cancelled;

            var report = new StringBuilder();
            report.AppendLine($"STING Retrofit — {doc.Title}");
            report.AppendLine(new string('═', 45));
            var totalSw = Stopwatch.StartNew();
            int phasesRun = 0, phasesSkipped = 0;

            // ── Phase A: Shared parameters ────────────────────────────────────
            var a = AskPhase("Phase A — Shared parameters",
                "Bind the STING parameter registry to categories via LoadSharedParams.\n\n" +
                "Safe, fast, idempotent. Re-runs are no-ops.");
            if (a == PhaseChoice.Stop) return FinishEarly(report, totalSw, phasesRun, phasesSkipped);
            if (a == PhaseChoice.Run)
            {
                RunPhase(report, "Phase A — Shared parameters", ref phasesRun,
                    () => RunCommand(new Tags.LoadSharedParamsCommand(), commandData, elements));
            }
            else phasesSkipped++;

            // ── Phase B: Loaded families ──────────────────────────────────────
            var b = AskPhase("Phase B — Loaded families",
                $"Open every editable family in-memory and inject STING params. " +
                $"Approximately {countFams} families.\n\n" +
                "You'll be prompted for scope (mapped vs. all) and purge mode inside the command. " +
                "This is the longest phase — expect 5-30 minutes depending on family count.");
            if (b == PhaseChoice.Stop) return FinishEarly(report, totalSw, phasesRun, phasesSkipped);
            if (b == PhaseChoice.Run)
            {
                RunPhase(report, "Phase B — Loaded families", ref phasesRun,
                    () => RunCommand(new Tags.ConfigureLoadedFamiliesCommand(), commandData, elements));
            }
            else phasesSkipped++;

            // ── Phase C: Placed elements ──────────────────────────────────────
            var c = AskPhase("Phase C — Placed elements",
                $"Run the full tagging pipeline (TokenAutoPopulator → build tag → write containers → " +
                $"TAG7 narrative) on every taggable placed element. Approximately {countElts} elements.\n\n" +
                "Idempotent — already-populated elements are skipped.");
            if (c == PhaseChoice.Stop) return FinishEarly(report, totalSw, phasesRun, phasesSkipped);
            if (c == PhaseChoice.Run)
            {
                RunPhase(report, "Phase C — Placed elements", ref phasesRun,
                    () => RunCommand(new Tags.BatchTagCommand(), commandData, elements));
            }
            else phasesSkipped++;

            // ── Phase D: Visual standards ─────────────────────────────────────
            var d = AskPhase("Phase D — Visual standards",
                "Apply filters, worksets, view templates, and VG overrides to align visual " +
                "presentation with the STING standard.\n\n" +
                "Skippable. Existing items are left untouched.");
            if (d == PhaseChoice.Stop) return FinishEarly(report, totalSw, phasesRun, phasesSkipped);
            if (d == PhaseChoice.Run)
            {
                RunPhase(report, "Phase D.1 — View filters",       ref phasesRun,
                    () => RunCommand(new CreateFiltersCommand(), commandData, elements));
                RunPhase(report, "Phase D.2 — Worksets",           ref phasesRun,
                    () => RunCommand(new CreateWorksetsCommand(), commandData, elements));
                RunPhase(report, "Phase D.3 — View templates",     ref phasesRun,
                    () => RunCommand(new ViewTemplatesCommand(), commandData, elements));
                RunPhase(report, "Phase D.4 — Apply filters + VG", ref phasesRun,
                    () => RunCommand(new ApplyFiltersToViewsCommand(), commandData, elements));
            }
            else phasesSkipped++;

            totalSw.Stop();
            report.AppendLine(new string('─', 45));
            report.AppendLine($"Phases run:     {phasesRun}");
            report.AppendLine($"Phases skipped: {phasesSkipped}");
            report.AppendLine($"Duration:       {totalSw.Elapsed.TotalSeconds:F1}s");

            // Stamp the project so later tooling knows retrofit ran — distinct from
            // STING_MASTER_SETUP_TS so users can tell retrofit from fresh setup.
            try
            {
                using (var tx = new Transaction(doc, "STING Retrofit Timestamp"))
                {
                    tx.Start();
                    ParameterHelpers.SetString(doc.ProjectInformation, "STING_RETROFIT_TS",
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), overwrite: true);
                    tx.Commit();
                }
            }
            catch (Exception ex) { StingLog.Warn($"Retrofit timestamp: {ex.Message}"); }

            TaskDialog.Show("STING — Retrofit Project", report.ToString());
            StingLog.Info($"Retrofit complete: {phasesRun} phases run, {phasesSkipped} skipped, " +
                $"elapsed={totalSw.Elapsed.TotalSeconds:F1}s");
            return phasesRun > 0 ? Result.Succeeded : Result.Cancelled;
        }

        /// <summary>Execute an <see cref="IExternalCommand"/> without capturing <c>ref message</c> in a lambda.</summary>
        private static Result RunCommand(IExternalCommand cmd, ExternalCommandData data, ElementSet elems)
        {
            string msg = "";
            return cmd.Execute(data, ref msg, elems);
        }

        private static PhaseChoice AskPhase(string title, string content)
        {
            var dlg = new TaskDialog(title);
            dlg.MainInstruction = title;
            dlg.MainContent = content;
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Run this phase");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Skip — move to next phase");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Stop — finish retrofit here");
            dlg.CommonButtons = TaskDialogCommonButtons.None;
            var r = dlg.Show();
            if (r == TaskDialogResult.CommandLink1) return PhaseChoice.Run;
            if (r == TaskDialogResult.CommandLink2) return PhaseChoice.Skip;
            return PhaseChoice.Stop;
        }

        private static void RunPhase(StringBuilder report, string label, ref int phasesRun, Func<Result> action)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var result = action();
                sw.Stop();
                string status = result == Result.Succeeded ? "OK"
                              : result == Result.Cancelled ? "CANCELLED"
                              : "WARN";
                report.AppendLine($"  {label,-42}  {status}  ({sw.Elapsed.TotalSeconds:F1}s)");
                StingLog.Info($"Retrofit {label}: {status} ({sw.Elapsed.TotalSeconds:F1}s)");
                if (result == Result.Succeeded) phasesRun++;
            }
            catch (Exception ex)
            {
                sw.Stop();
                report.AppendLine($"  {label,-42}  FAILED — {ex.Message}");
                StingLog.Error($"Retrofit {label}", ex);
            }
        }

        private static Result FinishEarly(StringBuilder report, Stopwatch sw, int phasesRun, int phasesSkipped)
        {
            sw.Stop();
            report.AppendLine(new string('─', 45));
            report.AppendLine($"STOPPED early — {phasesRun} phase(s) run, {phasesSkipped} skipped");
            report.AppendLine($"Duration: {sw.Elapsed.TotalSeconds:F1}s");
            TaskDialog.Show("STING — Retrofit Project", report.ToString());
            return phasesRun > 0 ? Result.Succeeded : Result.Cancelled;
        }

        private static int CountEditableFamilies(Document doc)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .Count(f => f != null && f.IsEditable && !f.IsInPlace);
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; }
        }

        private static int CountTaggableElements(Document doc)
        {
            try
            {
                // Rough estimate — excludes types, uses WhereElementIsNotElementType.
                return new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WhereElementIsViewIndependent()
                    .GetElementCount();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; }
        }
    }
}
